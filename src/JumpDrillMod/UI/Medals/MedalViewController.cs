using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using IPA.Utilities.Async;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Replays;
using JumpDrillMod.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JumpDrillMod.UI.Medals
{
    /// <summary>
    /// ドリルを選んで叩く画面。<b>Drill</b> はドリルセット（14方向 × 8段）のメダルと Lv、
    /// <b>Other</b> はドリルセット以外のドリル譜面の一覧。
    /// </summary>
    /// <remarks>
    /// Drill は GUI の「メダル」画面と同じもの。メダルと Lv の決め方は JumpDrill.Core の <c>DrillSetMedals</c>
    /// （再現精度 % で 🥉70 / 🥈80 / 🥇90、Lv はポイントの合計）で、ここには書かない。
    /// 行頭の絵も GUI と同じ（JumpDrill.Core の <c>SequenceIcon</c>）。
    ///
    /// Other はグリッドや CLI で作ったドリル。どの段にも属さないので Lv には数えないが、
    /// ベストと、取れていればメダルの色は出す。
    ///
    /// マスや行を押すとそのドリルを選び、下の <b>Play</b> で曲選択の練習画面まで進める
    /// （<see cref="LevelLauncher"/>）。
    ///
    /// マークアップは手で書かずにコードで組む（Drill だけで 112 マス）。押したものは
    /// BSML のタグで拾う。<c>on-click</c> は引数を取れず、マスごとにアクションを並べることになるため。
    /// </remarks>
    internal class MedalViewController : BSMLViewController
    {
        private ReplayScoreReader? reader;
        private LevelLauncher? launcher;
        private IconService? icons;
        private Action<string>? onPlay;
        private DrillSetGenerationService? drillSet;

        /// <summary>ドリルを選んだとき。右の枠のリーダーボードを切り替えるのに使う。</summary>
        private Action<string?>? onSelect;

        /// <summary>方向ごとの成績。読み込み前は全部空で出す（どこを埋めればいいかが見える）。</summary>
        private List<MedalRow> rows = DrillSetMedals.Evaluate(new Dictionary<string, double>());

        /// <summary>ID ごとのベスト再現精度 %。Other の数字もここから引く。</summary>
        private Dictionary<string, double> bestById = new Dictionary<string, double>();

        /// <summary>Other に出すドリル。名前順。</summary>
        private List<OtherDrill> others = new List<OtherDrill>();

        private sealed class OtherDrill
        {
            public string Id = string.Empty;

            /// <summary>画面に出す名前。ID と頭の "Drill " を落としたもの。</summary>
            public string Name = string.Empty;

            public List<HandSequence> Sequences = new List<HandSequence>();
        }

        /// <summary>選んでいるドリルの ID。Drill と Other で共通。</summary>
        private string? selectedId;

        /// <summary>Other を出しているか。</summary>
        private bool showOther;

        /// <summary>Other の何ページ目か。</summary>
        private int otherPage;

        private bool loading;

        /// <summary>読んでいる最中に読み直しを頼まれた。終わったらもう1回読む。</summary>
        private bool rescanAgain;

        private readonly Dictionary<string, ClickableText> cells = new Dictionary<string, ClickableText>();
        private readonly List<TextMeshProUGUI?> rowLabels = new List<TextMeshProUGUI?>();
        private readonly List<ImageView?> rowIcons = new List<ImageView?>();
        private readonly List<ClickableText?> otherTexts = new List<ClickableText?>();
        private readonly List<ImageView?> otherIcons = new List<ImageView?>();

        [UIParams] private readonly BSMLParserParams? parserParams = null;

        [UIComponent("total")] private TextMeshProUGUI? total = null;
        [UIComponent("count")] private TextMeshProUGUI? count = null;
        [UIComponent("detail")] private TextMeshProUGUI? detail = null;
        [UIComponent("status")] private TextMeshProUGUI? status = null;
        [UIComponent("other-page")] private TextMeshProUGUI? otherPageText = null;
        [UIComponent("view-button")] private Button? viewButton = null;

        [UIObject("set-view")] private GameObject? setView = null;
        [UIObject("other-view")] private GameObject? otherView = null;

        // メダルの色。GUI と同じ系統にしてある
        private static readonly Color Gold = new Color(0.91f, 0.77f, 0.28f);
        private static readonly Color Silver = new Color(0.78f, 0.80f, 0.85f);
        private static readonly Color Bronze = new Color(0.79f, 0.51f, 0.27f);
        private static readonly Color Played = Color.white;
        private static readonly Color Empty = new Color(0.35f, 0.38f, 0.44f);
        private static readonly Color Picked = new Color(0.55f, 0.85f, 1f);

        private static int Stages => DrillSet.StageBpm.Count + 1;

        /// <summary>
        /// 見出しの列の幅。絵と方向と Lv がちょうど収まる幅。
        /// 余らせると、方向名と段1の間が空いて、どの行の数字か目で追いにくくなる。
        /// </summary>
        private const int LabelWidth = 29;

        /// <summary>
        /// 1マスの幅。「●85.7」がちょうど収まるところまで詰めてある。
        /// 広げると画面の端まで伸びて、左の方向名から右の段まで首を振らないと読めない。
        /// </summary>
        private const int CellWidth = 10;

        /// <summary>表の幅。見出しの列 + 8マス。上下の行もこれに揃える。</summary>
        private static int TableWidth => LabelWidth + CellWidth * Stages;

        /// <summary>Other の1ページの行数。Drill の行数と同じにして、切り替えても高さが変わらないようにする。</summary>
        private static int OtherRows => DrillSet.Directions.Count;

        /// <summary>外から要るものを渡す。ビューは Zenject を通らない。</summary>
        internal void Setup(ReplayScoreReader reader, LevelLauncher launcher, IconService icons,
            DrillSetGenerationService drillSet, Action<string> onPlay, Action<string?> onSelect)
        {
            this.reader = reader;
            this.launcher = launcher;
            this.icons = icons;
            this.drillSet = drillSet;
            this.onPlay = onPlay;
            this.onSelect = onSelect;
        }

        public override string Content => BuildMarkup();

        // ───────── マークアップ ─────────

        /// <summary>
        /// 画面のマークアップ。上に総合と切り替え、真ん中に Drill か Other、下に選んだものの内訳と Play。
        /// </summary>
        /// <remarks>
        /// <b>マスは幅を固定した入れ物で1つずつ包む。</b> 文字に直接 pref-width を書いても効かず、
        /// 幅が中身の文字数で決まる。「●85.7」と「·」で幅が違うので、行ごとに並びがずれ、
        /// 見出しの段の番号も数字の上に来なくなる。入れ物（レイアウトの箱）の pref-width は効くので、
        /// その中で文字を中央寄せにする。行の側も子を広げない（child-expand-width='false'）。
        ///
        /// Drill と Other は同じ高さの入れ物に入れて、出す方だけ有効にする。
        /// </remarks>
        private static string BuildMarkup()
        {
            var sb = new StringBuilder();
            string width = TableWidth.ToString(CultureInfo.InvariantCulture);

            // 幅は表の幅に合わせる。広く取ると上の総合と下の内訳が端へ散る
            sb.Append("<bg><vertical pref-width='").Append(width)
              .Append("' spacing='0.3' pad='2' child-expand-height='false' child-align='UpperCenter'>");

            // 総合と切り替え
            sb.Append("<horizontal pref-height='6' spacing='3'>");
            sb.Append("<text id='total' text='' font-size='4.5' align='Left'/>");
            sb.Append("<text id='count' text='' font-size='3.2' align='Left' color='#8899AA'/>");
            sb.Append("<button id='view-button' text='Other' on-click='toggle-view' pref-width='18' pref-height='6'");
            sb.Append(" hover-hint='Switch between the 112 drills (with medals) and the other drills you made.'/>");
            sb.Append("<button text='Rescan' on-click='rescan' pref-width='20' pref-height='6'");
            sb.Append(" hover-hint='Read the replays again.'/>");
            sb.Append("</horizontal>");

            // ───── Drill ─────
            sb.Append("<vertical id='set-view' spacing='0.3' child-expand-height='false'>");

            OpenRow(sb, 4);
            Box(sb, LabelWidth, "MiddleLeft", "<text text='' font-size='2.6'/>");
            for (int c = 0; c < Stages; c++)
            {
                string caption = c == Stages - 1 ? "8 (30s)" : (c + 1).ToString(CultureInfo.InvariantCulture);
                Box(sb, CellWidth, "MiddleCenter",
                    "<text text='" + caption + "' font-size='2.6' align='Center' color='#8899AA'/>");
            }
            sb.Append("</horizontal>");

            for (int r = 0; r < DrillSet.Directions.Count; r++)
            {
                OpenRow(sb, 4.2);

                // 絵は左。方向名だけでは配置が思い浮かばないので、先に形を見せる
                Box(sb, LabelWidth, "MiddleLeft",
                    Icon("icon-" + r) +
                    "<text tags='row-" + r + "' text='' font-size='2.8' align='Left'/>");

                for (int c = 0; c < Stages; c++)
                {
                    Box(sb, CellWidth, "MiddleCenter",
                        "<clickable-text tags='mc-" + r + "-" + c + "' text='' font-size='3' align='Center'/>",
                        fill: true);
                }

                sb.Append("</horizontal>");
            }

            sb.Append("</vertical>");

            // ───── Other ─────
            sb.Append("<vertical id='other-view' active='false' spacing='0.3' child-expand-height='false'>");

            OpenRow(sb, 4);
            sb.Append("<text id='other-page' text='' font-size='2.6' align='Left' color='#8899AA' pref-width='")
              .Append(TableWidth - 20).Append("'/>");
            sb.Append("<button text='▲' on-click='other-up' pref-width='9' pref-height='4'/>");
            sb.Append("<button text='▼' on-click='other-down' pref-width='9' pref-height='4'/>");
            sb.Append("</horizontal>");

            for (int r = 0; r < OtherRows; r++)
            {
                OpenRow(sb, 4.2);
                // 広げるのは文字だけ。行全体を広げると絵の入れ物まで幅を分け合い、
                // 絵（縦横比を保つので小さいまま）の右に大きな空きができる。
                // 文字は幅固定の入れ物に1つだけ入れてその中で広げ、行の端まで押せるようにする
                var text = new StringBuilder();
                Box(text, TableWidth - IconWidth - 1, "MiddleLeft",
                    "<clickable-text tags='other-" + r + "' text='' font-size='3' align='Left'/>",
                    fill: true);

                Box(sb, TableWidth, "MiddleLeft", Icon("other-icon-" + r) + text);
                sb.Append("</horizontal>");
            }

            sb.Append("</vertical>");

            // 選んだものの内訳と Play
            sb.Append("<horizontal pref-height='9' spacing='3' pad-top='1'>");
            sb.Append("<text id='detail' text='' font-size='3' align='Left' pref-width='")
              .Append(TableWidth - 35).Append("'/>");
            sb.Append("<button text='Play' on-click='play' pref-width='32' pref-height='9'");
            sb.Append(" hover-hint='Play this drill now (practice mode; scores are not submitted). If it is not generated yet, it is generated first. You come back here when it ends.'/>");
            sb.Append("</horizontal>");

            // 状態の行。生成の進み具合もここに出る
            sb.Append("<horizontal pref-height='4'>");
            sb.Append("<text id='status' text='' font-size='2.6' align='Left' color='#8899AA' pref-width='")
              .Append(TableWidth).Append("'/>");
            sb.Append("</horizontal>");

            // 一番下に生成のボタン。叩くつもりで開いて譜面が無いと分かったとき、この画面を離れずに作れるように。
            // 上書きはボタンを分けずにチェックで切り替える（ボタンが4つだと横に収まらない）
            sb.Append("<horizontal pref-height='6' spacing='2'>");
            sb.Append("<button text='Generate' on-click='generate-selected' pref-width='24' pref-height='6'");
            sb.Append(" hover-hint='Generate the selected drill.'/>");
            sb.Append("<button text='Generate all' on-click='generate-all' pref-width='28' pref-height='6'");
            sb.Append(" hover-hint='Generate all 112 drills (14 directions x 8 stages).'/>");
            // toggle-setting は文字を左端、スイッチを右端に置くので、幅を詰めて間を空けすぎないようにする
            sb.Append("<horizontal pref-width='26'>");
            sb.Append("<toggle-setting text='Overwrite' value='overwrite' apply-on-change='true' get-event='overwrite-reset'");
            sb.Append(" hover-hint='Off: drills that already exist are skipped. On: they are written again. Records are kept.'/>");
            sb.Append("</horizontal>");
            sb.Append("</horizontal>");

            sb.Append("</vertical>");

            sb.Append("</bg>");
            return sb.ToString();
        }

        /// <summary>行頭の絵の幅。格子は 4x3 なので高さ 4 に対して横長に取る。</summary>
        private const double IconWidth = 5.3;

        /// <summary>行頭の絵。</summary>
        private static string Icon(string tag)
        {
            return "<image tags='" + tag + "' src='#EmptySprite' pref-width='" +
                   IconWidth.ToString(CultureInfo.InvariantCulture) +
                   "' min-width='" + IconWidth.ToString(CultureInfo.InvariantCulture) +
                   "' pref-height='4' preserve-aspect='true'/>";
        }

        /// <summary>表の1行を開く。子は広げず、決めた幅のまま左から並べる。</summary>
        private static void OpenRow(StringBuilder sb, double height)
        {
            sb.Append("<horizontal pref-height='").Append(height.ToString(CultureInfo.InvariantCulture))
              .Append("' spacing='0' child-expand-width='false' child-control-width='true' child-align='MiddleLeft'>");
        }

        /// <summary>幅を固定した入れ物に入れる。</summary>
        /// <param name="fill">
        /// 中身を入れ物いっぱいに広げるか。押せるマスは true にする。
        /// 広げないと押せる範囲が<b>文字の大きさだけ</b>になり、「○」1文字だと狙いにくい。
        /// 広げても文字は中央寄せのままなので、見た目は変わらない。
        /// </param>
        private static void Box(StringBuilder sb, double width, string align, string content, bool fill = false)
        {
            string expand = fill ? "true" : "false";
            string w = width.ToString(CultureInfo.InvariantCulture);
            sb.Append("<horizontal pref-width='").Append(w)
              .Append("' min-width='").Append(w)
              .Append("' child-align='").Append(align)
              .Append("' child-expand-width='").Append(expand)
              .Append("' child-expand-height='").Append(expand)
              .Append("' child-control-height='true' pad='0' spacing='1'>")
              .Append(content)
              .Append("</horizontal>");
        }

        // ───────── 組み上がった後 ─────────

        [UIAction("#post-parse")]
        public void PostParse()
        {
            if (parserParams == null) return;

            cells.Clear();
            rowLabels.Clear();
            rowIcons.Clear();
            otherTexts.Clear();
            otherIcons.Clear();

            for (int r = 0; r < DrillSet.Directions.Count; r++)
            {
                rowLabels.Add(Tagged<TextMeshProUGUI>("row-" + r));

                // 方向の絵は変わらないので、ここで1度入れれば済む
                var icon = Tagged<ImageView>("icon-" + r);
                rowIcons.Add(icon);
                if (icon != null && icons != null && r < rows.Count)
                    SetSprite(icon, icons.For(rows[r].Cells[0].Entry.Options.Sequences));

                for (int c = 0; c < Stages; c++)
                {
                    var text = Tagged<ClickableText>("mc-" + r + "-" + c);
                    if (text == null) continue;

                    int row = r, column = c;
                    text.OnClickEvent += _ => PickSet(row, column);
                    cells[Key(r, c)] = text;
                }
            }

            for (int r = 0; r < OtherRows; r++)
            {
                var text = Tagged<ClickableText>("other-" + r);
                otherTexts.Add(text);
                otherIcons.Add(Tagged<ImageView>("other-icon-" + r));

                int row = r;
                if (text != null) text.OnClickEvent += _ => PickOther(row);
            }

            ApplyView();
            Rescan();
        }

        private T? Tagged<T>(string tag) where T : Component
        {
            var go = parserParams?.GetObjectsWithTag(tag).FirstOrDefault();
            return go == null ? null : go.GetComponent<T>();
        }

        private static void SetSprite(ImageView image, Sprite? sprite)
        {
            image.gameObject.SetActive(sprite != null);
            if (sprite != null) image.sprite = sprite;
        }

        private static string Key(int row, int column) => row + "-" + column;

        // ───────── 操作 ─────────

        /// <summary>
        /// リプレイを読み直して数字を出し直す。
        /// </summary>
        /// <remarks>
        /// 別スレッドで読む。ドリルのリプレイを全部解析するので数秒かかる。
        /// 解析結果はファイル単位で持っているので、2回目からは増えた分しか読まない。
        /// Other の一覧（読み込まれている譜面）はメインスレッドでしか引けないので、戻ってから作る。
        /// </remarks>
        [UIAction("rescan")]
        public void Rescan()
        {
            if (reader == null) return;
            if (loading)
            {
                // 今読んでいる分は、頼まれた時点より前の状態かもしれない
                rescanAgain = true;
                return;
            }

            loading = true;
            rescanAgain = false;
            if (drillSet == null || !drillSet.IsRunning) SetStatus("Reading replays...");

            string packName = PluginConfig.Instance?.PackName ?? "JumpDrill";
            var source = reader;

            // 譜面のフォルダは SongCore から引くので、メインスレッドで先に控える
            source.RefreshLevelFolders(packName);

            Task.Run(() => source.BestById())
                .ContinueWith(task =>
                {
                    loading = false;

                    if (task.IsFaulted)
                    {
                        Plugin.Log?.Error("could not read the medals: " + task.Exception);
                        SetStatus("Could not read the replays. See the log.");
                    }
                    else
                    {
                        bestById = task.Result;
                        rows = DrillSetMedals.Evaluate(bestById);
                        LoadOthers();

                        // 選んでいた Other のドリルが消えていたら選び直させる
                        if (selectedId != null && !IsListed(selectedId)) selectedId = null;

                        Fill();

                        // 右の記録も同じ時点のものにする
                        onSelect?.Invoke(selectedId);

                        // 生成の知らせ（The drills are ready. など）が出ていればそのまま残す
                        if (status != null && status.text == "Reading replays...") SetStatus(string.Empty);
                    }

                    if (rescanAgain) Rescan();
                }, UnityMainThreadTaskScheduler.Default);
        }

        /// <summary>
        /// Other に出すドリルを集める。読み込まれているドリル譜面から、ドリルセットの ID を除いたもの。
        /// </summary>
        /// <remarks>
        /// 一覧の元は<b>読み込まれている譜面</b>（叩けるもの）。記録だけあって譜面を消したものは出さない。
        /// 叩けないものを並べても Play で止まるだけになる。
        /// </remarks>
        private void LoadOthers()
        {
            others = new List<OtherDrill>();
            if (launcher == null) return;

            try
            {
                var setIds = new HashSet<string>(
                    DrillSet.All().Select(e => DrillNaming.ExtractId(e.Options.Name)), StringComparer.Ordinal);

                others = launcher.DrillLevels()
                    .Where(d => !setIds.Contains(d.Id))
                    .Select(d => new OtherDrill
                    {
                        Id = d.Id,
                        Name = DisplayName(d.SongName),
                        Sequences = SequenceIcon.SequencesFromName(d.SongName),
                    })
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not list the other drills: " + e);
            }

            int pages = Math.Max(1, (others.Count + OtherRows - 1) / OtherRows);
            if (otherPage >= pages) otherPage = pages - 1;
        }

        private bool IsListed(string id)
        {
            return rows.Any(r => r.Cells.Any(c => c.Id == id)) || others.Any(o => o.Id == id);
        }

        /// <summary>ID と頭の "Drill " を落とした名前。行が長くなりすぎないように。</summary>
        private static string DisplayName(string songName)
        {
            string name = DrillNaming.StripId(songName).Trim();
            return name.StartsWith(DrillNaming.Prefix, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(DrillNaming.Prefix.Length)
                : name;
        }

        [UIAction("toggle-view")]
        public void ToggleView()
        {
            showOther = !showOther;
            ApplyView();
        }

        [UIAction("other-up")]
        public void OtherUp()
        {
            if (otherPage == 0) return;
            otherPage--;
            Fill();
        }

        [UIAction("other-down")]
        public void OtherDown()
        {
            if ((otherPage + 1) * OtherRows >= others.Count) return;
            otherPage++;
            Fill();
        }

        private void ApplyView()
        {
            if (setView != null) setView.SetActive(!showOther);
            if (otherView != null) otherView.SetActive(showOther);

            // ボタンには<b>押すと切り替わる先</b>を出す。2択なので、いま出ている方は
            // 画面の中身で分かる。いま出ている方を書くと、押す前に反対だと読めてしまう
            if (viewButton != null)
            {
                var label = viewButton.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = showOther ? "Drill" : "Other";
            }

            Fill();
        }

        private void PickSet(int row, int column)
        {
            if (row >= rows.Count || column >= rows[row].Cells.Count) return;

            selectedId = rows[row].Cells[column].Id;
            Fill();
            onSelect?.Invoke(selectedId);
        }

        private void PickOther(int row)
        {
            int index = otherPage * OtherRows + row;
            if (index >= others.Count) return;

            selectedId = others[index].Id;
            Fill();
            onSelect?.Invoke(selectedId);
        }

        /// <summary>
        /// 開くたびに Overwrite を外す。ビューは使い回すので、外さないと前に開いたときのままになる。
        /// </summary>
        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            if (firstActivation) return;

            Overwrite = false;
            parserParams?.EmitEvent("overwrite-reset");
        }

        /// <summary>
        /// 既にあるものを書き直すか。既定は書き直さない（足りない分だけ作る）。
        /// 設定には残さない。上書きは作り方が変わった版を入れたときにだけ要るので、開くたびに外しておく。
        /// </summary>
        [UIValue("overwrite")]
        public bool Overwrite { get; set; }

        /// <summary>
        /// ドリルセット（14方向 × 8段）を全部書く。GUI の「ドリル一括生成」と同じもの。
        /// </summary>
        /// <remarks>
        /// 数十秒かかるので別スレッドで回し、進み具合を状態の行に出す。
        /// 上書きしなければ既にあるものは飛ばすので、途中でやめても続きから再開できる。
        /// 記録は ID で引くので、上書きしても残る。
        /// 書き終えたら曲一覧が読み直されるので、そのあとで表も読み直す（Play が通るようになる）。
        /// </remarks>
        [UIAction("generate-all")]
        public void GenerateAll() => GenerateSet(Overwrite);

        private void GenerateSet(bool overwrite)
        {
            if (drillSet == null) return;

            if (drillSet.IsRunning)
            {
                SetStatus("Already generating...");
                return;
            }

            SetStatus(string.Format(CultureInfo.InvariantCulture,
                "Generating drills... 0/{0}", DrillSetGenerationService.Total));

            drillSet.Start(overwrite,
                (done, total) => SetStatus(string.Format(CultureInfo.InvariantCulture,
                    "Generating drills... {0}/{1}", done, total)),
                (ok, message) =>
                {
                    SetStatus(message);
                    if (ok) Rescan();
                });
        }

        /// <summary>選んでいる1本を書く。ドリルセットのものだけ（Other は作った設定が分からない）。</summary>
        [UIAction("generate-selected")]
        public void GenerateSelected() => GenerateOne(Overwrite);

        private void GenerateOne(bool overwrite)
        {
            if (drillSet == null) return;

            if (selectedId == null)
            {
                SetStatus("Pick a drill first.");
                return;
            }

            var entry = SetEntry(selectedId);
            if (entry == null)
            {
                SetStatus("Only drills in the table can be generated.");
                return;
            }

            if (drillSet.IsRunning)
            {
                SetStatus("Already generating...");
                return;
            }

            // 曲一覧の読み込み中は、譜面があるか分からない
            if (SongCore.Loader.AreSongsLoading)
            {
                SetStatus("Songs are still loading. Try again in a moment.");
                return;
            }

            if (!overwrite && launcher != null && !launcher.IsMissing(selectedId))
            {
                SetStatus("This drill already exists. Turn on Overwrite to write it again.");
                return;
            }

            SetStatus("Generating the drill...");
            drillSet.StartOne(entry, overwrite, (ok, message) =>
            {
                SetStatus(message);
                if (ok) Rescan();
            });
        }

        /// <summary>
        /// 選んでいるドリルを始める。ドリルセットの譜面がまだ無ければ、その1本だけ作ってから始める。
        /// </summary>
        /// <remarks>
        /// 112本を先にまとめて作らなくても、叩きたいものから叩ける。
        /// 作っている間に別のドリルを選んだり画面を閉じたりしたら、始めずに知らせだけ出す。
        /// </remarks>
        [UIAction("play")]
        public void Play()
        {
            if (selectedId == null)
            {
                SetStatus("Pick a drill first.");
                return;
            }

            string id = selectedId;
            var entry = SetEntry(id);
            if (entry == null || drillSet == null || launcher == null || !launcher.IsMissing(id))
            {
                onPlay?.Invoke(id);
                return;
            }

            if (drillSet.IsRunning)
            {
                SetStatus("Generating drills. Try again when it is done.");
                return;
            }

            SetStatus("Generating the drill...");
            drillSet.StartOne(entry, overwrite: false, (ok, message) =>
            {
                if (ok && isActivated && selectedId == id)
                {
                    SetStatus(string.Empty);
                    onPlay?.Invoke(id);
                    return;
                }

                SetStatus(message);
            });
        }

        /// <summary>ドリルセットの1本。ドリルセットの ID でなければ null。</summary>
        private DrillSetEntry? SetEntry(string id)
        {
            return rows.SelectMany(r => r.Cells).FirstOrDefault(c => c.Id == id)?.Entry;
        }

        // ───────── 塗り ─────────

        /// <summary>数字と色を今の成績で塗り直す。</summary>
        private void Fill()
        {
            var ci = CultureInfo.InvariantCulture;

            int level = rows.Sum(r => r.Level);
            int medals = rows.Sum(r => r.Cells.Count(c => c.Medal != Medal.None));
            int cellsTotal = rows.Sum(r => r.Cells.Count);

            if (total != null)
                total.text = string.Format(ci, "Total Lv {0} / {1}", level, DrillSetMedals.MaxLevel);
            if (count != null)
                count.text = string.Format(ci, "Medals {0} / {1}", medals, cellsTotal);

            FillSet();
            FillOther();
            FillDetail();
        }

        private void FillSet()
        {
            var ci = CultureInfo.InvariantCulture;

            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];

                if (r < rowLabels.Count && rowLabels[r] != null)
                {
                    int max = row.Cells.Count * DrillSetMedals.Points(Medal.Gold);
                    rowLabels[r]!.text = string.Format(ci, "{0}  <color=#8899AA><size=80%>Lv {1}/{2}</size></color>",
                        row.Direction, row.Level, max);
                }

                for (int c = 0; c < row.Cells.Count; c++)
                {
                    ClickableText text;
                    if (!cells.TryGetValue(Key(r, c), out text)) continue;

                    var cell = row.Cells[c];
                    Paint(text, cell.BestPercent, cell.Id == selectedId, center: true, label: null);
                }
            }
        }

        private void FillOther()
        {
            var ci = CultureInfo.InvariantCulture;

            if (otherPageText != null)
            {
                otherPageText.text = others.Count == 0
                    ? "No other drills. Make one with the grid in the Mods tab."
                    : string.Format(ci, "{0} other drill(s).  {1}-{2}",
                        others.Count,
                        otherPage * OtherRows + 1,
                        Math.Min((otherPage + 1) * OtherRows, others.Count));
            }

            for (int r = 0; r < otherTexts.Count; r++)
            {
                var text = otherTexts[r];
                var icon = r < otherIcons.Count ? otherIcons[r] : null;
                int index = otherPage * OtherRows + r;

                if (index >= others.Count)
                {
                    if (text != null) text.text = string.Empty;
                    if (icon != null) SetSprite(icon, null);
                    continue;
                }

                var drill = others[index];
                if (icon != null) SetSprite(icon, icons?.For(drill.Sequences));

                if (text != null)
                {
                    double best;
                    double? percent = bestById.TryGetValue(drill.Id, out best) ? best : (double?)null;
                    Paint(text, percent, drill.Id == selectedId, center: false, label: drill.Name);
                }
            }
        }

        /// <summary>
        /// 1マス（1行）を塗る。叩いていなければ ○、叩いていれば ○ と数字、
        /// メダルが取れていれば色付きの ● と数字。
        /// </summary>
        /// <param name="label">Other のときの名前。数字の前に置く。Drill では null。</param>
        private static void Paint(ClickableText text, double? percent, bool picked, bool center, string? label)
        {
            var ci = CultureInfo.InvariantCulture;
            var medal = percent.HasValue ? DrillSetMedals.MedalOf(percent.Value) : Medal.None;

            // 印は GUI のメダル画面と揃える。メダルが取れていれば色付きの ●、
            // 叩いたがメダルに届かなければ ○ と数字、叩いていなければ ○ だけ。
            // 数字だけのマスがあると、そこだけ列の中で位置がずれて見える
            string number;
            if (!percent.HasValue)
                number = "○";
            else if (medal != Medal.None)
                number = "<color=#" + ColorUtility.ToHtmlStringRGB(MedalColor(medal)) + ">●</color>" +
                         percent.Value.ToString("0.0", ci);
            else
                number = "○" + percent.Value.ToString("0.0", ci);

            string body = label == null ? number : label + "   " + number;
            text.text = picked ? "[" + body + "]" : body;

            Color color = picked ? Picked
                : medal != Medal.None ? MedalColor(medal)
                : percent.HasValue ? Played
                : label != null ? Played   // Other は叩いていなくても名前は読めるように
                : Empty;

            // ClickableText は指を離したときに DefaultColor へ戻すので、そちらに入れる
            text.DefaultColor = color;
            text.HighlightColor = Picked;
            text.color = color;
        }

        /// <summary>選んだものの内訳。どの速さ・どの尺のドリルかを出す。</summary>
        private void FillDetail()
        {
            if (detail == null) return;

            if (selectedId == null)
            {
                detail.text = "<color=#8899AA>Pick a drill, then Play.</color>";
                return;
            }

            var ci = CultureInfo.InvariantCulture;

            double bestValue;
            bool played = bestById.TryGetValue(selectedId, out bestValue);
            string best = played ? string.Format(ci, "best {0:0.0}%", bestValue) : "not played";
            var medal = played ? DrillSetMedals.MedalOf(bestValue) : Medal.None;
            string medalText = medal == Medal.None ? string.Empty : "  " + medal;

            var cell = rows.SelectMany(r => r.Cells).FirstOrDefault(c => c.Id == selectedId);
            if (cell != null)
            {
                var entry = cell.Entry;
                detail.text = string.Format(ci,
                    "{0}  stage {1}   {2:0.#} BPM   NJS {3:0.#}   {4:0.##}s per hand   {5}{6}",
                    entry.Direction, entry.Stage, entry.Bpm, entry.Options.Njs, entry.Options.DurationSeconds,
                    best, medalText);
                return;
            }

            var other = others.FirstOrDefault(o => o.Id == selectedId);
            detail.text = other == null
                ? string.Empty
                : string.Format(ci, "{0}   {1}{2}", other.Name, best, medalText);
        }

        private static Color MedalColor(Medal medal)
        {
            switch (medal)
            {
                case Medal.Gold: return Gold;
                case Medal.Silver: return Silver;
                case Medal.Bronze: return Bronze;
                default: return Played;
            }
        }

        internal void SetStatus(string text)
        {
            if (status != null) status.text = text;
        }
    }
}
