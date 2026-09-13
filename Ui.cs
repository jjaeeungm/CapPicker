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

internal sealed class DarkMenuColors : ProfessionalColorTable
{
    // Context menus in CapPicker UI tone (dark gray, no default blue theme).
    public override Color ToolStripDropDownBackground { get { return Color.FromArgb(64, 64, 64); } }
    public override Color MenuBorder { get { return Color.FromArgb(94, 94, 94); } }
    public override Color MenuItemBorder { get { return Color.FromArgb(132, 170, 216); } }
    public override Color MenuItemSelected { get { return Color.FromArgb(82, 82, 82); } }
    public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(82, 82, 82); } }
    public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(82, 82, 82); } }
    public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(70, 70, 70); } }
    public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(70, 70, 70); } }
    public override Color CheckBackground { get { return Color.FromArgb(82, 82, 82); } }
    public override Color CheckSelectedBackground { get { return Color.FromArgb(82, 82, 82); } }
    public override Color CheckPressedBackground { get { return Color.FromArgb(70, 70, 70); } }
    public override Color ImageMarginGradientBegin { get { return Color.FromArgb(64, 64, 64); } }
    public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(64, 64, 64); } }
    public override Color ImageMarginGradientEnd { get { return Color.FromArgb(64, 64, 64); } }
    public override Color SeparatorDark { get { return Color.FromArgb(94, 94, 94); } }
    public override Color SeparatorLight { get { return Color.FromArgb(64, 64, 64); } }
}

internal static class UiFonts
{
    // 가벼움: 툴바 수십 개가 각자 Font를 만들던 것을 공유 1개로 (MuseSpark).
    // 공유 폰트는 해제하지 않는다. 프로세스 종료 시 OS가 회수.
    private static Font flat9;
    public static Font Flat9
    {
        get
        {
            if (flat9 == null) flat9 = new Font("Segoe UI", 9.0f, FontStyle.Regular);
            return flat9;
        }
    }

    public static bool IsShared(Font f)
    {
        return f != null && f == flat9;
    }
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
        Settings,
        ScrollCapture,
        Timer,
        FlipHorizontal,
        FlipVertical
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

