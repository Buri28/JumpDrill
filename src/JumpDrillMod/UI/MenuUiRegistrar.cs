using System;
using BeatSaberMarkupLanguage.GameplaySetup;
using BeatSaberMarkupLanguage.MenuButtons;
using JumpDrillMod.Services;
using Zenject;

namespace JumpDrillMod.UI
{
    /// <summary>
    /// 曲選択画面の Mods タブと、メインメニューの Drill ボタンを BSML に登録する。
    /// </summary>
    /// <remarks>
    /// <c>GameplaySetup.Instance</c> は BSML のメニュー用 DiContainer から解決される。
    /// プラグインの <c>[OnEnable]</c> はそれより前に走るため、そこで触ると
    /// 「Tried getting BSMLSettings too early!」で落ちる。
    /// メニュー・スコープの <see cref="IInitializable"/> なら
    /// インストール完了後に呼ばれるので安全。
    /// </remarks>
    internal class MenuUiRegistrar : IInitializable, IDisposable
    {
        private readonly JumpDrillTabController tab;
        private readonly DrillPackLocator pack;

        /// <summary>
        /// メインメニュー左のMODのボタン。Jump Drill 画面は曲を選ばずに叩ける画面なので、
        /// 曲選択を通らずに開けるようにする。
        /// </summary>
        private readonly MenuButton drillSetButton;

        private bool tabRegistered;
        private bool buttonRegistered;

        internal MenuUiRegistrar(DrillGenerationService generator, SongRefreshService refresher, DrillPackLocator pack,
            DrillSetGenerationService drillSet, Medals.DrillSetScreen drillSetScreen)
        {
            this.pack = pack;
            tab = new JumpDrillTabController(generator, refresher, drillSet, drillSetScreen);
            drillSetButton = new MenuButton("JumpDrill",
                "Jump drills: medals, records and play",
                () => drillSetScreen.Open());
        }

        public void Initialize()
        {
            // SongCore は folders.xml を起動時に読む。生成のときに初めて登録すると
            // その回は曲一覧に出ないので、メニューに入った時点で通しておく
            TryRun("PreparePack", () => pack.Prepare(PluginConfig.Instance?.PackName ?? "JumpDrill"));

            tabRegistered = TryRun("AddTab", () =>
                GameplaySetup.Instance.AddTab(JumpDrillTabController.TabName,
                                              JumpDrillTabController.Resource,
                                              tab,
                                              // Solo だけにするとキャンペーンや Custom
                                              // （AccSaber など）でタブが出ない。
                                              // マルチは除く。Jump Drill 画面の Play は1人用の曲を始めるので
                                              MenuType.All & ~MenuType.Online));

            buttonRegistered = TryRun("RegisterButton", () =>
                MenuButtons.Instance.RegisterButton(drillSetButton));

            if (tabRegistered) Plugin.LogDebug("Menu UI registered");
        }

        public void Dispose()
        {
            if (tabRegistered && TryRun("RemoveTab", () =>
                    GameplaySetup.Instance.RemoveTab(JumpDrillTabController.TabName)))
            {
                tabRegistered = false;
            }

            if (buttonRegistered && TryRun("UnregisterButton", () =>
                    MenuButtons.Instance.UnregisterButton(drillSetButton)))
            {
                buttonRegistered = false;
            }
        }

        private static bool TryRun(string what, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log?.Error($"Menu UI {what} failed: {e}");
                return false;
            }
        }
    }
}
