using System.Linq;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Parsing;
using Xunit;

namespace JumpDrill.Core.Tests
{
    /// <summary>タワー（一直線のジャンプで間のマスも埋める）とスタック。</summary>
    public class TowerTests
    {
        private static DrillMap Generate(string spec, bool tower, HandPattern pattern = HandPattern.Sync)
        {
            return DrillGenerator.Generate(new DrillOptions
            {
                Sequences = SequenceParser.ParseAll(spec),
                IntervalMs = 200,
                DurationSeconds = 2,
                HandPattern = pattern,
                Tower = tower,
            });
        }

        private static string CellsAt(DrillMap map, int step)
        {
            return string.Concat(map.Notes.Where(n => n.StepIndex == step)
                .Select(n => n.Position.ToToken()).OrderBy(c => c));
        }

        private static CutDirection[] DirectionsAt(DrillMap map, int step)
        {
            return map.Notes.Where(n => n.StepIndex == step).Select(n => n.Direction).Distinct().ToArray();
        }

        [Fact]
        public void Vertical_three_fills_the_middle_with_the_same_arrow()
        {
            var map = Generate("R3b", true);

            // 最初は b を叩く。3 7 b すべて ▼。
            Assert.Equal("37b", CellsAt(map, 0));
            Assert.Equal(new[] { CutDirection.Down }, DirectionsAt(map, 0));

            // 折り返しは 3 7 b すべて ▲。
            Assert.Equal("37b", CellsAt(map, 1));
            Assert.Equal(new[] { CutDirection.Up }, DirectionsAt(map, 1));
        }

        [Theory]
        [InlineData("R14", "1234")]
        [InlineData("R13", "123")]
        [InlineData("R1b", "16b")]
        [InlineData("R9c", "9abc")]
        [InlineData("R37", "37")]   // 隣り合う2点はスタック
        [InlineData("R12", "12")]
        [InlineData("R16", "16")]
        public void Straight_lines_are_filled_end_to_end(string spec, string cells)
        {
            Assert.Equal(cells, CellsAt(Generate(spec, true), 0));
        }

        [Fact]
        public void Knight_moves_stay_single_notes()
        {
            Assert.Equal("b", CellsAt(Generate("R4b", true), 0));
        }

        [Fact]
        public void Off_by_default()
        {
            Assert.Equal("b", CellsAt(Generate("R3b", false), 0));
        }

        [Fact]
        public void Two_hands_on_separate_cells_are_fine()
        {
            // 右 3>b と左 2>a の同時。マスは重ならないので 6 つ。
            var map = Generate("R3b, L2a", true);
            Assert.Equal("2367ab", CellsAt(map, 0));
        }

        [Theory]
        [InlineData("R2a, L39")]   // 6 で取り合う
        [InlineData("R14, L41")]   // 横一列を両手で取り合う
        public void Overlapping_hands_cannot_be_built_together(string spec)
        {
            // 同じ直線の上で重なるもの（R14, L41）はクラッシュ配置として先に止まる。どちらでも作れない。
            Assert.ThrowsAny<System.InvalidOperationException>(() => Generate(spec, true));
        }

        [Fact]
        public void Overlapping_hands_are_fine_one_hand_at_a_time()
        {
            // 片手ずつは手を替えるまで間が空くので、ぶつからない。
            var map = Generate("R14, L41", true, HandPattern.Split);
            Assert.Equal("1234", CellsAt(map, 0));
        }

        [Theory]
        [InlineData("R14, L41", HandPattern.Alternate)]
        [InlineData("R14, L41", HandPattern.AlternateRoundTrip)]
        [InlineData("R2a, L39", HandPattern.Alternate)]
        public void Overlapping_hands_crash_when_alternating(string spec, HandPattern pattern)
        {
            // 続けて叩く左右のセイバーがぶつかる配置。同時と同じく作らない。
            Assert.ThrowsAny<System.InvalidOperationException>(() => Generate(spec, true, pattern));
        }

        [Theory]
        [InlineData(HandPattern.Alternate)]
        [InlineData(HandPattern.AlternateRoundTrip)]
        public void Separate_hands_can_alternate(HandPattern pattern)
        {
            var map = Generate("R3b, L2a", true, pattern);
            Assert.NotEmpty(map.Notes);
        }

        [Theory]
        [InlineData(HandPattern.Alternate)]
        [InlineData(HandPattern.AlternateRoundTrip)]
        public void Landing_inside_the_other_hands_tower_crashes(HandPattern pattern)
        {
            // 右 3>b のタワーは 7 を通る。左の 7>9 は一直線でないが、7 に降りるのでぶつかる。
            Assert.Throws<TowerOverlapException>(() => Generate("R3b, L79", true, pattern));
        }

        [Theory]
        [InlineData(HandPattern.Alternate)]
        [InlineData(HandPattern.AlternateRoundTrip)]
        public void Jumps_that_never_become_towers_are_not_checked(HandPattern pattern)
        {
            // 4>b も 2>b も一直線でないのでタワーにならない。行き先が同じでも、
            // 交互なら向きが違う線なのでぶつからない。タワーの判定でも止めない。
            var map = Generate("R4b, L2b", true, pattern);
            Assert.NotEmpty(map.Notes);
        }

        [Fact]
        public void The_name_says_tower()
        {
            var plain = new DrillOptions { Sequences = SequenceParser.ParseAll("R3b"), IntervalMs = 200 };
            var tower = new DrillOptions { Sequences = SequenceParser.ParseAll("R3b"), IntervalMs = 200, Tower = true };

            Assert.Contains(" axis tower ", DrillNaming.Compose(tower));
            Assert.NotEqual(DrillNaming.ExtractId(DrillNaming.Compose(plain)), DrillNaming.ExtractId(DrillNaming.Compose(tower)));
            // タワーは別の方向の行に並ぶ。
            Assert.Equal("R3b tower", DrillNaming.DirectionOf(DrillNaming.Compose(tower)));
            Assert.Equal("R3b", DrillNaming.DirectionOf(DrillNaming.Compose(plain)));
            Assert.True(DrillNaming.IsTower(DrillNaming.Compose(tower)));
            Assert.False(DrillNaming.IsTower(DrillNaming.Compose(plain)));
        }
    }
}
