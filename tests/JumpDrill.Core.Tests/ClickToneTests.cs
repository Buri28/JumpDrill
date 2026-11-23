using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JumpDrill.Audio;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;
using JumpDrill.Serialization;
using NVorbis;
using Xunit;
using Xunit.Abstractions;

namespace JumpDrill.Tests
{
    public class ClickToneTests
    {
        private readonly ITestOutputHelper output;
        public ClickToneTests(ITestOutputHelper output) { this.output = output; }

        [Theory]
        [InlineData(ClickTone.Standard, 44100, 75)]
        [InlineData(ClickTone.Low, 44100, 75)]
        [InlineData(ClickTone.Standard, 48000, 333)]
        [InlineData(ClickTone.Low, 48000, 333)]
        [InlineData(ClickTone.Kick, 44100, 120)]
        [InlineData(ClickTone.Retro, 44100, 120)]
        [InlineData(ClickTone.SynthBass, 48000, 120)]
        public void Written_audio_aligns_with_written_notes(ClickTone tone, int rate, double interval)
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-tone-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new DrillOptions {
                    Sequences = SequenceParser.ParseAll("R:8>b"), IntervalMs = interval,
                    DurationSeconds = 2, CountInClicks = 4, Click = ClickMode.All, ClickTone = tone
                };
                options.Name = DrillNaming.Compose(options);
                var map = DrillGenerator.Generate(options);
                string folder = LevelWriter.Write(map, root, options: new LevelWriteOptions { SampleRate = rate });
                var info = (Dictionary<string, object>)JsonReader.Parse(File.ReadAllText(Path.Combine(folder, "Info.dat")));
                var dat = (Dictionary<string, object>)JsonReader.Parse(File.ReadAllText(Path.Combine(folder, BeatmapSerializer.DifficultyFileName)));
                double bpm = (double)info["_beatsPerMinute"];
                var notes = ((List<object>)dat["_notes"]).Cast<Dictionary<string, object>>()
                    .Select(n => (double)n["_time"] * 60 / bpm).Distinct().ToArray();
                Assert.Equal(0.0, (double)info["_songTimeOffset"]);
                var samples = new List<float>();
                using (var reader = new VorbisReader(Path.Combine(folder, "song.ogg")))
                {
                    Assert.Equal(rate, reader.SampleRate);
                    var buffer = new float[8192]; int count;
                    while ((count = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
                        for (int i = 0; i < count; i += reader.Channels) samples.Add(buffer[i]);
                }
                double maxError = 0;
                // カウントインと本体の全クリックを照合。正弦波のピーク時刻ではなく、
                // 波形全体の相互相関で開始位置を推定し、23.22ms のずれも検出する。
                foreach (var click in map.Clicks)
                {
                    var voice = click.CountIn ? ClickVoice.CountIn : ClickVoice.For(tone, click.Accent);
                    var template = ClickTrackRenderer.Render(new[] { new ClickEvent(0, click.Accent) }, voice.DurationSeconds, rate, voice, voice);
                    int expected = (int)Math.Round(click.TimeSeconds * rate);
                    int bestLag = 0; double best = double.NegativeInfinity;
                    for (int lag = -(int)(rate * .030); lag <= rate * .030; lag++)
                    {
                        double dot = 0;
                        for (int i = 0; i < template.Length; i++)
                        {
                            int j = expected + lag + i;
                            if (j >= 0 && j < samples.Count) dot += template[i] * samples[j];
                        }
                        if (dot > best) { best = dot; bestLag = lag; }
                    }
                    double error = Math.Abs(bestLag * 1000.0 / rate);
                    maxError = Math.Max(maxError, error);
                    Assert.InRange(error, 0, 0.3);
                }
                foreach (double time in notes)
                    Assert.Contains(map.Clicks, c => Math.Abs(c.TimeSeconds - time) < .00001);
                Assert.True(samples.Max(x => Math.Abs(x)) > .3);
                // Vorbis の誤差を含めても振り切れない
                Assert.True(samples.Max(x => Math.Abs(x)) <= 1f);
                output.WriteLine($"{tone} {rate}Hz interval={interval}ms notes={notes.Length} clicks={map.Clicks.Count} max alignment error={maxError:F4}ms");
                Assert.Equal(tone, DrillRebuild.Read(folder).ClickTone);
                Assert.Equal(tone, DrillRebuild.Read(DrillRebuild.Rewrite(folder).Folder).ClickTone);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Standard_audio_is_unchanged_and_low_audio_is_distinct()
        {
            var map = DrillGenerator.Generate(new DrillOptions { Sequences = SequenceParser.ParseAll("R:8>b"), DurationSeconds = 1 });
            var original = ClickTrackRenderer.Render(map.Clicks, map.TotalSeconds, 44100, ClickVoice.Normal, ClickVoice.Accent);
            Assert.Equal(original, ClickTrackRenderer.Render(map));
            map.Options.ClickTone = ClickTone.Low;
            Assert.False(original.SequenceEqual(ClickTrackRenderer.Render(map)));
        }

        [Fact]
        public void Standard_stage_and_export_keep_tone_without_changing_identity()
        {
            var entry = DrillSet.Entries("8b")[1];
            var low = entry.WithClick(ClickMode.Up, 4, ClickTone.Low);
            Assert.Equal(entry.Options.Name, low.Options.Name);
            Assert.Equal(ClickTone.Standard, entry.Options.ClickTone);
            Assert.All(DrillSet.All(ClickMode.All, 4, ClickTone.Low), e => Assert.Equal(ClickTone.Low, e.Options.ClickTone));
            var w = new JsonWriter();
            DrillRebuild.WriteSettings(w, low.Options);
            var settings = (Dictionary<string, object>)JsonReader.Parse(w.ToString());
            var restored = entry.Options.Clone();
            DrillRebuild.ApplySettings(restored, settings);
            Assert.Equal(ClickTone.Low, restored.ClickTone);
            foreach (string text in new[] { "unknown", "123" })
            {
                var legacy = entry.Options.Clone();
                DrillRebuild.ApplySettings(legacy, new Dictionary<string, object> { { "clickTone", text } });
                Assert.Equal(ClickTone.Standard, legacy.ClickTone);
            }
            string root = Path.Combine(Path.GetTempPath(), "jd-tone-stage-" + Guid.NewGuid().ToString("N"));
            try
            {
                string folder = DrillSetWriter.Write(root, new[] { low }, new LevelWriteOptions()).Single();
                Assert.Equal(ClickTone.Low, DrillRebuild.Read(folder).ClickTone);
                Assert.Equal(ClickTone.Low, DrillRebuild.Read(DrillRebuild.Rewrite(folder).Folder).ClickTone);
                string info = Path.Combine(folder, "Info.dat");
                File.WriteAllText(info, File.ReadAllText(info).Replace("\"clickTone\": \"Low\",", ""));
                Assert.Equal(ClickTone.Standard, DrillRebuild.Read(folder).ClickTone);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        public static IEnumerable<object[]> BassTones()
        {
            foreach (var tone in new[] { ClickTone.Kick, ClickTone.Retro, ClickTone.SynthBass })
                yield return new object[] { tone };
        }

        // 1発分の波形のゼロ交差から、おおよその基本周波数を出す（後半の安定したところ）
        private static double ZeroCrossHz(float[] wave, int rate, double from, double to)
        {
            int a = (int)(from * rate), b = Math.Min(wave.Length - 1, (int)(to * rate));
            var ups = new List<int>();
            for (int i = a; i < b; i++) if (wave[i] <= 0 && wave[i + 1] > 0) ups.Add(i);
            return ups.Count < 2 ? 0 : (ups.Count - 1) * (double)rate / (ups[ups.Count - 1] - ups[0]);
        }

        [Theory]
        [MemberData(nameof(BassTones))]
        public void Bass_tones_sound_without_clipping_and_end_before_next_click(ClickTone tone)
        {
            foreach (int rate in new[] { 44100, 48000 })
            foreach (bool accent in new[] { false, true })
            {
                var voice = ClickVoice.For(tone, accent);
                var wave = ClickTrackRenderer.Render(new[] { new ClickEvent(0, accent) }, 0.2, rate, voice, voice);
                double peak = wave.Max(x => Math.Abs(x));
                Assert.InRange(peak, 0.4, 0.96);
                // All・250BPM（120ms 間隔）の次のクリックまでに鳴り終わる
                Assert.True(voice.DurationSeconds <= 0.110);
                Assert.All(wave.Skip((int)(0.110 * rate) + 1), x => Assert.Equal(0f, x));
                // 立ち上がりが速い（拍の位置が分かる）
                int first = Array.FindIndex(wave, x => Math.Abs(x) > peak * 0.5);
                Assert.InRange(first * 1000.0 / rate, 0, 15);
                // 低音（正弦波 160/240Hz）と波形が違う
                var low = ClickVoice.For(ClickTone.Low, accent);
                Assert.False(wave.SequenceEqual(ClickTrackRenderer.Render(new[] { new ClickEvent(0, accent) }, 0.2, rate, low, low)));
                output.WriteLine($"{tone} {rate}Hz accent={accent} peak={peak:F3} f0={ZeroCrossHz(wave, rate, 0.04, 0.10):F1}Hz len={voice.DurationSeconds * 1000:F0}ms");
            }
            // 強拍は通常より音程が高い（約 1.5 倍）
            {
                const int rate = 44100;
                var n = ClickVoice.For(tone, false); var a = ClickVoice.For(tone, true);
                double fn = ZeroCrossHz(ClickTrackRenderer.Render(new[] { new ClickEvent(0, false) }, 0.2, rate, n, n), rate, 0.04, 0.10);
                double fa = ZeroCrossHz(ClickTrackRenderer.Render(new[] { new ClickEvent(0, true) }, 0.2, rate, a, a), rate, 0.04, 0.10);
                Assert.InRange(fa / fn, 1.35, 1.7);
            }
        }

        [Theory]
        [MemberData(nameof(BassTones))]
        public void Bass_tones_do_not_overlap_or_clip_at_fast_intervals(ClickTone tone)
        {
            const int rate = 44100;
            foreach (double interval in new[] { 0.120, 0.075, 0.060, 0.030 })
            foreach (bool accent in new[] { false, true })
            {
                var voice = ClickVoice.For(tone, accent);
                var clicks = Enumerable.Range(0, 20).Select(i => new ClickEvent(0.1 + i * interval, accent)).ToArray();
                var pcm = ClickTrackRenderer.Render(clicks, 0.5 + 20 * interval, rate, voice, voice);
                double single = ClickTrackRenderer.Render(new[] { new ClickEvent(0.1, accent) }, 0.3, rate, voice, voice).Max(x => Math.Abs(x));
                // 重ならないので、1発だけのときより大きくならず、±1 に届かない
                Assert.True(pcm.Max(x => Math.Abs(x)) <= single + 1e-6, $"{tone} {interval}");
                Assert.True(pcm.Max(x => Math.Abs(x)) < 1f);
                // 次のクリックの直前は無音まで落ちている
                for (int i = 1; i < clicks.Length; i++)
                {
                    int at = (int)Math.Round(clicks[i].TimeSeconds * rate);
                    Assert.True(Math.Abs(pcm[at - 1]) < 0.02, $"{tone} {interval} click {i}: {pcm[at - 1]}");
                }
            }
        }

        [Fact]
        public void Bass_tone_drill_at_400bpm_all_with_tighten_does_not_clip()
        {
            foreach (var tone in new[] { ClickTone.Kick, ClickTone.Retro, ClickTone.SynthBass })
            {
                var options = new DrillOptions {
                    Sequences = SequenceParser.ParseAll("R:8>b"), IntervalMs = 75, FlickPercent = 50,
                    DurationSeconds = 2, CountInClicks = 8, Click = ClickMode.All, ClickTone = tone
                };
                var map = DrillGenerator.Generate(options);
                var pcm = ClickTrackRenderer.Render(map);
                Assert.True(pcm.Max(x => Math.Abs(x)) <= 0.95f, tone.ToString());
            }
        }

        [Fact]
        public void Every_tone_is_different_and_standard_low_are_unchanged()
        {
            // 低音・標準は以前と同じ正弦波のまま
            Assert.Equal(ClickShape.Sine, ClickVoice.For(ClickTone.Low, false).Shape);
            Assert.Equal(160.0, ClickVoice.For(ClickTone.Low, false).FrequencyHz);
            Assert.Equal(1000.0, ClickVoice.For(ClickTone.Standard, false).FrequencyHz);
            var map = DrillGenerator.Generate(new DrillOptions { Sequences = SequenceParser.ParseAll("R:8>b"), DurationSeconds = 1 });
            var rendered = new List<float[]>();
            foreach (ClickTone tone in Enum.GetValues(typeof(ClickTone)))
            {
                map.Options.ClickTone = tone;
                var audio = ClickTrackRenderer.Render(map);
                Assert.True(audio.Max(x => Math.Abs(x)) > .3);
                Assert.DoesNotContain(rendered, r => r.SequenceEqual(audio));
                rendered.Add(audio);
            }
        }

        [Theory]
        [MemberData(nameof(BassTones))]
        public void Bass_tones_are_saved_without_changing_identity(ClickTone tone)
        {
            var entry = DrillSet.Entries("8b")[1];
            var withTone = entry.WithClick(ClickMode.All, 8, tone);
            Assert.Equal(entry.Options.Name, withTone.Options.Name);
            Assert.Equal(DrillNaming.ExtractId(entry.Options.Name), DrillNaming.ExtractId(withTone.Options.Name));
            string root = Path.Combine(Path.GetTempPath(), "jd-tone-bass-" + Guid.NewGuid().ToString("N"));
            try
            {
                string folder = DrillSetWriter.Write(root, new[] { withTone }, new LevelWriteOptions()).Single();
                Assert.Equal(tone, DrillRebuild.Read(folder).ClickTone);
                var rebuilt = DrillRebuild.Rewrite(folder).Folder;
                Assert.Equal(tone, DrillRebuild.Read(rebuilt).ClickTone);
                Assert.Equal(DrillNaming.ExtractId(entry.Options.Name), DrillNaming.ExtractId(DrillRebuild.Read(rebuilt).Name));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

    }
}
