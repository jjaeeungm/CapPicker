using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapPicker
{
    
internal static class DpiUtil
{
    public static int Scale(Control control, int logical)
    {
        float factor = Factor(control);
        return Math.Max(1, (int)Math.Round(logical * factor));
    }

    public static float ScaleF(Control control, float logical)
    {
        return logical * Factor(control);
    }

    // The UI was visually tuned on the reference 125% (120 DPI) screen.
    // Treat that screen as scale 1.0, then scale geometry/custom drawing by the
    // monitor DPI relative to 120 DPI. This keeps the same logical/physical size
    // and proportions at 100/125/150/175/200% instead of mixing a 96-DPI base
    // with a 125% cap. Capture/screen coordinates continue to use real pixels.
    public const int ReferenceDpi = 120;

    public static float Factor(Control control)
    {
        try
        {
            if (control != null && control.IsHandleCreated)
            {
                // DeviceDpi can remain 96 in .NET Framework even when the HWND is actually
                // on a high-DPI monitor. Ask Windows for the HWND DPI first.
                uint dpi = Native.GetDpiForWindow(control.Handle);
                if (dpi > 0) return NormalizeUiScale(dpi / (float)ReferenceDpi);

                if (control.DeviceDpi > 0)
                    return NormalizeUiScale(control.DeviceDpi / (float)ReferenceDpi);
            }
        }
        catch { }
        return 1f;
    }

    private static float NormalizeUiScale(float scale)
    {
        // Normal Windows desktop DPI values are comfortably inside this range.
        // The guard only protects custom drawing from a bogus native DPI value.
        if (scale < 0.5f) return 0.5f;
        if (scale > 4f) return 4f;
        return scale;
    }
}

internal static class AppTheme
{
    // 최초 제공된 화면구성 PDF의 실제 회색 톤을 기준으로 맞춤.
    public static readonly Color Title = Color.FromArgb(38, 38, 38);
    public static readonly Color Window = Color.FromArgb(64, 64, 64);
    public static readonly Color Toolbar = Color.FromArgb(64, 64, 64);
    public static readonly Color Workspace = Color.FromArgb(89, 89, 89);
    public static readonly Color Options = Color.FromArgb(64, 64, 64);
    public static readonly Color Status = Color.FromArgb(64, 64, 64);
    public static readonly Color Button = Color.FromArgb(70, 70, 70);
    public static readonly Color ButtonHover = Color.FromArgb(82, 82, 82);
    public static readonly Color Border = Color.FromArgb(94, 94, 94);
    public static readonly Color Text = Color.FromArgb(246, 246, 246);
    public static readonly Color MutedText = Color.FromArgb(205, 205, 205);
    public static readonly Color DisabledText = Color.FromArgb(145, 145, 145);
}

    internal enum AppIcon
    {
        None,
        RectangleCapture,
        SizeCapture,
        WindowCapture,
        FullScreen,
        History,
        Eyedropper,
        Pen,
        Highlighter,
        Rectangle,
        Ellipse,
        Arrow,
        Check,
        Emoji,
        Text,
        Eraser,
        PixelEraser,
        Crop,
        RotateLeft,
        RotateRight,
        Undo,
        Redo,
        Copy,
        Save,
        Print,
        Settings
    }

    internal sealed class FlatButton : Control
    {
        private bool hovered;
        private bool pressed;
        private bool selected;
        private AppIcon icon = AppIcon.None;
        private Color indicatorColor = Color.Empty;

        public Color FillColor { get; set; }
        public Color HoverColor { get; set; }
        public Color TextColor { get; set; }
        public int CornerRadius { get; set; }
        public bool Compact { get; set; }
        public bool IconOnly { get; set; }

        public bool Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        public AppIcon IconKind
        {
            get { return icon; }
            set { icon = value; Invalidate(); }
        }

        public Color IndicatorColor
        {
            get { return indicatorColor; }
            set { indicatorColor = value; Invalidate(); }
        }

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            Size = new Size(104, 40);
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);
            FillColor = AppTheme.Button;
            HoverColor = AppTheme.ButtonHover;
            TextColor = AppTheme.Text;
            CornerRadius = 10;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true; Invalidate(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Enabled && e.Button == MouseButtons.Left)
            {
                pressed = true; Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false; Invalidate(); base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill;
            Color borderColor;
            Color fg;

            if (!Enabled)
            {
                fill = Color.FromArgb(66, 66, 66);
                borderColor = Color.FromArgb(78, 78, 78);
                fg = AppTheme.DisabledText;
            }
            else
            {
                fill = selected ? Color.FromArgb(78, 86, 96) : (hovered ? HoverColor : FillColor);
                borderColor = selected ? Color.FromArgb(132, 170, 216) : AppTheme.Border;
                fg = selected ? Color.White : TextColor;
                if (pressed) fill = ControlPaint.Dark(fill, 0.05f);
            }

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundRect(r, DpiUtil.Scale(this, CornerRadius)))
            using (SolidBrush b = new SolidBrush(fill))
            using (Pen border = new Pen(borderColor))
            {
                e.Graphics.FillPath(b, path);
                e.Graphics.DrawPath(border, path);
            }

            bool iconOnly = IconOnly || String.IsNullOrEmpty(Text);

            if (icon != AppIcon.None)
            {
                if (iconOnly)
                {
                    int s = Math.Min(DpiUtil.Scale(this, 25), Math.Min(Width - DpiUtil.Scale(this, 9), Height - DpiUtil.Scale(this, 9)));
                    Rectangle iconRect = new Rectangle((Width - s) / 2, (Height - s) / 2, s, s);
                    DrawIcon(e.Graphics, iconRect, icon, fg);
                }
                else
                {
                    int isz = DpiUtil.Scale(this, Compact ? 18 : 20);
                    Rectangle iconRect = new Rectangle(DpiUtil.Scale(this, 10), (Height - isz) / 2, isz, isz);
                    Rectangle textRect = new Rectangle(DpiUtil.Scale(this, 34), 0, Width - DpiUtil.Scale(this, 40), Height);
                    DrawIcon(e.Graphics, iconRect, icon, fg);
                    TextRenderer.DrawText(
                        e.Graphics, Text, Font, textRect, fg,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }
            }
            else
            {
                TextRenderer.DrawText(
                    e.Graphics, Text, Font, ClientRectangle, fg,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPadding);
            }

            if (!indicatorColor.IsEmpty)
            {
                Rectangle dot = new Rectangle(Width - DpiUtil.Scale(this, 16), DpiUtil.Scale(this, 6), DpiUtil.Scale(this, 9), DpiUtil.Scale(this, 9));
                using (SolidBrush b = new SolidBrush(indicatorColor))
                using (Pen p = new Pen(Color.FromArgb(145, 80, 84, 90)))
                {
                    e.Graphics.FillEllipse(b, dot);
                    e.Graphics.DrawEllipse(p, dot);
                }
            }
        }

        private static void DrawIcon(Graphics g, Rectangle r, AppIcon kind, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Math.Max(1.5f, r.Width / 11f);
            using (Pen p = new Pen(color, w))
            using (SolidBrush b = new SolidBrush(color))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;

                int l = r.Left + 2;
                int t = r.Top + 2;
                int rr = r.Right - 2;
                int bb = r.Bottom - 2;
                int cx = r.Left + r.Width / 2;
                int cy = r.Top + r.Height / 2;

                switch (kind)
                {
                    case AppIcon.RectangleCapture:
                    case AppIcon.Rectangle:
                        g.DrawRectangle(p, l, t + 1, rr - l, bb - t - 2);
                        break;
                    case AppIcon.SizeCapture:
                        g.DrawRectangle(p, l + 2, t + 3, rr - l - 4, bb - t - 6);
                        g.DrawLine(p, l, cy, rr, cy);
                        g.DrawLine(p, l, cy, l + 3, cy - 3);
                        g.DrawLine(p, l, cy, l + 3, cy + 3);
                        g.DrawLine(p, rr, cy, rr - 3, cy - 3);
                        g.DrawLine(p, rr, cy, rr - 3, cy + 3);
                        break;
                    case AppIcon.WindowCapture:
                        g.DrawRectangle(p, l, t + 1, rr - l, bb - t - 2);
                        g.DrawLine(p, l, t + 5, rr, t + 5);
                        break;
                    case AppIcon.FullScreen:
                        g.DrawLine(p, l, t + 5, l, t); g.DrawLine(p, l, t, l + 5, t);
                        g.DrawLine(p, rr - 5, t, rr, t); g.DrawLine(p, rr, t, rr, t + 5);
                        g.DrawLine(p, l, bb - 5, l, bb); g.DrawLine(p, l, bb, l + 5, bb);
                        g.DrawLine(p, rr - 5, bb, rr, bb); g.DrawLine(p, rr, bb - 5, rr, bb);
                        break;
                    case AppIcon.History:
                        g.DrawArc(p, l + 2, t + 2, rr - l - 4, bb - t - 4, 35, 290);
                        g.DrawLine(p, l + 2, cy - 3, l + 2, cy + 3);
                        g.DrawLine(p, l + 2, cy + 3, l + 7, cy + 3);
                        break;
                    case AppIcon.Eyedropper:
                        g.DrawLine(p, l + 4, bb - 2, rr - 3, t + 3);
                        g.DrawEllipse(p, rr - 6, t, 6, 6);
                        g.DrawLine(p, l + 2, bb, l + 6, bb - 4);
                        break;
                    case AppIcon.Pen:
                        {
                            Point[] body = new Point[] {
                                new Point(l + 3, bb - 5),
                                new Point(l + 7, bb - 1),
                                new Point(rr - 1, t + 7),
                                new Point(rr - 6, t + 2)
                            };
                            g.DrawPolygon(p, body);
                            g.DrawLine(p, l + 3, bb - 5, l + 1, bb);
                            g.DrawLine(p, l + 7, bb - 1, l + 1, bb);
                            g.DrawLine(p, rr - 6, t + 2, rr - 1, t + 7);
                            g.FillEllipse(b, l + 3, bb - 6, 3, 3);
                        }
                        break;
                    case AppIcon.Highlighter:
                        {
                            Point[] body = new Point[] {
                                new Point(l + 3, bb - 5),
                                new Point(l + 8, bb),
                                new Point(rr, t + 8),
                                new Point(rr - 7, t + 1)
                            };
                            g.DrawPolygon(p, body);
                            using (Pen hp = new Pen(color, Math.Max(4f, w * 2.4f)))
                            {
                                hp.StartCap = LineCap.Square;
                                hp.EndCap = LineCap.Square;
                                g.DrawLine(hp, l + 1, bb - 1, l + 9, bb - 1);
                            }
                        }
                        break;
                    case AppIcon.Ellipse:
                        g.DrawEllipse(p, l, t + 1, rr - l, bb - t - 2);
                        break;
                    case AppIcon.Arrow:
                        // 좌우 반전된 하향 화살표: 좌상단에서 우하단으로 향합니다.
                        g.DrawLine(p, l + 2, t + 2, rr - 3, bb - 3);
                        g.DrawLine(p, rr - 3, bb - 3, rr - 9, bb - 3);
                        g.DrawLine(p, rr - 3, bb - 3, rr - 3, bb - 9);
                        break;
                    case AppIcon.Check:
                        using (Font cf = new Font("Segoe UI Symbol", Math.Max(11f, r.Height * 0.72f), FontStyle.Regular, GraphicsUnit.Pixel))
                        {
                            TextRenderer.DrawText(g, "✔", cf, r, color,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        }
                        break;
                    case AppIcon.Emoji:
                        using (Font ef = new Font("Segoe UI Emoji", Math.Max(10f, r.Height * 0.68f), FontStyle.Regular, GraphicsUnit.Pixel))
                        {
                            TextRenderer.DrawText(g, "😊", ef, r, color,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        }
                        break;
                    case AppIcon.Text:
                        using (Font f = new Font("Segoe UI", Math.Max(10f, r.Height * 0.70f), FontStyle.Bold, GraphicsUnit.Pixel))
                        {
                            TextRenderer.DrawText(g, "T", f, r, color,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        }
                        break;
                    case AppIcon.Eraser:
                    case AppIcon.PixelEraser:
                        {
                            Point[] er = new Point[] {
                                new Point(l + 2, cy + 4),
                                new Point(cx + 1, t + 1),
                                new Point(rr - 1, t + 7),
                                new Point(cx - 2, bb - 1)
                            };
                            g.DrawPolygon(p, er);
                            g.DrawLine(p, l + 1, bb - 1, cx + 4, bb - 1);
                            g.DrawLine(p, cx - 4, cy + 7, cx + 3, bb - 1);
                            if (kind == AppIcon.PixelEraser)
                            {
                                int q = Math.Max(2, r.Width / 8);
                                g.FillRectangle(b, rr - q * 2, bb - q, q, q);
                                g.FillRectangle(b, rr - q, bb - q * 2, q, q);
                            }
                        }
                        break;
                    case AppIcon.Crop:
                        g.DrawLine(p, l + 4, t, l + 4, bb - 4);
                        g.DrawLine(p, l + 4, bb - 4, rr, bb - 4);
                        g.DrawLine(p, l, t + 4, rr - 4, t + 4);
                        g.DrawLine(p, rr - 4, t + 4, rr - 4, bb);
                        break;
                    case AppIcon.RotateLeft:
                    case AppIcon.Undo:
                        g.DrawArc(p, l + 4, t + 3, rr - l - 5, bb - t - 5, 200, 270);
                        g.DrawLine(p, l + 3, cy - 3, l + 3, t + 2);
                        g.DrawLine(p, l + 3, t + 2, l + 8, t + 2);
                        break;
                    case AppIcon.RotateRight:
                    case AppIcon.Redo:
                        g.DrawArc(p, l + 1, t + 3, rr - l - 5, bb - t - 5, -110, 270);
                        g.DrawLine(p, rr - 3, cy - 3, rr - 3, t + 2);
                        g.DrawLine(p, rr - 3, t + 2, rr - 8, t + 2);
                        break;
                    case AppIcon.Copy:
                        g.DrawRectangle(p, l + 5, t + 1, rr - l - 5, bb - t - 5);
                        g.DrawRectangle(p, l, t + 6, rr - l - 5, bb - t - 5);
                        break;
                    case AppIcon.Save:
                        g.DrawRectangle(p, l, t, rr - l, bb - t);
                        g.DrawRectangle(p, l + 4, t + 2, rr - l - 8, 5);
                        g.DrawRectangle(p, l + 4, cy + 2, rr - l - 8, bb - cy - 4);
                        break;
                    case AppIcon.Print:
                        g.DrawRectangle(p, l + 4, t, rr - l - 8, Math.Max(4, (cy - t) - 1));
                        g.DrawRectangle(p, l + 2, cy - 2, rr - l - 4, Math.Max(5, bb - cy - 2));
                        g.DrawRectangle(p, l + 5, cy + 2, rr - l - 10, Math.Max(3, bb - cy - 5));
                        g.FillEllipse(b, rr - 5, cy, Math.Max(2, r.Width / 10), Math.Max(2, r.Width / 10));
                        break;
                    case AppIcon.Settings:
                        {
                            int outer = Math.Max(4, r.Width / 3);
                            int inner = Math.Max(2, r.Width / 7);
                            g.DrawEllipse(p, cx - outer / 2, cy - outer / 2, outer, outer);
                            g.DrawEllipse(p, cx - inner / 2, cy - inner / 2, inner, inner);
                            int spoke = Math.Max(3, r.Width / 5);
                            for (int i = 0; i < 8; i++)
                            {
                                double a = Math.PI * i / 4.0;
                                int x1 = cx + (int)Math.Round(Math.Cos(a) * (outer / 2.0));
                                int y1 = cy + (int)Math.Round(Math.Sin(a) * (outer / 2.0));
                                int x2 = cx + (int)Math.Round(Math.Cos(a) * (outer / 2.0 + spoke / 2.0));
                                int y2 = cy + (int)Math.Round(Math.Sin(a) * (outer / 2.0 + spoke / 2.0));
                                g.DrawLine(p, x1, y1, x2, y2);
                            }
                        }
                        break;
                }
            }
        }

        internal static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = Math.Max(2, radius * 2);
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal sealed class PaletteSwatch : Control
    {
        private bool selected;
        public Color SwatchColor { get; private set; }
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        public PaletteSwatch(Color color)
        {
            SwatchColor = color;
            Size = new Size(30, 30);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = DpiUtil.Scale(this, 3);
            Rectangle outer = new Rectangle(pad, pad, Width - DpiUtil.Scale(this, 7), Height - DpiUtil.Scale(this, 7));

            if (selected)
            {
                Rectangle halo = outer;
                halo.Inflate(DpiUtil.Scale(this, 2), DpiUtil.Scale(this, 2));
                using (Pen hp = new Pen(Color.White, DpiUtil.ScaleF(this, 2f)))
                    e.Graphics.DrawEllipse(hp, halo);
            }

            using (SolidBrush b = new SolidBrush(SwatchColor))
                e.Graphics.FillEllipse(b, outer);
            using (Pen p = new Pen(selected ? Color.FromArgb(0, 120, 212) : Color.FromArgb(190, 194, 200), DpiUtil.ScaleF(this, selected ? 2.5f : 1f)))
                e.Graphics.DrawEllipse(p, outer);

            if (selected)
            {
                int lum = (SwatchColor.R * 299 + SwatchColor.G * 587 + SwatchColor.B * 114) / 1000;
                Color tickColor = lum > 155 ? Color.FromArgb(35, 38, 43) : Color.White;
                using (Font f = new Font("Segoe UI Symbol", DpiUtil.Scale(this, 11), FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(e.Graphics, "✓", f, outer, tickColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class ThicknessChoice : Control
    {
        private bool selected;
        public int Thickness { get; private set; }
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        public ThicknessChoice(int thickness)
        {
            Thickness = thickness;
            Size = new Size(42, 34);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (selected)
            {
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(229, 239, 252)))
                    e.Graphics.FillEllipse(bg, 4, 2, Width - 9, Height - 5);
            }

            int d = Math.Max(3, Math.Min(16, Thickness / 2 + 3));
            Rectangle dot = new Rectangle((Width - d) / 2, (Height - d) / 2, d, d);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(55, 59, 66)))
                e.Graphics.FillEllipse(b, dot);
        }
    }


    internal sealed class ToolbarSeparator : Control
    {
        public ToolbarSeparator()
        {
            Size = new Size(17, 40);
            Margin = new Padding(10, 0, 14, 0);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int x = Width / 2;
            using (Pen p = new Pen(Color.FromArgb(112, 112, 112), DpiUtil.ScaleF(this, 1f)))
                e.Graphics.DrawLine(p, x, DpiUtil.Scale(this, 7), x, Height - DpiUtil.Scale(this, 7));
        }
    }

    internal sealed class ModernSlider : Control
    {
        private int minimum = 1;
        private int maximum = 100;
        private int value = 50;
        private bool dragging;

        public event EventHandler ValueChanged;

        public int Minimum
        {
            get { return minimum; }
            set { minimum = value; if (maximum < minimum) maximum = minimum; Value = this.value; }
        }

        public int Maximum
        {
            get { return maximum; }
            set { maximum = Math.Max(value, minimum); Value = this.value; }
        }

        public int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(minimum, Math.Min(maximum, value));
                if (this.value == next) return;
                this.value = next;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public ModernSlider()
        {
            Size = new Size(160, 32);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                Capture = true;
                SetFromX(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) SetFromX(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragging)
            {
                SetFromX(e.X);
                dragging = false;
                Capture = false;
            }
            base.OnMouseUp(e);
        }

        private void SetFromX(int x)
        {
            int left = DpiUtil.Scale(this, 11);
            int right = Math.Max(left + 1, Width - DpiUtil.Scale(this, 11));
            double t = (double)(Math.Max(left, Math.Min(right, x)) - left) / (right - left);
            Value = minimum + (int)Math.Round((maximum - minimum) * t);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int left = DpiUtil.Scale(this, 11);
            int right = Math.Max(left + 1, Width - DpiUtil.Scale(this, 11));
            int cy = Height / 2;
            double t = maximum == minimum ? 0 : (double)(value - minimum) / (maximum - minimum);
            int thumbX = left + (int)Math.Round((right - left) * t);

            using (Pen basePen = new Pen(Color.FromArgb(210, 214, 220), DpiUtil.ScaleF(this, 4f)))
            using (Pen fillPen = new Pen(Color.FromArgb(0, 120, 212), DpiUtil.ScaleF(this, 4f)))
            {
                basePen.StartCap = basePen.EndCap = LineCap.Round;
                fillPen.StartCap = fillPen.EndCap = LineCap.Round;
                e.Graphics.DrawLine(basePen, left, cy, right, cy);
                e.Graphics.DrawLine(fillPen, left, cy, thumbX, cy);
            }

            int thumbRadius = DpiUtil.Scale(this, 7);
            Rectangle thumb = new Rectangle(thumbX - thumbRadius, cy - thumbRadius, thumbRadius * 2, thumbRadius * 2);
            using (SolidBrush b = new SolidBrush(Color.White))
            using (Pen p = new Pen(Color.FromArgb(0, 120, 212), DpiUtil.ScaleF(this, 2f)))
            {
                e.Graphics.FillEllipse(b, thumb);
                e.Graphics.DrawEllipse(p, thumb);
            }
        }
    }

    internal sealed class CustomColorButton : Control
    {
        private bool hovered;
        private bool selected;
        private Color selectedColor = Color.Empty;

        public bool Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        public Color SelectedColor
        {
            get { return selectedColor; }
            set { selectedColor = value; Invalidate(); }
        }

        public CustomColorButton()
        {
            Size = new Size(36, 32);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 8.5f);
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true; Invalidate(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = FlatButton.RoundRect(r, DpiUtil.Scale(this, 9)))
            using (SolidBrush b = new SolidBrush(hovered ? Color.FromArgb(239, 242, 247) : Color.White))
            using (Pen p = new Pen(Color.FromArgb(216, 220, 226)))
            {
                e.Graphics.FillPath(b, path);
                e.Graphics.DrawPath(p, path);
            }

            Color[] ring = new Color[] {
                Color.FromArgb(220, 50, 47), Color.FromArgb(246, 199, 45),
                Color.FromArgb(76, 170, 91), Color.FromArgb(0, 120, 212),
                Color.FromArgb(156, 86, 184), Color.FromArgb(232, 82, 141)
            };
            Rectangle arc = new Rectangle(DpiUtil.Scale(this, 7), DpiUtil.Scale(this, 7), DpiUtil.Scale(this, 18), DpiUtil.Scale(this, 18));
            for (int i = 0; i < ring.Length; i++)
            {
                using (Pen p = new Pen(ring[i], DpiUtil.ScaleF(this, 3f)))
                    e.Graphics.DrawArc(p, arc, i * 60, 62);
            }

            if (selected && selectedColor != Color.Empty)
            {
                Rectangle center = new Rectangle(DpiUtil.Scale(this, 10), DpiUtil.Scale(this, 10), DpiUtil.Scale(this, 12), DpiUtil.Scale(this, 12));
                using (SolidBrush cb = new SolidBrush(selectedColor))
                    e.Graphics.FillEllipse(cb, center);
                using (Pen cp = new Pen(Color.White, DpiUtil.ScaleF(this, 1.5f)))
                    e.Graphics.DrawEllipse(cp, center);

                int lum = (selectedColor.R * 299 + selectedColor.G * 587 + selectedColor.B * 114) / 1000;
                Color tickColor = lum > 155 ? Color.FromArgb(35, 38, 43) : Color.White;
                using (Font f = new Font("Segoe UI Symbol", DpiUtil.Scale(this, 9), FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(e.Graphics, "✓", f, center, tickColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class ColorResultChip : Control
    {
        private Color value = Color.White;
        private bool hasValue;
        private readonly ToolTip feedbackTip = new ToolTip();

        public Color Value
        {
            get { return value; }
            set { this.value = value; hasValue = true; Invalidate(); }
        }

        public void ClearValue()
        {
            hasValue = false;
            value = Color.Empty;
            Invalidate();
        }

        public string Note
        {
            get { return ""; }
            set { Invalidate(); }
        }

        public ColorResultChip()
        {
            Size = new Size(180, 40);
            BackColor = AppTheme.Toolbar;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            feedbackTip.ShowAlways = true;
            RefreshLanguage();
        }

        public void RefreshLanguage()
        {
            feedbackTip.SetToolTip(this, L10n.T(
                "HEX 또는 RGB 줄을 클릭하면 해당 값이 복사됩니다.",
                "Click the HEX or RGB row to copy its value."));
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) feedbackTip.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !hasValue) return;

            string hex = String.Format("#{0:X2}{1:X2}{2:X2}", value.R, value.G, value.B);
            string rgb = String.Format("{0}, {1}, {2}", value.R, value.G, value.B);
            string copied = e.Y < Height / 2 ? hex : rgb;
            string label = e.Y < Height / 2 ? "HEX" : "RGB";

            try
            {
                Clipboard.SetText(copied);
                feedbackTip.Show(label + L10n.T(" 값이 복사되었습니다.", " copied."), this, DpiUtil.Scale(this, 18), Height + DpiUtil.Scale(this, 4), 1200);
            }
            catch
            {
                feedbackTip.Show(L10n.T("클립보드 복사에 실패했습니다.", "Clipboard copy failed."), this, DpiUtil.Scale(this, 18), Height + DpiUtil.Scale(this, 4), 1200);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle card = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = FlatButton.RoundRect(card, DpiUtil.Scale(this, 8)))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(55, 55, 55)))
            using (Pen border = new Pen(Color.FromArgb(96, 96, 96)))
            {
                e.Graphics.FillPath(bg, path);
                e.Graphics.DrawPath(border, path);
            }

            int swSize = DpiUtil.Scale(this, 26);
            Rectangle sw = new Rectangle(DpiUtil.Scale(this, 8), (Height - swSize) / 2, swSize, swSize);
            using (SolidBrush b = new SolidBrush(hasValue ? value : Color.FromArgb(80, 80, 80)))
            using (Pen p = new Pen(Color.FromArgb(150, 150, 150)))
            {
                e.Graphics.FillRectangle(b, sw);
                e.Graphics.DrawRectangle(p, sw);
            }

            // Custom-painted text uses reference-device pixels and follows the same
            // 120-DPI reference scale as the surrounding geometry.
            using (Font label = new Font("Segoe UI", DpiUtil.Scale(this, 11), FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font valueFont = new Font("Consolas", DpiUtil.Scale(this, 12), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                Color muted = Color.FromArgb(200, 200, 200);
                Color text = Color.White;
                int left = DpiUtil.Scale(this, 41);
                int valueLeft = DpiUtil.Scale(this, 71);
                int padY = DpiUtil.Scale(this, 4);
                int rowH = Math.Max(DpiUtil.Scale(this, 19), (Height - padY * 2) / 2);
                Rectangle hexLabel = new Rectangle(left, padY, DpiUtil.Scale(this, 29), rowH);
                Rectangle rgbLabel = new Rectangle(left, Height - padY - rowH, DpiUtil.Scale(this, 29), rowH);
                Rectangle hexValue = new Rectangle(valueLeft, padY, Math.Max(1, Width - valueLeft - DpiUtil.Scale(this, 4)), rowH);
                Rectangle rgbValue = new Rectangle(valueLeft, Height - padY - rowH, Math.Max(1, Width - valueLeft - DpiUtil.Scale(this, 4)), rowH);

                TextRenderer.DrawText(e.Graphics, "HEX", label, hexLabel, muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, "RGB", label, rgbLabel, muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                // 초기 상태에서는 값 영역을 아예 그리지 않습니다.
                // 색상을 실제로 선택한 뒤에만 HEX/RGB 값이 나타납니다.
                if (hasValue)
                {
                    string hex = String.Format("#{0:X2}{1:X2}{2:X2}", value.R, value.G, value.B);
                    string rgb = String.Format("{0}, {1}, {2}", value.R, value.G, value.B);
                    TextRenderer.DrawText(e.Graphics, hex, valueFont, hexValue, text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(e.Graphics, rgb, valueFont, rgbValue, text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }
    }

    internal static class PickerInfoRenderer
    {
        // Screen and in-image pickers intentionally share one information-card geometry.
        // Preserve the preferred vertical density while making the card less wide.
        public const int ReferenceWidth = 204;
        public const int ReferenceHeight = 128;

        private static int S(float scale, int logical)
        {
            if (scale < 0.5f) scale = 0.5f;
            if (scale > 4f) scale = 4f;
            return Math.Max(1, (int)Math.Round(logical * scale));
        }

        public static void Draw(Graphics g, Rectangle card, Color color, Point point, int zoom, float uiScale)
        {
            if (g == null) return;

            SmoothingMode oldSmoothing = g.SmoothingMode;
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (GraphicsPath path = FlatButton.RoundRect(card, S(uiScale, 12)))
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(55, 55, 55)))
                using (Pen border = new Pen(Color.FromArgb(105, 105, 105)))
                {
                    g.FillPath(bg, path);
                    g.DrawPath(border, path);
                }

                Rectangle swatch = new Rectangle(
                    card.X + S(uiScale, 12),
                    card.Y + S(uiScale, 16),
                    S(uiScale, 34),
                    S(uiScale, 38));
                using (SolidBrush b = new SolidBrush(color))
                using (Pen p = new Pen(Color.FromArgb(160, 160, 160)))
                {
                    g.FillRectangle(b, swatch);
                    g.DrawRectangle(p, swatch);
                }

                string hex = String.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
                string rgb = String.Format("{0}, {1}, {2}", color.R, color.G, color.B);

                using (Font label = new Font("Segoe UI", S(uiScale, 14), FontStyle.Regular, GraphicsUnit.Pixel))
                using (Font value = new Font("Segoe UI", S(uiScale, 15), FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    Color muted = Color.FromArgb(195, 195, 195);
                    Color text = Color.White;
                    int valueLeft = card.X + S(uiScale, 91);
                    int valueWidth = Math.Max(S(uiScale, 92), card.Right - valueLeft - S(uiScale, 10));

                    TextRenderer.DrawText(g, "HEX", label,
                        new Rectangle(card.X + S(uiScale, 53), card.Y + S(uiScale, 10), S(uiScale, 34), S(uiScale, 22)), muted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, hex, value,
                        new Rectangle(valueLeft, card.Y + S(uiScale, 9), valueWidth, S(uiScale, 23)), text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                    TextRenderer.DrawText(g, "RGB", label,
                        new Rectangle(card.X + S(uiScale, 53), card.Y + S(uiScale, 35), S(uiScale, 34), S(uiScale, 22)), muted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, rgb, value,
                        new Rectangle(valueLeft, card.Y + S(uiScale, 34), valueWidth, S(uiScale, 23)), text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                    TextRenderer.DrawText(g, L10n.T("좌표", "XY"), label,
                        new Rectangle(card.X + S(uiScale, 14), card.Y + S(uiScale, 75), S(uiScale, 42), S(uiScale, 22)), muted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, String.Format("{0}, {1}", point.X, point.Y), value,
                        new Rectangle(card.X + S(uiScale, 60), card.Y + S(uiScale, 74), card.Width - S(uiScale, 72), S(uiScale, 23)), text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                    TextRenderer.DrawText(g, L10n.T("확대", "Zoom"), label,
                        new Rectangle(card.X + S(uiScale, 14), card.Y + S(uiScale, 101), S(uiScale, 44), S(uiScale, 22)), muted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, zoom + "×", value,
                        new Rectangle(card.X + S(uiScale, 60), card.Y + S(uiScale, 100), S(uiScale, 80), S(uiScale, 23)), text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
            finally
            {
                g.SmoothingMode = oldSmoothing;
            }
        }
    }

}
