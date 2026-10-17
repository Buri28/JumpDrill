using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using JumpDrill.Model;
using JumpDrill.Serialization;

namespace JumpDrill.Output
{
    /// <summary>
    /// 書き出したドリルを、同じ指定で作り直す（メダル画面の「ドリルを再作成」）。
    /// 生成の決まり（クリックやカウントインの鳴らし方など）を変えたあとで、今の決まりで書き直すのに使う。
    /// </summary>
    /// <remarks>
    /// 指定は譜面から読み戻す。
    /// <list type="bullet">
    /// <item>遷移・切る方向・タワー・配置・フリック・BPM・尺・セット数は曲名から（ID もここから決まる）。</item>
    /// <item>NJS・オフセット（ジャンプ距離）・譜面の BPM は Info.dat から。</item>
    /// <item>クリック・カウントイン・末尾の余白・ブロックの間は、Info.dat の <c>_jumpDrill</c> に書いてあればそこから。
    ///   それより前に作った譜面には無いので既定値（Down・8 発・1.5 秒）で作る。</item>
    /// </list>
    /// 標準のステージのドリルは、指定を持っているのでそちらを使う。
    /// 曲名は元のまま使う（名前と ID が変わると記録が切れ、フォルダも別になる）。
    /// ただし以前の書き方の alt の名前（BPM が片手の速さ）は、今の書き方（<see cref="DrillNaming.CurrentName"/>）に付け替え、
    /// フォルダも今の名前のものに移す。ID は変わらないので記録は続く。カスタムステージの曲名は呼んだ側が
    /// <see cref="RebuildResult.OldName"/> / <see cref="RebuildResult.NewName"/> で付け替える。
    /// 記録に残る以前の曲名からは <see cref="DrillLibrary.FindByName(string, bool, string, string[])"/> が今の名前でも探す。
    /// </remarks>
    public static class DrillRebuild
    {
        /// <summary>Info.dat の難易度の <c>_customData</c> に書く、曲名に入らない指定の置き場の名前。</summary>
        public const string CustomDataKey = "_jumpDrill";

