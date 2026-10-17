using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using HMUI;
using JumpDrill.Model;
using JumpDrillMod.Services;
using RankMetric = JumpDrill.Replays.RankMetric;
using TMPro;
using UnityEngine;
using ScoreFormat = JumpDrill.Replays.ScoreFormat;

namespace JumpDrillMod.UI.Medals
{
    /// <summary>
    /// カスタムステージに足すドリルを選ぶ別窓。GUI の「ドリルの選択」と同じ決まりで選ばせる。
    /// </summary>
    /// <remarks>
    /// 1行は1方向。既にドリルのある行に足すときは、その行の方向のドリルだけを出す。
    /// 新しい行では何でも選べ、違う方向を混ぜたら方向ごとに行を分ける（JumpDrill.Core の <c>CustomStage.AddDrills</c>）。
    /// Low BPM / High BPM は、選んだドリル（足す先の行があればその行）の方向から、
    /// BPM の小さい方・大きい方を行の空きの数だけ取り、遅い順に並べる。
    ///
    /// 出すのは<b>読み込まれている譜面</b>（叩けるもの）だけ。Other と同じ。
    /// そのステージに既にあるドリルは、どの行にあっても出さない。
    /// 並びは作った日時の新しい順。作ってすぐ足しに来たとき、いま作ったものが一番上に来る。
    /// 作った日時は Info.dat を書いた日時（上書きで作り直せば、その日時になる）。
    /// </remarks>
    internal partial class MedalViewController
    {
        /// <summary>別窓の1ページの行数。</summary>
        private const int PickerRows = 10;

        /// <summary>別窓の幅。</summary>
        private const int PickerWidth = 96;

        /// <summary>足す先の行。行の数と同じなら新しい行。</summary>
        private int pickerTarget;

        /// <summary>足す先の行の方向。新しい行なら null。</summary>
        private string? pickerDirection;

        private List<string> pickerNames = new List<string>();

        /// <summary>曲名ごとの作った日時。</summary>
        private readonly Dictionary<string, DateTime> pickerTimes = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly HashSet<string> pickerChosen = new HashSet<string>(StringComparer.Ordinal);
        private int pickerPage;

        /// <summary>足す窓から Make drill へ行ったときの、戻り先のステージ。行っていなければ null。</summary>
        private string? makeStage;

        /// <summary>戻り先の行。</summary>
        private int makeRow;

        /// <summary>Make drill へ行った時刻。これより後に書いたドリルを、戻ったときに選んでおく。</summary>
        private DateTime makeSince;

        private readonly List<ClickableText?> pickerTexts = new List<ClickableText?>();
        private readonly List<ImageView?> pickerIcons = new List<ImageView?>();

        /// <summary>選んだドリルの色。ホバーの水色（<see cref="Picked"/>）と見分けが付くようにする。</summary>
        private static readonly Color Chosen = new Color(0.45f, 0.95f, 0.55f);

        /// <summary>件数の色（マークアップの #8899AA と同じ）と、知らせの色。</summary>
        private static readonly Color NoteColor = new Color(0.53f, 0.60f, 0.67f);
        private static readonly Color WarningColor = new Color(1f, 0.7f, 0.3f);

        [UIComponent("picker-title")] private TextMeshProUGUI? pickerTitle = null;
        [UIComponent("picker-note")] private TextMeshProUGUI? pickerNote = null;

