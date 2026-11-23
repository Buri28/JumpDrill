using System;
using System.IO;
using System.Linq;
using JumpDrill.Audio;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;
using NVorbis;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>
    /// カスタムステージ・その他のドリルを、生成オプションの窓で選んだ値で作り直す（DrillRebuild.Rewrite の sound）。
    /// 窓の4項目だけが替わり、形・BPM・NJS と曲名・ID は譜面のまま。
    /// </summary>
    public class RebuildSoundTests
    {
        private static string TempRoot() => Path.Combine(Path.GetTempPath(), "jd-rebuild-sound-" + Guid.NewGuid().ToString("N"));

        /// <summary>Mods タブ・GUI で作った、標準のステージに無いドリル（その他・カスタムステージに並ぶもの）。</summary>
        private static DrillOptions Custom()
        {
            var sequences = SequenceParser.ParseAll("R:8>b");
            sequences.Add(SequenceParser.Mirror(sequences[0], Hand.Left));
            var options = new DrillOptions
            {
                Sequences = sequences,
                IntervalMs = Tempo.IntervalMsFromBpm(175, 2.0),
                NotesPerBeat = 2.0,
                DurationSeconds = 12,
                Sets = 2,
                HandPattern = HandPattern.AlternateRoundTrip,
                Direction = DirectionMode.Perpendicular,
                Bpm = 175,
                Njs = 17,
                JumpDistance = 20,
                Click = ClickMode.All,
                ClickTone = ClickTone.Standard,
                CountInClicks = 4,
                TailSeconds = 1.5,
            };
            options.Name = DrillNaming.Compose(options);
            return options;
        }

        private static readonly SoundSettings Chosen = new SoundSettings
        {
            Click = ClickMode.Beat,
            Tone = ClickTone.Kick,
            CountIn = 3,
            TailSeconds = 2.3,
        };

        [Fact]
        public void Custom_drill_is_rebuilt_with_the_chosen_values_and_keeps_the_rest()
        {
            string root = TempRoot();
            try
            {
                var options = Custom();
                string folder = LevelWriter.Write(DrillGenerator.Generate(options), root);
                string id = DrillNaming.ExtractId(options.Name);
                byte[] oldAudio = File.ReadAllBytes(Path.Combine(folder, "song.ogg"));

                var result = DrillRebuild.Rewrite(folder, null, Chosen);
                Assert.Equal(Path.GetFullPath(folder).TrimEnd('\\'), Path.GetFullPath(result.Folder).TrimEnd('\\'));
                Assert.False(result.Renamed);

                var read = DrillRebuild.Read(folder);
                Assert.True(Chosen.SameAs(SoundSettings.Of(read)));
                // 窓に無いものは譜面のまま
                Assert.Equal(options.Name, read.Name);
                Assert.Equal(id, DrillNaming.ExtractId(read.Name));
                Assert.Equal(17.0, read.Njs);
                Assert.Equal(175.0, read.Bpm);
                Assert.Equal(options.IntervalMs, read.IntervalMs, 9);
                Assert.Equal(HandPattern.AlternateRoundTrip, read.HandPattern);
                Assert.Equal(DirectionMode.Perpendicular, read.Direction);

                Assert.False(oldAudio.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "song.ogg"))));
                var expected = options.Clone();
                Chosen.ApplyTo(expected);
                AssertAudioMatches(folder, expected);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Without_chosen_values_the_drill_keeps_its_own()
        {
            string root = TempRoot();
            try
            {
                var options = Custom();
                string folder = LevelWriter.Write(DrillGenerator.Generate(options), root);
                DrillRebuild.Rewrite(folder, null, null);
                Assert.True(SoundSettings.Of(options).SameAs(SoundSettings.Of(DrillRebuild.Read(folder))));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Standard_drill_reads_and_rebuilds_all_four_values()
        {
            string root = TempRoot();
            try
            {
                var entry = DrillSet.Entries("8a")[2].WithClick(ClickMode.None, 5, ClickTone.Retro, 2.3);
                string folder = DrillSetWriter.Write(root, new[] { entry }, new LevelWriteOptions()).Single();

                // 末尾の余白も作ったときの値で読む（以前は 1.5 秒に戻っていた）
                Assert.True(SoundSettings.Of(entry.Options).SameAs(SoundSettings.Of(DrillRebuild.Read(folder))));

                var chosen = new SoundSettings { Click = ClickMode.Down, Tone = ClickTone.SynthBass, CountIn = 0, TailSeconds = 0.4 };
                DrillRebuild.Rewrite(folder, null, chosen);
                var read = DrillRebuild.Read(folder);
                Assert.True(chosen.SameAs(SoundSettings.Of(read)));
                Assert.Equal(entry.Options.Name, read.Name);
                Assert.Equal(entry.Options.Njs, read.Njs);
                Assert.Single(Directory.GetDirectories(root));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Same_id_variant_keeps_its_njs_and_name()
        {
            string root = TempRoot();
            try
            {
                var o = DrillSet.Entries("8a")[2].Options;
                var variant = o.Clone();
                variant.Njs = o.Njs + 4;
                variant.Name = DrillNaming.Compose(variant);
                Assert.Equal(DrillNaming.ExtractId(o.Name), DrillNaming.ExtractId(variant.Name));
                Assert.False(DrillSet.IsStandardName(variant.Name));
                string folder = LevelWriter.Write(DrillGenerator.Generate(variant), root);

                DrillRebuild.Rewrite(folder, null, Chosen);
                var read = DrillRebuild.Read(folder);
                Assert.Equal(variant.Name, read.Name);
                Assert.Equal(variant.Njs, read.Njs);
                Assert.True(Chosen.SameAs(SoundSettings.Of(read)));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Common_uses_the_first_value_and_tells_when_they_differ()
        {
            var a = new SoundSettings { Click = ClickMode.Up, CountIn = 2 };
            var b = new SoundSettings { Click = ClickMode.Up, CountIn = 2 };
            var c = new SoundSettings { Click = ClickMode.None, CountIn = 2 };

            Assert.Same(a, SoundSettings.Common(new[] { a, b }, out bool same));
            Assert.False(same);
            Assert.Same(a, SoundSettings.Common(new[] { a, null, c }, out bool differ));
            Assert.True(differ);
            var none = SoundSettings.Common(new SoundSettings[0], out bool empty);
            Assert.False(empty);
            Assert.Equal(ClickMode.Down, none.Click);
        }

        private static void AssertAudioMatches(string folder, DrillOptions options)
        {
            var expected = ClickTrackRenderer.Render(DrillGenerator.Generate(options));
            using var vorbis = new VorbisReader(Path.Combine(folder, "song.ogg"));
            var decoded = new float[(expected.Length + 16384) * vorbis.Channels];
            int length = 0, n;
            while ((n = vorbis.ReadSamples(decoded, length, decoded.Length - length)) > 0) length += n;
            length /= vorbis.Channels;
            Assert.InRange(length, expected.Length, expected.Length + 9216);
            double error = 0, energy = 0;
            for (int i = 0; i < Math.Min(length, expected.Length); i++)
            {
                error += Math.Pow(decoded[i * vorbis.Channels] - expected[i], 2);
                energy += expected[i] * expected[i];
            }
            Assert.True(energy > 0);
            Assert.True(Math.Sqrt(error / energy) < 0.12, "Decoded audio differs from the requested render.");
        }
    }
}
