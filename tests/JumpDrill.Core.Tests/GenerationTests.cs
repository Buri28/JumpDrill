using System;
using System.Collections.Generic;
using System.Linq;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Parsing;
using Xunit;

namespace JumpDrill.Tests
{
    public class JumpMathTests
    {
        [Fact]
        public void Half_jump_halves_until_the_distance_fits()
        {
            // NJS 16 / BPM 120 なら 16*0.5*4 = 32 m で上限超え。半分の 2 拍に落ちる。
            Assert.Equal(2.0, JumpMath.HalfJumpBase(16, 120), 6);
            // 遅い譜面では 4 拍のまま。
            Assert.Equal(4.0, JumpMath.HalfJumpBase(10, 200), 6);
        }

        [Theory]
        [InlineData(16.0, 120.0, 18.0)]
        [InlineData(20.0, 120.0, 22.5)]
        [InlineData(12.0, 90.0, 14.0)]
        public void Offset_for_jump_distance_round_trips(double njs, double bpm, double jd)
        {
            double offset = JumpMath.OffsetForJumpDistance(njs, bpm, jd);
            var result = JumpMath.Compute(njs, bpm, offset);

            Assert.Equal(jd, result.JumpDistance, 6);
        }

        [Theory]
        [InlineData(16.0, 120.0, 0.55)]
        [InlineData(23.0, 150.0, 0.40)]
        public void Offset_for_reaction_time_round_trips(double njs, double bpm, double rt)
        {
            double offset = JumpMath.OffsetForReactionTime(njs, bpm, rt);
            var result = JumpMath.Compute(njs, bpm, offset);

            Assert.Equal(rt, result.ReactionTimeSeconds, 6);
        }

        [Fact]
        public void Reaction_time_is_half_the_jump_duration()
        {
            var r = JumpMath.Compute(16, 120, 0);
            Assert.Equal(r.JumpDurationSeconds / 2.0, r.ReactionTimeSeconds, 9);
        }

        [Fact]
        public void Njs_and_interval_are_independent()
        {
            // 生成の肝。BPM を動かさずに NJS だけ変えられる（設計メモ §2）。
            var slow = JumpMath.Compute(12, 120, 0);
            var fast = JumpMath.Compute(20, 120, 0);
            Assert.NotEqual(slow.JumpDistance, fast.JumpDistance);
        }
    }

    public class DrillGeneratorTests
    {
        private static DrillOptions Options(string seq, double interval = 300, double sec = 6)
        {
            return new DrillOptions
            {
                Sequences = SequenceParser.ParseAll(seq),
                IntervalMs = interval,
                DurationSeconds = sec,
            };
        }

        [Fact]
        public void Notes_are_spaced_exactly_by_the_requested_interval()
        {
            var map = DrillGenerator.Generate(Options("R:8>b", interval: 110, sec: 5));
            var times = map.Notes.Select(n => n.TimeSeconds).ToList();

            for (int i = 1; i < times.Count; i++)
                Assert.Equal(0.110, times[i] - times[i - 1], 9);
        }

        [Fact]
        public void Duration_determines_the_note_count()
        {
            var map = DrillGenerator.Generate(Options("R:8>b", interval: 100, sec: 10));

            // ノーツ数は速さどおり。10 秒 / 100ms = 100 本。
            // 両端に置いて 101 本にすると、2点の往復で片側だけ1本多い非対称な譜面になる。
            Assert.Equal(100, map.Notes.Count);
        }

        [Fact]
        public void Positions_repeat_the_cycle()
        {
            var map = DrillGenerator.Generate(Options("L:1>9>4>c", interval: 200, sec: 4));
            var tokens = map.Notes.Select(n => n.Position.ToToken()).ToArray();

            // 記法 1>9>4>c は「1 から 9 へ振る」から始まるので、最初のノーツは 9。
            Assert.Equal(new[] { '9', '4', 'c', '1', '9', '4', 'c', '1' }, tokens.Take(8).ToArray());
            for (int i = 4; i < tokens.Length; i++)
                Assert.Equal(tokens[i - 4], tokens[i]);
        }

