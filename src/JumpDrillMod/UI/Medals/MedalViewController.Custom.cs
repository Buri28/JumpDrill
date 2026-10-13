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

        /// <summary>
        /// 1回押した削除。もう1回押すと消す（"stage" か "row"）。ほかの操作をしたら取り消す。
        /// </summary>
        private string? deleteArmed;

        private const string StandardOption = "Standard";
        private const string OtherOption = "Other";

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
            Button(sb, "Delete row", "row-delete", 20, "Delete the selected row. Press twice if it has drills.");
            sb.Append("</horizontal>");
        }

        /// <summary>
        /// Other とカスタムステージで、選んだドリルの譜面そのものを消すボタン。2回押す。
        /// 組み替えのボタンの段には入りきらないので、段を分ける。
        /// </summary>
        private static void AppendDrillRow(StringBuilder sb)
        {
            sb.Append("<horizontal id='drill-row' active='false' pref-height='6' spacing='1'>");
            Button(sb, "Delete drill", "drill-delete", 24,
                "Delete the map of the selected drill. Press twice. Records are kept. It is also removed from custom stages.");
            sb.Append("</horizontal>");
        }

        [UIObject("drill-row")] private GameObject? drillRow = null;

        /// <summary>選んだドリルの譜面を消す。どのカスタムステージからも外す。</summary>
        [UIAction("drill-delete")]
        public void DeleteDrill()
        {
            if (launcher == null || drillSet == null) return;

            // 選んでいるマスの曲名。カスタムでは行と列で、Other では ID で選んでいる
            string? songName = null;
            if (mode == ViewMode.Custom)
            {
                var row = SelectedRow();
                if (row != null && HasSelectedCell(row)) songName = row.Drills[selectedColumn];
            }
            else if (selectedId != null)
            {
                songName = ListRows.SelectMany(r => r.Cells).FirstOrDefault(c => c.Id == selectedId)?.SongName;
            }

            string? id = songName == null ? null : DrillNaming.ExtractId(songName);
            if (songName == null || id == null)
            {
                SetStatus("Pick a drill first.");
                return;
            }

            if (deleteArmed != "drill:" + id)
            {
                deleteArmed = "drill:" + id;
                SetStatus("Press Delete drill again to delete the map of " + DisplayName(songName) + ". Records are kept.");
                return;
            }
            deleteArmed = null;

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
            if (saved == "other") mode = ViewMode.Other;
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
                book = CustomStageBook.Load(CustomStageBook.DefaultPath());
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

        /// <summary>stages.json に書く。書けなければ知らせて false。</summary>
        private bool SaveBook()
        {
            try
            {
                book.Save(CustomStageBook.DefaultPath());
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
                var options = new List<object> { StandardOption, OtherOption };
                options.AddRange(book.Stages.Select(s => (object)s.Name));
                return options;
            }
        }

        [UIValue("stage")]
        public string Stage
        {
            get => mode == ViewMode.Standard ? StandardOption : mode == ViewMode.Other ? OtherOption : customName ?? StandardOption;
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

            SetMode(index <= 0 ? ViewMode.Standard : index == 1 ? ViewMode.Other : ViewMode.Custom,
                index > 1 ? label : null);
        }

        private void SetMode(ViewMode newMode, string? name)
        {
            mode = newMode;
            customName = newMode == ViewMode.Custom ? name : null;
            selectedRow = selectedColumn = -1;
            DisarmDelete();

            if (PluginConfig.Instance != null)
                PluginConfig.Instance.MedalStage = mode == ViewMode.Other ? "other" : mode == ViewMode.Custom ? "custom:" + name : string.Empty;

            EvaluateOthers();
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
            DisarmDelete();
            AskName(stage.Name, name =>
            {
                if (name == stage.Name) return;
                if (name == StandardOption || name == OtherOption || book.Find(name) != null)
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

            if (deleteArmed != "stage")
            {
                deleteArmed = "stage";
                SetStatus("Press Delete again to delete " + stage.Name + ". Maps and records are kept.");
                return;
            }

            book.Stages.Remove(stage);
            if (!SaveBook()) return;
            SetMode(ViewMode.Standard, null);
            RefreshDropdown();
            SetStatus(stage.Name + " was deleted.");
        }

        private void DisarmDelete()
        {
            if (deleteArmed == null) return;
            deleteArmed = null;
            SetStatus(string.Empty);
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
            DisarmDelete();
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
            DisarmDelete();
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
            DisarmDelete();
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
            DisarmDelete();
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

            if (row.Drills.Count > 0 && deleteArmed != "row")
            {
                deleteArmed = "row";
                SetStatus("Press Delete row again to delete " + row.Name + ".");
                return;
            }

            deleteArmed = null;
            stage.Rows.Remove(row);
            selectedRow = selectedColumn = -1;
            SetStatus(string.Empty);
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
