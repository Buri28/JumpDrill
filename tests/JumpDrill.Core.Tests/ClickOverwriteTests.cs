using System;
using System.IO;
using System.Linq;
using JumpDrill.Audio;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using NVorbis;
using Xunit;

namespace JumpDrill.Tests
{
    public class ClickOverwriteTests
    {
        [Theory]
        [InlineData(ClickMode.Down, ClickTone.Kick, 8)]
        [InlineData(ClickMode.Down, ClickTone.Retro, 8)]
        [InlineData(ClickMode.Down, ClickTone.SynthBass, 8)]
        [InlineData(ClickMode.Beat, ClickTone.Kick, 8)]
        [InlineData(ClickMode.None, ClickTone.Kick, 8)]
        [InlineData(ClickMode.Down, ClickTone.Kick, 3)]
        [InlineData(ClickMode.None, ClickTone.Kick, 0)]
        public void Overwrite_updates_metadata_and_decoded_audio_in_the_same_folder(ClickMode click, ClickTone tone, int countIn)
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-overwrite-" + Guid.NewGuid().ToString("N"));
            try
            {
                var entry = DrillSet.Entries("8a")[2];
                string folder = DrillSetWriter.Write(root, new[] { entry }, new LevelWriteOptions()).Single();
                byte[] oldInfo = File.ReadAllBytes(Path.Combine(folder, "Info.dat"));
                byte[] oldAudio = File.ReadAllBytes(Path.Combine(folder, "song.ogg"));
                var changed = entry.WithClick(click, countIn, tone);
                Assert.Equal(folder, DrillSetWriter.Write(root, new[] { changed }, new LevelWriteOptions(), skipExisting: true).Single());
                Assert.Equal(oldInfo, File.ReadAllBytes(Path.Combine(folder, "Info.dat")));
                Assert.Equal(oldAudio, File.ReadAllBytes(Path.Combine(folder, "song.ogg")));

                Assert.Equal(folder, DrillSetWriter.Write(root, new[] { changed }, new LevelWriteOptions(), skipExisting: false).Single());
                Assert.Single(Directory.GetDirectories(root));
                var read = DrillRebuild.Read(folder);
                Assert.Equal(click, read.Click);
                Assert.Equal(tone, read.ClickTone);
                Assert.Equal(countIn, read.CountInClicks);
                Assert.Equal(entry.Options.Name, read.Name);
                Assert.Equal(DrillNaming.ExtractId(entry.Options.Name), DrillNaming.ExtractId(read.Name));
                Assert.False(oldAudio.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "song.ogg"))));

                // Compare all decoded samples against a fresh render of the requested settings.
                // This detects stale audio even if Info.dat was updated correctly.
                var expected = ClickTrackRenderer.Render(DrillGenerator.Generate(changed.Options));
                using var vorbis = new VorbisReader(Path.Combine(folder, "song.ogg"));
                Assert.Equal(44100, vorbis.SampleRate);
                Assert.Equal(2, vorbis.Channels);
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
                if (energy == 0) Assert.All(decoded.Take(length * vorbis.Channels), value => Assert.Equal(0f, value));
                else Assert.True(Math.Sqrt(error / energy) < 0.12, "Decoded audio differs from the requested render.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}


