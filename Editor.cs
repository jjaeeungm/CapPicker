using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CapPicker
{
    internal enum EditorTool
    {
        None,
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
        SampleColor
    }

    internal sealed class ToolStyle
    {
        public Color Color;
        public int Width;
        public int TextSize;
        public string FontName;
        public bool Fill;
        public bool TextBold;

        public ToolStyle(Color color, int width, int textSize)
            : this(color, width, textSize, L10n.DefaultFontName)
        {
        }

        public ToolStyle(Color color, int width, int textSize, string fontName)
        {
            Color = color;
            Width = width;
            TextSize = textSize;
            FontName = String.IsNullOrEmpty(fontName) ? L10n.DefaultFontName : fontName;
            Fill = false;
            TextBold = false;
        }

        public ToolStyle(Color color, int width, int textSize, string fontName, bool fill)
            : this(color, width, textSize, fontName)
        {
            Fill = fill;
        }

        public ToolStyle(Color color, int width, int textSize, string fontName, bool fill, bool textBold)
            : this(color, width, textSize, fontName, fill)
        {
            TextBold = textBold;
        }
    }

    internal sealed class EditorState
    {
        public byte[] Current;
        public byte[] Clean;
        public Rectangle ImageBounds;
    }

    internal sealed class CanvasPointerInfo
    {
        public Point Point;
        public Color Color;
        public bool Transparent;
        public bool InsideImage;
        public Size DragSize;
        public Size ImageSize;
        public int ZoomPercent;
    }

    internal sealed class ImeCaptureTextBox : TextBox
    {
        private const int WM_IME_COMPOSITION = 0x010F;
        private const int WM_IME_ENDCOMPOSITION = 0x010E;
        private const uint GCS_COMPSTR = 0x0008;

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll", CharSet = CharSet.Unicode, EntryPoint = "ImmGetCompositionStringW")]
        private static extern int ImmGetCompositionString(
            IntPtr hIMC, uint dwIndex, [Out] byte[] lpBuf, uint dwBufLen);

        public string CompositionText { get; private set; }
        public event EventHandler CompositionChanged;

        public ImeCaptureTextBox()
        {
            CompositionText = String.Empty;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_IME_COMPOSITION)
            {
                CompositionText = ReadCompositionString();
                if (CompositionChanged != null) CompositionChanged(this, EventArgs.Empty);
            }
            else if (m.Msg == WM_IME_ENDCOMPOSITION)
            {
                CompositionText = String.Empty;
                if (CompositionChanged != null) CompositionChanged(this, EventArgs.Empty);
            }
        }

        private string ReadCompositionString()
        {
            IntPtr imc = ImmGetContext(Handle);
            if (imc == IntPtr.Zero) return String.Empty;
            try
            {
                int byteLen = ImmGetCompositionString(imc, GCS_COMPSTR, null, 0);
                if (byteLen <= 0) return String.Empty;
                byte[] buffer = new byte[byteLen];
                int copied = ImmGetCompositionString(imc, GCS_COMPSTR, buffer, (uint)buffer.Length);
                if (copied <= 0) return String.Empty;
                return Encoding.Unicode.GetString(buffer, 0, copied);
            }
            finally
            {
                ImmReleaseContext(Handle, imc);
            }
        }
    }

    internal sealed class PaintTextEditorHost : Control
    {
        private const int GripSize = 9;
        private const int MoveBand = 12;
        private enum HostDragMode { None, Move, ResizeNW, ResizeNE, ResizeSW, ResizeSE }
        private HostDragMode dragMode = HostDragMode.None;
        private Point dragStartScreen;
        private Rectangle dragStartBounds;
        private Bitmap backgroundSnapshot;
        private Font previewFont;
        private Color textColor = Color.FromArgb(220, 50, 47);
        private Color borderColor = Color.FromArgb(220, 50, 47);
        private int contentInset = 2;

        public readonly ImeCaptureTextBox Editor;
        public event EventHandler UserBoundsEditCompleted;
        public event EventHandler CommitRequested;
        public event EventHandler CancelRequested;

        public PaintTextEditorHost()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            BackColor = AppTheme.Workspace;
            TabStop = false;
            MinimumSize = new Size(24, 24);

            Editor = new ImeCaptureTextBox();
            Editor.BorderStyle = BorderStyle.None;
            Editor.Multiline = true;
            Editor.WordWrap = true;
            Editor.AcceptsReturn = true;
            Editor.AcceptsTab = false;
            Editor.ShortcutsEnabled = true;
            Editor.AutoSize = false;
            Editor.ScrollBars = ScrollBars.None;
            Editor.ImeMode = ImeMode.NoControl;
            Editor.Size = new Size(2, 2);
            Editor.Location = new Point(2, 2);
            Editor.TabStop = true;
            Controls.Add(Editor);

            Editor.TextChanged += delegate { RefreshPreviewAndCaret(); };
            Editor.CompositionChanged += delegate { RefreshPreviewAndCaret(); };
            Editor.KeyUp += delegate { RefreshPreviewAndCaret(); };
            Editor.GotFocus += delegate { Invalidate(); };
            Editor.LostFocus += delegate { Invalidate(); };
            Editor.KeyDown += EditorKeyDown;
        }

        public Color TextColor
        {
            get { return textColor; }
            set { textColor = value; borderColor = value; Invalidate(); }
        }

        public Font PreviewFont
        {
            get { return previewFont; }
            set
            {
                previewFont = value;
                if (value != null && Editor != null && !Editor.IsDisposed)
                    Editor.Font = value;
                RefreshPreviewAndCaret();
            }
        }

        public void SetContentInset(int pixels)
        {
            contentInset = Math.Max(1, pixels);
            RefreshPreviewAndCaret();
        }

        public void SetBackgroundSnapshot(Bitmap snapshot)
        {
            Bitmap old = backgroundSnapshot;
            backgroundSnapshot = snapshot;
            if (old != null)
            {
                try { old.Dispose(); } catch { }
            }
            Invalidate();
        }

        public string GetDisplayText(out int caretIndex)
        {
            string committed = Editor == null || Editor.IsDisposed ? String.Empty : (Editor.Text ?? String.Empty);
            string composition = Editor == null || Editor.IsDisposed ? String.Empty : (Editor.CompositionText ?? String.Empty);
            int selection = Editor == null || Editor.IsDisposed ? committed.Length : Editor.SelectionStart;
            selection = Math.Max(0, Math.Min(committed.Length, selection));

            if (!String.IsNullOrEmpty(composition))
            {
                // Win32 EDIT는 한글 조합 중 현재 조합문자를 Text에도 반영하는 경우가 있습니다.
                // 이때 GCS_COMPSTR까지 다시 삽입하면 '가가'처럼 순간적으로 두 글자로 보입니다.
                bool alreadyBefore = selection >= composition.Length &&
                    String.CompareOrdinal(committed, selection - composition.Length, composition, 0, composition.Length) == 0;
                bool alreadyAt = selection + composition.Length <= committed.Length &&
                    String.CompareOrdinal(committed, selection, composition, 0, composition.Length) == 0;

                if (alreadyBefore || alreadyAt)
                {
                    caretIndex = selection;
                    return committed;
                }

                // Text에 아직 조합문자가 들어오지 않은 IME에서만 미리보기용으로 추가합니다.
                caretIndex = selection + composition.Length;
                return committed.Insert(selection, composition);
            }

            caretIndex = selection;
            return committed;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        public void FocusEditor()
        {
            if (Editor == null || Editor.IsDisposed) return;
            try
            {
                if (!Editor.IsHandleCreated) Editor.CreateControl();
                Editor.Select();
                Editor.Focus();
                if (Editor.IsHandleCreated) SetFocus(Editor.Handle);
            }
            catch { }
            Invalidate();
        }

        // MouseDown 중에 동적으로 Host가 생기면 Win32가 그 클릭의 포커스 처리를
        // 뒤늦게 마무리하면서 EDIT 포커스를 다시 빼앗을 수 있습니다.
        // 따라서 최초 클릭의 MouseUp 메시지가 완전히 반환된 다음에만 포커스를 확정합니다.
        public void FocusEditorAfterCurrentMouseMessage()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (!IsDisposed && Editor != null && !Editor.IsDisposed)
                        FocusEditor();
                });
            }
            catch { }
        }

        private void EditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                if (CancelRequested != null) CancelRequested(this, EventArgs.Empty);
                return;
            }

            // 그림판처럼 Enter는 줄바꿈입니다. Ctrl+Enter만 즉시 확정합니다.
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                if (CommitRequested != null) CommitRequested(this, EventArgs.Empty);
            }
        }

        private Rectangle GetContentRectangle()
        {
            int inset = Math.Max(1, contentInset);
            return new Rectangle(
                inset,
                inset,
                Math.Max(1, ClientSize.Width - inset * 2),
                Math.Max(1, ClientSize.Height - inset * 2));
        }

        private static StringFormat CreatePaintTextFormat()
        {
            StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces |
                                  StringFormatFlags.LineLimit |
                                  StringFormatFlags.NoClip;
            format.Trimming = StringTrimming.Word;
            format.Alignment = StringAlignment.Near;
            format.LineAlignment = StringAlignment.Near;
            return format;
        }

        private PointF MeasureCaretPoint(Graphics g, string text, int caretIndex, Rectangle content)
        {
            float x = content.Left;
            float y = content.Top;
            if (previewFont == null || String.IsNullOrEmpty(text) || caretIndex <= 0)
                return new PointF(x, y);

            int index = Math.Max(0, Math.Min(text.Length, caretIndex));
            string prefix = text.Substring(0, index);
            float lineHeight = Math.Max(1f, previewFont.GetHeight(g));

            // 줄바꿈 직후라면 다음 줄의 왼쪽에서 caret을 표시합니다.
            if (prefix.EndsWith("\n", StringComparison.Ordinal))
            {
                using (StringFormat f = CreatePaintTextFormat())
                {
                    SizeF all = g.MeasureString(prefix + "M", previewFont, content.Size, f);
                    y = content.Top + Math.Max(0f, all.Height - lineHeight);
                }
                return new PointF(content.Left, y);
            }

            int rangeStart = Math.Max(0, index - 1);
            using (StringFormat f = CreatePaintTextFormat())
            {
                f.SetMeasurableCharacterRanges(new CharacterRange[] { new CharacterRange(rangeStart, 1) });
                Region[] regions = null;
                try
                {
                    regions = g.MeasureCharacterRanges(text, previewFont, content, f);
                    if (regions != null && regions.Length > 0)
                    {
                        RectangleF b = regions[0].GetBounds(g);
                        return new PointF(Math.Max(content.Left, b.Right), Math.Max(content.Top, b.Top));
                    }
                }
                catch { }
                finally
                {
                    if (regions != null)
                    {
                        for (int i = 0; i < regions.Length; i++)
                            if (regions[i] != null) regions[i].Dispose();
                    }
                }
            }

            return new PointF(content.Left, content.Top);
        }

        private void RefreshPreviewAndCaret()
        {
            if (IsDisposed) return;
            Invalidate();
            if (!IsHandleCreated || previewFont == null || Editor == null || Editor.IsDisposed) return;

            try
            {
                using (Graphics g = CreateGraphics())
                {
                    int caretIndex;
                    string text = GetDisplayText(out caretIndex);
                    PointF caret = MeasureCaretPoint(g, text, caretIndex, GetContentRectangle());
                    int x = Math.Max(0, Math.Min(Math.Max(0, ClientSize.Width - 2), (int)Math.Round(caret.X + 3)));
                    int y = Math.Max(0, Math.Min(Math.Max(0, ClientSize.Height - 2), (int)Math.Round(caret.Y + 3)));
                    Editor.Location = new Point(x, y);
                    Editor.Size = new Size(2, 2);
                }
            }
            catch { }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            if (backgroundSnapshot != null)
            {
                pevent.Graphics.InterpolationMode = ClientSize.Width >= backgroundSnapshot.Width
                    ? InterpolationMode.NearestNeighbor
                    : InterpolationMode.HighQualityBicubic;
                pevent.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                pevent.Graphics.DrawImage(backgroundSnapshot, ClientRectangle,
                    new Rectangle(Point.Empty, backgroundSnapshot.Size), GraphicsUnit.Pixel);
            }
            else
            {
                pevent.Graphics.Clear(BackColor);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle content = GetContentRectangle();
            int caretIndex;
            string text = GetDisplayText(out caretIndex);

            if (previewFont != null && !String.IsNullOrEmpty(text))
            {
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (SolidBrush brush = new SolidBrush(textColor))
                using (StringFormat format = CreatePaintTextFormat())
                    e.Graphics.DrawString(text, previewFont, brush, content, format);
            }

            // 편집 중 입력선은 실제 숨은 EDIT의 순간적인 Focus 상태와 분리해 항상 표시합니다.
            // WinForms가 동일 클릭의 포커스 처리를 마무리하면서 EDIT 포커스가 잠깐 흔들려도
            // 사용자가 보는 입력선이 사라지지 않아 그림판처럼 편집 상태가 명확합니다.
            if (previewFont != null && Editor != null && !Editor.IsDisposed)
            {
                PointF caret = MeasureCaretPoint(e.Graphics, text, caretIndex, content);
                float h = Math.Max(4f, previewFont.GetHeight(e.Graphics));
                using (Pen dark = new Pen(Color.FromArgb(55, 55, 55), 2f))
                using (Pen light = new Pen(textColor, 1f))
                {
                    e.Graphics.DrawLine(dark, caret.X, caret.Y, caret.X, Math.Min(content.Bottom, caret.Y + h));
                    e.Graphics.DrawLine(light, caret.X, caret.Y, caret.X, Math.Min(content.Bottom, caret.Y + h));
                }
            }

            Rectangle r = ClientRectangle;
            if (r.Width > 1 && r.Height > 1)
            {
                r.Width -= 1;
                r.Height -= 1;
                using (Pen shadow = new Pen(Color.FromArgb(225, 255, 255, 255), 2f))
                using (Pen border = new Pen(Color.FromArgb(235, borderColor), 1f))
                {
                    shadow.DashStyle = DashStyle.Dot;
                    border.DashStyle = DashStyle.Dot;
                    e.Graphics.DrawRectangle(shadow, r);
                    e.Graphics.DrawRectangle(border, r);
                }

                DrawGrip(e.Graphics, new Rectangle(0, 0, GripSize, GripSize));
                DrawGrip(e.Graphics, new Rectangle(Math.Max(0, Width - GripSize), 0, GripSize, GripSize));
                DrawGrip(e.Graphics, new Rectangle(0, Math.Max(0, Height - GripSize), GripSize, GripSize));
                DrawGrip(e.Graphics, new Rectangle(Math.Max(0, Width - GripSize), Math.Max(0, Height - GripSize), GripSize, GripSize));
            }
        }

        private static void DrawGrip(Graphics g, Rectangle r)
        {
            Rectangle inner = r;
            inner.Inflate(-2, -2);
            if (inner.Width < 1 || inner.Height < 1) return;
            using (SolidBrush fill = new SolidBrush(Color.White))
            using (Pen edge = new Pen(Color.FromArgb(72, 76, 84)))
            {
                g.FillRectangle(fill, inner);
                g.DrawRectangle(edge, inner);
            }
        }

        private HostDragMode HitDragMode(Point p)
        {
            bool left = p.X <= GripSize + 3;
            bool right = p.X >= Math.Max(0, Width - GripSize - 3);
            bool top = p.Y <= GripSize + 3;
            bool bottom = p.Y >= Math.Max(0, Height - GripSize - 3);

            if (left && top) return HostDragMode.ResizeNW;
            if (right && top) return HostDragMode.ResizeNE;
            if (left && bottom) return HostDragMode.ResizeSW;
            if (right && bottom) return HostDragMode.ResizeSE;

            // 그림판처럼 상단 테두리 영역을 끌면 텍스트 상자를 이동합니다.
            if (p.Y <= MoveBand) return HostDragMode.Move;
            return HostDragMode.None;
        }

        private static Cursor CursorForMode(HostDragMode mode)
        {
            if (mode == HostDragMode.Move) return Cursors.SizeAll;
            if (mode == HostDragMode.ResizeNW || mode == HostDragMode.ResizeSE) return Cursors.SizeNWSE;
            if (mode == HostDragMode.ResizeNE || mode == HostDragMode.ResizeSW) return Cursors.SizeNESW;
            return Cursors.IBeam;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            // 최초 클릭 위치 바로 아래에 Host가 생성되는 경우, 마우스가 Host로 진입한 뒤
            // 메시지 큐가 비었을 때 EDIT 포커스를 한 번 확정합니다.
            if (dragMode == HostDragMode.None) FocusEditorAfterCurrentMouseMessage();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            HostDragMode hit = HitDragMode(e.Location);
            if (hit != HostDragMode.None)
            {
                dragMode = hit;
                dragStartScreen = PointToScreen(e.Location);
                dragStartBounds = Bounds;
                Capture = true;
                Cursor = CursorForMode(hit);
                return;
            }

            FocusEditorAfterCurrentMouseMessage();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragMode != HostDragMode.None)
            {
                Point nowScreen = PointToScreen(e.Location);
                int dx = nowScreen.X - dragStartScreen.X;
                int dy = nowScreen.Y - dragStartScreen.Y;
                Rectangle b = dragStartBounds;

                if (dragMode == HostDragMode.Move)
                {
                    b.X += dx;
                    b.Y += dy;
                }
                else
                {
                    if (dragMode == HostDragMode.ResizeNW || dragMode == HostDragMode.ResizeSW)
                    {
                        int right = b.Right;
                        b.X += dx;
                        b.Width = right - b.X;
                    }
                    else
                    {
                        b.Width += dx;
                    }

                    if (dragMode == HostDragMode.ResizeNW || dragMode == HostDragMode.ResizeNE)
                    {
                        int bottom = b.Bottom;
                        b.Y += dy;
                        b.Height = bottom - b.Y;
                    }
                    else
                    {
                        b.Height += dy;
                    }

                    if (b.Width < MinimumSize.Width)
                    {
                        if (dragMode == HostDragMode.ResizeNW || dragMode == HostDragMode.ResizeSW)
                            b.X = b.Right - MinimumSize.Width;
                        b.Width = MinimumSize.Width;
                    }
                    if (b.Height < MinimumSize.Height)
                    {
                        if (dragMode == HostDragMode.ResizeNW || dragMode == HostDragMode.ResizeNE)
                            b.Y = b.Bottom - MinimumSize.Height;
                        b.Height = MinimumSize.Height;
                    }
                }

                Bounds = b;
                return;
            }
            Cursor = CursorForMode(HitDragMode(e.Location));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            // 최초 Canvas 클릭에서 Host가 생성되면 같은 클릭의 MouseUp이 새 Host로
            // 전달될 수 있습니다. 이 경우에도 마지막 메시지에서 EDIT에 포커스를 확정합니다.
            if (dragMode == HostDragMode.None)
            {
                FocusEditorAfterCurrentMouseMessage();
                return;
            }

            dragMode = HostDragMode.None;
            Capture = false;
            Cursor = Cursors.IBeam;
            if (UserBoundsEditCompleted != null) UserBoundsEditCompleted(this, EventArgs.Empty);
            FocusEditorAfterCurrentMouseMessage();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (backgroundSnapshot != null)
                {
                    try { backgroundSnapshot.Dispose(); } catch { }
                    backgroundSnapshot = null;
                }
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class EditorCanvas : Control
    {
        private const int CanvasMargin = 48;
        private const int MaxHistory = 8;
        private const int WM_IME_STARTCOMPOSITION = 0x010D;
        private const int WM_IME_COMPOSITION = 0x010F;
        private const int WM_IME_ENDCOMPOSITION = 0x010E;
        private const uint GCS_COMPSTR = 0x0008;

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);
        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        [DllImport("imm32.dll", CharSet = CharSet.Unicode, EntryPoint = "ImmGetCompositionStringW")]
        private static extern int ImmGetCompositionString(IntPtr hIMC, uint dwIndex, [Out] byte[] lpBuf, uint dwBufLen);

        private Bitmap current;
        private Bitmap clean;
        private Bitmap checkerTile;
        private TextureBrush checkerBrush;
        private Rectangle imageBounds;

        private readonly Stack<EditorState> undo = new Stack<EditorState>();
        private readonly Stack<EditorState> redo = new Stack<EditorState>();

        private EditorTool tool = EditorTool.None;
        private Color drawColor = Color.FromArgb(220, 50, 47);
        private int strokeWidth = 4;
        private int textSize = 24;
        private string fontName = L10n.DefaultFontName;
        private bool textBold;
        private string emojiGlyph = "😊";
        private int emojiSize = 48;

        private bool drawing;
        // With no editing tool selected, a plain left-drag acts as a lightweight ruler.
        // The measured size remains in the status bar after mouse-up until another
        // measurement starts or an editing tool is selected.
        private bool measuringDrag;
        private Size measuredDragSize = Size.Empty;
        private int lastPointerInfoTick = Int32.MinValue;
        private int lastPreviewInvalidateTick = Int32.MinValue;
        private const int NormalPointerInfoInterval = 16;
        private const int LowSpecPointerInfoInterval = 80;
        private const int NormalPreviewInterval = 16;
        private const int LowSpecPreviewInterval = 50;
        private readonly List<Point> strokePoints = new List<Point>();
        private Point start;
        private Point last;
        private Point now;

        private bool textEditing;
        private string textBuffer = String.Empty;
        private string compositionText = String.Empty;
        private Point textPoint;
        private Rectangle textEditRect;
        private PaintTextEditorHost textEditorHost;
        private ImeCaptureTextBox textEditor;
        private readonly List<Font> textSessionFonts = new List<Font>();
        private bool closingTextEditor;
        private bool pendingInitialTextFocus;
        private bool shapeFill;

        private float zoomFactor = 1.0f;
        private bool pointerInside;
        private Point hoverPoint;

        // Image-local color picker magnifier (eyedropper next to Copy).
        private static readonly int[] SamplePickerZoomLevels = new int[] { 6, 8, 10, 12, 16, 20, 24, 32, 40, 48, 64, 80, 96, 128 };
        private int samplePickerZoomIndex = 4;

        public event EventHandler HistoryChanged;
        public event EventHandler ZoomChanged;
        public event Action<CanvasPointerInfo> PointerInfoChanged;
        public event Action<Color, Point> ImageColorPicked;

        public EditorCanvas(Bitmap source)
        {
            if (source == null) throw new ArgumentNullException("source");

            current = new Bitmap(source.Width + CanvasMargin * 2, source.Height + CanvasMargin * 2, PixelFormat.Format32bppArgb);
            clean = new Bitmap(current.Width, current.Height, PixelFormat.Format32bppArgb);
            imageBounds = new Rectangle(CanvasMargin, CanvasMargin, source.Width, source.Height);
            InitializeCheckerBrush();

            using (Graphics g = Graphics.FromImage(current))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.DrawImageUnscaled(source, imageBounds.Left, imageBounds.Top);
            }
            using (Graphics g = Graphics.FromImage(clean))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.DrawImageUnscaled(source, imageBounds.Left, imageBounds.Top);
            }

            BackColor = AppTheme.Workspace;
            Cursor = Cursors.Default;
            DoubleBuffered = true;
            TabStop = true;
            ImeMode = ImeMode.NoControl;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);

            UpdateControlSize();

            MouseDown += CanvasMouseDown;
            MouseMove += CanvasMouseMove;
            MouseUp += CanvasMouseUp;
            MouseWheel += CanvasMouseWheel;
            MouseEnter += delegate
            {
                pointerInside = true;
                // 텍스트 편집 중에는 Canvas가 숨은 EDIT의 포커스를 빼앗지 않습니다.
                if (!textEditing) Focus();
                Invalidate();
            };
            MouseLeave += delegate { pointerInside = false; Invalidate(); };
        }

        public EditorTool Tool
        {
            get { return tool; }
            set
            {
                if (textEditing) CommitText();
                tool = value;
                measuringDrag = false;
                measuredDragSize = Size.Empty;
                if (tool == EditorTool.Text) Cursor = Cursors.IBeam;
                else if (tool == EditorTool.Crop || tool == EditorTool.SampleColor) Cursor = Cursors.Cross;
                else Cursor = Cursors.Default;
                Invalidate();
            }
        }

        public Color DrawColor
        {
            get { return drawColor; }
            set
            {
                drawColor = value;
                if (textEditing) UpdateTextEditorAppearance(true);
                Invalidate();
            }
        }

        public int StrokeWidth
        {
            get { return strokeWidth; }
            set { strokeWidth = Math.Max(1, Math.Min(128, value)); }
        }

        public int TextSize
        {
            get { return textSize; }
            set
            {
                textSize = Math.Max(10, Math.Min(120, value));
                if (textEditing) UpdateTextEditorAppearance(true);
                Invalidate();
            }
        }

        public string FontName
        {
            get { return fontName; }
            set
            {
                fontName = String.IsNullOrEmpty(value) ? L10n.DefaultFontName : value;
                if (textEditing) UpdateTextEditorAppearance(true);
                Invalidate();
            }
        }

        public bool TextBold
        {
            get { return textBold; }
            set
            {
                textBold = value;
                if (textEditing) UpdateTextEditorAppearance(true);
                Invalidate();
            }
        }

        public string EmojiGlyph
        {
            get { return emojiGlyph; }
            set { emojiGlyph = String.IsNullOrEmpty(value) ? "😊" : value; Invalidate(); }
        }

        public int EmojiSize
        {
            get { return emojiSize; }
            set { emojiSize = Math.Max(20, Math.Min(160, value)); Invalidate(); }
        }

        public bool ShapeFill
        {
            get { return shapeFill; }
            set { shapeFill = value; Invalidate(); }
        }

        public float ZoomFactor
        {
            get { return zoomFactor; }
        }

        public int ZoomPercent
        {
            get { return (int)Math.Round(zoomFactor * 100.0f); }
        }

        public Size CapturedImageSize
        {
            get { return imageBounds.Size; }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_SETCURSOR = 0x0020;
            if (m.Msg == WM_SETCURSOR && UsesThicknessCursor(tool))
            {
                // 브러시/도형/지우개 도구는 실제 픽셀 크기의 원형 프리뷰가 커서 역할을 합니다.
                Native.SetCursor(IntPtr.Zero);
                m.Result = new IntPtr(1);
                return;
            }
            base.WndProc(ref m);
        }

        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        public Bitmap ExportBitmap()
        {
            if (textEditing) CommitText();

            // v6: 이미지 밖 여백은 상호작용 시작점으로만 사용하며 저장 범위에는 포함하지 않습니다.
            Rectangle export = imageBounds;
            export.Intersect(new Rectangle(Point.Empty, current.Size));
            if (export.Width < 1 || export.Height < 1)
                export = new Rectangle(0, 0, Math.Max(1, current.Width), Math.Max(1, current.Height));

            return current.Clone(export, PixelFormat.Format32bppArgb);
        }

        public void SetZoomPercent(int percent)
        {
            int[] levels = new int[] { 25, 33, 50, 67, 80, 100, 125, 150, 175, 200, 250, 300, 400, 500, 600, 800, 1000 };
            int best = levels[0];
            int delta = Math.Abs(percent - best);
            for (int i = 1; i < levels.Length; i++)
            {
                int d = Math.Abs(percent - levels[i]);
                if (d < delta) { delta = d; best = levels[i]; }
            }
            zoomFactor = best / 100.0f;
            UpdateControlSize();
            if (textEditing) LayoutTextEditor();
            Invalidate();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            if (zoomFactor >= 1.0f)
                e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            else
                e.Graphics.InterpolationMode = AppSettings.LowSpecOptimization
                    ? InterpolationMode.Bilinear
                    : InterpolationMode.HighQualityBicubic;
            e.Graphics.ScaleTransform(zoomFactor, zoomFactor);

            // 바깥 상호작용 여백은 작업영역 색으로 유지하고, 실제 이미지 영역만 체크무늬/이미지를 표시합니다.
            DrawCheckerboard(e.Graphics, imageBounds);

            GraphicsState clipped = e.Graphics.Save();
            e.Graphics.SetClip(imageBounds);
            e.Graphics.DrawImageUnscaled(current, 0, 0);

            if (tool == EditorTool.Emoji && pointerInside)
                DrawEmojiStamp(e.Graphics, hoverPoint, true);

            if (drawing)
            {
                if ((tool == EditorTool.Pen || tool == EditorTool.Highlighter) && strokePoints.Count > 0)
                {
                    DrawStrokePreview(e.Graphics);
                }
                else if (tool == EditorTool.Rectangle ||
                         tool == EditorTool.Ellipse ||
                         tool == EditorTool.Arrow ||
                         tool == EditorTool.Check)
                {
                    using (Pen p = BuildPen(false))
                    {
                        p.DashStyle = DashStyle.Dash;
                        DrawShape(e.Graphics, tool, p, start, now, shapeFill);
                    }
                }
                else if (tool == EditorTool.Crop)
                {
                    Rectangle r = Normalize(start, now);
                    Rectangle img = imageBounds;
                    using (SolidBrush shade = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                    {
                        e.Graphics.FillRectangle(shade, new Rectangle(img.Left, img.Top, img.Width, Math.Max(0, r.Top - img.Top)));
                        e.Graphics.FillRectangle(shade, new Rectangle(img.Left, r.Bottom, img.Width, Math.Max(0, img.Bottom - r.Bottom)));
                        e.Graphics.FillRectangle(shade, new Rectangle(img.Left, r.Top, Math.Max(0, r.Left - img.Left), Math.Max(0, r.Height)));
                        e.Graphics.FillRectangle(shade, new Rectangle(r.Right, r.Top, Math.Max(0, img.Right - r.Right), Math.Max(0, r.Height)));
                    }
                    using (Pen p = new Pen(Color.White, Math.Max(1f, 2f / zoomFactor)))
                    {
                        p.DashStyle = DashStyle.Dash;
                        if (r.Width > 0 && r.Height > 0) e.Graphics.DrawRectangle(p, r);
                    }
                }
            }

            if (measuringDrag)
            {
                Rectangle measureRect = Normalize(start, now);
                measureRect.Intersect(imageBounds);
                if (measureRect.Width > 0 && measureRect.Height > 0)
                {
                    float outerWidth = Math.Max(1f, 3f / zoomFactor);
                    float innerWidth = Math.Max(1f, 1f / zoomFactor);
                    using (Pen shadow = new Pen(Color.FromArgb(190, 0, 0, 0), outerWidth))
                    using (Pen guide = new Pen(Color.FromArgb(245, 255, 255, 255), innerWidth))
                    {
                        shadow.DashStyle = DashStyle.Dash;
                        guide.DashStyle = DashStyle.Dash;
                        e.Graphics.DrawRectangle(shadow, measureRect);
                        e.Graphics.DrawRectangle(guide, measureRect);
                    }
                }
            }

            e.Graphics.Restore(clipped);

            using (Pen imageEdge = new Pen(Color.FromArgb(145, 120, 126, 136), Math.Max(1f / zoomFactor, 0.75f / zoomFactor)))
                e.Graphics.DrawRectangle(imageEdge, imageBounds);

            DrawImageSampleMagnifier(e.Graphics);
            DrawThicknessCursor(e.Graphics);
        }

        private void DrawImageSampleMagnifier(Graphics g)
        {
            if (tool != EditorTool.SampleColor || !pointerInside || !imageBounds.Contains(hoverPoint)) return;

            int pickerZoom = SamplePickerZoomLevels[samplePickerZoomIndex];
            float pickerUiScale = DpiUtil.Factor(this);
            int displayZoom = ScalePickerMetric(pickerUiScale, pickerZoom);
            bool lowSpecMode = AppSettings.LowSpecOptimization;
            int referenceTargetSize = lowSpecMode
                ? MagnifierHost.LowSpecWindowSize
                : (pickerZoom > 64 ? MagnifierHost.LargeWindowSize : MagnifierHost.BaseWindowSize);
            int targetSize = ScalePickerMetric(pickerUiScale, referenceTargetSize);

            // Scale both the magnifier window and each displayed source pixel by the same
            // UI factor. In low-spec mode the sampled grid is capped as well, limiting the
            // number of Bitmap.GetPixel calls made during rapid mouse movement.
            int cells = Math.Max(3, (int)Math.Round((double)targetSize / displayZoom));
            if (lowSpecMode && cells > 31) cells = 31;
            if ((cells & 1) == 0) cells++;
            int contentSize = cells * displayZoom;

            int cursorX = (int)Math.Round(hoverPoint.X * zoomFactor);
            int cursorY = (int)Math.Round(hoverPoint.Y * zoomFactor);
            int x = cursorX - contentSize / 2;
            int y = cursorY - contentSize / 2;

            // Same composition as the screen picker: magnifier + information card.
            // The local picker is drawn inside the editor, so keep both parts inside the canvas.
            int infoWidth = ScalePickerMetric(pickerUiScale, PickerInfoRenderer.ReferenceWidth);
            int infoHeight = ScalePickerMetric(pickerUiScale, PickerInfoRenderer.ReferenceHeight);
            int infoGap = ScalePickerMetric(pickerUiScale, 10);
            int totalWidth = contentSize + infoGap + infoWidth;

            if (x + totalWidth > ClientSize.Width - 4)
                x = cursorX - contentSize - infoGap - infoWidth / 2;
            x = Math.Max(4, Math.Min(x, Math.Max(4, ClientSize.Width - contentSize - 4)));
            y = Math.Max(4, Math.Min(y, Math.Max(4, ClientSize.Height - contentSize - 4)));

            int infoX = x + contentSize + infoGap;
            if (infoX + infoWidth > ClientSize.Width - 4)
                infoX = x - infoGap - infoWidth;
            if (infoX < 4)
                infoX = Math.Max(4, Math.Min(x + 12, ClientSize.Width - infoWidth - 4));
            int infoY = y + contentSize / 2 - infoHeight / 2;
            infoY = Math.Max(4, Math.Min(infoY, Math.Max(4, ClientSize.Height - infoHeight - 4)));

            GraphicsState state = g.Save();
            try
            {
                g.ResetTransform();
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                Rectangle outer = new Rectangle(x - 2, y - 2, contentSize + 4, contentSize + 4);
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(72, 0, 0, 0)))
                    g.FillRectangle(shadow, outer.X + 5, outer.Y + 5, outer.Width, outer.Height);
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(232, 235, 239)))
                    g.FillRectangle(bg, outer);

                int half = cells / 2;
                int startX = hoverPoint.X - half;
                int startY = hoverPoint.Y - half;
                // Reuse brushes across all magnifier cells. Creating thousands of GDI brushes
                // per mouse-move paint is much more expensive than changing SolidBrush.Color.
                using (SolidBrush checkerA = new SolidBrush(Color.White))
                using (SolidBrush checkerB = new SolidBrush(Color.FromArgb(226, 229, 233)))
                using (SolidBrush pixelBrush = new SolidBrush(Color.Transparent))
                {
                    for (int row = 0; row < cells; row++)
                    {
                        for (int col = 0; col < cells; col++)
                        {
                            Rectangle cell = new Rectangle(x + col * displayZoom, y + row * displayZoom, displayZoom, displayZoom);
                            int sx = startX + col;
                            int sy = startY + row;

                            g.FillRectangle((((row + col) & 1) == 0) ? checkerA : checkerB, cell);

                            if (sx >= imageBounds.Left && sx < imageBounds.Right &&
                                sy >= imageBounds.Top && sy < imageBounds.Bottom &&
                                sx >= 0 && sy >= 0 && sx < current.Width && sy < current.Height)
                            {
                                pixelBrush.Color = current.GetPixel(sx, sy);
                                g.FillRectangle(pixelBrush, cell);
                            }
                        }
                    }
                }

                // Same low-density semi-transparent pixel grid as the screen picker.
                using (Pen grid = new Pen(Color.FromArgb(88, 56, 60, 66), Math.Max(1f, ScalePickerMetric(pickerUiScale, 1))))
                {
                    grid.DashStyle = DashStyle.Dot;
                    for (int i = 0; i <= cells; i++)
                    {
                        int gx = x + i * displayZoom;
                        int gy = y + i * displayZoom;
                        g.DrawLine(grid, gx, y, gx, y + contentSize);
                        g.DrawLine(grid, x, gy, x + contentSize, gy);
                    }
                }

                int centerCellX = x + half * displayZoom;
                int centerCellY = y + half * displayZoom;
                Rectangle central = new Rectangle(centerCellX, centerCellY, displayZoom, displayZoom);
                using (Pen dark = new Pen(Color.FromArgb(18, 18, 18), ScalePickerMetric(pickerUiScale, 3)))
                using (Pen light = new Pen(Color.White, Math.Max(1f, ScalePickerMetric(pickerUiScale, 1))))
                {
                    g.DrawRectangle(dark, central);
                    Rectangle inner = central;
                    int innerInset = ScalePickerMetric(pickerUiScale, 2);
                    inner.Inflate(-innerInset, -innerInset);
                    if (inner.Width > 1 && inner.Height > 1) g.DrawRectangle(light, inner);
                }

                int centerX = centerCellX + displayZoom / 2;
                int centerY = centerCellY + displayZoom / 2;
                int crossGap = displayZoom / 2 + ScalePickerMetric(pickerUiScale, 5);
                int arm = Math.Max(ScalePickerMetric(pickerUiScale, 42), Math.Min(ScalePickerMetric(pickerUiScale, 72), displayZoom * 2 + ScalePickerMetric(pickerUiScale, 18)));
                using (Pen dark = new Pen(Color.FromArgb(16, 16, 16), ScalePickerMetric(pickerUiScale, 4)))
                using (Pen light = new Pen(Color.White, Math.Max(1f, ScalePickerMetric(pickerUiScale, 1))))
                {
                    DrawPickerCross(g, dark, centerX, centerY, crossGap, arm);
                    DrawPickerCross(g, light, centerX, centerY, crossGap, arm);
                }

                using (Pen border = new Pen(Color.FromArgb(42, 45, 50), ScalePickerMetric(pickerUiScale, 2)))
                    g.DrawRectangle(border, x - 1, y - 1, contentSize + 2, contentSize + 2);

                Rectangle badge = new Rectangle(
                    x + ScalePickerMetric(pickerUiScale, 10),
                    y + ScalePickerMetric(pickerUiScale, 10),
                    ScalePickerMetric(pickerUiScale, 56),
                    ScalePickerMetric(pickerUiScale, 26));
                using (SolidBrush badgeBg = new SolidBrush(Color.FromArgb(220, 38, 41, 46)))
                using (Font badgeFont = new Font("Segoe UI", ScalePickerMetric(pickerUiScale, 13), FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    g.FillRectangle(badgeBg, badge);
                    TextRenderer.DrawText(g, pickerZoom + "×", badgeFont, badge, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }

                Color selected = current.GetPixel(hoverPoint.X, hoverPoint.Y);
                Point imagePoint = new Point(hoverPoint.X - imageBounds.Left, hoverPoint.Y - imageBounds.Top);
                DrawImagePickerInfoCard(g, new Rectangle(infoX, infoY, Math.Max(1, infoWidth - 1), Math.Max(1, infoHeight - 1)), selected, imagePoint, pickerZoom, pickerUiScale);
            }
            finally
            {
                g.Restore(state);
            }
        }

        private static int ScalePickerMetric(float scale, int logical)
        {
            if (scale < 0.5f) scale = 0.5f;
            if (scale > 4f) scale = 4f;
            return Math.Max(1, (int)Math.Round(logical * scale));
        }

        private static void DrawPickerCross(Graphics g, Pen p, int centerX, int centerY, int gap, int arm)
        {
            g.DrawLine(p, centerX, centerY - gap - arm, centerX, centerY - gap);
            g.DrawLine(p, centerX, centerY + gap, centerX, centerY + gap + arm);
            g.DrawLine(p, centerX - gap - arm, centerY, centerX - gap, centerY);
            g.DrawLine(p, centerX + gap, centerY, centerX + gap + arm, centerY);
        }

        private static void DrawImagePickerInfoCard(Graphics g, Rectangle card, Color color, Point imagePoint, int zoom, float uiScale)
        {
            // Keep the in-image picker information card visually identical to the
            // standalone screen color picker. Only the coordinate value is image-local.
            PickerInfoRenderer.Draw(g, card, color, imagePoint, zoom, uiScale);
        }

        private void DrawThicknessCursor(Graphics g)
        {
            if (!pointerInside || !UsesThicknessCursor(tool)) return;

            int d = Math.Max(1, strokeWidth);
            float left = hoverPoint.X - d / 2f;
            float top = hoverPoint.Y - d / 2f;
            RectangleF r = new RectangleF(left, top, d, d);

            Color fill;
            if (tool == EditorTool.Eraser || tool == EditorTool.PixelEraser)
                fill = Color.FromArgb(34, 90, 96, 106);
            else if (tool == EditorTool.Highlighter)
                fill = Color.FromArgb(45, drawColor.R, drawColor.G, drawColor.B);
            else
                fill = Color.FromArgb(30, drawColor.R, drawColor.G, drawColor.B);

            using (SolidBrush b = new SolidBrush(fill))
            using (Pen dark = new Pen(Color.FromArgb(50, 54, 60), Math.Max(1f, 2f / zoomFactor)))
            using (Pen light = new Pen(Color.White, Math.Max(0.7f, 1f / zoomFactor)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(b, r);
                g.DrawEllipse(dark, r);
                RectangleF inner = r;
                inner.Inflate(-Math.Max(1f / zoomFactor, 0.6f), -Math.Max(1f / zoomFactor, 0.6f));
                if (inner.Width > 0 && inner.Height > 0) g.DrawEllipse(light, inner);
            }

            // 실제 두께 원형과 함께 위치를 정확히 잡을 수 있는 십자선 표시.
            float radius = d / 2f;
            float gap = radius + Math.Max(3f / zoomFactor, 2f);
            float arm = Math.Max(gap + 15f / zoomFactor, radius + 10f / zoomFactor);
            float cx = hoverPoint.X;
            float cy = hoverPoint.Y;
            using (Pen crossDark = new Pen(Color.FromArgb(34, 34, 34), Math.Max(1.5f / zoomFactor, 1f)))
            using (Pen crossLight = new Pen(Color.White, Math.Max(0.7f / zoomFactor, 0.7f)))
            {
                DrawCursorCross(g, crossDark, cx, cy, gap, arm);
                DrawCursorCross(g, crossLight, cx, cy, gap, arm);
            }
        }

        private static void DrawCursorCross(Graphics g, Pen p, float cx, float cy, float gap, float arm)
        {
            g.DrawLine(p, cx - arm, cy, cx - gap, cy);
            g.DrawLine(p, cx + gap, cy, cx + arm, cy);
            g.DrawLine(p, cx, cy - arm, cx, cy - gap);
            g.DrawLine(p, cx, cy + gap, cx, cy + arm);
        }

        private static bool UsesThicknessCursor(EditorTool t)
        {
            return t == EditorTool.Pen || t == EditorTool.Highlighter ||
                   t == EditorTool.Rectangle || t == EditorTool.Ellipse ||
                   t == EditorTool.Arrow || t == EditorTool.Check ||
                   t == EditorTool.Eraser || t == EditorTool.PixelEraser;
        }

        private bool NeedsHoverPreview()
        {
            return tool == EditorTool.Emoji || tool == EditorTool.SampleColor || UsesThicknessCursor(tool);
        }

        private void InitializeCheckerBrush()
        {
            checkerTile = new Bitmap(24, 24, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(checkerTile))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush gray = new SolidBrush(Color.FromArgb(232, 234, 237)))
            {
                g.FillRectangle(white, 0, 0, 24, 24);
                g.FillRectangle(gray, 12, 0, 12, 12);
                g.FillRectangle(gray, 0, 12, 12, 12);
            }
            checkerBrush = new TextureBrush(checkerTile, WrapMode.Tile);
        }

        private void DrawCheckerboard(Graphics g, Rectangle rect)
        {
            if (checkerBrush != null)
                g.FillRectangle(checkerBrush, rect);
            else
            {
                using (SolidBrush fallback = new SolidBrush(Color.White))
                    g.FillRectangle(fallback, rect);
            }
        }

        private void CanvasMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            Point p = ToLogicalPoint(e.Location);

            // 텍스트 편집 상자 밖의 Canvas를 클릭하면 그림판처럼 현재 입력을 확정합니다.
            // 같은 클릭으로 새 텍스트 상자를 만들지 않아 의도치 않은 연속 입력을 막습니다.
            if (textEditing && tool == EditorTool.Text)
            {
                CommitText();
                return;
            }

            // 텍스트 도구에서는 이 MouseDown에서 Canvas.Focus()를 호출하지 않습니다.
            // 동적으로 생성한 EDIT에 MouseUp 이후 포커스를 넘긴 뒤 Canvas의 지연 Focus가
            // 다시 포커스를 가져가던 간헐적 경합의 원인이었습니다.
            if (tool != EditorTool.Text) Focus();
            if (p.X < 0 || p.Y < 0 || p.X >= current.Width || p.Y >= current.Height) return;
            hoverPoint = p;
            pointerInside = true;
            if (tool == EditorTool.None)
            {
                if (!imageBounds.Contains(p)) return;
                start = now = p;
                measuringDrag = true;
                measuredDragSize = Size.Empty;
                RaisePointerInfo(p, true);
                RequestPreviewInvalidate(true);
                return;
            }

            if (tool == EditorTool.SampleColor)
            {
                if (imageBounds.Contains(p) && ImageColorPicked != null)
                {
                    Color c = current.GetPixel(p.X, p.Y);
                    Point imagePoint = new Point(p.X - imageBounds.Left, p.Y - imageBounds.Top);
                    ImageColorPicked(c, imagePoint);
                }
                return;
            }

            if (tool == EditorTool.Text)
            {
                BeginText(p);
                return;
            }

            if (tool == EditorTool.Emoji)
            {
                if (imageBounds.Contains(p))
                {
                    SaveUndo();
                    redo.Clear();
                    using (Graphics g = Graphics.FromImage(current))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.SetClip(imageBounds);
                        DrawEmojiStamp(g, p, false);
                        g.ResetClip();
                    }
                    RaiseHistoryChanged();
                    Invalidate();
                }
                return;
            }

            start = last = now = p;
            drawing = true;

            if (tool == EditorTool.Pen ||
                tool == EditorTool.Highlighter ||
                tool == EditorTool.Eraser ||
                tool == EditorTool.PixelEraser)
            {
                SaveUndo();
                redo.Clear();

                if (tool == EditorTool.Eraser)
                    EraseAnnotationAt(now);
                else if (tool == EditorTool.PixelEraser)
                    ErasePixelsBetween(now, now);
                else
                {
                    strokePoints.Clear();
                    strokePoints.Add(now);
                }
            }

            RaisePointerInfo(p, true);
            RequestPreviewInvalidate(true);
        }

        private void CanvasMouseMove(object sender, MouseEventArgs e)
        {
            Point p = ToLogicalPoint(e.Location);
            p = ClampPoint(p);
            hoverPoint = p;
            pointerInside = true;

            if (measuringDrag)
            {
                now = ClampToImageBounds(p);
                measuredDragSize = Normalize(start, now).Size;
                RaisePointerInfo(p, false);
                RequestPreviewInvalidate(false);
                return;
            }

            if (!drawing)
            {
                RaisePointerInfo(p, false);
                if (NeedsHoverPreview()) RequestPreviewInvalidate(false);
                return;
            }

            now = p;

            if (tool == EditorTool.Pen || tool == EditorTool.Highlighter)
            {
                if (last != now) strokePoints.Add(now);
                last = now;
            }
            else if (tool == EditorTool.Eraser)
            {
                EraseAnnotationBetween(last, now);
                last = now;
            }
            else if (tool == EditorTool.PixelEraser)
            {
                ErasePixelsBetween(last, now);
                last = now;
            }

            RaisePointerInfo(p, false);
            RequestPreviewInvalidate(false);
        }

        private void CanvasMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && textEditing && pendingInitialTextFocus)
            {
                pendingInitialTextFocus = false;
                if (textEditorHost != null && !textEditorHost.IsDisposed)
                    textEditorHost.FocusEditorAfterCurrentMouseMessage();
                return;
            }

            if (measuringDrag && e.Button == MouseButtons.Left)
            {
                Point pointer = ClampPoint(ToLogicalPoint(e.Location));
                now = ClampToImageBounds(pointer);
                measuredDragSize = Normalize(start, now).Size;
                measuringDrag = false;
                RaisePointerInfo(pointer, true);
                RequestPreviewInvalidate(true);
                return;
            }

            if (!drawing || e.Button != MouseButtons.Left) return;
            now = ClampPoint(ToLogicalPoint(e.Location));

            if (tool == EditorTool.Pen)
            {
                if (strokePoints.Count == 0) strokePoints.Add(now);
                using (Graphics g = Graphics.FromImage(current))
                {
                    g.SetClip(imageBounds);
                    DrawPenStroke(g, false);
                    g.ResetClip();
                }
                strokePoints.Clear();
            }
            else if (tool == EditorTool.Highlighter)
            {
                if (strokePoints.Count == 0) strokePoints.Add(now);
                CommitHighlighterStroke();
                strokePoints.Clear();
            }
            else if (tool == EditorTool.Rectangle ||
                     tool == EditorTool.Ellipse ||
                     tool == EditorTool.Arrow ||
                     tool == EditorTool.Check)
            {
                if (Distance(start, now) >= 2)
                {
                    SaveUndo();
                    redo.Clear();
                    using (Graphics g = Graphics.FromImage(current))
                    using (Pen p = BuildPen(false))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.SetClip(imageBounds);
                        DrawShape(g, tool, p, start, now, shapeFill);
                        g.ResetClip();
                    }
                }
            }
            else if (tool == EditorTool.Crop)
            {
                Rectangle r = Normalize(start, now);
                r.Intersect(imageBounds);
                if (r.Width >= 4 && r.Height >= 4)
                {
                    SaveUndo();
                    redo.Clear();
                    CropTo(r);
                }
            }

            drawing = false;
            RaisePointerInfo(now, true);
            RequestPreviewInvalidate(true);
            RaiseHistoryChanged();
        }

        private void CanvasMouseWheel(object sender, MouseEventArgs e)
        {
            // While the image-local eyedropper is active, both wheel and Ctrl+wheel belong
            // to the picker. Do not zoom or scroll the captured image underneath it.
            if (tool == EditorTool.SampleColor)
            {
                HandledMouseEventArgs handled = e as HandledMouseEventArgs;
                if (handled != null) handled.Handled = true;

                Point pickerPoint = ToLogicalPoint(e.Location);
                if (imageBounds.Contains(pickerPoint))
                {
                    int next = samplePickerZoomIndex + (e.Delta > 0 ? 1 : -1);
                    samplePickerZoomIndex = Math.Max(0, Math.Min(SamplePickerZoomLevels.Length - 1, next));
                    hoverPoint = pickerPoint;
                    pointerInside = true;
                    RaisePointerInfo(pickerPoint, true);
                    RequestPreviewInvalidate(true);
                    Update();
                }
                return;
            }

            if ((Control.ModifierKeys & Keys.Control) != Keys.Control) return;

            Point logicalAnchor = ToLogicalPoint(e.Location);
            ScrollableControl scroll = Parent as ScrollableControl;
            Point screenAnchorBefore = PointToScreen(e.Location);

            int percent = ZoomPercent;
            int direction = e.Delta > 0 ? 1 : -1;
            int[] levels = new int[] { 25, 33, 50, 67, 80, 100, 125, 150, 175, 200, 250, 300, 400, 500, 600, 800, 1000 };
            int index = 0;
            int best = Int32.MaxValue;
            for (int i = 0; i < levels.Length; i++)
            {
                int d = Math.Abs(levels[i] - percent);
                if (d < best) { best = d; index = i; }
            }
            index = Math.Max(0, Math.Min(levels.Length - 1, index + direction));
            SetZoomPercent(levels[index]);

            // 확대/축소 시 마우스 아래 픽셀이 화면에서 가능한 한 같은 위치에 남도록 스크롤을 보정합니다.
            if (scroll != null)
            {
                Point anchorAfterClient = new Point(
                    (int)Math.Round(logicalAnchor.X * zoomFactor),
                    (int)Math.Round(logicalAnchor.Y * zoomFactor));
                Point screenAnchorAfter = PointToScreen(anchorAfterClient);
                int dx = screenAnchorAfter.X - screenAnchorBefore.X;
                int dy = screenAnchorAfter.Y - screenAnchorBefore.Y;

                Point currentScroll = scroll.AutoScrollPosition;
                int sx = Math.Max(0, -currentScroll.X + dx);
                int sy = Math.Max(0, -currentScroll.Y + dy);
                scroll.AutoScrollPosition = new Point(sx, sy);
            }

            RaisePointerInfo(logicalAnchor, true);
        }

        private void DrawStrokePreview(Graphics g)
        {
            if (tool == EditorTool.Highlighter)
            {
                using (Pen p = BuildPen(true))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    p.LineJoin = LineJoin.Round;
                    if (strokePoints.Count == 1)
                    {
                        int d = Math.Max(4, (int)Math.Round(p.Width));
                        using (SolidBrush b = new SolidBrush(p.Color))
                            g.FillEllipse(b, strokePoints[0].X - d / 2, strokePoints[0].Y - d / 2, d, d);
                    }
                    else
                    {
                        g.DrawLines(p, strokePoints.ToArray());
                    }
                }
            }
            else
            {
                DrawPenStroke(g, false);
            }
        }

        private void DrawPenStroke(Graphics g, bool highlighter)
        {
            using (Pen p = BuildPen(highlighter))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingMode = CompositingMode.SourceOver;
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;

                if (strokePoints.Count == 1)
                {
                    Point pt = strokePoints[0];
                    int d = Math.Max(1, (int)Math.Round(p.Width));
                    using (SolidBrush b = new SolidBrush(p.Color))
                        g.FillEllipse(b, pt.X - d / 2, pt.Y - d / 2, d, d);
                }
                else if (strokePoints.Count > 1)
                    g.DrawLines(p, strokePoints.ToArray());
            }
        }

        private void CommitHighlighterStroke()
        {
            using (Bitmap overlay = new Bitmap(current.Width, current.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(overlay))
                using (Pen p = BuildPen(true))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    p.LineJoin = LineJoin.Round;

                    if (strokePoints.Count == 1)
                    {
                        Point pt = strokePoints[0];
                        int d = Math.Max(4, (int)Math.Round(p.Width));
                        using (SolidBrush b = new SolidBrush(p.Color))
                            g.FillEllipse(b, pt.X - d / 2, pt.Y - d / 2, d, d);
                    }
                    else
                    {
                        g.DrawLines(p, strokePoints.ToArray());
                    }
                }

                using (Graphics g = Graphics.FromImage(current))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.SetClip(imageBounds);
                    g.DrawImageUnscaled(overlay, 0, 0);
                    g.ResetClip();
                }
            }
        }

        private Pen BuildPen(bool highlighter)
        {
            if (highlighter)
                return new Pen(Color.FromArgb(76, drawColor.R, drawColor.G, drawColor.B), Math.Max(8, strokeWidth));

            return new Pen(Color.FromArgb(255, drawColor.R, drawColor.G, drawColor.B), Math.Max(1, strokeWidth));
        }

        private void EraseAnnotationBetween(Point a, Point b)
        {
            int diameter = Math.Max(8, strokeWidth);
            double dist = Distance(a, b);
            int steps = Math.Max(1, (int)Math.Ceiling(dist / Math.Max(2.0, diameter / 4.0)));
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                Point p = new Point(
                    (int)Math.Round(a.X + (b.X - a.X) * t),
                    (int)Math.Round(a.Y + (b.Y - a.Y) * t));
                EraseAnnotationAt(p);
            }
        }

        private void EraseAnnotationAt(Point p)
        {
            int diameter = Math.Max(8, strokeWidth);
            Rectangle r = new Rectangle(p.X - diameter / 2, p.Y - diameter / 2, diameter, diameter);
            r.Intersect(new Rectangle(Point.Empty, current.Size));
            if (r.Width < 1 || r.Height < 1) return;

            using (Graphics g = Graphics.FromImage(current))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(clean, r, r, GraphicsUnit.Pixel);
            }
        }

        private void ErasePixelsBetween(Point a, Point b)
        {
            int diameter = Math.Max(8, strokeWidth);
            ClearLine(current, a, b, diameter, imageBounds);
            ClearLine(clean, a, b, diameter, imageBounds);
        }

        private static void ClearLine(Bitmap target, Point a, Point b, int diameter, Rectangle clip)
        {
            using (Graphics g = Graphics.FromImage(target))
            using (Pen p = new Pen(Color.Transparent, diameter))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.SetClip(clip);
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                g.DrawLine(p, a, b);
                if (a == b)
                {
                    using (SolidBrush br = new SolidBrush(Color.Transparent))
                        g.FillEllipse(br, a.X - diameter / 2, a.Y - diameter / 2, diameter, diameter);
                }
                g.ResetClip();
            }
        }

        private static void DrawShape(Graphics g, EditorTool shape, Pen p, Point a, Point b, bool fill)
        {
            Rectangle r = shape == EditorTool.Check ? NormalizeAspect(a, b, 1.0f) : Normalize(a, b);
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;

            if (shape == EditorTool.Rectangle)
            {
                if (fill)
                {
                    using (SolidBrush br = new SolidBrush(p.Color)) g.FillRectangle(br, r);
                }
                g.DrawRectangle(p, r);
            }
            else if (shape == EditorTool.Ellipse)
            {
                if (fill)
                {
                    using (SolidBrush br = new SolidBrush(p.Color)) g.FillEllipse(br, r);
                }
                g.DrawEllipse(p, r);
            }
            else if (shape == EditorTool.Arrow)
                DrawArrow(g, p, a, b);
            else if (shape == EditorTool.Check)
                DrawCheck(g, p, r);
        }

        private static Rectangle NormalizeAspect(Point a, Point b, float widthToHeight)
        {
            int sx = b.X >= a.X ? 1 : -1;
            int sy = b.Y >= a.Y ? 1 : -1;
            int rawW = Math.Abs(b.X - a.X);
            int rawH = Math.Abs(b.Y - a.Y);
            if (rawW < 1 && rawH < 1) return new Rectangle(a.X, a.Y, 1, 1);

            int w = rawW;
            int h = rawH;
            if (h < 1) h = Math.Max(1, (int)Math.Round(w / widthToHeight));
            else if (w < 1) w = Math.Max(1, (int)Math.Round(h * widthToHeight));
            else if ((double)w / h > widthToHeight) h = Math.Max(1, (int)Math.Round(w / widthToHeight));
            else w = Math.Max(1, (int)Math.Round(h * widthToHeight));

            int x2 = a.X + sx * w;
            int y2 = a.Y + sy * h;
            return Normalize(a, new Point(x2, y2));
        }

        private static void DrawArrow(Graphics g, Pen p, Point a, Point b)
        {
            using (AdjustableArrowCap cap = new AdjustableArrowCap(
                Math.Max(4f, p.Width * 1.7f),
                Math.Max(5f, p.Width * 2.2f), true))
            {
                p.CustomEndCap = cap;
                g.DrawLine(p, a, b);
            }
        }

        private static void DrawCheck(Graphics g, Pen p, Rectangle r)
        {
            if (r.Width < 10 || r.Height < 10)
                r = new Rectangle(r.Left, r.Top, Math.Max(28, r.Width), Math.Max(28, r.Height));

            // 강조 체크는 실제 "✔" 심볼을 사용하고 드래그 비율은 1:1로 고정합니다.
            using (GraphicsPath path = new GraphicsPath())
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip;

                FontFamily family = null;
                bool disposeFamily = false;
                try
                {
                    try { family = new FontFamily("Segoe UI Symbol"); disposeFamily = true; }
                    catch { family = FontFamily.GenericSansSerif; }

                    float em = Math.Max(12f, r.Height * 0.90f);
                    path.AddString("✔", family, (int)FontStyle.Regular, em, r, format);
                    using (SolidBrush brush = new SolidBrush(p.Color))
                        g.FillPath(brush, path);
                }
                finally
                {
                    if (disposeFamily && family != null) family.Dispose();
                }
            }
        }

        private void DrawEmojiStamp(Graphics g, Point center, bool preview)
        {
            string glyph = String.IsNullOrEmpty(emojiGlyph) ? "😊" : emojiGlyph;
            using (Font f = new Font("Segoe UI Emoji", Math.Max(20, emojiSize), FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                SizeF size = g.MeasureString(glyph, f, new SizeF(1000f, 1000f), sf);
                PointF origin = new PointF(center.X - size.Width / 2f, center.Y - size.Height / 2f);

                if (preview)
                {
                    Color previewColor = Color.FromArgb(190, drawColor.R, drawColor.G, drawColor.B);
                    using (SolidBrush b = new SolidBrush(previewColor))
                        g.DrawString(glyph, f, b, origin, sf);
                }
                else
                {
                    // GDI+ 환경에서는 일부 컬러 이모지가 단색 글리프로 표시될 수 있습니다.
                    // 단색 글리프는 현재 선택된 그리기 색상을 사용합니다.
                    using (SolidBrush b = new SolidBrush(drawColor))
                        g.DrawString(glyph, f, b, origin, sf);
                }
            }
        }

        private void BeginText(Point p)
        {
            if (!imageBounds.Contains(p)) return;
            if (textEditing) CommitText();

            int right = imageBounds.Right - 10;
            int bottom = imageBounds.Bottom - 10;
            if (right <= imageBounds.Left) right = imageBounds.Right;
            if (bottom <= imageBounds.Top) bottom = imageBounds.Bottom;

            int left = Math.Max(imageBounds.Left, Math.Min(p.X, Math.Max(imageBounds.Left, right - 1)));
            int top = Math.Max(imageBounds.Top, Math.Min(p.Y, Math.Max(imageBounds.Top, bottom - 1)));

            // 기본 크기: 선택 지점부터 이미지 우측/하단 끝에서 각각 10px 안쪽까지.
            textEditRect = Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
            textPoint = new Point(left, top);
            textBuffer = String.Empty;
            compositionText = String.Empty;
            closingTextEditor = false;
            textEditing = true;
            pendingInitialTextFocus = true;

            PaintTextEditorHost host = new PaintTextEditorHost();
            textEditorHost = host;
            textEditor = host.Editor;

            host.TextColor = drawColor;
            host.CommitRequested += delegate { if (!closingTextEditor) CommitText(); };
            host.CancelRequested += delegate { if (!closingTextEditor) CancelText(); };
            host.UserBoundsEditCompleted += delegate
            {
                if (closingTextEditor || textEditorHost == null || textEditorHost.IsDisposed) return;
                Rectangle b = textEditorHost.Bounds;
                float z = Math.Max(0.01f, zoomFactor);
                int logicalX = (int)Math.Round(b.X / z);
                int logicalY = (int)Math.Round(b.Y / z);
                int logicalWidth = Math.Max(24, (int)Math.Round(b.Width / z));
                int logicalHeight = Math.Max(24, (int)Math.Round(b.Height / z));

                logicalX = Math.Max(imageBounds.Left, Math.Min(logicalX, imageBounds.Right - 1));
                logicalY = Math.Max(imageBounds.Top, Math.Min(logicalY, imageBounds.Bottom - 1));
                logicalWidth = Math.Min(logicalWidth, Math.Max(1, imageBounds.Right - logicalX));
                logicalHeight = Math.Min(logicalHeight, Math.Max(1, imageBounds.Bottom - logicalY));

                textEditRect = new Rectangle(logicalX, logicalY, logicalWidth, logicalHeight);
                textPoint = new Point(logicalX, logicalY);
                UpdateTextEditorBackground();
                LayoutTextEditor();
            };

            textEditor.Text = String.Empty;
            textEditor.ImeMode = ImeMode.NoControl;
            textEditor.BackColor = GetTextEditorBackColor(p);
            textEditor.ForeColor = textEditor.BackColor; // 2x2 입력 sink 자체는 시각적으로 숨깁니다.

            Controls.Add(host);
            host.BringToFront();
            UpdateTextEditorBackground();
            UpdateTextEditorAppearance(false);
            LayoutTextEditor();
            textEditor.SelectionStart = 0;
            textEditor.SelectionLength = 0;

            // 여기(MouseDown)에서는 포커스를 주지 않습니다.
            // 동적 자식 HWND 생성 중 Focus/SetFocus를 호출하면 같은 클릭의 Win32 포커스 처리가
            // 끝날 때 다시 Canvas/Host로 되돌아가는 경합이 생길 수 있습니다.
            // 최초 MouseUp은 Canvas 또는 새 Host 중 한 곳으로 전달되며, 두 경로 모두
            // 메시지가 완전히 끝난 뒤 FocusEditorAfterCurrentMouseMessage()를 호출합니다.
        }

        private Font CreateTextFont()
        {
            FontStyle style = textBold ? FontStyle.Bold : FontStyle.Regular;
            try { return new Font(fontName, textSize, style, GraphicsUnit.Pixel); }
            catch { return new Font("Segoe UI", textSize, style, GraphicsUnit.Pixel); }
        }

        private Font CreateTextEditorFont()
        {
            FontStyle style = textBold ? FontStyle.Bold : FontStyle.Regular;
            float scaledSize = Math.Max(6f, textSize * zoomFactor);
            try { return new Font(fontName, scaledSize, style, GraphicsUnit.Pixel); }
            catch { return new Font("Segoe UI", scaledSize, style, GraphicsUnit.Pixel); }
        }

        private Color GetTextEditorBackColor(Point p)
        {
            try
            {
                if (imageBounds.Contains(p))
                {
                    Color c = current.GetPixel(p.X, p.Y);
                    if (c.A > 32) return Color.FromArgb(c.R, c.G, c.B);
                }
            }
            catch { }
            return AppTheme.Workspace;
        }

        private static StringFormat CreatePaintTextFormat()
        {
            StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces |
                                  StringFormatFlags.LineLimit |
                                  StringFormatFlags.NoClip;
            format.Trimming = StringTrimming.Word;
            format.Alignment = StringAlignment.Near;
            format.LineAlignment = StringAlignment.Near;
            return format;
        }

        private void UpdateTextEditorBackground()
        {
            if (!textEditing || textEditorHost == null || textEditorHost.IsDisposed) return;
            Rectangle r = Rectangle.Intersect(textEditRect, imageBounds);
            if (r.Width < 1 || r.Height < 1) return;
            Bitmap snapshot = null;
            try
            {
                snapshot = current.Clone(r, PixelFormat.Format32bppArgb);
                textEditorHost.SetBackgroundSnapshot(snapshot);
                snapshot = null; // host owns it now.
            }
            finally
            {
                if (snapshot != null) snapshot.Dispose();
            }
        }

        private void UpdateTextEditorAppearance(bool restoreFocus)
        {
            if (!textEditing || textEditorHost == null || textEditorHost.IsDisposed ||
                textEditor == null || textEditor.IsDisposed) return;

            Font next = CreateTextEditorFont();
            // Native EDIT는 지정 Font를 내부 메시지 처리 중에도 참조할 수 있으므로
            // 편집 도중 이전 Font를 Dispose하지 않고 세션 종료 때 한꺼번에 정리합니다.
            textSessionFonts.Add(next);
            textEditorHost.PreviewFont = next;
            textEditorHost.TextColor = drawColor;
            textEditor.Font = next;
            textEditor.ForeColor = textEditor.BackColor;
            LayoutTextEditor();

            if (restoreFocus && IsHandleCreated)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (textEditing && textEditorHost != null && !textEditorHost.IsDisposed)
                            textEditorHost.FocusEditor();
                    });
                }
                catch { }
            }
        }

        private void LayoutTextEditor()
        {
            if (!textEditing || textEditorHost == null || textEditorHost.IsDisposed) return;

            int x = (int)Math.Round(textEditRect.X * zoomFactor);
            int y = (int)Math.Round(textEditRect.Y * zoomFactor);
            int w = Math.Max(1, (int)Math.Round(textEditRect.Width * zoomFactor));
            int h = Math.Max(1, (int)Math.Round(textEditRect.Height * zoomFactor));

            // 입력/확정 위치가 맞도록 논리 2px 여백을 화면 배율에 맞춰 적용합니다.
            textEditorHost.SetContentInset(Math.Max(2, (int)Math.Round(2f * zoomFactor)));
            textEditorHost.Bounds = new Rectangle(Math.Max(0, x), Math.Max(0, y), w, h);
            textEditorHost.BringToFront();
        }

        public void InsertText(string value)
        {
            if (tool != EditorTool.Text || !textEditing || textEditor == null ||
                textEditor.IsDisposed || String.IsNullOrEmpty(value)) return;
            textEditor.SelectedText = value;
            if (textEditorHost != null && !textEditorHost.IsDisposed)
                textEditorHost.FocusEditor();
        }

        public bool IsTextEditing
        {
            get { return textEditing; }
        }

        public void CommitActiveText()
        {
            if (textEditing) CommitText();
        }

        private void CommitText()
        {
            if (!textEditing) return;

            PaintTextEditorHost host = textEditorHost;
            string text = String.Empty;
            if (host != null && !host.IsDisposed)
            {
                int caretIndex;
                text = host.GetDisplayText(out caretIndex);
            }
            else if (textEditor != null && !textEditor.IsDisposed)
            {
                text = textEditor.Text ?? String.Empty;
            }

            Rectangle rect = textEditRect;
            closingTextEditor = true;
            textEditing = false;
            pendingInitialTextFocus = false;
            textEditor = null;
            textEditorHost = null;
            textBuffer = String.Empty;
            compositionText = String.Empty;

            if (host != null && !host.IsDisposed)
            {
                try { Controls.Remove(host); } catch { }
            }

            if (!String.IsNullOrWhiteSpace(text))
            {
                SaveUndo();
                redo.Clear();
                using (Graphics g = Graphics.FromImage(current))
                using (Font f = CreateTextFont())
                using (SolidBrush b = new SolidBrush(drawColor))
                using (StringFormat format = CreatePaintTextFormat())
                {
                    RectangleF content = new RectangleF(
                        rect.X + 2,
                        rect.Y + 2,
                        Math.Max(1, rect.Width - 4),
                        Math.Max(1, rect.Height - 4));
                    g.SetClip(imageBounds);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.DrawString(text, f, b, content, format);
                    g.ResetClip();
                }
                RaiseHistoryChanged();
            }

            if (host != null)
            {
                try { host.Dispose(); } catch { }
            }
            DisposeTextSessionFonts();
            closingTextEditor = false;
            Invalidate();
            Focus();
        }

        private void CancelText()
        {
            if (!textEditing) return;
            PaintTextEditorHost host = textEditorHost;
            closingTextEditor = true;
            textEditing = false;
            pendingInitialTextFocus = false;
            textEditor = null;
            textEditorHost = null;
            textBuffer = String.Empty;
            compositionText = String.Empty;

            if (host != null)
            {
                try { Controls.Remove(host); } catch { }
                try { host.Dispose(); } catch { }
            }
            DisposeTextSessionFonts();
            closingTextEditor = false;
            Invalidate();
            Focus();
        }

        private void DisposeTextSessionFonts()
        {
            for (int i = 0; i < textSessionFonts.Count; i++)
            {
                try { if (textSessionFonts[i] != null) textSessionFonts[i].Dispose(); } catch { }
            }
            textSessionFonts.Clear();
        }

        private void CropTo(Rectangle r)
        {
            Rectangle oldImage = imageBounds;
            Bitmap croppedCurrent = current.Clone(r, PixelFormat.Format32bppArgb);
            Bitmap croppedClean = clean.Clone(r, PixelFormat.Format32bppArgb);

            Rectangle intersect = Rectangle.Intersect(oldImage, r);
            Rectangle nextImageBounds = Rectangle.Empty;
            if (!intersect.IsEmpty)
                nextImageBounds = new Rectangle(intersect.X - r.X + CanvasMargin, intersect.Y - r.Y + CanvasMargin, intersect.Width, intersect.Height);

            Bitmap paddedCurrent = new Bitmap(croppedCurrent.Width + CanvasMargin * 2, croppedCurrent.Height + CanvasMargin * 2, PixelFormat.Format32bppArgb);
            Bitmap paddedClean = new Bitmap(paddedCurrent.Width, paddedCurrent.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(paddedCurrent))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.DrawImageUnscaled(croppedCurrent, CanvasMargin, CanvasMargin);
            }
            using (Graphics g = Graphics.FromImage(paddedClean))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.DrawImageUnscaled(croppedClean, CanvasMargin, CanvasMargin);
            }

            croppedCurrent.Dispose();
            croppedClean.Dispose();
            current.Dispose();
            clean.Dispose();
            current = paddedCurrent;
            clean = paddedClean;
            imageBounds = nextImageBounds.IsEmpty
                ? new Rectangle(CanvasMargin, CanvasMargin, Math.Max(1, r.Width), Math.Max(1, r.Height))
                : nextImageBounds;

            UpdateControlSize();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        public void RotateLeft()
        {
            if (textEditing) CommitText();
            SaveUndo();
            redo.Clear();

            int oldWidth = current.Width;
            Rectangle oldImage = imageBounds;
            current.RotateFlip(RotateFlipType.Rotate270FlipNone);
            clean.RotateFlip(RotateFlipType.Rotate270FlipNone);
            imageBounds = new Rectangle(
                oldImage.Y,
                oldWidth - (oldImage.X + oldImage.Width),
                oldImage.Height,
                oldImage.Width);

            UpdateControlSize();
            Invalidate();
            RaiseHistoryChanged();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        public void RotateRight()
        {
            if (textEditing) CommitText();
            SaveUndo();
            redo.Clear();

            int oldHeight = current.Height;
            Rectangle oldImage = imageBounds;
            current.RotateFlip(RotateFlipType.Rotate90FlipNone);
            clean.RotateFlip(RotateFlipType.Rotate90FlipNone);
            imageBounds = new Rectangle(
                oldHeight - (oldImage.Y + oldImage.Height),
                oldImage.X,
                oldImage.Height,
                oldImage.Width);

            UpdateControlSize();
            Invalidate();
            RaiseHistoryChanged();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        public void Undo()
        {
            if (textEditing) CommitText();
            if (undo.Count == 0) return;
            redo.Push(CaptureState());
            EditorState previous = undo.Pop();
            RestoreState(previous);
            Invalidate();
            RaiseHistoryChanged();
        }

        public void Redo()
        {
            if (textEditing) CommitText();
            if (redo.Count == 0) return;
            PushLimited(undo, CaptureState());
            EditorState next = redo.Pop();
            RestoreState(next);
            Invalidate();
            RaiseHistoryChanged();
        }

        private void SaveUndo()
        {
            PushLimited(undo, CaptureState());
            RaiseHistoryChanged();
        }

        private static void PushLimited(Stack<EditorState> stack, EditorState state)
        {
            if (stack.Count < MaxHistory)
            {
                stack.Push(state);
                return;
            }

            EditorState[] existing = stack.ToArray();
            stack.Clear();
            for (int i = Math.Min(MaxHistory - 2, existing.Length - 1); i >= 0; i--)
                stack.Push(existing[i]);
            stack.Push(state);
        }

        private EditorState CaptureState()
        {
            EditorState s = new EditorState();
            s.Current = EncodePng(current);
            s.Clean = EncodePng(clean);
            s.ImageBounds = imageBounds;
            return s;
        }

        private void RestoreState(EditorState state)
        {
            Bitmap nextCurrent = DecodeBitmap(state.Current);
            Bitmap nextClean = DecodeBitmap(state.Clean);
            current.Dispose();
            clean.Dispose();
            current = nextCurrent;
            clean = nextClean;
            imageBounds = state.ImageBounds;
            UpdateControlSize();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        private static byte[] EncodePng(Bitmap bmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private static Bitmap DecodeBitmap(byte[] bytes)
        {
            using (MemoryStream ms = new MemoryStream(bytes))
            using (Image img = Image.FromStream(ms))
                return CloneArgb(img);
        }

        private static Bitmap CloneArgb(Image source)
        {
            Bitmap bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }
            return bmp;
        }

        private static Rectangle FindAlphaBounds(Bitmap bmp)
        {
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = null;
            try
            {
                data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int stride = Math.Abs(data.Stride);
                byte[] row = new byte[stride];

                int minX = bmp.Width;
                int minY = bmp.Height;
                int maxX = -1;
                int maxY = -1;

                for (int y = 0; y < bmp.Height; y++)
                {
                    IntPtr rowPtr = new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride);
                    Marshal.Copy(rowPtr, row, 0, stride);
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        if (row[x * 4 + 3] != 0)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }

                if (maxX < minX || maxY < minY) return Rectangle.Empty;
                return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            }
            finally
            {
                if (data != null) bmp.UnlockBits(data);
            }
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            int left = Math.Min(a.X, b.X);
            int top = Math.Min(a.Y, b.Y);
            int right = Math.Max(a.X, b.X);
            int bottom = Math.Max(a.Y, b.Y);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private Point ToLogicalPoint(Point client)
        {
            return new Point(
                (int)Math.Floor(client.X / zoomFactor),
                (int)Math.Floor(client.Y / zoomFactor));
        }

        private Point ClampPoint(Point p)
        {
            int x = Math.Max(0, Math.Min(current.Width - 1, p.X));
            int y = Math.Max(0, Math.Min(current.Height - 1, p.Y));
            return new Point(x, y);
        }

        private Point ClampToImageBounds(Point p)
        {
            int x = Math.Max(imageBounds.Left, Math.Min(imageBounds.Right - 1, p.X));
            int y = Math.Max(imageBounds.Top, Math.Min(imageBounds.Bottom - 1, p.Y));
            return new Point(x, y);
        }

        private void UpdateControlSize()
        {
            Size = new Size(
                Math.Max(1, (int)Math.Round(current.Width * zoomFactor)),
                Math.Max(1, (int)Math.Round(current.Height * zoomFactor)));
            if (textEditing) LayoutTextEditor();
            if (Parent != null) Parent.PerformLayout();
        }

        private void RequestPreviewInvalidate(bool force)
        {
            int nowTick = Environment.TickCount;
            int interval = AppSettings.LowSpecOptimization ? LowSpecPreviewInterval : NormalPreviewInterval;
            if (!force && unchecked(nowTick - lastPreviewInvalidateTick) >= 0 &&
                unchecked(nowTick - lastPreviewInvalidateTick) < interval) return;

            lastPreviewInvalidateTick = nowTick;
            Invalidate();
        }

        private void RaisePointerInfo(Point p, bool force)
        {
            if (PointerInfoChanged == null) return;

            int nowTick = Environment.TickCount;
            int interval = AppSettings.LowSpecOptimization ? LowSpecPointerInfoInterval : NormalPointerInfoInterval;
            if (!force && unchecked(nowTick - lastPointerInfoTick) >= 0 &&
                unchecked(nowTick - lastPointerInfoTick) < interval) return;
            lastPointerInfoTick = nowTick;

            p = ClampPoint(p);

            bool insideImage = imageBounds.Contains(p);
            Color c = insideImage ? current.GetPixel(p.X, p.Y) : Color.Transparent;
            Size selected = measuredDragSize;
            if (drawing || measuringDrag)
            {
                Rectangle r = tool == EditorTool.Check ? NormalizeAspect(start, now, 1.0f) : Normalize(start, now);
                selected = r.Size;
            }

            CanvasPointerInfo info = new CanvasPointerInfo();
            info.Point = new Point(p.X - imageBounds.X, p.Y - imageBounds.Y);
            info.Color = c;
            info.Transparent = c.A == 0;
            info.InsideImage = insideImage;
            info.DragSize = selected;
            info.ImageSize = imageBounds.Size;
            info.ZoomPercent = ZoomPercent;
            PointerInfoChanged(info);
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void RaiseHistoryChanged()
        {
            if (HistoryChanged != null) HistoryChanged(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    if (textEditorHost != null && !textEditorHost.IsDisposed)
                        textEditorHost.Dispose();
                }
                catch { }
                textEditorHost = null;
                textEditor = null;
                DisposeTextSessionFonts();
                if (checkerBrush != null) checkerBrush.Dispose();
                if (checkerTile != null) checkerTile.Dispose();
                checkerBrush = null;
                checkerTile = null;
                if (current != null) current.Dispose();
                if (clean != null) clean.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ToolOptionsBar : Panel
    {
        private readonly FlowLayoutPanel flow;
        private Color currentColor;
        private int currentWidth;
        private int currentTextSize;
        private string currentFontName;
        private bool currentFill;
        private bool currentTextBold;
        private EditorTool currentTool;
        private bool reconfiguring;

        public event Action<Color, int, int, string, bool, bool> SettingsChanged;
        public event Action<string> TextInsertRequested;

        public ToolOptionsBar()
        {
            Height = 56;
            Dock = DockStyle.Top;
            BackColor = AppTheme.Options;
            Padding = new Padding(14, 8, 14, 8);
            // 옵션 영역은 항상 56px을 예약합니다. 도구를 선택하지 않았을 때는
            // 빈 영역만 보여 주어 이미지/캔버스가 위아래로 움직이지 않게 합니다.
            Visible = true;

            flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.BackColor = AppTheme.Options;
            Controls.Add(flow);
        }

        public void Configure(EditorTool tool, ToolStyle style)
        {
            currentTool = tool;
            currentColor = style.Color;
            currentWidth = style.Width;
            currentTextSize = style.TextSize;
            currentFontName = style.FontName;
            currentFill = style.Fill;
            currentTextBold = style.TextBold;

            reconfiguring = true;
            flow.SuspendLayout();
            ClearFlowControls();

            bool colorTool = tool == EditorTool.Pen || tool == EditorTool.Highlighter ||
                             tool == EditorTool.Rectangle || tool == EditorTool.Ellipse ||
                             tool == EditorTool.Arrow || tool == EditorTool.Check || tool == EditorTool.Text || tool == EditorTool.Emoji;
            bool widthTool = tool == EditorTool.Pen || tool == EditorTool.Highlighter ||
                             tool == EditorTool.Rectangle || tool == EditorTool.Ellipse ||
                             tool == EditorTool.Arrow || tool == EditorTool.Check ||
                             tool == EditorTool.Eraser || tool == EditorTool.PixelEraser;
            bool textTool = tool == EditorTool.Text;
            bool emojiTool = tool == EditorTool.Emoji;

            if (colorTool)
            {
                flow.Controls.Add(MakeLabel(L10n.T("색상", "Color"), 50));
                Color[] colors = tool == EditorTool.Highlighter
                    ? new Color[] {
                        Color.FromArgb(255, 235, 59),
                        Color.FromArgb(205, 255, 82),
                        Color.FromArgb(76, 233, 218),
                        Color.FromArgb(255, 112, 174),
                        Color.FromArgb(255, 171, 64),
                        Color.FromArgb(190, 126, 255),
                        Color.FromArgb(255, 255, 255)
                    }
                    : new Color[] {
                        Color.FromArgb(35, 38, 43),
                        Color.FromArgb(220, 50, 47),
                        Color.FromArgb(255, 0, 0),
                        Color.FromArgb(240, 125, 35),
                        Color.FromArgb(246, 199, 45),
                        Color.FromArgb(76, 170, 91),
                        Color.FromArgb(0, 120, 212),
                        Color.FromArgb(0, 0, 255),
                        Color.FromArgb(91, 96, 205),
                        Color.FromArgb(156, 86, 184),
                        Color.FromArgb(232, 82, 141),
                        Color.White
                    };

                bool presetSelected = false;
                for (int i = 0; i < colors.Length; i++)
                    if (SameRgb(colors[i], currentColor)) presetSelected = true;

                for (int i = 0; i < colors.Length; i++)
                {
                    Color c = colors[i];
                    PaletteSwatch sw = new PaletteSwatch(c);
                    sw.Size = new Size(DpiUtil.Scale(this, 30), DpiUtil.Scale(this, 30));
                    sw.Margin = new Padding(DpiUtil.Scale(this, 2), DpiUtil.Scale(this, 1), DpiUtil.Scale(this, 1), 0);
                    sw.Selected = SameRgb(c, currentColor);
                    sw.Click += delegate
                    {
                        currentColor = c;
                        Configure(currentTool, new ToolStyle(currentColor, currentWidth, currentTextSize, currentFontName, currentFill, currentTextBold));
                        RaiseChanged();
                    };
                    flow.Controls.Add(sw);
                }

                CustomColorButton custom = new CustomColorButton();
                custom.Size = new Size(DpiUtil.Scale(this, 36), DpiUtil.Scale(this, 32));
                custom.Margin = new Padding(DpiUtil.Scale(this, 5), DpiUtil.Scale(this, 1), DpiUtil.Scale(this, 2), 0);
                custom.Selected = !presetSelected;
                custom.SelectedColor = currentColor;
                custom.Click += delegate
                {
                    using (ColorDialog dialog = new ColorDialog())
                    {
                        dialog.Color = currentColor;
                        dialog.FullOpen = true;
                        if (dialog.ShowDialog() == DialogResult.OK)
                        {
                            currentColor = dialog.Color;
                            Configure(currentTool, new ToolStyle(currentColor, currentWidth, currentTextSize, currentFontName, currentFill, currentTextBold));
                            RaiseChanged();
                        }
                    }
                };
                flow.Controls.Add(custom);
            }

            if (widthTool)
            {
                flow.Controls.Add(MakeGap(14));
                flow.Controls.Add(MakeLabel(tool == EditorTool.Eraser || tool == EditorTool.PixelEraser ? L10n.T("크기", "Size") : L10n.T("두께", "Width"), 50));

                int min;
                int max;
                if (tool == EditorTool.Highlighter) { min = 8; max = 48; }
                else if (tool == EditorTool.Eraser || tool == EditorTool.PixelEraser) { min = 8; max = 96; }
                else { min = 1; max = 24; }

                ModernSlider slider = new ModernSlider();
                slider.Minimum = min;
                slider.Maximum = max;
                slider.Value = Math.Max(min, Math.Min(max, currentWidth));
                slider.Size = new Size(DpiUtil.Scale(this, 160), DpiUtil.Scale(this, 32));
                slider.Margin = new Padding(DpiUtil.Scale(this, 2), DpiUtil.Scale(this, 2), DpiUtil.Scale(this, 2), 0);

                Label value = MakeLabel(slider.Value + " px", 54);
                value.ForeColor = AppTheme.Text;

                slider.ValueChanged += delegate
                {
                    currentWidth = slider.Value;
                    value.Text = currentWidth + " px";
                    RaiseChanged();
                };

                flow.Controls.Add(slider);
                flow.Controls.Add(value);
            }

            if (tool == EditorTool.Rectangle || tool == EditorTool.Ellipse)
            {
                flow.Controls.Add(MakeGap(14));

                CheckBox fillCheck = new CheckBox();
                fillCheck.Text = L10n.T("채우기", "Fill");
                fillCheck.Checked = currentFill;
                fillCheck.AutoSize = false;
                fillCheck.Size = new Size(DpiUtil.Scale(this, 82), DpiUtil.Scale(this, 32));
                fillCheck.Margin = new Padding(DpiUtil.Scale(this, 2), DpiUtil.Scale(this, 2), DpiUtil.Scale(this, 2), 0);
                fillCheck.FlatStyle = FlatStyle.Flat;
                fillCheck.ForeColor = AppTheme.Text;
                fillCheck.BackColor = AppTheme.Options;
                fillCheck.TextAlign = ContentAlignment.MiddleLeft;
                fillCheck.CheckAlign = ContentAlignment.MiddleLeft;
                fillCheck.Cursor = Cursors.Hand;
                fillCheck.CheckedChanged += delegate
                {
                    if (reconfiguring) return;
                    currentFill = fillCheck.Checked;
                    RaiseChanged();
                };
                flow.Controls.Add(fillCheck);
            }

            if (emojiTool)
            {
                flow.Controls.Add(MakeLabel(L10n.T("선택", "Select"), 52));

                FlatButton emojiPicker = new FlatButton();
                emojiPicker.Text = L10n.T("선택", "Select");
                emojiPicker.IconKind = AppIcon.None;
                emojiPicker.Compact = true;
                emojiPicker.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
                emojiPicker.Size = new Size(DpiUtil.Scale(this, L10n.IsKorean ? 58 : 64), DpiUtil.Scale(this, 32));
                emojiPicker.Margin = new Padding(DpiUtil.Scale(this, 2), DpiUtil.Scale(this, 2), DpiUtil.Scale(this, 2), 0);
                emojiPicker.Click += delegate
                {
                    ContextMenuStrip menu = new ContextMenuStrip();
                    menu.BackColor = AppTheme.Button;
                    menu.ForeColor = AppTheme.Text;
                    menu.Font = new Font("Segoe UI Emoji", 11f);

                    string[] emojiChoices = new string[] {
                        "😀", "😂", "😊", "😍", "👍", "👏", "✅", "✔",
                        "❤", "🔥", "⭐", "⚠", "❗", "🎉", "💡", "📌",
                        "➡", "⬆", "⬇", "🔍"
                    };

                    for (int i = 0; i < emojiChoices.Length; i++)
                    {
                        string glyph = emojiChoices[i];
                        ToolStripMenuItem item = new ToolStripMenuItem(glyph);
                        item.Checked = String.Equals(currentFontName, glyph, StringComparison.Ordinal);
                        item.Click += delegate
                        {
                            currentFontName = glyph;
                            emojiPicker.Text = L10n.T("선택", "Select");
                            RaiseChanged();
                        };
                        menu.Items.Add(item);
                    }

                    menu.Closed += delegate
                    {
                        try
                        {
                            BeginInvoke((MethodInvoker)delegate
                            {
                                if (!menu.IsDisposed) menu.Dispose();
                            });
                        }
                        catch { }
                    };
                    menu.Show(emojiPicker, new Point(0, emojiPicker.Height + DpiUtil.Scale(this, 2)));
                };
                flow.Controls.Add(emojiPicker);

                flow.Controls.Add(MakeGap(14));
                flow.Controls.Add(MakeLabel(L10n.T("크기", "Size"), 46));

                ModernSlider emojiSizeSlider = new ModernSlider();
                emojiSizeSlider.Minimum = 24;
                emojiSizeSlider.Maximum = 128;
                emojiSizeSlider.Value = Math.Max(24, Math.Min(128, currentTextSize));
                emojiSizeSlider.Size = new Size(DpiUtil.Scale(this, 160), DpiUtil.Scale(this, 32));
                emojiSizeSlider.Margin = new Padding(DpiUtil.Scale(this, 2), DpiUtil.Scale(this, 2), DpiUtil.Scale(this, 2), 0);

                Label emojiSizeValue = MakeLabel(emojiSizeSlider.Value + " px", 58);
                emojiSizeValue.ForeColor = AppTheme.Text;

                emojiSizeSlider.ValueChanged += delegate
                {
                    currentTextSize = emojiSizeSlider.Value;
                    emojiSizeValue.Text = currentTextSize + " px";
                    RaiseChanged();
                };

                flow.Controls.Add(emojiSizeSlider);
                flow.Controls.Add(emojiSizeValue);
            }

            if (textTool)
            {
                flow.Controls.Add(MakeGap(16));
                flow.Controls.Add(MakeLabel(L10n.T("폰트", "Font"), 46));

                ComboBox fonts = new ComboBox();
                fonts.DropDownStyle = ComboBoxStyle.DropDownList;
                fonts.FlatStyle = FlatStyle.Flat;
                fonts.DrawMode = DrawMode.OwnerDrawFixed;
                fonts.ItemHeight = DpiUtil.Scale(this, 24);
                fonts.BackColor = AppTheme.Button;
                fonts.ForeColor = AppTheme.Text;
                fonts.Font = new Font("Segoe UI", 9f);
                fonts.DrawItem += DrawDarkComboItem;
                fonts.Width = DpiUtil.Scale(this, 132);
                fonts.Height = DpiUtil.Scale(this, 30);
                fonts.Margin = new Padding(DpiUtil.Scale(this, 2), DpiUtil.Scale(this, 3), DpiUtil.Scale(this, 4), 0);

                string[] candidates = L10n.IsKorean
                    ? new string[] { "맑은 고딕", "Segoe UI", "Segoe UI Emoji", "Arial", "굴림", "바탕", "Consolas" }
                    : new string[] { "Segoe UI", "Arial", "Calibri", "Segoe UI Emoji", "Consolas" };
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (FontExists(candidates[i]))
                        fonts.Items.Add(candidates[i]);
                }
                if (fonts.Items.Count == 0) fonts.Items.Add("Segoe UI");

                int selected = fonts.Items.IndexOf(currentFontName);
                fonts.SelectedIndex = selected >= 0 ? selected : 0;
                currentFontName = fonts.SelectedItem.ToString();

                fonts.SelectedIndexChanged += delegate
                {
                    if (reconfiguring) return;
                    if (fonts.SelectedItem != null)
                    {
                        currentFontName = fonts.SelectedItem.ToString();
                        RaiseChanged();
                    }
                };
                flow.Controls.Add(fonts);

                flow.Controls.Add(MakeLabel(L10n.T("크기", "Size"), 46));
                ComboBox sizes = new ComboBox();
                sizes.DropDownStyle = ComboBoxStyle.DropDownList;
                sizes.FlatStyle = FlatStyle.Flat;
                sizes.DrawMode = DrawMode.OwnerDrawFixed;
                sizes.ItemHeight = DpiUtil.Scale(this, 24);
                sizes.BackColor = AppTheme.Button;
                sizes.ForeColor = AppTheme.Text;
                sizes.DrawItem += DrawDarkComboItem;
                sizes.Width = DpiUtil.Scale(this, 70);
                sizes.Margin = new Padding(DpiUtil.Scale(this, 2), DpiUtil.Scale(this, 3), DpiUtil.Scale(this, 2), 0);
                int[] textSizes = new int[] { 12, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64, 72 };
                for (int i = 0; i < textSizes.Length; i++)
                    sizes.Items.Add(textSizes[i].ToString());

                int sizeIndex = Array.IndexOf(textSizes, currentTextSize);
                if (sizeIndex < 0) sizeIndex = Array.IndexOf(textSizes, 24);
                sizes.SelectedIndex = sizeIndex >= 0 ? sizeIndex : 0;

                sizes.SelectedIndexChanged += delegate
                {
                    if (reconfiguring) return;
                    int v;
                    if (Int32.TryParse(sizes.SelectedItem.ToString(), out v))
                    {
                        currentTextSize = v;
                        RaiseChanged();
                    }
                };
                flow.Controls.Add(sizes);

                FlatButton boldButton = new FlatButton();
                boldButton.Text = "B";
                boldButton.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
                boldButton.Compact = true;
                boldButton.Size = new Size(DpiUtil.Scale(this, 38), DpiUtil.Scale(this, 32));
                boldButton.Margin = new Padding(DpiUtil.Scale(this, 6), DpiUtil.Scale(this, 2), DpiUtil.Scale(this, 2), 0);
                boldButton.Selected = currentTextBold;
                boldButton.Click += delegate
                {
                    currentTextBold = !currentTextBold;
                    boldButton.Selected = currentTextBold;
                    RaiseChanged();
                };
                flow.Controls.Add(boldButton);

            }

            flow.ResumeLayout();
            reconfiguring = false;
            Visible = colorTool || widthTool || textTool || emojiTool;
        }

        public void ShowEmpty()
        {
            flow.SuspendLayout();
            ClearFlowControls();
            flow.ResumeLayout();
            Visible = true;
        }

        public void HideOptions()
        {
            currentTool = EditorTool.None;
            reconfiguring = true;
            flow.SuspendLayout();
            ClearFlowControls();
            flow.ResumeLayout();
            reconfiguring = false;
            Visible = true;
            Invalidate();
        }

        private void ClearFlowControls()
        {
            if (flow.Controls.Count == 0) return;

            Control[] oldControls = new Control[flow.Controls.Count];
            flow.Controls.CopyTo(oldControls, 0);
            flow.Controls.Clear();
            for (int i = 0; i < oldControls.Length; i++)
            {
                if (oldControls[i] != null && !oldControls[i].IsDisposed)
                    oldControls[i].Dispose();
            }
        }

        private static void DrawDarkComboItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null || e.Index < 0 || e.Index >= combo.Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (SolidBrush bg = new SolidBrush(selected ? AppTheme.ButtonHover : AppTheme.Button))
                e.Graphics.FillRectangle(bg, e.Bounds);
            TextRenderer.DrawText(e.Graphics, combo.Items[e.Index].ToString(), combo.Font,
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 8), e.Bounds.Height),
                AppTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if ((e.State & DrawItemState.Focus) == DrawItemState.Focus) e.DrawFocusRectangle();
        }

        private Label MakeLabel(string text, int width)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Size = new Size(DpiUtil.Scale(this, width), DpiUtil.Scale(this, 34));
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.ForeColor = AppTheme.MutedText;
            l.Margin = new Padding(0, DpiUtil.Scale(this, 1), DpiUtil.Scale(this, 4), 0);
            return l;
        }

        private Control MakeGap(int width)
        {
            Panel p = new Panel();
            p.Size = new Size(DpiUtil.Scale(this, width), 1);
            p.Margin = Padding.Empty;
            return p;
        }

        private static bool SameRgb(Color a, Color b)
        {
            return a.R == b.R && a.G == b.G && a.B == b.B;
        }

        private static bool FontExists(string name)
        {
            try
            {
                using (Font f = new Font(name, 9f))
                    return String.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase) ||
                           !String.IsNullOrEmpty(f.Name);
            }
            catch { return false; }
        }

        private void RaiseChanged()
        {
            if (SettingsChanged != null)
                SettingsChanged(currentColor, currentWidth, currentTextSize, currentFontName, currentFill, currentTextBold);
        }
    }
}