        [Fact]
        public void Each_note_is_cut_by_the_move_that_arrives_at_it()
        {
            // 実際に叩いて確かめた向き。8 を叩いたあと b へ向かって動くので、
            // b は 8→b（右下がり = DownLeft）で切る。戻りが b→8 なので
            // 8 は UpRight で切る。逆に入れると振りと矢印が噛み合わない。
            var map = DrillGenerator.Generate(Options("R:8>b", interval: 200, sec: 2));

            // 8>b は「8 から b へ振る」なので、その振りで切る b が最初のノーツ。
            Assert.Equal('b', map.Notes[0].Position.ToToken());
            Assert.Equal(CutDirection.DownLeft, map.Notes[0].Direction);

            Assert.Equal('8', map.Notes[1].Position.ToToken());
            Assert.Equal(CutDirection.UpRight, map.Notes[1].Direction);

            foreach (var n in map.Notes)
                Assert.Equal(n.Position.ToToken() == '8' ? CutDirection.UpRight : CutDirection.DownLeft, n.Direction);
        }

        [Fact]
        public void A_longer_cycle_also_uses_the_incoming_move()
        {
            // 1>9>4>c。9 は 1 から降りてきて切るので Down、
            // 4 は 9 から上がってきて切るので UpRight。
            var map = DrillGenerator.Generate(Options("L:1>9>4>c", interval: 200, sec: 1));

            Assert.Equal('9', map.Notes[0].Position.ToToken());
            Assert.Equal(CutDirection.Down, map.Notes[0].Direction);     // 1→9

            Assert.Equal('4', map.Notes[1].Position.ToToken());
            Assert.Equal(CutDirection.UpRight, map.Notes[1].Direction);  // 9→4

            Assert.Equal('c', map.Notes[2].Position.ToToken());
            Assert.Equal(CutDirection.Down, map.Notes[2].Direction);     // 4→c

            Assert.Equal('1', map.Notes[3].Position.ToToken());
            Assert.Equal(CutDirection.UpLeft, map.Notes[3].Direction);   // c→1
        }

        [Fact]
        public void Axis_mode_makes_fore_and_back_share_one_line()
        {
            var map = DrillGenerator.Generate(Options("R:8>b", interval: 200, sec: 2));

            for (int i = 1; i < map.Notes.Count; i++)
                Assert.Equal(map.Notes[i - 1].Direction.Opposite(), map.Notes[i].Direction);
        }

        [Fact]
        public void Dot_mode_removes_every_direction_constraint()
        {
            var options = Options("R:8>b");
            options.Direction = DirectionMode.Dot;

            var map = DrillGenerator.Generate(options);
            Assert.All(map.Notes, n => Assert.Equal(CutDirection.Any, n.Direction));
        }

        [Fact]
        public void Perpendicular_mode_is_orthogonal_to_the_movement_axis()
        {
            var options = Options("R:8>b");
            options.Direction = DirectionMode.Perpendicular;

            var map = DrillGenerator.Generate(options);
            var axis = Geometry.FromTransition(GridPosition.Parse('8'), GridPosition.Parse('b'));
            int ax, ay;
            axis.ToVector(out ax, out ay);

            foreach (var n in map.Notes)
            {
                int dx, dy;
                n.Direction.ToVector(out dx, out dy);
                Assert.Equal(0, ax * dx + ay * dy);
            }
        }

        [Fact]
        public void Explicit_directions_from_the_spec_win()
        {
            var options = Options("R:8@up>b@down");
            options.Direction = DirectionMode.Explicit;

            // 最初のノーツは b（@down）、次が 8（@up）。
            var map = DrillGenerator.Generate(options);
            Assert.Equal(CutDirection.Down, map.Notes[0].Direction);
            Assert.Equal(CutDirection.Up, map.Notes[1].Direction);
        }

