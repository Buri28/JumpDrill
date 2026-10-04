using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using JumpDrill.Audio;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;
using JumpDrill.Serialization;
using Xunit;

namespace JumpDrill.Tests
{
    public class JsonWriterTests
    {
        [Fact]
        public void Writes_a_nested_object()
        {
            var w = new JsonWriter(indent: false);
            w.StartObject()
             .Property("a", 1)
             .Name("b").StartArray().Value(1).Value(2).EndArray()
             .Name("c").StartObject().Property("d", "x").EndObject()
             .EndObject();

            using (var doc = JsonDocument.Parse(w.ToString()))
            {
                Assert.Equal(1, doc.RootElement.GetProperty("a").GetInt32());
                Assert.Equal(2, doc.RootElement.GetProperty("b").GetArrayLength());
                Assert.Equal("x", doc.RootElement.GetProperty("c").GetProperty("d").GetString());
            }
        }

        [Fact]
        public void Escapes_control_characters_and_quotes()
        {
            var w = new JsonWriter(indent: false);
            w.StartObject().Property("k", "a\"b\\c\nd\te").EndObject();

            using (var doc = JsonDocument.Parse(w.ToString()))
                Assert.Equal("a\"b\\c\nd\te", doc.RootElement.GetProperty("k").GetString());
        }

        [Fact]
        public void Escapes_non_ascii_so_the_file_stays_portable()
        {
            var w = new JsonWriter(indent: false);
            w.StartObject().Property("k", "ジャンプ").EndObject();

            Assert.DoesNotContain("ジ", w.ToString());
            using (var doc = JsonDocument.Parse(w.ToString()))
                Assert.Equal("ジャンプ", doc.RootElement.GetProperty("k").GetString());
        }

        [Fact]
        public void Formats_numbers_without_scientific_notation_or_locale_commas()
        {
            Assert.Equal("0.0001", JsonWriter.Format(0.0001));
            Assert.Equal("3", JsonWriter.Format(3.0));
            Assert.Equal("-1.5", JsonWriter.Format(-1.5));
        }

        [Fact]
        public void Rejects_values_json_cannot_carry()
        {
            var w = new JsonWriter(indent: false);
            w.StartObject();
            Assert.Throws<ArgumentException>(() => w.Property("k", double.NaN));
        }
    }

    public class BeatmapSerializerTests
    {
        private static DrillMap Sample(string seq = "R:8>b", double interval = 150, double sec = 5)
        {
            return DrillGenerator.Generate(new DrillOptions
            {
                Sequences = SequenceParser.ParseAll(seq),
                IntervalMs = interval,
                DurationSeconds = sec,
            });
        }

        [Fact]
        public void Difficulty_file_is_valid_v2_json()
        {
            var map = Sample();
            using (var doc = JsonDocument.Parse(BeatmapSerializer.WriteDifficulty(map)))
            {
                Assert.Equal("2.0.0", doc.RootElement.GetProperty("_version").GetString());
                Assert.Equal(map.Notes.Count, doc.RootElement.GetProperty("_notes").GetArrayLength());
                Assert.Equal(0, doc.RootElement.GetProperty("_obstacles").GetArrayLength());
            }
        }

        [Fact]
        public void Note_fields_match_the_generated_notes()
        {
            var map = Sample("L:1>9>4>c");
            using (var doc = JsonDocument.Parse(BeatmapSerializer.WriteDifficulty(map)))
            {
                var notes = doc.RootElement.GetProperty("_notes").EnumerateArray().ToList();
                for (int i = 0; i < map.Notes.Count; i++)
                {
                    var expected = map.Notes[i];
                    Assert.Equal(expected.Beat, notes[i].GetProperty("_time").GetDouble(), 5);
                    Assert.Equal(expected.Position.LineIndex, notes[i].GetProperty("_lineIndex").GetInt32());
                    Assert.Equal(expected.Position.LineLayer, notes[i].GetProperty("_lineLayer").GetInt32());
                    Assert.Equal((int)expected.Hand, notes[i].GetProperty("_type").GetInt32());
                    Assert.Equal((int)expected.Direction, notes[i].GetProperty("_cutDirection").GetInt32());
                }
            }
        }

        [Fact]
        public void Note_times_are_strictly_increasing_and_start_after_the_lead_in()
        {
            var map = Sample();
            using (var doc = JsonDocument.Parse(BeatmapSerializer.WriteDifficulty(map)))
            {
                var times = doc.RootElement.GetProperty("_notes").EnumerateArray()
                    .Select(n => n.GetProperty("_time").GetDouble()).ToList();

                Assert.True(times[0] > 0);
                for (int i = 1; i < times.Count; i++)
                    Assert.True(times[i] > times[i - 1]);
            }
        }

