using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Components.Settings;
using HMUI;
using JumpDrill.Model;
using JumpDrill.Replays;
using UnityEngine;

namespace JumpDrillMod.UI.Medals
{
    /// <summary>
    /// Jump Drill 画面のステージの切り替えと、カスタムステージの組み替え。
    /// </summary>
    /// <remarks>
    /// カスタムステージは GUI のメダル画面と同じもの（JumpDrill.Core の <c>CustomStageBook</c>）で、
    /// 同じ stages.json を読み書きする。どちらで組んだステージも両方に出る。
    ///
    /// VR では文字を打つのが面倒なので、作るときは名前を聞かずに CUSTOM1、CUSTOM2… と付け、
    /// あとから Rename（画面のキーボード）で変えられるようにする。
    /// GUI の右クリックのメニューの代わりに、マスか行を選んでから下のボタンで組み替える。
    /// </remarks>
    internal partial class MedalViewController
    {
        private enum ViewMode { Standard, Other, Custom }

        private ViewMode mode = ViewMode.Standard;

        /// <summary>出しているカスタムステージの名前。</summary>
        private string? customName;

        private CustomStageBook book = new CustomStageBook();

        /// <summary>stages.json が読めなかった。上書きして中身を失わないよう、組み替えさせない。</summary>
        private bool bookBroken;

        /// <summary>出しているカスタムステージの成績。</summary>
        private List<MedalRow> customRows = new List<MedalRow>();

        /// <summary>カスタムステージで選んでいる行とマス。マスを選んでいなければ列は -1。</summary>
        private int selectedRow = -1;
        private int selectedColumn = -1;

        private const string StandardOption = "Standard";
        private const string OtherOption = "Other";
        private const string AllOption = "All";

        /// <summary>
        /// Other の表を All（カスタムステージに入っているものも含めて、ドリルセット以外の全部）で出しているか。
        /// 表の作りは Other と同じなので、ViewMode は Other のままこの印で分ける。
        /// </summary>
        private bool allDrills;

        [UIObject("page-buttons")] private GameObject? pageButtons = null;
        [UIObject("stage-buttons")] private GameObject? stageButtons = null;
        [UIObject("generate-row")] private GameObject? generateRow = null;
        [UIObject("edit-row")] private GameObject? editRow = null;
        [UIComponent("stage-dropdown")] private DropDownListSetting? stageDropdown = null;
        [UIComponent("keyboard")] private ModalKeyboard? keyboard = null;

        private CustomStage? CurrentStage => mode == ViewMode.Custom && customName != null ? book.Find(customName) : null;

        // ───────── マークアップ ─────────

        /// <summary>カスタムステージの組み替えのボタン。マスを選んで ◀ ▶ Remove、行を選んで Row ▲ ▼ Rename Delete。</summary>
        private static void AppendEditRow(StringBuilder sb)
        {
            sb.Append("<horizontal id='edit-row' active='false' pref-height='6' spacing='1'>");
            Button(sb, "Remove", "cell-remove", 15, "Remove the selected drill from this stage. The map and its records are kept.");
            Button(sb, "◀", "cell-left", 8, "Move the selected drill left.");
            Button(sb, "▶", "cell-right", 8, "Move the selected drill right.");
            Button(sb, "Row ▲", "row-up", 13, "Move the selected row up.");
            Button(sb, "Row ▼", "row-down", 13, "Move the selected row down.");
            Button(sb, "Rename row", "row-rename", 20, "Rename the selected row.");
            Button(sb, Danger("Delete row"), "row-delete", 20, "Delete the selected row. Maps and records are kept.");
            sb.Append("</horizontal>");
        }

        /// <summary>
        /// Other とカスタムステージで、選んだドリルの譜面そのものを消すボタン。2回押す。
        /// 組み替えのボタンの段には入りきらないので、段を分ける。
        /// </summary>
        private static void AppendDrillRow(StringBuilder sb)
        {
            sb.Append("<horizontal id='drill-row' active='false' pref-height='6' spacing='1'>");
            Button(sb, "Rebuild", "drill-rebuild", 20,
                "Write the selected drill again with the same settings. Records are kept.");
            Button(sb, "Rebuild all", "drill-rebuild-all", 26,
                "Write every drill in this table again with the same settings. Records are kept.");
            Button(sb, Danger("Delete drill"), "drill-delete", 24,
                "Delete the map of the selected drill. Records are kept. It is also removed from custom stages.");
            sb.Append("</horizontal>");
        }

