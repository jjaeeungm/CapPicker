using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CapPicker
{
    internal sealed class MagnifierHost : Form
    {
        // 120-DPI reference sizes. Runtime dimensions and the native magnification
        // transform are scaled together so the magnifier keeps the same physical size,
        // visible source-pixel count, and apparent pixel size across Windows scaling.
        public const int BaseWindowSize = 408;
        public const int LargeWindowSize = 612;
        public const int LowSpecWindowSize = 300;
        private const int RefDpi = DpiUtil.ReferenceDpi;

        public IntPtr MagnifierHandle = IntPtr.Zero;
        private int zoom = 16;
        private int currentSize = BaseWindowSize;
        private int uiDpi = RefDpi;
        private float effectiveZoom = 16f;
        private bool lowSpecMode;

        public int Zoom { get { return zoom; } }
        public int CurrentSize { get { return currentSize; } }
        public float EffectiveZoom { get { return effectiveZoom; } }
        public float UiScale { get { return uiDpi / (float)RefDpi; } }

        public MagnifierHost()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(BaseWindowSize, BaseWindowSize);
            BackColor = Color.Black;

            HandleCreated += delegate { ApplyDpi(ReadWindowDpi()); };
            DpiChanged += delegate(object sender, DpiChangedEventArgs e) { ApplyDpi(e.DeviceDpiNew); };

            CreateControl();
            CreateMagnifierChild();
            ApplyMagnificationTransform();
            TryExcludeFromCapture();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        private int ReadWindowDpi()
        {
            try
            {
                uint dpi = Native.GetDpiForWindow(Handle);
                if (dpi >= 48 && dpi <= 480) return (int)dpi;
            }
            catch { }
            return RefDpi;
        }

        private int S(int value)
        {
            return Math.Max(1, (int)Math.Round(value * uiDpi / (double)RefDpi));
        }

        private int GetReferenceWindowSize()
        {
            if (lowSpecMode) return LowSpecWindowSize;
            return zoom > 64 ? LargeWindowSize : BaseWindowSize;
        }

        public void SetLowSpecMode(bool enabled)
        {
            if (lowSpecMode == enabled) return;
            lowSpecMode = enabled;
            int desiredSize = S(GetReferenceWindowSize());
            if (desiredSize != currentSize)
            {
                currentSize = desiredSize;
                ClientSize = new Size(currentSize, currentSize);
                if (MagnifierHandle != IntPtr.Zero)
                    Native.SetWindowPos(MagnifierHandle, IntPtr.Zero, 0, 0, currentSize, currentSize, Native.SWP_NOACTIVATE);
            }
            ApplyMagnificationTransform();
        }

        private void ApplyDpi(int dpi)
        {
            if (dpi < 48 || dpi > 480) dpi = RefDpi;
            uiDpi = dpi;
            currentSize = S(GetReferenceWindowSize());
            ClientSize = new Size(currentSize, currentSize);
            if (MagnifierHandle != IntPtr.Zero)
                Native.SetWindowPos(MagnifierHandle, IntPtr.Zero, 0, 0, currentSize, currentSize, Native.SWP_NOACTIVATE);
            ApplyMagnificationTransform();
        }

        private void CreateMagnifierChild()
        {
            MagnifierHandle = Native.CreateWindowEx(
                0, "Magnifier", null,
                Native.WS_CHILD | Native.WS_VISIBLE,
                0, 0, currentSize, currentSize,
                Handle, IntPtr.Zero, Native.GetModuleHandle(null), IntPtr.Zero);

            if (MagnifierHandle == IntPtr.Zero)
                throw new InvalidOperationException(L10n.T("Magnifier 컨트롤 생성 실패", "Failed to create magnifier control."));
        }

        public void SetZoom(int value)
        {
            zoom = Math.Max(6, Math.Min(128, value));
            int desiredSize = S(GetReferenceWindowSize());
            if (desiredSize != currentSize)
            {
                currentSize = desiredSize;
                ClientSize = new Size(currentSize, currentSize);
                if (MagnifierHandle != IntPtr.Zero)
                    Native.SetWindowPos(MagnifierHandle, IntPtr.Zero, 0, 0, currentSize, currentSize, Native.SWP_NOACTIVATE);
            }
            ApplyMagnificationTransform();
        }

        private void ApplyMagnificationTransform()
        {
            if (MagnifierHandle == IntPtr.Zero) return;
            effectiveZoom = Math.Max(1f, zoom * UiScale);
            Native.MAGTRANSFORM mt = new Native.MAGTRANSFORM();
            mt.v0 = effectiveZoom;
            mt.v4 = effectiveZoom;
            mt.v8 = 1.0f;
            Native.MagSetWindowTransform(MagnifierHandle, ref mt);
        }

        public Color ReadCenterColor()
        {
            IntPtr dc = Native.GetDC(MagnifierHandle);
            if (dc == IntPtr.Zero) return Color.White;
            uint raw = Native.GetPixel(dc, currentSize / 2, currentSize / 2);
            Native.ReleaseDC(MagnifierHandle, dc);
            if (raw == 0xFFFFFFFF) return Color.White;
            int r = (int)(raw & 0xFF);
            int g = (int)((raw >> 8) & 0xFF);
            int b = (int)((raw >> 16) & 0xFF);
            return Color.FromArgb(r, g, b);
        }

        private void TryExcludeFromCapture()
        {
            try { Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE); }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (MagnifierHandle != IntPtr.Zero)
            {
                Native.DestroyWindow(MagnifierHandle);
                MagnifierHandle = IntPtr.Zero;
            }
            base.Dispose(disposing);
        }
    }

    // Native Magnifier HWND는 WinForms 자식 컨트롤보다 위에 그려질 수 있으므로,
    // 격자/십자선을 별도 투명 top-level overlay로 분리합니다.
    // 투명 키는 눈에 띄는 분홍색 대신 화면에 거의 나타나지 않는 RGB(1,2,3)를 사용합니다.
    internal sealed class PickerOverlayForm : Form
    {
        private static readonly Color KeyColor = Color.FromArgb(1, 2, 3);
        private const int RefDpi = DpiUtil.ReferenceDpi;
        private int zoom = 16;
        private int uiDpi = RefDpi;
        private bool lowSpecMode;

        public PickerOverlayForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(MagnifierHost.BaseWindowSize, MagnifierHost.BaseWindowSize);
            BackColor = KeyColor;
            TransparencyKey = KeyColor;
            AllowTransparency = true;
            DoubleBuffered = true;

            HandleCreated += delegate { ApplyDpi(ReadWindowDpi()); };
            DpiChanged += delegate(object sender, DpiChangedEventArgs e) { ApplyDpi(e.DeviceDpiNew); };

            CreateControl();
            try { Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE); }
            catch { }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW |
                              Native.WS_EX_NOACTIVATE |
                              Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        private int ReadWindowDpi()
        {
            try
            {
                uint dpi = Native.GetDpiForWindow(Handle);
                if (dpi >= 48 && dpi <= 480) return (int)dpi;
            }
            catch { }
            return RefDpi;
        }

        private int S(int value)
        {
            return Math.Max(1, (int)Math.Round(value * uiDpi / (double)RefDpi));
        }

        private int GetReferenceWindowSize()
        {
            if (lowSpecMode) return MagnifierHost.LowSpecWindowSize;
            return zoom > 64 ? MagnifierHost.LargeWindowSize : MagnifierHost.BaseWindowSize;
        }

        public void SetLowSpecMode(bool enabled)
        {
            if (lowSpecMode == enabled) return;
            lowSpecMode = enabled;
            int desiredSize = S(GetReferenceWindowSize());
            if (ClientSize.Width != desiredSize)
                ClientSize = new Size(desiredSize, desiredSize);
            Invalidate();
        }

        private void ApplyDpi(int dpi)
        {
            if (dpi < 48 || dpi > 480) dpi = RefDpi;
            uiDpi = dpi;
            int size = S(GetReferenceWindowSize());
            ClientSize = new Size(size, size);
            Invalidate();
        }

        public void SetZoom(int value)
        {
            zoom = Math.Max(6, Math.Min(128, value));
            int desiredSize = S(GetReferenceWindowSize());
            if (ClientSize.Width != desiredSize)
                ClientSize = new Size(desiredSize, desiredSize);
            Invalidate();
            if (!lowSpecMode) Update();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.None;

            int size = ClientSize.Width;
            int center = size / 2;
            int pixel = Math.Max(1, S(zoom));
            int boundary = center - pixel / 2;

            using (Pen grid = new Pen(Color.FromArgb(105, 170, 170, 170), Math.Max(1f, S(1))))
            {
                grid.DashStyle = DashStyle.Dot;
                for (int x = boundary; x > S(2); x -= pixel)
                    e.Graphics.DrawLine(grid, x, 0, x, size);
                for (int x = boundary + pixel; x < size - S(2); x += pixel)
                    e.Graphics.DrawLine(grid, x, 0, x, size);
                for (int y = boundary; y > S(2); y -= pixel)
                    e.Graphics.DrawLine(grid, 0, y, size, y);
                for (int y = boundary + pixel; y < size - S(2); y += pixel)
                    e.Graphics.DrawLine(grid, 0, y, size, y);
            }

            Rectangle centralPixel = new Rectangle(boundary, boundary, pixel, pixel);
            using (Pen black = new Pen(Color.FromArgb(18, 18, 18), S(3)))
            using (Pen white = new Pen(Color.White, Math.Max(1f, S(1))))
            {
                e.Graphics.DrawRectangle(black, centralPixel);
                Rectangle inner = centralPixel;
                inner.Inflate(-S(2), -S(2));
                if (inner.Width > 1 && inner.Height > 1)
                    e.Graphics.DrawRectangle(white, inner);
            }

            int gap = pixel / 2 + S(5);
            int arm = Math.Max(S(42), Math.Min(S(72), pixel * 2 + S(18)));
            using (Pen black = new Pen(Color.FromArgb(16, 16, 16), S(4)))
            using (Pen white = new Pen(Color.White, Math.Max(1f, S(1))))
            {
                DrawCrossArm(e.Graphics, black, center, gap, arm);
                DrawCrossArm(e.Graphics, white, center, gap, arm);
            }

            using (Pen border = new Pen(Color.FromArgb(42, 45, 50), S(2)))
                e.Graphics.DrawRectangle(border, S(1), S(1), size - S(3), size - S(3));

            Rectangle badge = new Rectangle(S(10), S(10), S(54), S(26));
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(38, 41, 46)))
            using (Font f = new Font("Segoe UI", S(13), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                e.Graphics.FillRectangle(bg, badge);
                TextRenderer.DrawText(e.Graphics, zoom + "×", f, badge, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private static void DrawCrossArm(Graphics g, Pen p, int center, int gap, int arm)
        {
            g.DrawLine(p, center, center - gap - arm, center, center - gap);
            g.DrawLine(p, center, center + gap, center, center + gap + arm);
            g.DrawLine(p, center - gap - arm, center, center - gap, center);
            g.DrawLine(p, center + gap, center, center + gap + arm, center);
        }
    }

    internal sealed class PickerInfoForm : Form
    {
        private const int RefDpi = DpiUtil.ReferenceDpi;
        private const int RefWidth = PickerInfoRenderer.ReferenceWidth;
        private const int RefHeight = PickerInfoRenderer.ReferenceHeight;

        private Point screenPoint;
        private Color color = Color.White;
        private int zoom = 16;
        private int uiDpi = RefDpi;

        public PickerInfoForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(RefWidth, RefHeight);
            BackColor = AppTheme.Toolbar;
            DoubleBuffered = true;

            HandleCreated += delegate { ApplyDpi(ReadWindowDpi()); };
            DpiChanged += delegate(object sender, DpiChangedEventArgs e) { ApplyDpi(e.DeviceDpiNew); };

            CreateControl();
            try { Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE); }
            catch { }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        private int ReadWindowDpi()
        {
            try
            {
                uint dpi = Native.GetDpiForWindow(Handle);
                if (dpi >= 48 && dpi <= 480) return (int)dpi;
            }
            catch { }
            return RefDpi;
        }

        private int S(int value)
        {
            return Math.Max(1, (int)Math.Round(value * uiDpi / (double)RefDpi));
        }

        private void ApplyDpi(int dpi)
        {
            if (dpi < 48 || dpi > 480) dpi = RefDpi;
            uiDpi = dpi;
            ClientSize = new Size(S(RefWidth), S(RefHeight));
            Invalidate();
        }

        public void UpdateInfo(Point p, Color c, int zoomValue, bool immediate)
        {
            screenPoint = p;
            color = c;
            zoom = zoomValue;
            Invalidate();
            if (immediate) Update();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PickerInfoRenderer.Draw(
                e.Graphics,
                new Rectangle(0, 0, Width - 1, Height - 1),
                color,
                screenPoint,
                zoom,
                uiDpi / (float)RefDpi);
        }
    }

    internal sealed class ColorPickerController : IDisposable
    {
        private static readonly int[] ZoomLevels = new int[] { 6, 8, 10, 12, 16, 20, 24, 32, 40, 48, 64, 80, 96, 128 };
        private const int NormalMovingInterval = 25;
        private const int NormalIdleInterval = 50;
        private const int LowSpecMovingInterval = 50;
        private const int LowSpecIdleInterval = 160;
        private const int LowSpecAccurateColorEvery = 5;

        private readonly Form owner;
        private readonly MagnifierHost host;
        private readonly PickerOverlayForm overlay;
        private readonly PickerInfoForm info;
        private readonly System.Windows.Forms.Timer timer;

        private Native.LowLevelMouseProc hookProc;
        private IntPtr hook = IntPtr.Zero;
        private bool running;
        private bool mousePending;
        private bool cursorHidden;
        private Point pendingPoint;
        private Point lastPoint = new Point(Int32.MinValue, Int32.MinValue);
        private int zoomIndex = 4;
        private bool forceRefresh;
        private bool lowSpecMode;
        private int lowSpecColorCounter;

        public event Action<Color, Point> Picked;
        public event Action Cancelled;
        public bool IsReady { get; private set; }

        public ColorPickerController(Form ownerForm)
        {
            owner = ownerForm;
            if (!Native.MagInitialize())
            {
                IsReady = false;
                return;
            }

            try
            {
                host = new MagnifierHost();
                overlay = new PickerOverlayForm();
                info = new PickerInfoForm();

                host.CreateControl();
                overlay.CreateControl();
                info.CreateControl();

                IntPtr[] excluded = new IntPtr[] { owner.Handle, host.Handle, overlay.Handle, info.Handle };
                Native.MagSetWindowFilterList(host.MagnifierHandle, Native.MW_FILTERMODE_EXCLUDE, excluded.Length, excluded);

                hookProc = new Native.LowLevelMouseProc(MouseHook);
                timer = new System.Windows.Forms.Timer();
                timer.Interval = NormalMovingInterval;
                timer.Tick += TimerTick;

                IsReady = true;
            }
            catch
            {
                IsReady = false;
                try { if (info != null) info.Dispose(); } catch { }
                try { if (overlay != null) overlay.Dispose(); } catch { }
                try { if (host != null) host.Dispose(); } catch { }
                Native.MagUninitialize();
            }
        }

        public bool Start()
        {
            if (!IsReady || running) return false;

            hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, hookProc, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) return false;

            running = true;
            mousePending = false;
            lastPoint = new Point(Int32.MinValue, Int32.MinValue);
            forceRefresh = true;
            lowSpecMode = AppSettings.LowSpecOptimization;
            lowSpecColorCounter = 0;
            timer.Interval = lowSpecMode ? LowSpecMovingInterval : NormalMovingInterval;
            host.SetLowSpecMode(lowSpecMode);
            overlay.SetLowSpecMode(lowSpecMode);

            try { Cursor.Hide(); cursorHidden = true; }
            catch { cursorHidden = false; }

            int zoom = ZoomLevels[zoomIndex];
            host.SetZoom(zoom);
            overlay.SetZoom(zoom);

            // 숨겨진 상태에서 1차 위치/값을 준비한 뒤 한 번에 표시합니다.
            UpdateView(true);
            Native.ShowWindow(host.Handle, Native.SW_SHOWNOACTIVATE);
            Native.ShowWindow(overlay.Handle, Native.SW_SHOWNOACTIVATE);
            Native.ShowWindow(info.Handle, Native.SW_SHOWNOACTIVATE);

            timer.Start();
            return true;
        }

        private void TimerTick(object sender, EventArgs e)
        {
            if (!running) return;

            if ((Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0)
            {
                StopInternal();
                if (Cancelled != null) Cancelled();
                return;
            }

            UpdateView(forceRefresh);
            forceRefresh = false;
        }

        private void UpdateView(bool forced)
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;
            Point point = new Point(p.X, p.Y);
            bool moved = point != lastPoint;
            lastPoint = point;

            int zoom = ZoomLevels[zoomIndex];
            int windowSize = host.CurrentSize;
            int sourcePixels = (int)Math.Round(windowSize / Math.Max(1.0f, host.EffectiveZoom));
            if (sourcePixels < 3) sourcePixels = 3;
            if ((sourcePixels & 1) == 0) sourcePixels++;
            int half = sourcePixels / 2;

            Native.RECT src = new Native.RECT();
            src.Left = p.X - half;
            src.Top = p.Y - half;
            src.Right = src.Left + sourcePixels;
            src.Bottom = src.Top + sourcePixels;
            Native.MagSetWindowSource(host.MagnifierHandle, src);
            // Updating the native Magnifier child is sufficient; forcing the empty parent
            // window as well adds synchronous work on slower PCs without changing output.
            Native.UpdateWindow(host.MagnifierHandle);

            int x = p.X - windowSize / 2;
            int y = p.Y - windowSize / 2;

            Native.SetWindowPos(host.Handle, Native.HWND_TOPMOST,
                x, y, windowSize, windowSize, Native.SWP_NOACTIVATE);
            Native.SetWindowPos(overlay.Handle, Native.HWND_TOPMOST,
                x, y, windowSize, windowSize, Native.SWP_NOACTIVATE);

            PositionInfoWindow(point, x, y);

            Color live;
            if (!lowSpecMode)
            {
                // Normal mode favors exact live color synchronization.
                try { Native.DwmFlush(); } catch { }
                Point displayedCenter = new Point(
                    x + windowSize / 2,
                    y + windowSize / 2);
                live = CaptureService.ReadScreenPixel(displayedCenter);
            }
            else
            {
                // Low-spec mode avoids a DWM synchronization barrier on every frame.
                // The native magnifier DC is sampled cheaply and periodically corrected
                // with an exact post-DWM screen read to prevent long-lived stale colors.
                lowSpecColorCounter++;
                bool accurateFrame = forced || lowSpecColorCounter >= LowSpecAccurateColorEvery;
                if (accurateFrame)
                {
                    lowSpecColorCounter = 0;
                    try { Native.DwmFlush(); } catch { }
                    Point displayedCenter = new Point(
                        x + windowSize / 2,
                        y + windowSize / 2);
                    live = CaptureService.ReadScreenPixel(displayedCenter);
                }
                else
                {
                    live = host.ReadCenterColor();
                }
            }

            info.UpdateInfo(point, live, zoom, !lowSpecMode);

            // Adapt update cadence instead of burning CPU/GPU at a fixed high frame rate.
            // Stationary cursors still refresh periodically so animated content can change.
            int desiredInterval;
            if (lowSpecMode)
                desiredInterval = (moved || forced) ? LowSpecMovingInterval : LowSpecIdleInterval;
            else
                desiredInterval = (moved || forced) ? NormalMovingInterval : NormalIdleInterval;
            if (timer.Interval != desiredInterval) timer.Interval = desiredInterval;
        }

        private void PositionInfoWindow(Point cursor, int hostX, int hostY)
        {
            Rectangle wa = Screen.FromPoint(cursor).WorkingArea;
            int gap = Math.Max(4, (int)Math.Round(10f * host.UiScale));
            int x = hostX + host.CurrentSize + gap;
            if (x + info.Width > wa.Right)
                x = hostX - info.Width - gap;

            int y = cursor.Y - info.Height / 2;
            y = Math.Max(wa.Top + 4, Math.Min(y, wa.Bottom - info.Height - 4));

            Native.SetWindowPos(info.Handle, Native.HWND_TOPMOST,
                x, y, info.Width, info.Height, Native.SWP_NOACTIVATE);
        }

        private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && running)
            {
                int msg = wParam.ToInt32();
                Native.MSLLHOOKSTRUCT data = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                    lParam, typeof(Native.MSLLHOOKSTRUCT));

                if (msg == Native.WM_MOUSEMOVE)
                {
                    // Wake the adaptive timer immediately when movement resumes after an idle period.
                    forceRefresh = true;
                    int movingInterval = lowSpecMode ? LowSpecMovingInterval : NormalMovingInterval;
                    if (timer.Interval != movingInterval) timer.Interval = movingInterval;
                }

                if (msg == Native.WM_MOUSEWHEEL)
                {
                    short delta = unchecked((short)((data.mouseData >> 16) & 0xFFFF));
                    int next = zoomIndex + (delta > 0 ? 1 : -1);
                    next = Math.Max(0, Math.Min(ZoomLevels.Length - 1, next));
                    if (next != zoomIndex)
                    {
                        zoomIndex = next;
                        int zoom = ZoomLevels[zoomIndex];
                        host.SetZoom(zoom);
                        overlay.SetZoom(zoom);
                        forceRefresh = true;
                    }
                    return new IntPtr(1);
                }

                if (msg == Native.WM_LBUTTONDOWN)
                {
                    pendingPoint = new Point(data.pt.X, data.pt.Y);
                    mousePending = true;
                    return new IntPtr(1);
                }

                if (msg == Native.WM_LBUTTONUP && mousePending)
                {
                    mousePending = false;
                    Point selectedPoint = pendingPoint;
                    owner.BeginInvoke((MethodInvoker)delegate
                    {
                        timer.Stop();
                        Native.ShowWindow(info.Handle, Native.SW_HIDE);
                        Native.ShowWindow(overlay.Handle, Native.SW_HIDE);
                        Native.ShowWindow(host.Handle, Native.SW_HIDE);
                        Application.DoEvents();
                        Thread.Sleep(8);

                        Color exact = CaptureService.ReadScreenPixel(selectedPoint);
                        StopInternal();
                        if (Picked != null) Picked(exact, selectedPoint);
                    });
                    return new IntPtr(1);
                }
            }

            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        private void StopInternal()
        {
            if (!running) return;

            timer.Stop();
            running = false;
            mousePending = false;
            Native.ShowWindow(info.Handle, Native.SW_HIDE);
            Native.ShowWindow(overlay.Handle, Native.SW_HIDE);
            Native.ShowWindow(host.Handle, Native.SW_HIDE);

            if (hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }

            if (cursorHidden)
            {
                try { Cursor.Show(); } catch { }
                cursorHidden = false;
            }
        }

        public void Dispose()
        {
            StopInternal();
            if (timer != null) timer.Dispose();
            if (info != null) info.Dispose();
            if (overlay != null) overlay.Dispose();
            if (host != null) host.Dispose();
            if (IsReady) Native.MagUninitialize();
            IsReady = false;
        }
    }
}
