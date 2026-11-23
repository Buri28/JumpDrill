using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JumpDrill.Model;
using JumpDrill.Output;
using NVorbis;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>消した音色 KickBass（5）は、譜面・設定のどこから読んでもシンセベースになる。</summary>
    public class RemovedKickBassTests
    {
        [Fact]
        public void Tone_choices_are_the_five_remaining_tones()
        {
            Assert.Equal(new[] { "Standard", "Low", "Kick", "Retro", "SynthBass" }, Enum.GetNames(typeof(ClickTone)));
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, Enum.GetValues(typeof(ClickTone)).Cast<int>().ToArray());
        }

        [Theory]
        [InlineData("KickBass", ClickTone.SynthBass)]
        [InlineData("kickbass", ClickTone.SynthBass)]
        [InlineData(" KickBass ", ClickTone.SynthBass)]
        [InlineData("5", ClickTone.SynthBass)]
        [InlineData("SynthBass", ClickTone.SynthBass)]
        [InlineData("synthbass", ClickTone.SynthBass)]
        [InlineData("Standard", ClickTone.Standard)]
        [InlineData("Low", ClickTone.Low)]
        [InlineData("Kick", ClickTone.Kick)]
        [InlineData("Retro", ClickTone.Retro)]
        public void Names_are_read_with_kickbass_as_synthbass(string text, ClickTone expected)
        {
            Assert.True(ClickTones.TryParse(text, out var tone));
            Assert.Equal(expected, tone);
            Assert.Equal(expected, ClickTones.Parse(text, ClickTone.Standard));
        }

        [Theory]
        [InlineData("unknown")]
        [InlineData("6")]
        [InlineData("123")]
        [InlineData("-1")]
        [InlineData("Low, Kick")]
        [InlineData("Standard, KickBass")]
        [InlineData("")]
        [InlineData(null)]
        public void Unknown_names_fall_back(string text)
        {
            Assert.False(ClickTones.TryParse(text, out _));
            Assert.Equal(ClickTone.Standard, ClickTones.Parse(text, ClickTone.Standard));
        }

        [Theory]
        [InlineData(0, ClickTone.Standard)]
        [InlineData(1, ClickTone.Low)]
        [InlineData(2, ClickTone.Kick)]
        [InlineData(3, ClickTone.Retro)]
        [InlineData(4, ClickTone.SynthBass)]
        [InlineData(5, ClickTone.SynthBass)]
        [InlineData(6, ClickTone.Standard)]
        [InlineData(-1, ClickTone.Standard)]
        public void Gui_setting_index_five_reads_as_synthbass(int index, ClickTone expected)
        {
            // GUI の settings.txt の clickTone・medalClickTone はプルダウンの番号（＝enum の値）
            Assert.Equal(expected, ClickTones.FromIndex(index));
        }

        [Fact]
        public void Kickbass_map_reads_and_rebuilds_as_synthbass()
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-kickbass-" + Guid.NewGuid().ToString("N"));
            try
            {
                var entry = DrillSet.Entries("8b")[1].WithClick(ClickMode.Down, 8, ClickTone.SynthBass);
                string folder = DrillSetWriter.Write(root, new[] { entry }, new LevelWriteOptions()).Single();
                string info = Path.Combine(folder, "Info.dat");
                string original = File.ReadAllText(info);
                float[] synthAudio = Decode(Path.Combine(folder, "song.ogg"));
                Assert.Contains("\"clickTone\": \"SynthBass\"", original);

                // KickBass で作った譜面と同じ Info.dat にする
                File.WriteAllText(info, original.Replace("\"clickTone\": \"SynthBass\"", "\"clickTone\": \"KickBass\""));
                var read = DrillRebuild.Read(folder);
                Assert.Equal(ClickTone.SynthBass, read.ClickTone);
                Assert.Equal(entry.Options.Name, read.Name);

                // 作り直すとシンセベースで書かれ、曲名・ID は変わらない
                var rebuilt = DrillRebuild.Rewrite(folder);
                Assert.Equal(folder, rebuilt.Folder);
                Assert.Contains("\"clickTone\": \"SynthBass\"", File.ReadAllText(Path.Combine(rebuilt.Folder, "Info.dat")));
                Assert.Equal(ClickTone.SynthBass, DrillRebuild.Read(rebuilt.Folder).ClickTone);
                Assert.Equal(DrillNaming.ExtractId(entry.Options.Name), DrillNaming.ExtractId(DrillRebuild.Read(rebuilt.Folder).Name));
                // ogg の通し番号は毎回変わるので、復号した音で比べる
                Assert.Equal(synthAudio, Decode(Path.Combine(rebuilt.Folder, "song.ogg")));

                var settings = new Dictionary<string, object> { { "clickTone", "KickBass" } };
                var options = entry.Options.Clone();
                options.ClickTone = ClickTone.Standard;
                DrillRebuild.ApplySettings(options, settings);
                Assert.Equal(ClickTone.SynthBass, options.ClickTone);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static float[] Decode(string path)
        {
            using var vorbis = new VorbisReader(path);
            var all = new List<float>();
            var buffer = new float[16384];
            int n;
            while ((n = vorbis.ReadSamples(buffer, 0, buffer.Length)) > 0) all.AddRange(buffer.Take(n));
            return all.ToArray();
        }
    }
}
