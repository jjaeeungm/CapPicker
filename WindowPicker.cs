
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CapPicker
{
    internal sealed class WindowHighlightForm : Form
    {
        public WindowHighlightForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

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

        protected override void OnPaint(PaintEventArgs e)
        {
            using (Pen dark = new Pen(Color.Black, 5))
            using (Pen light = new Pen(Color.White, 2))
            {
                Rectangle r = new Rectangle(2, 2, Width - 5, Height - 5);
                e.Graphics.DrawRectangle(dark, r);
                e.Graphics.DrawRectangle(light, r);
            }
        }
    }

    internal sealed class WindowPickerController : IDisposable
    {
        private readonly Form owner;
        private readonly Timer timer;
        private readonly WindowHighlightForm highlight;
        private Native.LowLevelMouseProc hookProc;
        private IntPtr hook = IntPtr.Zero;
        private bool running;
        private bool mousePending;

        private IntPtr currentHwnd = IntPtr.Zero;
        private Rectangle currentBounds = Rectangle.Empty;
        private IntPtr pendingHwnd = IntPtr.Zero;
        private Rectangle pendingBounds = Rectangle.Empty;

        public event Action<IntPtr, Rectangle> Selected;
        public event Action Cancelled;

        public WindowPickerController(Form ownerForm)
        {
            owner = ownerForm;
            highlight = new WindowHighlightForm();
            hookProc = new Native.LowLevelMouseProc(MouseHook);

            timer = new Timer();
            timer.Interval = 40;
            timer.Tick += TimerTick;
        }

        public bool Start()
        {
            if (running) return false;

            hook = Native.SetWindowsHookEx(
                Native.WH_MOUSE_LL,
                hookProc,
                Native.GetModuleHandle(null),
                0);

            if (hook == IntPtr.Zero)
                return false;

            running = true;
            mousePending = false;
            UpdateTarget();
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

            UpdateTarget();
        }

        private void UpdateTarget()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;

            IntPtr hwnd = CaptureService.GetTopWindowAtPoint(new Point(p.X, p.Y));
            Rectangle r;

            if (hwnd != IntPtr.Zero && CaptureService.TryGetWindowBounds(hwnd, out r))
            {
                currentHwnd = hwnd;
                currentBounds = CaptureService.ClampToVirtualScreen(r);

                if (currentBounds.Width > 1 && currentBounds.Height > 1)
                {
                    highlight.Bounds = currentBounds;
                    if (!highlight.Visible)
                        highlight.Show();
                    highlight.Invalidate();
                    return;
                }
            }

            currentHwnd = IntPtr.Zero;
            currentBounds = Rectangle.Empty;
            highlight.Hide();
        }

        private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && running)
            {
                int msg = wParam.ToInt32();

                if (msg == Native.WM_LBUTTONDOWN)
                {
                    if (currentHwnd != IntPtr.Zero && !currentBounds.IsEmpty)
                    {
                        mousePending = true;
                        pendingHwnd = currentHwnd;
                        pendingBounds = currentBounds;
                        return new IntPtr(1);
                    }
                }

                if (msg == Native.WM_LBUTTONUP && mousePending)
                {
                    mousePending = false;
                    IntPtr selectedHwnd = pendingHwnd;
                    Rectangle selected = pendingBounds;
                    pendingHwnd = IntPtr.Zero;
                    pendingBounds = Rectangle.Empty;

                    owner.BeginInvoke((MethodInvoker)delegate
                    {
                        StopInternal();
                        if (Selected != null && selectedHwnd != IntPtr.Zero && selected.Width > 1 && selected.Height > 1)
                            Selected(selectedHwnd, selected);
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
            pendingHwnd = IntPtr.Zero;
            pendingBounds = Rectangle.Empty;
            highlight.Hide();

            if (hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            StopInternal();
            timer.Dispose();
            highlight.Dispose();
        }
    }
}
