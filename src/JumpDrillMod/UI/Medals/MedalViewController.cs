using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Components.Settings;
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
    /// ドリルを選んで叩く画面。上のプルダウンでステージを切り替える。
    /// <b>Standard</b> はドリルセット（14方向 × 8段）のメダルと Lv、
    /// <b>Other</b> はどのステージにも入っていないドリル、<b>CUSTOM…</b> は自分で並べたカスタムステージ
    /// （組み替えは MedalViewController.Custom.cs / Picker.cs）。
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
    internal partial class MedalViewController : BSMLViewController
    {
        private ReplayScoreReader? reader;
        private LevelLauncher? launcher;
        private IconService? icons;
        private Action<string>? onPlay;
        private DrillSetGenerationService? drillSet;

        /// <summary>ドリルを選んだとき。右の枠のリーダーボードを切り替えるのに使う。</summary>
        private Action<string?>? onSelect;

        /// <summary>Make drill を押したとき。ドリルを組む画面を出す。</summary>
        /// <summary>ドリルを組む画面を出す。引数はグリッドを合わせる方向（無ければ null）。出せなければ false。</summary>
        private Func<string?, bool>? onMake;

        /// <summary>ドリルを組む画面を開いた時刻。戻ったとき、これより後に作ったものがあるときだけ選び直す。</summary>
        private DateTime makerOpenedAt;

        /// <summary>方向ごとの成績。読み込み前は全部空で出す（どこを埋めればいいかが見える）。</summary>
        private List<MedalRow> rows = DrillSetMedals.Evaluate(new Dictionary<string, double>());

        /// <summary>ID ごとのベスト（REPRO と ACC）。基準を切り替えても読み直さずに済むよう両方持つ。</summary>
        private ReplayScoreReader.BestRecords bests = new ReplayScoreReader.BestRecords();

        /// <summary>今の基準での ID ごとのベスト。Other の数字もここから引く。</summary>
        private Dictionary<string, double> bestById => bests.For(RankSetting.Current);

        /// <summary>Other に出せるドリルの曲名（読み込まれている譜面のうち、ドリルセット以外も含む全部）。</summary>
        private List<string> otherNames = new List<string>();

        /// <summary>作ったばかりのドリルの曲名（新しい順、JumpDrill.Core の <c>DrillLibrary.JustMade</c>）。</summary>
        private List<string> justMade = new List<string>();

        /// <summary>いちばん新しく作ったドリル（<see cref="justMade"/> の先頭）を書いた時刻。</summary>
        private DateTime justMadeAt = DateTime.MinValue;

        /// <summary>読み直しが終わったら、いちばん新しく作ったドリルを選ぶ（Make drill から戻ったとき）。</summary>
        private bool revealPending;

        /// <summary>
        /// Other の表。方向ごとに遅い順に 8 本ずつ、超えた分は同じ方向の次の行へ
        /// （JumpDrill.Core の <c>CustomStage.Other</c>、GUI のメダル画面の「その他」と同じ）。
        /// </summary>
        private List<MedalRow> otherRows = new List<MedalRow>();

        /// <summary>選んでいるドリルの ID。Drill と Other で共通。</summary>
        private string? selectedId;

        /// <summary>Other の何ページ目か。</summary>
        private int otherPage;

        private bool loading;

        /// <summary>読んでいる最中に読み直しを頼まれた。終わったらもう1回読む。</summary>
        private bool rescanAgain;

        private readonly Dictionary<string, ClickableText> cells = new Dictionary<string, ClickableText>();
        private readonly List<TextMeshProUGUI?> rowLabels = new List<TextMeshProUGUI?>();
        private readonly List<ImageView?> rowIcons = new List<ImageView?>();
        private readonly Dictionary<string, ClickableText> otherCells = new Dictionary<string, ClickableText>();
        private readonly List<ClickableText?> otherLabels = new List<ClickableText?>();
        private readonly List<ImageView?> otherIcons = new List<ImageView?>();

        [UIParams] private readonly BSMLParserParams? parserParams = null;

        [UIComponent("total")] private TextMeshProUGUI? total = null;
        [UIComponent("count")] private TextMeshProUGUI? count = null;
        [UIComponent("detail")] private TextMeshProUGUI? detail = null;
        [UIComponent("status")] private TextMeshProUGUI? status = null;
        [UIComponent("other-page")] private TextMeshProUGUI? otherPageText = null;

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
            DrillSetGenerationService drillSet, Action<string> onPlay, Action<string?> onSelect, Func<string?, bool>? onMake)
        {
            this.onMake = onMake;
            this.reader = reader;
            this.launcher = launcher;
            this.icons = icons;
            this.drillSet = drillSet;
            this.onPlay = onPlay;
            this.onSelect = onSelect;

            // プルダウンの中身は組み上げるときに読むので、その前にステージを読んでおく
            LoadBook();
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

            // 総合と切り替え。2段に分ける（ユーザー指定の並び）。
            // 1段目: 総合とメダル数（左端）・ステージ（Standard / Other / カスタム）・New、右端に Make drill。
            // 2段目: 基準（REPRO / ACC）・Rescan を左寄せ。
            // 基準とステージは2択でもボタンにはしない（今どちらか読めない）。
            // 総合は入れ物いっぱいに広げて左寄せにする。広げないと入れ物の真ん中に出た（ゲーム内で確認）。
            // 1段目の幅: 総合 30 + Standard 30 + New 12 + 空き + Make drill 24、間 1×4 で表の内側（TableWidth - 4）。
            // 画面を移るボタンは色ではなく字の後ろの「→」で示す（ユーザー指定。→ は ▲▼ と同じく JIS の記号で、本体の日本語フォントで出る見込み）
            sb.Append("<horizontal pref-height='7' spacing='1' child-expand-width='false' child-control-width='true' child-align='MiddleLeft'>");
            // 入れ物は Box と同じだが、幅も中身に合わせて広げる（child-control-width）。広げないと文字が真ん中に寄る
            sb.Append("<horizontal pref-width='30' min-width='30' child-align='MiddleLeft' child-expand-width='true' child-control-width='true'")
              .Append(" child-expand-height='true' child-control-height='true' pad='0'>");
            sb.Append("<vertical spacing='0' child-expand-height='false' child-expand-width='true' child-control-width='true' child-align='MiddleLeft'>");
            sb.Append("<text id='total' text='' font-size='3.4' align='Left' word-wrapping='false'/>");
            sb.Append("<text id='count' text='' font-size='2.6' align='Left' color='#8899AA'/>");
            sb.Append("</vertical></horizontal>");
            sb.Append("<dropdown-list-setting id='stage-dropdown' text='' value='stage' options='stage-options'");
            sb.Append(" apply-on-change='true' bind-value='true' on-change='stage-changed' pref-width='30' pref-height='6'");
            sb.Append(" hover-hint='Standard: the 112 drills. Other: drills not in any stage. All: drills you made, including those in stages. CUSTOM: stages you made.'/>");
            sb.Append("<button text='New' on-click='stage-new' pref-width='12' pref-height='6'");
            sb.Append(" hover-hint='Make a new custom stage. You can rename it later.'/>");
            Box(sb, TableWidth - 4 - 30 - 30 - 12 - 24 - 4, "MiddleLeft", "<text text='' font-size='2.6'/>");
            sb.Append("<button text='Make drill →' on-click='make-drill' pref-width='24' pref-height='6'");
            sb.Append(" hover-hint='Make a new drill with the grid, the same as in the Mods tab.'/>");
            sb.Append("</horizontal>");

            // プルダウンは指定の幅より広く出ることがあるので、Rescan との間を広めに空ける
            sb.Append("<horizontal pref-height='6' spacing='3' child-expand-width='false' child-control-width='true' child-align='MiddleLeft'>");
            sb.Append("<dropdown-list-setting id='rank-dropdown' text='' value='rank' options='rank-options'");
            sb.Append(" apply-on-change='true' bind-value='true' on-change='rank-changed' pref-width='30' pref-height='6'");
            sb.Append(" hover-hint='The number for medals, Lv and the rank. REPRO: reproducibility accuracy. ACC: the game accuracy. Medals are 70 / 80 / 90 for both.'/>");
            sb.Append("<button text='Rescan' on-click='rescan' pref-width='15' pref-height='6'");
            sb.Append(" hover-hint='Read the replays again.'/>");
            sb.Append("</horizontal>");

            // ───── Drill ─────
            sb.Append("<vertical id='set-view' spacing='0.3' child-expand-height='false'>");

            // 見出しの段。Other・カスタムの見出しの段（ボタンが乗る）と高さを揃える
            OpenRow(sb, HeaderRowHeight);
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

            // ───── Other / カスタムステージ ─────
            // Drill と同じ形の表（方向ごとに遅い順に8マス、超えた分は同じ方向の次の行）。
            // 見出しの段の番号の代わりに、Other では件数とページ送り、カスタムではステージの名前の変更と削除を置く
            // （高さを Drill と揃えるため）。マスの BPM は方向ごとに違うので、選んだときに下の内訳に出す。
            // カスタムでは行頭を押すと行を選び、「+」でドリルを足す
            sb.Append("<vertical id='other-view' active='false' spacing='0.3' child-expand-height='false'>");

            OpenRow(sb, HeaderRowHeight);
            sb.Append("<text id='other-page' text='' font-size='2.6' align='Left' color='#8899AA' pref-width='")
              .Append(TableWidth - 40).Append("'/>");
            sb.Append("<horizontal id='page-buttons' pref-width='40' spacing='1' child-expand-width='false' child-align='MiddleRight'>");
            sb.Append("<button text='▲' on-click='other-up' pref-width='9' pref-height='5'/>");
            sb.Append("<button text='▼' on-click='other-down' pref-width='9' pref-height='5'/>");
            sb.Append("</horizontal>");
            sb.Append("<horizontal id='stage-buttons' active='false' pref-width='40' spacing='1' child-expand-width='false' child-align='MiddleRight'>");
            sb.Append("<button text='Rename' on-click='stage-rename' pref-width='18' pref-height='5' hover-hint='Rename this stage.'/>");
            sb.Append("<button text='").Append(Danger("Delete")).Append("' on-click='stage-delete' pref-width='18' pref-height='5'");
            sb.Append(" hover-hint='Delete this stage. Maps and records are kept.'/>");
            sb.Append("</horizontal>");
            sb.Append("</horizontal>");

            for (int r = 0; r < OtherRows; r++)
            {
                OpenRow(sb, 4.2);
                Box(sb, LabelWidth, "MiddleLeft",
                    Icon("other-icon-" + r) +
                    "<clickable-text tags='orow-" + r + "' text='' font-size='2.8' align='Left'/>");

                for (int c = 0; c < Stages; c++)
                {
                    Box(sb, CellWidth, "MiddleCenter",
                        "<clickable-text tags='oc-" + r + "-" + c + "' text='' font-size='3' align='Center'/>",
                        fill: true);
                }

                sb.Append("</horizontal>");
            }

            sb.Append("</vertical>");

            // 選んだものの内訳と Play。
            // 文字は幅を固定した入れ物で包む。文字に直接 pref-width を書いても効かず、
            // 内訳の長さで Play が左右に動いた（ゲーム内で確認）。入れ物 + 間 3 + Play 32 で表の内側の幅（TableWidth - 4）
            sb.Append("<horizontal pref-height='9' spacing='3' pad-top='1' child-expand-width='false' child-control-width='true' child-align='MiddleLeft'>");
            Box(sb, TableWidth - 4 - 3 - 32, "MiddleLeft", "<text id='detail' text='' font-size='3' align='Left'/>", fill: true);
            sb.Append("<action-button text='Play' on-click='play' pref-width='32' pref-height='9'");
            sb.Append(" hover-hint='Play this drill now (practice mode; scores are not submitted). If it is not generated yet, it is generated first. You come back here when it ends.'/>");
            sb.Append("</horizontal>");

            // 状態の行。生成の進み具合もここに出る。文字は内訳と同じく幅を固定した入れ物で包む
            sb.Append("<horizontal pref-height='4' child-expand-width='false' child-control-width='true' child-align='MiddleLeft'>");
            Box(sb, TableWidth - 4, "MiddleLeft", "<text id='status' text='' font-size='2.6' align='Left' color='#8899AA'/>", fill: true);
            sb.Append("</horizontal>");

            // 一番下に生成のボタン。叩くつもりで開いて譜面が無いと分かったとき、この画面を離れずに作れるように。
            // 上書きはボタンを分けずにチェックで切り替える（ボタンが4つだと横に収まらない）
            sb.Append("<horizontal id='generate-row' pref-height='6' spacing='2'>");
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

            // カスタムステージでは、生成のボタンの代わりに組み替えのボタンを出す
            AppendEditRow(sb);
            // Other とカスタムでは、ドリルそのものを消すボタン
            AppendDrillRow(sb);

            sb.Append("</vertical>");

            // 別窓（ルート直下に置く）。ドリルの選択と、名前を入れるキーボード
            AppendPicker(sb);
            sb.Append("<modal-keyboard id='keyboard' value='keyboard-text' on-enter='keyboard-enter' clear-on-open='false'");
            sb.Append(" show-event='show-keyboard' hide-event='hide-keyboard'/>");

            sb.Append("</bg>");
            return sb.ToString();
        }

        /// <summary>
        /// 削除のボタンの字。橙赤（警告の文と同じ #FF8060）にして、押す前に目に入るようにする（ユーザー指定）。
        /// 押せば確かめずに消えるので、見た目で注意させる。字の色はリッチテキストで付ける
        /// （マークアップの属性に書くので &lt; &gt; はエスケープする）。
        /// </summary>
        internal static string Danger(string text) => "&lt;color=#FF8060&gt;" + text + "&lt;/color&gt;";

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
        /// <summary>
        /// 表の見出しの段の高さ。Other・カスタムではここにボタン（▲▼・Rename・Delete）が乗るので、
        /// ボタンの文字が潰れない高さにする。Drill の見出しの段も同じ高さにして、表の位置を揃える。
        /// </summary>
        private const double HeaderRowHeight = 5;

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
            otherCells.Clear();
            otherLabels.Clear();
            otherIcons.Clear();

            for (int r = 0; r < DrillSet.Directions.Count; r++)
            {
                rowLabels.Add(Tagged<TextMeshProUGUI>("row-" + r));

                // 方向の絵は変わらないので、ここで1度入れれば済む
                var icon = Tagged<ImageView>("icon-" + r);
                rowIcons.Add(icon);
                if (icon != null && icons != null && r < rows.Count)
                    SetSprite(icon, icons.For(rows[r].Cells[0].Entry.Options.Sequences, rows[r].Cells[0].Entry.Options.Tower));

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
                var label = Tagged<ClickableText>("orow-" + r);
                otherLabels.Add(label);
                otherIcons.Add(Tagged<ImageView>("other-icon-" + r));
                int labelRow = r;
                if (label != null) label.OnClickEvent += _ => PickLabel(labelRow);

                for (int c = 0; c < Stages; c++)
                {
                    var text = Tagged<ClickableText>("oc-" + r + "-" + c);
                    if (text == null) continue;

                    int row = r, column = c;
                    text.OnClickEvent += _ => PickOther(row, column);
                    otherCells[Key(r, c)] = text;
                }
            }

            PostParseCustom();

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

        /// <summary>
        /// 行名の「方向 + 印（tower・alt-trip・flick25）」の印を、色と大きさを変えて方向と見分けが付くようにする。
        /// 行名はカスタムステージで自由に付けられるので、タグとして読まれないよう &lt;noparse&gt; で囲む。
        /// </summary>
        private static string RowName(string? direction)
        {
            string name, tags;
            JumpDrill.Model.DrillNaming.SplitTags(direction, out name, out tags);

            string shown = NoParse(name);
            if (tags.Length > 0) shown += " <color=#E0B060><size=75%>" + NoParse(tags) + "</size></color>";
            return shown;
        }

        /// <summary>リッチテキストのタグとして読ませない。閉じタグが中にあれば崩れるので、その &lt; だけ全角に置き換える。</summary>
        private static string NoParse(string text)
        {
            return "<noparse>" + text.Replace("</noparse>", "＜/noparse>") + "</noparse>";
        }

        // ───────── 操作 ─────────

        /// <summary>
        /// リプレイを読み直して数字を出し直す。
        /// </summary>
        /// <remarks>
        /// 別スレッドで読む。ドリルのリプレイを全部解析するので数秒かかる。
        /// 解析結果はファイル単位で持っているので、2回目からは増えた分しか読まない。
        /// Other の一覧（読み込まれている譜面）はメインスレッドでしか引けないので、戻ってから作る。
        /// </remarks>
        [UIAction("make-drill")]
        public void MakeDrill()
        {
            makerOpenedAt = DateTime.Now;
            onMake?.Invoke(null);
        }

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
                        bests = task.Result;
                        rows = DrillSetMedals.Evaluate(bestById);

                        // GUI で組み替えたかもしれないので、ステージも読み直す
                        LoadBook();
                        LoadOthers();

                        // 選んでいた Other のドリルが消えていたら選び直させる
                        if (selectedId != null && !IsListed(selectedId)) selectedId = null;

                        if (revealPending)
                        {
                            revealPending = false;
                            // 作る画面で何か作ったときだけ。開いて戻っただけなら今の表のまま
                            if (justMadeAt >= makerOpenedAt) RevealNewest();
                        }

                        // 足す窓から Make drill へ行っていたら、窓を開き直して作ったものを選んでおく
                        if (makeStage != null)
                        {
                            string stageName = makeStage;
                            makeStage = null;
                            if (mode == ViewMode.Custom && customName == stageName) OpenPicker(makeRow, makeSince);
                        }

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
        /// Other に出すドリルを集める。読み込まれているドリル譜面から、ドリルセットの ID を除いたもの
        /// （除くのは <c>CustomStage.Other</c> の中でやる）。
        /// </summary>
        /// <remarks>
        /// 一覧の元は<b>読み込まれている譜面</b>（叩けるもの）。記録だけあって譜面を消したものは出さない。
        /// 叩けないものを並べても Play で止まるだけになる。
        /// </remarks>
        private void LoadOthers()
        {
            otherNames = new List<string>();
            if (launcher != null)
            {
                try
                {
                    var levels = launcher.DrillLevels();
                    otherNames = levels.Select(d => d.SongName).ToList();
                    var written = levels.Select(d => new KeyValuePair<string, DateTime>(d.SongName, WrittenAt(d.Folder))).ToList();
                    justMade = DrillLibrary.JustMade(written, DateTime.Now);
                    justMadeAt = justMade.Count == 0 ? DateTime.MinValue
                        : written.Where(w => w.Key == justMade[0]).Max(w => w.Value);
                }
                catch (Exception e)
                {
                    Plugin.Log?.Error("could not list the other drills: " + e);
                }
            }

            EvaluateOthers();
        }

        /// <summary>譜面を書いた時刻（Info.dat の更新時刻）。読めなければ最小値。</summary>
        private static DateTime WrittenAt(string folder)
        {
            try { return File.GetLastWriteTime(Path.Combine(folder, "Info.dat")); }
            catch (Exception) { return DateTime.MinValue; }
        }

        /// <summary>読み直して、いちばん新しく作ったドリルを選ぶ。Make drill から戻ったときに使う。</summary>
        public void RescanAndRevealNewest()
        {
            revealPending = true;
            Rescan();
        }

        /// <summary>
        /// いちばん新しく作ったドリルを Other で選び、そのページを出す。選べたら true（表も出し直してある）。
        /// カスタムステージを見ているときは切り替えない（そこへ足すために作っている途中かもしれない）。
        /// </summary>
        private bool RevealNewest()
        {
            if (justMade.Count == 0 || mode == ViewMode.Custom) return false;

            int index = otherRows.FindIndex(r => r.Cells.Any(c => c.SongName == justMade[0]));
            if (index < 0) return false;

            if (mode != ViewMode.Other)
            {
                mode = ViewMode.Other;
                if (PluginConfig.Instance != null) PluginConfig.Instance.MedalStage = "other";
                RefreshDropdown();
            }
            otherPage = index / OtherRows;
            selectedId = otherRows[index].Cells.First(c => c.SongName == justMade[0]).Id;
            ApplyView();
            onSelect?.Invoke(selectedId);
            return true;
        }

        /// <summary>
        /// Other とカスタムステージの表を今の記録で作り直す。
        /// Other にはどのカスタムステージにも入っていないものだけを出す。
        /// </summary>
        private void EvaluateOthers()
        {
            otherRows = DrillSetMedals.Evaluate(CustomStage.Other(otherNames, allDrills ? null : book.Stages), bestById);

            int pages = Math.Max(1, (otherRows.Count + OtherRows - 1) / OtherRows);
            if (otherPage >= pages) otherPage = pages - 1;

            var stage = CurrentStage;
            customRows = stage == null ? new List<MedalRow>() : DrillSetMedals.Evaluate(stage, bestById);
        }

        private IEnumerable<MedalCell> OtherCells => otherRows.SelectMany(r => r.Cells);

        private bool IsListed(string id)
        {
            return rows.Any(r => r.Cells.Any(c => c.Id == id)) || OtherCells.Any(c => c.Id == id) ||
                   customRows.Any(r => r.Cells.Any(c => c.Id == id));
        }

        /// <summary>真ん中の下の表（Other かカスタムステージ）に出している行。</summary>
        private List<MedalRow> ListRows => mode == ViewMode.Custom ? customRows : otherRows;

        /// <summary>表の何行目から出すか。ページを送るのは Other だけ（カスタムは 14 行まで）。</summary>
        private int ListOffset => mode == ViewMode.Other ? otherPage * OtherRows : 0;

        /// <summary>ID と頭の "Drill " を落とした名前。行が長くなりすぎないように。</summary>
        private static string DisplayName(string songName)
        {
            string name = DrillNaming.DisplayName(songName).Trim();
            return name.StartsWith(DrillNaming.Prefix, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(DrillNaming.Prefix.Length)
                : name;
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
            if ((otherPage + 1) * OtherRows >= otherRows.Count) return;
            otherPage++;
            Fill();
        }

        private void ApplyView()
        {
            bool list = mode != ViewMode.Standard;
            bool custom = mode == ViewMode.Custom;

            if (setView != null) setView.SetActive(!list);
            if (otherView != null) otherView.SetActive(list);
            if (pageButtons != null) pageButtons.SetActive(!custom);
            if (stageButtons != null) stageButtons.SetActive(custom);

            // 生成は指定を持っている Standard だけ。カスタムでは組み替えのボタンを出す
            if (generateRow != null) generateRow.SetActive(mode == ViewMode.Standard);
            if (editRow != null) editRow.SetActive(custom);
            if (drillRow != null) drillRow.SetActive(list);

            Fill();
        }

        private void PickSet(int row, int column)
        {
            if (row >= rows.Count || column >= rows[row].Cells.Count) return;

            selectedId = rows[row].Cells[column].Id;
            Fill();
            onSelect?.Invoke(selectedId);
        }

        /// <summary>下の表のマス。カスタムでは行の右端の「+」でドリルを足す。</summary>
        private void PickOther(int row, int column)
        {
            int index = ListOffset + row;
            var list = ListRows;

            if (mode == ViewMode.Custom && index < list.Count && column == list[index].Cells.Count &&
                column < CustomStage.MaxDrillsPerRow)
            {
                OpenPicker(index);
                return;
            }

            if (index >= list.Count || column >= list[index].Cells.Count) return;

            selectedId = list[index].Cells[column].Id;
            selectedRow = index;
            selectedColumn = column;
            Fill();
            onSelect?.Invoke(selectedId);
        }

        /// <summary>下の表の行頭。カスタムでは行を選ぶ。行の続きの「+ Add row」で新しい行を作る。</summary>
        private void PickLabel(int row)
        {
            if (mode != ViewMode.Custom) return;

            int index = ListOffset + row;
            if (index == customRows.Count && index < CustomStage.MaxRows)
            {
                OpenPicker(index);
                return;
            }
            if (index >= customRows.Count) return;

            selectedRow = index;
            selectedColumn = -1;
            Fill();
        }

        /// <summary>
        /// 開くたびに Overwrite を外す。ビューは使い回すので、外さないと前に開いたときのままになる。
        /// </summary>
        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);

            // 基準はリーダーボードの並べ方からも変わる。閉じている間に変わっていれば合わせる。
            // 外す前に出し直しが来ても二重にならないよう、いったん外してから付ける
            RankSetting.Changed -= OnRankChanged;
            RankSetting.Changed += OnRankChanged;
            if (shownMetric != RankSetting.Current) OnRankChanged();

            if (firstActivation) return;

            Overwrite = false;
            parserParams?.EmitEvent("overwrite-reset");
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
            RankSetting.Changed -= OnRankChanged;
        }

        /// <summary>DidDeactivate を通らずに消えたときも、基準の知らせを受けないようにする。</summary>
        protected override void OnDestroy()
        {
            RankSetting.Changed -= OnRankChanged;
            base.OnDestroy();
        }

        // ───────── 基準（REPRO / ACC） ─────────

        private const string RankRepro = "By REPRO";
        private const string RankAcc = "By ACC";

        /// <summary>表を作った基準。違っていれば作り直す。</summary>
        private RankMetric shownMetric = RankSetting.Current;

        [UIComponent("rank-dropdown")] private DropDownListSetting? rankDropdown = null;

        [UIValue("rank-options")]
        public List<object> RankOptions => new List<object> { RankRepro, RankAcc };

        [UIValue("rank")]
        public string Rank
        {
            get => RankSetting.Current == RankMetric.Accuracy ? RankAcc : RankRepro;
            set { }
        }

        [UIAction("rank-changed")]
        public void RankChanged(object value)
        {
            RankSetting.Set((value as string) == RankAcc ? RankMetric.Accuracy : RankMetric.Reproducibility);
        }

        /// <summary>基準が変わった。読んである記録でメダルと Lv を出し直す。</summary>
        private void OnRankChanged()
        {
            shownMetric = RankSetting.Current;
            rows = DrillSetMedals.Evaluate(bestById);
            EvaluateOthers();
            rankDropdown?.ReceiveValue();
            if (parserParams != null) Fill();
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

            // 総合は出しているステージのもの（GUI のメダル画面と同じ）
            var shown = mode == ViewMode.Standard ? rows : ListRows;
            int level = shown.Sum(r => r.Level);
            int medals = shown.Sum(r => r.Cells.Count(c => c.Medal != Medal.None));
            int cellsTotal = shown.Sum(r => r.Cells.Count);

            if (total != null)
                total.text = string.Format(ci, "Total Lv {0} / {1}", level, shown.Sum(r => r.MaxLevel));
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

            bool custom = mode == ViewMode.Custom;
            var list = ListRows;
            int offset = ListOffset;

            if (otherPageText != null)
            {
                if (custom)
                {
                    otherPageText.text = bookBroken
                        ? "Could not read the stage file. Editing is off."
                        : "Tap + to add drills. Tap a row name to pick the row.";
                }
                else
                {
                    int drills = OtherCells.Count();
                    int pages = Math.Max(1, (otherRows.Count + OtherRows - 1) / OtherRows);
                    otherPageText.text = drills == 0
                        ? "No other drills. Make one with the grid in the Mods tab."
                        : pages == 1
                            ? string.Format(ci, allDrills ? "{0} drill(s)." : "{0} other drill(s).", drills)
                            : string.Format(ci, allDrills ? "{0} drill(s).  Page {1}/{2}" : "{0} other drill(s).  Page {1}/{2}", drills, otherPage + 1, pages);
                }
            }

            bool editable = custom && !bookBroken;
            for (int r = 0; r < OtherRows; r++)
            {
                int index = offset + r;
                var row = index < list.Count ? list[index] : null;

                var label = r < otherLabels.Count ? otherLabels[r] : null;
                if (label != null)
                {
                    Color color = custom && index == selectedRow ? Picked : Played;
                    if (row != null)
                        label.text = string.Format(ci, "{0}  <color=#8899AA><size=80%>Lv {1}/{2}</size></color>", RowName(row.Direction), row.Level, row.MaxLevel);
                    else if (editable && index == list.Count && index < CustomStage.MaxRows)
                    {
                        label.text = "+ Add row";
                        color = Empty;
                    }
                    else
                        label.text = string.Empty;

                    label.DefaultColor = color;
                    label.HighlightColor = Picked;
                    label.color = color;
                }

                var icon = r < otherIcons.Count ? otherIcons[r] : null;
                if (icon != null) SetSprite(icon, row == null || row.Sequences.Count == 0 ? null : icons?.For(row.Sequences, row.Tower));

                for (int c = 0; c < Stages; c++)
                {
                    ClickableText text;
                    if (!otherCells.TryGetValue(Key(r, c), out text)) continue;

                    if (row == null || c >= row.Cells.Count)
                    {
                        // カスタムでは行の右端に「+」（満杯なら出さない）
                        bool add = editable && row != null && c == row.Cells.Count && c < CustomStage.MaxDrillsPerRow;
                        text.text = add ? "+" : string.Empty;
                        text.DefaultColor = Empty;
                        text.HighlightColor = Picked;
                        text.color = Empty;
                        continue;
                    }

                    var cell = row.Cells[c];
                    bool picked = custom ? index == selectedRow && c == selectedColumn : cell.Id == selectedId;
                    Paint(text, cell.BestPercent, picked, center: true, label: null);
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
                         CellNumber(percent.Value);
            else
                number = "○" + CellNumber(percent.Value);

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

        /// <summary>
        /// マスの数字。小数は切り捨て。マスは「●85.7」の幅に詰めてあるので、満点（100 以上）は「100」と出す。
        /// </summary>
        private static string CellNumber(double percent)
        {
            return percent >= 100 ? "100" : ScoreFormat.Floor(percent, 1);
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
            string best = !played ? "not played"
                : RankSetting.Current == RankMetric.Accuracy ? "best ACC " + ScoreFormat.Floor(bestValue, 2) + "%"
                : "best REPRO " + ScoreFormat.Floor(bestValue, 1) + "%";
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

            var other = OtherCells.Concat(customRows.SelectMany(r => r.Cells)).FirstOrDefault(c => c.Id == selectedId);
            detail.text = other == null
                ? string.Empty
                : string.Format(ci, "{0}{1}   {2}{3}", justMade.Contains(other.SongName) ? "<color=#E0B060>NEW</color>  " : string.Empty,
                    DisplayName(other.SongName), best, medalText);
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
