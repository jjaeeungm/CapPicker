
using System;
using System.Threading;
using System.Windows.Forms;

namespace CapPicker
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "CapPicker_1_0_0_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        L10n.T("CapPicker이 이미 실행 중입니다.\r\n작업 표시줄의 트레이 아이콘을 확인하세요.", "CapPicker is already running.\r\nCheck the taskbar or system tray."),
                        "CapPicker",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
                    if (!Native.SetProcessDpiAwarenessContext(new IntPtr(-4)))
                        Native.SetProcessDPIAware();
                }
                catch
                {
                    try { Native.SetProcessDPIAware(); } catch { }
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try
                {
                    Application.Run(new MainForm());
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        L10n.T("CapPicker 실행 중 오류가 발생했습니다.\r\n\r\n", "An error occurred while running CapPicker.\r\n\r\n") + ex,
                        "CapPicker",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }
    }
}