        /// <summary>別窓のマークアップ。ルート直下に置く。</summary>
        private static void AppendPicker(StringBuilder sb)
        {
            string width = PickerWidth.ToString(CultureInfo.InvariantCulture);

            sb.Append("<modal id='picker' show-event='show-picker' hide-event='hide-picker' click-off-closes='true'");
            sb.Append(" move-to-center='true' size-delta-x='").Append(PickerWidth + 6).Append("' size-delta-y='76'>");
            sb.Append("<vertical pad='2' spacing='0.3' child-expand-height='false' child-align='UpperCenter' pref-width='")
              .Append(width).Append("'>");

            // 右上に、この行の方向でドリルを作りに行くボタン
            sb.Append("<horizontal pref-height='5'>");
            sb.Append("<text id='picker-title' text='' font-size='3.2' align='Left' pref-width='").Append(PickerWidth - 26).Append("'/>");
            sb.Append("<button text='Make drill →' on-click='picker-make' pref-width='26' pref-height='5'");
            sb.Append(" hover-hint='Make a drill of this row direction, then come back here to add it.'/>");
            sb.Append("</horizontal>");

            for (int r = 0; r < PickerRows; r++)
            {
                OpenRow(sb, 4.2);
                var text = new StringBuilder();
                Box(text, PickerWidth - IconWidth - 1, "MiddleLeft",
                    "<clickable-text tags='pick-" + r + "' text='' font-size='3' align='Left'/>", fill: true);
                Box(sb, PickerWidth, "MiddleLeft", Icon("pick-icon-" + r) + text);
                sb.Append("</horizontal>");
            }

            // ▲▼ は高さ 5 ないと記号が潰れる（表の見出しの段と同じ）
            OpenRow(sb, 6);
            sb.Append("<text id='picker-note' text='' font-size='2.6' align='Left' color='#8899AA' pref-width='")
              .Append(PickerWidth - 20).Append("'/>");
            sb.Append("<button text='▲' on-click='picker-up' pref-width='9' pref-height='5'/>");
            sb.Append("<button text='▼' on-click='picker-down' pref-width='9' pref-height='5'/>");
            sb.Append("</horizontal>");

            sb.Append("<horizontal pref-height='7' spacing='2' pad-top='1'>");
            sb.Append("<button text='Add' on-click='picker-add' pref-width='18' pref-height='6' hover-hint='Add the drills you tapped.'/>");
            sb.Append("<button text='Low BPM' on-click='picker-low' pref-width='22' pref-height='6'");
            sb.Append(" hover-hint='Add the 8 lowest-BPM drills of the tapped direction, slowest first.'/>");
            sb.Append("<button text='High BPM' on-click='picker-high' pref-width='22' pref-height='6'");
            sb.Append(" hover-hint='Add the 8 highest-BPM drills of the tapped direction, slowest first.'/>");
            sb.Append("<button text='Cancel' click-event='hide-picker' pref-width='18' pref-height='6'/>");
            sb.Append("</horizontal>");

            sb.Append("</vertical></modal>");
        }

        private void PostParsePicker()
        {
            pickerTexts.Clear();
            pickerIcons.Clear();

            for (int r = 0; r < PickerRows; r++)
            {
                var text = Tagged<ClickableText>("pick-" + r);
                pickerTexts.Add(text);
                pickerIcons.Add(Tagged<ImageView>("pick-icon-" + r));

                int row = r;
                if (text != null) text.OnClickEvent += _ => TogglePick(row);
            }
        }

        /// <summary>行の空き。新しい行なら 1 行ぶん。</summary>
        private int PickerRoom()
        {
            var stage = CurrentStage;
            if (stage == null) return 0;
            return pickerTarget < stage.Rows.Count
                ? CustomStage.MaxDrillsPerRow - stage.Rows[pickerTarget].Drills.Count
                : CustomStage.MaxDrillsPerRow;
        }

