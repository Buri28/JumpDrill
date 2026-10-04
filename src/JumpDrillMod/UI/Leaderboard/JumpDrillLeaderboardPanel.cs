using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using TMPro;

namespace JumpDrillMod.UI.Leaderboard
{
    /// <summary>
    /// リーダーボード枠の上に出る帯。枠は左右の矢印で切り替わるので、
    /// いま何を見ているのかが分かる必要がある。
    /// </summary>
    internal class JumpDrillLeaderboardPanel : BSMLResourceViewController
    {
        public override string ResourceName => "JumpDrillMod.Resources.LeaderboardPanel.bsml";

        private string text = Title;

        /// <summary>
        /// 見出し。左右の矢印で切り替わる他のリーダーボードと見分けられるように、大きく色を付ける。
        /// </summary>
        private const string Title = "<size=150%><b><color=#3FD0FF>Jump Drill</color></b></size>";

        [UIComponent("panel-text")] private TextMeshProUGUI? panelText = null;

        /// <summary>見ているドリル。画面ができる前に呼ばれても捨てずに持っておく。</summary>
        internal void SetDrill(string drillId)
        {
            text = Title + "   <color=#B0B0B0>[" + drillId + "]</color>";
            Apply();
        }

        [UIAction("#post-parse")]
        public void PostParse() => Apply();

        private void Apply()
        {
            if (panelText != null) panelText.text = text;
        }
    }
}
