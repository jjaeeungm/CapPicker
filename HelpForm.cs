using System;
using System.Drawing;
using System.Windows.Forms;

namespace CapPicker
{
    internal sealed class HelpForm : Form
    {
        private RichTextBox helpText;
        private Button closeButton;

        public HelpForm()
        {
            Text = L10n.T("CapPicker 도움말", "CapPicker Help") + "  ·  " + L10n.Version;
            // Help uses layout-managed WinForms controls, so DPI autoscaling is reliable here.
            // Use the same 120-DPI design reference as the rest of CapPicker.
            AutoScaleDimensions = new SizeF(DpiUtil.ReferenceDpi, DpiUtil.ReferenceDpi);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 640);
            MinimumSize = new Size(700, 520);
            BackColor = AppTheme.Window;
            ForeColor = AppTheme.Text;
            Font = new Font(L10n.DefaultFontName, 9f);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Margin = Padding.Empty;
            root.Padding = Padding.Empty;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
            root.BackColor = AppTheme.Window;
            Controls.Add(root);

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Margin = Padding.Empty;
            body.Padding = new Padding(22, 18, 22, 16);
            body.BackColor = AppTheme.Window;
            root.Controls.Add(body, 0, 0);

            helpText = new RichTextBox();
            helpText.Dock = DockStyle.Fill;
            helpText.ReadOnly = true;
            helpText.BorderStyle = BorderStyle.None;
            helpText.BackColor = AppTheme.Window;
            helpText.ForeColor = AppTheme.Text;
            helpText.Font = new Font(L10n.DefaultFontName, 9.5f);
            helpText.DetectUrls = false;
            helpText.ScrollBars = RichTextBoxScrollBars.Vertical;
            helpText.WordWrap = true;
            helpText.Margin = Padding.Empty;
            body.Controls.Add(helpText);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Fill;
            bottom.Margin = Padding.Empty;
            bottom.BackColor = AppTheme.Toolbar;
            root.Controls.Add(bottom, 0, 1);

            closeButton = new Button();
            closeButton.Text = L10n.T("닫기", "Close");
            closeButton.FlatStyle = FlatStyle.Flat;
            closeButton.BackColor = AppTheme.Button;
            closeButton.ForeColor = AppTheme.Text;
            closeButton.FlatAppearance.BorderColor = Color.FromArgb(100, 100, 100);
            closeButton.Size = new Size(90, 32);
            closeButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            closeButton.Location = new Point(bottom.Width - 110, 12);
            closeButton.Click += delegate { Close(); };
            bottom.Controls.Add(closeButton);
            bottom.Resize += delegate { closeButton.Left = Math.Max(12, bottom.ClientSize.Width - closeButton.Width - 20); };

            Shown += delegate { ApplyWindowTone(); };
            RefreshHelpPreview();
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

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape || keyData == Keys.F1)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void RefreshHelpPreview()
        {
            BuildHelpDocument(L10n.IsKorean);
            helpText.SelectionStart = 0;
            helpText.SelectionLength = 0;
            helpText.ScrollToCaret();
        }

