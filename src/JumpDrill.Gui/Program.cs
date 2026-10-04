using System;
using System.Globalization;
using System.Windows.Forms;

namespace JumpDrill.Gui
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Lang.English = LoadEnglish();

            // 言語を切り替えると窓が閉じるので、作り直して開き直す。
            MainForm form;
            do
            {
                form = new MainForm();
                Application.Run(form);
            }
            while (form.RestartRequested);
        }

        /// <summary>保存した言語。まだ選んでいなければ Windows の表示言語に合わせる。</summary>
        private static bool LoadEnglish()
        {
            var settings = new SettingsStore(SettingsStore.DefaultPath());
            settings.Load();
            string lang = settings.GetString("lang", "");
            if (lang.Length > 0) return lang == "en";
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "ja";
        }
    }
}
