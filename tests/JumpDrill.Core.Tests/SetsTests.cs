using System;
using System.Collections.Generic;
using System.Linq;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>
    /// 1セット = 各手1本ずつ。それを何セット繰り返すか。
    /// ドリルとしては「右30秒 → 左30秒」を1組として数えるのが自然なので、
    /// そこを単位にしてある。
    /// </summary>
    public class SetsTests
    {
        private static DrillOptions Options(int sets, Hand first = Hand.Right, double sec = 6)
        {
            var right = SequenceParser.ParseOne("R:8>b");
            var left = SequenceParser.Mirror(right, Hand.Left);

            var sequences = first == Hand.Right
                ? new List<HandSequence> { right, left }
                : new List<HandSequence> { left, right };

            return new DrillOptions
            {
                Sequences = sequences,
                IntervalMs = 300,
                DurationSeconds = sec,
                HandPattern = HandPattern.Split,
                Sets = sets,
            };
        }

        private static List<Hand> BlockOrder(DrillMap map)
        {
            var order = new List<Hand>();
            foreach (var n in map.Notes)
                if (order.Count == 0 || order[order.Count - 1] != n.Hand) order.Add(n.Hand);
            return order;
        }

        [Fact]
        public void One_set_is_one_block_per_hand()
        {
            var map = DrillGenerator.Generate(Options(sets: 1));

            Assert.Equal(1, map.Sets);
            Assert.Equal(2, map.BlocksPerSet);
            Assert.Equal(2, map.BlockCount);
            Assert.Equal(new[] { Hand.Right, Hand.Left }, BlockOrder(map).ToArray());
        }

        [Fact]
        public void Three_sets_repeat_the_pair()
        {
            var map = DrillGenerator.Generate(Options(sets: 3));

            Assert.Equal(6, map.BlockCount);
            Assert.Equal(
                new[] { Hand.Right, Hand.Left, Hand.Right, Hand.Left, Hand.Right, Hand.Left },
                BlockOrder(map).ToArray());
        }

        [Fact]
        public void Order_can_start_with_the_left_hand()
        {
            var map = DrillGenerator.Generate(Options(sets: 2, first: Hand.Left));

            Assert.Equal(
                new[] { Hand.Left, Hand.Right, Hand.Left, Hand.Right },
                BlockOrder(map).ToArray());
        }

        [Fact]
        public void Every_block_repeats_the_same_shape_for_that_hand()
        {
            var map = DrillGenerator.Generate(Options(sets: 3, sec: 3));
            int perBlock = map.Notes.Count / map.BlockCount;

            // 手ごとに配置は左右反転しているので、同じ手のブロックどうしを比べる。
            // 2ブロック先が同じ手になる。
            for (int b = 2; b < map.BlockCount; b++)
            {
                var reference = map.Notes.Skip((b - 2) * perBlock).Take(perBlock).ToList();
                var block = map.Notes.Skip(b * perBlock).Take(perBlock).ToList();

                Assert.Equal(reference[0].Hand, block[0].Hand);
                for (int i = 0; i < perBlock; i++)
                {
                    Assert.Equal(reference[i].Position, block[i].Position);
                    Assert.Equal(reference[i].Direction, block[i].Direction);
                    // ブロック内の相対時刻も一致する。
                    Assert.Equal(reference[i].TimeSeconds - reference[0].TimeSeconds,
                                 block[i].TimeSeconds - block[0].TimeSeconds, 9);
                }
            }
        }

        [Fact]
        public void Each_hand_keeps_its_own_shape_across_sets()
        {
            var map = DrillGenerator.Generate(Options(sets: 3, sec: 3));

            var right = map.Notes.Where(n => n.Hand == Hand.Right).Select(n => n.Position.ToToken()).ToList();
            var left = map.Notes.Where(n => n.Hand == Hand.Left).Select(n => n.Position.ToToken()).ToList();

            Assert.All(right, c => Assert.Contains(c, "8b"));
            Assert.All(left, c => Assert.Contains(c, "5a"));

            // どのブロックも同じ位置から始まる。8>b の振りの行き先なので b。
            int perBlock = right.Count / 3;
            for (int b = 0; b < 3; b++)
                Assert.Equal('b', right[b * perBlock]);
        }

        [Fact]
        public void Note_count_scales_with_the_set_count()
        {
            var one = DrillGenerator.Generate(Options(sets: 1));
            var three = DrillGenerator.Generate(Options(sets: 3));

            Assert.Equal(one.Notes.Count * 3, three.Notes.Count);
        }

        [Fact]
        public void Every_block_boundary_gets_the_same_gap()
        {
            var map = DrillGenerator.Generate(Options(sets: 3, sec: 3));
            int perBlock = map.Notes.Count / map.BlockCount;

            for (int b = 1; b < map.BlockCount; b++)
            {
                double previousEnd = map.Notes[b * perBlock - 1].TimeSeconds;
                double blockStart = map.Notes[b * perBlock].TimeSeconds;
                Assert.Equal(0.300 + map.GapSeconds, blockStart - previousEnd, 6);
            }
        }

        [Fact]
        public void Every_block_gets_its_own_count_in()
        {
            var options = Options(sets: 3, sec: 3);
            options.CountInClicks = 4;

            var map = DrillGenerator.Generate(options);
            int perBlock = map.Notes.Count / map.BlockCount;

            for (int b = 1; b < map.BlockCount; b++)
            {
                double previousEnd = map.Notes[b * perBlock - 1].TimeSeconds;
                double blockStart = map.Notes[b * perBlock].TimeSeconds;

                int inGap = map.Clicks.Count(c => c.TimeSeconds > previousEnd && c.TimeSeconds < blockStart);
                Assert.Equal(4, inGap);
            }
        }

        [Fact]
        public void Sets_also_repeat_a_two_handed_drill()
        {
            // Split 以外でも「1本やって休んでまた1本」として使える。
            var options = Options(sets: 2);
            options.HandPattern = HandPattern.Sync;

            var map = DrillGenerator.Generate(options);
            Assert.Equal(1, map.BlocksPerSet);
            Assert.Equal(2, map.BlockCount);
        }

        [Fact]
        public void A_single_hand_can_be_repeated_on_its_own()
        {
            // 右だけを何本か、という使い方も通る。
            var options = new DrillOptions
            {
                Sequences = SequenceParser.ParseAll("R:8>b"),
                IntervalMs = 300,
                DurationSeconds = 3,
                HandPattern = HandPattern.Split,
                Sets = 3,
            };

            var map = DrillGenerator.Generate(options);
            Assert.Equal(1, map.BlocksPerSet);
            Assert.Equal(3, map.BlockCount);
            Assert.All(map.Notes, n => Assert.Equal(Hand.Right, n.Hand));
            Assert.True(map.GapSeconds > 0);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Set_count_must_be_at_least_one(int sets)
        {
            var options = Options(sets: 1);
            options.Sets = sets;

            Assert.Throws<InvalidOperationException>(() => DrillGenerator.Generate(options));
        }

        [Fact]
        public void Summary_spells_out_the_order_and_the_set_count()
        {
            string text = LevelWriter.Summarize(DrillGenerator.Generate(Options(sets: 3)));
            Assert.Contains("右→左", text);
            Assert.Contains("3 セット", text);
        }

        [Fact]
        public void Summary_says_one_set_when_there_is_no_repeat()
        {
            string text = LevelWriter.Summarize(DrillGenerator.Generate(Options(sets: 1, first: Hand.Left)));
            Assert.Contains("左→右", text);
            Assert.Contains("1セット", text);
        }
    }
}
