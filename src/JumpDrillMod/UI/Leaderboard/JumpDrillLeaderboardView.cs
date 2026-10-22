using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Components.Settings;
using RankMetric = JumpDrill.Replays.RankMetric;
using HMUI;
using JumpDrillMod.Models;
using JumpDrillMod.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ScoreFormat = JumpDrill.Replays.ScoreFormat;

namespace JumpDrillMod.UI.Leaderboard
{
    /// <summary>
    /// リーダーボード枠の本体。そのドリルの記録を並べる。
    /// </summary>
    /// <remarks>
    /// 本体のスコア表（<c>LeaderboardTableView</c>）は使わない。あれは
    /// 「順位・名前・スコア」の3列で、スコアが整数1つに限られる。
    /// ここで見たいのは再現精度・精度・ミスと日時なので、自前で行を並べる。
    /// </remarks>
    internal class JumpDrillLeaderboardView : BSMLResourceViewController
    {
        /// <summary>出す行数。残りは軌道の図に回すので、枠に収まるぶんより少ない。</summary>
        internal const int Rows = 5;

        /// <summary>図を描く係。ビューは Zenject を通らないので、外から渡される。</summary>
        private DiagramService? diagrams;

        public override string ResourceName => "JumpDrillMod.Resources.Leaderboard.bsml";

        /// <summary>順位の順（基準で並べ直したもの、<see cref="RankSetting.Rank"/>）。# の数字はこの並びで付ける。</summary>
        private IReadOnlyList<DrillScore> ranked = new List<DrillScore>();

        /// <summary>画面に出す順。順位の順か新しい順（<see cref="NewestFirst"/>）。</summary>
        private IReadOnlyList<DrillScore> scores = new List<DrillScore>();

        [UIComponent("header")] private TextMeshProUGUI? header = null;
        [UIComponent("note")] private TextMeshProUGUI? note = null;

        [UIComponent("row-1")] private ClickableText? row1 = null;
        [UIComponent("row-2")] private ClickableText? row2 = null;
        [UIComponent("row-3")] private ClickableText? row3 = null;
        [UIComponent("row-4")] private ClickableText? row4 = null;
        [UIComponent("row-5")] private ClickableText? row5 = null;

        [UIComponent("diagram")] private ImageView? diagram = null;
        [UIComponent("diagram-note")] private TextMeshProUGUI? diagramNote = null;
        [UIComponent("view-button")] private Button? viewButton = null;
        [UIComponent("drill-set-button")] private Button? drillSetButton = null;
        [UIComponent("delete-button")] private Button? deleteButton = null;

        /// <summary>
        /// Jump Drill 画面を開く。開けなければ理由を返す。null ならボタンを出さない
        /// （Jump Drill 画面の右の枠に出しているとき）。
        /// </summary>
        internal Func<string?>? OpenDrillSet { get; set; }

        [UIComponent("replay-1")] private ClickableImage? replay1 = null;
        [UIComponent("replay-2")] private ClickableImage? replay2 = null;
        [UIComponent("replay-3")] private ClickableImage? replay3 = null;
        [UIComponent("replay-4")] private ClickableImage? replay4 = null;
        [UIComponent("replay-5")] private ClickableImage? replay5 = null;

        /// <summary>
        /// 出すものを差し替える。画面ができる前に呼ばれても捨てずに持っておき、
        /// <c>#post-parse</c> で描き直す。
        /// </summary>
        internal void SetScores(IReadOnlyList<DrillScore> scores, DiagramService diagrams)
        {
            rankedMetric = RankSetting.Current;
            ranked = RankSetting.Rank(scores, rankedMetric);
            this.scores = Arrange(ranked);
            this.diagrams = diagrams;

            // 譜面が変われば選び直し。前の譜面の3位を選んでいた、を持ち越さない。
            // 前の譜面の記録を消す確かめの窓も閉じる
            confirm.No(parserParams);
            selected = 0;
            page = 0;
            SelectJustPlayed();
            Fill();
        }

        /// <summary>
        /// もう自動では選ばない「いま叩いた記録」。行を押したか、別の譜面に移ったら入れる。
        /// </summary>
        private string? justPlayedDone;

