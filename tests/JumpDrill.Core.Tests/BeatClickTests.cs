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
    /// <summary>粒度 Beat：全ノーツを、小節の頭は強く、拍の頭はふつう、拍の間は弱く鳴らす。</summary>
    public class BeatClickTests
    {
        private readonly ITestOutputHelper output;
        public BeatClickTests(ITestOutputHelper output) { this.output = output; }

        public static IEnumerable<object[]> Tones()
        {
            foreach (ClickTone tone in Enum.GetValues(typeof(ClickTone)))
                yield return new object[] { tone };
        }

        private static DrillOptions Options(double bpm, ClickMode click, ClickTone tone = ClickTone.Standard, string seq = "R:8>b",
            HandPattern hands = HandPattern.Sync, double notesPerBeat = 2, double sec = 4)
        {
            return new DrillOptions {
                Sequences = SequenceParser.ParseAll(seq), IntervalMs = Tempo.IntervalMsFromBpm(bpm, notesPerBeat),
                NotesPerBeat = notesPerBeat, Bpm = bpm, HandPattern = hands,
                DurationSeconds = sec, CountInClicks = 8, Click = click, ClickTone = tone
            };
        }

        [Fact]
        public void Beat_marks_the_bar_the_beat_and_the_offbeat()
        {
            var map = DrillGenerator.Generate(Options(250, ClickMode.Beat));
            var body = map.Clicks.Where(c => !c.CountIn).ToList();
            Assert.Equal(map.Notes.Select(n => n.TimeSeconds).Distinct().Count(), body.Count);
            for (int i = 0; i < body.Count; i++)
            {
                // 1/2 なので 2 ノーツで1拍、8 ノーツで1小節
                Assert.Equal(i % 8 == 0, body[i].Accent);
                Assert.Equal(i % 2 == 1, body[i].Weak);
            }
            // カウントインは今のまま（強拍の固定音）
            Assert.All(map.Clicks.Where(c => c.CountIn), c => { Assert.True(c.Accent); Assert.False(c.Weak); });
            Assert.Equal(8, map.Clicks.Count(c => c.CountIn));
        }

        [Fact]
        public void Beat_counts_from_the_head_of_each_block()
        {
            // 片手ずつ（2ブロック）で、1ブロックのノーツ数が小節で割り切れなくても2ブロック目の頭が強拍
            var options = Options(250, ClickMode.Beat, seq: "R:8>b, L:8>b", hands: HandPattern.Split, sec: 1.3);
            var map = DrillGenerator.Generate(options);
            Assert.Equal(2, map.BlockCount);
            var body = map.Clicks.Where(c => !c.CountIn).ToList();
            int perBlock = body.Count / 2;
            Assert.NotEqual(0, perBlock % 8);
            Assert.True(body[0].Accent);
            Assert.True(body[perBlock].Accent);
            Assert.True(body[perBlock + 1].Weak);
        }

        [Fact]
        public void Beat_sounds_at_the_same_times_as_all_and_other_modes_have_no_weak_clicks()
        {
            var beat = DrillGenerator.Generate(Options(400, ClickMode.Beat));
            var all = DrillGenerator.Generate(Options(400, ClickMode.All));
            Assert.Equal(all.Clicks.Select(c => c.TimeSeconds), beat.Clicks.Select(c => c.TimeSeconds));
            Assert.Equal(all.LeadInSeconds, beat.LeadInSeconds);
            Assert.Equal(all.ClickPeriodSeconds, beat.ClickPeriodSeconds);
            foreach (ClickMode mode in Enum.GetValues(typeof(ClickMode)))
            {
                if (mode == ClickMode.Beat) continue;
                Assert.DoesNotContain(DrillGenerator.Generate(Options(400, mode)).Clicks, c => c.Weak);
            }
        }

        [Fact]
        public void One_note_per_beat_has_no_offbeat()
        {
            var map = DrillGenerator.Generate(Options(200, ClickMode.Beat, notesPerBeat: 1));
            var body = map.Clicks.Where(c => !c.CountIn).ToList();
            Assert.DoesNotContain(body, c => c.Weak);
            for (int i = 0; i < body.Count; i++) Assert.Equal(i % 4 == 0, body[i].Accent);
        }

        [Fact]
        public void Two_beats_per_note_marks_every_second_note_as_the_bar()
        {
            // 2/1（1ノーツで2拍）なら 2ノーツで1小節。拍の間は無い
            var map = DrillGenerator.Generate(Options(120, ClickMode.Beat, notesPerBeat: 0.5));
            var body = map.Clicks.Where(c => !c.CountIn).ToList();
            Assert.DoesNotContain(body, c => c.Weak);
            for (int i = 0; i < body.Count; i++) Assert.Equal(i % 2 == 0, body[i].Accent);
        }

        [Theory]
        [InlineData(HandPattern.Alternate, 0)]
        [InlineData(HandPattern.AlternateRoundTrip, 0)]
        [InlineData(HandPattern.Sync, 50)]
        public void Hand_patterns_and_tighten_keep_the_beat_on_every_note(HandPattern hands, double flick)
        {
            const int rate = 44100;
            var options = Options(400, ClickMode.Beat, ClickTone.Kick, seq: "R:8>b, L:5>a", hands: hands);
            options.FlickPercent = flick;
            var map = DrillGenerator.Generate(options);
            var body = map.Clicks.Where(c => !c.CountIn).ToList();
            var noteTimes = map.Notes.Select(n => n.TimeSeconds).Distinct().OrderBy(t => t).ToList();
            Assert.Equal(noteTimes.Count, body.Count);
            for (int i = 0; i < body.Count; i++)
            {
                Assert.Equal(noteTimes[i], body[i].TimeSeconds, 9);
                Assert.Equal(i % 8 == 0, body[i].Accent);
                Assert.Equal(i % 2 == 1, body[i].Weak);
            }
            Assert.True(ClickTrackRenderer.Render(map, rate).Max(x => Math.Abs(x)) < 1f);
        }

        private static double Peak(float[] pcm, int rate, double at, double window = 0.020)
        {
            int a = (int)Math.Round(at * rate), n = (int)(window * rate);
            double peak = 0;
            for (int i = a; i < a + n && i < pcm.Length; i++) peak = Math.Max(peak, Math.Abs(pcm[i]));
            return peak;
        }

        [Theory]
        [MemberData(nameof(Tones))]
        public void Three_levels_differ_in_volume_or_pitch_without_clipping_or_overlap(ClickTone tone)
        {
            const int rate = 44100;
            foreach (double bpm in new[] { 120.0, 250.0, 400.0 })
            {
                var map = DrillGenerator.Generate(Options(bpm, ClickMode.Beat, tone));
                var pcm = ClickTrackRenderer.Render(map, rate);
                var body = map.Clicks.Where(c => !c.CountIn).ToList();
                double bar = body.Where(c => c.Accent).Average(c => Peak(pcm, rate, c.TimeSeconds));
                double beat = body.Where(c => !c.Accent && !c.Weak).Average(c => Peak(pcm, rate, c.TimeSeconds));
                double weak = body.Where(c => c.Weak).Average(c => Peak(pcm, rate, c.TimeSeconds));
                output.WriteLine($"{tone} {bpm}BPM peak bar={bar:F3} beat={beat:F3} weak={weak:F3}");
                // 拍の間は拍の頭より 6 dB 以上小さい
                Assert.True(weak < beat * 0.5, $"{tone} {bpm}: weak {weak} beat {beat}");
                // 小節の頭は拍の頭と音程が違い（5 度〜1 オクターブ上）、音量はほとんど下がらない
                Assert.True(bar >= beat * 0.9, $"{tone} {bpm}: bar {bar} beat {beat}");
                Assert.True(ClickVoice.For(tone, true).FrequencyHz >= ClickVoice.For(tone, false).FrequencyHz * 1.4);
                Assert.True(pcm.Max(x => Math.Abs(x)) < 1f);
                // 次のクリックの直前は無音まで落ちている（前の音が重ならない）
                var starts = map.Clicks.Select(c => (int)Math.Round(c.TimeSeconds * rate)).ToList();
                for (int i = 1; i < starts.Count; i++)
                    Assert.True(Math.Abs(pcm[starts[i] - 1]) < 0.02, $"{tone} {bpm} click {i}: {pcm[starts[i] - 1]}");
            }
        }

        [Fact]
        public void Standard_beat_uses_2000_and_1000_hz()
        {
            const int rate = 44100;
            var map = DrillGenerator.Generate(Options(250, ClickMode.Beat));
            var pcm = ClickTrackRenderer.Render(map, rate);
            foreach (var c in map.Clicks.Where(c => !c.CountIn).Take(16))
                Assert.InRange(CountInToneTests.DominantHz(pcm, rate, c.TimeSeconds, 0.015), c.Accent ? 1950 : 950, c.Accent ? 2050 : 1050);
        }

        [Theory]
        [MemberData(nameof(Tones))]
        public void Ogg_at_400bpm_stays_below_full_scale(ClickTone tone)
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-beat-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = Options(400, ClickMode.Beat, tone, sec: 3);
                options.Name = DrillNaming.Compose(options);
                string folder = LevelWriter.Write(DrillGenerator.Generate(options), root);
                float max = 0;
                using (var reader = new VorbisReader(Path.Combine(folder, "song.ogg")))
                {
                    var buffer = new float[8192]; int count;
                    while ((count = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
                        for (int i = 0; i < count; i++) max = Math.Max(max, Math.Abs(buffer[i]));
                }
                output.WriteLine($"{tone}: max {max:F3}");
                Assert.True(max < 1f, $"{tone}: {max}");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Beat_is_saved_and_read_back_without_changing_the_id()
        {
            var standard = DrillSet.All();
            var beat = DrillSet.All(ClickMode.Beat);
            for (int i = 0; i < standard.Count; i++)
            {
                Assert.Equal(standard[i].Options.Name, beat[i].Options.Name);
                Assert.Equal(ClickMode.Beat, beat[i].Options.Click);
            }

            string root = Path.Combine(Path.GetTempPath(), "jd-beat-save-" + Guid.NewGuid().ToString("N"));
            try
            {
                var entry = DrillSet.Entries("8b")[2];
                string folder = DrillSetWriter.Write(root, new[] { entry.WithClick(ClickMode.Beat, 8, ClickTone.Kick) }, new LevelWriteOptions()).Single();
                Assert.Contains("\"click\": \"Beat\"", File.ReadAllText(Path.Combine(folder, "Info.dat")));
                var read = DrillRebuild.Read(folder);
                Assert.Equal(ClickMode.Beat, read.Click);
                Assert.Equal(ClickTone.Kick, read.ClickTone);
                Assert.Equal(2.0, read.NotesPerBeat);
                var rebuilt = DrillRebuild.Rewrite(folder);
                Assert.False(rebuilt.Renamed);
                Assert.Equal(ClickMode.Beat, DrillRebuild.Read(rebuilt.Folder).Click);
                Assert.Equal(DrillNaming.ExtractId(entry.Options.Name), DrillNaming.ExtractId(DrillRebuild.Read(rebuilt.Folder).Name));

                // 自分で作ったドリル（標準のステージ以外）も読み直せる
                var custom = Options(300, ClickMode.Beat, ClickTone.Low);
                custom.Name = DrillNaming.Compose(custom);
                string customFolder = LevelWriter.Write(DrillGenerator.Generate(custom), root);
                Assert.Equal(ClickMode.Beat, DrillRebuild.Read(customFolder).Click);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
