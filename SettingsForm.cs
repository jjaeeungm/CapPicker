using System;
using System.Drawing;
using System.Windows.Forms;

namespace CapPicker
{
    internal sealed class SettingsForm : Form
    {
        private const int RefDpi = DpiUtil.ReferenceDpi;
        private int uiDpi = RefDpi;
        private Font uiFont;
        private Font sectionFont;

        private Label languageLabel;
        private Panel languagePanel;
        private RadioButton languageAutoRadio;
        private RadioButton languageKoreanRadio;
        private RadioButton languageEnglishRadio;

        private Label minimizeLabel;
        private Panel minimizePanel;
        private RadioButton minimizeTaskbarRadio;
        private RadioButton minimizeTrayRadio;

        private Label closeLabel;
        private Panel closePanel;
        private RadioButton closeTrayRadio;
        private RadioButton closeExitRadio;

        private Label performanceLabel;
        private Panel performancePanel;
        private RadioButton performanceNormalRadio;
        private RadioButton performanceLowSpecRadio;

        private Button helpButton;
        private Button okButton;
        private Button cancelButton;

        public SettingsForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = AppTheme.Window;
            ForeColor = AppTheme.Text;
            KeyPreview = true;

            // Own the two fonts used by this dialog explicitly. Section labels share one
            // bold Font instead of allocating a separate GDI object for every label.
            uiFont = new Font(L10n.DefaultFontName, 9.5f, FontStyle.Regular);
            sectionFont = new Font(L10n.DefaultFontName, 9.5f, FontStyle.Bold);
            Font = uiFont;

            languageLabel = MakeSectionLabel();
            languagePanel = MakeOptionPanel();
            languageAutoRadio = MakeRadio();
            languageKoreanRadio = MakeRadio();
            languageEnglishRadio = MakeRadio();

            minimizeLabel = MakeSectionLabel();
            minimizePanel = MakeOptionPanel();
            minimizeTaskbarRadio = MakeRadio();
            minimizeTrayRadio = MakeRadio();

            closeLabel = MakeSectionLabel();
            closePanel = MakeOptionPanel();
            closeTrayRadio = MakeRadio();
            closeExitRadio = MakeRadio();

            performanceLabel = MakeSectionLabel();
            performancePanel = MakeOptionPanel();
            performanceNormalRadio = MakeRadio();
            performanceLowSpecRadio = MakeRadio();

            helpButton = MakeButton();
            helpButton.Click += delegate { ShowHelp(); };

            okButton = MakeButton();
            okButton.Click += delegate
            {
                AppSettings.MinimizeBehavior = minimizeTrayRadio.Checked
                    ? MinimizeButtonBehavior.Tray
                    : MinimizeButtonBehavior.Taskbar;
                AppSettings.CloseBehavior = closeExitRadio.Checked
                    ? CloseButtonBehavior.Exit
                    : CloseButtonBehavior.Tray;
                AppSettings.PickerPerformance = performanceLowSpecRadio.Checked
                    ? PickerPerformanceMode.LowSpec
                    : PickerPerformanceMode.Normal;
                AppSettings.Save();

                UiLanguageMode selectedLanguage = UiLanguageMode.Auto;
                if (languageKoreanRadio.Checked) selectedLanguage = UiLanguageMode.Korean;
                else if (languageEnglishRadio.Checked) selectedLanguage = UiLanguageMode.English;
                L10n.SaveLanguageMode(selectedLanguage);

                DialogResult = DialogResult.OK;
                Close();
            };

            cancelButton = MakeButton();
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            languagePanel.Controls.Add(languageAutoRadio);
            languagePanel.Controls.Add(languageKoreanRadio);
            languagePanel.Controls.Add(languageEnglishRadio);
            minimizePanel.Controls.Add(minimizeTaskbarRadio);
            minimizePanel.Controls.Add(minimizeTrayRadio);
            closePanel.Controls.Add(closeTrayRadio);
            closePanel.Controls.Add(closeExitRadio);
            performancePanel.Controls.Add(performanceNormalRadio);
            performancePanel.Controls.Add(performanceLowSpecRadio);

            Controls.Add(languageLabel);
            Controls.Add(languagePanel);
            Controls.Add(minimizeLabel);
            Controls.Add(minimizePanel);
            Controls.Add(closeLabel);
            Controls.Add(closePanel);
            Controls.Add(performanceLabel);
            Controls.Add(performancePanel);
            Controls.Add(helpButton);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;

            HandleCreated += delegate { ApplyDpi(ReadWindowDpi()); };
            DpiChanged += delegate(object sender, DpiChangedEventArgs e) { ApplyDpi(e.DeviceDpiNew); };
            Shown += delegate { ApplyWindowTone(); };

            RefreshLanguage();
            LoadValues();
        }

        private Label MakeSectionLabel()
        {
            Label l = new Label();
            l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.ForeColor = AppTheme.Text;
            l.Font = sectionFont;
            return l;
        }

        private static Panel MakeOptionPanel()
        {
            Panel p = new Panel();
            p.BackColor = AppTheme.Window;
            p.Margin = Padding.Empty;
            return p;
        }

        private static RadioButton MakeRadio()
        {
            RadioButton r = new RadioButton();
            r.AutoSize = true;
            r.BackColor = AppTheme.Window;
            r.ForeColor = AppTheme.Text;
            r.TextAlign = ContentAlignment.MiddleLeft;
            r.UseVisualStyleBackColor = false;
            return r;
        }

        private static Button MakeButton()
        {
            Button b = new Button();
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = AppTheme.Button;
            b.ForeColor = AppTheme.Text;
            b.FlatAppearance.BorderColor = AppTheme.Border;
            return b;
        }

