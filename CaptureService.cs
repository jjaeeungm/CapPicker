
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CapPicker
{
    internal static class CaptureService
    {
        public static Bitmap CaptureRectangle(Rectangle screenRect)
        {
            if (screenRect.Width < 1 || screenRect.Height < 1)
                throw new ArgumentException(L10n.T("캡처 영역이 올바르지 않습니다.", "The capture region is invalid."));

            Bitmap bmp = new Bitmap(screenRect.Width, screenRect.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(screenRect.Location, Point.Empty, screenRect.Size, CopyPixelOperation.SourceCopy);
                }
                return bmp;
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
        }

        public static Bitmap CaptureVirtualScreen()
        {
            return CaptureRectangle(SystemInformation.VirtualScreen);
        }

        // 대상 HWND 자체 렌더링을 우선 사용합니다. PrintWindow가 실패하거나
        // GPU/브라우저 계열 창에서 명백한 빈 결과를 반환하면, 현재 화면에 보이는
        // DWM 프레임 영역을 CopyFromScreen으로 캡처하는 안전한 fallback을 사용합니다.
        public static Bitmap CaptureWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                throw new ArgumentException(L10n.T("캡처할 윈도우가 올바르지 않습니다.", "The selected window is invalid."));

            Native.RECT rawWindowRect;
            if (!Native.GetWindowRect(hwnd, out rawWindowRect))
                throw new InvalidOperationException(L10n.T("윈도우 영역을 가져오지 못했습니다.", "Could not get the window bounds."));

            Rectangle windowRect = rawWindowRect.ToRectangle();
            if (windowRect.Width < 2 || windowRect.Height < 2)
                throw new InvalidOperationException(L10n.T("윈도우 크기가 올바르지 않습니다.", "The window size is invalid."));

            Rectangle visualRect = GetVisualWindowRect(hwnd, windowRect);
            Bitmap full = new Bitmap(windowRect.Width, windowRect.Height, PixelFormat.Format32bppArgb);
            bool rendered = false;

            try
            {
                using (Graphics g = Graphics.FromImage(full))
                {
                    g.Clear(Color.Transparent);
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        try { Native.DwmFlush(); } catch { }

                        // PrintWindow 기반 구현을 유지하되 최신 DWM/브라우저 창에 먼저
                        // PW_RENDERFULLCONTENT를 요청하고, 지원하지 않으면 기본 모드로 재시도합니다.
                        rendered = Native.PrintWindow(hwnd, hdc, Native.PW_RENDERFULLCONTENT);
                        if (!rendered)
                            rendered = Native.PrintWindow(hwnd, hdc, 0);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }
            }
            catch
            {
                // PrintWindow 자체가 예외를 일으켜도 화면 기반 fallback을 시도할 수 있도록
                // 여기서는 즉시 실패시키지 않습니다.
                rendered = false;
            }

            // 일부 Chromium/UWP/GPU 창은 PrintWindow가 true를 반환하면서도 전체가
            // 투명/검정인 비정상 비트맵을 돌려줍니다. 그런 경우에만 화면 fallback을 사용합니다.
            if (!rendered || IsObviouslyBlank(full))
            {
                Bitmap visible = TryCaptureVisibleWindow(visualRect);
                if (visible != null)
                {
                    full.Dispose();
                    return visible;
                }

                if (!rendered)
                {
                    full.Dispose();
                    throw new InvalidOperationException(L10n.T(
                        "이 윈도우를 직접 렌더링하거나 화면에서 캡처하지 못했습니다.",
                        "Could not capture this window by direct rendering or from the screen."));
                }
            }

            // GetWindowRect에는 투명한 resize border가 포함될 수 있으므로 DWM의
            // 실제 보이는 프레임 영역으로 결과를 잘라냅니다.
            Rectangle relative = new Rectangle(
                visualRect.Left - windowRect.Left,
                visualRect.Top - windowRect.Top,
                visualRect.Width,
                visualRect.Height);
            Rectangle bitmapBounds = new Rectangle(0, 0, full.Width, full.Height);
            relative = Rectangle.Intersect(bitmapBounds, relative);

            if (relative.Width > 1 && relative.Height > 1 &&
                (relative.X != 0 || relative.Y != 0 ||
                 relative.Width != full.Width || relative.Height != full.Height))
            {
                Bitmap cropped = full.Clone(relative, PixelFormat.Format32bppArgb);
                full.Dispose();
                return cropped;
            }

            return full;
        }

        private static Rectangle GetVisualWindowRect(IntPtr hwnd, Rectangle fallback)
        {
            try
            {
                Native.RECT rawVisualRect;
                int hr = Native.DwmGetWindowAttribute(
                    hwnd,
                    Native.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out rawVisualRect,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.RECT)));

                if (hr == 0)
                {
                    Rectangle visual = rawVisualRect.ToRectangle();
                    if (visual.Width > 1 && visual.Height > 1)
                        return visual;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static Bitmap TryCaptureVisibleWindow(Rectangle visualRect)
        {
            Rectangle visible = Rectangle.Intersect(SystemInformation.VirtualScreen, visualRect);
            if (visible.Width < 2 || visible.Height < 2)
                return null;

            try
            {
                return CaptureRectangle(visible);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsObviouslyBlank(Bitmap bmp)
        {
            if (bmp == null || bmp.Width < 2 || bmp.Height < 2)
                return true;

            // 전체 픽셀을 훑지 않고 내부 5x5 지점만 샘플링해 비용을 제한합니다.
            int transparent = 0;
            int veryDark = 0;
            int samples = 0;
            int[] steps = new int[] { 1, 2, 3, 4, 5 };

            for (int yi = 0; yi < steps.Length; yi++)
            {
                int y = (bmp.Height - 1) * steps[yi] / 6;
                for (int xi = 0; xi < steps.Length; xi++)
                {
                    int x = (bmp.Width - 1) * steps[xi] / 6;
                    Color c = bmp.GetPixel(x, y);
                    samples++;
                    if (c.A == 0) transparent++;
                    if (c.R <= 2 && c.G <= 2 && c.B <= 2) veryDark++;
                }
            }

            return transparent == samples || veryDark == samples;
        }

        public static Color ReadScreenPixel(Point p)
        {
            IntPtr dc = Native.GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return Color.White;

            uint raw = Native.GetPixel(dc, p.X, p.Y);
            Native.ReleaseDC(IntPtr.Zero, dc);

            if (raw == 0xFFFFFFFF) return Color.White;

            int r = (int)(raw & 0xFF);
            int g = (int)((raw >> 8) & 0xFF);
            int b = (int)((raw >> 16) & 0xFF);
            return Color.FromArgb(r, g, b);
        }

        public static IntPtr GetTopWindowAtPoint(Point p)
        {
            uint ownPid = (uint)Process.GetCurrentProcess().Id;

            // 먼저 실제 hit-test 결과를 사용합니다. 브라우저/IDE처럼 자식 HWND가 많은 창도
            // GA_ROOT로 최상위 창을 얻어 사용자가 가리킨 창과 선택 테두리가 일치하게 합니다.
            Native.POINT nativePoint;
            nativePoint.X = p.X;
            nativePoint.Y = p.Y;
            IntPtr hit = Native.WindowFromPoint(nativePoint);
            if (hit != IntPtr.Zero)
            {
                IntPtr root = Native.GetAncestor(hit, Native.GA_ROOT);
                if (IsSelectableWindow(root, ownPid, p))
                    return root;
            }

            // 하이라이트처럼 CapPicker 자체의 투명/TopMost 창이 hit-test를 가릴 수 있으므로
            // 실패 시 기존 Z-order 순회 방식으로 바로 아래의 실제 대상 창을 찾습니다.
            IntPtr hwnd = Native.GetTopWindow(IntPtr.Zero);
            while (hwnd != IntPtr.Zero)
            {
                if (IsSelectableWindow(hwnd, ownPid, p))
                    return hwnd;

                hwnd = Native.GetWindow(hwnd, Native.GW_HWNDNEXT);
            }

            return IntPtr.Zero;
        }

        private static bool IsSelectableWindow(IntPtr hwnd, uint ownPid, Point p)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd))
                return false;

            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == ownPid)
                return false;

            Rectangle r;
            return TryGetWindowBounds(hwnd, out r) && r.Contains(p);
        }

        public static bool TryGetWindowBounds(IntPtr hwnd, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (hwnd == IntPtr.Zero) return false;

            Native.RECT r;
            try
            {
                int hr = Native.DwmGetWindowAttribute(
                    hwnd,
                    Native.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out r,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.RECT)));

                if (hr == 0)
                {
                    Rectangle dr = r.ToRectangle();
                    if (dr.Width > 1 && dr.Height > 1)
                    {
                        bounds = dr;
                        return true;
                    }
                }
            }
            catch
            {
            }

            if (Native.GetWindowRect(hwnd, out r))
            {
                Rectangle wr = r.ToRectangle();
                if (wr.Width > 1 && wr.Height > 1)
                {
                    bounds = wr;
                    return true;
                }
            }

            return false;
        }

        public static Rectangle ClampToVirtualScreen(Rectangle r)
        {
            Rectangle v = SystemInformation.VirtualScreen;
            return Rectangle.Intersect(v, r);
        }
    }
}
