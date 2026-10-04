#if JUMPDRILL_LEADERBOARDCORE
using System;
using System.Linq;
using BeatSaberMarkupLanguage;
using HMUI;
using JumpDrill.Model;
using JumpDrillMod.Services;
using LeaderboardCore.Models;

namespace JumpDrillMod.UI.Leaderboard
{
    /// <summary>
    /// 曲選択画面のリーダーボード枠に足す JumpDrill のリーダーボード。
    /// </summary>
    /// <remarks>
    /// 枠に自前のものを足す口は <b>LeaderboardCore</b> が持っている。
    /// ScoreSaber も BeatLeader も LocalLeaderboard もこれを通していて、
    /// 左右の矢印で切り替わるのはここに登録されたもの。
    ///
    /// 本体のビューに自前のものを重ねて本体側を隠す形は、他のリーダーボードを壊しうるので使わない。
    ///
    /// <see cref="ShowForLevel"/> が false を返す譜面では矢印にも出ないので、
    /// ドリル以外の曲では普段どおりの並びになる。
    /// </remarks>
    internal class JumpDrillLeaderboard : CustomLeaderboard
    {
        private readonly JumpDrillLeaderboardPanel panel;
        private readonly JumpDrillLeaderboardView view;
        private readonly ReplayScoreReader reader;
        private readonly DiagramService diagrams;
        private readonly LastLeaderboardSetting? lastLeaderboard;

        internal JumpDrillLeaderboard(ReplayScoreReader reader, DiagramService diagrams, Medals.DrillSetScreen drillSetScreen,
            LastLeaderboardSetting? lastLeaderboard)
        {
            this.reader = reader;
            this.diagrams = diagrams;
            this.lastLeaderboard = lastLeaderboard;

            panel = BeatSaberUI.CreateViewController<JumpDrillLeaderboardPanel>();
            view = BeatSaberUI.CreateViewController<JumpDrillLeaderboardView>();

            // 曲選択の枠からも Jump Drill 画面を開けるようにする。
            // Jump Drill 画面の右に出す方には渡さない（その画面から自分を開き直すことになる）
            view.OpenDrillSet = () => drillSetScreen.Open();

            // 叩き終えて曲選択に戻った時点では、まだリプレイを書いている途中のことがある。
            // 書き終えたら、いま出している譜面の記録を読み直す（いま叩いた記録が選ばれる）
            Gameplay.DrillReplayRecorder.Saved += OnSaved;
        }

        private void OnSaved(string _) => Reload();

        /// <summary>登録を外すときに呼ぶ。保存の知らせは static なので、外さないと古いものが読み直し続ける。</summary>
        internal void Release()
        {
            Gameplay.DrillReplayRecorder.Saved -= OnSaved;
        }

        /// <summary>いま枠に出しているドリル。出していなければ null。</summary>
        private string? shownDrillId;

        private void Reload()
        {
            if (shownDrillId == null) return;

            try
            {
                view.SetScores(reader.For(shownDrillId, PluginConfig.Instance?.PackName ?? "JumpDrill"), diagrams);
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not reload the leaderboard: " + e);
            }
        }

        protected override ViewController panelViewController => panel;
        protected override ViewController leaderboardViewController => view;

        protected override string leaderboardId => "JumpDrill";

        /// <summary>
        /// この譜面で枠に出すか。ドリル以外では出さない。
        /// </summary>
        /// <remarks>
        /// 譜面がドリルかどうかは<b>曲名の頭の ID</b> で見る（JumpDrill.Core の <c>DrillNaming</c>）。
        /// levelId はハッシュなので、それだけでは自作ドリルか判別できない。
        /// </remarks>
        public override bool ShowForLevel(BeatmapKey? beatmapKey)
        {
            string? drillId = DrillIdOf(beatmapKey);
            string? previous = shownDrillId;
            shownDrillId = drillId;
            if (drillId == null)
            {
                if (beatmapKey != null) lastLeaderboard?.LeaveDrill(this, beatmapKey.Value);
                return false;
            }

            lastLeaderboard?.EnterDrill(this);

            // 出すと決まった時点で中身を入れる。枠が開くのはこの後なので間に合う。
            // LeaderboardCore は1回の選曲で何度もこれを聞く（ScoreSaber の読み込み完了などでも）。
            // 同じドリルのままなら読み直さない。読み直すと選んでいる行とページが戻り、ディスクも毎回読む。
            // 叩いた後の新しい記録は、保存の知らせ（Reload）で入る
            if (drillId != previous)
            {
                panel.SetDrill(drillId);
                view.SetScores(reader.For(drillId, PluginConfig.Instance?.PackName ?? "JumpDrill"), diagrams);
            }
            return true;
        }

        private static string? DrillIdOf(BeatmapKey? beatmapKey)
        {
            if (beatmapKey == null) return null;

            try
            {
                string levelId = beatmapKey.Value.levelId;
                if (string.IsNullOrEmpty(levelId)) return null;

                var level = SongCore.Loader.GetLevelById(levelId);
                return level == null ? null : DrillNaming.ExtractId(level.songName);
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not read the selected level: " + e);
                return null;
            }
        }
    }

