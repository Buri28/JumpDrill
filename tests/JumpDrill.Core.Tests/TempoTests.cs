using System;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;
using JumpDrill.Serialization;
using Xunit;

namespace JumpDrill.Tests
{
    public class TempoTests
    {
        [Theory]
        // 1拍1ノーツなら BPM がそのまま「1分あたりのノーツ数」になる。
        [InlineData(300.0, 1.0, 200.0)]
        [InlineData(350.0, 1.0, 171.4285714)]
        [InlineData(120.0, 1.0, 500.0)]
        // 細分化を上げるとその分だけ間隔が縮む。
        [InlineData(300.0, 2.0, 100.0)]
        [InlineData(300.0, 4.0, 50.0)]
        [InlineData(350.0, 2.0, 85.7142857)]
        public void Bpm_converts_to_interval(double bpm, double notesPerBeat, double expectedMs)
        {
            Assert.Equal(expectedMs, Tempo.IntervalMsFromBpm(bpm, notesPerBeat), 5);
        }

        [Theory]
        [InlineData(200.0, 1.0, 300.0)]
        [InlineData(100.0, 2.0, 300.0)]
        [InlineData(110.0, 1.0, 545.4545454)]
        public void Interval_converts_back_to_bpm(double intervalMs, double notesPerBeat, double expectedBpm)
        {
            Assert.Equal(expectedBpm, Tempo.BpmFromIntervalMs(intervalMs, notesPerBeat), 5);
        }

        [Theory]
        [InlineData(300.0, 1.0)]
        [InlineData(350.0, 2.0)]
        [InlineData(175.5, 4.0)]
        public void Conversion_round_trips(double bpm, double notesPerBeat)
        {
            double ms = Tempo.IntervalMsFromBpm(bpm, notesPerBeat);
            Assert.Equal(bpm, Tempo.BpmFromIntervalMs(ms, notesPerBeat), 8);
        }

        [Fact]
        public void Same_bpm_at_different_subdivisions_is_a_factor_of_two_apart()
        {
            // BPM だけでは間隔が決まらないという設計メモ §2 の指摘そのもの。
            double whole = Tempo.IntervalMsFromBpm(300, 1);
            double half = Tempo.IntervalMsFromBpm(300, 2);
            Assert.Equal(whole / 2.0, half, 9);
        }

        [Fact]
        public void Notes_per_minute_equals_bpm_at_one_note_per_beat()
        {
            Assert.Equal(Tempo.BpmFromIntervalMs(110, 1), Tempo.NotesPerMinute(110), 9);
        }

        [Fact]
        public void Describe_lists_the_common_subdivisions()
        {
            // 100ms = 600/分。1/2 で数えれば 300 BPM。
            string text = Tempo.Describe(100.0);
            Assert.Contains("600 BPM 1/1", text);
            Assert.Contains("300 BPM 1/2", text);
            Assert.Contains("150 BPM 1/4", text);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void Rejects_non_positive_input(double bad)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Tempo.IntervalMsFromBpm(bad, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Tempo.BpmFromIntervalMs(bad, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Tempo.IntervalMsFromBpm(300, bad));
        }
    }

    public class IntervalParserTests
    {
        [Theory]
        [InlineData("110", 1.0, 110.0)]
        [InlineData("110ms", 1.0, 110.0)]
        [InlineData("110MS", 1.0, 110.0)]
        [InlineData(" 110 ms ", 1.0, 110.0)]
        [InlineData("85.7", 1.0, 85.7)]
        public void Plain_values_are_milliseconds(string text, double notesPerBeat, double expected)
        {
            Assert.Equal(expected, IntervalParser.ParseIntervalMs(text, notesPerBeat), 5);
        }

        [Theory]
        [InlineData("300bpm", 1.0, 200.0)]
        [InlineData("300BPM", 1.0, 200.0)]
        [InlineData("300bpm", 2.0, 100.0)]
        [InlineData("350bpm", 2.0, 85.7142857)]
        [InlineData("500bpm", 1.0, 120.0)]
        public void Bpm_suffix_uses_the_subdivision(string text, double notesPerBeat, double expected)
        {
            Assert.Equal(expected, IntervalParser.ParseIntervalMs(text, notesPerBeat), 5);
        }

