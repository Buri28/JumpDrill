using System;
using System.Collections.Generic;
using BeatSaberMarkupLanguage.Attributes;
using JumpDrillMod.Services;
using TMPro;
using UnityEngine.UI;

namespace JumpDrillMod.UI
{
    /// <summary>
    /// 曲選択画面の Mods タブの中身。ドリルの指定項目を並べる。
    /// </summary>
    /// <remarks>
    /// 表示は英語で統一する。JumpDrill.Coreから来る例外の本文は日本語なので画面には出さず、
    /// <see cref="DrillGenerationService"/> が短い英語に畳んでログへ回す。
    ///
    /// ルートを PreferredSize の vertical にしない
    /// （Mods タブは全MODで1枚のパネルを共有していて、他MODのタブまで崩れる）。
    /// </remarks>
    internal class JumpDrillTabController
    {
        internal const string TabName = "JumpDrill";
        internal const string Resource = "JumpDrillMod.Resources.JumpDrillTab.bsml";

        private readonly DrillGenerationService generator;
        private readonly SongRefreshService refresher;
        private readonly DrillSetGenerationService drillSet;
        private readonly Medals.DrillSetScreen drillSetScreen;
        private readonly SequenceEditor editor = new SequenceEditor();

        /// <summary>
        /// 設定の記法をグリッドに読み戻せなかった（手で書いた複数の遷移など）。
        /// グリッドを触るまでは設定の記法をそのまま使い、書き換えない。
        /// </summary>
        private bool keepConfigSequence;

        internal JumpDrillTabController(DrillGenerationService generator, SongRefreshService refresher,
            DrillSetGenerationService drillSet, Medals.DrillSetScreen drillSetScreen)
        {
            this.generator = generator;
            this.refresher = refresher;
            this.drillSet = drillSet;
            this.drillSetScreen = drillSetScreen;

            // 設定ファイルの記法からグリッドの状態を復元する。
            // 読めなければ編集前の既定（右手・空）のままにして、指定は残しておく
            keepConfigSequence = !editor.Load(Config?.Sequence) && !string.IsNullOrWhiteSpace(Config?.Sequence);
        }

        private static PluginConfig? Config => PluginConfig.Instance;

        [UIComponent("sequence-text")]
        private TextMeshProUGUI? sequenceText = null;

        [UIComponent("status")]
        private TextMeshProUGUI? status = null;

        // マスのボタン。置いた点を GUI と同じく手の色と順番で見せるために、文字を書き換える
        [UIComponent("cell-button-1")] private Button? cellButton1 = null;
        [UIComponent("cell-button-2")] private Button? cellButton2 = null;
        [UIComponent("cell-button-3")] private Button? cellButton3 = null;
        [UIComponent("cell-button-4")] private Button? cellButton4 = null;
        [UIComponent("cell-button-5")] private Button? cellButton5 = null;
        [UIComponent("cell-button-6")] private Button? cellButton6 = null;
        [UIComponent("cell-button-7")] private Button? cellButton7 = null;
        [UIComponent("cell-button-8")] private Button? cellButton8 = null;
        [UIComponent("cell-button-9")] private Button? cellButton9 = null;
        [UIComponent("cell-button-10")] private Button? cellButton10 = null;
        [UIComponent("cell-button-11")] private Button? cellButton11 = null;
        [UIComponent("cell-button-12")] private Button? cellButton12 = null;

        // 点ごとの「矢印をまっすぐ」。置いた点のぶんだけ出し、文字はその点の記法（4 / 4s）
        [UIComponent("straight-1")] private Button? straight1 = null;
        [UIComponent("straight-2")] private Button? straight2 = null;

        /// <summary>右手・左手の色。GUI のグリッドと同じ。</summary>
        private const string RightColor = "#2E86D8";
        private const string LeftColor = "#D03A3A";

        [UIAction("#post-parse")]
        public void PostParse()
        {
            RefreshSequenceText();
            SetStatus(string.Empty);
        }

        // ───────── 遷移（グリッド） ─────────
        //
        // BSML の on-click は引数を取れないので、マスごとにアクションを置く。
        // 12個ぶん並ぶが、処理は Tap に集約できる

        [UIAction("cell-1")] public void Cell1() => Tap(0);
        [UIAction("cell-2")] public void Cell2() => Tap(1);
        [UIAction("cell-3")] public void Cell3() => Tap(2);
        [UIAction("cell-4")] public void Cell4() => Tap(3);
        [UIAction("cell-5")] public void Cell5() => Tap(4);
        [UIAction("cell-6")] public void Cell6() => Tap(5);
        [UIAction("cell-7")] public void Cell7() => Tap(6);
        [UIAction("cell-8")] public void Cell8() => Tap(7);
        [UIAction("cell-9")] public void Cell9() => Tap(8);
        [UIAction("cell-10")] public void Cell10() => Tap(9);
        [UIAction("cell-11")] public void Cell11() => Tap(10);
        [UIAction("cell-12")] public void Cell12() => Tap(11);