        [UIObject("drill-row")] private GameObject? drillRow = null;

        /// <summary>選んでいるマスの曲名。カスタムでは行と列で、Other・All では ID で選んでいる。</summary>
        private string? SelectedSongName()
        {
            if (mode == ViewMode.Custom)
            {
                var row = SelectedRow();
                return row != null && HasSelectedCell(row) ? row.Drills[selectedColumn] : null;
            }
            return selectedId == null ? null
                : ListRows.SelectMany(r => r.Cells).FirstOrDefault(c => c.Id == selectedId)?.SongName;
        }

        /// <summary>選んだドリルを同じ指定で作り直す。</summary>
        [UIAction("drill-rebuild")]
        public void RebuildDrill()
        {
            string? songName = SelectedSongName();
            if (songName == null)
            {
                SetStatus("Pick a drill first.");
                return;
            }
            Rebuild(new[] { songName });
        }

        /// <summary>いま出している表のドリルを全部、同じ指定で作り直す。</summary>
        [UIAction("drill-rebuild-all")]
        public void RebuildAll() => Rebuild(ListRows.SelectMany(r => r.Cells).Select(c => c.SongName).ToList());

        /// <summary>
        /// ドリルを作り直す（JumpDrill.Core の <c>DrillRebuild</c>）。同じ ID の譜面が置き場所ごとにあれば全部。
        /// 生成の決まりを変えたあとで、今の決まりで書き直すのに使う。
        /// </summary>
        private void Rebuild(IReadOnlyList<string> songNames)
        {
            if (launcher == null || drillSet == null) return;

            var folders = songNames
                .Select(n => DrillNaming.ExtractId(n))
                .Where(id => id != null)
                .Distinct()
                .SelectMany(id => launcher.FoldersOf(id!))
                .ToList();
            if (folders.Count == 0)
            {
                SetStatus("No maps to rebuild.");
                return;
            }

            SetStatus("Rebuilding...");
            drillSet.Rebuild(folders,
                (done, total) => SetStatus(string.Format(CultureInfo.InvariantCulture, "Rebuilding... {0}/{1}", done, total)),
                (ok, message, renamed) =>
                {
                    // 曲名を今の書き方に付け替えたものは、カスタムステージの曲名も付け替える
                    if (!bookBroken && book.RenameDrills(renamed)) SaveBook();
                    SetStatus(message);
                    LoadOthers();
                    Fill();
                });
        }

        /// <summary>選んだドリルの譜面を消す。どのカスタムステージからも外す。</summary>
        [UIAction("drill-delete")]
        public void DeleteDrill()
        {
            if (launcher == null || drillSet == null) return;

            string? songName = SelectedSongName();
            string? id = songName == null ? null : DrillNaming.ExtractId(songName);
            if (songName == null || id == null)
            {
                SetStatus("Pick a drill first.");
                return;
            }

            // 押したらすぐ消す（2回押す形は慣れないとのことでやめた、ユーザー指定）。記録は残る

            var folders = launcher.FoldersOf(id);
            if (!bookBroken)
            {
                bool changed = false;
                foreach (var stage in book.Stages)
                    foreach (var row in stage.Rows)
                        changed |= row.Drills.RemoveAll(d => d == songName) > 0;
                if (changed) SaveBook();
            }

            selectedColumn = -1;
            selectedId = null;
            SetStatus("Deleting the drill...");
            drillSet.Delete(folders, (ok, message) =>
            {
                SetStatus(message);
                LoadOthers();
                EvaluateOthers();
                Fill();
            });
        }

        private static void Button(StringBuilder sb, string text, string action, int width, string hint)
        {
            sb.Append("<button text='").Append(text).Append("' on-click='").Append(action)
              .Append("' pref-width='").Append(width).Append("' pref-height='6' hover-hint='").Append(hint).Append("'/>");
        }

        // ───────── 組み上がった後・読み書き ─────────

        /// <summary>前に見ていたステージに戻す。プルダウンの中身は Setup の時点で読んだファイルから作ってある。</summary>
        private void PostParseCustom()
        {
            PostParsePicker();

            string saved = PluginConfig.Instance?.MedalStage ?? string.Empty;
            if (saved == "other" || saved == "all")
            {
                mode = ViewMode.Other;
                allDrills = saved == "all";
            }
            else if (saved.StartsWith("custom:", StringComparison.Ordinal) && book.Find(saved.Substring(7)) != null)
            {
                mode = ViewMode.Custom;
                customName = saved.Substring(7);
            }

            EvaluateOthers();
            RefreshDropdown();
        }

