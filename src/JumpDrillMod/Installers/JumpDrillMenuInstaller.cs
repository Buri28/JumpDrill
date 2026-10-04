using System.Runtime.CompilerServices;
using IPA.Loader;
using JumpDrillMod.Services;
using JumpDrillMod.UI;
using JumpDrillMod.UI.Leaderboard;
using Zenject;

namespace JumpDrillMod.Installers
{
    /// <summary>
    /// メニュー・スコープのインストーラー。譜面の生成と曲一覧の更新、
    /// そして曲選択画面への UI 登録をまとめる。
    /// </summary>
    internal class JumpDrillMenuInstaller : Installer
    {
        public override void InstallBindings()
        {
            Container.BindInterfacesAndSelfTo<DrillPackLocator>().AsSingle();
            Container.BindInterfacesAndSelfTo<ReplayScoreReader>().AsSingle();
            Container.BindInterfacesAndSelfTo<DiagramService>().AsSingle();
            Container.BindInterfacesAndSelfTo<DrillGenerationService>().AsSingle();
            Container.BindInterfacesAndSelfTo<SongRefreshService>().AsSingle();
            Container.BindInterfacesAndSelfTo<DrillSetGenerationService>().AsSingle();
            Container.BindInterfacesAndSelfTo<LevelLauncher>().AsSingle();
            Container.BindInterfacesAndSelfTo<IconService>().AsSingle();
            Container.BindInterfacesAndSelfTo<UI.Medals.DrillSetScreen>().AsSingle();
            Container.BindInterfacesAndSelfTo<MenuUiRegistrar>().AsSingle();

            // LeaderboardCore は任意。無ければ曲選択の枠には出さず、記録は Jump Drill 画面で見る
            if (PluginManager.GetPluginFromId("LeaderboardCore") != null)
                BindLeaderboard();
            else
                Plugin.LogDebug("LeaderboardCore not found; leaderboard not registered");
        }

        /// <summary>
        /// LeaderboardCore の型に触るのはここだけ。無いときに呼ぶと型が読めずに落ちるので、
        /// 別のメソッドに分けて、入っていると確かめてから呼ぶ。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void BindLeaderboard()
        {
#if JUMPDRILL_LEADERBOARDCORE
            Container.BindInterfacesAndSelfTo<LeaderboardRegistrar>().AsSingle();
#else
            Plugin.LogDebug("LeaderboardCore is loaded, but this build does not include the leaderboard");
#endif
        }
    }
}