        private void BuildHelpDocument(bool korean)
        {
            helpText.Clear();
            string fontName = korean ? "맑은 고딕" : "Segoe UI";
            helpText.Font = new Font(fontName, 9.5f);

            AppendTitle("CapPicker " + L10n.Version, fontName);
            AppendLead(korean
                ? "캡처부터 간단한 표시·텍스트 편집·색상 추출까지 한 창에서 처리합니다."
                : "Capture, annotate, enter text, and pick colors in one lightweight window.", fontName);

            if (korean)
            {
                AppendSection("1. 빠른 시작", new string[] {
                    "직사각형 캡처  —  Alt + Shift + S",
                    "화면 컬러피커  —  Alt + Shift + C",
                    "도움말  —  F1",
                    "캡처가 끝나면 원본 캡처 이미지를 클립보드에 자동 복사합니다."
                }, fontName);

                AppendSection("2. 캡처", new string[] {
                    "직사각형: 마우스로 원하는 영역을 드래그합니다.",
                    "크기지정: 폭과 높이를 먼저 지정한 뒤 위치를 선택합니다.",
                    "윈도우창: 대상 창을 선택합니다. 대상 창 자체 렌더링을 우선 사용하고, 지원하지 않는 창은 현재 화면의 보이는 영역으로 자동 대체합니다.",
                    "전체화면: 가상 데스크톱 전체를 캡처합니다.",
                    "지난영역: 직전에 사용한 영역을 다시 캡처합니다."
                }, fontName);

                AppendSection("3. 화면 컬러피커", new string[] {
                    "마우스를 움직이면 확대창, 픽셀 격자, 중앙 십자선과 HEX/RGB 값이 실시간으로 표시됩니다.",
                    "마우스 휠로 6×~128×까지 확대·축소합니다.",
                    "일반 모드에서는 64×를 초과하면 확대창 자체가 1.5배 커집니다.",
                    "저사양 PC 최적화를 켜면 확대창 크기와 상태정보·측정·미리보기 갱신 빈도를 낮춰 CPU/GPU 부하를 줄입니다.",
                    "클릭하면 선택 색상의 RGB 값을 클립보드에 복사합니다. Esc는 취소입니다."
                }, fontName);

                AppendSection("4. 편집", new string[] {
                    "펜 / 형광펜 / 사각형 / 원형 / 화살표 / 체크 / 선택 / 텍스트 / 지우개 / 확장 지우개 / 자르기를 사용할 수 있습니다.",
                    "사각형과 원형은 '채우기'를 켜면 선색과 같은 색으로 채워집니다.",
                    "선택된 색상은 팔레트의 체크 표시와 활성 도구의 색상 표시점으로 확인합니다.",
                    "되돌리기 Ctrl+Z · 다시 실행 Ctrl+Y · 복사 Ctrl+C · 저장 Ctrl+S · 인쇄 Ctrl+P"
                }, fontName);

                AppendSection("5. 이미지에서 색상 추출", new string[] {
                    "복사 버튼 왼쪽의 스포이드 아이콘을 선택한 뒤 캡처 이미지 위에서 움직입니다.",
                    "화면 컬러피커와 같은 확대창·픽셀 격자·중앙 십자선·HEX/RGB/좌표/배율 정보를 표시합니다.",
                    "마우스 휠 또는 Ctrl+마우스 휠로 6×~128× 배율을 조절합니다. 이때 이미지 자체 확대/스크롤은 실행되지 않습니다.",
                    "클릭하면 해당 이미지 픽셀 색상을 선택하고 RGB 값을 복사합니다."
                }, fontName);

                AppendSection("6. 텍스트", new string[] {
                    "텍스트 도구를 선택한 뒤 이미지에서 시작 위치를 클릭하면 입력 상자가 생성됩니다.",
                    "기본 영역은 클릭 지점부터 이미지 오른쪽·아래 끝의 10px 안쪽까지입니다.",
                    "상단 테두리를 드래그해 이동하고 모서리 핸들로 크기를 바꿀 수 있습니다.",
                    "Enter는 줄바꿈 · Ctrl+Enter는 확정 · Esc는 취소입니다.",
                    "입력 상자 밖, 다른 프로그램 또는 바탕화면을 클릭해도 입력이 확정됩니다."
                }, fontName);

                AppendSection("7. 확대와 상태줄", new string[] {
                    "Ctrl+마우스 휠: 이미지 25%~1000% 확대·축소",
                    "Ctrl+0: 이미지 100%",
                    "하단 상태줄: 포인터 색상 · 좌표 · 크기 · 이미지 크기 · 현재 확대율. 편집 도구가 없는 상태에서도 이미지 위를 드래그하면 크기를 잴 수 있습니다."
                }, fontName);

                AppendSection("8. 저장·인쇄·설정·트레이", new string[] {
                    "PNG는 투명 픽셀을 유지합니다. JPG 저장 시 투명 부분은 흰색으로 처리합니다.",
                    "편집줄의 인쇄 버튼 또는 Ctrl+P로 CapPicker 스타일의 큰 인쇄 미리보기를 먼저 확인할 수 있습니다. 미리보기에서 세로/가로 방향을 바꿀 수 있고, 인쇄 버튼을 누르면 Windows 인쇄 창에서 프린터 설정을 확인한 뒤 현재 이미지를 페이지에 맞춰 가운데 인쇄합니다.",
                    "상단 HEX/RGB 결과 오른쪽의 설정 아이콘에서 언어, 최소화 버튼, 닫기 버튼 동작과 성능옵션을 선택할 수 있습니다.",
                    "성능옵션 기본값은 일반(권장)이며, 저사양 PC 최적화는 컬러피커 확대창, 상태정보, 측정/미리보기의 실시간 갱신 빈도와 렌더링 부하를 낮춥니다.",
                    "기본값은 최소화=작업표시줄, 닫기=트레이입니다. Win+D는 Windows 기본 동작을 유지합니다.",
                    "CapPicker가 실행 중이면 트레이 아이콘은 항상 표시되며, 아이콘을 오른쪽 클릭해 언제든 종료할 수 있습니다."
                }, fontName);
            }
            else
            {
                AppendSection("1. Quick start", new string[] {
                    "Rectangle capture  —  Alt + Shift + S",
                    "Screen Color Picker  —  Alt + Shift + C",
                    "Help  —  F1",
                    "Every completed capture is automatically copied to the clipboard before editing."
                }, fontName);

                AppendSection("2. Capture", new string[] {
                    "Rectangle: drag the area you want to capture.",
                    "Fixed Size: set width and height first, then choose the position.",
                    "Window: select a target window. CapPicker prefers direct window rendering and automatically falls back to the visible screen area when needed.",
                    "Full Screen: capture the entire virtual desktop.",
                    "Last Region: capture the most recently used region again."
                }, fontName);

                AppendSection("3. Screen Color Picker", new string[] {
                    "Move the mouse to see the magnifier, pixel grid, center crosshair, and live HEX/RGB values.",
                    "Use the mouse wheel to zoom from 6× to 128×.",
                    "In Normal mode, the magnifier window grows to 1.5× its normal size above 64×.",
                    "Low-spec PC optimization reduces magnifier size and lowers status, measurement, preview, and live picker refresh rates to reduce CPU/GPU usage.",
                    "Click to copy the selected RGB value. Press Esc to cancel."
                }, fontName);

                AppendSection("4. Edit", new string[] {
                    "Pen / Highlighter / Rectangle / Ellipse / Arrow / Check / Select / Text / Eraser / Pixel Eraser / Crop are available.",
                    "Rectangle and Ellipse can be filled with the same color as their outline.",
                    "The selected color is shown by a check mark on the palette and a color indicator on the active tool.",
                    "Undo Ctrl+Z · Redo Ctrl+Y · Copy Ctrl+C · Save Ctrl+S · Print Ctrl+P"
                }, fontName);

                AppendSection("5. Pick color from image", new string[] {
                    "Select the eyedropper immediately to the left of Copy, then move over the captured image.",
                    "It uses the same magnifier, pixel grid, center crosshair, and HEX/RGB/XY/Zoom information as the screen picker.",
                    "Use the mouse wheel or Ctrl+mouse wheel to zoom from 6× to 128×. The image itself will not zoom or scroll while this tool is active.",
                    "Click to select the exact image pixel color and copy its RGB value."
                }, fontName);

                AppendSection("6. Text", new string[] {
                    "Select Text and click the image to create an editing box.",
                    "Its default bounds extend from the clicked point to 10 px inside the image's right and bottom edges.",
                    "Drag the top border to move the box and use corner handles to resize it.",
                    "Enter inserts a line break · Ctrl+Enter commits · Esc cancels.",
                    "Clicking outside the box, another app, or the desktop also commits the text."
                }, fontName);

                AppendSection("7. Zoom and status bar", new string[] {
                    "Ctrl+mouse wheel: zoom the image from 25% to 1000%.",
                    "Ctrl+0: return the image to 100%.",
                    "The bottom status bar shows pointer color · coordinates · size · image size · zoom level. With no edit tool selected, drag on the image to measure a region."
                }, fontName);

                AppendSection("8. Save, print, settings, and tray", new string[] {
                    "PNG preserves transparent pixels. Transparent areas are rendered white when saving as JPEG.",
                    "Use the Print button on the edit row or Ctrl+P to open the large CapPicker-style print preview. You can switch portrait/landscape there, then choose Print to open the Windows print dialog; after confirming printer settings, the image is fit and centered on the printer page.",
                    "Use the Settings icon to the right of the top HEX/RGB result to choose language, title-bar minimize, close-button actions, and performance options.",
                    "Performance options default to Normal (Recommended); Low-spec PC optimization reduces magnifier size plus status, measurement, and preview refresh load.",
                    "Defaults are Minimize=Taskbar and Close=Tray. Win+D keeps the normal Windows behavior.",
                    "The tray icon remains visible while CapPicker is running; right-click it to exit at any time."
                }, fontName);
            }
        }

