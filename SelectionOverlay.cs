
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CapPicker
{
    internal enum SelectionMode
    {
        Rectangle,
        FixedSize
    }

    internal sealed class SelectionOverlay : Form
    {
        private readonly SelectionMode mode;
        private readonly Size fixedSize;
        private bool dragging;
        private Point startScreen;
        private Rectangle currentScreenRect;
        private Bitmap screenPreview;
        private readonly Font overlayFont = new Font(L10n.DefaultFontName, 9f, FontStyle.Regular);
        private readonly Font overlayBoldFont = new Font("Segoe UI", 9f, FontStyle.Bold);

        public event Action<Rectangle> Selected;
        public event Action Cancelled;

        public SelectionOverlay(SelectionMode selectionMode, Size size)
        {
            mode = selectionMode;
            fixedSize = size;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            BackColor = Color.Black;
            Opacity = 1.0;
            DoubleBuffered = true;
            CaptureScreenPreview();
            Cursor = Cursors.Cross;

            KeyPreview = true;
            MouseDown += OverlayMouseDown;
            MouseMove += OverlayMouseMove;
            MouseUp += OverlayMouseUp;
            KeyDown += OverlayKeyDown;
        }

        private void CaptureScreenPreview()
        {
            Rectangle v = SystemInformation.VirtualScreen;
            Bitmap preview = null;
            try
            {
                preview = new Bitmap(Math.Max(1, v.Width), Math.Max(1, v.Height));
                bool copiedAny = false;
                using (Graphics g = Graphics.FromImage(preview))
                {
                    // Capture each display independently into the virtual-desktop bitmap.
                    // Under Per-Monitor-V2 awareness, Screen.Bounds is expressed in the
                    // process coordinate space for each monitor. Copying display-by-display
                    // avoids relying on one cross-monitor CopyFromScreen operation when the
                    // desktop mixes DPI scales or contains negative monitor coordinates.
                    g.Clear(Color.Black);
                    foreach (Screen screen in Screen.AllScreens)
                    {
                        Rectangle src = Rectangle.Intersect(v, screen.Bounds);
                        if (src.Width < 1 || src.Height < 1) continue;

                        int dstX = src.Left - v.Left;
                        int dstY = src.Top - v.Top;
                        try
                        {
                            g.CopyFromScreen(
                                src.Left, src.Top,
                                dstX, dstY,
                                src.Size,
                                CopyPixelOperation.SourceCopy);
                            copiedAny = true;
                        }
                        catch
                        {
                            // Keep the preview usable for the displays that were captured.
                            // The normal dark overlay remains in an uncaptured display area.
                        }
                    }
                }

                if (!copiedAny)
                {
                    preview.Dispose();
                    preview = null;
                }

                if (screenPreview != null) screenPreview.Dispose();
                screenPreview = preview;
                preview = null;
            }
            catch
            {
                if (preview != null) preview.Dispose();
                if (screenPreview != null) screenPreview.Dispose();
                screenPreview = null;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Focus();

            Native.POINT p;
            if (mode == SelectionMode.FixedSize && Native.GetCursorPos(out p))
            {
                currentScreenRect = BuildFixedRect(new Point(p.X, p.Y));
                Invalidate();
            }
        }

        private void OverlayMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            Point screen = PointToScreen(e.Location);

            if (mode == SelectionMode.FixedSize)
            {
                currentScreenRect = BuildFixedRect(screen);
                Commit(currentScreenRect);
                return;
            }

            dragging = true;
            startScreen = screen;
            currentScreenRect = new Rectangle(screen, Size.Empty);
            Invalidate();
        }

        private void OverlayMouseMove(object sender, MouseEventArgs e)
        {
            Point screen = PointToScreen(e.Location);

            if (mode == SelectionMode.FixedSize)
            {
                currentScreenRect = BuildFixedRect(screen);
                Invalidate();
                return;
            }

            if (!dragging) return;
            currentScreenRect = Normalize(startScreen, screen);
            Invalidate();
        }

        private void OverlayMouseUp(object sender, MouseEventArgs e)
        {
            if (mode != SelectionMode.Rectangle || !dragging || e.Button != MouseButtons.Left)
                return;

            dragging = false;
            Point screen = PointToScreen(e.Location);
            currentScreenRect = Normalize(startScreen, screen);

            if (currentScreenRect.Width < 3 || currentScreenRect.Height < 3)
                return;

            Commit(currentScreenRect);
        }

        private void OverlayKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Hide();
                if (Cancelled != null) Cancelled();
                Close();
            }
        }

        private void Commit(Rectangle r)
        {
            Rectangle clipped = CaptureService.ClampToVirtualScreen(r);
            if (clipped.Width < 1 || clipped.Height < 1) return;

            Hide();
            Application.DoEvents();

            if (Selected != null) Selected(clipped);
            Close();
        }

        private Rectangle BuildFixedRect(Point center)
        {
            Rectangle v = SystemInformation.VirtualScreen;

            int w = Math.Max(1, Math.Min(fixedSize.Width, v.Width));
            int h = Math.Max(1, Math.Min(fixedSize.Height, v.Height));

            int x = center.X - w / 2;
            int y = center.Y - h / 2;

            if (x < v.Left) x = v.Left;
            if (y < v.Top) y = v.Top;
            if (x + w > v.Right) x = v.Right - w;
            if (y + h > v.Bottom) y = v.Bottom - h;

            return new Rectangle(x, y, w, h);
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            int left = Math.Min(a.X, b.X);
            int top = Math.Min(a.Y, b.Y);
            int right = Math.Max(a.X, b.X);
            int bottom = Math.Max(a.Y, b.Y);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (screenPreview != null)
                e.Graphics.DrawImageUnscaled(screenPreview, 0, 0);
            else
                e.Graphics.Clear(Color.FromArgb(35, 35, 35));

            Rectangle client = ClientRectangle;
            Rectangle local = Rectangle.Empty;
            bool hasSelection = currentScreenRect.Width > 0 && currentScreenRect.Height > 0;
            if (hasSelection)
            {
                local = new Rectangle(
                    currentScreenRect.X - Bounds.X,
                    currentScreenRect.Y - Bounds.Y,
                    currentScreenRect.Width,
                    currentScreenRect.Height);
                local.Intersect(client);
            }

            using (SolidBrush dim = new SolidBrush(Color.FromArgb(128, 18, 18, 18)))
            {
                if (!hasSelection || local.Width < 1 || local.Height < 1)
                {
                    e.Graphics.FillRectangle(dim, client);
                }
                else
                {
                    if (local.Top > client.Top)
                        e.Graphics.FillRectangle(dim, Rectangle.FromLTRB(client.Left, client.Top, client.Right, local.Top));
                    if (local.Bottom < client.Bottom)
                        e.Graphics.FillRectangle(dim, Rectangle.FromLTRB(client.Left, local.Bottom, client.Right, client.Bottom));
                    if (local.Left > client.Left)
                        e.Graphics.FillRectangle(dim, Rectangle.FromLTRB(client.Left, local.Top, local.Left, local.Bottom));
                    if (local.Right < client.Right)
                        e.Graphics.FillRectangle(dim, Rectangle.FromLTRB(local.Right, local.Top, client.Right, local.Bottom));
                }
            }

            DrawInstruction(e.Graphics);
            if (!hasSelection || local.Width < 1 || local.Height < 1) return;

            Color accent = Color.FromArgb(118, 190, 255);
            using (Pen shadow = new Pen(Color.FromArgb(205, 20, 20, 20), 5f))
            using (Pen accentPen = new Pen(accent, 2f))
            using (Pen inner = new Pen(Color.FromArgb(235, 255, 255, 255), 1f))
            {
                e.Graphics.DrawRectangle(shadow, local);
                e.Graphics.DrawRectangle(accentPen, local);
                Rectangle innerRect = local;
                innerRect.Inflate(-2, -2);
                if (innerRect.Width > 0 && innerRect.Height > 0)
                    e.Graphics.DrawRectangle(inner, innerRect);
            }

            DrawCornerHandles(e.Graphics, local, accent);
            DrawSizeBadge(e.Graphics, local);
        }

        private void DrawInstruction(Graphics g)
        {
            string text = mode == SelectionMode.FixedSize
                ? L10n.T("마우스로 위치 선택  ·  Esc 취소", "Choose a position  ·  Esc to cancel")
                : L10n.T("드래그하여 캡처 영역 선택  ·  Esc 취소", "Drag to select a capture area  ·  Esc to cancel");
            Size ts = TextRenderer.MeasureText(text, overlayBoldFont, Size.Empty, TextFormatFlags.NoPadding);
            Rectangle card = new Rectangle(
                Math.Max(12, (ClientSize.Width - ts.Width - 34) / 2),
                18,
                ts.Width + 34,
                34);
            using (System.Drawing.Drawing2D.GraphicsPath path = FlatButton.RoundRect(card, 9))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(232, AppTheme.Title)))
            using (Pen border = new Pen(Color.FromArgb(115, 115, 115)))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }
            TextRenderer.DrawText(g, text, overlayBoldFont, card, AppTheme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawSizeBadge(Graphics g, Rectangle local)
        {
            string sizeText = currentScreenRect.Width + " × " + currentScreenRect.Height;
            Size ts = TextRenderer.MeasureText(sizeText, overlayFont, Size.Empty, TextFormatFlags.NoPadding);
            int x = local.Left + 8;
            int y = local.Top + 8;
            if (local.Width < ts.Width + 34) x = Math.Max(8, local.Left - ts.Width - 26);
            Rectangle badge = new Rectangle(x, y, ts.Width + 22, ts.Height + 12);
            if (badge.Right > ClientSize.Width - 8) badge.X = Math.Max(8, ClientSize.Width - badge.Width - 8);
            if (badge.Bottom > ClientSize.Height - 8) badge.Y = Math.Max(8, ClientSize.Height - badge.Height - 8);
            using (System.Drawing.Drawing2D.GraphicsPath path = FlatButton.RoundRect(badge, 7))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(235, AppTheme.Title)))
            {
                g.FillPath(bg, path);
            }
            TextRenderer.DrawText(g, sizeText, overlayFont, badge, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static void DrawCornerHandles(Graphics g, Rectangle r, Color accent)
        {
            const int len = 16;
            using (Pen p = new Pen(accent, 3f))
            {
                g.DrawLine(p, r.Left, r.Top, r.Left + len, r.Top);
                g.DrawLine(p, r.Left, r.Top, r.Left, r.Top + len);
                g.DrawLine(p, r.Right, r.Top, r.Right - len, r.Top);
                g.DrawLine(p, r.Right, r.Top, r.Right, r.Top + len);
                g.DrawLine(p, r.Left, r.Bottom, r.Left + len, r.Bottom);
                g.DrawLine(p, r.Left, r.Bottom, r.Left, r.Bottom - len);
                g.DrawLine(p, r.Right, r.Bottom, r.Right - len, r.Bottom);
                g.DrawLine(p, r.Right, r.Bottom, r.Right, r.Bottom - len);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (screenPreview != null) screenPreview.Dispose();
                overlayFont.Dispose();
                overlayBoldFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class FixedSizeDialog : Form
    {
        private const int RefDpi = DpiUtil.ReferenceDpi;
        private int uiDpi = RefDpi;

        private Label widthLabel;
        private Label heightLabel;
        private NumericUpDown widthBox;
        private NumericUpDown heightBox;
        private Button okButton;
        private Button cancelButton;

        public Size SelectedSize { get; private set; }

        public FixedSizeDialog(Size initial)
        {
            Text = L10n.T("크기 지정 캡처", "Fixed-size capture");
            // Match MainForm: all bounds below are authored in the 120-DPI reference
            // coordinate system and scaled explicitly per monitor.
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = AppTheme.Window;
            ForeColor = AppTheme.Text;
            Font = new Font(L10n.DefaultFontName, 9.5f);

            widthLabel = new Label();
            widthLabel.Text = L10n.T("가로", "Width");
            widthLabel.AutoSize = false;
            widthLabel.ForeColor = AppTheme.MutedText;
            widthLabel.TextAlign = ContentAlignment.MiddleLeft;

            heightLabel = new Label();
            heightLabel.Text = L10n.T("세로", "Height");
            heightLabel.AutoSize = false;
            heightLabel.ForeColor = AppTheme.MutedText;
            heightLabel.TextAlign = ContentAlignment.MiddleLeft;

            widthBox = MakeNumber(initial.Width);
            heightBox = MakeNumber(initial.Height);

            okButton = MakeButton(L10n.T("선택 시작", "Start"));
            okButton.Click += delegate
            {
                SelectedSize = new Size((int)widthBox.Value, (int)heightBox.Value);
                DialogResult = DialogResult.OK;
                Close();
            };

            cancelButton = MakeButton(L10n.T("취소", "Cancel"));
            cancelButton.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            Controls.Add(widthLabel);
            Controls.Add(heightLabel);
            Controls.Add(widthBox);
            Controls.Add(heightBox);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;

            HandleCreated += delegate { ApplyDpi(ReadWindowDpi()); };
            DpiChanged += delegate(object sender, DpiChangedEventArgs e) { ApplyDpi(e.DeviceDpiNew); };
            Shown += delegate { ApplyWindowTone(); };
        }

        private NumericUpDown MakeNumber(int value)
        {
            NumericUpDown box = new NumericUpDown();
            box.Minimum = 1;
            box.Maximum = 10000;
            box.Value = Math.Max(1, Math.Min(10000, value));
            box.BackColor = AppTheme.Button;
            box.ForeColor = AppTheme.Text;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Font = new Font(L10n.DefaultFontName, 10f);
            box.TextAlign = HorizontalAlignment.Left;
            return box;
        }

        private Button MakeButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = AppTheme.Button;
            b.ForeColor = AppTheme.Text;
            b.FlatAppearance.BorderColor = AppTheme.Border;
            return b;
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

        private void SetBoundsRef(Control c, int x, int y, int w, int h)
        {
            c.Bounds = new Rectangle(S(x), S(y), S(w), S(h));
        }

        private void ApplyDpi(int dpi)
        {
            if (dpi < 48 || dpi > 480) dpi = RefDpi;
            uiDpi = dpi;

            SuspendLayout();
            try
            {
                ClientSize = new Size(S(342), S(188));
                SetBoundsRef(widthLabel, 24, 22, 138, 24);
                SetBoundsRef(heightLabel, 180, 22, 138, 24);
                SetBoundsRef(widthBox, 24, 50, 138, 32);
                SetBoundsRef(heightBox, 180, 50, 138, 32);
                SetBoundsRef(okButton, 102, 126, 104, 36);
                SetBoundsRef(cancelButton, 216, 126, 102, 36);
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        private void ApplyWindowTone()
        {
            try
            {
                int dark = 1;
                int caption = AppTheme.Title.R | (AppTheme.Title.G << 8) | (AppTheme.Title.B << 16);
                int text = 255 | (255 << 8) | (255 << 16);
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_TEXT_COLOR, ref text, sizeof(int));
            }
            catch { }
        }
    }
}