    /// <summary>
    /// LeaderboardCore が覚えている「最後に見ていたリーダーボード」。null は本体の枠。
    /// ドリルを選んだらこのリーダーボードを開かせ、ドリル以外の曲に戻ったら元のものに戻す。
    /// </summary>
    /// <remarks>
    /// LeaderboardCore は曲を選ぶたびに、各リーダーボードの <c>ShowForLevel</c> を聞いてから
    /// 「最後に見ていたもの」を開き直す。ScoreSaber などはドリル（WIP の譜面）を扱わないので、
    /// そのままだとドリルでは本体の「ハイスコア」の枠に戻ってしまう。
    /// そこで <c>ShowForLevel</c> の中で「最後に見ていたもの」をこのリーダーボードに書き換える。
    ///
    /// 戻すときは同じ場では書き戻せない。LeaderboardCore はこの後で「最後に見ていたもの」が
    /// 出せないのを見て本体の枠に戻す（そこで今の位置を 0 に戻す）。書き戻しが先だと位置がずれたまま
    /// このリーダーボードが残る。なので次のフレームで書き戻し、曲を選んだ知らせを送り直して開き直させる。
    ///
    /// 外から切り替える口が無く、設定（<c>PluginConfig.LastLeaderboard</c>）も切り替えの部品も internal なので、
    /// Zenject から実体を取り出してリフレクションで触る。見つからなければ何もしない（切り替えないだけ）。
    /// 覚えているのは IPA の設定ファイルに書かれる値で、キーは「プラグイン名＋リーダーボードの ID」
    /// （<c>CustomLeaderboard.LeaderboardId</c>）。
    /// </remarks>
    internal class LastLeaderboardSetting
    {
        private readonly object config;
        private readonly System.Reflection.PropertyInfo property;
        private readonly object? navigation;
        private readonly System.Reflection.MethodInfo? notifySet;

        /// <summary>
        /// ドリル以外の曲で見ていたもの。ドリルから離れたらここに戻す。
        /// </summary>
        /// <remarks>
        /// ドリル以外の曲を選ぶたびに覚え直す。LeaderboardCore は「最後に見ていたもの」を設定ファイルに残すので、
        /// ドリルを選んだまま閉じると次の起動では最初から JumpDrill のものになっていて、
        /// 切り替える前の値を見る機会が無い。なので覚えたものもこのMODの設定に残す。
        /// 空文字は本体の枠、null はまだ分からない（戻さず LeaderboardCore に任せる）。
        /// </remarks>
        private static string? Outside
        {
            get => PluginConfig.Instance?.LeaderboardOutsideDrills;
            // 設定は代入のたびにファイルへ書かれるので、変わったときだけ書く
            set { if (PluginConfig.Instance != null && PluginConfig.Instance.LeaderboardOutsideDrills != value) PluginConfig.Instance.LeaderboardOutsideDrills = value; }
        }

        /// <summary>
        /// 直前に選んでいたのがドリルか。ドリルからドリルへ移るときに見ているものは、
        /// ドリルの上で矢印で選んだものなので、戻し先として覚えない。
        /// </summary>
        private bool onDrill;

        private LastLeaderboardSetting(object config, System.Reflection.PropertyInfo property,
            object? navigation, System.Reflection.MethodInfo? notifySet)
        {
            this.config = config;
            this.property = property;
            this.navigation = navigation;
            this.notifySet = notifySet;
        }

        private string? Value
        {
            get => property.GetValue(config) as string;
            set => property.SetValue(config, value);
        }

