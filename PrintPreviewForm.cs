using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace CapPicker
{
    internal enum PrintHorizontalAlign
    {
        Left,
        Center,
        Right
    }

    internal enum PrintVerticalAlign
    {
        Top,
        Center,
        Bottom
    }

    internal sealed class CapPrintPreviewForm : Form
    {
        private readonly Bitmap image;
        private readonly PrintDocument document;
        private readonly PaperPreviewControl previewControl;
        private readonly FlatButton portraitButton;
        private readonly FlatButton landscapeButton;
        private readonly FlatButton printButton;
        private readonly FlatButton closeButton;
        private readonly FlatButton leftAlignButton;
        private readonly FlatButton centerAlignButton;
        private readonly FlatButton rightAlignButton;
        private readonly FlatButton topAlignButton;
        private readonly FlatButton middleAlignButton;
        private readonly FlatButton bottomAlignButton;
        private readonly Label infoLabel;
        private readonly Font ownedFont;
        private readonly Font ownedTitleFont;

        private PrintHorizontalAlign horizontalAlign = PrintHorizontalAlign.Center;
        private PrintVerticalAlign verticalAlign = PrintVerticalAlign.Center;

        public CapPrintPreviewForm(Bitmap source)
        {
            if (source == null) throw new ArgumentNullException("source");

            image = source;
            document = new PrintDocument();
            document.DocumentName = L10n.T("CapPicker 캡처", "CapPicker Capture");
            document.PrintPage += DocumentPrintPage;
            try { document.DefaultPageSettings.Landscape = image.Width > image.Height; }
            catch { }

            Text = L10n.T("CapPicker - 인쇄 미리보기", "CapPicker - Print Preview");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = false;
            MinimizeBox = false;
            KeyPreview = true;
            BackColor = AppTheme.Window;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(DpiUtil.ReferenceDpi, DpiUtil.ReferenceDpi);
            MinimumSize = new Size(980, 620);
            ClientSize = new Size(1120, 800);

            ownedFont = new Font("Segoe UI", 9f, FontStyle.Regular);
            ownedTitleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
            Font = ownedFont;

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 124;
            header.BackColor = AppTheme.Toolbar;
            header.Padding = new Padding(20, 10, 20, 10);

            TableLayoutPanel headerRows = new TableLayoutPanel();
            headerRows.Dock = DockStyle.Fill;
            headerRows.RowCount = 2;
            headerRows.ColumnCount = 1;
            headerRows.Margin = Padding.Empty;
            headerRows.Padding = Padding.Empty;
            headerRows.BackColor = AppTheme.Toolbar;
            headerRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            headerRows.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            TableLayoutPanel topRow = new TableLayoutPanel();
            topRow.Dock = DockStyle.Fill;
            topRow.RowCount = 1;
            topRow.ColumnCount = 3;
            topRow.Margin = Padding.Empty;
            topRow.Padding = Padding.Empty;
            topRow.BackColor = AppTheme.Toolbar;
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label title = new Label();
            title.AutoSize = true;
            title.Text = L10n.T("인쇄 미리보기", "Print Preview");
            title.ForeColor = AppTheme.Text;
            title.BackColor = Color.Transparent;
            title.Font = ownedTitleFont;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Anchor = AnchorStyles.Left;
            title.Margin = new Padding(2, 0, 24, 0);

            Label pageLabel = new Label();
            pageLabel.AutoSize = true;
            pageLabel.Text = "1 / 1";
            pageLabel.ForeColor = AppTheme.MutedText;
            pageLabel.BackColor = Color.Transparent;
            pageLabel.Font = ownedFont;
            pageLabel.TextAlign = ContentAlignment.MiddleCenter;
            pageLabel.Anchor = AnchorStyles.None;
            pageLabel.Margin = Padding.Empty;

            FlowLayoutPanel actionFlow = new FlowLayoutPanel();
            actionFlow.AutoSize = true;
            actionFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            actionFlow.Anchor = AnchorStyles.Right;
            actionFlow.FlowDirection = FlowDirection.LeftToRight;
            actionFlow.WrapContents = false;
            actionFlow.BackColor = Color.Transparent;
            actionFlow.Margin = Padding.Empty;
            actionFlow.Padding = Padding.Empty;

            printButton = MakeHeaderButton(L10n.T("인쇄", "Print"), L10n.IsKorean ? 132 : 128);
            printButton.IconKind = AppIcon.Print;
            printButton.Margin = new Padding(0, 4, 8, 0);
            printButton.Click += delegate { PrintNow(); };

            closeButton = MakeHeaderButton(L10n.T("닫기", "Close"), L10n.IsKorean ? 88 : 90);
            closeButton.Margin = new Padding(0, 4, 0, 0);
            closeButton.Click += delegate { Close(); };

            actionFlow.Controls.Add(printButton);
            actionFlow.Controls.Add(closeButton);

            topRow.Controls.Add(title, 0, 0);
            topRow.Controls.Add(pageLabel, 1, 0);
            topRow.Controls.Add(actionFlow, 2, 0);

            FlowLayoutPanel settingsFlow = new FlowLayoutPanel();
            settingsFlow.Dock = DockStyle.Fill;
            settingsFlow.FlowDirection = FlowDirection.LeftToRight;
            settingsFlow.WrapContents = false;
            settingsFlow.AutoScroll = false;
            settingsFlow.BackColor = Color.Transparent;
            settingsFlow.Margin = Padding.Empty;
            settingsFlow.Padding = new Padding(2, 7, 0, 0);

            FlowLayoutPanel orientationGroup = MakeSettingGroup();
            Label orientationLabel = MakeSettingLabel(L10n.T("방향", "Orientation"));
            portraitButton = MakeHeaderButton(L10n.T("세로", "Portrait"), L10n.IsKorean ? 76 : 92);
            landscapeButton = MakeHeaderButton(L10n.T("가로", "Landscape"), L10n.IsKorean ? 76 : 102);
            portraitButton.Margin = new Padding(12, 0, 7, 0);
            landscapeButton.Margin = Padding.Empty;
            portraitButton.Click += delegate { SetLandscape(false); };
            landscapeButton.Click += delegate { SetLandscape(true); };
            orientationGroup.Controls.Add(orientationLabel);
            orientationGroup.Controls.Add(portraitButton);
            orientationGroup.Controls.Add(landscapeButton);
            orientationGroup.Margin = new Padding(0, 0, 34, 0);

            FlowLayoutPanel horizontalGroup = MakeSettingGroup();
            Label horizontalLabel = MakeSettingLabel(L10n.T("가로 맞춤", "Horizontal"));
            leftAlignButton = MakeHeaderButton(L10n.T("왼쪽", "Left"), L10n.IsKorean ? 76 : 74);
            centerAlignButton = MakeHeaderButton(L10n.T("가운데", "Center"), L10n.IsKorean ? 92 : 88);
            rightAlignButton = MakeHeaderButton(L10n.T("오른쪽", "Right"), L10n.IsKorean ? 76 : 76);
            leftAlignButton.Margin = new Padding(12, 0, 7, 0);
            centerAlignButton.Margin = new Padding(0, 0, 7, 0);
            rightAlignButton.Margin = Padding.Empty;
            leftAlignButton.Click += delegate { SetHorizontalAlign(PrintHorizontalAlign.Left); };
            centerAlignButton.Click += delegate { SetHorizontalAlign(PrintHorizontalAlign.Center); };
            rightAlignButton.Click += delegate { SetHorizontalAlign(PrintHorizontalAlign.Right); };
            horizontalGroup.Controls.Add(horizontalLabel);
            horizontalGroup.Controls.Add(leftAlignButton);
            horizontalGroup.Controls.Add(centerAlignButton);
            horizontalGroup.Controls.Add(rightAlignButton);
            horizontalGroup.Margin = new Padding(0, 0, 34, 0);

            FlowLayoutPanel verticalGroup = MakeSettingGroup();
            Label verticalLabel = MakeSettingLabel(L10n.T("세로 맞춤", "Vertical"));
            topAlignButton = MakeHeaderButton(L10n.T("위", "Top"), L10n.IsKorean ? 62 : 66);
            middleAlignButton = MakeHeaderButton(L10n.T("가운데", "Center"), L10n.IsKorean ? 92 : 88);
            bottomAlignButton = MakeHeaderButton(L10n.T("아래", "Bottom"), L10n.IsKorean ? 62 : 84);
            topAlignButton.Margin = new Padding(12, 0, 7, 0);
            middleAlignButton.Margin = new Padding(0, 0, 7, 0);
            bottomAlignButton.Margin = Padding.Empty;
            topAlignButton.Click += delegate { SetVerticalAlign(PrintVerticalAlign.Top); };
            middleAlignButton.Click += delegate { SetVerticalAlign(PrintVerticalAlign.Center); };
            bottomAlignButton.Click += delegate { SetVerticalAlign(PrintVerticalAlign.Bottom); };
            verticalGroup.Controls.Add(verticalLabel);
            verticalGroup.Controls.Add(topAlignButton);
            verticalGroup.Controls.Add(middleAlignButton);
            verticalGroup.Controls.Add(bottomAlignButton);

            settingsFlow.Controls.Add(orientationGroup);
            settingsFlow.Controls.Add(horizontalGroup);
            settingsFlow.Controls.Add(verticalGroup);

            headerRows.Controls.Add(topRow, 0, 0);
            headerRows.Controls.Add(settingsFlow, 0, 1);
            header.Controls.Add(headerRows);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 42;
            footer.BackColor = AppTheme.Status;
            footer.Padding = new Padding(18, 0, 18, 0);

            infoLabel = new Label();
            infoLabel.Dock = DockStyle.Fill;
            infoLabel.ForeColor = AppTheme.MutedText;
            infoLabel.BackColor = Color.Transparent;
            infoLabel.Font = ownedFont;
            infoLabel.TextAlign = ContentAlignment.MiddleLeft;
            footer.Controls.Add(infoLabel);

            previewControl = new PaperPreviewControl(image, document);
            previewControl.Dock = DockStyle.Fill;
            previewControl.BackColor = AppTheme.Workspace;
            previewControl.HorizontalAlign = horizontalAlign;
            previewControl.VerticalAlign = verticalAlign;

            Controls.Add(previewControl);
            Controls.Add(footer);
            Controls.Add(header);

            UpdateInfoText();

            Shown += delegate
            {
                try
                {
                    if (Owner != null && Owner.Icon != null) Icon = Owner.Icon;
                }
                catch { }

                FitInitialSizeToOwnerScreen();
                UpdateOrientationButtons();
                UpdateAlignmentButtons();
                previewControl.Focus();
            };
            KeyDown += PreviewKeyDown;
        }

        private FlowLayoutPanel MakeSettingGroup()
        {
            FlowLayoutPanel group = new FlowLayoutPanel();
            group.AutoSize = true;
            group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            group.FlowDirection = FlowDirection.LeftToRight;
            group.WrapContents = false;
            group.BackColor = Color.Transparent;
            group.Padding = Padding.Empty;
            return group;
        }

        private Label MakeSettingLabel(string text)
        {
            Label label = new Label();
            label.AutoSize = false;
            label.Text = text;
            label.ForeColor = AppTheme.MutedText;
            label.BackColor = Color.Transparent;
            label.Font = ownedFont;
            label.TextAlign = ContentAlignment.MiddleLeft;
            int textWidth = TextRenderer.MeasureText(text, ownedFont).Width;
            label.Size = new Size(Math.Max(34, textWidth + 4), 38);
            label.Margin = Padding.Empty;
            return label;
        }

        private FlatButton MakeHeaderButton(string text, int width)
        {
            FlatButton button = new FlatButton();
            button.Text = text;
            button.Size = new Size(width, 38);
            button.BackColor = AppTheme.Toolbar;
            button.FillColor = AppTheme.Button;
            button.HoverColor = AppTheme.ButtonHover;
            button.TextColor = AppTheme.Text;
            button.CornerRadius = 8;
            button.Compact = true;
            button.Font = ownedFont;
            return button;
        }

        private void FitInitialSizeToOwnerScreen()
        {
            try
            {
                Screen screen = Owner != null ? Screen.FromControl(Owner) : Screen.FromControl(this);
                Rectangle work = screen.WorkingArea;
                int maxWidth = Math.Max(MinimumSize.Width, (int)Math.Round(work.Width * 0.88));
                int maxHeight = Math.Max(MinimumSize.Height, (int)Math.Round(work.Height * 0.88));
                Size desired = new Size(Math.Min(1240, maxWidth), Math.Min(900, maxHeight));
                Size = desired;
                if (Owner != null)
                {
                    int x = Owner.Left + (Owner.Width - Width) / 2;
                    int y = Owner.Top + (Owner.Height - Height) / 2;
                    x = Math.Max(work.Left, Math.Min(x, work.Right - Width));
                    y = Math.Max(work.Top, Math.Min(y, work.Bottom - Height));
                    Location = new Point(x, y);
                }
            }
            catch
            {
            }
        }

        private void SetLandscape(bool landscape)
        {
            try
            {
                document.DefaultPageSettings.Landscape = landscape;
            }
            catch
            {
            }
            UpdateOrientationButtons();
            previewControl.Invalidate();
        }

        private void UpdateOrientationButtons()
        {
            bool landscape = false;
            try { landscape = document.DefaultPageSettings.Landscape; }
            catch { }
            portraitButton.Selected = !landscape;
            landscapeButton.Selected = landscape;
        }

        private void SetHorizontalAlign(PrintHorizontalAlign value)
        {
            horizontalAlign = value;
            UpdateAlignmentButtons();
            ApplyPreviewLayout();
        }

        private void SetVerticalAlign(PrintVerticalAlign value)
        {
            verticalAlign = value;
            UpdateAlignmentButtons();
            ApplyPreviewLayout();
        }

        private void UpdateAlignmentButtons()
        {
            leftAlignButton.Selected = horizontalAlign == PrintHorizontalAlign.Left;
            centerAlignButton.Selected = horizontalAlign == PrintHorizontalAlign.Center;
            rightAlignButton.Selected = horizontalAlign == PrintHorizontalAlign.Right;
            topAlignButton.Selected = verticalAlign == PrintVerticalAlign.Top;
            middleAlignButton.Selected = verticalAlign == PrintVerticalAlign.Center;
            bottomAlignButton.Selected = verticalAlign == PrintVerticalAlign.Bottom;
        }

        private void ApplyPreviewLayout()
        {
            if (previewControl != null)
            {
                previewControl.HorizontalAlign = horizontalAlign;
                previewControl.VerticalAlign = verticalAlign;
                previewControl.Invalidate();
            }
            UpdateInfoText();
        }

        private void UpdateInfoText()
        {
            if (infoLabel == null) return;

            string h = horizontalAlign == PrintHorizontalAlign.Left
                ? L10n.T("왼쪽", "Left")
                : (horizontalAlign == PrintHorizontalAlign.Right ? L10n.T("오른쪽", "Right") : L10n.T("가운데", "Center"));
            string v = verticalAlign == PrintVerticalAlign.Top
                ? L10n.T("맨 위", "Top")
                : (verticalAlign == PrintVerticalAlign.Bottom ? L10n.T("맨 아래", "Bottom") : L10n.T("가운데", "Center"));

            infoLabel.Text = L10n.T(
                "페이지에 맞춤 · 가로 " + h + " · 세로 " + v + " · 인쇄 시 프린터 설정 적용",
                "Fit to page · Horizontal " + h + " · Vertical " + v + " · Printer settings applied when printing");
        }

        private void PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.P)
            {
                PrintNow();
                e.SuppressKeyPress = true;
            }
        }

        private void PrintNow()
        {
            try
            {
                using (PrintDialog dialog = new PrintDialog())
                {
                    dialog.Document = document;
                    dialog.UseEXDialog = true;
                    dialog.AllowCurrentPage = false;
                    dialog.AllowSelection = false;
                    dialog.AllowSomePages = false;
                    dialog.PrintToFile = false;

                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    document.Print();
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    L10n.T("인쇄 창을 열거나 인쇄하지 못했습니다.\r\n", "Could not open the print dialog or print.\r\n") + ex.Message,
                    "CapPicker",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void DocumentPrintPage(object sender, PrintPageEventArgs args)
        {
            Rectangle bounds = args.MarginBounds;
            if (bounds.Width < 1 || bounds.Height < 1) bounds = args.PageBounds;
            DrawImageFit(args.Graphics, image, bounds, horizontalAlign, verticalAlign);
            args.HasMorePages = false;
        }

        internal static void DrawImageFit(
            Graphics graphics,
            Image source,
            Rectangle bounds,
            PrintHorizontalAlign horizontal,
            PrintVerticalAlign vertical)
        {
            if (graphics == null || source == null || bounds.Width < 1 || bounds.Height < 1) return;

            double scale = Math.Min(bounds.Width / (double)Math.Max(1, source.Width),
                                    bounds.Height / (double)Math.Max(1, source.Height));
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));

            int x;
            if (horizontal == PrintHorizontalAlign.Left) x = bounds.Left;
            else if (horizontal == PrintHorizontalAlign.Right) x = bounds.Right - width;
            else x = bounds.Left + (bounds.Width - width) / 2;

            int y;
            if (vertical == PrintVerticalAlign.Top) y = bounds.Top;
            else if (vertical == PrintVerticalAlign.Bottom) y = bounds.Bottom - height;
            else y = bounds.Top + (bounds.Height - height) / 2;

            InterpolationMode oldInterpolation = graphics.InterpolationMode;
            PixelOffsetMode oldPixelOffset = graphics.PixelOffsetMode;
            CompositingQuality oldCompositing = graphics.CompositingQuality;
            try
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.DrawImage(source, new Rectangle(x, y, width, height));
            }
            finally
            {
                graphics.InterpolationMode = oldInterpolation;
                graphics.PixelOffsetMode = oldPixelOffset;
                graphics.CompositingQuality = oldCompositing;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && document != null) document.Dispose();
            base.Dispose(disposing);
            if (disposing)
            {
                if (ownedTitleFont != null) ownedTitleFont.Dispose();
                if (ownedFont != null) ownedFont.Dispose();
            }
        }
    }

    internal sealed class PaperPreviewControl : Control
    {
        private readonly Image image;
        private readonly PrintDocument document;

        public PrintHorizontalAlign HorizontalAlign { get; set; }
        public PrintVerticalAlign VerticalAlign { get; set; }

        public PaperPreviewControl(Image source, PrintDocument printDocument)
        {
            image = source;
            document = printDocument;
            HorizontalAlign = PrintHorizontalAlign.Center;
            VerticalAlign = PrintVerticalAlign.Center;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            TabStop = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int paperWidth = 827;
            int paperHeight = 1169;
            Margins margins = new Margins(100, 100, 100, 100);
            bool landscape = false;

            try
            {
                PageSettings page = document.DefaultPageSettings;
                if (page != null)
                {
                    landscape = page.Landscape;
                    if (page.PaperSize != null && page.PaperSize.Width > 0 && page.PaperSize.Height > 0)
                    {
                        paperWidth = page.PaperSize.Width;
                        paperHeight = page.PaperSize.Height;
                    }
                    if (page.Margins != null) margins = page.Margins;
                }
            }
            catch
            {
            }

            if (landscape && paperHeight > paperWidth)
            {
                int temp = paperWidth;
                paperWidth = paperHeight;
                paperHeight = temp;
            }
            else if (!landscape && paperWidth > paperHeight)
            {
                int temp = paperWidth;
                paperWidth = paperHeight;
                paperHeight = temp;
            }

            Rectangle client = ClientRectangle;
            int pad = Math.Max(18, DpiUtil.Scale(this, 24));
            client.Inflate(-pad, -pad);
            if (client.Width < 40 || client.Height < 40) return;

            double scale = Math.Min(client.Width / (double)Math.Max(1, paperWidth),
                                    client.Height / (double)Math.Max(1, paperHeight));
            int drawWidth = Math.Max(1, (int)Math.Round(paperWidth * scale));
            int drawHeight = Math.Max(1, (int)Math.Round(paperHeight * scale));
            Rectangle paper = new Rectangle(
                client.Left + (client.Width - drawWidth) / 2,
                client.Top + (client.Height - drawHeight) / 2,
                drawWidth,
                drawHeight);

            int shadow = Math.Max(4, DpiUtil.Scale(this, 6));
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                e.Graphics.FillRectangle(shadowBrush, new Rectangle(paper.X + shadow, paper.Y + shadow, paper.Width, paper.Height));

            using (SolidBrush paperBrush = new SolidBrush(Color.White))
                e.Graphics.FillRectangle(paperBrush, paper);
            using (Pen borderPen = new Pen(Color.FromArgb(185, 185, 185)))
                e.Graphics.DrawRectangle(borderPen, paper);

            double sx = paper.Width / (double)Math.Max(1, paperWidth);
            double sy = paper.Height / (double)Math.Max(1, paperHeight);
            int left = paper.Left + (int)Math.Round(margins.Left * sx);
            int top = paper.Top + (int)Math.Round(margins.Top * sy);
            int right = paper.Right - (int)Math.Round(margins.Right * sx);
            int bottom = paper.Bottom - (int)Math.Round(margins.Bottom * sy);
            Rectangle content = Rectangle.FromLTRB(left, top, right, bottom);
            content.Intersect(paper);

            if (content.Width < 1 || content.Height < 1)
                content = paper;

            CapPrintPreviewForm.DrawImageFit(e.Graphics, image, content, HorizontalAlign, VerticalAlign);

            using (Pen marginPen = new Pen(Color.FromArgb(125, 150, 150, 150)))
            {
                marginPen.DashStyle = DashStyle.Dash;
                e.Graphics.DrawRectangle(marginPen, content);
            }
        }
    }
}
