using System;
using System.Collections.Generic;
using System.Globalization;
using JumpDrill.Model;

namespace JumpDrill.Serialization
{
    /// <summary>info.dat と難易度 dat の書き出し（v2 フォーマット）。</summary>
    public static class BeatmapSerializer
    {
        public const string SongFileName = "song.ogg";
        public const string CoverFileName = "cover.png";
        public const string DifficultyFileName = "ExpertPlusStandard.dat";

        public static string WriteDifficulty(DrillMap map, bool indent = false)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var w = new JsonWriter(indent);
            w.StartObject();
            w.Property("_version", "2.0.0");

            w.Name("_events").StartArray().EndArray();

            w.Name("_notes").StartArray();
            foreach (var n in map.Notes)
            {
                w.StartObject();
                w.Property("_time", Round(n.Beat));
                w.Property("_lineIndex", n.Position.LineIndex);
                w.Property("_lineLayer", n.Position.LineLayer);
                w.Property("_type", (int)n.Hand);
                w.Property("_cutDirection", (int)n.Direction);
                w.EndObject();
            }
            w.EndArray();

            w.Name("_obstacles").StartArray().EndArray();
            w.EndObject();
            return w.ToString();
        }

        public static string WriteInfo(DrillMap map, string songName = null, string songFileName = null, bool indent = true)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var w = new JsonWriter(indent);
            w.StartObject();
            w.Property("_version", "2.0.0");
            w.Property("_songName", songName ?? map.Name);
            w.Property("_songSubName", "");
            w.Property("_songAuthorName", "JumpDrill");
            w.Property("_levelAuthorName", "JumpDrill");
            w.Property("_beatsPerMinute", Round(map.Bpm));
            w.Property("_shuffle", 0);
            w.Property("_shufflePeriod", 0.5);
            // プレビューは短く。曲頭のカウントインだけ鳴らす。
            w.Property("_previewStartTime", Round(map.LeadInSeconds));
            w.Property("_previewDuration", 4);
            w.Property("_songFilename", songFileName ?? SongFileName);
            w.Property("_coverImageFilename", CoverFileName);
            w.Property("_environmentName", "DefaultEnvironment");
            w.Property("_allDirectionsEnvironmentName", "GlassDesertEnvironment");
            w.Property("_songTimeOffset", 0);

            w.Name("_difficultyBeatmapSets").StartArray();
            w.StartObject();
            w.Property("_beatmapCharacteristicName", "Standard");
            w.Name("_difficultyBeatmaps").StartArray();
            w.StartObject();
            w.Property("_difficulty", "ExpertPlus");
            w.Property("_difficultyRank", 9);
            w.Property("_beatmapFilename", DifficultyFileName);
            w.Property("_noteJumpMovementSpeed", Round(map.Njs));
            w.Property("_noteJumpStartBeatOffset", Round(map.NoteJumpStartBeatOffset));
            w.Name("_customData").StartObject();
            w.Property("_difficultyLabel", DifficultyLabel(map));
            w.EndObject();
            w.EndObject();
            w.EndArray();
            w.EndObject();
            w.EndArray();

            w.EndObject();
            return w.ToString();
        }

        /// <summary>曲選択画面で識別できるだけの情報を難易度ラベルに詰める。</summary>
        public static string DifficultyLabel(DrillMap map)
        {
            double notesPerBeat = map.Options.NotesPerBeat;
            string bpm = string.Format(CultureInfo.InvariantCulture, "{0:0}BPM",
                Model.Tempo.BpmFromIntervalMs(map.Options.IntervalMs, notesPerBeat));
            if (notesPerBeat != 1.0)
                bpm += string.Format(CultureInfo.InvariantCulture, " 1/{0:0.##}", notesPerBeat);

            // 速さは BPM で言う。ms は普段目にしない単位なので出さない。
            // 末尾の RT は反応時間で、こちらは ms で読むもの（JDFixer と同じ）。
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}  NJS{1:0.##}  JD{2:0.#}  RT{3:0}ms",
                bpm,
                map.Njs,
                map.Jump.JumpDistance,
                map.Jump.ReactionTimeSeconds * 1000.0);
        }

        /// <summary>_time を書くときの丸め。1e-6 拍まで残せば ms 精度は十分に足りる。</summary>
        private static double Round(double value)
        {
            return Math.Round(value, 6, MidpointRounding.AwayFromZero);
        }
    }
}
