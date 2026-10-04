using System;
using System.Collections.Generic;
using System.Linq;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Parsing;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>
    /// 片手ずつ（Split）の並び。ジャンプ練習は同じ配置を続けて往復しないと
    /// 軌道が作られないので、右で1本叩いてから左に移る形が要る。
    /// </summary>
    public class SplitHandTests
    {
        private static DrillOptions Options(double interval = 300, double sec = 30)
        {
            var sequences = SequenceParser.ParseAll("R:8>b");
            sequences.Add(SequenceParser.Mirror(sequences[0], Hand.Left));

            return new DrillOptions
            {
                Sequences = sequences,
                IntervalMs = interval,
                DurationSeconds = sec,
                HandPattern = HandPattern.Split,
            };
        }

        [Fact]
        public void Each_hand_gets_one_uninterrupted_block()
        {
            var map = DrillGenerator.Generate(Options());

            // 手が切り替わるのは1回だけ。
            int switches = 0;
            for (int i = 1; i < map.Notes.Count; i++)
                if (map.Notes[i].Hand != map.Notes[i - 1].Hand) switches++;

            Assert.Equal(1, switches);
            Assert.Equal(Hand.Right, map.Notes.First().Hand);
            Assert.Equal(Hand.Left, map.Notes.Last().Hand);
        }

        [Fact]
        public void Duration_is_per_hand_not_shared()
        {
            var map = DrillGenerator.Generate(Options(interval: 300, sec: 30));

            var right = map.Notes.Where(n => n.Hand == Hand.Right).ToList();
            var left = map.Notes.Where(n => n.Hand == Hand.Left).ToList();

            // 30 秒 / 300ms = 100 ノーツが手ごとに出る。
            Assert.Equal(100, right.Count);
            Assert.Equal(100, left.Count);

            // 最後のノーツぶんを足して 30 秒。
            Assert.Equal(29.7, right.Last().TimeSeconds - right.First().TimeSeconds, 6);
            Assert.Equal(29.7, left.Last().TimeSeconds - left.First().TimeSeconds, 6);
        }

        [Fact]
        public void Within_a_block_the_interval_is_the_requested_one()
        {
            // 交互と違い、同じ手の間隔がそのまま指定どおりになる。
            var map = DrillGenerator.Generate(Options(interval: 300, sec: 6));

            foreach (var hand in new[] { Hand.Right, Hand.Left })
            {
                var times = map.Notes.Where(n => n.Hand == hand).Select(n => n.TimeSeconds).ToList();
                for (int i = 1; i < times.Count; i++)
                    Assert.Equal(0.300, times[i] - times[i - 1], 9);
            }
        }

        [Fact]
        public void A_gap_separates_the_two_blocks()
        {
            var map = DrillGenerator.Generate(Options(interval: 300, sec: 6));

            double rightEnd = map.Notes.Where(n => n.Hand == Hand.Right).Max(n => n.TimeSeconds);
            double leftStart = map.Notes.Where(n => n.Hand == Hand.Left).Min(n => n.TimeSeconds);

            Assert.True(map.GapSeconds > 0);
            Assert.Equal(0.300 + map.GapSeconds, leftStart - rightEnd, 6);
        }

        [Fact]
        public void Gap_can_be_set_explicitly()
        {
            var options = Options(interval: 300, sec: 4);
            options.GapSeconds = 5.0;

            var map = DrillGenerator.Generate(options);
            Assert.Equal(5.0, map.GapSeconds, 6);
        }

        [Fact]
        public void Each_block_gets_its_own_count_in()
        {
            var options = Options(interval: 300, sec: 6);
            options.CountInClicks = 4;

            var map = DrillGenerator.Generate(options);
            double leftStart = map.Notes.Where(n => n.Hand == Hand.Left).Min(n => n.TimeSeconds);
            double rightEnd = map.Notes.Where(n => n.Hand == Hand.Right).Max(n => n.TimeSeconds);

            // 手を替える直前にもクリックが並んでいること。
            var inGap = map.Clicks
                .Where(c => c.TimeSeconds > rightEnd && c.TimeSeconds < leftStart)
                .Select(c => c.TimeSeconds)
                .OrderBy(t => t)
                .ToList();

            Assert.Equal(4, inGap.Count);
            for (int i = 1; i < inGap.Count; i++)
                Assert.Equal(map.ClickPeriodSeconds, inGap[i] - inGap[i - 1], 6);
            Assert.Equal(leftStart - map.ClickPeriodSeconds, inGap.Last(), 6);
        }

        [Fact]
        public void Count_in_never_runs_into_the_previous_block()
        {
            var options = Options(interval: 300, sec: 6);
            options.CountInClicks = 32;   // 間より長いカウントインを要求する
            options.GapSeconds = 1.0;

            var map = DrillGenerator.Generate(options);
            double rightEnd = map.Notes.Where(n => n.Hand == Hand.Right).Max(n => n.TimeSeconds);

            var straddling = map.Clicks.Count(c => c.TimeSeconds > rightEnd - 1e-9 && c.TimeSeconds < rightEnd);
            Assert.Equal(0, straddling);
        }

        [Fact]
        public void Clicks_come_out_in_time_order()
        {
            var map = DrillGenerator.Generate(Options(interval: 300, sec: 6));
            var times = map.Clicks.Select(c => c.TimeSeconds).ToList();

            for (int i = 1; i < times.Count; i++)
                Assert.True(times[i] >= times[i - 1], "クリックが時刻順に並んでいない");
        }

        [Fact]
        public void Positions_cycle_within_each_hand()
        {
            var map = DrillGenerator.Generate(Options(interval: 300, sec: 3));

            var right = map.Notes.Where(n => n.Hand == Hand.Right).Select(n => n.Position.ToToken()).Take(4);
            var left = map.Notes.Where(n => n.Hand == Hand.Left).Select(n => n.Position.ToToken()).Take(4);

            // 8>b / 5>a とも、振りの行き先である b / a から始まる。
            Assert.Equal(new[] { 'b', '8', 'b', '8' }, right.ToArray());
            Assert.Equal(new[] { 'a', '5', 'a', '5' }, left.ToArray());
        }

        [Fact]
        public void Three_hands_would_be_three_blocks_but_two_is_the_normal_case()
        {
            var map = DrillGenerator.Generate(Options());
            Assert.Equal(2, map.BlockCount);
        }

        [Fact]
        public void Total_length_covers_both_blocks_and_the_gap()
        {
            // 9 秒 / 300ms はちょうど 30 間隔。割り切れない尺だと
            // ブロックの実長が丸められるので、ここでは割り切れる値を使う。
            var options = Options(interval: 300, sec: 9);
            var map = DrillGenerator.Generate(options);

            double expected = map.LeadInSeconds + 8.7 + map.GapSeconds + 0.300 + 8.7 + options.TailSeconds;
            Assert.Equal(expected, map.TotalSeconds, 6);
        }

        [Fact]
        public void Single_hand_split_behaves_like_a_plain_drill()
        {
            var options = new DrillOptions
            {
                Sequences = SequenceParser.ParseAll("R:8>b"),
                IntervalMs = 300,
                DurationSeconds = 6,
                HandPattern = HandPattern.Split,
            };

            var map = DrillGenerator.Generate(options);
            Assert.Equal(1, map.BlockCount);
            Assert.Equal(0.0, map.GapSeconds, 9);
            Assert.All(map.Notes, n => Assert.Equal(Hand.Right, n.Hand));
        }
    }

    /// <summary>交互との違いを固定しておく。</summary>
    public class AlternateVsSplitTests
    {
        private static DrillOptions Options(HandPattern pattern)
        {
            var sequences = SequenceParser.ParseAll("R:8>b");
            sequences.Add(SequenceParser.Mirror(sequences[0], Hand.Left));

            return new DrillOptions
            {
                Sequences = sequences,
                IntervalMs = 300,
                DurationSeconds = 6,
                HandPattern = pattern,
            };
        }

        [Fact]
        public void Alternate_swaps_hands_on_every_step()
        {
            var map = DrillGenerator.Generate(Options(HandPattern.Alternate));

            for (int i = 1; i < map.Notes.Count; i++)
                Assert.NotEqual(map.Notes[i - 1].Hand, map.Notes[i].Hand);
        }

        [Fact]
        public void Alternate_doubles_the_effective_interval_for_each_hand()
        {
            var map = DrillGenerator.Generate(Options(HandPattern.Alternate));
            var right = map.Notes.Where(n => n.Hand == Hand.Right).Select(n => n.TimeSeconds).ToList();

            // 指定は 300ms でも、その手にとっては 600ms 間隔になる。
            Assert.Equal(0.600, right[1] - right[0], 9);
        }

        [Fact]
        public void Split_keeps_the_requested_interval_for_the_drilling_hand()
        {
            var map = DrillGenerator.Generate(Options(HandPattern.Split));
            var right = map.Notes.Where(n => n.Hand == Hand.Right).Select(n => n.TimeSeconds).ToList();

            Assert.Equal(0.300, right[1] - right[0], 9);
        }
    }
}
