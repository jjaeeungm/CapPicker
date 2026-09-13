using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CapPicker
{
    // v2.1 scroll capture: pick a window, auto-scroll it, stitch frames by
    // visual overlap, and hand the result to the existing Editor pipeline.
    //
    // Lightness rules: no new dependencies, incremental stitching (peak memory
    // is two frames plus the result), LockBits integer math for matching,
    // vertical scrolling only.
    internal static class ScrollCapture
    {
        private const int MaxFrames = 50;
        private const int MaxTotalHeight = 16000;
        private const int MinOverlap = 16;
        private const double MaxOverlapRatio = 0.6;
        private const int StripRows = 256;

        private const int WHEEL_DELTA = 120;
        private const int WheelNotches = 3;

        private enum ScrollMode
        {
            Wheel,
            PageDown
        }

        // Returns the stitched bitmap. Esc finishes early and keeps the frames
        // captured so far; only the pre-scroll window picker can cancel fully.
        // Throws on capture failures so the caller can show ex.Message.
        public static Bitmap Capture(IntPtr hwnd, Rectangle region)
        {
            if (hwnd == IntPtr.Zero)
                throw new ArgumentException(L10n.T("캡처할 윈도우가 올바르지 않습니다.", "The selected window is invalid."));
            if (region.Width < 50 || region.Height < 100)
                throw new ArgumentException(L10n.T("스크롤 캡처 영역이 너무 작습니다.", "The scroll capture region is too small."));

            int settleMs = AppSettings.LowSpecOptimization ? 600 : 350;

            try { Native.SetForegroundWindow(hwnd); } catch { }
            Thread.Sleep(200);

            Bitmap first = CaptureService.CaptureRectangle(region);
            Bitmap accumulator = null;
            Bitmap previous = null;
            try
            {
                accumulator = (Bitmap)first.Clone();
                previous = first;
                first = null;

                ScrollMode mode = ScrollMode.Wheel;

                for (int i = 1; i < MaxFrames; i++)
                {
                    if (IsEscPressed() || !Native.IsWindow(hwnd))
                        break;
                    Application.DoEvents();

                    SendScroll(hwnd, region, mode);
                    Thread.Sleep(settleMs);
                    Application.DoEvents();

                    if (IsEscPressed() || !Native.IsWindow(hwnd))
                        break;

                    Bitmap frame = CaptureService.CaptureRectangle(region);
                    try
                    {
                        if (FramesEqual(previous, frame))
                        {
                            if (mode == ScrollMode.Wheel)
                            {
                                // Wheel did not move the content. Fall back to
                                // PageDown once before giving up.
                                mode = ScrollMode.PageDown;
                                continue;
                            }
                            break; // No change after PageDown: reached the end.
                        }

                        int overlap = FindOverlap(accumulator, frame);
                        if (overlap < MinOverlap)
                            break; // Cannot stitch reliably: keep what we have.

                        int newHeight = accumulator.Height + frame.Height - overlap;
                        if (newHeight > MaxTotalHeight)
                            break;

                        Bitmap stitched = new Bitmap(accumulator.Width, newHeight, PixelFormat.Format32bppArgb);
                        using (Graphics g = Graphics.FromImage(stitched))
                        {
                            g.DrawImageUnscaled(accumulator, 0, 0);
                            g.DrawImageUnscaled(frame, 0, accumulator.Height - overlap);
                        }
                        accumulator.Dispose();
                        accumulator = stitched;
                    }
                    finally
                    {
                        if (previous != null) previous.Dispose();
                        previous = frame;
                    }
                }

                Bitmap result = accumulator;
                accumulator = null;
                return result;
            }
            finally
            {
                if (first != null) first.Dispose();
                if (previous != null) previous.Dispose();
                if (accumulator != null) accumulator.Dispose();
            }
        }

        private static bool IsEscPressed()
        {
            try { return (Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0; }
            catch { return false; }
        }

        private static void SendScroll(IntPtr hwnd, Rectangle region, ScrollMode mode)
        {
            try
            {
                if (mode == ScrollMode.Wheel)
                {
                    int x = region.Left + region.Width / 2;
                    int y = region.Top + region.Height / 2;
                    IntPtr wParam = new IntPtr((-(WHEEL_DELTA * WheelNotches) << 16) & 0xFFFFFFFF);
                    IntPtr lParam = new IntPtr((y << 16) | (x & 0xFFFF));
                    Native.SendMessage(hwnd, Native.WM_MOUSEWHEEL, wParam, lParam);
                }
                else
                {
                    Native.SendMessage(hwnd, Native.WM_VSCROLL, new IntPtr(Native.SB_PAGEDOWN), IntPtr.Zero);
                }
            }
            catch { }
        }

        // True when two same-size frames are visually identical within a tiny
        // tolerance (blinkers/carets must not block end-of-content detection).
        private static bool FramesEqual(Bitmap a, Bitmap b)
        {
            if (a == null || b == null) return false;
            if (a.Width != b.Width || a.Height != b.Height) return false;

            int allowed = Math.Max(64, (a.Width * a.Height) / 1000); // ~0.1%
            int diff = 0;

            BitmapData da = null;
            BitmapData db = null;
            try
            {
                Rectangle rc = new Rectangle(0, 0, a.Width, a.Height);
                da = a.LockBits(rc, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                db = b.LockBits(rc, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                int stride = Math.Abs(da.Stride) / 4;
                int[] pa = new int[stride * a.Height];
                int[] pb = new int[stride * b.Height];
                Marshal.Copy(da.Scan0, pa, 0, pa.Length);
                Marshal.Copy(db.Scan0, pb, 0, pb.Length);

                for (int i = 0; i < pa.Length; i += 2)
                {
                    if (pa[i] != pb[i] && ++diff > allowed)
                        return false;
                }
                return true;
            }
            catch { return false; }
            finally
            {
                try { if (da != null) a.UnlockBits(da); } catch { }
                try { if (db != null) b.UnlockBits(db); } catch { }
            }
        }

        // Largest overlap h (in pixels) where the bottom h rows of acc match
        // the top h rows of frame. Returns -1 when nothing reliable is found.
        // Never trusts scroll distance: purely visual, so wheel settings, DPI,
        // and per-app scroll units cannot skew the seam.
        private static int FindOverlap(Bitmap acc, Bitmap frame)
        {
            if (acc == null || frame == null) return -1;
            if (acc.Width != frame.Width) return -1;

            int maxOverlap = Math.Min((int)(frame.Height * MaxOverlapRatio), acc.Height);
            if (maxOverlap < MinOverlap) return -1;

            BitmapData da = null;
            BitmapData db = null;
            try
            {
                da = acc.LockBits(new Rectangle(0, 0, acc.Width, acc.Height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                db = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                int stride = Math.Abs(da.Stride) / 4;
                int[] pa = new int[stride * acc.Height];
                int[] pb = new int[stride * frame.Height];
                Marshal.Copy(da.Scan0, pa, 0, pa.Length);
                Marshal.Copy(db.Scan0, pb, 0, pb.Length);

                int w = acc.Width;
                // Step 1px: real scroll deltas are arbitrary (DPI, wheel
                // settings, app units), so even candidates would all miss
                // an odd offset and report no overlap. Wrong candidates
                // exit on the first rows, keeping the scan cheap.
                for (int h = maxOverlap; h >= MinOverlap; h--)
                {
                    if (RowsMatch(pa, stride, acc.Height - h, pb, stride, 0, w, h))
                        return h;
                }
                return -1;
            }
            catch { return -1; }
            finally
            {
                try { if (da != null) acc.UnlockBits(da); } catch { }
                try { if (db != null) frame.UnlockBits(db); } catch { }
            }
        }

        private static bool RowsMatch(int[] pa, int strideA, int startA, int[] pb, int strideB, int startB, int width, int height)
        {
            // Compare a bounded strip with early exit: full-height comparison
            // on every candidate would dominate the scroll settle time.
            int rows = Math.Min(height, StripRows);
            int rowStep = Math.Max(1, height / rows);
            int compared = 0;
            int diff = 0;
            for (int y = 0; y < height; y += rowStep)
            {
                int ia = (startA + y) * strideA;
                int ib = (startB + y) * strideB;
                for (int x = 0; x < width; x += 2)
                {
                    compared++;
                    if (pa[ia + x] != pb[ib + x] && ++diff * 12 > compared)
                        return false; // > ~8% differing pixels.
                }
            }
            return compared > 0;
        }
    }
}
