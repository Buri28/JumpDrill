using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Parser;
using HMUI;

namespace JumpDrillMod.UI.Medals
{
    /// <summary>
    /// Jump Drill 画面の Make drill で出す、ドリルを組んで作る画面。中身は Mods タブと同じもの。
    /// </summary>
    /// <remarks>
    /// カスタムステージに足すドリルを、曲選択へ戻らずにその場で作れるようにする。
    /// Mods タブとは別の <see cref="JumpDrillTabController"/> を受け持たせる
    /// （1つの実体を2つの画面に組むと、部品の参照が後から組んだ方に付け替わる）。
    /// グリッドと設定は共有しているので、閉じるときに Mods タブの表示を合わせる。
    /// </remarks>
    internal class DrillMakerViewController : ViewController
    {
        private JumpDrillTabController? host;

        internal void Setup(JumpDrillTabController host) => this.host = host;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            if (host == null) return;

            if (firstActivation)
            {
                string content = Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), JumpDrillTabController.Resource);
                BSMLParser.Instance.Parse(content, gameObject, host);
            }
            else
            {
                // Mods タブで変えた分を出し直す
                host.Reload();
            }
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
            host?.ReloadOthers();
        }
    }
}