        private static readonly System.Reflection.PropertyInfo? LeaderboardIdProperty =
            typeof(CustomLeaderboard).GetProperty("LeaderboardId",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        private static string? IdOf(CustomLeaderboard leaderboard) => LeaderboardIdProperty?.GetValue(leaderboard) as string;

        /// <summary>切り替えを次のフレームに頼んである。同じ曲で何度も聞かれるので1回にまとめる。</summary>
        private bool enterPending;

        /// <summary>戻すのを次のフレームに頼んである。</summary>
        private bool leavePending;

        /// <remarks>
        /// 切り替えは LeaderboardCore が曲を選んだ処理を終えた後（次のフレーム）にやる。
        /// LeaderboardCore が「最後に見ていたもの」を開き直すのは本体の枠を見ているときだけで、
        /// ScoreSaber などの枠を見ていると何もせず、前の曲の表示が残る。
        /// なので見ているものを自分で閉じて、このリーダーボードを開く。
        /// </remarks>
        internal void EnterDrill(CustomLeaderboard leaderboard)
        {
            bool fromDrill = onDrill;
            onDrill = true;

            if (enterPending || navigation == null) return;
            enterPending = true;

            RunNextFrame(() =>
            {
                enterPending = false;
                SwitchTo(leaderboard, fromDrill);
            }, "could not switch to this leaderboard");
        }

        private void SwitchTo(CustomLeaderboard leaderboard, bool fromDrill)
        {
            string? id = IdOf(leaderboard);
            if (id == null || navigation == null) return;

            var ordered = Field<System.Collections.Generic.List<CustomLeaderboard>>("orderedCustomLeaderboards");
            var byId = Field<System.Collections.Generic.Dictionary<string, CustomLeaderboard>>("customLeaderboardsById");
            var indexField = NavigationField("currentIndex");
            if (ordered == null || byId == null || indexField == null) return;

            int target = ordered.IndexOf(leaderboard) + 1;
            if (target == 0) return;   // この曲では出さないことになった（選び直された）

            int current = (int)indexField.GetValue(navigation);
            string? last = Value;
            if (current == target && last == id) return;   // もう開いている

            // 通常の曲から来たときだけ、いま見ているものを戻し先として覚える
            if (last != id && !fromDrill) Outside = last ?? string.Empty;

            if (current == 0)
            {
                // 本体の枠を見ている。LeaderboardCore と同じく、本体の枠を退けて開く
                Value = id;
                Invoke("SwitchToLastLeaderboard");
            }
            else
            {
                // 別のリーダーボードを見ている。それを閉じて開く
                CustomLeaderboard? shown = last != null && byId.TryGetValue(last, out var found) ? found : null;
                Invoke("SwitchToIndex", target, shown);
            }

            // 左右の矢印の出し入れを更新させる
            Invoke("NotifyPropertyChanged", "LeftButtonActive");
            Invoke("NotifyPropertyChanged", "RightButtonActive");
        }

        private T? Field<T>(string name) where T : class => NavigationField(name)?.GetValue(navigation) as T;

        private System.Reflection.FieldInfo? NavigationField(string name) =>
            navigation?.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private void Invoke(string name, params object?[] args)
        {
            for (var type = navigation?.GetType(); type != null; type = type.BaseType)
            {
                var method = type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                                             System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length);
                if (method == null) continue;

                method.Invoke(navigation, args);
                return;
            }
            Plugin.Log?.Warn("LeaderboardCore: " + name + " not found");
        }

        private static void RunNextFrame(Action action, string what)
        {
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Plugin.Log?.Warn("LeaderboardCore: " + what + ": " + e);
                }
            }, System.Threading.CancellationToken.None, System.Threading.Tasks.TaskCreationOptions.None,
                IPA.Utilities.Async.UnityMainThreadTaskScheduler.Default);
        }

        internal void LeaveDrill(CustomLeaderboard leaderboard, BeatmapKey key)
        {
            try
            {
                onDrill = false;

                string? current = Value;
                if (current != IdOf(leaderboard))
                {
                    // ドリル以外の曲で見ているもの。次にドリルから離れたときの戻し先
                    Outside = current ?? string.Empty;
                    return;
                }

                // ドリルから離れた。LeaderboardCore はこの後で本体の枠に戻すので、次のフレームで戻し先を開き直させる
                // 戻し先がまだ分からない（覚える前の版から上げた直後など）ときは本体の枠に戻す。
                // 戻さないと「最後に見ていたもの」が JumpDrill のまま残る
                string outside = Outside ?? string.Empty;
                if (leavePending) return;
                string? restore = outside.Length == 0 ? null : outside;
                leavePending = true;

                RunNextFrame(() =>
                {
                    leavePending = false;
                    Value = restore;
                    notifySet?.Invoke(navigation, new object[] { key });
                }, "could not switch back");
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn("LeaderboardCore: could not switch back: " + e.Message);
            }
        }

        internal static LastLeaderboardSetting? Find(Zenject.DiContainer container)
        {
            try
            {
                var type = typeof(LeaderboardCore.Managers.CustomLeaderboardManager).Assembly
                    .GetType("LeaderboardCore.Configuration.PluginConfig");
                var property = type?.GetProperty("LastLeaderboard");
                var config = type == null ? null : container.TryResolve(type);
                if (config == null || property == null)
                {
                    Plugin.Log?.Warn("LeaderboardCore: could not find its config; drills will not switch to this leaderboard");
                    return null;
                }
                // 切り替えの部品（曲を選んだ知らせの受け口でもある）。無ければドリルでの切り替えも戻しもしない
                var navigationType = type!.Assembly.GetType("LeaderboardCore.UI.ViewControllers.LeaderboardNavigationButtonsController");
                var navigation = navigationType == null ? null : container.TryResolve(navigationType);
                var notifySet = navigationType?.GetMethod("OnLeaderboardSet", new[] { typeof(BeatmapKey) });
                return new LastLeaderboardSetting(config, property, navigation, navigation == null ? null : notifySet);
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn("LeaderboardCore: could not read its config: " + e.Message);
                return null;
            }
        }
    }
}
#endif