        private void AppendTitle(string text, string fontName)
        {
            helpText.SelectionStart = helpText.TextLength;
            helpText.SelectionLength = 0;
            helpText.SelectionColor = Color.White;
            using (Font f = new Font(fontName, 14f, FontStyle.Bold))
                helpText.SelectionFont = f;
            helpText.AppendText(text + Environment.NewLine);
        }

        private void AppendLead(string text, string fontName)
        {
            helpText.SelectionStart = helpText.TextLength;
            helpText.SelectionColor = AppTheme.MutedText;
            using (Font f = new Font(fontName, 9.6f, FontStyle.Regular))
                helpText.SelectionFont = f;
            helpText.AppendText(text + Environment.NewLine + Environment.NewLine);
        }

        private void AppendSection(string heading, string[] bullets, string fontName)
        {
            helpText.SelectionStart = helpText.TextLength;
            helpText.SelectionColor = Color.FromArgb(245, 245, 245);
            using (Font headingFont = new Font(fontName, 10.8f, FontStyle.Bold))
                helpText.SelectionFont = headingFont;
            helpText.AppendText(heading + Environment.NewLine);

            for (int i = 0; i < bullets.Length; i++)
            {
                helpText.SelectionStart = helpText.TextLength;
                helpText.SelectionColor = AppTheme.Text;
                using (Font bulletFont = new Font(fontName, 9.5f, FontStyle.Regular))
                    helpText.SelectionFont = bulletFont;
                helpText.SelectionIndent = 4;
                helpText.SelectionHangingIndent = 14;
                helpText.AppendText("•  " + bullets[i] + Environment.NewLine);
                helpText.SelectionIndent = 0;
                helpText.SelectionHangingIndent = 0;
            }
            helpText.AppendText(Environment.NewLine);
        }

    }
}