        // Small bottom-right badge text (e.g. timer delay seconds).
        public string Badge { get; set; }

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            Size = new Size(104, 40);
            Font = UiFonts.Flat9;
            FillColor = AppTheme.Button;
            HoverColor = AppTheme.ButtonHover;
            TextColor = AppTheme.Text;
            CornerRadius = 10;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void Dispose(bool disposing)
        {
            Font owned = null;
            if (disposing)
            {
                // 공유 폰트는 해제하지 않는다 (MuseSpark). 소유 폰트만 base 해제 뒤 GDI 해제.
                if (Font != null && !UiFonts.IsShared(Font))
                {
                    owned = Font;
                    Font = null;
                }
            }

            base.Dispose(disposing);

            if (disposing)
            {
                try { if (owned != null) owned.Dispose(); } catch { }
            }
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

            if (!String.IsNullOrEmpty(Badge))
            {
                using (Font bf = new Font("Segoe UI", Math.Max(8f, r.Height * 0.26f), FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    Rectangle badgeRect = new Rectangle(0, 0, Width - 2, Height - 2);
                    TextRenderer.DrawText(e.Graphics, Badge, bf, badgeRect, fg,
                        TextFormatFlags.Bottom | TextFormatFlags.Right | TextFormatFlags.NoPadding);
                }
            }
        }

        private static void DrawIcon(Graphics g, Rectangle r, AppIcon kind, Color color)
        {
            // CapPicker icon language:
            // - 24/25px class, rounded outline, one primary metaphor per icon.
            // - Avoid font glyphs for toolbar icons so stroke/weight stays consistent.
            // - Rotation is a circular arrow; Undo/Redo is a hook arrow. Do not mix them.
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float unit = Math.Min(r.Width, r.Height);
            float stroke = Math.Max(1.45f, unit / 12.5f);
            float pad = Math.Max(1.6f, unit * 0.105f);
            float l = r.Left + pad;
            float t = r.Top + pad;
            float rr = r.Right - pad;
            float bb = r.Bottom - pad;
            float cx = (l + rr) * 0.5f;
            float cy = (t + bb) * 0.5f;
            float iw = rr - l;
            float ih = bb - t;

            using (Pen p = new Pen(color, stroke))
            using (SolidBrush b = new SolidBrush(color))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;

                switch (kind)
                {
                    case AppIcon.RectangleCapture:
                        DrawCaptureCorners(g, p, l, t, rr, bb);
                        break;

                    case AppIcon.SizeCapture:
                        // Selection corners + horizontal dimension arrow.
                        DrawCaptureCorners(g, p, l, t, rr, bb);
                        {
                            float x1 = l + iw * 0.18f;
                            float x2 = rr - iw * 0.18f;
                            g.DrawLine(p, x1, cy, x2, cy);
                            DrawArrowHead(g, p, new PointF(x1, cy), new PointF(x1 + iw * 0.22f, cy), unit * 0.14f);
                            DrawArrowHead(g, p, new PointF(x2, cy), new PointF(x2 - iw * 0.22f, cy), unit * 0.14f);
                        }
                        break;

                    case AppIcon.WindowCapture:
                        // A single application window is clearer than two overlapping boxes at 25px.
                        {
                            RectangleF win = new RectangleF(l + iw * 0.04f, t + ih * 0.08f, iw * 0.92f, ih * 0.84f);
                            g.DrawRectangle(p, win.X, win.Y, win.Width, win.Height);
                            g.DrawLine(p, win.Left, win.Top + ih * 0.22f, win.Right, win.Top + ih * 0.22f);
                            float dot = Math.Max(1.5f, unit * 0.055f);
                            g.FillEllipse(b, win.Left + iw * 0.10f - dot * 0.5f, win.Top + ih * 0.11f - dot * 0.5f, dot, dot);
                            g.FillEllipse(b, win.Left + iw * 0.20f - dot * 0.5f, win.Top + ih * 0.11f - dot * 0.5f, dot, dot);
                        }
                        break;

                    case AppIcon.FullScreen:
                        // Monitor outline = entire display; distinct from region-selection corners.
                        {
                            RectangleF screen = new RectangleF(l + iw * 0.02f, t + ih * 0.04f, iw * 0.96f, ih * 0.68f);
                            g.DrawRectangle(p, screen.X, screen.Y, screen.Width, screen.Height);
                            g.DrawLine(p, cx, screen.Bottom, cx, bb - ih * 0.05f);
                            g.DrawLine(p, cx - iw * 0.19f, bb - ih * 0.05f, cx + iw * 0.19f, bb - ih * 0.05f);
                        }
                        break;

                    case AppIcon.History:
                        // Last region = remembered dashed selection + small replay arrow.
                        {
                            using (Pen dp = new Pen(color, Math.Max(1f, stroke * 0.72f)))
                            {
                                dp.DashStyle = DashStyle.Dash;
                                dp.DashCap = DashCap.Round;
                                g.DrawRectangle(dp, l + iw * 0.03f, t + ih * 0.08f, iw * 0.73f, ih * 0.72f);
                            }
                            RectangleF arc = new RectangleF(cx + iw * 0.01f, cy + ih * 0.01f, iw * 0.43f, ih * 0.43f);
                            DrawArcArrow(g, p, arc, 35f, 220f, unit * 0.13f);
                        }
                        break;

                    case AppIcon.ScrollCapture:
                        // Page + right-side scroll direction.
                        {
                            RectangleF page = new RectangleF(l + iw * 0.02f, t + ih * 0.03f, iw * 0.78f, ih * 0.92f);
                            g.DrawRectangle(p, page.X, page.Y, page.Width, page.Height);
                            g.DrawLine(p, page.Left, page.Top + ih * 0.19f, page.Right, page.Top + ih * 0.19f);
                            float sx = rr - iw * 0.07f;
                            float sy1 = t + ih * 0.28f;
                            float sy2 = bb - ih * 0.10f;
                            g.DrawLine(p, sx, sy1, sx, sy2);
                            DrawArrowHead(g, p, new PointF(sx, sy2), new PointF(sx, sy1), unit * 0.15f);
                        }
                        break;

                    case AppIcon.Eyedropper:
                        DrawEyedropper(g, p, l, t, rr, bb);
                        break;

                    case AppIcon.Pen:
                        {
                            PointF[] body = new PointF[] {
                                new PointF(l + iw * 0.12f, bb - ih * 0.27f),
                                new PointF(l + iw * 0.34f, bb - ih * 0.05f),
                                new PointF(rr - iw * 0.04f, t + ih * 0.30f),
                                new PointF(rr - iw * 0.28f, t + ih * 0.06f)
                            };
                            g.DrawPolygon(p, body);
                            PointF nib = new PointF(l + iw * 0.03f, bb - ih * 0.02f);
                            g.DrawLine(p, body[0], nib);
                            g.DrawLine(p, body[1], nib);
                        }
                        break;

                    case AppIcon.Highlighter:
                        {
                            PointF[] body = new PointF[] {
                                new PointF(l + iw * 0.14f, bb - ih * 0.31f),
                                new PointF(l + iw * 0.41f, bb - ih * 0.04f),
                                new PointF(rr - iw * 0.03f, t + ih * 0.38f),
                                new PointF(rr - iw * 0.34f, t + ih * 0.07f)
                            };
                            g.DrawPolygon(p, body);
                            using (Pen hp = new Pen(color, Math.Max(3f, stroke * 2.05f)))
                            {
                                hp.StartCap = LineCap.Square;
                                hp.EndCap = LineCap.Square;
                                g.DrawLine(hp, l + iw * 0.04f, bb - ih * 0.02f, l + iw * 0.45f, bb - ih * 0.02f);
                            }
                        }
                        break;

                    case AppIcon.Rectangle:
                        g.DrawRectangle(p, l + iw * 0.05f, t + ih * 0.10f, iw * 0.90f, ih * 0.80f);
                        break;

                    case AppIcon.Ellipse:
                        g.DrawEllipse(p, l + iw * 0.04f, t + ih * 0.10f, iw * 0.92f, ih * 0.80f);
                        break;

                    case AppIcon.Arrow:
                        {
                            PointF from = new PointF(l + iw * 0.10f, t + ih * 0.12f);
                            PointF tip = new PointF(rr - iw * 0.08f, bb - ih * 0.10f);
                            g.DrawLine(p, from, tip);
                            DrawArrowHead(g, p, tip, from, unit * 0.26f);
                        }
                        break;

                    case AppIcon.Check:
                        using (Pen cp = new Pen(color, Math.Max(stroke, unit / 9.8f)))
                        {
                            cp.StartCap = LineCap.Round;
                            cp.EndCap = LineCap.Round;
                            cp.LineJoin = LineJoin.Round;
                            g.DrawLines(cp, new PointF[] {
                                new PointF(l + iw * 0.09f, cy + ih * 0.02f),
                                new PointF(l + iw * 0.39f, bb - ih * 0.11f),
                                new PointF(rr - iw * 0.04f, t + ih * 0.13f)
                            });
                        }
                        break;

                    case AppIcon.Emoji:
                        // Stamp/symbol tool: neutral monochrome smile keeps the toolbar coherent.
                        {
                            float d = Math.Min(iw, ih) * 0.88f;
                            RectangleF face = new RectangleF(cx - d * 0.5f, cy - d * 0.5f, d, d);
                            g.DrawEllipse(p, face);
                            float eye = Math.Max(1.7f, unit * 0.067f);
                            g.FillEllipse(b, cx - d * 0.21f - eye * 0.5f, cy - d * 0.15f, eye, eye);
                            g.FillEllipse(b, cx + d * 0.21f - eye * 0.5f, cy - d * 0.15f, eye, eye);
                            g.DrawArc(p, cx - d * 0.25f, cy - d * 0.02f, d * 0.50f, d * 0.33f, 15f, 150f);
                        }
                        break;

                    case AppIcon.Text:
                        using (Pen tp = new Pen(color, Math.Max(stroke, unit / 10.0f)))
                        {
                            tp.StartCap = LineCap.Round;
                            tp.EndCap = LineCap.Round;
                            g.DrawLine(tp, l + iw * 0.13f, t + ih * 0.10f, rr - iw * 0.13f, t + ih * 0.10f);
                            g.DrawLine(tp, cx, t + ih * 0.10f, cx, bb - ih * 0.05f);
                        }
                        break;

                    case AppIcon.Eraser:
                    case AppIcon.PixelEraser:
                        {
                            PointF[] er = new PointF[] {
                                new PointF(l + iw * 0.08f, cy + ih * 0.13f),
                                new PointF(cx + iw * 0.03f, t + ih * 0.04f),
                                new PointF(rr - iw * 0.05f, t + ih * 0.35f),
                                new PointF(cx - iw * 0.08f, bb - ih * 0.02f)
                            };
                            g.DrawPolygon(p, er);
                            g.DrawLine(p, l + iw * 0.03f, bb - ih * 0.02f, cx + iw * 0.17f, bb - ih * 0.02f);
                            g.DrawLine(p, l + iw * 0.29f, cy + ih * 0.24f, cx + iw * 0.15f, bb - ih * 0.02f);
                            if (kind == AppIcon.PixelEraser)
                            {
                                float q = Math.Max(2f, unit * 0.115f);
                                float px = rr - q * 2.05f;
                                float py = bb - q * 1.95f;
                                g.FillRectangle(b, px, py + q, q, q);
                                g.FillRectangle(b, px + q, py, q, q);
                                g.DrawRectangle(p, px, py, q, q);
                                g.DrawRectangle(p, px + q, py + q, q, q);
                            }
                        }
                        break;

                    case AppIcon.Crop:
                        {
                            float x1 = l + iw * 0.28f;
                            float y1 = t + ih * 0.08f;
                            float x2 = rr - iw * 0.10f;
                            float y2 = bb - ih * 0.28f;
                            g.DrawLine(p, x1, t, x1, y2);
                            g.DrawLine(p, x1, y2, rr, y2);
                            g.DrawLine(p, l, y1, x2, y1);
                            g.DrawLine(p, x2, y1, x2, bb);
                        }
                        break;

                    case AppIcon.RotateLeft:
                        DrawRotateIcon(g, p, l, t, rr, bb, false);
                        break;
                    case AppIcon.RotateRight:
                        DrawRotateIcon(g, p, l, t, rr, bb, true);
                        break;
                    case AppIcon.Undo:
                        DrawUndoRedoIcon(g, p, l, t, rr, bb, false);
                        break;
                    case AppIcon.Redo:
                        DrawUndoRedoIcon(g, p, l, t, rr, bb, true);
                        break;

                    case AppIcon.Copy:
                        {
                            RectangleF back = new RectangleF(l + iw * 0.27f, t + ih * 0.03f, iw * 0.66f, ih * 0.66f);
                            RectangleF front = new RectangleF(l + iw * 0.03f, t + ih * 0.28f, iw * 0.66f, ih * 0.66f);
                            g.DrawRectangle(p, back.X, back.Y, back.Width, back.Height);
                            g.DrawRectangle(p, front.X, front.Y, front.Width, front.Height);
                        }
                        break;

                    case AppIcon.Save:
                        {
                            RectangleF body = new RectangleF(l + iw * 0.05f, t + ih * 0.02f, iw * 0.90f, ih * 0.94f);
                            g.DrawRectangle(p, body.X, body.Y, body.Width, body.Height);
                            g.DrawRectangle(p, l + iw * 0.23f, t + ih * 0.04f, iw * 0.47f, ih * 0.26f);
                            g.DrawRectangle(p, l + iw * 0.22f, cy + ih * 0.11f, iw * 0.56f, ih * 0.28f);
                        }
                        break;

                    case AppIcon.Print:
                        {
                            RectangleF paper = new RectangleF(l + iw * 0.25f, t, iw * 0.50f, ih * 0.38f);
                            RectangleF body = new RectangleF(l + iw * 0.06f, t + ih * 0.30f, iw * 0.88f, ih * 0.43f);
                            RectangleF output = new RectangleF(l + iw * 0.21f, t + ih * 0.57f, iw * 0.58f, ih * 0.38f);
                            g.DrawRectangle(p, paper.X, paper.Y, paper.Width, paper.Height);
                            g.DrawRectangle(p, body.X, body.Y, body.Width, body.Height);
                            g.DrawRectangle(p, output.X, output.Y, output.Width, output.Height);
                            float dot = Math.Max(1.6f, unit * 0.06f);
                            g.FillEllipse(b, body.Right - iw * 0.15f - dot * 0.5f, body.Top + ih * 0.16f - dot * 0.5f, dot, dot);
                        }
                        break;

                    case AppIcon.Settings:
                        DrawGear(g, p, l, t, rr, bb);
                        break;

                    case AppIcon.Timer:
                        {
                            float d = Math.Min(iw, ih) * 0.76f;
                            RectangleF clock = new RectangleF(cx - d * 0.5f, cy - d * 0.41f, d, d);
                            g.DrawEllipse(p, clock);
                            g.DrawLine(p, cx, clock.Top, cx, t);
                            g.DrawLine(p, cx - d * 0.13f, t, cx + d * 0.13f, t);
                            g.DrawLine(p, cx, cy - d * 0.01f, cx, cy - d * 0.24f);
                            g.DrawLine(p, cx, cy - d * 0.01f, cx + d * 0.20f, cy + d * 0.10f);
                        }
                        break;

                    case AppIcon.FlipHorizontal:
                        DrawFlipIcon(g, p, l, t, rr, bb, true);
                        break;
                    case AppIcon.FlipVertical:
                        DrawFlipIcon(g, p, l, t, rr, bb, false);
                        break;
                }
            }
        }