        /// <summary>別窓を開く。<paramref name="row"/> が行の数と同じなら新しい行に足す。</summary>
        /// <param name="chooseSince">これより後に作ったドリルを選んだ状態で開く（Make drill から戻ったとき）。</param>
        private void OpenPicker(int row, DateTime? chooseSince = null)
        {
            var stage = CurrentStage;
            if (stage == null || bookBroken || launcher == null || parserParams == null) return;

            pickerTarget = row;
            pickerDirection = row < stage.Rows.Count ? CustomStage.DirectionOf(stage.Rows[row]) : null;
            if (PickerRoom() <= 0) return;

            try
            {
                // このステージに既にあるドリルは出さない（同じドリルを2回足させない）。曲名の付け方が変わっても拾えるよう ID で比べる
                var placed = new HashSet<string>(
                    stage.Rows.SelectMany(r => r.Drills).Select(n => DrillNaming.ExtractId(n) ?? n),
                    StringComparer.Ordinal);

                pickerTimes.Clear();
                hiddenMade = null;
                foreach (var level in launcher.DrillLevels())
                {
                    bool hidden = (pickerDirection != null && DrillNaming.DirectionOf(level.SongName) != pickerDirection) ||
                                  placed.Contains(level.Id) || placed.Contains(level.SongName);
                    if (hidden)
                    {
                        // Make drill で作ったのに一覧に出ないもの。何も出ないと作れなかったように見えるので、訳を知らせる
                        if (chooseSince.HasValue && hiddenMade == null && CreatedAt(level.Folder) >= chooseSince.Value)
                            hiddenMade = level.SongName;
                        continue;
                    }
                    if (pickerTimes.ContainsKey(level.SongName)) continue;
                    pickerTimes[level.SongName] = CreatedAt(level.Folder);
                }

                pickerNames = pickerTimes.Keys
                    .OrderByDescending(n => pickerTimes[n])
                    .ThenBy(n => DrillNaming.DirectionOf(n), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => DrillNaming.ParseBpm(n) ?? double.MaxValue)
                    .ThenBy(n => n, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not list the drills: " + e);
                pickerNames = new List<string>();
            }

            pickerChosen.Clear();
            if (chooseSince.HasValue)
                foreach (var name in pickerNames)
                    if (pickerTimes[name] >= chooseSince.Value) pickerChosen.Add(name);
            pickerPage = 0;
            if (pickerTitle != null)
                pickerTitle.text = row < stage.Rows.Count
                    ? "Add drills to " + stage.Rows[row].Name
                    : "Add a row";

            // 足せるものが無くても開く。右上の Make drill → から作りに行ける
            // 作ったうちの一部だけが出ているときも、出ない訳を知らせる（件数はマスを触れば出る）
            string? note = hiddenMade != null ? HiddenMadeNote(stage, hiddenMade)
                : pickerNames.Count == 0 ? "No drills to add. Make one with Make drill →."
                : null;
            FillPicker(note);
            parserParams.EmitEvent("show-picker");
        }

        /// <summary>Make drill で作ったが一覧に出ないドリル（ステージに既にある・方向が違う）。無ければ null。</summary>
        private string? hiddenMade;

        /// <summary>作ったドリルが一覧に出ない訳。既にあるなら何行目か（同じ名前の行が2つあることもある）。</summary>
        private static string HiddenMadeNote(CustomStage stage, string songName)
        {
            string id = DrillNaming.ExtractId(songName) ?? songName;
            int row = stage.Rows.FindIndex(r => r.Drills.Any(n => (DrillNaming.ExtractId(n) ?? n) == id));
            return row >= 0
                ? string.Format(CultureInfo.InvariantCulture, "Already in row {0}: {1}", row + 1, DisplayName(songName))
                : "Other direction: " + DisplayName(songName);
        }

        [UIAction("picker-make")]
        public void PickerMake()
        {
            if (CurrentStage == null || onMake == null) return;

            parserParams?.EmitEvent("hide-picker");
            makeStage = customName;
            makeRow = pickerTarget;
            makeSince = DateTime.Now;
            makerOpenedAt = makeSince;
            // 作る画面を出せなければ、戻ったときに窓を開き直さない
            if (!onMake(pickerDirection)) makeStage = null;
        }

        /// <summary>譜面を作った日時。読めなければ一番古い扱いにする。</summary>
        private static DateTime CreatedAt(string? folder)
        {
            if (string.IsNullOrEmpty(folder)) return DateTime.MinValue;
            try
            {
                string info = System.IO.Path.Combine(folder, "Info.dat");
                if (System.IO.File.Exists(info)) return System.IO.File.GetLastWriteTime(info);
                if (System.IO.Directory.Exists(folder)) return System.IO.Directory.GetLastWriteTime(folder);
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn("could not read the date of " + folder + ": " + e.Message);
            }
            return DateTime.MinValue;
        }

        private void TogglePick(int row)
        {
            int index = pickerPage * PickerRows + row;
            if (index >= pickerNames.Count) return;

            string name = pickerNames[index];
            if (!pickerChosen.Remove(name)) pickerChosen.Add(name);
            FillPicker(null);
        }

        [UIAction("picker-up")]
        public void PickerUp()
        {
            if (pickerPage == 0) return;
            pickerPage--;
            FillPicker(null);
        }

        [UIAction("picker-down")]
        public void PickerDown()
        {
            if ((pickerPage + 1) * PickerRows >= pickerNames.Count) return;
            pickerPage++;
            FillPicker(null);
        }

        [UIAction("picker-add")]
        public void PickerAdd()
        {
            var chosen = pickerNames.Where(pickerChosen.Contains).ToList();
            if (chosen.Count == 0)
            {
                FillPicker("No drills picked. Tap drills to pick them, then Add.");
                return;
            }
            AddPicked(chosen);
        }

        [UIAction("picker-low")]
        public void PickerLow() => PickBySpeed(highest: false);

        [UIAction("picker-high")]
        public void PickerHigh() => PickBySpeed(highest: true);

        /// <summary>方向（行の方向、無ければ最初に選んだドリルの方向）から BPM の小さい・大きい方を取る。</summary>
        private void PickBySpeed(bool highest)
        {
            string? direction = pickerDirection ??
                pickerNames.Where(pickerChosen.Contains).Select(DrillNaming.DirectionOf).FirstOrDefault();
            if (string.IsNullOrEmpty(direction))
            {
                FillPicker("Tap a drill to choose the direction.");
                return;
            }

            var picked = CustomStage.PickBySpeed(pickerNames, direction!, highest, Math.Max(1, PickerRoom()));
            if (picked.Count == 0)
            {
                FillPicker("No drills of this direction have a BPM in the name.");
                return;
            }
            AddPicked(picked);
        }

        private void AddPicked(List<string> picked)
        {
            var stage = CurrentStage;
            if (stage == null) return;

            int skipped = stage.AddDrills(pickerTarget, picked);
            parserParams?.EmitEvent("hide-picker");

            selectedRow = Math.Min(pickerTarget, stage.Rows.Count - 1);
            selectedColumn = -1;

            // 足したドリルの最初の1本を選んでおく（右に記録が出て、そのまま Play できる）。
            // 新しい行に足すと、同じ方向の行の空きに入ったり方向ごとに行が分かれたりするので、足した先は名前で探す
            bool found = false;
            foreach (string name in picked)
            {
                for (int r = 0; r < stage.Rows.Count && !found; r++)
                {
                    int c = stage.Rows[r].Drills.IndexOf(name);
                    if (c < 0) continue;
                    selectedRow = r;
                    selectedColumn = c;
                    found = true;
                }
                if (found) break;
            }

            Edited();

            if (found && selectedRow < customRows.Count && selectedColumn < customRows[selectedRow].Cells.Count)
            {
                selectedId = customRows[selectedRow].Cells[selectedColumn].Id;
                Fill();
                onSelect?.Invoke(selectedId);
            }

            SetStatus(skipped > 0
                ? string.Format(CultureInfo.InvariantCulture,
                    "A stage holds up to {0} rows x {1} drills. {2} drill(s) were not added.",
                    CustomStage.MaxRows, CustomStage.MaxDrillsPerRow, skipped)
                : string.Empty);
        }

        /// <summary>別窓の一覧を塗る。<paramref name="note"/> があれば件数の代わりに出す。</summary>
        private void FillPicker(string? note)
        {
            var ci = CultureInfo.InvariantCulture;

            for (int r = 0; r < PickerRows; r++)
            {
                int index = pickerPage * PickerRows + r;
                var text = r < pickerTexts.Count ? pickerTexts[r] : null;
                var icon = r < pickerIcons.Count ? pickerIcons[r] : null;

                if (index >= pickerNames.Count)
                {
                    if (text != null) text.text = string.Empty;
                    if (icon != null) SetSprite(icon, null);
                    continue;
                }

                string name = pickerNames[index];
                if (icon != null)
                {
                    var sequences = DrillNaming.ParseSequences(name);
                    SetSprite(icon, sequences.Count == 0 ? null : icons?.For(sequences, DrillNaming.IsTower(name)));
                }

                if (text != null)
                {
                    bool chosen = pickerChosen.Contains(name);
                    string shown = DisplayName(name);
                    DateTime created;
                    if (pickerTimes.TryGetValue(name, out created) && created != DateTime.MinValue)
                        shown = "<color=#8899AA>" + created.ToString("yyyy/MM/dd HH:mm", ci) + "</color>  " + shown;
                    double best;
                    string id = DrillNaming.ExtractId(name) ?? string.Empty;
                    if (bestById.TryGetValue(id, out best))
                        shown += RankSetting.Current == RankMetric.Accuracy
                            ? "   <color=#8899AA>best ACC " + ScoreFormat.Floor(best, 2) + "%</color>"
                            : "   <color=#8899AA>best REPRO " + ScoreFormat.Floor(best, 1) + "%</color>";

                    text.text = chosen ? "[" + shown + "]" : shown;
                    // 選んだ行はホバー（水色）と違う色にする。同じ色だと選べたのか分からない
                    Color color = chosen ? Chosen : Played;
                    text.DefaultColor = color;
                    text.HighlightColor = Picked;
                    text.color = color;
                }
            }

            if (pickerNote != null)
            {
                int pages = Math.Max(1, (pickerNames.Count + PickerRows - 1) / PickerRows);
                pickerNote.text = note ?? string.Format(ci, "{0} drill(s), {1} picked.  Page {2}/{3}",
                    pickerNames.Count, pickerChosen.Count, pickerPage + 1, pages);
                // 足せなかったときの知らせは件数と同じ灰色だと気付かないので、目立つ色にする
                pickerNote.color = note == null ? NoteColor : WarningColor;
            }
        }
    }
}