        /// <summary>
        /// 叩いた直後は、その記録を選んでそのページを出す。叩いたばかりのものを探させない。
        /// </summary>
        /// <remarks>
        /// 戻った後も枠は何度か読み直される（曲選択に戻ったとき・リプレイを書き終えたとき）。
        /// 1回選んだだけだと次の読み直しでベストに戻るので、行を押すか別の譜面に移るまでは選び続ける。
        ///
        /// 一覧に出るのがこのMODのリプレイとは限らない。BeatLeader も同じプレイを残していればそちらが出る
        /// （BeatLeader が書き終えるのが後なら、最初は自分の分、読み直すと BeatLeader の分）。
        /// なのでファイル名ではなく、同じプレイかどうかで探す。
        /// </remarks>
        private void SelectJustPlayed()
        {
            string? latest = Gameplay.DrillReplayRecorder.LastSavedPath;
            if (latest == null || latest == justPlayedDone) return;

            int found = -1;
            long nearest = long.MaxValue;
            for (int i = 0; i < scores.Count; i++)
            {
                long gap = string.Equals(scores[i].ReplayPath, latest, StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : JumpDrill.Replays.ReplayLibrary.SamePlayGap(scores[i].ReplayPath, latest);
                if (gap < 0 || gap >= nearest) continue;

                found = i;
                nearest = gap;
            }

            if (found >= 0)
            {
                selected = found;
                page = found / Rows;
                lastShown = latest;
                return;
            }

            // 別の譜面の一覧。戻ってきても、もう選ばない
            if (lastShown == latest) justPlayedDone = latest;
        }

        /// <summary>選んで出した「いま叩いた記録」。一覧から消えたら別の譜面に移ったと分かる。</summary>
        private string? lastShown;

        /// <summary>図に出している記録。0 がベスト。一覧の何ページ目かに依らない通し番号。</summary>
        private int selected;

        /// <summary>何ページ目を出しているか。1ページ <see cref="Rows"/> 件。</summary>
        private int page;

        /// <summary>
        /// 平均軌道だけを描くか（GUI の「平均だけ」と同じ）。設定に残して次も同じで開く。
        /// </summary>
        /// <remarks>
        /// 図は常に正面と横を並べて出す。どちらか一方では読めないものがあるため
        /// （正面図では振りかぶりも振り抜きも手前・奥に潰れ、横図にはノーツの格子が出ない）。
        ///
        /// 1本1本の束は再現性をそのまま太さで見せるが、崩れた回が混ざると
        /// 平均の形が埋もれて読めない。形だけ見たいときに平均だけにする。
        /// </remarks>
        /// <summary>新しい順に並べるか。設定に残して次も同じで開く。</summary>
        private static bool NewestFirst
        {
            get => PluginConfig.Instance?.LeaderboardNewestFirst ?? false;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.LeaderboardNewestFirst = value; }
        }

        /// <summary>出す順に並べる。順位の順はそのまま、新しい順は日時で並べ直す。</summary>
        private static IReadOnlyList<DrillScore> Arrange(IReadOnlyList<DrillScore> ranked)
        {
            return NewestFirst ? ranked.OrderByDescending(s => s.PlayedAt).ToList() : ranked;
        }

        /// <summary>その記録の順位（1 始まり）。並べ方（新しい順）に関わらず、基準（REPRO / ACC）の順。</summary>
        private int RankOf(DrillScore score)
        {
            for (int i = 0; i < ranked.Count; i++)
                if (ReferenceEquals(ranked[i], score)) return i + 1;
            return 0;
        }

        private static bool MeanOnly
        {
            get => PluginConfig.Instance?.DiagramMeanOnly ?? false;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.DiagramMeanOnly = value; }
        }

        // 押した行を選ぶ。BSML の on-click は引数を取れないので行ごとに置く
        [UIAction("pick-1")] public void Pick1() => Select(0);
        [UIAction("pick-2")] public void Pick2() => Select(1);
        [UIAction("pick-3")] public void Pick3() => Select(2);
        [UIAction("pick-4")] public void Pick4() => Select(3);
        [UIAction("pick-5")] public void Pick5() => Select(4);

        // 行の右端の 🔁。押したその行のリプレイを再生する（選んでいる行でなくてよい）
        [UIAction("replay-1")] public void Replay1() => PlayReplay(0);
        [UIAction("replay-2")] public void Replay2() => PlayReplay(1);
        [UIAction("replay-3")] public void Replay3() => PlayReplay(2);
        [UIAction("replay-4")] public void Replay4() => PlayReplay(3);
        [UIAction("replay-5")] public void Replay5() => PlayReplay(4);

        /// <summary>
        /// その行の記録を BeatLeader のゲーム内リプレイヤーで再生する。
        /// </summary>
        /// <remarks>
        /// 図だけでは分からない、実際にどう振っていたかを見るため。
        /// 場面の切り替えから終わった後のメニューへの戻りまで BeatLeader がやる。
        /// </remarks>
        /// <param name="row">画面の上から何行目か。</param>
        private void PlayReplay(int row)
        {
            int index = page * Rows + row;
            var score = index < scores.Count ? scores[index] : null;
            if (score == null) return;

            if (diagramNote != null) diagramNote.text = "Starting the replay...";
            BeatLeaderReplayer.Play(score.ReplayPath, message =>
            {
                if (diagramNote != null) diagramNote.text = message;
            });
        }

        [UIParams] private readonly BeatSaberMarkupLanguage.Parser.BSMLParserParams? parserParams = null;

        /// <summary>削除の前の確かめの窓（<see cref="ConfirmPrompt"/>）。</summary>
        private readonly ConfirmPrompt confirm = new ConfirmPrompt();

        [UIComponent("confirm-text")] private TextMeshProUGUI? confirmText = null;

        [UIAction("confirm-yes")]
        public void ConfirmYes() => confirm.Yes(parserParams);

        [UIAction("confirm-no")]
        public void ConfirmNo() => confirm.No(parserParams);