        /// <summary>stages.json を読む。出していたカスタムステージが消えていたら Standard に戻す。</summary>
        private void LoadBook()
        {
            try
            {
                CustomStageBook.MoveLegacy(StagesPath);
                book = CustomStageBook.Load(StagesPath);
                bookBroken = false;
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not read the stage file: " + e);
                book = new CustomStageBook();
                bookBroken = true;
            }

            if (mode == ViewMode.Custom && CurrentStage == null)
            {
                mode = ViewMode.Standard;
                customName = null;
                ApplyView();
            }
            RefreshDropdown();
        }

        /// <summary>このインスタンスの <c>UserData\JumpDrill\stages.json</c>。</summary>
        private static string StagesPath => CustomStageBook.PathFor(InstallPaths.Root);

        /// <summary>stages.json に書く。書けなければ知らせて false。</summary>
        private bool SaveBook()
        {
            try
            {
                book.Save(StagesPath);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not save the stage file: " + e);
                SetStatus("Could not save the stage. See the log.");
                return false;
            }
        }

        /// <summary>組み替えたら書いて出し直す。</summary>
        private void Edited()
        {
            SaveBook();
            EvaluateOthers();
            Fill();
        }

        // ───────── ステージの切り替え ─────────

        [UIValue("stage-options")]
        public List<object> StageOptions
        {
            get
            {
                var options = new List<object> { StandardOption, OtherOption, AllOption };
                options.AddRange(book.Stages.Select(s => (object)s.Name));
                return options;
            }
        }

        [UIValue("stage")]
        public string Stage
        {
            get => mode == ViewMode.Standard ? StandardOption
                : mode == ViewMode.Other ? (allDrills ? AllOption : OtherOption)
                : customName ?? StandardOption;
            set { }
        }

        /// <summary>プルダウンの中身と選んでいるものを今の状態に合わせる。</summary>
        private void RefreshDropdown()
        {
            if (stageDropdown == null) return;
            stageDropdown.Values = StageOptions;
            stageDropdown.UpdateChoices();
            stageDropdown.ReceiveValue();
        }

        [UIAction("stage-changed")]
        public void StageChanged(object value)
        {
            string label = value as string ?? StandardOption;
            int index = StageOptions.IndexOf(label);

            SetMode(index <= 0 ? ViewMode.Standard : index <= 2 ? ViewMode.Other : ViewMode.Custom,
                index > 2 ? label : null, all: index == 2);
        }

        private void SetMode(ViewMode newMode, string? name, bool all = false)
        {
            mode = newMode;
            allDrills = newMode == ViewMode.Other && all;
            customName = newMode == ViewMode.Custom ? name : null;
            selectedRow = selectedColumn = -1;

            if (PluginConfig.Instance != null)
                PluginConfig.Instance.MedalStage = mode == ViewMode.Other ? (allDrills ? "all" : "other")
                    : mode == ViewMode.Custom ? "custom:" + name : string.Empty;

            EvaluateOthers();

            // Other を開いたとき何も選んでいなければ、いちばん新しく作ったドリルを選んで見せる
            if (mode == ViewMode.Other && (selectedId == null || !OtherCells.Any(c => c.Id == selectedId)) && RevealNewest())
                return;
            ApplyView();
        }

        /// <summary>カスタムステージを作る。名前は CUSTOM1、CUSTOM2… で、あとから変えられる。</summary>
        [UIAction("stage-new")]
        public void NewStage()
        {
            if (bookBroken)
            {
                SetStatus("Could not read the stage file. Editing is off.");
                return;
            }

            string name = book.NextDefaultName();
            book.Stages.Add(new CustomStage { Name = name });
            if (!SaveBook()) return;

            SetMode(ViewMode.Custom, name);
            RefreshDropdown();
            SetStatus(name + " was made. Tap + Add row to add drills.");
        }

        [UIAction("stage-rename")]
        public void RenameStage()
        {
            var stage = CurrentStage;
            if (stage == null || bookBroken) return;
            AskName(stage.Name, name =>
            {
                if (name == stage.Name) return;
                // GUI の日本語の名前も使わせない（同じ stages.json を GUI でも開く）
                if (name == StandardOption || name == OtherOption || name == AllOption ||
                    name == "標準" || name == "その他" || name == "すべて" || book.Find(name) != null)
                {
                    SetStatus("A stage with this name already exists.");
                    return;
                }

                stage.Name = name;
                if (!SaveBook()) return;
                SetMode(ViewMode.Custom, name);
                RefreshDropdown();
            });
        }