        private void LoadValues()
        {
            UiLanguageMode mode = L10n.PreferredLanguageMode;
            languageAutoRadio.Checked = mode == UiLanguageMode.Auto;
            languageKoreanRadio.Checked = mode == UiLanguageMode.Korean;
            languageEnglishRadio.Checked = mode == UiLanguageMode.English;

            minimizeTaskbarRadio.Checked = AppSettings.MinimizeBehavior == MinimizeButtonBehavior.Taskbar;
            minimizeTrayRadio.Checked = AppSettings.MinimizeBehavior == MinimizeButtonBehavior.Tray;
            closeTrayRadio.Checked = AppSettings.CloseBehavior == CloseButtonBehavior.Tray;
            closeExitRadio.Checked = AppSettings.CloseBehavior == CloseButtonBehavior.Exit;
            performanceNormalRadio.Checked = AppSettings.PickerPerformance == PickerPerformanceMode.Normal;
            performanceLowSpecRadio.Checked = AppSettings.PickerPerformance == PickerPerformanceMode.LowSpec;
        }

        private void RefreshLanguage()
        {
            Text = L10n.T("CapPicker 설정", "CapPicker Settings");

            languageLabel.Text = L10n.T("언어", "Language");
            languageAutoRadio.Text = L10n.T("Windows 설정 (자동)", "Windows setting (Auto)");
            languageKoreanRadio.Text = "한국어";
            languageEnglishRadio.Text = "English";

            minimizeLabel.Text = L10n.T("최소화 버튼 동작", "Minimize button action");
            minimizeTaskbarRadio.Text = L10n.T("작업표시줄로", "To taskbar");
            minimizeTrayRadio.Text = L10n.T("트레이로", "To tray");

            closeLabel.Text = L10n.T("닫기 버튼 동작", "Close button action");
            closeTrayRadio.Text = L10n.T("트레이로", "To tray");
            closeExitRadio.Text = L10n.T("앱 종료", "Exit app");

            performanceLabel.Text = L10n.T("성능옵션", "Performance options");
            performanceNormalRadio.Text = L10n.T("일반 (권장)", "Normal (Recommended)");
            performanceLowSpecRadio.Text = L10n.T("저사양 PC 최적화", "Low-spec PC optimization");

            helpButton.Text = L10n.T("도움말 (F1)", "Help (F1)");
            okButton.Text = L10n.T("확인", "OK");
            cancelButton.Text = L10n.T("취소", "Cancel");
        }

        private void ShowHelp()
        {
            using (HelpForm help = new HelpForm())
                help.ShowDialog(this);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F1)
            {
                ShowHelp();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
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

        private void LayoutRadioRow(Panel panel, RadioButton[] radios, int leftRef, int gapRef)
        {
            if (panel == null || radios == null) return;
            int x = S(leftRef);
            int gap = S(gapRef);
            for (int i = 0; i < radios.Length; i++)
            {
                RadioButton radio = radios[i];
                if (radio == null) continue;
                Size preferred = radio.GetPreferredSize(Size.Empty);
                int h = Math.Min(panel.ClientSize.Height, Math.Max(S(24), preferred.Height));
                radio.Size = new Size(preferred.Width, h);
                radio.Location = new Point(x, Math.Max(0, (panel.ClientSize.Height - h) / 2));
                x += radio.Width + gap;
            }
        }

        private void ApplyDpi(int dpi)
        {
            if (dpi < 48 || dpi > 480) dpi = RefDpi;
            uiDpi = dpi;

            SuspendLayout();
            try
            {
                ClientSize = new Size(S(468), S(400));

                SetBoundsRef(languageLabel, 24, 18, 420, 24);
                SetBoundsRef(languagePanel, 24, 44, 420, 34);
                LayoutRadioRow(languagePanel, new RadioButton[] { languageAutoRadio, languageKoreanRadio, languageEnglishRadio }, 4, 10);

                SetBoundsRef(minimizeLabel, 24, 92, 420, 24);
                SetBoundsRef(minimizePanel, 24, 118, 420, 34);
                LayoutRadioRow(minimizePanel, new RadioButton[] { minimizeTaskbarRadio, minimizeTrayRadio }, 4, 18);

                SetBoundsRef(closeLabel, 24, 166, 420, 24);
                SetBoundsRef(closePanel, 24, 192, 420, 34);
                LayoutRadioRow(closePanel, new RadioButton[] { closeTrayRadio, closeExitRadio }, 4, 18);

                SetBoundsRef(performanceLabel, 24, 240, 420, 24);
                SetBoundsRef(performancePanel, 24, 266, 420, 34);
                LayoutRadioRow(performancePanel, new RadioButton[] { performanceNormalRadio, performanceLowSpecRadio }, 4, 18);

                SetBoundsRef(helpButton, 24, 334, 132, 40);
                SetBoundsRef(okButton, 276, 334, 80, 40);
                SetBoundsRef(cancelButton, 364, 334, 80, 40);
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        protected override void Dispose(bool disposing)
        {
            Font ownedSectionFont = null;
            Font ownedUiFont = null;
            if (disposing)
            {
                // Keep the shared fonts alive while child controls are being disposed, then
                // release the two GDI resources explicitly after the base Form cleanup.
                ownedSectionFont = sectionFont;
                ownedUiFont = uiFont;
                sectionFont = null;
                uiFont = null;
            }

            base.Dispose(disposing);

            if (disposing)
            {
                if (ownedSectionFont != null) ownedSectionFont.Dispose();
                if (ownedUiFont != null) ownedUiFont.Dispose();
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
