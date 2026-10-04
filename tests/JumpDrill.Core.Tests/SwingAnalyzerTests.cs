using System;
using System.Collections.Generic;
using System.Linq;
using JumpDrill.Model;
using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>
    /// 再現性の出し方。切る位置は当てにならないので、
    /// 同じ振りをどれだけ同じ軌道で繰り返せているかを主指標にしている。
    /// </summary>
    public class ArcLengthTests
    {
        private static Vector3 V(double x, double y, double z)
        {
            return new Vector3 { X = (float)x, Y = (float)y, Z = (float)z };
        }

        [Fact]
        public void Resampling_spreads_points_evenly_along_the_path()
        {
            // 長さ 3 の直線。5点に取り直せば 0, 0.75, 1.5, 2.25, 3 になる。
            var line = new[] { V(0, 0, 0), V(3, 0, 0) };
            var result = SwingAnalyzer.ResampleByArcLength(line, 5);

            Assert.Equal(5, result.Length);
            for (int i = 0; i < 5; i++)
                Assert.Equal(3.0 * i / 4.0, result[i].X, 4);
        }

        [Fact]
        public void Resampling_ignores_how_fast_the_path_was_traced()
        {
            // 同じ直線を、密な点と粗い点で辿ったもの。
            // 弧長で取り直せば同じ形になる（速さの違いを畳むのが狙い）。
            var dense = new List<Vector3>();
            for (int i = 0; i <= 100; i++) dense.Add(V(i / 100.0, 0, 0));
            var sparse = new[] { V(0, 0, 0), V(0.5, 0, 0), V(1, 0, 0) };

            var a = SwingAnalyzer.ResampleByArcLength(dense, 8);
            var b = SwingAnalyzer.ResampleByArcLength(sparse, 8);

            for (int i = 0; i < 8; i++)
                Assert.Equal(a[i].X, b[i].X, 4);
        }

        [Fact]
        public void A_still_saber_resamples_to_one_point()
        {
            var stuck = new[] { V(1, 2, 3), V(1, 2, 3), V(1, 2, 3) };
            var result = SwingAnalyzer.ResampleByArcLength(stuck, 4);

            Assert.All(result, p => Assert.Equal(1f, p.X));
        }

        [Fact]
        public void Identical_paths_have_no_spread()
        {
            var path = SwingAnalyzer.ResampleByArcLength(new[] { V(0, 0, 0), V(1, 1, 0) }, 8);
            var spread = SwingAnalyzer.SpreadFromMean(new[] { path, path, path });

            Assert.Equal(0.0, spread, 9);
        }

        [Fact]
        public void Spread_grows_with_the_scatter()
        {
            var a = SwingAnalyzer.ResampleByArcLength(new[] { V(0, 0, 0), V(1, 0, 0) }, 8);
            var b = SwingAnalyzer.ResampleByArcLength(new[] { V(0, 0.1, 0), V(1, 0.1, 0) }, 8);
            var c = SwingAnalyzer.ResampleByArcLength(new[] { V(0, 0.3, 0), V(1, 0.3, 0) }, 8);

            double tight = SwingAnalyzer.SpreadFromMean(new[] { a, b });
            double loose = SwingAnalyzer.SpreadFromMean(new[] { a, c });

            Assert.True(loose > tight, "散らばりが大きい方が値も大きくなるはず");
            Assert.Equal(0.05, tight, 3);   // ±0.05 に平均が来るので RMS も 0.05
        }

        [Theory]
        [InlineData(2)]
        [InlineData(1)]
        public void Resampling_needs_at_least_two_points(int count)
        {
            var points = Enumerable.Repeat(V(0, 0, 0), count).ToArray();
            if (count < 2)
                Assert.Throws<ArgumentException>(() => SwingAnalyzer.ResampleByArcLength(points, 4));
            else
                Assert.Equal(4, SwingAnalyzer.ResampleByArcLength(points, 4).Length);
        }

        [Fact]
        public void Standard_deviation_matches_the_usual_definition()
        {
            // 標本標準偏差（n-1）。
            var values = new List<double> { 2, 4, 4, 4, 5, 5, 7, 9 };
            Assert.Equal(2.138, SwingAnalyzer.StandardDeviation(values), 3);
            Assert.Equal(0.0, SwingAnalyzer.StandardDeviation(new List<double> { 1 }), 9);
        }

        [Theory]
        // 跳び幅の 5% までは直線とみなして 100、50% で 0。
        [InlineData(0.00, 1.0, 100.0)]
        [InlineData(0.05, 1.0, 100.0)]
        [InlineData(0.50, 1.0, 0.0)]
        [InlineData(1.00, 1.0, 0.0)]
        [InlineData(0.275, 1.0, 50.0)]
        // 跳び幅で割るので、大きく跳ぶドリルは同じずれでも有利になる。
        [InlineData(0.10, 0.5, 66.6667)]
        [InlineData(0.10, 2.0, 100.0)]
        public void Reproducibility_is_the_spread_relative_to_the_jump(double spread, double length, double expected)
        {
            Assert.Equal(expected, SwingAnalyzer.Reproducibility(spread, length), 3);
        }

        [Theory]
        // BPM は実測ではなく曲名の指定値から出す。100ms 指定が 99.7ms と出ても意味がない。
        [InlineData("Drill R:4>b L:1>a axis 100ms", 100.0)]
        [InlineData("Drill R:8>b L:5>a axis 171ms", 171.0)]
        [InlineData("Drill R:8>b axis 85ms", 85.0)]
        [InlineData("Cendrillon", 0.0)]
        [InlineData("", 0.0)]
        [InlineData("ms", 0.0)]
        public void The_note_interval_comes_from_the_song_name(string songName, double expected)
        {
            Assert.Equal(expected, SwingAnalyzer.DeclaredNoteInterval(songName), 6);
        }

        [Theory]
        // EBPM は「片手が BPM に対して 1/2 の精度で動く速さ」＝ 30000 / 片手の間隔。
        [InlineData("300ebpm", 1.0, 100.0)]
        [InlineData("300ebpm", 2.0, 100.0)]   // 細分化を渡しても無視する
        [InlineData("200ebpm", 1.0, 150.0)]
        // BPM の方は細分化で倍違う。
        [InlineData("300bpm", 1.0, 200.0)]
        [InlineData("300bpm", 2.0, 100.0)]
        [InlineData("110ms", 1.0, 110.0)]
        [InlineData("110", 1.0, 110.0)]
        public void The_interval_can_be_written_in_ebpm(string text, double notesPerBeat, double expected)
        {
            Assert.Equal(expected, JumpDrill.Parsing.IntervalParser.ParseIntervalMs(text, notesPerBeat), 3);
        }

        [Fact]
        public void Misses_do_not_slow_down_the_measured_interval()
        {
            // 譜面は 100ms 間隔。左手だけ1つ置きにミスしても、間隔は 100ms のまま。
            // 当たったノーツだけで測ると 200ms に見えてしまう。
            var replay = new Replay();
            replay.Info.SongName = "Drill R:3>b L:2>a axis 100ms";

            for (int i = 0; i < 40; i++)
            {
                float time = 1.0f + i * 0.1f;

                replay.Notes.Add(new ReplayNote
                {
                    NoteId = 2 * 1000 + (i % 2) * 100 + 1 * 10 + 6,
                    EventTime = time,
                    EventType = NoteEventType.Good,
                    Cut = new NoteCutInfo { SaberType = 1 },
                });

                replay.Notes.Add(new ReplayNote
                {
                    NoteId = 1 * 1000 + (i % 2) * 100 + 0 * 10 + 7,
                    EventTime = time,
                    EventType = i % 2 == 0 ? NoteEventType.Good : NoteEventType.Miss,
                    Cut = i % 2 == 0 ? new NoteCutInfo { SaberType = 0 } : null,
                });
            }

            Assert.Equal(100.0, SwingAnalyzer.MedianHandInterval(replay, 100.0), 3);
        }

        [Fact]
        public void The_wiki_example_pins_down_the_ebpm_formula()
        {
            // wiki の例: 100 BPM の曲の 1/4 精度・片手モーションは 200 EBPM。
            // 100 BPM の 1/4 は 150 ms なので、30000 / 150 = 200 になる。
            double quarterAt100Bpm = Tempo.IntervalMsFromBpm(100, 4);
            Assert.Equal(150.0, quarterAt100Bpm, 6);
            Assert.Equal(200.0, Tempo.EbpmFromHandIntervalMs(quarterAt100Bpm), 6);
        }

        [Fact]
        public void A_hundred_millisecond_one_handed_drill_is_three_hundred_ebpm()
        {
            Assert.Equal(300.0, Tempo.EbpmFromHandIntervalMs(100.0), 6);
            Assert.Equal(100.0, Tempo.HandIntervalMsFromEbpm(300.0), 6);
        }

        [Fact]
        public void A_hundred_millisecond_axis_is_three_hundred_bpm_at_a_half_beat()
        {
            // 1拍に2ノーツなら 300 BPM で 100ms。実測値だと端数が出るので指定値を使う。
            Assert.Equal(300.0, Tempo.BpmFromIntervalMs(100.0, 2), 6);
            Assert.Equal(175.4, Tempo.BpmFromIntervalMs(171.0, 2), 1);
        }

        [Theory]
        // 往復のずれには「避けようのないぶん」が無い。重なれば 100。
        [InlineData(0.00, 1.0, 100.0)]
        [InlineData(0.05, 1.0, 90.0)]
        [InlineData(0.25, 1.0, 50.0)]
        [InlineData(0.50, 1.0, 0.0)]
        [InlineData(0.80, 1.0, 0.0)]
        public void The_round_trip_scale_gives_full_marks_only_when_the_paths_overlap(
            double gap, double span, double expected)
        {
            Assert.Equal(expected, SwingAnalyzer.RoundTripPercent(gap, span), 3);
        }

        [Theory]
        // 要求どおりなら 100、直角にずれたら 0。
        [InlineData(0.0, 100.0)]
        [InlineData(9.0, 90.0)]
        [InlineData(45.0, 50.0)]
        [InlineData(90.0, 0.0)]
        [InlineData(120.0, 0.0)]
        [InlineData(-45.0, 50.0)]
        public void The_angle_percent_reaches_zero_at_a_right_angle(double degrees, double expected)
        {
            Assert.Equal(expected, SwingAnalyzer.AnglePercentOf(degrees), 6);
        }

        [Fact]
        public void Scatter_between_repetitions_counts_even_when_the_averages_line_up()
        {
            // 往路が +d、復路が -d に交互にぶれるだけの振り。
            // 往復の平均どうしを比べると打ち消し合って 0 に見えるが、
            // 1本ずつ見れば d のばらつきがそのまま残る。
            var line = SwingAnalyzer.Line.Between(
                new ReplayNote { NoteId = 2 * 1000 + 0 * 100 + 1 * 10 + 6 },
                new ReplayNote { NoteId = 2 * 1000 + 1 * 100 + 1 * 10 + 0 });

            var wobbly = new List<Vector3[]>();
            for (int i = 0; i < 8; i++)
            {
                double shift = i % 2 == 0 ? 0.10 : -0.10;
                wobbly.Add(Straight(line, shift));
            }

            var means = SwingAnalyzer.MeanPath(wobbly);
            var flat = SwingAnalyzer.AxisProfile(means, line, 0.55);

            // 平均は真ん中に来る（＝ずれが見えなくなる）。
            foreach (var value in flat)
                if (!double.IsNaN(value)) Assert.Equal(0.0, value, 3);
        }

        /// <summary>ノーツ間を直線で結び、軸から <paramref name="shift"/> だけ横にずらした振り。</summary>
        private static Vector3[] Straight(SwingAnalyzer.Line line, double shift)
        {
            var path = new Vector3[SwingAnalyzer.SamplesPerSwing];
            for (int i = 0; i < path.Length; i++)
            {
                double t = 0.55 * i / (path.Length - 1);
                path[i] = new Vector3
                {
                    X = (float)(line.X + line.DirX * t + line.NormalX * shift),
                    Y = (float)(line.Y + line.DirY * t + line.NormalY * shift),
                };
            }
            return path;
        }

        [Fact]
        public void A_machine_round_trip_really_does_reach_zero()
        {
            // 自動プレイの実測は両手とも 0.0000 m。弧を許容する必要がない根拠。
            Assert.Equal(100.0, SwingAnalyzer.RoundTripPercent(0.0, 0.81), 6);

            // 直線基準は手首の弧のぶん 5% を満点にしていた。1本の曲線には許容を置かない。
            Assert.Equal(100.0, SwingAnalyzer.Reproducibility(0.05 * 0.81, 0.81), 6);
            Assert.Equal(100.0 * (1.0 - 0.05 / SwingAnalyzer.RoundTripWayOff),
                SwingAnalyzer.RoundTripPercent(0.05 * 0.81, 0.81), 6);
        }

        [Fact]
        public void The_arc_a_swing_cannot_avoid_still_scores_full_marks()
        {
            // 自動プレイの実測: 跳び幅 0.81 m に対しずれ 0.0388 m（4.8%）。
            // セイバー先端は手首を中心に回るので、これ以上まっすぐには振れない。
            Assert.Equal(100.0, SwingAnalyzer.Reproducibility(0.0388, 0.81), 6);
        }

        [Fact]
        public void A_swing_of_no_length_cannot_be_scored()
        {
            // 動いていない＝比べる幅が無い。100% にすると「振らないのが満点」になる。
            Assert.Equal(0.0, SwingAnalyzer.Reproducibility(0.0, 0.0), 9);
        }

        [Fact]
        public void Score_never_goes_up_as_the_spread_grows()
        {
            double previous = double.MaxValue;
            for (double spread = 0.0; spread <= 1.2; spread += 0.02)
            {
                double score = SwingAnalyzer.Reproducibility(spread, 1.0);
                Assert.True(score <= previous + 1e-9, "ばらつきが増えたのに点が上がっている");
                previous = score;
            }
        }

        [Fact]
        public void Path_length_is_measured_along_the_line()
        {
            var l = new[] { V(0, 0, 0), V(0, 3, 0), V(4, 3, 0) };
            Assert.Equal(7.0, SwingAnalyzer.PathLength(l), 6);
        }
    }

    /// <summary>
    /// リプレイの集め方。BeatLeader と LocalLeaderboard が同じプレイを
    /// それぞれ保存するので、素直に集めると同じものが2件出る。
    /// </summary>
    public class ReplayLibraryTests
    {
        [Theory]
        // LocalLeaderboard は末尾に "_<長い数字>" を足すだけ。そこだけ落とす。
        [InlineData("76561198324870685-Drill R_8_b-Std-ABC-1788657947_639242548163678667",
                    "76561198324870685-Drill R_8_b-Std-ABC-1788657947")]
        // BeatLeader 側はそのまま。曲名の "_" で切ってはいけない。
        [InlineData("76561198324870685-Drill R_8_b-Std-ABC-1788657947",
                    "76561198324870685-Drill R_8_b-Std-ABC-1788657947")]
        [InlineData("A_B", "A_B")]
        [InlineData("A_12345", "A_12345")]
        [InlineData("", "")]
        public void Duplicate_replays_collapse_to_one_key(string name, string expected)
        {
            Assert.Equal(expected, ReplayLibrary.NormalizeName(name));
        }

        [Fact]
        public void The_drill_filter_does_not_catch_songs_that_merely_contain_drill()
        {
            // "Cendrillon" には "drill" が含まれる。末尾の空白まで見ないと当たる。
            Assert.DoesNotContain(ReplayLibrary.DrillFilter, "Cendrillon 10th Anniversary",
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(ReplayLibrary.DrillFilter, "Drill R:8>b L:5>a axis 171ms",
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>合成したリプレイで、解析全体が通ることを確かめる。</summary>
    public class SwingAnalyzerTests
    {
        /// <summary>
        /// 8(3,1) と b(2,0) を往復するドリルを組み立てる。
        /// <paramref name="jitter"/> で軌道の乱れを足す。
        /// </summary>
        /// <param name="backCenter">
        /// 0 より大きければ、バック（切り上げ）側のノーツだけ中心からの距離をこの値にする。
        /// フォアとバックが分かれているかを確かめるのに使う。
        /// </param>
        private static Replay BuildDrill(int swings, double jitter, int seed = 1, double lateMs = 0.0,
                                         double bow = 0.0, double backCenter = 0.0)
        {
            var random = new Random(seed);
            var replay = new Replay();
            replay.Info.SongName = "Drill R:8>b axis 300ms";

            const float Interval = 0.3f;
            float time = 1.0f;

            for (int i = 0; i < swings; i++)
            {
                bool toB = i % 2 == 0;
                int lineIndex = toB ? 2 : 3;
                int lineLayer = toB ? 0 : 1;

                replay.Notes.Add(new ReplayNote
                {
                    NoteId = lineIndex * 1000 + lineLayer * 100 + 1 * 10 + (toB ? 6 : 5),
                    EventTime = time,
                    EventType = NoteEventType.Good,
                    Cut = new NoteCutInfo
                    {
                        SaberType = 1,
                        // toB は矢印 6（DownLeft）＝フォア、戻りは 5（UpRight）＝バック。
                        CutDistanceToCenter = !toB && backCenter > 0.0 ? (float)backCenter : 0.05f,
                        TimeDeviation = (float)(lateMs / 1000.0),
                    },
                });

                // 打点の間を 8 フレームで埋める。
                for (int f = 0; f < 8; f++)
                {
                    float t = time + Interval * f / 8f;
                    float progress = f / 8f;
                    float wobble = (float)((random.NextDouble() - 0.5) * jitter);

                    replay.Frames.Add(new ReplayFrame
                    {
                        Time = t,
                        Right = new ReplayTransform
                        {
                            Position = new Vector3
                            {
                                X = (toB ? 0.4f - progress * 0.4f : progress * 0.4f) + wobble,
                                // bow は振りの真ん中で最も膨らむ。毎回同じ形。
                                Y = 1.2f + wobble + (float)(bow * Math.Sin(Math.PI * progress)),
                                Z = 0.5f,
                            },
                        },
                    });
                }

                time += Interval;
            }

            return replay;
        }

        [Theory]
        [InlineData(1, 0, true)]            // Down
        [InlineData(6, 0, true)]            // DownLeft
        [InlineData(0, 0, false)]           // Up
        [InlineData(5, 0, false)]           // UpRight
        public void The_arrow_decides_which_way_the_swing_went(int cutDirection, int index, bool forehand)
        {
            var note = new ReplayNote { NoteId = 1000 + 100 + 10 + cutDirection };

            Assert.Equal(forehand, SwingAnalyzer.IsForehand(note, index));
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(2, true)]
        public void A_flat_arrow_falls_back_to_the_turn_of_the_cycle(int index, bool forehand)
        {
            // 水平（Right）もドット（Any）も上下が無いので、矢印だけでは決まらない。
            var right = new ReplayNote { NoteId = 1000 + 100 + 10 + 3 };
            var dot = new ReplayNote { NoteId = 1000 + 100 + 10 + 8 };

            Assert.Equal(forehand, SwingAnalyzer.IsForehand(right, index));
            Assert.Equal(forehand, SwingAnalyzer.IsForehand(dot, index));
        }

        [Fact]
        public void Every_swing_lands_on_exactly_one_side()
        {
            var hand = SwingAnalyzer.Analyze(BuildDrill(40, jitter: 0.01)).Hand(1);

            Assert.NotNull(hand.Fore);
            Assert.NotNull(hand.Back);

            // 数え落としも二重数えもない。
            Assert.Equal(hand.GoodCount, hand.Fore.GoodCount + hand.Back.GoodCount);
            Assert.Equal(hand.MissCount, hand.Fore.MissCount + hand.Back.MissCount);
            Assert.Equal(hand.SwingCount, hand.Fore.SwingCount + hand.Back.SwingCount);
        }

        [Fact]
        public void The_two_ways_of_a_round_trip_are_measured_apart()
        {
            // バックだけ当たり所を 20 cm にずらす。まとめた数字では埋もれる差。
            var hand = SwingAnalyzer.Analyze(BuildDrill(40, jitter: 0.01, backCenter: 0.20)).Hand(1);

            Assert.Equal(0.05, hand.Fore.CutDistanceMean, 3);
            Assert.Equal(0.20, hand.Back.CutDistanceMean, 3);

            // まとめると両側の真ん中に来てしまい、どちらが悪いのか読めない。
            Assert.InRange(hand.CutDistanceMean, 0.05, 0.20);
        }

        [Fact]
        public void A_miss_is_counted_on_the_side_it_belongs_to()
        {
            // 戻り（矢印 5 ＝ バック）を4本落とす。ミスには Cut が入らないので、
            // 向きは NoteId の矢印から読むことになる。
            var replay = BuildDrill(40, jitter: 0.01);

            int dropped = 0;
            foreach (var note in replay.Notes)
            {
                if (note.NoteId % 10 != 5 || dropped == 4) continue;

                note.EventType = NoteEventType.Miss;
                note.Cut = null;
                dropped++;
            }

            var hand = SwingAnalyzer.Analyze(replay).Hand(1);

            Assert.Equal(4, hand.Back.MissCount);
            Assert.Equal(0, hand.Fore.MissCount);
        }

        [Fact]
        public void A_steady_drill_keeps_a_tighter_bundle_than_a_shaky_one()
        {
            var steady = SwingAnalyzer.Analyze(BuildDrill(40, jitter: 0.002));
            var shaky = SwingAnalyzer.Analyze(BuildDrill(40, jitter: 0.10));

            // 束の幅は「同じ振りを繰り返せているか」。ぶれれば広がる。
            Assert.True(steady.PathScatter < shaky.PathScatter,
                "ぶれの小さい方が束も細いはず");
        }

        [Fact]
        public void Bowing_away_from_the_line_costs_points()
        {
            // 直線に乗った振りと、途中で膨らむ振り。
            // 毎回同じように膨らんでいても、直線から外れていれば点は下がる。
            var straight = SwingAnalyzer.Analyze(BuildDrill(40, jitter: 0.002));
            var bowed = SwingAnalyzer.Analyze(BuildDrill(40, jitter: 0.002, bow: 0.30));

            Assert.True(straight.TrajectorySpread < bowed.TrajectorySpread,
                "膨らんだ方が直線から離れるはず");
            Assert.True(straight.Score > bowed.Score);

            // 膨らみ方が毎回同じなら束は広がらない。そこが再現性とは別の指標。
            Assert.Equal(straight.PathScatter, bowed.PathScatter, 2);
        }

        [Fact]
        public void Counts_come_from_the_note_events()
        {
            var replay = BuildDrill(20, jitter: 0.01);
            replay.Notes.Add(new ReplayNote { EventTime = 99f, EventType = NoteEventType.Miss });

            var score = SwingAnalyzer.Analyze(replay);

            Assert.Equal(20, score.GoodCount);
            Assert.Equal(1, score.MissCount);
        }

        [Fact]
        public void The_hand_that_actually_swung_is_reported()
        {
            var score = SwingAnalyzer.Analyze(BuildDrill(30, jitter: 0.01));

            Assert.Single(score.PerHand);
            Assert.Equal(1, score.PerHand[0].SaberType);   // 右手だけ
            Assert.True(score.PerHand[0].SwingCount > 0);
        }

        [Fact]
        public void The_timing_is_read_from_each_note_not_from_the_gaps_between_them()
        {
            // 20 ms 遅れて切り続けたドリル。間隔は一定なので、
            // 間隔のばらつきで測っていたら遅れは出てこない。
            var score = SwingAnalyzer.Analyze(BuildDrill(30, jitter: 0.01, lateMs: 20.0));

            Assert.Equal(20.0, score.TimeDeviationMean, 3);
            Assert.Equal(0.0, score.TimeDeviationSpread, 3);
        }

        [Fact]
        public void Being_early_shows_up_as_a_negative_number()
        {
            var score = SwingAnalyzer.Analyze(BuildDrill(30, jitter: 0.01, lateMs: -12.0));
            Assert.Equal(-12.0, score.TimeDeviationMean, 3);
        }

        [Fact]
        public void A_gap_between_sets_does_not_count_as_bad_timing()
        {
            // 右30秒 → 左30秒 のようなセットの切れ目や、ミスで空いた所は
            // 間隔で測ると跳ねる。ノーツごとのずれで測るなら影響しない。
            var replay = BuildDrill(30, jitter: 0.01, lateMs: 5.0);
            foreach (var note in replay.Notes.Skip(15)) note.EventTime += 30f;
            foreach (var frame in replay.Frames.Where(f => f.Time > 5.5f)) frame.Time += 30f;

            var score = SwingAnalyzer.Analyze(replay);

            Assert.Equal(5.0, score.TimeDeviationMean, 3);
            Assert.Equal(0.0, score.TimeDeviationSpread, 3);
        }

        [Fact]
        public void Misses_are_counted_for_the_hand_that_dropped_them()
        {
            var replay = BuildDrill(30, jitter: 0.01);

            // noteID に色が埋まっている（colorType 1 = 右）。ミスには切り方の情報が無いので、
            // 手はここからしか分からない。
            replay.Notes.Add(new ReplayNote { NoteId = 2 * 1000 + 0 * 100 + 1 * 10 + 6, EventTime = 99f, EventType = NoteEventType.Miss });
            replay.Notes.Add(new ReplayNote { NoteId = 1 * 1000 + 1 * 100 + 0 * 10 + 5, EventTime = 99f, EventType = NoteEventType.Miss });

            var score = SwingAnalyzer.Analyze(replay);

            Assert.Equal(2, score.MissCount);
            Assert.Equal(1, score.Hand(1).MissCount);   // 右手だけが振っているドリル
        }

        [Fact]
        public void Too_few_swings_produce_no_score_instead_of_a_bogus_one()
        {
            var score = SwingAnalyzer.Analyze(BuildDrill(2, jitter: 0.01));
            Assert.Empty(score.PerHand);
            Assert.Equal(0.0, score.TrajectorySpread, 9);
        }

        [Fact]
        public void An_empty_replay_does_not_throw()
        {
            var score = SwingAnalyzer.Analyze(new Replay());

            Assert.Empty(score.PerHand);
            Assert.Equal(0, score.GoodCount);
        }
    }
}
