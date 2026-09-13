using System;
using System.IO;

namespace CapPicker
{
    internal enum MinimizeButtonBehavior
    {
        Taskbar,
        Tray
    }

    internal enum CloseButtonBehavior
    {
        Tray,
        Exit
    }

    internal enum PickerPerformanceMode
    {
        Normal,
        LowSpec
    }

    internal static class AppSettings
    {
        private static MinimizeButtonBehavior minimizeBehavior = MinimizeButtonBehavior.Taskbar;
        private static CloseButtonBehavior closeBehavior = CloseButtonBehavior.Tray;
        private static PickerPerformanceMode pickerPerformanceMode = PickerPerformanceMode.Normal;

        static AppSettings()
        {
            Load();
        }

        public static MinimizeButtonBehavior MinimizeBehavior
        {
            get { return minimizeBehavior; }
            set { minimizeBehavior = value; }
        }

        public static CloseButtonBehavior CloseBehavior
        {
            get { return closeBehavior; }
            set { closeBehavior = value; }
        }

        public static PickerPerformanceMode PickerPerformance
        {
            get { return pickerPerformanceMode; }
            set { pickerPerformanceMode = value; }
        }

        public static bool LowSpecOptimization
        {
            get { return pickerPerformanceMode == PickerPerformanceMode.LowSpec; }
        }

        public static void Save()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CapPicker");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "settings.txt");
                File.WriteAllLines(path, new string[]
                {
                    "MinimizeBehavior=" + minimizeBehavior,
                    "CloseBehavior=" + closeBehavior,
                    "PickerPerformance=" + pickerPerformanceMode
                });
            }
            catch { }
        }

        private static void Load()
        {
            minimizeBehavior = MinimizeButtonBehavior.Taskbar;
            closeBehavior = CloseButtonBehavior.Tray;
            pickerPerformanceMode = PickerPerformanceMode.Normal;

            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CapPicker",
                    "settings.txt");
                if (!File.Exists(path)) return;

                string[] lines = File.ReadAllLines(path);
                foreach (string raw in lines)
                {
                    string line = raw == null ? "" : raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    if (String.Equals(key, "MinimizeBehavior", StringComparison.OrdinalIgnoreCase))
                    {
                        MinimizeButtonBehavior parsed;
                        if (Enum.TryParse(value, true, out parsed)) minimizeBehavior = parsed;
                    }
                    else if (String.Equals(key, "CloseBehavior", StringComparison.OrdinalIgnoreCase))
                    {
                        CloseButtonBehavior parsed;
                        if (Enum.TryParse(value, true, out parsed)) closeBehavior = parsed;
                    }
                    else if (String.Equals(key, "PickerPerformance", StringComparison.OrdinalIgnoreCase))
                    {
                        PickerPerformanceMode parsed;
                        if (Enum.TryParse(value, true, out parsed)) pickerPerformanceMode = parsed;
                    }
                }
            }
            catch { }
        }
    }
}