        [Fact]
        public void Sync_puts_both_hands_on_every_step()
        {
            var map = DrillGenerator.Generate(Options("R:8>b, L:5>a", interval: 200, sec: 2));

            foreach (var group in map.Notes.GroupBy(n => n.StepIndex))
            {
                Assert.Equal(2, group.Count());
                Assert.Equal(1, group.Count(n => n.Hand == Hand.Right));
                Assert.Equal(1, group.Count(n => n.Hand == Hand.Left));
            }
        }

        [Fact]
        public void Alternate_hands_take_turns_and_advance_their_own_cycle()
        {
            var options = Options("R:8>b, L:5>a", interval: 200, sec: 2);
            options.HandPattern = HandPattern.Alternate;

            var map = DrillGenerator.Generate(options);

            Assert.All(map.Notes.GroupBy(n => n.StepIndex), g => Assert.Single(g));
            Assert.Equal(new[] { Hand.Right, Hand.Left, Hand.Right, Hand.Left },
                         map.Notes.Take(4).Select(n => n.Hand).ToArray());
            // 右手は自分の番でだけ 8→b と進む。
            Assert.Equal(new[] { 'b', '8', 'b' },
                         map.Notes.Where(n => n.Hand == Hand.Right).Take(3).Select(n => n.Position.ToToken()).ToArray());
        }

        [Fact]
        public void Lead_in_clears_the_note_spawn_time()
        {
            // ノーツはジャンプ時間ぶん手前でスポーンするので、
            // 最初のノーツがそれより前にあると壊れる（設計メモ §3）。
            var map = DrillGenerator.Generate(Options("R:8>b"));
            Assert.True(map.LeadInSeconds > map.Jump.JumpDurationSeconds);
            Assert.Equal(map.LeadInSeconds, map.Notes[0].TimeSeconds, 9);
        }

        [Fact]
        public void Lead_in_grows_to_fit_the_count_in()
        {
            var options = Options("R:8>b", interval: 500, sec: 5);
            options.CountInClicks = 16;

            var map = DrillGenerator.Generate(options);
            Assert.True(map.LeadInSeconds >= 16 * map.ClickPeriodSeconds);
        }

        [Fact]
        public void Beats_follow_the_time_base_bpm()
        {
            var options = Options("R:8>b", interval: 250, sec: 4);
            options.Bpm = 120;

            var map = DrillGenerator.Generate(options);
            foreach (var n in map.Notes)
                Assert.Equal(n.TimeSeconds * 2.0, n.Beat, 9);
        }

        [Fact]
        public void Tail_leaves_room_after_the_last_note()
        {
            var options = Options("R:8>b");
            options.TailSeconds = 2.0;

            var map = DrillGenerator.Generate(options);
            Assert.Equal(map.Notes.Last().TimeSeconds + 2.0, map.TotalSeconds, 9);
        }

        [Fact]
        public void Jump_distance_request_is_honoured()
        {
            var options = Options("R:8>b");
            options.Njs = 18;
            options.JumpDistance = 20.0;

            var map = DrillGenerator.Generate(options);
            Assert.Equal(20.0, map.Jump.JumpDistance, 6);
        }

        [Fact]
        public void Specifying_two_jump_parameters_is_rejected()
        {
            var options = Options("R:8>b");
            options.JumpDistance = 20;
            options.ReactionTimeMs = 500;

            Assert.Throws<InvalidOperationException>(() => DrillGenerator.Generate(options));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        public void Non_positive_interval_is_rejected(double interval)
        {
            var options = Options("R:8>b");
            options.IntervalMs = interval;

            Assert.Throws<InvalidOperationException>(() => DrillGenerator.Generate(options));
        }
    }

    public class ClickTests
    {
        private static DrillMap Generate(string seq, ClickMode mode, double interval = 200, double sec = 4)
        {
            return DrillGenerator.Generate(new DrillOptions
            {
                Sequences = SequenceParser.ParseAll(seq),
                IntervalMs = interval,
                DurationSeconds = sec,
                Click = mode,
            });
        }

