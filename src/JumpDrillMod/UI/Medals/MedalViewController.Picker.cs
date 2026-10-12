using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using HMUI;
using JumpDrill.Model;
using TMPro;
using UnityEngine;

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
        private readonly HashSet<string> pickerChosen = new HashSet<string>(StringComparer.Ordinal);
        private int pickerPage;

        private readonly List<ClickableText?> pickerTexts = new List<ClickableText?>();
        private readonly List<ImageView?> pickerIcons = new List<ImageView?>();

        [UIComponent("picker-title")] private TextMeshProUGUI? pickerTitle = null;
        [UIComponent("picker-note")] private TextMeshProUGUI? pickerNote = null;

        /// <summary>別窓のマークアップ。ルート直下に置く。</summary>
        private static void AppendPicker(StringBuilder sb)
        {
            string width = PickerWidth.ToString(CultureInfo.InvariantCulture);

            sb.Append("<modal id='picker' show-event='show-picker' hide-event='hide-picker' click-off-closes='true'");
            sb.Append(" move-to-center='true' size-delta-x='").Append(PickerWidth + 6).Append("' size-delta-y='74'>");
            sb.Append("<vertical pad='2' spacing='0.3' child-expand-height='false' child-align='UpperCenter' pref-width='")
              .Append(width).Append("'>");

            sb.Append("<horizontal pref-height='5'>");
            sb.Append("<text id='picker-title' text='' font-size='3.2' align='Left' pref-width='").Append(width).Append("'/>");
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

            OpenRow(sb, 4.5);
            sb.Append("<text id='picker-note' text='' font-size='2.6' align='Left' color='#8899AA' pref-width='")
              .Append(PickerWidth - 20).Append("'/>");
            sb.Append("<button text='▲' on-click='picker-up' pref-width='9' pref-height='4'/>");
            sb.Append("<button text='▼' on-click='picker-down' pref-width='9' pref-height='4'/>");
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
        private void OpenPicker(int row)
        {
            var stage = CurrentStage;
            if (stage == null || bookBroken || launcher == null || parserParams == null) return;

            pickerTarget = row;
            pickerDirection = row < stage.Rows.Count ? CustomStage.DirectionOf(stage.Rows[row]) : null;
            if (PickerRoom() <= 0) return;

            try
            {
                pickerNames = launcher.DrillLevels()
                    .Select(d => d.SongName)
                    .Where(n => pickerDirection == null || DrillNaming.DirectionOf(n) == pickerDirection)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(n => DrillNaming.DirectionOf(n), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => DrillNaming.ParseBpm(n) ?? double.MaxValue)
                    .ThenBy(n => n, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not list the drills: " + e);
                pickerNames = new List<string>();
            }

            if (pickerNames.Count == 0)
            {
                SetStatus(pickerDirection == null
                    ? "No drills are loaded. Make one with the grid in the Mods tab."
                    : "No " + pickerDirection + " drills are loaded.");
                return;
            }

            pickerChosen.Clear();
            pickerPage = 0;
            if (pickerTitle != null)
                pickerTitle.text = row < stage.Rows.Count
                    ? "Add drills to " + stage.Rows[row].Name
                    : "Add a row";

            FillPicker(null);
            parserParams.EmitEvent("show-picker");
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
                FillPicker("Tap drills to pick them.");
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
            Edited();

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
                    double best;
                    string id = DrillNaming.ExtractId(name) ?? string.Empty;
                    if (bestById.TryGetValue(id, out best)) shown += string.Format(ci, "   <color=#8899AA>best {0:0.0}%</color>", best);

                    text.text = chosen ? "[" + shown + "]" : shown;
                    Color color = chosen ? Picked : Played;
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
            }
        }
    }
}
