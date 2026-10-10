using JumpDrill.Model;
using JumpDrillMod.Gameplay;
using Zenject;

namespace JumpDrillMod.Installers
{
    /// <summary>
    /// 曲を叩く場面（ソロ）に、ドリルのリプレイ記録を入れる。
    /// </summary>
    /// <remarks>
    /// 入れないのは、設定で切っているとき・リプレイの再生中・ドリルでない曲のとき。
    /// 場面ができる前に決まるものはここで見て、記録の部品そのものを作らない。
    /// </remarks>
    internal class JumpDrillGameInstaller : Installer
    {
        private readonly GameplayCoreSceneSetupData setupData;

        public JumpDrillGameInstaller(GameplayCoreSceneSetupData setupData)
        {
            this.setupData = setupData;
        }

        public override void InstallBindings()
        {
            if (PluginConfig.Instance?.RecordReplays != true) return;
            if (DrillNaming.ExtractId(setupData.beatmapLevel?.songName) == null) return;

            // 記録しなかった理由は必ず残す。リプレイが無いと言われたときに、ログで追えるように
            string? playback = ReplayPlaybackCheck.PlayingBackBy();
            if (playback != null)
            {
                Plugin.Log?.Info("not recording (replay playback: " + playback + ")");
                return;
            }

            Container.BindInterfacesAndSelfTo<DrillReplayRecorder>().AsSingle();
        }
    }
}
