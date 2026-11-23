using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JumpDrill.Audio;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;
using NVorbis;
using Xunit;
using Xunit.Abstractions;

namespace JumpDrill.Tests
{
    /// <summary>カウントインは、どのクリック音色を選んでもクリックと聞き分けられる音で鳴る。</summary>
    public class CountInToneTests
    {
        private readonly ITestOutputHelper output;
        public CountInToneTests(ITestOutputHelper output) { this.output = output; }

        public static IEnumerable<object[]> Tones()
        {
            foreach (ClickTone tone in Enum.GetValues(typeof(ClickTone)))
                yield return new object[] { tone };
        }

        /// <summary>開始位置から window 秒の中で一番強い周波数（40〜4000Hz を 10Hz 刻みで探す）。</summary>
        internal static double DominantHz(IList<float> pcm, int rate, double startSeconds, double window = 0.025)
        {
            int a = (int)Math.Round(startSeconds * rate) + (int)(0.001 * rate);
            int n = (int)(window * rate);
            double bestHz = 0, best = -1;
            for (double hz = 40; hz <= 4000; hz += 10)
            {
                double w = 2 * Math.PI * hz / rate, re = 0, im = 0;
                for (int i = 0; i < n && a + i < pcm.Count; i++)
                {
                    double x = pcm[a + i];
                    re += x * Math.Cos(w * i);
                    im += x * Math.Sin(w * i);
                }
                double p = re * re + im * im;
                if (p > best) { best = p; bestHz = hz; }
            }
            return bestHz;
        }

        private static DrillMap Map(ClickTone tone, ClickMode click, double intervalMs = 220)
        {
            return DrillGenerator.Generate(new DrillOptions {
                Sequences = SequenceParser.ParseAll("R:8>b"), IntervalMs = intervalMs,
                DurationSeconds = 2, CountInClicks = 8, Click = click, ClickTone = tone
            });
        }

        [Theory]
        [MemberData(nameof(Tones))]
        public void Count_in_uses_the_fixed_high_tone_for_every_click_tone(ClickTone tone)
        {
            const int rate = 44100;
            foreach (var mode in new[] { ClickMode.Down, ClickMode.All })
            {
                var map = Map(tone, mode);
                var pcm = ClickTrackRenderer.Render(map, rate);
                var countIns = map.Clicks.Where(c => c.CountIn).ToList();
                var clicks = map.Clicks.Where(c => !c.CountIn).ToList();
                Assert.Equal(8, countIns.Count);
                Assert.NotEmpty(clicks);
                foreach (var c in countIns)
                    Assert.InRange(DominantHz(pcm, rate, c.TimeSeconds), 1950, 2050);
                foreach (var c in clicks)
                {
                    double hz = DominantHz(pcm, rate, c.TimeSeconds);
                    // 標準の強拍（All の切り下げ）は以前からカウントインと同じ 2000Hz。それ以外は 1 オクターブ近く離れる
                    if (tone == ClickTone.Standard && c.Accent) { Assert.InRange(hz, 1950, 2050); continue; }
                    Assert.True(hz <= 1100, $"{tone} {mode} click {c.TimeSeconds:F3}s: {hz}Hz");
                }
                output.WriteLine($"{tone} {mode}: count-in {DominantHz(pcm, rate, countIns[0].TimeSeconds)}Hz, first click {DominantHz(pcm, rate, clicks[0].TimeSeconds)}Hz");
            }
        }

        [Theory]
        [MemberData(nameof(Tones))]
        public void Every_block_count_in_uses_the_fixed_tone(ClickTone tone)
        {
            const int rate = 44100;
            // 片手ずつ（右→左）。2 ブロック目の前のカウントインも同じ音
            var map = DrillGenerator.Generate(new DrillOptions {
                Sequences = SequenceParser.ParseAll("R:8>b, L:5>a"), HandPattern = HandPattern.Split, IntervalMs = 120,
                DurationSeconds = 2, CountInClicks = 8, Click = ClickMode.Down, ClickTone = tone
            });
            Assert.Equal(2, map.BlockCount);
            var pcm = ClickTrackRenderer.Render(map, rate);
            var countIns = map.Clicks.Where(c => c.CountIn).ToList();
            Assert.Equal(16, countIns.Count);
            foreach (var c in countIns) Assert.InRange(DominantHz(pcm, rate, c.TimeSeconds), 1950, 2050);
            foreach (var c in map.Clicks.Where(c => !c.CountIn)) Assert.True(DominantHz(pcm, rate, c.TimeSeconds) <= 1100);
        }