        [Fact]
        public void Subdivision_is_ignored_for_millisecond_input()
        {
            // ms 指定は細分化と無関係。--div を付けても間隔は変わらない。
            Assert.Equal(110.0, IntervalParser.ParseIntervalMs("110ms", 4.0), 9);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abc")]
        [InlineData("bpm")]
        [InlineData("0")]
        [InlineData("-110")]
        [InlineData("0bpm")]
        public void Rejects_malformed_values(string text)
        {
            Assert.Throws<FormatException>(() => IntervalParser.ParseIntervalMs(text, 1.0));
        }

        [Theory]
        // 音符でも「1拍あたりのノーツ数」でも同じ値になること。
        [InlineData("1", 1.0)]
        [InlineData("1/1", 1.0)]
        [InlineData("2", 2.0)]
        [InlineData("1/2", 2.0)]
        [InlineData("1/4", 4.0)]
        [InlineData("1/8", 8.0)]
        [InlineData(" 1/3 ", 3.0)]
        public void Subdivision_accepts_both_notations(string text, double expected)
        {
            Assert.Equal(expected, IntervalParser.ParseNotesPerBeat(text), 9);
        }

        [Fact]
        public void Bpm_with_a_half_note_doubles_the_note_rate()
        {
            // 1/2 なら1拍で2回切るので、350 BPM は 1分に 700 個。
            double ms = IntervalParser.ParseIntervalMs("350bpm", IntervalParser.ParseNotesPerBeat("1/2"));

            Assert.Equal(85.714, ms, 3);
            Assert.Equal(700.0, Tempo.NotesPerMinute(ms), 6);
        }

        [Theory]
        [InlineData("1/1", 350.0)]
        [InlineData("1/2", 700.0)]
        [InlineData("1/4", 1400.0)]
        public void Note_rate_scales_with_the_subdivision(string div, double expectedPerMinute)
        {
            double ms = IntervalParser.ParseIntervalMs("350bpm", IntervalParser.ParseNotesPerBeat(div));
            Assert.Equal(expectedPerMinute, Tempo.NotesPerMinute(ms), 6);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1/0")]
        [InlineData("0/2")]
        [InlineData("-1")]
        [InlineData("x/2")]
        public void Subdivision_rejects_nonsense(string text)
        {
            Assert.Throws<FormatException>(() => IntervalParser.ParseNotesPerBeat(text));
        }

        [Theory]
        [InlineData("300bpm", true)]
        [InlineData("300BPM", true)]
        [InlineData("110ms", false)]
        [InlineData("110", false)]
        public void Detects_the_bpm_form(string text, bool expected)
        {
            Assert.Equal(expected, IntervalParser.IsBpm(text));
        }
    }

    public class TempoReportingTests
    {
        private static DrillMap Sample(double intervalMs, double notesPerBeat = 1.0)
        {
            return DrillGenerator.Generate(new DrillOptions
            {
                Sequences = SequenceParser.ParseAll("R:8>b"),
                IntervalMs = intervalMs,
                DurationSeconds = 4,
                NotesPerBeat = notesPerBeat,
            });
        }

        [Fact]
        public void Summary_reports_the_tempo_in_bpm_only()
        {
            // ms も EBPM も出さない。同じ速さを3通りに書いても読み替えが増えるだけ。
            string text = LevelWriter.Summarize(Sample(100.0, 2.0));
            Assert.Contains("300 BPM 1/2", text);
            Assert.DoesNotContain("100 ms", text);
        }

        [Fact]
        public void Difficulty_label_carries_the_bpm()
        {
            // 曲選択画面で速さが読めるようにしてある。単位は BPM だけ。
            string label = BeatmapSerializer.DifficultyLabel(Sample(200.0, 2.0));
            Assert.Contains("150BPM 1/2", label);
            Assert.DoesNotContain("200ms", label);
        }

        [Fact]
        public void A_bpm_request_produces_the_matching_interval_end_to_end()
        {
            double ms = IntervalParser.ParseIntervalMs("350bpm", 2.0);
            var map = Sample(ms, 2.0);

            Assert.Equal(85.714, map.Options.IntervalMs, 3);
            Assert.Contains("350 BPM 1/2", LevelWriter.Summarize(map));
        }

        [Fact]
        public void Reporting_keeps_the_subdivision_that_was_asked_for()
        {
            // 300BPM 1/2 と指定したものが 600BPM と表示されると読み替えが要る。
            var map = Sample(IntervalParser.ParseIntervalMs("300bpm", 2.0), 2.0);

            Assert.Equal(100.0, map.Options.IntervalMs, 6);
            Assert.Contains("300 BPM 1/2", LevelWriter.Summarize(map));

            string label = BeatmapSerializer.DifficultyLabel(map);
            Assert.Contains("300BPM 1/2", label);
            Assert.DoesNotContain("100ms", label);
        }

        [Fact]
        public void Subdivision_is_display_only_and_does_not_move_the_notes()
        {
            var plain = Sample(100.0);
            var halved = Sample(100.0, 2.0);

            Assert.Equal(plain.Notes.Count, halved.Notes.Count);
            for (int i = 0; i < plain.Notes.Count; i++)
                Assert.Equal(plain.Notes[i].TimeSeconds, halved.Notes[i].TimeSeconds, 9);
        }

        [Fact]
        public void Label_omits_the_subdivision_when_counting_whole_beats()
        {
            string label = BeatmapSerializer.DifficultyLabel(Sample(200.0));
            Assert.Contains("300BPM", label);
            Assert.DoesNotContain("1/1", label);
        }
    }
}