        private void Tap(int index)
        {
            keepConfigSequence = false;
            editor.Tap(SequenceEditor.Tokens[index]);
            StoreSequence();
        }

        [UIAction("straight-1")] public void Straight1() => ToggleStraighten(0);
        [UIAction("straight-2")] public void Straight2() => ToggleStraighten(1);

        private void ToggleStraighten(int index)
        {
            keepConfigSequence = false;
            editor.ToggleStraighten(index);
            StoreSequence();
        }

        [UIAction("sequence-clear")]
        public void SequenceClear()
        {
            keepConfigSequence = false;
            editor.Clear();
            StoreSequence();
        }

        /// <summary>グリッドの状態を記法にして設定へ書き戻し、表示を更新する。</summary>
        private void StoreSequence()
        {
            // 書き戻すのは2点そろったときだけ。1点以下の記法（"R" や "R4"）は読み戻せないので、
            // 置きかけでゲームを閉じると次回グリッドに戻らなくなる。そろうまでは前の遷移を残す
            if (Config != null && !keepConfigSequence && editor.Count == SequenceEditor.MaxSteps)
                Config.Sequence = editor.Notation;
            RefreshSequenceText();
            SetStatus(string.Empty);
        }

        /// <summary>記法（GUI の「遷移（記法）」の欄と同じ）と、グリッドの見た目を今の状態に合わせる。</summary>
        private void RefreshSequenceText()
        {
            if (sequenceText != null)
            {
                if (keepConfigSequence)
                    sequenceText.text = Config?.Sequence ?? string.Empty;
                else if (editor.Count < SequenceEditor.MaxSteps)
                    sequenceText.text = editor.Notation + "  <color=#808080><size=70%>tap two cells</size></color>";
                else
                    sequenceText.text = editor.Notation;
            }

            RefreshGrid();
        }

        private void RefreshGrid()
        {
            var buttons = new[]
            {
                cellButton1, cellButton2, cellButton3, cellButton4,
                cellButton5, cellButton6, cellButton7, cellButton8,
                cellButton9, cellButton10, cellButton11, cellButton12,
            };

            string color = editor.Hand == JumpDrill.Model.Hand.Left ? LeftColor : RightColor;
            var steps = keepConfigSequence ? new List<JumpDrill.Model.SequenceStep>() : new List<JumpDrill.Model.SequenceStep>(editor.Steps);

            for (int i = 0; i < buttons.Length; i++)
            {
                char token = SequenceEditor.Tokens[i];
                int order = steps.FindIndex(s => s.Position.ToToken() == token);

                // 置いた点は手の色で、何番目かを添える（GUI の丸の中の数字）
                SetLabel(buttons[i], order < 0
                    ? token.ToString()
                    : "<color=" + color + ">" + token + "<size=70%> " + (order + 1) + "</size></color>");
            }

            var toggles = new[] { straight1, straight2 };
            for (int i = 0; i < toggles.Length; i++)
            {
                var toggle = toggles[i];
                if (toggle == null) continue;

                bool shown = i < steps.Count;
                toggle.gameObject.SetActive(shown);
                if (shown) SetLabel(toggle, steps[i].ToString());
            }
        }

        private static void SetLabel(Button? button, string text)
        {
            if (button == null) return;
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = text;
        }

        // ───────── 手 ─────────

        [UIValue("hand-options")]
        public List<object> HandOptions { get; } = new List<object> { "Right", "Left" };

        /// <summary>遷移を組む手。GUI の「編集する手」。</summary>
        [UIValue("hand")]
        public string Hand
        {
            get => editor.Hand == JumpDrill.Model.Hand.Left ? "Left" : "Right";
            set
            {
                editor.Hand = value == "Left" ? JumpDrill.Model.Hand.Left : JumpDrill.Model.Hand.Right;
                StoreSequence();
            }
        }

        /// <summary>
        /// 左右反転して反対の手にも同じ形を組む（CLI の --mirror）。GUI のチェックと同じ。
        /// グリッドで組むのは片手ぶんで済む。
        /// </summary>
        [UIValue("mirror")]
        public bool Mirror
        {
            get => Config?.Mirror ?? false;
            set { if (Config != null) Config.Mirror = value; }
        }