        [Fact]
        public void Count_in_sound_is_the_same_as_before_tones_were_added()
        {
            // HEAD（e111a62）はカウントインを ClickVoice.Accent（2000Hz 正弦・0.95・50ms）で鳴らしていた
            var map = Map(ClickTone.Kick, ClickMode.Down);
            var pcm = ClickTrackRenderer.Render(map);
            var countInOnly = map.Clicks.Where(c => c.CountIn).ToList();
            var expected = ClickTrackRenderer.Render(countInOnly, map.TotalSeconds, 44100, ClickVoice.Normal, ClickVoice.Accent);
            double first = map.Clicks.Where(c => !c.CountIn).Min(c => c.TimeSeconds);
            int end = (int)Math.Round(first * 44100);
            for (int i = 0; i < end; i++) Assert.Equal(expected[i], pcm[i]);
        }

        [Theory]
        [MemberData(nameof(Tones))]
        public void None_keeps_only_the_count_in(ClickTone tone)
        {
            const int rate = 44100;
            var map = Map(tone, ClickMode.None);
            Assert.All(map.Clicks, c => Assert.True(c.CountIn));
            Assert.Equal(8, map.Clicks.Count);
            var pcm = ClickTrackRenderer.Render(map, rate);
            foreach (var c in map.Clicks) Assert.InRange(DominantHz(pcm, rate, c.TimeSeconds), 1950, 2050);
            // カウントインが鳴り終わった後（ノーツの区間）は無音
            int from = (int)Math.Round((map.Clicks.Max(c => c.TimeSeconds) + 0.06) * rate);
            int to = pcm.Length;
            Assert.True(to - from > rate);
            for (int i = from; i < to; i++) Assert.Equal(0f, pcm[i]);
        }

        [Theory]
        [MemberData(nameof(Tones))]
        public void Fast_400bpm_all_does_not_clip(ClickTone tone)
        {
            var map = DrillGenerator.Generate(new DrillOptions {
                Sequences = SequenceParser.ParseAll("R:8>b"), IntervalMs = 75, FlickPercent = 50,
                DurationSeconds = 2, CountInClicks = 8, Click = ClickMode.All, ClickTone = tone
            });
            var pcm = ClickTrackRenderer.Render(map);
            Assert.True(pcm.Max(x => Math.Abs(x)) <= 0.95f, tone.ToString());
        }

        [Theory]
        [MemberData(nameof(Tones))]
        public void Written_ogg_keeps_count_in_and_click_apart(ClickTone tone)
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-countin-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new DrillOptions {
                    Sequences = SequenceParser.ParseAll("R:8>b"), IntervalMs = 75,
                    DurationSeconds = 2, CountInClicks = 8, Click = ClickMode.All, ClickTone = tone
                };
                options.Name = DrillNaming.Compose(options);
                var map = DrillGenerator.Generate(options);
                string folder = LevelWriter.Write(map, root);
                var samples = new List<float>();
                int rate;
                using (var reader = new VorbisReader(Path.Combine(folder, "song.ogg")))
                {
                    rate = reader.SampleRate;
                    var buffer = new float[8192]; int count;
                    while ((count = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
                        for (int i = 0; i < count; i += reader.Channels) samples.Add(buffer[i]);
                }
                Assert.True(samples.Max(x => Math.Abs(x)) <= 1f);
                double countIn = DominantHz(samples, rate, map.Clicks.First(c => c.CountIn).TimeSeconds, 0.020);
                double click = DominantHz(samples, rate, map.Clicks.First(c => !c.CountIn && !c.Accent).TimeSeconds, 0.020);
                Assert.InRange(countIn, 1950, 2050);
                Assert.True(click <= 1100, $"{tone}: {click}Hz");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
