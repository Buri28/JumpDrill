using System;
using System.Linq;
using JumpDrill.Model;
using JumpDrill.Parsing;
using Xunit;

namespace JumpDrill.Tests
{
    public class GridTests
    {
        [Theory]
        // 設計メモ §2 の格子。lineIndex = (n-1)%4、lineLayer = 2-(n-1)/4。
        [InlineData('1', 0, 2)]
        [InlineData('4', 3, 2)]
        [InlineData('5', 0, 1)]
        [InlineData('8', 3, 1)]
        [InlineData('9', 0, 0)]
        [InlineData('a', 1, 0)]
        [InlineData('b', 2, 0)]
        [InlineData('c', 3, 0)]
        public void Parse_maps_tokens_to_beat_saber_coordinates(char token, int lineIndex, int lineLayer)
        {
            var p = GridPosition.Parse(token);
            Assert.Equal(lineIndex, p.LineIndex);
            Assert.Equal(lineLayer, p.LineLayer);
        }

        [Fact]
        public void ToToken_round_trips_every_cell()
        {
            foreach (char token in "123456789abc")
                Assert.Equal(token, GridPosition.Parse(token).ToToken());
        }

        [Fact]
        public void Parse_accepts_uppercase_hex_style_tokens()
        {
            Assert.Equal(GridPosition.Parse('b'), GridPosition.Parse('B'));
        }

        [Theory]
        [InlineData('0')]
        [InlineData('d')]
        [InlineData('z')]
        public void Parse_rejects_out_of_grid_tokens(char token)
        {
            Assert.Throws<FormatException>(() => GridPosition.Parse(token));
        }
    }

    public class GeometryTests
    {
        [Fact]
        public void Axis_direction_matches_the_design_note_example()
        {
            // FromTransition は from→to のベクトルをそのまま8方向に丸めるだけ。
            // ノーツの矢印をどちらにするかは DirectionResolver の責任
            // （入ってくる動きで決まる）なので、ここでは混同しないこと。
            var eight = GridPosition.Parse('8');
            var bee = GridPosition.Parse('b');

            Assert.Equal(CutDirection.DownLeft, Geometry.FromTransition(eight, bee));
            Assert.Equal(CutDirection.UpRight, Geometry.FromTransition(bee, eight));
        }

        [Fact]
        public void Two_layer_diagonal_snaps_to_the_nearest_octant()
        {
            // Δ(1,2) は 63.4°。8方向では UpRight に丸まる。
            Assert.Equal(CutDirection.UpRight, Geometry.FromVector(1, 2));
            Assert.Equal(CutDirection.DownLeft, Geometry.FromVector(-1, -2));
        }

        [Fact]
        public void Vertical_and_horizontal_transitions_are_exact()
        {
            Assert.Equal(CutDirection.Down, Geometry.FromTransition(GridPosition.Parse('1'), GridPosition.Parse('9')));
            Assert.Equal(CutDirection.Up, Geometry.FromTransition(GridPosition.Parse('9'), GridPosition.Parse('1')));
            Assert.Equal(CutDirection.Right, Geometry.FromTransition(GridPosition.Parse('9'), GridPosition.Parse('c')));
            Assert.Equal(CutDirection.Left, Geometry.FromTransition(GridPosition.Parse('c'), GridPosition.Parse('9')));
        }

        [Fact]
        public void Perpendicular_is_orthogonal_to_the_placement_vector()
        {
            var from = GridPosition.Parse('1');
            var to = GridPosition.Parse('9');
            var axis = Geometry.FromTransition(from, to);

            var cw = Geometry.PerpendicularTo(from, to, false);
            var ccw = Geometry.PerpendicularTo(from, to, true);

            int ax, ay, cx, cy;
            axis.ToVector(out ax, out ay);
            cw.ToVector(out cx, out cy);
            Assert.Equal(0, ax * cx + ay * cy);

            Assert.Equal(cw, ccw.Opposite());
        }

        [Fact]
        public void Zero_vector_is_a_dot()
        {
            Assert.Equal(CutDirection.Any, Geometry.FromVector(0, 0));
        }
    }

    public class SequenceParserTests
    {
        [Fact]
        public void Parses_two_hands_separated_by_comma()
        {
            var all = SequenceParser.ParseAll("R:8>b, L:a>1");

            Assert.Equal(2, all.Count);
            Assert.Equal(Hand.Right, all[0].Hand);
            Assert.Equal(Hand.Left, all[1].Hand);
            Assert.Equal("8", all[0].Steps[0].Position.ToString());
            Assert.Equal("b", all[0].Steps[1].Position.ToString());
        }

        [Fact]
        public void Parses_a_cycle_longer_than_two_points()
        {
            var seq = SequenceParser.ParseOne("L:1>9>4>c");

            Assert.Equal(4, seq.Length);

            // 昔の : > の形も読めるが、書き戻すのは短い形。
            Assert.Equal("L194c", seq.ToString());
        }

        [Fact]
        public void Wraps_around_at_the_end_of_the_cycle()
        {
            var seq = SequenceParser.ParseOne("R:1>9>4");
            Assert.Equal(seq.StepAt(0).Position, seq.StepAt(3).Position);
        }

        [Fact]
        public void Hand_prefix_defaults_to_right()
        {
            Assert.Equal(Hand.Right, SequenceParser.ParseOne("8>b").Hand);
        }

        [Fact]
        public void Parses_explicit_directions()
        {
            var seq = SequenceParser.ParseOne("R:8@dl>b@ur");

            Assert.Equal(CutDirection.DownLeft, seq.Steps[0].ExplicitDirection);
            Assert.Equal(CutDirection.UpRight, seq.Steps[1].ExplicitDirection);
        }

        [Fact]
        public void Parses_numeric_directions()
        {
            var seq = SequenceParser.ParseOne("R:8@6>b@5");
            Assert.Equal(CutDirection.DownLeft, seq.Steps[0].ExplicitDirection);
        }

        [Theory]
        [InlineData("R:8")]              // 1点しかない
        [InlineData("R:")]               // 空
        [InlineData("X:8>b")]            // 手が不正
        [InlineData("R:8>z")]            // 格子外
        [InlineData("R:8>b, R:1>9")]     // 同じ手が2回
        [InlineData("R:8@nope>b")]       // 方向名が不正
        public void Rejects_malformed_specs(string spec)
        {
            Assert.ThrowsAny<Exception>(() => SequenceParser.ParseAll(spec));
        }

        [Fact]
        public void Mirror_flips_positions_and_directions_horizontally()
        {
            var source = SequenceParser.ParseOne("R:8@dl>b@ur");
            var mirrored = SequenceParser.Mirror(source, Hand.Left);

            Assert.Equal(Hand.Left, mirrored.Hand);
            // 8 (index 3) → index 0 = 5、b (index 2) → index 1 = a。
            Assert.Equal("5", mirrored.Steps[0].Position.ToString());
            Assert.Equal("a", mirrored.Steps[1].Position.ToString());
            Assert.Equal(CutDirection.DownRight, mirrored.Steps[0].ExplicitDirection);
            Assert.Equal(CutDirection.UpLeft, mirrored.Steps[1].ExplicitDirection);
        }

        [Fact]
        public void Mirror_of_a_mirror_is_the_original()
        {
            var source = SequenceParser.ParseOne("R:1>9>4>c");
            var back = SequenceParser.Mirror(SequenceParser.Mirror(source, Hand.Left), Hand.Right);

            Assert.Equal(source.Steps.Select(s => s.Position).ToArray(),
                         back.Steps.Select(s => s.Position).ToArray());
        }
    }
}