        [Fact]
        public void Info_file_carries_the_jump_settings_the_generator_resolved()
        {
            var options = new DrillOptions
            {
                Sequences = SequenceParser.ParseAll("R:8>b"),
                Njs = 18,
                JumpDistance = 20,
            };
            var map = DrillGenerator.Generate(options);

            using (var doc = JsonDocument.Parse(BeatmapSerializer.WriteInfo(map)))
            {
                var beatmap = doc.RootElement
                    .GetProperty("_difficultyBeatmapSets")[0]
                    .GetProperty("_difficultyBeatmaps")[0];

                Assert.Equal(18, beatmap.GetProperty("_noteJumpMovementSpeed").GetDouble(), 6);
                Assert.Equal(map.NoteJumpStartBeatOffset, beatmap.GetProperty("_noteJumpStartBeatOffset").GetDouble(), 5);
                Assert.Equal("ExpertPlus", beatmap.GetProperty("_difficulty").GetString());
                Assert.Equal(BeatmapSerializer.DifficultyFileName, beatmap.GetProperty("_beatmapFilename").GetString());
            }
        }

        [Fact]
        public void Info_preview_starts_at_the_first_note_and_stays_short()
        {
            var map = Sample();
            using (var doc = JsonDocument.Parse(BeatmapSerializer.WriteInfo(map)))
            {
                Assert.Equal(map.LeadInSeconds, doc.RootElement.GetProperty("_previewStartTime").GetDouble(), 5);
                Assert.True(doc.RootElement.GetProperty("_previewDuration").GetDouble() <= 10);
            }
        }

        [Fact]
        public void Info_song_file_name_can_be_overridden_for_wav_output()
        {
            var map = Sample();
            using (var doc = JsonDocument.Parse(BeatmapSerializer.WriteInfo(map, null, "song.wav")))
                Assert.Equal("song.wav", doc.RootElement.GetProperty("_songFilename").GetString());
        }
    }

    public class AudioTests
    {
        [Fact]
        public void Click_track_is_silent_except_around_the_clicks()
        {
            var clicks = new[] { new ClickEvent(0.5, false) };
            var samples = ClickTrackRenderer.Render(clicks, 1.0, 44100, ClickVoice.Normal, ClickVoice.Accent);

            Assert.Equal(44100, samples.Length);
            Assert.Equal(0f, samples[0]);
            Assert.Equal(0f, samples[44099]);
            Assert.Contains(samples.Skip(22050).Take(1323), s => Math.Abs(s) > 0.1f);
        }

        [Fact]
        public void Click_lands_on_the_requested_sample()
        {
            var samples = ClickTrackRenderer.Render(
                new[] { new ClickEvent(0.25, false) }, 0.5, 44100, ClickVoice.Normal, ClickVoice.Accent);

            int first = Array.FindIndex(samples, s => Math.Abs(s) > 1e-6f);
            Assert.InRange(first, 11025, 11027);
        }

        [Fact]
        public void Accent_is_louder_and_higher_than_the_normal_click()
        {
            Assert.True(ClickVoice.Accent.Amplitude > ClickVoice.Normal.Amplitude);
            Assert.True(ClickVoice.Accent.FrequencyHz > ClickVoice.Normal.FrequencyHz);
        }

        [Fact]
        public void Samples_never_clip()
        {
            // 同じ位置に重ねても振り切れないこと。
            var clicks = Enumerable.Repeat(new ClickEvent(0.1, true), 8).ToArray();
            var samples = ClickTrackRenderer.Render(clicks, 0.5, 44100, ClickVoice.Normal, ClickVoice.Accent);

            Assert.All(samples, s => Assert.InRange(s, -1f, 1f));
        }

        [Fact]
        public void Wav_header_describes_the_payload()
        {
            var bytes = WavWriter.ToBytes(new float[100], 44100);

            Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.Equal(44 + 200, bytes.Length);
            Assert.Equal(BitConverter.GetBytes(bytes.Length - 8), bytes.Skip(4).Take(4).ToArray());
        }