        /// <summary>
        /// 選んでいる記録を消す。窓で確かめてから消す（元に戻せない）。
        /// </summary>
        /// <remarks>
        /// ごみ箱は通さない（元に戻せない）。同じプレイの BeatLeader・LocalLeaderboard のコピーも消す
        /// （<see cref="ReplayCleanupService"/>）。消したら、この枠を出している画面が読み直す。
        /// 何を消したかは図の下の文に出す。
        /// </remarks>
        [UIAction("delete-record")]
        public void DeleteRecord()
        {
            var score = selected < scores.Count ? scores[selected] : null;
            if (score == null) return;

            string what = string.Format(CultureInfo.InvariantCulture, "{0} ({1})",
                score.CountsAsRecord ? "#" + RankOf(score) : "the record",
                score.PlayedAt.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture));
            confirm.Ask(parserParams, confirmText, "Delete " + what + "?\nThis cannot be undone.", () => DeleteRecordNow(score, what));
        }

        private void DeleteRecordNow(DrillScore score, string what)
        {
            var result = ReplayCleanupService.Delete(new[] { score });
            if (diagramNote != null)
                diagramNote.text = result.Failed > 0 ? "Could not delete some files. See the log." : "Deleted " + what + ".";
        }

        [UIAction("open-drill-set")]
        public void OnOpenDrillSet()
        {
            string? error = OpenDrillSet?.Invoke();
            if (error != null && diagramNote != null) diagramNote.text = error;
        }

        // 並べ方。REPRO / ACC は順位の順で、その数字で順位を付ける（基準、Jump Drill 画面のプルダウンと共通）。
        // Newest は日時の順で、# は直前に選んでいた基準の順位のまま
        private const string OrderRepro = "REPRO";
        private const string OrderAcc = "ACC";
        private const string OrderNewest = "Newest";

        [UIComponent("order-dropdown")] private DropDownListSetting? orderDropdown = null;
        [UIComponent("stroke-dropdown")] private DropDownListSetting? strokeDropdown = null;
        [UIComponent("room-dropdown")] private DropDownListSetting? roomDropdown = null;
        [UIComponent("size-dropdown")] private DropDownListSetting? sizeDropdown = null;

        [UIValue("order-options")]
        public List<object> OrderOptions => new List<object> { OrderRepro, OrderAcc, OrderNewest };

        [UIValue("order")]
        public string Order
        {
            get => NewestFirst ? OrderNewest : RankSetting.Current == RankMetric.Accuracy ? OrderAcc : OrderRepro;
            set { }
        }

        /// <summary>
        /// 並べ方を変える。選んでいた記録は選んだまま、その記録のページを出す。
        /// </summary>
        [UIAction("order-changed")]
        public void OrderChanged(object value)
        {
            string order = value as string ?? OrderRepro;
            NewestFirst = order == OrderNewest;
            if (order != OrderNewest)
                RankSetting.Set(order == OrderAcc ? RankMetric.Accuracy : RankMetric.Reproducibility);

            // 基準が変わったときは Changed から Rerank が呼ばれているが、並べ方だけのときのためにもう一度
            Rerank();
        }

        /// <summary>
        /// 今の基準と並べ方で並べ直す。選んでいた記録は選んだまま、その記録のページを出す。
        /// </summary>
        private void Rerank()
        {
            var current = selected < scores.Count ? scores[selected] : null;

            rankedMetric = RankSetting.Current;
            ranked = RankSetting.Rank(ranked, rankedMetric);
            scores = Arrange(ranked);

            selected = 0;
            for (int i = 0; i < scores.Count; i++)
                if (ReferenceEquals(scores[i], current)) selected = i;
            page = selected / Rows;

            orderDropdown?.ReceiveValue();
            Fill();
        }

        /// <summary>振りを空間に置く（VR 表示）。リーダーボードが2か所にあっても置くのは1つだけ。</summary>
        private static SwingSceneService room => SwingSceneService.Shared;

        private const string RoomOff = "3D view off";

        /// <summary>出す位置の選択肢。プレイしたときの位置と、そこから前へずらした位置 (m)。</summary>
        private static readonly KeyValuePair<string, float>[] RoomPlaces =
        {
            new KeyValuePair<string, float>("3D at play spot", 0f),
            new KeyValuePair<string, float>("3D 0.5 m ahead", 0.5f),
            new KeyValuePair<string, float>("3D 1 m ahead", 1f),
            new KeyValuePair<string, float>("3D 1.5 m ahead", 1.5f),
            new KeyValuePair<string, float>("3D 2 m ahead", 2f),
        };

        private static bool ShowInRoom => PluginConfig.Instance?.DiagramRoom ?? false;

        private static float RoomForward => PluginConfig.Instance?.DiagramRoomForward ?? 0f;

        [UIValue("room-options")]
        public List<object> RoomOptions => new[] { RoomOff }.Concat(RoomPlaces.Select(p => p.Key)).Cast<object>().ToList();

        [UIValue("room")]
        public string Room
        {
            get
            {
                if (!ShowInRoom) return RoomOff;
                // 選択肢に無い値（手で設定を書き換えたなど）は、いちばん近いものを出す
                return RoomPlaces.OrderBy(p => Math.Abs(p.Value - RoomForward)).First().Key;
            }
            set { }
        }

        /// <summary>
        /// 選んでいる記録の振りを空間に出すか、どこに出すか。今どれかが分かるようにプルダウンにする。
        /// プレイしたときの位置のままだと振りの中に立つ形になり、全体を見渡しにくいので、前へずらして出せる。
        /// </summary>
        [UIAction("room-changed")]
        public void RoomChanged(object value)
        {
            var label = value as string;
            var place = RoomPlaces.FirstOrDefault(p => p.Key == label);
            if (PluginConfig.Instance != null)
            {
                PluginConfig.Instance.DiagramRoom = place.Key != null;
                if (place.Key != null) PluginConfig.Instance.DiagramRoomForward = place.Value;
            }
            UpdateRoom();
        }

        /// <summary>選んでいる記録の振りを空間に出し直す。出さない設定か、記録が無ければ消す。</summary>
        private void UpdateRoom()
        {
            var score = selected < scores.Count ? scores[selected] : null;
            if (!ShowInRoom || score == null || !isActiveAndEnabled)
            {
                room.Hide(this);
                return;
            }
            room.Show(this, score.ReplayPath, OnlyForehand, RoomForward, Hitbox, MeanOnly);
        }

        private const string StrokeBoth = "Fore + back";
        private const string StrokeFore = "Forehand";
        private const string StrokeBack = "Backhand";

        /// <summary>軌道の図でフォアだけ（true）かバックだけ（false）。両方なら null（GUI の「フォアとバック」）。</summary>
        private static bool? OnlyForehand
        {
            get
            {
                string value = PluginConfig.Instance?.DiagramStroke ?? string.Empty;
                return value == "fore" ? true : value == "back" ? (bool?)false : null;
            }
        }

        [UIValue("stroke-options")]
        public List<object> StrokeOptions => new List<object> { StrokeBoth, StrokeFore, StrokeBack };

        [UIValue("stroke")]
        public string Stroke
        {
            get => OnlyForehand == true ? StrokeFore : OnlyForehand == false ? StrokeBack : StrokeBoth;
            set { }
        }

        /// <summary>
        /// フォアだけ・バックだけを切り替える。2択以上なのでボタンにはせずプルダウンにする
        /// （ボタンの字からは今どちらを出しているかが読めない）。
        /// </summary>
        [UIAction("stroke-changed")]
        public void StrokeChanged(object value)
        {
            string stroke = value as string ?? StrokeBoth;
            if (PluginConfig.Instance != null)
                PluginConfig.Instance.DiagramStroke = stroke == StrokeFore ? "fore" : stroke == StrokeBack ? "back" : string.Empty;
            FillDiagram();
        }

        private const string SizeVisual = "Notes";
        private const string SizeHitbox = "Hitbox";

        /// <summary>ノーツを良い切りの当たり判定の大きさで描くか（GUI の「当たり判定の大きさ」）。</summary>
        private static bool Hitbox => PluginConfig.Instance?.DiagramHitbox ?? false;

        [UIValue("size-options")]
        public List<object> SizeOptions => new List<object> { SizeVisual, SizeHitbox };

        [UIValue("size")]
        public string Size
        {
            get => Hitbox ? SizeHitbox : SizeVisual;
            set { }
        }

        /// <summary>ノーツの大きさ（見た目か当たり判定か）。図と空間の振りの両方に効く。</summary>
        [UIAction("size-changed")]
        public void SizeChanged(object value)
        {
            if (PluginConfig.Instance != null) PluginConfig.Instance.DiagramHitbox = (value as string) == SizeHitbox;
            FillDiagram();
        }

        [UIAction("toggle-mean")]
        public void ToggleMean()
        {
            MeanOnly = !MeanOnly;
            Fill();
        }

        [UIAction("page-up")]
        public void PageUp()
        {
            if (page == 0) return;

            page--;
            Fill();
        }

        [UIAction("page-down")]
        public void PageDown()
        {
            if ((page + 1) * Rows >= scores.Count) return;

            page++;
            Fill();
        }

        /// <param name="row">画面の上から何行目か。通し番号ではない。</param>
        private void Select(int row)
        {
            int index = page * Rows + row;
            if (index < 0 || index >= scores.Count) return;

            selected = index;
            justPlayedDone = Gameplay.DrillReplayRecorder.LastSavedPath;
            Fill();
        }

        /// <summary>
        /// 別のリーダーボードに切り替わったら自分で隠れる。
        /// </summary>
        /// <remarks>
        /// LeaderboardCore は切り替えるとき、上の帯（panel）は GameObject ごと消すが、
        /// この本体には非活性の知らせ（<c>__Deactivate</c>）を送るだけで GameObject は消さない。
        /// そのままだと AccSaber や ScoreSaber に切り替えても、記録の行と軌道の図が上に残り続ける。
        /// 戻ってきたときは LeaderboardCore が <c>__Activate</c> で出し直すので、消しても困らない。
        /// </remarks>
        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
            // 曲を始めるときやほかのリーダーボードに切り替えたときに、空間に振りが残らないように
            room.Hide(this);
            RankSetting.Changed -= Rerank;
            RestoreCovered();
            gameObject.SetActive(false);
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            HideCovered();

            // 基準は Jump Drill 画面のプルダウンからも変わる。出ていない間に変わっていれば合わせる。
            // 変わっていなければ並べ直さない（▼ で見ていたページを、出し直すたびに戻さない）。
            // 外す前に出し直しが来ても二重にならないよう、いったん外してから付ける
            RankSetting.Changed -= Rerank;
            RankSetting.Changed += Rerank;
            if (rankedMetric != RankSetting.Current) Rerank();

            UpdateRoom();
        }

        /// <summary>DidDeactivate を通らずに消えたときも、基準の知らせを受けないようにする。</summary>
        protected override void OnDestroy()
        {
            room.Hide(this);
            RankSetting.Changed -= Rerank;
            base.OnDestroy();
        }

        /// <summary><see cref="ranked"/> を並べた基準。</summary>
        private RankMetric rankedMetric = RankSetting.Current;

        /// <summary>曲選択の枠に出している間だけ退けたものと、元の位置。離れるときに戻す。</summary>
        private readonly Dictionary<Transform, Vector3> covered = new Dictionary<Transform, Vector3>();

        /// <summary>退ける先。LeaderboardCore が本体の枠を退けるのと同じ場所。</summary>
        private static readonly Vector3 Away = new Vector3(-999f, -999f, -999f);

        private static bool siblingsLogged;

        /// <summary>
        /// 出している間は毎フレーム見直す。持ち主が出し直したり位置を戻したりすることがある
        /// （WhyIsThereNoLeaderboard は曲を選び直すたびに案内を出し直す）。見るのは兄弟の数個だけ。
        /// また、最初に出たときは親がまだ本体の枠に付いておらず、DidActivate の時点では見つからない。
        /// </summary>
        private void Update() => HideCovered();

        /// <summary>
        /// 曲選択の枠で、この表と重なって見えるものを隠す。
        /// </summary>
        /// <remarks>
        /// LeaderboardCore が隠すのは本体の枠の中身（<c>Container</c>）だけで、
        /// 本体の見出し（「ハイスコア」、<c>HeaderPanel</c>）と下の自己ベストの欄（<c>LevelStatsView</c>）は残る。さらに、リーダーボードの MOD が1つも無い環境では
        /// WhyIsThereNoLeaderboard が「リーダーボードの MOD を入れて」という案内を枠に重ねていて、
        /// LeaderboardCore で足した枠のことは見ない（1.44.1 で ScoreSaber も BeatLeader も無いとき）。
        /// BeatLeader も本体の枠の位置に自分の表（<c>LeaderboardView</c>）を出していて、これも残る。
        /// どれもこの表の上に重なって読めなくなるので、出している間だけ位置を退けて隠す。
        /// Jump Drill 画面の右に出しているときは本体の枠の中ではないので、何もしない。
        /// </remarks>
        private void HideCovered()
        {
            try
            {
                var parent = transform.parent;
                if (parent == null || parent.GetComponent<PlatformLeaderboardViewController>() == null) return;

                if (!siblingsLogged)
                {
                    siblingsLogged = true;
                    Plugin.LogDebug("leaderboard siblings: " + string.Join(", ", parent.Cast<Transform>().Select(c => c.name)));
                }

                foreach (Transform child in parent)
                {
                    if (child == transform || !Covers(child.name)) continue;
                    if (child.localPosition == Away) continue;

                    // 表示・非表示（SetActive）は持ち主に任せ、位置だけ退ける。
                    // SetActive で隠すと、持ち主が「この曲では出さない」と決めても、離れるときに出し直してしまう
                    covered[child] = child.localPosition;
                    child.localPosition = Away;
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn("could not hide the default leaderboard header: " + e.Message);
            }
        }

        private static bool Covers(string name)
        {
            return name == "HeaderPanel" || name == "LevelStatsView" || name == "WhyIsThereNoLeaderboardPanel"
                // BeatLeader の表。本体の枠の位置に自分で出していて、LeaderboardCore は退けない
                || name == "LeaderboardView";
        }

        private void RestoreCovered()
        {
            foreach (var entry in covered)
            {
                // 退けている間に持ち主が位置を変えていたら、そちらを残す
                if (entry.Key != null && entry.Key.localPosition == Away) entry.Key.localPosition = entry.Value;
            }
            covered.Clear();
        }

        /// <summary>行ごとの下線。選んでいる行だけ見せる。</summary>
        private readonly Image?[] underlines = new Image?[Rows];

        /// <summary>
        /// 選んでいる行の下に線を引く（他のリーダーボードと同じ見せ方）。
        /// </summary>
        /// <remarks>
        /// 行ごとに線を1本ずつ作っておき、選んでいる行だけ見せる。
        /// LocalLeaderboard や ScoreSaber は本体の表（<c>LeaderboardTableView</c>）を使っていて
        /// 下線も表の部品に入っているが、ここは 11 列を出すために表を自前で組んでいるので線も自前。
        /// 線はレイアウトに入れない（<c>ignoreLayout</c>）。入れると行の高さが変わって、
        /// 選び直すたびに下の図の位置が動く。BSML にはレイアウトの外に置く指定が無いので、
        /// 組み上がった後にコードで行の下端へ貼り付ける。
        /// </remarks>
        private void CreateUnderlines()
        {
            var rows = new[] { row1, row2, row3, row4, row5 };
            var white = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f));

            for (int i = 0; i < Rows; i++)
            {
                // 文字 → 幅を決める入れ物 → 行。線は行（🔁 まで含む幅）に貼る
                var row = rows[i]?.transform.parent?.parent;
                if (row == null || underlines[i] != null) continue;

                var go = new GameObject("JumpDrillUnderline", typeof(RectTransform));
                go.transform.SetParent(row, false);
                go.AddComponent<LayoutElement>().ignoreLayout = true;

                var image = go.AddComponent<ImageView>();
                image.sprite = white;

                // 曲面用の素材を当てる。Beat Saber の UI は画面を平らなまま置いて、曲面に曲げるのは
                // 素材（シェーダー）側でやっている。標準の素材のままだと曲がる前の平らな面に描かれ、
                // 文字より奥に・横に長く見えて行と合わない。BSML が画像に当てているものを図から借りる
                var curved = diagram != null ? diagram.material : replay1 != null ? replay1.material : null;
                if (curved != null) image.material = curved;
                image.raycastTarget = false;   // 下の行を押せなくならないように
                image.color = new Color(1f, 1f, 1f, 0.55f);

                // 行の下端に、横いっぱいの細い線
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(0f, 0.2f);
                rect.anchoredPosition = new Vector2(0f, -0.1f);

                underlines[i] = image;
            }
        }

        /// <summary>
        /// 図に重ねる数字。0・1 が正面図（左半分）の左手・右手、2・3 が横図（右半分）の左手・右手。
        /// </summary>
        private readonly TextMeshProUGUI?[] figureLabels = new TextMeshProUGUI?[4];

        private const string LeftColor = "#E06060";
        private const string RightColor = "#4A9CE8";

        /// <summary>
        /// 図の上の隅に手ごとの数字を置く。正面図には 再現・点数・角度、横図には PRE / POST。
        /// </summary>
        /// <remarks>
        /// JumpDrill の GUI の図と同じ置き方（左手は左上、右手は右上）。図は JumpDrill.Core が
        /// 画素で描いていて字を持たないので、字は図の上に重ねる。
        /// PRE / POST をフォアとバックに分けて出すのは、往路と復路が絵の中で重なり、
        /// まとめた1つの数字ではどちらの伸びが足りないのかが分からないため。
        /// </remarks>
        private void CreateFigureLabels()
        {
            if (diagram == null || figureLabels[0] != null) return;

            for (int i = 0; i < figureLabels.Length; i++)
            {
                bool leftHand = i % 2 == 0;
                float paneLeft = i < 2 ? 0f : 0.5f;

                // 1.44 の BSML・TextMeshPro では古い扱いだが、代わりの CreateCurvedUIText と textWrappingMode は
                // 1.39 / 1.40 には無い。3つの版を同じコードでビルドするため、こちらを使い続ける
#pragma warning disable CS0618
                var label = BeatSaberUI.CreateText((RectTransform)diagram.transform, string.Empty, Vector2.zero);
                label.fontSize = 2.2f;
                label.enableWordWrapping = false;
#pragma warning restore CS0618
                label.overflowMode = TextOverflowModes.Overflow;
                label.raycastTarget = false;
                label.alignment = leftHand ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight;
                label.lineSpacing = -10f;

                // 左手はその図の左上、右手はその図の右上
                var rect = label.rectTransform;
                var corner = new Vector2(leftHand ? paneLeft : paneLeft + 0.5f, 1f);
                rect.anchorMin = corner;
                rect.anchorMax = corner;
                rect.pivot = new Vector2(leftHand ? 0f : 1f, 1f);
                rect.sizeDelta = new Vector2(20f, 10f);
                rect.anchoredPosition = leftHand ? new Vector2(1.5f, -0.5f) : new Vector2(-1f, -0.5f);

                figureLabels[i] = label;
            }
        }

        private void FillFigureLabels(DrillScore? score)
        {
            FillHandLabel(figureLabels[0], LeftColor, score?.LeftHand);
            FillHandLabel(figureLabels[1], RightColor, score?.RightHand);
            FillAngleLabel(figureLabels[2], LeftColor, score?.LeftFore, score?.LeftBack);
            FillAngleLabel(figureLabels[3], RightColor, score?.RightFore, score?.RightBack);
        }

        /// <summary>正面図の1手ぶん（GUI の 再現 /100・点数 /115・角度 /100）。振っていない手は出さない。</summary>
        private static void FillHandLabel(TextMeshProUGUI? label, string color, HandFigures? hand)
        {
            if (label == null) return;

            if (hand == null)
            {
                label.text = string.Empty;
                return;
            }

            var ci = CultureInfo.InvariantCulture;
            label.text = "<color=" + color + ">"
                + FigureLine("Repro", ScoreFormat.Floor(hand.Reproducibility, 1), "/100") + "\n"
                + FigureLine("Cut", ScoreFormat.Floor(hand.AverageCut, 1), "/115") + "\n"
                + FigureLine("Angle", ScoreFormat.Floor(hand.AnglePercent, 1), "/100");
        }

        private static string FigureLine(string caption, string value, string max)
        {
            return "<alpha=#99>" + caption + "  <alpha=#FF>" + value + " <alpha=#99>" + max;
        }

        /// <summary>横図の1手ぶん。フォアもバックも無い手（振っていない手）は出さない。</summary>
        private static void FillAngleLabel(TextMeshProUGUI? label, string color, SwingAngles? fore, SwingAngles? back)
        {
            if (label == null) return;

            if (fore == null && back == null)
            {
                label.text = string.Empty;
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("<color=").Append(color).Append("><alpha=#99>PRE / POST %");
            if (fore != null) sb.Append('\n').Append(AngleLine("Fore", fore));
            if (back != null) sb.Append('\n').Append(AngleLine("Back", back));
            label.text = sb.ToString();
        }

        private static string AngleLine(string caption, SwingAngles side)
        {
            var ci = CultureInfo.InvariantCulture;
            return "<alpha=#99>" + caption + "  <alpha=#FF>"
                + ScoreFormat.Floor(side.PreSwing, 0) + " / " + ScoreFormat.Floor(side.PostSwing, 0);
        }

        private void FillUnderlines()
        {
            for (int i = 0; i < Rows; i++)
            {
                var line = underlines[i];
                if (line != null) line.gameObject.SetActive(page * Rows + i == selected && selected < scores.Count);
            }
        }

        /// <summary>
        /// プルダウンの本体を枠（pref-width）いっぱいに合わせる。BSML のプルダウンはゲームの設定画面の部品をそのまま使っていて、
        /// 本体が右端に寄せた決まった幅になっている。枠を詰めても本体の幅は変わらず、左へはみ出して隣と重なった。
        /// </summary>
        private static void FitToFrame(DropDownListSetting? setting)
        {
            if (setting == null) return;
            var rect = setting.transform as RectTransform;
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        [UIAction("#post-parse")]
        public void PostParse()
        {
            CreateUnderlines();
            CreateFigureLabels();
            foreach (var setting in new[] { strokeDropdown, roomDropdown, sizeDropdown }) FitToFrame(setting);

            // 枠に出ているのがこのビューかどうかは画面からは分かりにくい。
            // 出るはずのときに出ていない、を切り分けられるようにしておく
            Plugin.LogDebug($"leaderboard view parsed ({scores.Count} record(s))");
            Fill();
        }

        /// <summary>
        /// 列の位置（BSML の長さの単位）。見出しと行で同じものを使う。
        /// </summary>
        /// <remarks>
        /// 空白で桁を揃えると、フォントが等幅でないので数字の幅で列がずれる。
        /// 列が 11 本（REPRO / ACC / MISS それぞれの全体・左・右）あると読めなくなるので、
        /// TextMeshPro の <c>&lt;pos&gt;</c> で位置を固定する。
        ///
        /// <b>数字の列は右寄せ</b>（他のリーダーボードと同じ）。<c>&lt;pos&gt;</c> は左端しか
        /// 指定できないので、値ごとに文字の幅を測って「右端 − 幅」の位置に置く。
        /// 見出しも同じく右寄せにして、数字の真上に来るようにする。
        /// 順位は右寄せ（10 位で桁がずれないように）、日付だけは左寄せ。
        ///
        /// 最後の列（MISS の R）は、行の右端にある 🔁 の手前で終わるところまで伸ばしてある。
        /// 🔁 は常に行の右端（幅 86 − 4 = 82）に出る。文字の入れ物に幅を書いても効かず、
        /// 入れ物が行の残り幅いっぱいに伸びるため。
        /// </remarks>
        private static readonly Column[] Layout =
        {
            new Column(2, true),        // #（右寄せ）
            new Column(4, false),       // DATE（左寄せ）
            new Column(27, true), new Column(33.5, true), new Column(40, true),     // REPRO / L / R
            new Column(49, true), new Column(55.5, true), new Column(62, true),     // ACC / L / R
            new Column(69.5, true), new Column(74.5, true), new Column(79, true),   // MISS / L / R
        };

        private struct Column
        {
            /// <summary>右寄せなら右端、左寄せなら左端。</summary>
            public readonly double At;
            public readonly bool Right;

            public Column(double at, bool right)
            {
                At = at;
                Right = right;
            }
        }

        /// <summary>1行ぶんの文字列を組む。右寄せの列は、その文字で測った幅で位置を決める。</summary>
        /// <param name="measure">幅を測る文字の部品。書き込む先と同じもの（大きさを揃えるため）。</param>
        private static string Columns(TMP_Text measure, params string[] values)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < values.Length && i < Layout.Length; i++)
            {
                var column = Layout[i];
                double x = column.At;
                if (column.Right) x -= measure.GetPreferredValues(values[i]).x;

                sb.Append("<pos=").Append(Math.Max(0, x).ToString("0.##", ci)).Append('>').Append(values[i]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 記録として数えないプレイの印。日付の後ろに小さく出す。
        /// 薄い色だけでは、自動プレイの記録かどうかが一目で分からない。
        /// </summary>
        private static string Mark(DrillScore score)
        {
            if (score.Autoplay) return " <size=70%><color=#FFA040>AUTO</color></size>";
            if (score.SpeedChanged) return " <size=70%><color=#FFA040>SPEED</color></size>";
            return string.Empty;
        }

        // 見出しの L / R はノーツの色。どちらの手の数字か、色で読めるように
        private const string LeftLabel = "<color=#E06060>L</color>";
        private const string RightLabel = "<color=#4A9CE8>R</color>";

        /// <summary>手ごとの数字（割合）。その手の振りが無ければ横棒。</summary>
        /// <remarks>
        /// 小数は1桁。全体の精度は GUI と同じ2桁のままにしてあるが、列が 11 本あり、
        /// 手ごとまで2桁にすると枠に収まらない。
        /// </remarks>
        private static string Hand(double? percent)
        {
            return percent.HasValue
                ? ScoreFormat.Floor(percent.Value, 1)
                : "-";
        }

        /// <summary>手ごとの数字（個数）。その手の振りが無ければ横棒。</summary>
        private static string Hand(int? count)
        {
            return count.HasValue ? count.Value.ToString(CultureInfo.InvariantCulture) : "-";
        }

        private void Fill()
        {
            var ci = CultureInfo.InvariantCulture;

            // 見出しは記録が無くても出す。何も出ないと「表示できていない」のか
            // 「記録が無い」のか、画面からは切り分けられない
            if (header != null)
                header.text = Columns(header, "#", "DATE",
                    "REPRO", LeftLabel, RightLabel,
                    "ACC", LeftLabel, RightLabel,
                    "MISS", LeftLabel, RightLabel);

            if (drillSetButton != null) drillSetButton.gameObject.SetActive(OpenDrillSet != null);
            if (deleteButton != null) deleteButton.interactable = selected < scores.Count;

            var rows = new[] { row1, row2, row3, row4, row5 };
            var replays = new[] { replay1, replay2, replay3, replay4, replay5 };

            // 🔁 は記録のある行だけ。BeatLeader が無ければ押しても何もできないので出さない
            bool canReplay = BeatLeaderReplayer.IsAvailable;
            for (int i = 0; i < Rows; i++)
            {
                var icon = replays[i];
                if (icon == null) continue;

                bool show = canReplay && page * Rows + i < scores.Count;
                icon.gameObject.SetActive(show);
                if (show && icon.sprite != ReplayIcon.Sprite) icon.sprite = ReplayIcon.Sprite;
            }
            for (int i = 0; i < Rows; i++)
            {
                var text = rows[i];
                if (text == null) continue;

                int index = page * Rows + i;
                if (index >= scores.Count)
                {
                    text.text = string.Empty;
                    continue;
                }

                var score = scores[index];
                text.text = Columns(text,
                    score.CountsAsRecord ? RankOf(score).ToString(ci) : "-",
                    score.PlayedAt.ToString("MM/dd HH:mm", ci) + Mark(score),
                    ScoreFormat.Floor(score.ReproducibilityPercent, 1),
                    Hand(score.LeftReproducibilityPercent),
                    Hand(score.RightReproducibilityPercent),
                    ScoreFormat.Floor(score.Accuracy, 2),
                    Hand(score.LeftAccuracy),
                    Hand(score.RightAccuracy),
                    score.MissCount.ToString(ci),
                    Hand(score.LeftMissCount),
                    Hand(score.RightMissCount));

                // 記録として数えないもの（自動プレイ・速度を変えたもの）は同じ濃さで並べない。
                // 自動プレイはヘッドセットが動かないぶん再現性がほぼ満点になり、
                // 人の記録と同じ濃さだと上位の行として読めてしまう（JumpDrill の GUI のスコアボードと同じ扱い）。
                // 選んでいる行は色を変える。どの記録の図が出ているかが分からないと選べない
                Color color = index == selected
                    ? new Color(1f, 0.85f, 0.3f)
                    : !score.CountsAsRecord
                        ? new Color(0.6f, 0.6f, 0.65f)
                        : Color.white;

                // ClickableText は指を離したときに DefaultColor へ戻すので、
                // color だけ変えても hover から抜けた瞬間に元へ戻ってしまう
                text.DefaultColor = color;
                text.color = color;

                // 指を乗せた色は選択の色と変える。同じ系統にすると、
                // 触っているだけの行が選ばれているように見える
                text.HighlightColor = new Color(0.55f, 0.85f, 1f);
            }

            FillUnderlines();

            if (note != null)
            {
                note.text = scores.Count == 0
                    ? "No records yet. Play this drill once."
                    : string.Format(ci, "{0} record(s)  {1}-{2}",
                        scores.Count,
                        page * Rows + 1,
                        Math.Min((page + 1) * Rows, scores.Count));
            }

            FillDiagram();
        }

        /// <summary>譜面の NJS（GUI の譜面の情報と同じ）。図を描いたときに読んだ値（遊んだ難易度のもの）。分からなければ空。</summary>
        private string NoteJumpSpeedText(DrillScore score)
        {
            float? njs = diagrams?.NoteJumpSpeedOf(score.ReplayPath);
            return njs.HasValue ? string.Format(CultureInfo.InvariantCulture, "  NJS {0:0.##}", njs.Value) : string.Empty;
        }

        /// <summary>
        /// 選んでいる記録の軌道を出す。
        /// </summary>
        /// <remarks>
        /// 出すのは<b>横から見た図</b>。正面図では振りかぶりも振り抜きも
        /// 画面の手前・奥に潰れて見えないので、束の太さは読めても
        /// どれだけ引けて振り抜けているかが分からない（JumpDrill.Core の <c>SwingDiagram</c>）。
        /// </remarks>
        private void FillDiagram()
        {
            if (diagram == null) return;

            var score = selected < scores.Count ? scores[selected] : null;
            var sprite = score == null || diagrams == null ? null : diagrams.For(score.ReplayPath, MeanOnly, OnlyForehand, Hitbox);

            diagram.gameObject.SetActive(sprite != null);
            if (sprite != null) diagram.sprite = sprite;
            FillFigureLabels(sprite != null ? score : null);
            UpdateRoom();

            // ボタンには押すと切り替わる先を出す（Drill / Other と同じ）
            if (viewButton != null)
            {
                var label = viewButton.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = MeanOnly ? "All swings" : "Mean only";
            }

            if (diagramNote != null)
            {
                diagramNote.text = sprite == null
                    ? string.Empty
                    : ((score!.CountsAsRecord
                        ? string.Format(CultureInfo.InvariantCulture, "#{0}", RankOf(score!))
                        : string.Empty) + NoteJumpSpeedText(score!)).Trim();
            }

        }
    }
}
