using System.Runtime.CompilerServices;
using IPA;
using IPA.Config.Stores;
using JumpDrillMod.Installers;
using SiraUtil.Zenject;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace JumpDrillMod
{
    /// <summary>
    /// エントリポイント。設定の読み込みとメニュー・スコープのインストーラー登録を行う。
    /// </summary>
    [Plugin(RuntimeOptions.DynamicInit)]
    public class Plugin
    {
        internal static Plugin? Instance { get; private set; }
        public static IPA.Logging.Logger? Log { get; private set; }

        /// <summary>
        /// 詳細ログを出すか。既定は false で、警告とエラーだけをログに残す。
        /// 調べたいときだけここを true にしてビルドし直す。
        /// </summary>
        internal static bool DebugLogging = false;

        /// <summary>
        /// 詳細ログ。<see cref="DebugLogging"/> が有効なときだけ出力する。
        /// 警告とエラーはこれを通さず、直接 <see cref="Log"/> へ出す。
        /// </summary>
        internal static void LogDebug(string message)
        {
            if (DebugLogging) Log?.Info(message);
        }

        [Init]
        public void Init(IPA.Logging.Logger logger, IPA.Config.Config config, Zenjector zenjector)
        {
            Instance = this;
            Log = logger;
            PluginConfig.Instance = config.Generated<PluginConfig>();

            // 別スレッドからは取れないので、ここで1度だけ取る
            InstallPaths.Capture();

            // 曲選択タブの登録は MenuUiRegistrar が行う。
            // BSML のシングルトンはここではまだ取得できない
            zenjector.Install<JumpDrillMenuInstaller>(Location.Menu);

            // ドリルを叩いたときのリプレイ記録。練習もソロの場面で動く
            zenjector.Install<JumpDrillGameInstaller>(Location.StandardPlayer);
        }

        /// <summary>
        /// 本体の処理への差し込み。リプレイを BeatLeader と同じ値で残すのに使う
        /// （<see cref="Gameplay.ScoreControllerPatch"/>、<see cref="Gameplay.SwingRatingPatches"/>）。
        /// </summary>
        private readonly HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("JumpDrillMod");

        [OnEnable]
        public void OnEnable()
        {
            try
            {
                harmony.PatchAll(typeof(Plugin).Assembly);
            }
            catch (System.Exception e)
            {
                // 差し込めなくてもリプレイは残る（ノーツの時刻と振りの評価が BeatLeader と少しずれるだけ）
                Log?.Error("Harmony patching failed: " + e);
            }

            LogDebug("JumpDrillMod enabled");
        }

        [OnDisable]
        public void OnDisable()
        {
            harmony.UnpatchSelf();
            LogDebug("JumpDrillMod disabled");
        }
    }
}