        /// <summary>譜面フォルダから指定を読み戻す。読めなければ理由付きの例外。</summary>
        public static DrillOptions Read(string folder)
        {
            string info = Path.Combine(folder, "Info.dat");
            var root = JsonReader.Parse(File.ReadAllText(info, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) throw new FormatException(Lang.T("Info.dat を読めません。", "Could not read Info.dat."));

            string songName = root.TryGetValue("_songName", out var n) ? n as string : null;
            if (string.IsNullOrEmpty(songName)) throw new FormatException(Lang.T("曲名がありません。", "The map has no song name."));

            // 標準のステージのものは指定をそのまま使う
            string id = DrillNaming.ExtractId(songName);
            var entry = id == null ? null : DrillSet.All().FirstOrDefault(e => DrillNaming.ExtractId(e.Options.Name) == id);
            if (entry != null) return entry.Options;

            var shape = DrillShape.Parse(DrillNaming.DirectionOf(songName));
            double? bpm = DrillNaming.ParseBpm(songName);
            if (shape == null || !bpm.HasValue)
                throw new FormatException(Lang.T("曲名から指定を読めません: ", "Could not read the settings from the name: ") + songName);

            double seconds;
            int sets;
            if (!ParseLength(songName, out seconds, out sets))
                throw new FormatException(Lang.T("曲名から尺を読めません: ", "Could not read the length from the name: ") + songName);

            var options = new DrillOptions
            {
                Sequences = shape.Sequences,
                Direction = shape.Direction,
                Tower = shape.Tower,
                HandPattern = shape.HandPattern,
                FlickPercent = shape.FlickPercent,
                // 名前の BPM はノーツ間隔を 1/2 で数えた値（以前の alt の書き方は ParseBpm がそろえる）
                IntervalMs = Tempo.HandIntervalMsFromEbpm(bpm.Value),
                NotesPerBeat = 2.0,
                DurationSeconds = seconds,
                Sets = sets,
                Name = songName,
            };

            if (root.TryGetValue("_beatsPerMinute", out var b) && b is double infoBpm && infoBpm > 0) options.Bpm = infoBpm;

            var difficulty = FirstDifficulty(root);
            if (difficulty != null)
            {
                if (difficulty.TryGetValue("_noteJumpMovementSpeed", out var njs) && njs is double njsValue && njsValue > 0)
                    options.Njs = njsValue;
                if (difficulty.TryGetValue("_noteJumpStartBeatOffset", out var offset) && offset is double offsetValue)
                    options.NoteJumpStartBeatOffset = offsetValue;

                var custom = (difficulty.TryGetValue("_customData", out var c) ? c : null) as Dictionary<string, object>;
                var saved = (custom != null && custom.TryGetValue(CustomDataKey, out var j) ? j : null) as Dictionary<string, object>;
                if (saved != null) ApplySaved(options, saved);
            }

            return options;
        }

        /// <summary>作り直した結果。</summary>
        public sealed class RebuildResult
        {
            /// <summary>書いたフォルダ。名前を付け替えたときは新しいフォルダ（元のフォルダは消してある）。</summary>
            public string Folder { get; internal set; }
            public string OldName { get; internal set; }
            public string NewName { get; internal set; }
            public bool Renamed { get { return OldName != NewName; } }
        }

        /// <summary>読み戻した指定で書き直す。</summary>
        public static RebuildResult Rewrite(string folder, LevelWriteOptions writeOptions = null)
        {
            var options = Read(folder);
            string oldName = options.Name;
            options.Name = DrillNaming.CurrentName(oldName);
            var map = Generation.DrillGenerator.Generate(options);

            // 作った日時（Info.dat の更新時刻）は変えない。作ったばかりの印（NEW）や新しい順の並びが、
            // まとめて作り直しただけで全部変わってしまうため
            string info = Path.Combine(folder, "Info.dat");
            DateTime written = File.GetLastWriteTime(info);

            var write = writeOptions ?? new LevelWriteOptions();
            write.Overwrite = true;
            string full = Path.GetFullPath(folder).TrimEnd('\\', '/');
            string parent = Path.GetDirectoryName(full);
            // 名前を付け替えるときは、フォルダも新しい名前のものにする（名前で探す処理はフォルダ名から当たりを付ける）
            string folderName = options.Name == oldName ? Path.GetFileName(full) : LevelWriter.SanitizeFolderName(options.Name);
            string result = LevelWriter.Write(map, parent, folderName, write);

            File.SetLastWriteTime(Path.Combine(result, "Info.dat"), written);

            if (!string.Equals(Path.GetFullPath(result).TrimEnd('\\', '/'), full, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(full, true);

            return new RebuildResult { Folder = result, OldName = oldName, NewName = options.Name };
        }

        /// <summary>曲名に入らない指定を Info.dat に書く（<see cref="Read"/> で読み戻す）。</summary>
        internal static void WriteSaved(JsonWriter w, DrillOptions options)
        {
            w.Name(CustomDataKey).StartObject();
            w.Property("intervalMs", options.IntervalMs);
            w.Property("notesPerBeat", options.NotesPerBeat);
            w.Property("click", options.Click.ToString());
            w.Property("countIn", options.CountInClicks);
            w.Property("tail", options.TailSeconds);
            w.Property("minLeadIn", options.MinLeadInSeconds);
            if (options.GapSeconds.HasValue) w.Property("gap", options.GapSeconds.Value);
            w.EndObject();
        }

        private static void ApplySaved(DrillOptions options, Dictionary<string, object> saved)
        {
            // 間隔は名前の BPM（小数1桁）より細かく持っているので、名前から出したものと食い違わないときだけ使う
            if (saved.TryGetValue("intervalMs", out var i) && i is double interval && interval > 0 &&
                Math.Abs(Tempo.EbpmFromHandIntervalMs(interval) - Tempo.EbpmFromHandIntervalMs(options.IntervalMs)) < 0.051)
                options.IntervalMs = interval;
            if (saved.TryGetValue("notesPerBeat", out var d) && d is double div && div > 0) options.NotesPerBeat = div;
            if (saved.TryGetValue("click", out var c) && c is string click && Enum.TryParse(click, out ClickMode mode)) options.Click = mode;
            if (saved.TryGetValue("countIn", out var k) && k is double count && count >= 0) options.CountInClicks = (int)count;
            if (saved.TryGetValue("tail", out var t) && t is double tail && tail >= 0) options.TailSeconds = tail;
            if (saved.TryGetValue("minLeadIn", out var l) && l is double lead && lead >= 0) options.MinLeadInSeconds = lead;
            if (saved.TryGetValue("gap", out var g) && g is double gap) options.GapSeconds = gap;
        }

        private static Dictionary<string, object> FirstDifficulty(Dictionary<string, object> root)
        {
            var sets = (root.TryGetValue("_difficultyBeatmapSets", out var s) ? s : null) as List<object>;
            var set = sets?.FirstOrDefault() as Dictionary<string, object>;
            var maps = (set != null && set.TryGetValue("_difficultyBeatmaps", out var m) ? m : null) as List<object>;
            return maps?.FirstOrDefault() as Dictionary<string, object>;
        }

        /// <summary>曲名の尺とセット数（<see cref="DrillNaming.ParseLength"/>）。</summary>
        public static bool ParseLength(string songName, out double seconds, out int sets)
        {
            return DrillNaming.ParseLength(songName, out seconds, out sets);
        }
    }
}