        [UIValue("pattern-options")]
        public List<object> PatternOptions { get; } = new List<object> { "Split", "Alternate", "Sync" };

        /// <summary>両手のときの噛み合わせ。反転しない（片手だけ）なら効かない。</summary>
        [UIValue("pattern")]
        public string Pattern
        {
            get => Config?.HandPattern ?? "Split";
            set { if (Config != null) Config.HandPattern = value; }
        }

        // ───────── 切る方向 ─────────

        [UIValue("direction-options")]
        public List<object> DirectionOptions { get; } = new List<object> { "Axis", "Perpendicular", "Dot" };

        /// <summary>
        /// 軸に沿う / 垂直 / ドット。記法で1点ずつ明示する Explicit は
        /// グリッドからは組めないので、ここには出さない。
        /// </summary>
        [UIValue("direction")]
        public string Direction
        {
            get => Config?.Direction ?? "Axis";
            set { if (Config != null) Config.Direction = value; }
        }

        // ───────── 時間軸 ─────────

        /// <summary>
        /// ノーツ間隔の BPM。ビーセイで速さを言う単位がこれなので、ms ではなくこちらを出す。
        /// 1拍に2ノーツ（1/2）で数える。一番微調整したいところなので刻みを細かくしてある。
        /// </summary>
        [UIValue("interval-bpm")]
        public float IntervalBpm
        {
            get => Config?.IntervalBpm ?? 150f;
            set { if (Config != null) Config.IntervalBpm = value; }
        }

        [UIValue("njs")]
        public float Njs
        {
            get => Config?.Njs ?? 16f;
            set { if (Config != null) Config.Njs = value; }
        }

        [UIValue("jump-distance")]
        public float JumpDistance
        {
            get => Config?.JumpDistance ?? 18f;
            set { if (Config != null) Config.JumpDistance = value; }
        }

        [UIValue("duration")]
        public float Duration
        {
            get => Config?.DurationSeconds ?? 30f;
            set { if (Config != null) Config.DurationSeconds = value; }
        }

        [UIValue("sets")]
        public int Sets
        {
            get => Config?.Sets ?? 1;
            set { if (Config != null) Config.Sets = value; }
        }

        // ───────── クリック ─────────

        [UIValue("click-options")]
        public List<object> ClickOptions { get; } = new List<object> { "Down", "Up", "All", "Every4", "None" };

        [UIValue("click")]
        public string Click
        {
            get => Config?.Click ?? "Down";
            set { if (Config != null) Config.Click = value; }
        }

        [UIValue("count-in")]
        public int CountIn
        {
            get => Config?.CountInClicks ?? 8;
            set { if (Config != null) Config.CountInClicks = value; }
        }

        // ───────── 生成 ─────────

        /// <summary>
        /// いまの指定で1本作り、曲一覧に反映させる。
        /// </summary>
        /// <remarks>
        /// 生成は数百ミリ秒かかる（音源のエンコードが要るため）が、ここでは同期のまま呼ぶ。
        /// 譜面を書いている最中に画面を触らせても得がなく、別スレッドにすると
        /// SongCore の呼び出しをメインスレッドへ戻す手間が増えるだけなので。
        /// </remarks>
        [UIAction("generate")]
        public void Generate()
        {
            if (Config == null)
            {
                SetStatus("Config not loaded");
                return;
            }

            if (!keepConfigSequence)
            {
                if (editor.Count < SequenceEditor.MaxSteps)
                {
                    SetStatus("Tap two cells");
                    return;
                }
            }

            // 一括生成と同じフォルダへ書くので、終わるまで待たせる
            if (drillSet.IsRunning)
            {
                SetStatus("Wait until the drills are generated");
                return;
            }

            SetStatus("Generating...");

            var result = generator.Generate(Config);
            if (!result.Ok)
            {
                SetStatus(result.Message);
                return;
            }

            refresher.Refresh(ok => SetStatus(ok
                ? "Added: " + result.SongName
                : "Written, but the song list did not reload. Restart the game."));
        }

        // ───────── ドリルセット ─────────

        /// <summary>
        /// Jump Drill 画面を開く。表は画面をまるごと1枚使うので、ここには収まらない。
        /// ドリルセットの一括生成もその画面の中にある。
        /// </summary>
        [UIAction("open-medals")]
        public void OpenMedals()
        {
            string? error = drillSetScreen.Open();
            if (error != null) SetStatus(error);
        }

        private void SetStatus(string text)
        {
            if (status != null) status.text = text;
            if (text.Length > 0) Plugin.LogDebug("status: " + text);
        }
    }
}
