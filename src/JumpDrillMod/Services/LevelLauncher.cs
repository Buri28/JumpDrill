using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using IPA.Utilities;
using JumpDrill.Model;
using UnityEngine;
using Zenject;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// ドリルをその場で始める（練習モード）。
    /// </summary>
    /// <remarks>
    /// 本体の曲選択を通さず、<c>MenuTransitionsHelper.StartStandardLevel</c> で直接始める。
    /// Jump Drill 画面はメインメニューからも開くので、曲選択が無いところからでも叩けるようにするため。
    ///
    /// 始め方は本体の練習の「プレイ」と同じにしてある。
    /// <list type="bullet">
    ///   <item>設定（左右・高さ・モディファイア・色・環境）はプレイヤーが本体で選んでいるもの</item>
    ///   <item>練習の設定を付ける（頭から、等速）。練習として扱われ、スコアは送信されない</item>
    ///   <item>やり直しは本体がその場でやる。終わったら（戻る・クリア・失敗）呼んだ画面に戻る</item>
    /// </list>
    /// 記録はこのMODのリプレイ記録が残す（BeatLeader は本体のボタンから始めたときしか記録しない）。
    /// </remarks>
    internal class LevelLauncher
    {
        private readonly DrillPackLocator pack;
        private readonly MenuTransitionsHelper? transitions;
        private readonly PlayerDataModel? playerData;
        private readonly EnvironmentsListModel? environments;

        /// <summary>
        /// 最後に始めた時刻。画面が切り替わる間（約 0.7 秒）に Play をもう一度押すと、
        /// 本体は2度目の切り替えを無視するが、どの曲を始めるかの指定だけは上書きされてしまう。
        /// </summary>
        private float lastStart = float.NegativeInfinity;

        /// <summary>続けて始めるのを受け付けない間（秒）。切り替えが終わるまでより長くとる。</summary>
        private const float StartCooldown = 3f;

        internal LevelLauncher(DrillPackLocator pack,
            [InjectOptional] MenuTransitionsHelper? transitions,
            [InjectOptional] PlayerDataModel? playerData,
            [InjectOptional] EnvironmentsListModel? environments)
        {
            this.pack = pack;
            this.transitions = transitions;
            this.playerData = playerData;
            this.environments = environments;
        }

        /// <summary>
        /// そのドリルが始められるか。始められなければ画面に出す理由を返す。
        /// </summary>
        /// <param name="drillId">曲名か ID（<see cref="Resolve"/>）。</param>
        internal string? Check(string drillId)
        {
            try
            {
                return Resolve(drillId) == null
                    ? "This drill has not been generated yet."
                    : null;
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not look up the drill: " + e);
                return "Could not look up the drill. See the log.";
            }
        }

        /// <summary>
        /// そのドリルの譜面がまだ無いか。曲一覧を読み込んでいる最中は分からないので false。
        /// </summary>
        /// <param name="drillId">曲名か ID（<see cref="Resolve"/>）。標準のステージの曲名なら、その名前の譜面があるかだけを見る。</param>
        internal bool IsMissing(string drillId)
        {
            try
            {
                return !SongCore.Loader.AreSongsLoading && Resolve(drillId) == null;
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not look up the drill: " + e);
                return false;
            }
        }

        /// <summary>
        /// そのドリルを始める。始められなければ画面に出す理由を返す。
        /// </summary>
        /// <param name="onFinished">
        /// 叩き終えてメニューに戻ったところで呼ぶ（やり直しでは呼ばない）。
        /// </param>
        /// <param name="beforeSceneSwitch">場面が切り替わる直前に呼ぶ。始められなかったときは呼ばない。</param>
        /// <param name="drillId">曲名か ID（<see cref="Resolve"/>）。表のマスからは曲名を渡す。</param>
        internal string? Start(string drillId,
            Action<StandardLevelScenesTransitionSetupDataSO, LevelCompletionResults>? onFinished,
            Action? beforeSceneSwitch = null)
        {
            try
            {
                if (Time.realtimeSinceStartup - lastStart < StartCooldown) return null;

                // 読み込みの途中だと一覧が欠けていて「無い」と誤って出るうえ、
                // 叩いている間に譜面の実体が差し替わる
                if (SongCore.Loader.AreSongsLoading) return "Songs are still loading. Try again in a moment.";

                // マルチのロビーの中から1人用の曲は始められない（接続したまま別の場面に移ってしまう）
                if (InMultiplayer()) return "Drills cannot be played from a multiplayer lobby.";

                var found = Resolve(drillId);
                if (found == null) return "This drill has not been generated yet.";

                var helper = transitions ?? Find<MenuTransitionsHelper>();
                var model = playerData ?? Find<PlayerDataModel>();
                var envs = environments ?? FindEnvironments();
                if (helper == null || model == null || envs == null)
                {
                    Plugin.Log?.Warn($"could not start the drill: transitions={helper != null} player={model != null} environments={envs != null}");
                    return "Could not start the drill. See the log.";
                }

                var level = found.Level;
                var keys = level.GetBeatmapKeys().ToList();
                if (keys.Count == 0) return "This drill has no difficulty to play.";

                // ドリルは Standard の1難易度だけ。念のため Standard を優先する
                var key = keys.FirstOrDefault(k => k.beatmapCharacteristic?.serializedName == "Standard");
                if (key.beatmapCharacteristic == null) key = keys[0];

                var data = model.playerData;
                var colors = data.colorSchemesSettings;

                StartStandardLevel(helper, new Dictionary<string, object?>
                {
                    ["gameMode"] = "Solo",
                    ["beatmapKey"] = key,
                    ["beatmapLevel"] = level,
                    ["overrideEnvironmentSettings"] = data.overrideEnvironmentSettings,
                    ["playerOverrideColorScheme"] = colors.GetOverrideColorScheme(),
                    // 1.39 までの名前
                    ["overrideColorScheme"] = colors.GetOverrideColorScheme(),
                    ["playerOverrideLightshowColors"] = ShouldOverrideLightshowColors(colors),
                    ["beatmapOverrideColorScheme"] = level.GetColorScheme(key.beatmapCharacteristic, key.difficulty),
                    ["gameplayModifiers"] = data.gameplayModifiers,
                    ["playerSpecificSettings"] = data.playerSpecificSettings,
                    ["practiceSettings"] = new PracticeSettings(0f, 1f),
                    ["environmentsListModel"] = envs,
                    ["backButtonText"] = "Menu",
                    ["useTestNoteCutSoundEffects"] = false,
                    ["startPaused"] = false,
                    ["beforeSceneSwitch"] = beforeSceneSwitch,
                    ["levelFinishedCallback"] = new Action<StandardLevelScenesTransitionSetupDataSO, LevelCompletionResults>(
                        (setup, results) => onFinished?.Invoke(setup, results)),
                });
                // 始められてから。先に入れると、失敗した後の数秒は押しても黙って何も起きない
                lastStart = Time.realtimeSinceStartup;

                return null;
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not start the drill: " + e);
                return "Could not start the drill. See the log.";
            }
        }

        /// <summary>
        /// <c>MenuTransitionsHelper.StartStandardLevel</c> を、引数の名前で合わせて呼ぶ。
        /// </summary>
        /// <remarks>
        /// 引数の並びがゲームの版で違う。1.44 からは戻るボタンの文字・一時停止で始めるかなどが
        /// <c>GameplayAdditionalInformation</c> にまとめられた。版ごとに #if で書き分けず、
        /// 引数の名前で値を当てる（どの版でビルドした DLL でも、どちらの版でも始められる）。
        /// 呼ぶのは、譜面の中身（<c>IBeatmapLevelData</c>）を渡さなくてよい形。
        /// </remarks>
        private static void StartStandardLevel(MenuTransitionsHelper helper, Dictionary<string, object?> values)
        {
            var method = typeof(MenuTransitionsHelper).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == nameof(MenuTransitionsHelper.StartStandardLevel))
                .FirstOrDefault(m => m.GetParameters().All(p =>
                    p.HasDefaultValue || p.ParameterType.Name != "IBeatmapLevelData"));
            if (method == null) throw new MissingMethodException(nameof(MenuTransitionsHelper), "StartStandardLevel");

            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                args[i] = ArgumentFor(parameters[i], values);

            method.Invoke(helper, args);
        }

        private static object? ArgumentFor(ParameterInfo parameter, Dictionary<string, object?> values)
        {
            string name = parameter.Name;
            var type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;

            if (values.TryGetValue(name, out var value)) return value;

            // 場面が切り替わる前の呼び出しは、版によって名前が違う（beforeSceneSwitchCallback など）
            if (name.StartsWith("beforeSceneSwitch", StringComparison.Ordinal)) return values["beforeSceneSwitch"];

            // 1.44 から: 戻るボタンの文字などをまとめたもの。同じ名前の値を当てて作る
            if (type.Name == "GameplayAdditionalInformation") return Construct(type, values);

            if (parameter.HasDefaultValue && parameter.DefaultValue != null && parameter.DefaultValue != DBNull.Value)
                return type.IsEnum ? Enum.ToObject(type, parameter.DefaultValue) : parameter.DefaultValue;
            // 既定値も無いのに名前が合わない。次の版で引数の名前が変わったときに気づけるよう残す
            if (!parameter.HasDefaultValue) Plugin.LogDebug("StartStandardLevel: no value for " + name + ", passing the default");
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        /// <summary>
        /// 自分の色をライトにも使うか。1.40 から。それより前の版には無いので false（その版の
        /// <c>StartStandardLevel</c> にはこの引数も無く、値は使われない）。
        /// </summary>
        private static bool ShouldOverrideLightshowColors(ColorSchemesSettings colors)
        {
            var method = typeof(ColorSchemesSettings).GetMethod("ShouldOverrideLightshowColors",
                BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            return method != null && (bool)method.Invoke(colors, null);
        }

        private static object Construct(Type type, Dictionary<string, object?> values)
        {
            var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            var ps = ctor.GetParameters();
            var args = new object?[ps.Length];
            for (int i = 0; i < ps.Length; i++)
                args[i] = ArgumentFor(ps[i], values);
            return ctor.Invoke(args);
        }

        /// <summary>マルチの画面（モード選択・ロビー）が開いているか。</summary>
        private static bool InMultiplayer()
        {
            return Resources.FindObjectsOfTypeAll<MultiplayerModeSelectionFlowCoordinator>()
                .Any(f => f != null && f.isActivated);
        }

        /// <summary>
        /// 環境の一覧。インストーラーの入れ物に無いときは、本体の曲選択が持っているものを借りる。
        /// </summary>
        private static EnvironmentsListModel? FindEnvironments()
        {
            var flow = Find<SoloFreePlayFlowCoordinator>();
            if (flow == null) return null;

            try
            {
                SinglePlayerLevelSelectionFlowCoordinator baseFlow = flow;
                return FieldAccessor<SinglePlayerLevelSelectionFlowCoordinator, EnvironmentsListModel>
                    .Get(ref baseFlow, "_environmentsListModel");
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn("could not read the environments list: " + e.Message);
                return null;
            }
        }

        /// <summary>読み込まれているドリル譜面1本と、それが入っているパック。</summary>
        internal sealed class DrillLevel
        {
            public string Id = string.Empty;
            public string SongName = string.Empty;
            /// <summary>譜面のフォルダ。SongCore の一覧の鍵。</summary>
            public string Folder = string.Empty;
            public BeatmapLevel Level = null!;
            public BeatmapLevelPack Pack = null!;
        }

        /// <summary>
        /// 読み込まれているドリル譜面を全部。曲名に ID があるものがドリル。
        /// </summary>
        /// <remarks>
        /// 専用パック → WIP → CustomLevels の順に見て、同じ曲名は最初に見つけた方を採る。
        /// <b>ID ではまとめない。</b> NJS やクリック音だけ変えて作った譜面は同じ ID の別の曲名になる
        /// （標準のステージと同じ形を Mods タブで作ると、頭の並び順の印だけが違う）。ID でまとめると片方が一覧から消え、
        /// どちらを叩くかが読み込みの順で決まってしまう。
        /// 一括生成やこのMODの生成は専用パックに書くが、CLI の <c>--install</c> などで
        /// 別の場所に置いたドリルもある。どこにあっても叩けるように全部見る。
        /// 選ぶときは、その譜面が入っているパックへ切り替える必要があるので、パックも一緒に持つ。
        /// </remarks>
        internal List<DrillLevel> DrillLevels()
        {
            var found = new List<DrillLevel>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void Add(IEnumerable<KeyValuePair<string, BeatmapLevel>>? levels, BeatmapLevelPack? pack)
            {
                if (levels == null || pack == null) return;

                foreach (var entry in levels)
                {
                    var level = entry.Value;
                    if (level == null) continue;

                    string? id = DrillNaming.ExtractId(level.songName);
                    if (id == null || !seen.Add(level.songName)) continue;

                    found.Add(new DrillLevel
                    {
                        Id = id, SongName = level.songName, Folder = entry.Key, Level = level, Pack = pack,
                    });
                }
            }

            string folder = pack.ResolveOutputFolder(PluginConfig.Instance?.PackName ?? "JumpDrill");
            var separates = SongCore.Loader.SeparateSongFolders ?? new List<SongCore.Data.SeparateSongFolder>();

            // 専用パックを先に。同じ曲名が別の場所にもあれば、ここのものを選ばせる
            foreach (var separate in separates.Where(f => SamePath(f.SongFolderEntry?.Path, folder)))
                Add(separate.Levels, separate.LevelPack);
            foreach (var separate in separates.Where(f => !SamePath(f.SongFolderEntry?.Path, folder)))
                Add(separate.Levels, separate.LevelPack);

            Add(SongCore.Loader.CustomWIPLevels, SongCore.Loader.WIPLevelsPack);
            Add(SongCore.Loader.CustomLevels, SongCore.Loader.CustomLevelsPack);

            return found;
        }

        /// <summary>
        /// 読み込まれているドリル譜面のフォルダを全部（同じ曲名が別の場所にあっても全部）。鍵は SongCore の一覧のまま。
        /// </summary>
        internal List<string> DrillFolders()
        {
            var folders = new List<string>();

            void Add(IEnumerable<KeyValuePair<string, BeatmapLevel>>? levels)
            {
                if (levels == null) return;
                foreach (var entry in levels)
                    if (entry.Value != null && DrillNaming.ExtractId(entry.Value.songName) != null) folders.Add(entry.Key);
            }

            foreach (var separate in SongCore.Loader.SeparateSongFolders ?? new List<SongCore.Data.SeparateSongFolder>())
                Add(separate.Levels);
            Add(SongCore.Loader.CustomWIPLevels);
            Add(SongCore.Loader.CustomLevels);
            return folders;
        }

        /// <summary>
        /// その曲名の譜面のフォルダを、読み込まれている場所すべてから集める（同じドリルが複数の場所にあることがある）。
        /// ドリルの削除と作り直しで使う。ID を渡したときは、その ID の譜面を全部。
        /// </summary>
        /// <remarks>
        /// 曲名で見るのは、同じ ID の別の譜面（NJS などだけ変えたもの）まで消したり作り直したりしないため。
        /// </remarks>
        internal List<string> FoldersOf(string drillId)
        {
            var folders = new List<string>();
            bool byName = IsSongName(drillId);

            void Add(IEnumerable<KeyValuePair<string, BeatmapLevel>>? levels)
            {
                if (levels == null) return;
                foreach (var entry in levels)
                {
                    if (entry.Value == null) continue;
                    if (byName ? entry.Value.songName != drillId : DrillNaming.ExtractId(entry.Value.songName) != drillId) continue;
                    if (!folders.Any(f => SamePath(f, entry.Key))) folders.Add(entry.Key);
                }
            }

            foreach (var separate in SongCore.Loader.SeparateSongFolders ?? new List<SongCore.Data.SeparateSongFolder>())
                Add(separate.Levels);
            Add(SongCore.Loader.CustomWIPLevels);
            Add(SongCore.Loader.CustomLevels);
            return folders;
        }

        /// <summary>
        /// 曲名か ID から譜面と、それが入っているパックを引く。無ければ null。
        /// </summary>
        /// <remarks>
        /// 表のマスからは曲名で引く。ID だけで引くと、同じ ID の別の譜面（NJS などだけ変えて作ったもの）を始めてしまう。
        /// 曲名の譜面が無いとき、標準のステージの曲名なら無いと答える（Play がその1本を作ってから始める）。
        /// それ以外は古い名前のままのマスなどなので、今までどおり同じ ID の譜面で代わりにする。
        /// </remarks>
        private DrillLevel? Resolve(string drillId)
        {
            var levels = DrillLevels();
            if (!IsSongName(drillId)) return levels.FirstOrDefault(d => d.Id == drillId);

            var exact = levels.FirstOrDefault(d => d.SongName == drillId);
            if (exact != null) return exact;
            if (DrillSet.IsStandardName(drillId))
            {
                // カスタムステージに古い書き方の名前（EBPM など）で残っているマスは、今の名前の譜面で始める
                string? current = DrillSet.FindStandard(drillId)?.Options.Name;
                return current == null || current == drillId ? null : levels.FirstOrDefault(d => d.SongName == current);
            }

            string? id = DrillNaming.ExtractId(drillId);
            return id == null ? null : levels.FirstOrDefault(d => d.Id == id);
        }

        /// <summary>ID（5文字）ではなく曲名か。</summary>
        private static bool IsSongName(string key) => DrillNaming.ExtractId(key) != null;

        /// <summary>
        /// 画面に出ている方を掴む。
        /// </summary>
        /// <remarks>
        /// Zenject から取れなかったときの代わり。このMODのインストーラーが入る入れ物
        /// （<c>MainSettingsMenuViewControllersInstaller</c>）には、メニューの部品が居ないことがある。
        /// </remarks>
        private static T? Find<T>() where T : MonoBehaviour
        {
            var all = Resources.FindObjectsOfTypeAll<T>();
            return all.FirstOrDefault(c => c != null && c.isActiveAndEnabled) ?? all.FirstOrDefault(c => c != null);
        }

        private static bool SamePath(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(
                Path.GetFullPath(a!).TrimEnd('\\', '/'),
                Path.GetFullPath(b!).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