        [Fact]
        public void Ogg_output_is_a_vorbis_stream()
        {
            var samples = ClickTrackRenderer.Render(
                new[] { new ClickEvent(0.2, false), new ClickEvent(0.6, true) }, 1.0, 44100,
                ClickVoice.Normal, ClickVoice.Accent);

            var ogg = OggEncoder.ToBytes(samples, 44100);

            Assert.Equal("OggS", Encoding.ASCII.GetString(ogg, 0, 4));
            Assert.Contains("vorbis", Encoding.ASCII.GetString(ogg, 0, 128));
            Assert.True(ogg.Length > 200);
        }

        [Fact]
        public void Ogg_keeps_the_whole_track()
        {
            const int SampleRate = 44100;
            var samples = new float[SampleRate * 2];
            var ogg = OggEncoder.ToBytes(samples, SampleRate);

            Assert.True(FinalGranule(ogg) >= samples.Length,
                "末尾が切り落とされている: " + FinalGranule(ogg) + " < " + samples.Length);
        }

        private static long FinalGranule(byte[] ogg)
        {
            int offset = 0;
            long last = 0;
            while (offset + 27 <= ogg.Length)
            {
                long granule = BitConverter.ToInt64(ogg, offset + 6);
                int segments = ogg[offset + 26];
                int body = 0;
                for (int i = 0; i < segments; i++) body += ogg[offset + 27 + i];
                if (granule >= 0) last = granule;
                offset += 27 + segments + body;
            }
            return last;
        }
    }

    public class PngWriterTests
    {
        [Fact]
        public void Writes_a_png_with_correct_chunk_crcs()
        {
            var rgb = new byte[4 * 3 * 3];
            var png = PngWriter.Encode(rgb, 4, 3);

            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8).ToArray());

            var types = new List<string>();
            int offset = 8;
            while (offset < png.Length)
            {
                int length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
                types.Add(Encoding.ASCII.GetString(png, offset + 4, 4));
                offset += 12 + length;
            }

            Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, types.ToArray());
            Assert.Equal(png.Length, offset);
        }

        [Fact]
        public void Rejects_a_buffer_that_does_not_match_the_dimensions()
        {
            Assert.Throws<ArgumentException>(() => PngWriter.Encode(new byte[10], 4, 3));
        }
    }

    public class LevelWriterTests
    {
        private static DrillMap Sample()
        {
            return DrillGenerator.Generate(new DrillOptions
            {
                Sequences = SequenceParser.ParseAll("R:8>b, L:a>1"),
                IntervalMs = 200,
                DurationSeconds = 2,
            });
        }

        [Theory]
        [InlineData("R:8>b 110ms axis", "R_8-b_110ms_axis")]
        [InlineData("", "drill")]
        [InlineData("///", "drill")]
        public void Folder_names_drop_characters_the_filesystem_rejects(string input, string expected)
        {
            Assert.Equal(expected, LevelWriter.SanitizeFolderName(input));
        }

        [Fact]
        public void Writes_a_complete_custom_level_folder()
        {
            string root = Path.Combine(Path.GetTempPath(), "JumpDrillTests", Guid.NewGuid().ToString("N"));
            try
            {
                var map = Sample();
                string folder = LevelWriter.Write(map, root, null, new LevelWriteOptions { Format = AudioFormat.Wav });

                Assert.True(File.Exists(Path.Combine(folder, "Info.dat")));
                Assert.True(File.Exists(Path.Combine(folder, BeatmapSerializer.DifficultyFileName)));
                Assert.True(File.Exists(Path.Combine(folder, BeatmapSerializer.CoverFileName)));
                Assert.True(File.Exists(Path.Combine(folder, "song.wav")));

                // Beat Saber のパーサは BOM を嫌う。
                var head = File.ReadAllBytes(Path.Combine(folder, "Info.dat")).Take(3).ToArray();
                Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, head);

                using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "Info.dat"))))
                    Assert.Equal("song.wav", doc.RootElement.GetProperty("_songFilename").GetString());
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Audio_is_at_least_as_long_as_the_map()
        {
            string root = Path.Combine(Path.GetTempPath(), "JumpDrillTests", Guid.NewGuid().ToString("N"));
            try
            {
                var map = Sample();
                string folder = LevelWriter.Write(map, root, null, new LevelWriteOptions { Format = AudioFormat.Wav });

                var wav = File.ReadAllBytes(Path.Combine(folder, "song.wav"));
                int dataBytes = BitConverter.ToInt32(wav, 40);
                double seconds = dataBytes / 2.0 / 44100.0;

                Assert.True(seconds >= map.TotalSeconds - 1e-6);
                Assert.True(seconds < map.TotalSeconds + 0.05);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