        [Fact]
        public void Down_mode_clicks_on_every_other_note_of_a_two_point_cycle()
        {
            var map = Generate("R:1>9", ClickMode.Down);
            Assert.Equal(0.400, map.ClickPeriodSeconds, 9);

            var drill = map.Clicks.Where(c => c.TimeSeconds >= map.LeadInSeconds - 1e-9).ToList();
            var downTimes = map.Notes.Where(n => n.Direction.IsDownward()).Select(n => n.TimeSeconds).ToList();
            Assert.Equal(downTimes, drill.Select(c => c.TimeSeconds).ToList());
        }

        [Fact]
        public void Up_mode_anchors_on_the_other_half()
        {
            var down = Generate("R:1>9", ClickMode.Down);
            var up = Generate("R:1>9", ClickMode.Up);

            var downSteps = StepsOf(down);
            var upSteps = StepsOf(up);
            Assert.Empty(downSteps.Intersect(upSteps));
        }

        [Fact]
        public void All_mode_clicks_every_note_and_accents_the_down_cuts()
        {
            var map = Generate("R:1>9", ClickMode.All);
            var drill = map.Clicks.Where(c => c.TimeSeconds >= map.LeadInSeconds - 1e-9).ToList();

            Assert.Equal(map.Notes.Count, drill.Count);
            Assert.Equal(map.Notes.Count(n => n.Direction.IsDownward()), drill.Count(c => c.Accent));
        }

        [Fact]
        public void Every4_mode_clicks_a_quarter_of_the_notes()
        {
            var map = Generate("R:1>9", ClickMode.Every4, interval: 200, sec: 4);
            Assert.Equal(0.800, map.ClickPeriodSeconds, 9);
        }

        [Fact]
        public void None_mode_still_produces_a_count_in()
        {
            var map = Generate("R:1>9", ClickMode.None);
            Assert.All(map.Clicks, c => Assert.True(c.TimeSeconds < map.LeadInSeconds));
        }

        [Fact]
        public void Dot_drills_fall_back_to_alternating_steps()
        {
            // 方向がドットだと上下が定義できないので、循環の偶奇で代用する。
            var options = new DrillOptions
            {
                Sequences = SequenceParser.ParseAll("R:9>c"),
                Direction = DirectionMode.Dot,
                IntervalMs = 200,
                DurationSeconds = 4,
                Click = ClickMode.Down,
            };

            var map = DrillGenerator.Generate(options);
            Assert.Equal(0.400, map.ClickPeriodSeconds, 9);
        }

        [Fact]
        public void Horizontal_drills_still_get_clicks()
        {
            // 9>c は真横なので切り下げが1つも無い。無音になってはいけない。
            var map = Generate("R:9>c", ClickMode.Down);
            Assert.Contains(map.Clicks, c => c.TimeSeconds >= map.LeadInSeconds - 1e-9);
        }

        [Fact]
        public void Count_in_runs_at_the_click_period_and_stays_inside_the_track()
        {
            var map = Generate("R:1>9", ClickMode.Down);
            var countIn = map.Clicks.Where(c => c.TimeSeconds < map.LeadInSeconds - 1e-9).ToList();

            Assert.Equal(map.Options.CountInClicks, countIn.Count);
            Assert.All(countIn, c => Assert.True(c.TimeSeconds >= 0));
            Assert.All(countIn, c => Assert.True(c.Accent));

            for (int i = 1; i < countIn.Count; i++)
                Assert.Equal(map.ClickPeriodSeconds, countIn[i].TimeSeconds - countIn[i - 1].TimeSeconds, 9);

            // 最後のカウントインは1周期前。そのまま予備動作になる。
            Assert.Equal(map.LeadInSeconds - map.ClickPeriodSeconds, countIn.Last().TimeSeconds, 9);
        }

        private static HashSet<int> StepsOf(DrillMap map)
        {
            var byTime = map.Notes.ToLookup(n => Math.Round(n.TimeSeconds, 6));
            var steps = new HashSet<int>();
            foreach (var c in map.Clicks)
                foreach (var n in byTime[Math.Round(c.TimeSeconds, 6)])
                    steps.Add(n.StepIndex);
            return steps;
        }
    }
}