        private static void DrawCaptureCorners(Graphics g, Pen p, float l, float t, float rr, float bb)
        {
            float sx = (rr - l) * 0.27f;
            float sy = (bb - t) * 0.27f;
            g.DrawLine(p, l, t + sy, l, t); g.DrawLine(p, l, t, l + sx, t);
            g.DrawLine(p, rr - sx, t, rr, t); g.DrawLine(p, rr, t, rr, t + sy);
            g.DrawLine(p, l, bb - sy, l, bb); g.DrawLine(p, l, bb, l + sx, bb);
            g.DrawLine(p, rr - sx, bb, rr, bb); g.DrawLine(p, rr, bb - sy, rr, bb);
        }

        private static void DrawArrowHead(Graphics g, Pen p, PointF tip, PointF from, float size)
        {
            float dx = tip.X - from.X;
            float dy = tip.Y - from.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f) return;
            dx /= len; dy /= len;
            float px = -dy; float py = dx;
            float backX = tip.X - dx * size;
            float backY = tip.Y - dy * size;
            float wing = size * 0.52f;
            g.DrawLine(p, tip, new PointF(backX + px * wing, backY + py * wing));
            g.DrawLine(p, tip, new PointF(backX - px * wing, backY - py * wing));
        }

        private static PointF EllipsePoint(RectangleF r, float degrees)
        {
            double a = degrees * Math.PI / 180.0;
            return new PointF(
                r.Left + r.Width * 0.5f + (float)Math.Cos(a) * r.Width * 0.5f,
                r.Top + r.Height * 0.5f + (float)Math.Sin(a) * r.Height * 0.5f);
        }

        private static void DrawArcArrow(Graphics g, Pen p, RectangleF arc, float start, float sweep, float headSize)
        {
            g.DrawArc(p, arc, start, sweep);
            float end = start + sweep;
            float backAngle = end - (sweep >= 0f ? 12f : -12f);
            PointF tip = EllipsePoint(arc, end);
            PointF from = EllipsePoint(arc, backAngle);
            DrawArrowHead(g, p, tip, from, headSize);
        }

        private static void DrawUndoRedoIcon(Graphics g, Pen p, float l, float t, float rr, float bb, bool right)
        {
            float iw = rr - l;
            float ih = bb - t;
            float cy = (t + bb) * 0.5f;
            float size = Math.Min(iw, ih) * 0.20f;

            if (!right)
            {
                PointF tip = new PointF(l + iw * 0.02f, cy - ih * 0.03f);
                PointF join = new PointF(l + iw * 0.33f, cy - ih * 0.03f);
                g.DrawLine(p, tip, join);
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddBezier(join,
                        new PointF(l + iw * 0.38f, t + ih * 0.12f),
                        new PointF(rr - iw * 0.10f, t + ih * 0.10f),
                        new PointF(rr - iw * 0.04f, bb - ih * 0.13f));
                    g.DrawPath(p, path);
                }
                DrawArrowHead(g, p, tip, join, size);
            }
            else
            {
                PointF tip = new PointF(rr - iw * 0.02f, cy - ih * 0.03f);
                PointF join = new PointF(rr - iw * 0.33f, cy - ih * 0.03f);
                g.DrawLine(p, tip, join);
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddBezier(join,
                        new PointF(rr - iw * 0.38f, t + ih * 0.12f),
                        new PointF(l + iw * 0.10f, t + ih * 0.10f),
                        new PointF(l + iw * 0.04f, bb - ih * 0.13f));
                    g.DrawPath(p, path);
                }
                DrawArrowHead(g, p, tip, join, size);
            }
        }

        private static void DrawRotateIcon(Graphics g, Pen p, float l, float t, float rr, float bb, bool right)
        {
            float iw = rr - l;
            float ih = bb - t;
            RectangleF arc = new RectangleF(l + iw * 0.08f, t + ih * 0.08f, iw * 0.84f, ih * 0.84f);
            float size = Math.Min(iw, ih) * 0.18f;

            // A nearly complete circular arrow is the standard rotation metaphor.
            // Keep the center empty so it stays clean at 25px and remains clearly
            // different from the short hook used for Undo/Redo.
            if (right)
                DrawArcArrow(g, p, arc, 110f, 280f, size);
            else
                DrawArcArrow(g, p, arc, 70f, -280f, size);
        }

        private static void DrawEyedropper(Graphics g, Pen p, float l, float t, float rr, float bb)
        {
            float iw = rr - l;
            float ih = bb - t;

            // Classic pipette silhouette: pointed tip, parallel barrel and
            // rectangular bulb/cap. No circle, so it cannot read as a magnifier.
            PointF tip = new PointF(l + iw * 0.06f, bb - ih * 0.04f);
            PointF lo1 = new PointF(l + iw * 0.20f, bb - ih * 0.24f);
            PointF lo2 = new PointF(l + iw * 0.34f, bb - ih * 0.10f);
            PointF hi1 = new PointF(rr - iw * 0.29f, t + ih * 0.25f);
            PointF hi2 = new PointF(rr - iw * 0.15f, t + ih * 0.39f);

            g.DrawLine(p, tip, lo1);
            g.DrawLine(p, tip, lo2);
            g.DrawLine(p, lo1, hi1);
            g.DrawLine(p, lo2, hi2);
            g.DrawLine(p, hi1, hi2);

            PointF top1 = new PointF(rr - iw * 0.16f, t + ih * 0.02f);
            PointF top2 = new PointF(rr - iw * 0.02f, t + ih * 0.16f);
            g.DrawLine(p, hi1, top1);
            g.DrawLine(p, hi2, top2);
            g.DrawLine(p, top1, top2);

            // Shoulder line makes the bulb unmistakable at small sizes.
            g.DrawLine(p,
                new PointF(hi1.X - iw * 0.06f, hi1.Y - ih * 0.06f),
                new PointF(hi2.X + iw * 0.06f, hi2.Y + ih * 0.06f));
        }

        private static void DrawGear(Graphics g, Pen p, float l, float t, float rr, float bb)
        {
            float cx = (l + rr) * 0.5f;
            float cy = (t + bb) * 0.5f;
            float ro = Math.Min(rr - l, bb - t) * 0.49f;
            float ri = ro * 0.73f;

            // Six broad teeth remain readable at 25px. The previous 8-tooth
            // narrow polygon looked decorative rather than like a settings cog.
            PointF[] pts = new PointF[24];
            for (int i = 0; i < 6; i++)
            {
                double baseA = -Math.PI / 2.0 + i * Math.PI / 3.0;
                double a0 = baseA - 0.29;
                double a1 = baseA - 0.13;
                double a2 = baseA + 0.13;
                double a3 = baseA + 0.29;
                pts[i * 4] = new PointF(cx + (float)Math.Cos(a0) * ri, cy + (float)Math.Sin(a0) * ri);
                pts[i * 4 + 1] = new PointF(cx + (float)Math.Cos(a1) * ro, cy + (float)Math.Sin(a1) * ro);
                pts[i * 4 + 2] = new PointF(cx + (float)Math.Cos(a2) * ro, cy + (float)Math.Sin(a2) * ro);
                pts[i * 4 + 3] = new PointF(cx + (float)Math.Cos(a3) * ri, cy + (float)Math.Sin(a3) * ri);
            }
            g.DrawPolygon(p, pts);
            float hole = ro * 0.31f;
            g.DrawEllipse(p, cx - hole, cy - hole, hole * 2f, hole * 2f);
        }

        private static void DrawFlipIcon(Graphics g, Pen p, float l, float t, float rr, float bb, bool horizontal)
        {
            float iw = rr - l;
            float ih = bb - t;
            float cx = (l + rr) * 0.5f;
            float cy = (t + bb) * 0.5f;
            using (Pen dp = new Pen(p.Color, Math.Max(1f, p.Width * 0.64f)))
            {
                dp.DashStyle = DashStyle.Dot;
                dp.StartCap = LineCap.Round;
                dp.EndCap = LineCap.Round;
                if (horizontal) g.DrawLine(dp, cx, t, cx, bb);
                else g.DrawLine(dp, l, cy, rr, cy);
            }

            if (horizontal)
            {
                PointF[] leftTri = new PointF[] {
                    new PointF(l + iw * 0.07f, cy),
                    new PointF(cx - iw * 0.13f, t + ih * 0.15f),
                    new PointF(cx - iw * 0.13f, bb - ih * 0.15f)
                };
                PointF[] rightTri = new PointF[] {
                    new PointF(rr - iw * 0.07f, cy),
                    new PointF(cx + iw * 0.13f, t + ih * 0.15f),
                    new PointF(cx + iw * 0.13f, bb - ih * 0.15f)
                };
                g.DrawPolygon(p, leftTri);
                g.DrawPolygon(p, rightTri);
            }
            else
            {
                PointF[] topTri = new PointF[] {
                    new PointF(cx, t + ih * 0.07f),
                    new PointF(l + iw * 0.15f, cy - ih * 0.13f),
                    new PointF(rr - iw * 0.15f, cy - ih * 0.13f)
                };
                PointF[] bottomTri = new PointF[] {
                    new PointF(cx, bb - ih * 0.07f),
                    new PointF(l + iw * 0.15f, cy + ih * 0.13f),
                    new PointF(rr - iw * 0.15f, cy + ih * 0.13f)
                };
                g.DrawPolygon(p, topTri);
                g.DrawPolygon(p, bottomTri);
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
