using System.Drawing;

namespace JumpDrill.Gui
{
    /// <summary>
    /// 窓のアイコン。exe のアイコン（ApplicationIcon）と同じ JumpDrill.ico を埋め込んで使う。
    /// 窓は Icon を指定しないと WinForms の既定のアイコンになる。
    /// </summary>
    internal static class AppIcon
    {
        private static Icon _icon;

        public static Icon Value
        {
            get
            {
                if (_icon != null) return _icon;
                using (var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("JumpDrill.ico"))
                    _icon = stream != null ? new Icon(stream) : null;
                return _icon;
            }
        }
    }
}