        [UIAction("stage-delete")]
        public void DeleteStage()
        {
            var stage = CurrentStage;
            if (stage == null || bookBroken) return;

            // 押したらすぐ消す（2回押す形は慣れないとのことでやめた、ユーザー指定）。譜面と記録は残る
            book.Stages.Remove(stage);
            if (!SaveBook()) return;
            SetMode(ViewMode.Standard, null);
            RefreshDropdown();
            SetStatus(stage.Name + " was deleted.");
        }

        // ───────── 組み替え ─────────

        /// <summary>選んでいる行。無ければ null。</summary>
        private CustomStageRow? SelectedRow()
        {
            var stage = CurrentStage;
            if (stage == null || bookBroken) return null;
            if (selectedRow < 0 || selectedRow >= stage.Rows.Count)
            {
                SetStatus("Tap a row name or a drill first.");
                return null;
            }
            return stage.Rows[selectedRow];
        }

        private bool HasSelectedCell(CustomStageRow row)
        {
            if (selectedColumn >= 0 && selectedColumn < row.Drills.Count) return true;
            SetStatus("Tap a drill first.");
            return false;
        }

        [UIAction("cell-left")]
        public void CellLeft() => MoveCell(-1);

        [UIAction("cell-right")]
        public void CellRight() => MoveCell(1);

        private void MoveCell(int step)
        {
            var row = SelectedRow();
            if (row == null || !HasSelectedCell(row)) return;

            int to = selectedColumn + step;
            if (to < 0 || to >= row.Drills.Count) return;

            Swap(row.Drills, selectedColumn, to);
            selectedColumn = to;
            Edited();
        }

        [UIAction("cell-remove")]
        public void RemoveCell()
        {
            var row = SelectedRow();
            if (row == null || !HasSelectedCell(row)) return;

            row.Drills.RemoveAt(selectedColumn);
            selectedColumn = -1;
            Edited();
        }

        [UIAction("row-up")]
        public void RowUp() => MoveRow(-1);

        [UIAction("row-down")]
        public void RowDown() => MoveRow(1);

        private void MoveRow(int step)
        {
            var stage = CurrentStage;
            if (SelectedRow() == null || stage == null) return;

            int to = selectedRow + step;
            if (to < 0 || to >= stage.Rows.Count) return;

            Swap(stage.Rows, selectedRow, to);
            selectedRow = to;
            Edited();
        }

        [UIAction("row-rename")]
        public void RenameRow()
        {
            var row = SelectedRow();
            if (row == null) return;

            AskName(row.Name, name =>
            {
                row.Name = name;
                Edited();
            });
        }

        [UIAction("row-delete")]
        public void DeleteRow()
        {
            var stage = CurrentStage;
            var row = SelectedRow();
            if (row == null || stage == null) return;

            // 押したらすぐ消す（2回押す形は慣れないとのことでやめた、ユーザー指定）。ドリルの譜面と記録は残る
            stage.Rows.Remove(row);
            selectedRow = selectedColumn = -1;
            SetStatus(row.Name + " was deleted.");
            Edited();
        }

        private static void Swap<T>(List<T> list, int a, int b)
        {
            T t = list[a];
            list[a] = list[b];
            list[b] = t;
        }

        // ───────── 名前の入力 ─────────

        /// <summary>キーボードで入れた名前を渡す先。</summary>
        private Action<string>? onName;

        [UIValue("keyboard-text")]
        public string KeyboardText { get; set; } = string.Empty;

        /// <summary>画面のキーボードを出して名前を入れさせる。空なら何もしない。</summary>
        private void AskName(string current, Action<string> done)
        {
            if (keyboard == null || parserParams == null) return;

            onName = done;
            KeyboardText = current;
            keyboard.SetText(current);
            parserParams.EmitEvent("show-keyboard");
        }

        [UIAction("keyboard-enter")]
        public void KeyboardEnter(string value)
        {
            var done = onName;
            onName = null;

            string name = (value ?? string.Empty).Trim();
            if (name.Length == 0 || done == null) return;
            done(name);
        }
    }
}
