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
        // Near-full search: small wheel steps in a tall viewport leave a huge
        // true overlap. Adjacent-frame matching plus recent-shift continuity
        // keeps repetitive rows from winning at unrelated offsets.
        private const double MaxOverlapRatio = 1.0;
        private const int StripRows = 96;
        // Broad matching stays conservative. When a recent scroll distance is
        // known, continuity lets us accept a slightly noisier but correctly
        // positioned match (dynamic cards, caret/hover, compositor changes).
        private const double MinBroadSimilarity = 0.92;
        private const double MinExpectedSimilarity = 0.87;
        private const int EndConfirmRequired = 2;
        private const int EndNoMovePixels = 8;
        private const int PrefixDriftTolerance = 96;
        private const int WHEEL_DELTA = 120;
        private const int WheelNotches = 2;

        private enum ScrollMode
        {
            Wheel,
            PageDown
        }

        // Returns the stitched bitmap. Esc finishes early and keeps the frames
        // captured so far; only the pre-scroll window picker can cancel fully.
        // Throws on capture failures so the caller can show ex.Message.
        // After return, LastReport describes frames, height, and stop reason.
        // No scroll debug files/logs are written during normal use.
        public static string LastReport = "";
        public static Bitmap Capture(IntPtr hwnd, Rectangle region)
        {
            if (hwnd == IntPtr.Zero)
                throw new ArgumentException(L10n.T("캡처할 윈도우가 올바르지 않습니다.", "The selected window is invalid."));
            if (region.Width < 50 || region.Height < 100)
                throw new ArgumentException(L10n.T("스크롤 캡처 영역이 너무 작습니다.", "The scroll capture region is too small."));

            bool lowSpec = AppSettings.LowSpecOptimization;
            // Low-spec mode intentionally trades speed for stability. Captures
            // are spaced farther apart so GPU/compositor/lazy-paint work has
            // time to settle before a frame is compared. These sleeps consume
            // virtually no CPU while waiting.
            int settleMs = lowSpec ? 1000 : 500;
            int retrySettleMs = lowSpec ? 360 : 160;
            int topSettleMs = lowSpec ? 900 : 450;
            int focusSettleMs = lowSpec ? 350 : 200;

            string stopReason = L10n.T("완료", "Done");
            int frames = 1;
            int endNoMoveCount = 0;
            System.Collections.Generic.List<int> recentShifts = new System.Collections.Generic.List<int>();
            System.Collections.Generic.List<int> recentPrefixes = new System.Collections.Generic.List<int>();
            try { Native.SetForegroundWindow(hwnd); } catch { }
            Thread.Sleep(focusSettleMs);

            // Do not merely *request* SB_TOP. Repeat a modifier-free top command
            // and compare captures; only an unchanged repeated request counts as
            // verified. Keyboard Ctrl+Home is deliberately avoided so browser
            // zoom state can never be changed by a late Ctrl-key release.
            bool topVerified = MoveToTopAndVerify(hwnd, region, topSettleMs);
            Thread.Sleep(lowSpec ? 500 : 250);

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
                    {
                        stopReason = L10n.T("중단됨", "Stopped");
                        break;
                    }

                    try { Native.SetForegroundWindow(hwnd); } catch { }
                    Application.DoEvents();
                    SendScroll(hwnd, region, mode);
                    Thread.Sleep(settleMs + (mode == ScrollMode.PageDown ? 300 : 0));
                    Application.DoEvents();

                    if (IsEscPressed() || !Native.IsWindow(hwnd))
                    {
                        stopReason = L10n.T("중단됨", "Stopped");
                        break;
                    }

                    Bitmap frame = null;
                    int overlap = -1;
                    int prefix = 0;
                    double best = 0.0;
                    int bestH = 0;
                    bool equal = false;
                    bool stable = false;
                    int expectedShift = MedianShift(recentShifts);

                    // Do not scroll again just because one captured frame was
                    // transient (smooth animation/lazy paint). Re-capture the
                    // SAME scroll position up to two times before giving up.
                    for (int retry = 0; retry < 3; retry++)
                    {
                        if (frame != null) { frame.Dispose(); frame = null; }
                        if (retry > 0)
                        {
                            Thread.Sleep(retrySettleMs);
                            Application.DoEvents();
                        }

                        frame = CaptureService.CaptureRectangle(region);

                        equal = FramesEqual(previous, frame);
                        best = 0.0;
                        bestH = 0;
                        overlap = -1;
                        prefix = 0;
                        if (!equal)
                        {
                            // Always try overlap before the tolerant stationary
                            // test. Otherwise a real final movement of only
                            // 20~60 px could look "mostly unchanged" and its
                            // last content strip would be discarded.
                            int rawPrefix = StaticPrefix(previous, frame);
                            prefix = StabilizePrefix(rawPrefix, recentPrefixes);
                            overlap = FindOverlap(previous, frame, prefix, expectedShift, out best, out bestH);
                        }
                        stable = equal || (overlap < MinOverlap && FramesStable(previous, frame));

                        if (stable || overlap >= MinOverlap)
                            break;
                    }

                    if (stable)
                    {
                        frame.Dispose();
                        endNoMoveCount++;
                        if (endNoMoveCount >= EndConfirmRequired)
                        {
                            stopReason = L10n.T("끝까지 도달", "End reached");
                            break;
                        }

                        // A wheel message can be ignored by some controls. The
                        // second no-movement probe is therefore PageDown. End is
                        // confirmed only when two independent downward requests
                        // produce no meaningful visual movement.
                        mode = ScrollMode.PageDown;
                        continue;
                    }

                    if (overlap < MinOverlap)
                    {
                        frame.Dispose();
                        stopReason = L10n.T("이어붙이기 실패", "Stitch failed");
                        break;
                    }

                    if (prefix > 0)
                    {
                        recentPrefixes.Add(prefix);
                        if (recentPrefixes.Count > 5) recentPrefixes.RemoveAt(0);
                    }

                    // With adjacent-frame matching, added pixels are exactly the
                    // scroll delta. Static header + overlap are already present
                    // in the accumulator and MUST NOT be drawn again.
                    int added = frame.Height - prefix - overlap;
                    if (added <= EndNoMovePixels)
                    {
                        // Treat a zero/tiny measured shift as a no-movement
                        // probe, but do not stop on the first one. This is more
                        // robust than relying on exact identical screenshots.
                        frame.Dispose();
                        endNoMoveCount++;
                        if (endNoMoveCount >= EndConfirmRequired)
                        {
                            stopReason = L10n.T("끝까지 도달", "End reached");
                            break;
                        }
                        mode = ScrollMode.PageDown;
                        continue;
                    }

                    // Any real movement resets EOF confirmation. A small final
                    // movement (for example 20~60 px) is appended normally; the
                    // following two zero-movement probes will then close cleanly.
                    endNoMoveCount = 0;

                    int newHeight = accumulator.Height + added;
                    if (newHeight > MaxTotalHeight)
                    {
                        frame.Dispose();
                        stopReason = L10n.T("최대 크기 도달", "Max size reached");
                        break;
                    }

                    Bitmap stitched = new Bitmap(accumulator.Width, newHeight, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(stitched))
                    {
                        g.DrawImageUnscaled(accumulator, 0, 0);
                        Rectangle src = new Rectangle(0, frame.Height - added, frame.Width, added);
                        Rectangle dst = new Rectangle(0, accumulator.Height, frame.Width, added);
                        g.DrawImage(frame, dst, src, GraphicsUnit.Pixel);
                    }

                    accumulator.Dispose();
                    accumulator = stitched;
                    frames++;

                    // Keep a short robust history. It rejects periodic-content
                    // aliases that suddenly imply a wildly different scroll
                    // distance, but still allows a smaller final step at EOF.
                    if (added >= 64)
                    {
                        recentShifts.Add(added);
                        if (recentShifts.Count > 5) recentShifts.RemoveAt(0);
                    }

                    previous.Dispose();
                    previous = frame;
                    mode = ScrollMode.Wheel;
                }

                Bitmap result = accumulator;
                accumulator = null;
                if (frames >= MaxFrames)
                    stopReason = L10n.T("최대 장수 도달", "Max frames reached");
                LastReport = String.Format(
                    L10n.T("스크롤 {0}장 · {1}px · {2}", "Scroll {0} frames · {1}px · {2}"),
                    frames, result.Height, stopReason);
                if (!topVerified)
                    LastReport += L10n.T(" · 맨 위 확인 안됨", " · top not verified");
                return result;
            }
            finally
            {
                if (first != null) first.Dispose();
                if (previous != null) previous.Dispose();
                if (accumulator != null) accumulator.Dispose();
            }
        }

        private static int MedianShift(System.Collections.Generic.List<int> shifts)
        {
            if (shifts == null || shifts.Count == 0) return 0;
            int[] a = shifts.ToArray();
            Array.Sort(a);
            return a[a.Length / 2];
        }

        private static int StabilizePrefix(int rawPrefix, System.Collections.Generic.List<int> recentPrefixes)
        {
            if (recentPrefixes == null || recentPrefixes.Count == 0)
                return Math.Max(0, rawPrefix);

            int baseline = MedianShift(recentPrefixes);
            if (rawPrefix <= 0 || Math.Abs(rawPrefix - baseline) > PrefixDriftTolerance)
                return baseline;

            return rawPrefix;
        }

        private static bool IsEscPressed()
        {
            try { return (Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0; }
            catch { return false; }
        }

        // Requests the top repeatedly and verifies it visually. No keyboard
        // modifiers are injected: Ctrl+Home was removed because a late Ctrl-key
        // release can turn the following mouse-wheel message into browser zoom.
        // SB_TOP is tried first, then several modifier-free upward wheel messages.
        // An unchanged repeated top probe counts as verified.
        private static bool MoveToTopAndVerify(IntPtr hwnd, Rectangle region, int settleMs)
        {
            Bitmap previousProbe = null;
            Bitmap currentProbe = null;
            int stableCount = 0;
            try
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    if (IsEscPressed() || !Native.IsWindow(hwnd)) return false;
                    try { Native.SetForegroundWindow(hwnd); } catch { }
                    SendTopCommand(hwnd, region);
                    Thread.Sleep(settleMs);
                    Application.DoEvents();

                    currentProbe = CaptureService.CaptureRectangle(region);
                    if (previousProbe != null)
                    {
                        bool stable = FramesStable(previousProbe, currentProbe);
                        stableCount = stable ? stableCount + 1 : 0;
                        if (stableCount >= 1) return true;
                        previousProbe.Dispose();
                    }
                    previousProbe = currentProbe;
                    currentProbe = null;
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (currentProbe != null) currentProbe.Dispose();
                if (previousProbe != null) previousProbe.Dispose();
            }
        }

        private static void SendTopCommand(IntPtr hwnd, Rectangle region)
        {
            IntPtr target = hwnd;
            try
            {
                target = ResolveScrollTarget(hwnd, region);
                Native.SendMessage(target, Native.WM_VSCROLL, new IntPtr(Native.SB_TOP), IntPtr.Zero);
            }
            catch { }

            // Browser/Electron/custom controls may ignore SB_TOP. Send several
            // ordinary upward wheel messages with NO Ctrl/Shift/Alt modifiers.
            // Small repeated wheel messages are accepted more consistently than
            // one abnormally huge delta and cannot alter browser zoom.
            try
            {
                int x = region.Left + region.Width / 2;
                int y = region.Top + region.Height / 2;
                IntPtr lParam = new IntPtr((y << 16) | (x & 0xFFFF));
                for (int i = 0; i < 8; i++)
                {
                    int delta = WHEEL_DELTA * 4;
                    IntPtr wParam = new IntPtr((delta << 16) & 0xFFFFFFFF);
                    Native.SendMessage(target, Native.WM_MOUSEWHEEL, wParam, lParam);
                }
            }
            catch { }
        }

        private static void SendScroll(IntPtr hwnd, Rectangle region, ScrollMode mode)
        {
            try
            {
                IntPtr target = ResolveScrollTarget(hwnd, region);
                if (mode == ScrollMode.Wheel)
                {
                    int x = region.Left + region.Width / 2;
                    int y = region.Top + region.Height / 2;
                    IntPtr wParam = new IntPtr((-(WHEEL_DELTA * WheelNotches) << 16) & 0xFFFFFFFF);
                    IntPtr lParam = new IntPtr((y << 16) | (x & 0xFFFF));
                    Native.SendMessage(target, Native.WM_MOUSEWHEEL, wParam, lParam);
                }
                else
                {
                    Native.SendMessage(target, Native.WM_VSCROLL, new IntPtr(Native.SB_PAGEDOWN), IntPtr.Zero);
                }
            }
            catch { }
        }

        private static IntPtr ResolveScrollTarget(IntPtr fallback, Rectangle region)
        {
            try
            {
                Native.POINT pt = new Native.POINT();
                pt.X = region.Left + region.Width / 2;
                pt.Y = region.Top + region.Height / 2;
                IntPtr child = Native.WindowFromPoint(pt);
                if (child != IntPtr.Zero && Native.IsWindow(child)) return child;
            }
            catch { }
            return fallback;
        }

        // More tolerant no-movement check used for top/end verification. It
        // samples the image and accepts small dynamic changes (caret, clock,
        // hover/animation) while still rejecting a real scroll displacement.
        private static bool FramesStable(Bitmap a, Bitmap b)
        {
            if (a == null || b == null) return false;
            if (a.Width != b.Width || a.Height != b.Height) return false;

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

                int xStep = a.Width > 2000 ? 12 : 8;
                int yStep = a.Height > 1200 ? 6 : 4;
                int x0 = Math.Max(0, a.Width / 20);
                int x1 = Math.Min(a.Width, a.Width - x0);
                int compared = 0;
                int diff = 0;
                for (int y = 0; y < a.Height; y += yStep)
                {
                    int ia = y * stride;
                    int ib = y * stride;
                    for (int x = x0; x < x1; x += xStep)
                    {
                        compared++;
                        if (!PixelsNear(pa[ia + x], pb[ib + x])) diff++;
                    }
                }
                // Up to ~2% sampled change is treated as stationary. A normal
                // 300~500 px scroll changes far more than this.
                return compared > 0 && diff * 100 <= compared * 2;
            }
            catch { return false; }
            finally
            {
                try { if (da != null) a.UnlockBits(da); } catch { }
                try { if (db != null) b.UnlockBits(db); } catch { }
            }
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

        // Finds overlap between ADJACENT frames, never against the growing
        // accumulator. This keeps matching memory bounded and makes the added
        // height equal to the actual scroll delta.
        private static int FindOverlap(Bitmap previous, Bitmap frame, int frameTop, int expectedShift,
            out double bestRatio, out int bestH)
        {
            bestRatio = 0.0;
            bestH = 0;
            if (previous == null || frame == null) return -1;
            if (previous.Width != frame.Width || previous.Height != frame.Height) return -1;
            if (frameTop < 0) frameTop = 0;

            int usable = frame.Height - frameTop;
            int maxOverlap = Math.Min((int)(usable * MaxOverlapRatio), previous.Height);
            if (maxOverlap < MinOverlap) return -1;

            BitmapData da = null;
            BitmapData db = null;
            try
            {
                Rectangle rc = new Rectangle(0, 0, frame.Width, frame.Height);
                da = previous.LockBits(rc, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                db = frame.LockBits(rc, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                int stride = Math.Abs(da.Stride) / 4;
                int[] pa = new int[stride * previous.Height];
                int[] pb = new int[stride * frame.Height];
                Marshal.Copy(da.Scan0, pa, 0, pa.Length);
                Marshal.Copy(db.Scan0, pb, 0, pb.Length);

                int w = frame.Width;

                // Once a few normal steps are known, search near their median
                // first. Repetitive rows may match at many offsets; continuity
                // is a much safer tie-breaker than rejecting the strongest seam.
                if (expectedShift > 0)
                {
                    int radius = Math.Min(320, Math.Max(160, usable / 4));
                    int lo = Math.Max(1, expectedShift - radius);
                    int hi = Math.Min(usable - MinOverlap, expectedShift + radius);
                    int chosenH = -1;
                    double chosenScore = -1.0;
                    double chosenRatio = -1.0;
                    for (int shift = lo; shift <= hi; shift++)
                    {
                        int h = usable - shift;
                        if (h < MinOverlap || h > maxOverlap) continue;

                        // Always measure the complete bounded sample. The old
                        // implementation rejected a candidate as soon as an
                        // early sample exceeded the mismatch limit. A single
                        // dynamic card/line near the seam could therefore reject
                        // the true ~400 px shift even though the remaining overlap
                        // matched almost perfectly.
                        double ratio;
                        RowsMatch(pa, stride, previous.Height - h, pb, stride, frameTop, w, h, out ratio);
                        if (ratio > bestRatio) { bestRatio = ratio; bestH = h; }

                        if (ratio < MinExpectedSimilarity) continue;

                        double continuityPenalty = Math.Abs(shift - expectedShift) /
                            (double)Math.Max(1, usable) * 0.10;
                        double score = ratio - continuityPenalty;
                        if (score > chosenScore)
                        {
                            chosenScore = score;
                            chosenH = h;
                            chosenRatio = ratio;
                        }
                    }
                    if (chosenH >= MinOverlap && chosenRatio >= MinExpectedSimilarity)
                    {
                        bestRatio = chosenRatio;
                        bestH = chosenH;
                        return chosenH;
                    }
                }

                // Broad fallback. Largest valid overlap wins, which naturally
                // favors the smallest plausible scroll delta on the first step.
                for (int h = maxOverlap; h >= MinOverlap; h--)
                {
                    double ratio;
                    if (RowsMatch(pa, stride, previous.Height - h, pb, stride, frameTop, w, h, out ratio))
                        return h;
                    if (ratio > bestRatio) { bestRatio = ratio; bestH = h; }
                }
                return -1;
            }
            catch { return -1; }
            finally
            {
                try { if (da != null) previous.UnlockBits(da); } catch { }
                try { if (db != null) frame.UnlockBits(db); } catch { }
            }
        }

        // Counts static rows at the top shared by two ADJACENT frames at the
        // same position (sticky headers, toolbars). Capped well below full
        // height so a nearly-finished page cannot erase the match area.
        private static int StaticPrefix(Bitmap prev, Bitmap frame)
        {
            if (prev == null || frame == null) return 0;
            if (prev.Width != frame.Width || prev.Height != frame.Height) return 0;

            int cap = frame.Height * 2 / 5;
            BitmapData da = null;
            BitmapData db = null;
            try
            {
                Rectangle rc = new Rectangle(0, 0, frame.Width, frame.Height);
                da = prev.LockBits(rc, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                db = frame.LockBits(rc, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                int stride = Math.Abs(da.Stride) / 4;
                int[] pa = new int[stride * frame.Height];
                int[] pb = new int[stride * frame.Height];
                Marshal.Copy(da.Scan0, pa, 0, pa.Length);
                Marshal.Copy(db.Scan0, pb, 0, pb.Length);

                int w = frame.Width;
                int prefix = 0;
                for (int y = 0; y < cap; y++)
                {
                    int ia = y * stride;
                    int ib = y * stride;
                    int compared = 0;
                    int diff = 0;
                    for (int x = 0; x < w; x += 4)
                    {
                        compared++;
                        if (pa[ia + x] != pb[ib + x] && ++diff * 50 > compared)
                            return prefix; // > ~2% differing: scrolled content.
                    }
                    prefix++;
                }
                return prefix;
            }
            catch { return 0; }
            finally
            {
                try { if (da != null) prev.UnlockBits(da); } catch { }
                try { if (db != null) frame.UnlockBits(db); } catch { }
            }
        }

        private static bool RowsMatch(int[] pa, int strideA, int startA, int[] pb, int strideB, int startB,
            int width, int height, out double ratio)
        {
            // Sample a bounded number of rows/columns. Small RGB differences
            // from ClearType, fractional DPI and compositor timing are treated
            // as the same visual pixel instead of demanding exact ARGB equality.
            int rows = Math.Min(height, StripRows);
            int rowStep = Math.Max(1, height / Math.Max(1, rows));
            int xStep = width > 2000 ? 8 : (width > 1200 ? 6 : 4);
            int compared = 0;
            int diff = 0;

            for (int y = 0; y < height; y += rowStep)
            {
                int ia = (startA + y) * strideA;
                int ib = (startB + y) * strideB;
                for (int x = 0; x < width; x += xStep)
                {
                    compared++;
                    if (!PixelsNear(pa[ia + x], pb[ib + x]))
                        diff++;
                }
            }

            // Important: decide only AFTER the bounded sample is complete.
            // Early rejection made the result depend on which rows happened to
            // be sampled first and caused false failures on otherwise excellent
            // overlaps (observed: expected ~400 px shift, full match ~0.97).
            ratio = compared > 0 ? 1.0 - (double)diff / compared : 0.0;
            return compared > 0 && ratio >= MinBroadSimilarity;
        }

        private static bool PixelsNear(int a, int b)
        {
            if (a == b) return true;
            int ar = (a >> 16) & 255, ag = (a >> 8) & 255, ab = a & 255;
            int br = (b >> 16) & 255, bg = (b >> 8) & 255, bb = b & 255;
            int dr = Math.Abs(ar - br), dg = Math.Abs(ag - bg), db = Math.Abs(ab - bb);
            if (dr <= 12 && dg <= 12 && db <= 12) return true;
            int la = (ar * 77 + ag * 150 + ab * 29) >> 8;
            int lb = (br * 77 + bg * 150 + bb * 29) >> 8;
            return Math.Abs(la - lb) <= 8 && Math.Max(dr, Math.Max(dg, db)) <= 20;
        }

    }
}
