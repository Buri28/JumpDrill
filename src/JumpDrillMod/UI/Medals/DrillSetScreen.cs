using System;
using BeatSaberMarkupLanguage;
using JumpDrillMod.Services;

namespace JumpDrillMod.UI.Medals
{
    /// <summary>
    /// Jump Drill 画面を開く。Mods タブとリーダーボードの両方から呼ぶ。
    /// </summary>
    /// <remarks>
    /// 画面（FlowCoordinator）は1つだけ作って使い回す。呼ぶ側ごとに作ると、
    /// 片方で開いたまま、もう片方からも開けてしまう。
    /// </remarks>
    internal class DrillSetScreen
    {
        private readonly ReplayScoreReader reader;
        private readonly LevelLauncher launcher;
        private readonly IconService icons;
        private readonly DiagramService diagrams;
        private readonly DrillSetGenerationService drillSet;

        private MedalFlowCoordinator? flow;

        internal DrillSetScreen(ReplayScoreReader reader, LevelLauncher launcher, IconService icons, DiagramService diagrams,
            DrillSetGenerationService drillSet)
        {
            this.reader = reader;
            this.launcher = launcher;
            this.icons = icons;
            this.diagrams = diagrams;
            this.drillSet = drillSet;
        }

        /// <summary>開く。開けなければ理由を返す。</summary>
        internal string? Open()
        {
            try
            {
                if (flow == null) flow = BeatSaberUI.CreateFlowCoordinator<MedalFlowCoordinator>();

                // 既に出ているなら何もしない。重ねて出すと戻るボタンを2回押さないと抜けられない
                if (flow.isActivated) return null;

                flow.Setup(reader, launcher, icons, diagrams, drillSet);
                flow.Open();
                return null;
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not open the Jump Drill screen: " + e);
                return "Could not open the Jump Drill screen. See the log.";
            }
        }
    }
}
