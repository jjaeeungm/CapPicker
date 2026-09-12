using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CapPicker
{
    internal sealed class MainForm : Form
    {
        private const int RECT_HOTKEY_ID = 4101;
        private const int COLOR_HOTKEY_ID = 4102;

        private bool rectHotkeyRegistered;
        private bool colorHotkeyRegistered;
        private bool hotkeysInitialized;
        private bool trayMode;
        private bool busy;
        private bool interactionWasTray;
        private bool captionMinimizePressed;
        private bool forceExit;
        private FormWindowState restoreWindowState = FormWindowState.Normal;

        private Rectangle lastRegion = Rectangle.Empty;
        private Size fixedSize = new Size(800, 600);

        private NotifyIcon trayIcon;
        private ColorPickerController colorPicker;
        private WindowPickerController windowPicker;

        private FlowLayoutPanel captureBar;
        private FlowLayoutPanel editBar;
        private ToolOptionsBar optionsBar;
        private Panel workspace;
        private Panel emptyState;
        private Label emptyText;
        private ColorResultChip colorResult;
        private EditorCanvas canvas;

        private Panel statusBar;
        private Label statusColor;
        private Label statusPointer;
        private Label statusSize;
        private Label statusImage;
        private Label statusZoom;

        private readonly Dictionary<EditorTool, FlatButton> toolButtons = new Dictionary<EditorTool, FlatButton>();
        private readonly Dictionary<EditorTool, ToolStyle> styles = new Dictionary<EditorTool, ToolStyle>();
        private EditorTool activeTool = EditorTool.None;

        // The UI was tuned against the 1920x1080 / 125% reference screen.
        // All code-created geometry is therefore expressed in 120-DPI reference pixels.
        // Runtime layout scales once from that reference to the current monitor DPI.
        private int layoutUiDpi = DpiUtil.ReferenceDpi;
        private bool initialDpiLayoutApplied;

        private FlatButton rectangleCaptureButton;
        private FlatButton fixedCaptureButton;
        private FlatButton windowCaptureButton;
        private FlatButton fullScreenButton;
        private FlatButton lastRegionButton;
        private FlatButton screenPickerButton;

        private FlatButton undoButton;
        private FlatButton redoButton;
        private FlatButton rotateLeftButton;
        private FlatButton rotateRightButton;
        private FlatButton copyButton;
        private FlatButton saveButton;
        private FlatButton printButton;
        private FlatButton settingsButton;
        private Control captureRightAlignGap;
        private Control editRightAlignGap;

        private ToolTip toolTip;

        public MainForm()
        {
            Text = "CapPicker " + L10n.Version;
            // Controls are created in 120-DPI reference pixels. Disable WinForms implicit
            // autoscaling and apply one explicit Per-Monitor scale after the HWND knows its DPI.
            // This avoids the previous mix of 96-DPI geometry, point-font DPI scaling and a
            // separate 125% visual cap.
            AutoScaleMode = AutoScaleMode.None;

            // 실제 최소 폭/높이는 MainShown에서 현재 모니터 DPI를 확인한 뒤
            // 툴바 구성으로 계산합니다. 여기서는 표시 전 임시 ClientSize만 둡니다.
            ClientSize = new Size(1, 198);
            MinimumSize = Size.Empty;
            StartPosition = FormStartPosition.Manual;

            // v3에서 사용했던 밝고 부드러운 중성 회색 톤으로 복원.
            BackColor = AppTheme.Window;
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;

            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { Icon = SystemIcons.Application; }

            InitStyles();
            L10n.LanguageChanged += OnLanguageChanged;
            BuildUi();
            // Do not lock MinimumSize while the controls are still in their 120-DPI
            // reference geometry. The real monitor DPI is only reliable after the HWND
            // exists; MainShown applies the initial DPI scale and then derives the minimum
            // toolbar width from that scaled layout. This avoids a stale 120-DPI minimum
            // width preventing the window from shrinking correctly on a 96-DPI monitor.
            BuildTray();

            Shown += MainShown;
            FormClosing += MainFormClosing;
            FormClosed += MainClosed;
            Resize += MainResize;
            Deactivate += MainDeactivated;
            DpiChanged += MainDpiChanged;
            KeyPreview = true;
            KeyDown += MainKeyDown;
        }

        private void InitStyles()
        {
            styles[EditorTool.Pen] = new ToolStyle(Color.FromArgb(220, 50, 47), 4, 24, L10n.DefaultFontName);
            styles[EditorTool.Highlighter] = new ToolStyle(Color.FromArgb(255, 235, 59), 22, 24, L10n.DefaultFontName);
            styles[EditorTool.Rectangle] = new ToolStyle(Color.FromArgb(220, 50, 47), 4, 24, L10n.DefaultFontName);
            styles[EditorTool.Ellipse] = new ToolStyle(Color.FromArgb(220, 50, 47), 4, 24, L10n.DefaultFontName);
            styles[EditorTool.Arrow] = new ToolStyle(Color.FromArgb(220, 50, 47), 4, 24, L10n.DefaultFontName);
            styles[EditorTool.Check] = new ToolStyle(Color.FromArgb(220, 50, 47), 6, 24, L10n.DefaultFontName);
            styles[EditorTool.Emoji] = new ToolStyle(Color.White, 1, 48, "😊");
            styles[EditorTool.Text] = new ToolStyle(Color.FromArgb(220, 50, 47), 4, 24, L10n.DefaultFontName);
            styles[EditorTool.Eraser] = new ToolStyle(Color.Black, 24, 24, L10n.DefaultFontName);
            styles[EditorTool.PixelEraser] = new ToolStyle(Color.Black, 40, 24, L10n.DefaultFontName);
            styles[EditorTool.Crop] = new ToolStyle(Color.Black, 2, 24, L10n.DefaultFontName);
            styles[EditorTool.SampleColor] = new ToolStyle(Color.Black, 1, 24, L10n.DefaultFontName);
        }

        private void BuildUi()
        {
            toolTip = new ToolTip();
            toolTip.AutoPopDelay = 6000;
            toolTip.InitialDelay = 350;
            toolTip.ReshowDelay = 80;
            toolTip.ShowAlways = true;

            statusBar = BuildStatusBar();

            workspace = new Panel();
            workspace.Dock = DockStyle.Fill;
            workspace.AutoScroll = true;
            workspace.BackColor = AppTheme.Workspace;
            workspace.Padding = new Padding(8);
            // 캔버스가 작업영역보다 작으면 가운데 배치하고, 큰 경우에는 스크롤 가능한 좌상단 기준을 유지합니다.
            workspace.Resize += delegate { CenterEmptyState(); UpdateCanvasExtent(); };
            // 캔버스 바깥의 빈 작업영역을 클릭해도 현재 텍스트 입력을 확정합니다.
            workspace.MouseDown += delegate
            {
                if (canvas != null && canvas.IsTextEditing) canvas.CommitActiveText();
            };
            Controls.Add(workspace);

            emptyState = new Panel();
            emptyState.Dock = DockStyle.Fill;
            emptyState.BackColor = AppTheme.Workspace;

            emptyText = new Label();
            emptyText.Text = L10n.T(
                "직사각형 캡처    Alt + Shift + S\r\n컬러피커 실행    Alt + Shift + C\r\n도움말    F1",
                "Rectangle Capture    Alt + Shift + S\r\nColor Picker    Alt + Shift + C\r\nHelp    F1");
            emptyText.Font = new Font(L10n.DefaultFontName, 11.0f, FontStyle.Regular);
            emptyText.ForeColor = AppTheme.MutedText;
            emptyText.AutoSize = false;
            emptyText.Dock = DockStyle.Fill;
            emptyText.TextAlign = ContentAlignment.MiddleCenter;
            emptyText.Padding = new Padding(0, 0, 0, 4);
            emptyState.Controls.Add(emptyText);
            workspace.Controls.Add(emptyState);

            optionsBar = new ToolOptionsBar();
            optionsBar.SettingsChanged += OptionsChanged;
            optionsBar.TextInsertRequested += delegate(string value)
            {
                if (canvas != null) canvas.InsertText(value);
            };
            Controls.Add(optionsBar);

            editBar = BuildEditBar();
            Controls.Add(editBar);

            captureBar = BuildCaptureBar();
            Controls.Add(captureBar);

            // 마지막에 추가해 Dock=Bottom이 Fill 영역과 겹치지 않도록 순서를 고정합니다.
            Controls.Add(statusBar);

            SetEditorEnabled(false);
            CenterEmptyState();
            ResetStatus();
        }

        private Panel BuildStatusBar()
        {
            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 32;
            bar.BackColor = AppTheme.Status;

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.WrapContents = false;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.Padding = new Padding(10, 2, 8, 2);
            flow.BackColor = bar.BackColor;
            bar.Controls.Add(flow);

            statusColor = MakeStatusLabel(L10n.T("색상 -", "Color -"), L10n.IsKorean ? 220 : 240);
            statusPointer = MakeStatusLabel(L10n.T("좌표 -", "XY -"), 130);
            statusSize = MakeStatusLabel(L10n.T("크기 -", "Size -"), 145);
            statusImage = MakeStatusLabel(L10n.T("이미지 -", "Image -"), 150);
            statusZoom = MakeStatusLabel(L10n.T("확대 100%", "Zoom 100%"), 100);

            flow.Controls.Add(statusColor);
            flow.Controls.Add(MakeStatusDivider());
            flow.Controls.Add(statusPointer);
            flow.Controls.Add(MakeStatusDivider());
            flow.Controls.Add(statusSize);
            flow.Controls.Add(MakeStatusDivider());
            flow.Controls.Add(statusImage);
            flow.Controls.Add(MakeStatusDivider());
            flow.Controls.Add(statusZoom);

            return bar;
        }

        private static Label MakeStatusLabel(string text, int width)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Size = new Size(width, 28);
            l.Margin = new Padding(0);
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.ForeColor = AppTheme.MutedText;
            l.Font = new Font("Segoe UI", 8.2f);
            return l;
        }

        private static Label MakeStatusDivider()
        {
            Label l = MakeStatusLabel("│", 18);
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.ForeColor = Color.FromArgb(135, 135, 135);
            return l;
        }

        private FlowLayoutPanel BuildCaptureBar()
        {
            FlowLayoutPanel bar = NewToolbar(56);
            bar.Padding = new Padding(10, 8, 10, 8);

            int buttonWidth = CalculateCaptureButtonWidth();
            rectangleCaptureButton = MakeCaptureButton(L10n.T("직사각형", "Rectangle"), AppIcon.RectangleCapture, buttonWidth, StartRectangleCapture);
            fixedCaptureButton = MakeCaptureButton(L10n.T("크기지정", "Fixed Size"), AppIcon.SizeCapture, buttonWidth, StartFixedCapture);
            windowCaptureButton = MakeCaptureButton(L10n.T("윈도우창", "Window"), AppIcon.WindowCapture, buttonWidth, StartWindowCapture);
            fullScreenButton = MakeCaptureButton(L10n.T("전체화면", "Full Screen"), AppIcon.FullScreen, buttonWidth, CaptureFullScreen);
            lastRegionButton = MakeCaptureButton(L10n.T("지난영역", "Last Region"), AppIcon.History, buttonWidth, CaptureLastRegion);
            bar.Controls.Add(rectangleCaptureButton);
            bar.Controls.Add(fixedCaptureButton);
            bar.Controls.Add(windowCaptureButton);
            bar.Controls.Add(fullScreenButton);
            bar.Controls.Add(lastRegionButton);

            captureRightAlignGap = MakeGap(0);
            bar.Controls.Add(captureRightAlignGap);

            ToolbarSeparator captureSeparator = new ToolbarSeparator();
            captureSeparator.Margin = new Padding(18, 0, 18, 0);
            bar.Controls.Add(captureSeparator);

            screenPickerButton = MakeCaptureButton(L10n.T("컬러피커", "Color Picker"), AppIcon.Eyedropper, buttonWidth, StartColorPicker);
            screenPickerButton.Margin = new Padding(0, 0, 12, 0);
            bar.Controls.Add(screenPickerButton);

            colorResult = new ColorResultChip();
            colorResult.ClearValue();
            // Keep the result compact enough to match the approved 125% toolbar density.
            colorResult.Size = new Size(180, 40);
            colorResult.Margin = new Padding(0, 0, 0, 0);
            bar.Controls.Add(colorResult);

            ToolbarSeparator settingsSeparator = new ToolbarSeparator();
            settingsSeparator.Margin = new Padding(8, 0, 8, 0);
            bar.Controls.Add(settingsSeparator);

            settingsButton = MakeIconButton(AppIcon.Settings, L10n.T("설정", "Settings"));
            settingsButton.Margin = new Padding(0, 0, 0, 0);
            settingsButton.Click += delegate { ShowSettings(); };
            bar.Controls.Add(settingsButton);

            return bar;
        }

        private FlowLayoutPanel BuildEditBar()
        {
            FlowLayoutPanel bar = NewToolbar(56);
            bar.Padding = new Padding(10, 8, 10, 8);

            AddToolIconButton(bar, AppIcon.Pen, EditorTool.Pen, L10n.T("일반펜", "Pen"));
            AddToolIconButton(bar, AppIcon.Highlighter, EditorTool.Highlighter, L10n.T("형광펜", "Highlighter"));
            AddToolIconButton(bar, AppIcon.Rectangle, EditorTool.Rectangle, L10n.T("사각형 그리기", "Rectangle"));
            AddToolIconButton(bar, AppIcon.Ellipse, EditorTool.Ellipse, L10n.T("원형 그리기", "Ellipse"));
            AddToolIconButton(bar, AppIcon.Arrow, EditorTool.Arrow, L10n.T("화살표", "Arrow"));
            AddToolIconButton(bar, AppIcon.Check, EditorTool.Check, L10n.T("강조 체크", "Check mark"));
            AddToolIconButton(bar, AppIcon.Emoji, EditorTool.Emoji, L10n.T("선택", "Select"));
            AddToolIconButton(bar, AppIcon.Text, EditorTool.Text, L10n.T("텍스트 입력", "Text"));
            AddToolIconButton(bar, AppIcon.Eraser, EditorTool.Eraser, L10n.T("지우개 - 편집 내용을 원본으로 되돌립니다.", "Eraser - restores edited areas to the original image."));
            AddToolIconButton(bar, AppIcon.PixelEraser, EditorTool.PixelEraser, L10n.T("확장 지우개 - 이미지 픽셀 자체를 지웁니다. PNG는 투명, JPG는 흰색.", "Pixel eraser - removes image pixels. Transparent in PNG, white in JPG."));
            AddToolIconButton(bar, AppIcon.Crop, EditorTool.Crop, L10n.T("자르기", "Crop"));

            bar.Controls.Add(MakeGap(0));

            rotateLeftButton = MakeIconButton(AppIcon.RotateLeft, L10n.T("왼쪽 회전", "Rotate left"));
            rotateLeftButton.Click += delegate { if (canvas != null) canvas.RotateLeft(); };
            bar.Controls.Add(rotateLeftButton);

            rotateRightButton = MakeIconButton(AppIcon.RotateRight, L10n.T("오른쪽 회전", "Rotate right"));
            rotateRightButton.Click += delegate { if (canvas != null) canvas.RotateRight(); };
            bar.Controls.Add(rotateRightButton);

            ToolbarSeparator historySeparator = new ToolbarSeparator();
            historySeparator.Margin = new Padding(12, 0, 12, 0);
            bar.Controls.Add(historySeparator);

            undoButton = MakeIconButton(AppIcon.Undo, L10n.T("되돌리기 (Ctrl+Z)", "Undo (Ctrl+Z)"));
            undoButton.Click += delegate { if (canvas != null) canvas.Undo(); };
            bar.Controls.Add(undoButton);

            redoButton = MakeIconButton(AppIcon.Redo, L10n.T("다시 실행 (Ctrl+Y)", "Redo (Ctrl+Y)"));
            redoButton.Click += delegate { if (canvas != null) canvas.Redo(); };
            bar.Controls.Add(redoButton);

            // Image-local color picker belongs with the editing/history tools rather than
            // the output actions. Keep it immediately beside Redo as requested.
            AddToolIconButton(bar, AppIcon.Eyedropper, EditorTool.SampleColor, L10n.T("이미지에서 색상 추출", "Pick color from image"));

            editRightAlignGap = MakeGap(0);
            bar.Controls.Add(editRightAlignGap);

            ToolbarSeparator outputSeparator = new ToolbarSeparator();
            outputSeparator.Margin = new Padding(8, 0, 8, 0);
            bar.Controls.Add(outputSeparator);

            copyButton = MakeEditButton(L10n.T("복사", "Copy"), AppIcon.Copy, CalculateEditButtonWidth(L10n.T("복사", "Copy"), 78));
            copyButton.Click += CopyImage;
            bar.Controls.Add(copyButton);

            saveButton = MakeEditButton(L10n.T("저장", "Save"), AppIcon.Save, CalculateEditButtonWidth(L10n.T("저장", "Save"), 78));
            saveButton.Click += SaveImage;
            bar.Controls.Add(saveButton);

            printButton = MakeEditButton(L10n.T("인쇄", "Print"), AppIcon.Print, CalculateEditButtonWidth(L10n.T("인쇄", "Print"), 78));
            printButton.Margin = new Padding(0, 0, 0, 0);
            printButton.Click += PrintImage;
            toolTip.SetToolTip(printButton, L10n.T("인쇄 (Ctrl+P)", "Print (Ctrl+P)"));
            bar.Controls.Add(printButton);

            return bar;
        }

        private static Control MakeGap(int width)
        {
            Panel p = new Panel();
            p.Size = new Size(width, 1);
            p.Margin = Padding.Empty;
            return p;
        }

        private static FlowLayoutPanel NewToolbar(int height)
        {
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = height;
            bar.WrapContents = false;
            bar.AutoScroll = false;
            bar.FlowDirection = FlowDirection.LeftToRight;
            bar.BackColor = AppTheme.Toolbar;
            return bar;
        }

        private int CalculateCaptureButtonWidth()
        {
            string[] labels = new string[]
            {
                L10n.T("직사각형", "Rectangle"),
                L10n.T("크기지정", "Fixed Size"),
                L10n.T("윈도우창", "Window"),
                L10n.T("전체화면", "Full Screen"),
                L10n.T("지난영역", "Last Region"),
                L10n.T("컬러피커", "Color Picker")
            };

            int width = 106;
            using (Font f = CreateReferenceMeasureFont(9f))
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    int measured = TextRenderer.MeasureText(labels[i], f).Width + 48;
                    width = Math.Max(width, measured);
                }
            }
            return Math.Min(132, width);
        }

        private static int CalculateEditButtonWidth(string text, int minimum)
        {
            using (Font f = CreateReferenceMeasureFont(9f))
            {
                return Math.Max(minimum, TextRenderer.MeasureText(text, f).Width + 46);
            }
        }

        private static Font CreateReferenceMeasureFont(float points)
        {
            // Text measurement must not depend on the monitor where the process starts.
            // 9pt at the 120-DPI reference screen is 15 device pixels.
            float pixels = points * DpiUtil.ReferenceDpi / 72f;
            return new Font("Segoe UI", pixels, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private FlatButton MakeCaptureButton(string text, AppIcon icon, int width, EventHandler handler)
        {
            FlatButton b = new FlatButton();
            b.Text = text;
            b.IconKind = icon;
            b.Compact = true;
            b.Size = new Size(width, 40);
            // Caption widths are measured at the fixed 120-DPI reference scale, so the
            // initial font does not need monitor-dependent shrinking here.
            b.Margin = new Padding(0, 0, 7, 0);
            b.Click += handler;
            toolTip.SetToolTip(b, text);
            return b;
        }

        private FlatButton MakeEditButton(string text, AppIcon icon, int width)
        {
            FlatButton b = new FlatButton();
            b.Text = text;
            b.IconKind = icon;
            b.Compact = true;
            b.Size = new Size(width, 40);
            b.Margin = new Padding(0, 0, 5, 0);
            return b;
        }

        private FlatButton MakeIconButton(AppIcon icon, string tip)
        {
            FlatButton b = new FlatButton();
            b.Text = "";
            b.IconKind = icon;
            b.IconOnly = true;
            b.Size = new Size(40, 40);
            b.Margin = new Padding(0, 0, 4, 0);
            toolTip.SetToolTip(b, tip);
            return b;
        }

        private void AddToolIconButton(FlowLayoutPanel bar, AppIcon icon, EditorTool tool, string tip)
        {
            FlatButton b = MakeIconButton(icon, tip);
            b.Tag = tool;
            b.Click += delegate { SetTool((EditorTool)b.Tag); };
            // 캡처 전에는 편집 도구가 비활성 상태이므로 색상 표시점도 숨깁니다.
            b.IndicatorColor = Color.Empty;
            toolButtons[tool] = b;
            bar.Controls.Add(b);
        }

        private void AlignToolbarRightEdges()
        {
            if (captureBar == null || editBar == null || captureRightAlignGap == null || editRightAlignGap == null) return;

            // Both rows start at the same X. Put any compensation before the final action
            // group so the group stays visually compact while the visible right edges
            // (Settings above, Print below) land on the same vertical line.
            captureRightAlignGap.Width = 0;
            editRightAlignGap.Width = 0;
            int captureWidth = MeasureToolbarWidth(captureBar);
            int editWidth = MeasureToolbarWidth(editBar);
            if (captureWidth < editWidth)
                captureRightAlignGap.Width = editWidth - captureWidth;
            else if (editWidth < captureWidth)
                editRightAlignGap.Width = captureWidth - editWidth;
        }

        private static int MeasureToolbarWidth(FlowLayoutPanel bar)
        {
            if (bar == null) return 1;

            // 중요: Form이 아직 Show되기 전에는 부모가 보이지 않는다는 이유로
            // 자식 Control.Visible도 false로 평가될 수 있습니다.
            // 초기 최소폭 계산에서 Visible을 검사하면 모든 버튼 폭이 빠져
            // 창이 비정상적으로 좁아집니다. 툴바에 배치된 구성요소는 전부 계산합니다.
            int width = bar.Padding.Left + bar.Padding.Right;
            foreach (Control c in bar.Controls)
            {
                width += c.Width + c.Margin.Left + c.Margin.Right;
            }

            // DPI/비클라이언트 반올림과 마지막 컨트롤 테두리 여유.
            return width + 12;
        }

        private void BuildTray()
        {
            trayIcon = new NotifyIcon();
            trayIcon.Icon = Icon;
            trayIcon.Text = "CapPicker " + L10n.Version;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += delegate { RestoreFromTray(); };
            RebuildTrayMenu();
        }

        private void RebuildTrayMenu()
        {
            if (trayIcon == null) return;
            ContextMenuStrip old = trayIcon.ContextMenuStrip;

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem rect = new ToolStripMenuItem(L10n.T("직사각형 캡처", "Rectangle capture"));
            rect.Click += StartRectangleCapture;
            ToolStripMenuItem window = new ToolStripMenuItem(L10n.T("윈도우 캡처", "Window capture"));
            window.Click += StartWindowCapture;
            ToolStripMenuItem full = new ToolStripMenuItem(L10n.T("전체 화면 캡처", "Full screen capture"));
            full.Click += CaptureFullScreen;
            ToolStripMenuItem color = new ToolStripMenuItem(L10n.T("컬러피커", "Color picker"));
            color.Click += StartColorPicker;
            ToolStripMenuItem open = new ToolStripMenuItem(L10n.T("창 열기", "Open window"));
            open.Click += delegate { RestoreFromTray(); };
            ToolStripMenuItem help = new ToolStripMenuItem(L10n.T("도움말 (F1)", "Help (F1)"));
            help.Click += delegate { ShowHelp(); };
            ToolStripMenuItem exit = new ToolStripMenuItem(L10n.T("종료", "Exit"));
            exit.Click += delegate
            {
                forceExit = true;
                trayMode = false;
                trayIcon.Visible = false;
                Close();
            };

            menu.Items.Add(rect);
            menu.Items.Add(window);
            menu.Items.Add(full);
            menu.Items.Add(color);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(open);
            menu.Items.Add(help);
            menu.Items.Add(exit);
            trayIcon.ContextMenuStrip = menu;

            if (old != null) old.Dispose();
        }

        private void MainShown(object sender, EventArgs e)
        {
            // Once the HWND exists, scale the 120-DPI reference layout to the monitor DPI.
            // Example: a 40px reference button stays 40px at 125%, becomes 64px at 200%,
            // and 32px at 100%, preserving one logical size across displays.
            ApplyInitialDpiLayout();
            ApplyCompactVerticalMetrics();
            UpdateMinimumToolbarWidth();

            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int rightMargin = ScaleUiMetric(30);
            int topMargin = ScaleUiMetric(48);
            Location = new Point(
                Math.Max(wa.Left, wa.Right - Width - rightMargin),
                Math.Max(wa.Top, wa.Top + topMargin));
            ApplyWindowTone();

            colorPicker = new ColorPickerController(this);
            if (colorPicker.IsReady)
            {
                colorPicker.Picked += ColorPicked;
                colorPicker.Cancelled += InteractionCancelled;
            }

            windowPicker = new WindowPickerController(this);
            windowPicker.Selected += WindowSelected;
            windowPicker.Cancelled += InteractionCancelled;

            rectHotkeyRegistered = Native.RegisterHotKey(
                Handle, RECT_HOTKEY_ID, Native.MOD_ALT | Native.MOD_SHIFT, Native.VK_S);

            colorHotkeyRegistered = Native.RegisterHotKey(
                Handle, COLOR_HOTKEY_ID, Native.MOD_ALT | Native.MOD_SHIFT, Native.VK_C);
            hotkeysInitialized = true;

            // Keep capture hotkey conflict feedback symmetric for rectangle and color picker.
            UpdateHotkeyTooltips();

            // The HEX/RGB result chip is reserved for color values; initialization failures
            // may still be surfaced there because the picker itself is unavailable.
            if (!colorPicker.IsReady)
                toolTip.SetToolTip(colorResult, L10n.T("컬러피커 초기화 실패", "Color picker initialization failed"));
        }

        private void ApplyInitialDpiLayout()
        {
            if (initialDpiLayoutApplied) return;
            initialDpiLayoutApplied = true;

            int targetDpi = GetCurrentDpi();
            if (targetDpi <= 0) targetDpi = DpiUtil.ReferenceDpi;
            ScaleRuntimeLayout(targetDpi, true);
        }

        private int GetCurrentDpi()
        {
            try
            {
                if (IsHandleCreated)
                {
                    // On the affected 200% notebook Control.DeviceDpi can still report 96.
                    // GetDpiForWindow returns the real monitor DPI directly from Windows.
                    uint nativeDpi = Native.GetDpiForWindow(Handle);
                    if (nativeDpi > 0) return (int)nativeDpi;

                    if (DeviceDpi > 0) return DeviceDpi;
                }
            }
            catch { }
            // If native DPI probing fails, keep the reference appearance rather than
            // accidentally shrinking a high-DPI window to a 96-DPI assumption.
            return DpiUtil.ReferenceDpi;
        }

        private void MainDpiChanged(object sender, DpiChangedEventArgs e)
        {
            try
            {
                // The Form itself receives Windows' suggested bounds. Scale only the client UI
                // by the ratio between the old and new monitor DPI, then rebuild dynamic options.
                ScaleRuntimeLayout(e.DeviceDpiNew, false);
                BeginInvoke((MethodInvoker)delegate
                {
                    UpdateMinimumToolbarWidth();
                    if (activeTool != EditorTool.None && styles.ContainsKey(activeTool))
                        optionsBar.Configure(activeTool, styles[activeTool]);
                    CenterEmptyState();
                    UpdateCanvasExtent();
                });
            }
            catch { }
        }

        private static int NormalizeUiDpi(int actualDpi)
        {
            // Preserve one logical UI size across monitors. Common values are
            // 96/120/144/168/192 for 100/125/150/175/200%. The broad limits only
            // reject bogus values; unlike CompactDPI3 there is no 125% visual cap.
            if (actualDpi < 48) return DpiUtil.ReferenceDpi;
            if (actualDpi > 480) return 480;
            return actualDpi;
        }

        private void ScaleRuntimeLayout(int targetDpi, bool scaleClientSize)
        {
            if (targetDpi <= 0) targetDpi = DpiUtil.ReferenceDpi;

            int targetUiDpi = NormalizeUiDpi(targetDpi);
            if (layoutUiDpi <= 0) layoutUiDpi = DpiUtil.ReferenceDpi;
            if (targetUiDpi == layoutUiDpi) return;

            float factor = (float)targetUiDpi / (float)layoutUiDpi;
            if (factor <= 0.01f) return;

            SuspendLayout();
            try
            {
                Size oldClient = ClientSize;
                SizeF scale = new SizeF(factor, factor);

                // Scale each top-level client control recursively from the previous monitor
                // scale to the new one. Custom drawing uses the same reference factor.
                foreach (Control control in Controls)
                    control.Scale(scale);

                if (scaleClientSize)
                {
                    ClientSize = new Size(
                        Math.Max(1, (int)Math.Round(oldClient.Width * factor)),
                        Math.Max(1, (int)Math.Round(oldClient.Height * factor)));
                }

                layoutUiDpi = targetUiDpi;
                ApplyCompactVerticalMetrics();
            }
            finally
            {
                ResumeLayout(true);
                PerformLayout();
            }
        }

        private int ScaleUiMetric(int logical)
        {
            float f = layoutUiDpi / (float)DpiUtil.ReferenceDpi;
            if (f < 0.5f) f = 0.5f;
            if (f > 4f) f = 4f;
            return Math.Max(1, (int)Math.Round(logical * f));
        }

        private void ApplyCompactVerticalMetrics()
        {
            // Docked controls do not consistently preserve their scaled Height on
            // .NET Framework after layout. Re-apply the reference metrics explicitly.
            // All values below are 120-DPI reference pixels and use the same scale.
            int toolbarHeight = ScaleUiMetric(56);
            int toolbarPadX = ScaleUiMetric(10);
            int toolbarPadY = ScaleUiMetric(8);

            if (captureBar != null)
            {
                captureBar.Height = toolbarHeight;
                captureBar.Padding = new Padding(toolbarPadX, toolbarPadY, toolbarPadX, toolbarPadY);
            }
            if (editBar != null)
            {
                editBar.Height = toolbarHeight;
                editBar.Padding = new Padding(toolbarPadX, toolbarPadY, toolbarPadX, toolbarPadY);
            }
            if (optionsBar != null)
            {
                optionsBar.Height = toolbarHeight;
                optionsBar.Padding = new Padding(ScaleUiMetric(14), toolbarPadY, ScaleUiMetric(14), toolbarPadY);
            }

            if (colorResult != null)
                colorResult.Height = ScaleUiMetric(40);

            if (statusBar != null) statusBar.Height = ScaleUiMetric(32);
            Control statusFlow = statusBar != null && statusBar.Controls.Count > 0 ? statusBar.Controls[0] : null;
            FlowLayoutPanel sf = statusFlow as FlowLayoutPanel;
            if (sf != null)
                sf.Padding = new Padding(ScaleUiMetric(10), ScaleUiMetric(2), ScaleUiMetric(8), ScaleUiMetric(2));

            Label[] labels = new Label[] { statusColor, statusPointer, statusSize, statusImage, statusZoom };
            foreach (Label label in labels)
                if (label != null)
                {
                    label.Height = ScaleUiMetric(28);
                    if (label.Font != null) label.Font.Dispose();
                    label.Font = new Font("Segoe UI", 8.2f, FontStyle.Regular);
                }

            // Widths are DPI-dependent too. Recalculate them whenever monitor DPI changes
            // instead of waiting for a language change or pointer update.
            UpdateStatusLabelWidths();

            // Keep the approved 125% composition while adding a little breathing room
            // to the empty-state text and status bar: 3 x 56px rows + 98px workspace
            // + 32px status bar = 298px reference client height.
            if (emptyText != null)
                emptyText.Padding = new Padding(0, 0, 0, ScaleUiMetric(4));

            AlignToolbarRightEdges();

            int desiredClientHeight = ScaleUiMetric(298);
            if (canvas == null && ClientSize.Height < desiredClientHeight)
                ClientSize = new Size(ClientSize.Width, desiredClientHeight);
        }

        private void MainDeactivated(object sender, EventArgs e)
        {
            // 다른 앱/바탕화면 등 CapPicker 창 자체의 바깥을 클릭하면
            // 그림판처럼 현재 텍스트 편집을 확정합니다.
            if (canvas != null && canvas.IsTextEditing)
                canvas.CommitActiveText();
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !forceExit &&
                AppSettings.CloseBehavior == CloseButtonBehavior.Tray)
            {
                e.Cancel = true;
                MinimizeToTray();
            }
        }

        private void MainClosed(object sender, FormClosedEventArgs e)
        {
            L10n.LanguageChanged -= OnLanguageChanged;
            if (rectHotkeyRegistered) Native.UnregisterHotKey(Handle, RECT_HOTKEY_ID);
            if (colorHotkeyRegistered) Native.UnregisterHotKey(Handle, COLOR_HOTKEY_ID);
            if (windowPicker != null) windowPicker.Dispose();
            if (colorPicker != null) colorPicker.Dispose();
            if (canvas != null) canvas.Dispose();
            if (toolTip != null) toolTip.Dispose();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCLBUTTONDOWN = 0x00A1;
            const int HTMINBUTTON = 8;
            const int WM_SYSCOMMAND = 0x0112;
            const int SC_MINIMIZE = 0xF020;
            const int WM_DPICHANGED = 0x02E0;

            // Native fallback: some .NET Framework configurations do not raise the managed
            // DpiChanged event consistently. The X DPI is the low word of wParam.
            if (m.Msg == WM_DPICHANGED)
            {
                int newDpi = m.WParam.ToInt32() & 0xFFFF;
                if (newDpi > 0)
                    ScaleRuntimeLayout(newDpi, false);
            }

            // 트레이 이동은 사용자가 창의 '-' 캡션 버튼을 직접 누른 경우에만 수행합니다.
            // Win+D / 작업표시줄의 '바탕 화면 보기' / 일반적인 프로그램 최소화 명령은
            // Windows 기본 동작을 그대로 통과시켜 작업표시줄에 CapPicker을 남깁니다.
            if (m.Msg == WM_NCLBUTTONDOWN)
            {
                captionMinimizePressed = m.WParam.ToInt32() == HTMINBUTTON;
            }
            else if (m.Msg == WM_SYSCOMMAND)
            {
                int command = m.WParam.ToInt32() & 0xFFF0;
                if (command == SC_MINIMIZE && captionMinimizePressed &&
                    AppSettings.MinimizeBehavior == MinimizeButtonBehavior.Tray)
                {
                    captionMinimizePressed = false;
                    BeginInvoke((MethodInvoker)delegate { MinimizeToTray(); });
                    m.Result = IntPtr.Zero;
                    return;
                }
                captionMinimizePressed = false;
            }

            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == RECT_HOTKEY_ID)
                {
                    StartRectangleCapture(this, EventArgs.Empty);
                    return;
                }
                if (id == COLOR_HOTKEY_ID)
                {
                    StartColorPicker(this, EventArgs.Empty);
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private void MainResize(object sender, EventArgs e)
        {
            // 일반 최소화는 트레이로 보내지 않습니다.
            // 복원할 창 상태만 Normal/Maximized일 때 기억합니다.
            if (WindowState != FormWindowState.Minimized)
                restoreWindowState = WindowState;
        }

        private bool BeginInteraction()
        {
            if (busy) return false;
            busy = true;
            interactionWasTray = trayMode || !Visible;
            if (Visible) Hide();
            Application.DoEvents();
            return true;
        }

        private void InteractionCancelled()
        {
            busy = false;
            colorResult.Note = "";
            if (!interactionWasTray)
            {
                Show();
                WindowState = restoreWindowState;
                Activate();
            }
        }

        private void StartRectangleCapture(object sender, EventArgs e)
        {
            if (!BeginInteraction()) return;
            SelectionOverlay overlay = new SelectionOverlay(SelectionMode.Rectangle, Size.Empty);
            overlay.Selected += delegate(Rectangle r)
            {
                lastRegion = r;
                CaptureAndDisplay(r);
            };
            overlay.Cancelled += InteractionCancelled;
            overlay.Show();
        }

        private void StartFixedCapture(object sender, EventArgs e)
        {
            if (busy) return;
            using (FixedSizeDialog dialog = new FixedSizeDialog(fixedSize))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                Rectangle buttonRect = fixedCaptureButton.RectangleToScreen(fixedCaptureButton.ClientRectangle);
                int gap = ScaleUiMetric(6);

                // Give the dialog a provisional location on the button's monitor before
                // creating its HWND. FixedSizeDialog then reads the correct per-monitor DPI
                // and expands its 120-DPI reference layout exactly once.
                dialog.Location = new Point(buttonRect.Left, buttonRect.Bottom + gap);
                // Force HWND creation after the provisional monitor location so the dialog reads that monitor DPI.
                // The zero-handle branch is only a defensive fallback; reading Handle creates it.
                if (dialog.Handle == IntPtr.Zero) return;

                Rectangle wa = Screen.FromRectangle(buttonRect).WorkingArea;
                int x = buttonRect.Left;
                int y = buttonRect.Bottom + gap;

                if (x + dialog.Width > wa.Right) x = wa.Right - dialog.Width;
                if (x < wa.Left) x = wa.Left;
                if (y + dialog.Height > wa.Bottom) y = buttonRect.Top - gap - dialog.Height;
                if (y < wa.Top) y = wa.Top;
                dialog.Location = new Point(x, y);

                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                fixedSize = dialog.SelectedSize;
            }

            if (!BeginInteraction()) return;
            SelectionOverlay overlay = new SelectionOverlay(SelectionMode.FixedSize, fixedSize);
            overlay.Selected += delegate(Rectangle r)
            {
                lastRegion = r;
                CaptureAndDisplay(r);
            };
            overlay.Cancelled += InteractionCancelled;
            overlay.Show();
        }

        private void StartWindowCapture(object sender, EventArgs e)
        {
            if (!BeginInteraction()) return;
            if (!windowPicker.Start())
            {
                InteractionCancelled();
                MessageBox.Show(this, L10n.T("윈도우 선택을 시작하지 못했습니다.", "Could not start window selection."), "CapPicker");
            }
        }

        private void WindowSelected(IntPtr hwnd, Rectangle r)
        {
            lastRegion = r;
            try
            {
                // 선택 테두리가 DWM 합성 화면에서 완전히 사라질 시간을 한 프레임 이상 확보합니다.
                Thread.Sleep(55);
                Bitmap bmp = CaptureService.CaptureWindow(hwnd);
                DisplayCapturedBitmap(bmp);
            }
            catch (Exception ex)
            {
                InteractionCancelled();
                MessageBox.Show(this, L10n.T("윈도우 캡처 실패:\r\n", "Window capture failed:\r\n") + ex.Message, "CapPicker");
            }
        }

        private void CaptureFullScreen(object sender, EventArgs e)
        {
            if (!BeginInteraction()) return;
            try
            {
                Thread.Sleep(45);
                Bitmap bmp = CaptureService.CaptureVirtualScreen();
                lastRegion = SystemInformation.VirtualScreen;
                DisplayCapturedBitmap(bmp);
            }
            catch (Exception ex)
            {
                InteractionCancelled();
                MessageBox.Show(this, L10n.T("전체 화면 캡처 실패:\r\n", "Full screen capture failed:\r\n") + ex.Message, "CapPicker");
            }
        }

        private void CaptureLastRegion(object sender, EventArgs e)
        {
            if (lastRegion.IsEmpty)
            {
                MessageBox.Show(this, L10n.T("아직 지난 캡처 영역이 없습니다.", "There is no previous capture region yet."), "CapPicker");
                return;
            }
            if (!BeginInteraction()) return;
            CaptureAndDisplay(lastRegion);
        }

        private void CaptureAndDisplay(Rectangle r)
        {
            try
            {
                Thread.Sleep(35);
                Bitmap bmp = CaptureService.CaptureRectangle(r);
                DisplayCapturedBitmap(bmp);
            }
            catch (Exception ex)
            {
                InteractionCancelled();
                MessageBox.Show(this, L10n.T("캡처 실패:\r\n", "Capture failed:\r\n") + ex.Message, "CapPicker");
            }
        }

        private void DisplayCapturedBitmap(Bitmap bmp)
        {
            busy = false;
            trayMode = false;
            // The tray icon remains visible for the full application lifetime.

            EnsureEditorHeight();

            if (canvas != null)
            {
                workspace.Controls.Remove(canvas);
                canvas.Dispose();
                canvas = null;
            }

            try
            {
                canvas = new EditorCanvas(bmp);
            }
            finally
            {
                bmp.Dispose();
            }

            // 캡처가 완료되는 즉시 원본 캡처 이미지를 클립보드에도 넣습니다.
            // 이후 편집본은 사용자가 복사 버튼으로 다시 복사할 수 있습니다.
            try
            {
                using (Bitmap clip = canvas.ExportBitmap())
                    Clipboard.SetImage(clip);
            }
            catch { }

            canvas.HistoryChanged += delegate { UpdateHistoryButtons(); };
            canvas.ZoomChanged += delegate
            {
                UpdateCanvasExtent();
                statusZoom.Text = L10n.T("확대 ", "Zoom ") + canvas.ZoomPercent + "%";
            };
            canvas.PointerInfoChanged += UpdatePointerStatus;
            canvas.ImageColorPicked += ImageColorPicked;

            workspace.Controls.Add(canvas);
            canvas.BringToFront();
            emptyState.Visible = false;

            SetEditorEnabled(true);
            ClearToolSelection();
            UpdateHistoryButtons();
            UpdateImageStatus();
            workspace.AutoScrollPosition = Point.Empty;
            UpdateCanvasExtent();

            Show();
            WindowState = restoreWindowState;
            Activate();
        }

        private void SetEditorEnabled(bool enabled)
        {
            foreach (Control c in editBar.Controls)
            {
                FlatButton b = c as FlatButton;
                if (b != null) b.Enabled = enabled;
            }

            foreach (KeyValuePair<EditorTool, FlatButton> pair in toolButtons)
            {
                pair.Value.IndicatorColor = enabled && IsColorTool(pair.Key) && styles.ContainsKey(pair.Key)
                    ? styles[pair.Key].Color
                    : Color.Empty;
            }

            optionsBar.HideOptions();
        }

        private void ClearToolSelection()
        {
            activeTool = EditorTool.None;
            if (canvas != null) canvas.Tool = EditorTool.None;
            foreach (KeyValuePair<EditorTool, FlatButton> pair in toolButtons)
                pair.Value.Selected = false;
            optionsBar.HideOptions();
        }

        private void SetTool(EditorTool tool)
        {
            if (canvas == null) return;

            if (activeTool == tool)
            {
                ClearToolSelection();
                return;
            }

            activeTool = tool;
            foreach (KeyValuePair<EditorTool, FlatButton> pair in toolButtons)
                pair.Value.Selected = pair.Key == tool;

            ToolStyle style = styles[tool];
            canvas.Tool = tool;
            canvas.DrawColor = style.Color;
            canvas.StrokeWidth = style.Width;
            canvas.TextSize = style.TextSize;
            canvas.FontName = style.FontName;
            canvas.TextBold = style.TextBold;
            canvas.ShapeFill = style.Fill;
            if (tool == EditorTool.Emoji)
            {
                canvas.EmojiGlyph = style.FontName;
                canvas.EmojiSize = style.TextSize;
            }

            foreach (KeyValuePair<EditorTool, FlatButton> pair in toolButtons)
                pair.Value.IndicatorColor = IsColorTool(pair.Key) ? styles[pair.Key].Color : Color.Empty;

            if (tool == EditorTool.Crop || tool == EditorTool.SampleColor)
                optionsBar.HideOptions();
            else
                optionsBar.Configure(tool, style);
        }

        private static bool IsColorTool(EditorTool tool)
        {
            return tool == EditorTool.Pen || tool == EditorTool.Highlighter ||
                   tool == EditorTool.Rectangle || tool == EditorTool.Ellipse ||
                   tool == EditorTool.Arrow || tool == EditorTool.Check ||
                   tool == EditorTool.Text || tool == EditorTool.Emoji;
        }

        private void OptionsChanged(Color color, int width, int textSize, string fontName, bool fill, bool textBold)
        {
            if (activeTool == EditorTool.None) return;

            ToolStyle style = styles[activeTool];
            style.Color = color;
            style.Width = width;
            style.TextSize = textSize;
            style.FontName = fontName;
            style.Fill = fill;
            style.TextBold = textBold;

            FlatButton b;
            if (toolButtons.TryGetValue(activeTool, out b) && IsColorTool(activeTool))
                b.IndicatorColor = color;

            if (canvas != null)
            {
                canvas.DrawColor = color;
                canvas.StrokeWidth = width;
                canvas.TextSize = textSize;
                canvas.TextBold = textBold;
                canvas.ShapeFill = fill;

                if (activeTool == EditorTool.Emoji)
                {
                    canvas.EmojiGlyph = fontName;
                    canvas.EmojiSize = textSize;
                }
                else
                {
                    canvas.FontName = fontName;
                }
            }
        }

        private void ImageColorPicked(Color color, Point point)
        {
            string hex = String.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
            string rgb = String.Format("{0}, {1}, {2}", color.R, color.G, color.B);
            try { Clipboard.SetText(rgb); } catch { }

            colorResult.Value = color;
            statusColor.Text = String.Format(L10n.T("색상 {0} · RGB {1}, {2}, {3}", "Color {0} · RGB {1}, {2}, {3}"), hex, color.R, color.G, color.B);
            ClearToolSelection();
        }

        private void UpdateHistoryButtons()
        {
            bool has = canvas != null;
            undoButton.Enabled = has && canvas.CanUndo;
            redoButton.Enabled = has && canvas.CanRedo;
        }

        private void UpdateCanvasExtent()
        {
            if (canvas == null || workspace == null) return;

            int pad = ScaleUiMetric(8);
            int totalWidth = canvas.Width + pad * 2;
            int totalHeight = canvas.Height + pad * 2;
            workspace.AutoScrollMinSize = new Size(totalWidth, totalHeight);

            // A captured image that fits in the drawing viewport should sit in the center,
            // like a conventional image editor. Oversized images remain top/left anchored
            // so the existing AutoScroll behavior stays predictable.
            int viewportWidth = Math.Max(1, workspace.ClientSize.Width);
            int viewportHeight = Math.Max(1, workspace.ClientSize.Height);
            int x = totalWidth <= viewportWidth ? Math.Max(pad, (viewportWidth - canvas.Width) / 2) : pad;
            int y = totalHeight <= viewportHeight ? Math.Max(pad, (viewportHeight - canvas.Height) / 2) : pad;

            if (totalWidth <= viewportWidth && totalHeight <= viewportHeight)
                workspace.AutoScrollPosition = Point.Empty;

            canvas.Location = new Point(x, y);
        }

        private void EnsureEditorHeight()
        {
            if (WindowState != FormWindowState.Normal) return;
            int minEditorHeight = ScaleUiMetric(560);
            if (ClientSize.Height >= minEditorHeight) return;

            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int targetClientHeight = Math.Min(ScaleUiMetric(720),
                Math.Max(minEditorHeight, wa.Height - ScaleUiMetric(100)));
            int nonClient = Height - ClientSize.Height;
            int targetHeight = targetClientHeight + nonClient;

            // 좌상단 위치는 유지하고 아래로만 확장. 화면을 벗어날 때만 최소한 위로 보정.
            int newTop = Top;
            if (newTop + targetHeight > wa.Bottom)
                newTop = Math.Max(wa.Top, wa.Bottom - targetHeight);

            Bounds = new Rectangle(Left, newTop, Width, targetHeight);
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate { ApplyLocalizedUi(); });
                return;
            }
            ApplyLocalizedUi();
        }

        private void ApplyLocalizedUi()
        {
            emptyText.Text = L10n.T(
                "직사각형 캡처    Alt + Shift + S\r\n컬러피커 실행    Alt + Shift + C\r\n도움말    F1",
                "Rectangle Capture    Alt + Shift + S\r\nColor Picker    Alt + Shift + C\r\nHelp    F1");
            if (emptyText.Font != null) emptyText.Font.Dispose();
            emptyText.Font = new Font(L10n.DefaultFontName, 11.0f, FontStyle.Regular);

            int captureWidth = CalculateCaptureButtonWidth();
            ApplyCaptureButtonLanguage(rectangleCaptureButton, L10n.T("직사각형", "Rectangle"), captureWidth);
            ApplyCaptureButtonLanguage(fixedCaptureButton, L10n.T("크기지정", "Fixed Size"), captureWidth);
            ApplyCaptureButtonLanguage(windowCaptureButton, L10n.T("윈도우창", "Window"), captureWidth);
            ApplyCaptureButtonLanguage(fullScreenButton, L10n.T("전체화면", "Full Screen"), captureWidth);
            ApplyCaptureButtonLanguage(lastRegionButton, L10n.T("지난영역", "Last Region"), captureWidth);
            ApplyCaptureButtonLanguage(screenPickerButton, L10n.T("컬러피커", "Color Picker"), captureWidth);

            SetToolLanguage(EditorTool.Pen, L10n.T("일반펜", "Pen"));
            SetToolLanguage(EditorTool.Highlighter, L10n.T("형광펜", "Highlighter"));
            SetToolLanguage(EditorTool.Rectangle, L10n.T("사각형 그리기", "Rectangle"));
            SetToolLanguage(EditorTool.Ellipse, L10n.T("원형 그리기", "Ellipse"));
            SetToolLanguage(EditorTool.Arrow, L10n.T("화살표", "Arrow"));
            SetToolLanguage(EditorTool.Check, L10n.T("강조 체크", "Check mark"));
            SetToolLanguage(EditorTool.Emoji, L10n.T("선택", "Select"));
            SetToolLanguage(EditorTool.Text, L10n.T("텍스트 입력", "Text"));
            SetToolLanguage(EditorTool.Eraser, L10n.T("지우개 - 편집 내용을 원본으로 되돌립니다.", "Eraser - restores edited areas to the original image."));
            SetToolLanguage(EditorTool.PixelEraser, L10n.T("확장 지우개 - 이미지 픽셀 자체를 지웁니다. PNG는 투명, JPG는 흰색.", "Pixel eraser - removes image pixels. Transparent in PNG, white in JPG."));
            SetToolLanguage(EditorTool.Crop, L10n.T("자르기", "Crop"));
            SetToolLanguage(EditorTool.SampleColor, L10n.T("이미지에서 색상 추출", "Pick color from image"));

            toolTip.SetToolTip(rotateLeftButton, L10n.T("왼쪽 회전", "Rotate left"));
            toolTip.SetToolTip(rotateRightButton, L10n.T("오른쪽 회전", "Rotate right"));
            toolTip.SetToolTip(undoButton, L10n.T("되돌리기 (Ctrl+Z)", "Undo (Ctrl+Z)"));
            toolTip.SetToolTip(redoButton, L10n.T("다시 실행 (Ctrl+Y)", "Redo (Ctrl+Y)"));

            ApplyEditButtonLanguage(copyButton, L10n.T("복사", "Copy"));
            ApplyEditButtonLanguage(saveButton, L10n.T("저장", "Save"));
            ApplyEditButtonLanguage(printButton, L10n.T("인쇄", "Print"));
            if (printButton != null) toolTip.SetToolTip(printButton, L10n.T("인쇄 (Ctrl+P)", "Print (Ctrl+P)"));
            if (settingsButton != null) toolTip.SetToolTip(settingsButton, L10n.T("설정", "Settings"));
            colorResult.RefreshLanguage();
            UpdateHotkeyTooltips();

            if (activeTool != EditorTool.None && styles.ContainsKey(activeTool) &&
                activeTool != EditorTool.Crop && activeTool != EditorTool.SampleColor)
                optionsBar.Configure(activeTool, styles[activeTool]);
            else
                optionsBar.HideOptions();

            if (canvas == null) ResetStatus();
            else
            {
                UpdateImageStatus();
                UpdateStatusLabelWidths();
            }

            RebuildTrayMenu();
            UpdateMinimumToolbarWidth();
            captureBar.PerformLayout();
            editBar.PerformLayout();
            optionsBar.PerformLayout();
            Invalidate(true);
        }

        private void ApplyCaptureButtonLanguage(FlatButton button, string text, int width)
        {
            if (button == null) return;
            button.Text = text;
            button.Width = ScaleUiMetric(width);
            if (button.Font != null) button.Font.Dispose();
            button.Font = new Font("Segoe UI", 9.0f);
            toolTip.SetToolTip(button, text);
        }

        private void ApplyEditButtonLanguage(FlatButton button, string text)
        {
            if (button == null) return;
            button.Text = text;
            button.Width = ScaleUiMetric(CalculateEditButtonWidth(text, 78));
            if (button.Font != null) button.Font.Dispose();
            button.Font = new Font("Segoe UI", 9.0f);
        }

        private void SetToolLanguage(EditorTool tool, string tip)
        {
            FlatButton button;
            if (toolButtons.TryGetValue(tool, out button))
                toolTip.SetToolTip(button, tip);
        }

        private void UpdateMinimumToolbarWidth()
        {
            AlignToolbarRightEdges();
            int clientWidth = Math.Max(MeasureToolbarWidth(captureBar), MeasureToolbarWidth(editBar));
            int nonClientWidth = Math.Max(0, Width - ClientSize.Width);
            int nonClientHeight = Math.Max(0, Height - ClientSize.Height);
            int minimumClientHeight = ScaleUiMetric(298);
            MinimumSize = new Size(clientWidth + nonClientWidth, minimumClientHeight + nonClientHeight);
            if (ClientSize.Width < clientWidth || ClientSize.Height < minimumClientHeight)
                ClientSize = new Size(
                    Math.Max(ClientSize.Width, clientWidth),
                    Math.Max(ClientSize.Height, minimumClientHeight));
        }

        private void ApplyWindowTone()
        {
            try
            {
                int dark = 1;
                int caption = ColorRef(AppTheme.Title);
                int text = ColorRef(Color.White);
                int border = ColorRef(AppTheme.Title);
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_TEXT_COLOR, ref text, sizeof(int));
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
            catch { }
        }

        private static int ColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        private void CenterEmptyState()
        {
            // 초기 안내 패널은 Dock=Fill이며 Label 자체가 상하좌우 중앙 정렬됩니다.
            if (emptyState == null || workspace == null) return;
            if (emptyState.Visible) emptyState.BringToFront();
        }

        private void StartColorPicker(object sender, EventArgs e)
        {
            if (colorPicker == null || !colorPicker.IsReady)
            {
                MessageBox.Show(this, L10n.T("컬러피커를 사용할 수 없습니다.", "Color picker is unavailable."), "CapPicker");
                return;
            }
            if (!BeginInteraction()) return;
            if (!colorPicker.Start())
            {
                InteractionCancelled();
            }
        }

        private void ColorPicked(Color color, Point point)
        {
            string hex = String.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
            string rgb = String.Format("{0}, {1}, {2}", color.R, color.G, color.B);
            try { Clipboard.SetText(rgb); } catch { }

            colorResult.Value = color;
            statusColor.Text = String.Format(L10n.T("색상 {0} · RGB {1}, {2}, {3}", "Color {0} · RGB {1}, {2}, {3}"), hex, color.R, color.G, color.B);
            busy = false;

            if (!interactionWasTray)
            {
                Show();
                WindowState = restoreWindowState;
                Activate();
            }
            else
            {
                trayMode = true;
                trayIcon.Visible = true;
                Hide();
            }
        }

        private void UpdateHotkeyTooltips()
        {
            if (toolTip == null) return;

            string rectTip = L10n.T("직사각형 캡처 (Alt + Shift + S)", "Rectangle Capture (Alt + Shift + S)");
            string colorTip = L10n.T("컬러피커 (Alt + Shift + C)", "Color Picker (Alt + Shift + C)");

            if (hotkeysInitialized && !rectHotkeyRegistered)
                rectTip += "\r\n" + L10n.T(
                    "단축키가 다른 앱에서 사용 중입니다.",
                    "This hotkey is already used by another app.");
            if (hotkeysInitialized && !colorHotkeyRegistered)
                colorTip += "\r\n" + L10n.T(
                    "단축키가 다른 앱에서 사용 중입니다.",
                    "This hotkey is already used by another app.");

            if (rectangleCaptureButton != null) toolTip.SetToolTip(rectangleCaptureButton, rectTip);
            if (screenPickerButton != null) toolTip.SetToolTip(screenPickerButton, colorTip);
        }

        private void UpdateStatusLabelWidths()
        {
            if (statusColor == null) return;

            // Status cells use fixed widths for the current language/DPI. Do not size them
            // from the live pointer text: RGB values and coordinate digit counts change on
            // every mouse move and made the whole status row visibly "breathe".
            statusColor.Width = MeasureStatusSampleWidth(
                statusColor,
                L10n.T("색상 #FFFFFF · RGB 255, 255, 255", "Color #FFFFFF · RGB 255, 255, 255"),
                L10n.IsKorean ? 220 : 235);
            statusPointer.Width = MeasureStatusSampleWidth(
                statusPointer,
                L10n.T("좌표 99999, 99999", "XY 99999, 99999"),
                130);
            statusSize.Width = MeasureStatusSampleWidth(
                statusSize,
                L10n.T("크기 99999 × 99999", "Size 99999 × 99999"),
                145);
            statusImage.Width = MeasureStatusSampleWidth(
                statusImage,
                L10n.T("이미지 99999 × 99999", "Image 99999 × 99999"),
                150);
            statusZoom.Width = MeasureStatusSampleWidth(
                statusZoom,
                L10n.T("확대 1000%", "Zoom 1000%"),
                100);
        }

        private int MeasureStatusSampleWidth(Label label, string sample, int referenceMinimum)
        {
            if (label == null || label.Font == null) return ScaleUiMetric(referenceMinimum);
            Size measured = TextRenderer.MeasureText(
                sample ?? "", label.Font, new Size(Int32.MaxValue, Int32.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            return Math.Max(ScaleUiMetric(referenceMinimum), measured.Width + ScaleUiMetric(18));
        }

        private static void SetStatusText(Label label, string text)
        {
            if (label != null && !String.Equals(label.Text, text, StringComparison.Ordinal))
                label.Text = text;
        }

        private void UpdatePointerStatus(CanvasPointerInfo info)
        {
            if (info == null) return;

            string colorText;
            if (!info.InsideImage)
                colorText = L10n.T("색상 -", "Color -");
            else if (info.Transparent)
                colorText = L10n.T("색상 투명", "Color transparent");
            else
                colorText = String.Format(
                    L10n.T("색상 #{0:X2}{1:X2}{2:X2} · RGB {0}, {1}, {2}", "Color #{0:X2}{1:X2}{2:X2} · RGB {0}, {1}, {2}"),
                    info.Color.R, info.Color.G, info.Color.B);

            string pointerText = info.InsideImage
                ? String.Format(L10n.T("좌표 {0}, {1}", "XY {0}, {1}"), info.Point.X, info.Point.Y)
                : L10n.T("좌표 -", "XY -");
            string sizeText = info.DragSize.IsEmpty
                ? L10n.T("크기 -", "Size -")
                : String.Format(L10n.T("크기 {0} × {1}", "Size {0} × {1}"), info.DragSize.Width, info.DragSize.Height);
            string imageText = String.Format(L10n.T("이미지 {0} × {1}", "Image {0} × {1}"), info.ImageSize.Width, info.ImageSize.Height);
            string zoomText = L10n.T("확대 ", "Zoom ") + info.ZoomPercent + "%";

            SetStatusText(statusColor, colorText);
            SetStatusText(statusPointer, pointerText);
            SetStatusText(statusSize, sizeText);
            SetStatusText(statusImage, imageText);
            SetStatusText(statusZoom, zoomText);
        }

        private void UpdateImageStatus()
        {
            if (canvas == null)
            {
                ResetStatus();
                return;
            }

            Size s = canvas.CapturedImageSize;
            statusImage.Text = String.Format(L10n.T("이미지 {0} × {1}", "Image {0} × {1}"), s.Width, s.Height);
            statusZoom.Text = L10n.T("확대 ", "Zoom ") + canvas.ZoomPercent + "%";
            statusPointer.Text = L10n.T("좌표 -", "XY -");
            statusSize.Text = L10n.T("크기 -", "Size -");
        }

        private void ResetStatus()
        {
            statusColor.Text = L10n.T("색상 -", "Color -");
            statusPointer.Text = L10n.T("좌표 -", "XY -");
            statusSize.Text = L10n.T("크기 -", "Size -");
            statusImage.Text = L10n.T("이미지 -", "Image -");
            statusZoom.Text = L10n.T("확대 100%", "Zoom 100%");
            UpdateStatusLabelWidths();
        }

        private void CopyImage(object sender, EventArgs e)
        {
            if (canvas == null) return;
            using (Bitmap bmp = canvas.ExportBitmap())
            {
                try { Clipboard.SetImage(bmp); }
                catch (Exception ex) { MessageBox.Show(this, L10n.T("클립보드 복사 실패:\r\n", "Clipboard copy failed:\r\n") + ex.Message, "CapPicker"); }
            }
        }

        private void SaveImage(object sender, EventArgs e)
        {
            if (canvas == null) return;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = L10n.T("PNG 이미지 (*.png)|*.png|JPEG 이미지 (*.jpg)|*.jpg", "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg");
                dialog.DefaultExt = "png";
                dialog.AddExtension = true;
                dialog.FileName = "Capture_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                using (Bitmap bmp = canvas.ExportBitmap())
                {
                    string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                    if (ext == ".jpg" || ext == ".jpeg")
                    {
                        using (Bitmap jpg = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb))
                        using (Graphics g = Graphics.FromImage(jpg))
                        {
                            g.Clear(Color.White);
                            g.DrawImageUnscaled(bmp, 0, 0);
                            jpg.Save(dialog.FileName, ImageFormat.Jpeg);
                        }
                    }
                    else
                    {
                        bmp.Save(dialog.FileName, ImageFormat.Png);
                    }
                }
            }
        }

        private void PrintImage(object sender, EventArgs e)
        {
            if (canvas == null) return;

            try
            {
                using (Bitmap bmp = canvas.ExportBitmap())
                using (CapPrintPreviewForm preview = new CapPrintPreviewForm(bmp))
                {
                    preview.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    L10n.T("인쇄 미리보기를 열지 못했습니다.\r\n", "Could not open print preview.\r\n") + ex.Message,
                    "CapPicker",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void MainKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F1)
            {
                ShowHelp();
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (canvas == null) return;

            if (e.Control && e.KeyCode == Keys.C)
            {
                CopyImage(this, EventArgs.Empty);
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.S)
            {
                SaveImage(this, EventArgs.Empty);
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.P)
            {
                PrintImage(this, EventArgs.Empty);
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.Z)
            {
                canvas.Undo();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.Y)
            {
                canvas.Redo();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0))
            {
                canvas.SetZoomPercent(100);
                e.SuppressKeyPress = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // F1 must work even while a child editor control owns keyboard focus.
            if (keyData == Keys.F1)
            {
                ShowHelp();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowSettings()
        {
            try
            {
                using (SettingsForm settings = new SettingsForm())
                    settings.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    L10n.T("설정을 열지 못했습니다.\r\n", "Could not open Settings.\r\n") + ex.Message,
                    "CapPicker",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void ShowHelp()
        {
            try
            {
                using (HelpForm help = new HelpForm())
                {
                    if (Visible) help.ShowDialog(this);
                    else help.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    L10n.T("도움말을 열지 못했습니다.\r\n", "Could not open Help.\r\n") + ex.Message,
                    "CapPicker",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void MinimizeToTray()
        {
            if (WindowState != FormWindowState.Minimized)
                restoreWindowState = WindowState;
            trayMode = true;
            trayIcon.Visible = true;
            Hide();
        }

        private void RestoreFromTray()
        {
            trayMode = false;
            // Keep the tray icon visible even while the main window is open so the app
            // can always be controlled or exited from the notification area.
            trayIcon.Visible = true;
            Show();
            WindowState = restoreWindowState;
            Activate();
        }
    }
}
