using System;
using System.Globalization;
using System.IO;

namespace CapPicker
{
    internal enum UiLanguageMode
    {
        Auto,
        Korean,
        English
    }

    internal static class L10n
    {
        public const string Version = "1.1.1";

        public static event EventHandler LanguageChanged;
        private static UiLanguageMode languageMode = LoadLanguageMode();
        private static UiLanguageMode preferredLanguageMode = languageMode;

        // Language used by the currently running UI.
        public static UiLanguageMode LanguageMode
        {
            get { return languageMode; }
        }

        // Persisted language preference. SaveLanguageMode applies the same selection to the
        // running UI immediately and raises LanguageChanged so existing windows can relocalize.
        public static UiLanguageMode PreferredLanguageMode
        {
            get { return preferredLanguageMode; }
        }

        public static bool SystemIsKorean
        {
            get
            {
                try
                {
                    return String.Equals(
                        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                        "ko",
                        StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }
        }

        public static bool IsKorean
        {
            get
            {
                if (languageMode == UiLanguageMode.Korean) return true;
                if (languageMode == UiLanguageMode.English) return false;
                return SystemIsKorean;
            }
        }

        public static string T(string ko, string en)
        {
            return IsKorean ? ko : en;
        }

        public static string DefaultFontName
        {
            get { return IsKorean ? "맑은 고딕" : "Segoe UI"; }
        }

        public static void SaveLanguageMode(UiLanguageMode mode)
        {
            preferredLanguageMode = mode;
            languageMode = mode;
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CapPicker");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "language.txt"), mode.ToString());
            }
            catch { }

            EventHandler handler = LanguageChanged;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        private static UiLanguageMode LoadLanguageMode()
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CapPicker",
                    "language.txt");
                if (!File.Exists(path)) return UiLanguageMode.Auto;

                UiLanguageMode parsed;
                if (Enum.TryParse(File.ReadAllText(path).Trim(), true, out parsed))
                    return parsed;
            }
            catch { }
            return UiLanguageMode.Auto;
        }
    }
}
