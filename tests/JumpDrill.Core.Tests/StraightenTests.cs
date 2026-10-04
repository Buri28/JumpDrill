using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Parsing;
using Xunit;

namespace JumpDrill.Core.Tests
{
    /// <summary>
    /// 記法の <c>s</c>（矢印を上下左右に倒す）と、短い形の遷移記法。
    ///
    /// 4↔b はジャンプでよく出る縦振りの往復だが、素直に軸へ合わせると
    /// Δ(-1,-2) が ↙ に丸まって斜めの矢印になる。そこを ▼▲ に直すのが <c>s</c>。
    /// </summary>
    public class StraightenTests
    {
        private static CutDirection DirectionAt(string spec, int cycleIndex, DirectionMode mode = DirectionMode.Axis)
        {
            return DirectionResolver.Resolve(SequenceParser.ParseOne(spec), cycleIndex, mode);
        }

        [Fact]
        public void Four_to_b_is_diagonal_without_the_marker()
        {
            // 4 は右上、b は下段。Δ(-1,-2) なので8方向では ↙ になる。
            Assert.Equal(CutDirection.DownLeft, DirectionAt("R:4>b", 1));
            Assert.Equal(CutDirection.UpRight, DirectionAt("R:4>b", 2));
        }

        [Fact]
        public void The_marker_stands_the_arrow_up_along_the_swing()
        {
            // 記法 4>b は「4 から b へ振る」なので、最初に叩くのは b の方。
            Assert.Equal(CutDirection.Down, DirectionAt("R4sbs", 1));   // b へ入る動きは下向き
            Assert.Equal(CutDirection.Up, DirectionAt("R4sbs", 2));     // 戻りは上向き
        }

        [Fact]
        public void The_marker_binds_only_to_the_point_it_follows()
        {
            // b にだけ s。4 は計算どおりの斜めのまま。
            Assert.Equal(CutDirection.Down, DirectionAt("R4bs", 1));
            Assert.Equal(CutDirection.UpRight, DirectionAt("R4bs", 2));

            // 4 にだけ s。
            Assert.Equal(CutDirection.DownLeft, DirectionAt("R4sb", 1));
            Assert.Equal(CutDirection.Up, DirectionAt("R4sb", 2));
        }

        [Theory]
        // 横に大きく動くなら、倒す先は横になる。向きは動きが決める。
        [InlineData("R5s8s", 1, CutDirection.Right)]
        [InlineData("R5s8s", 2, CutDirection.Left)]
        // 縦だけの動きは元から真っ直ぐ。s があっても変わらない。
        [InlineData("R4scs", 1, CutDirection.Down)]
        [InlineData("R4scs", 2, CutDirection.Up)]
        public void The_arrow_follows_the_movement_not_the_letter(string spec, int cycleIndex, CutDirection expected)
        {
            Assert.Equal(expected, DirectionAt(spec, cycleIndex));
        }

        [Fact]
        public void An_exact_diagonal_falls_to_the_vertical()
        {
            // 8 は中段右端、b は下段。Δ(-1,-1) でちょうど 45°。縦を採る。
            Assert.Equal(CutDirection.Down, DirectionAt("R8sbs", 1));
            Assert.Equal(CutDirection.Up, DirectionAt("R8sbs", 2));
        }

        [Fact]
        public void An_explicit_direction_still_wins()
        {
            Assert.Equal(CutDirection.UpLeft, DirectionAt("R:4>b@ul", 1));
        }

        [Fact]
        public void The_perpendicular_mode_gets_straightened_too()
        {
            // 直交させた向きも斜めに出るので、同じように倒せる必要がある。
            var slanted = DirectionAt("R:4>b", 1, DirectionMode.Perpendicular);
            var straight = DirectionAt("R4bs", 1, DirectionMode.Perpendicular);

            Assert.Equal(CutDirection.UpLeft, slanted);
            Assert.Equal(CutDirection.Left, straight);
        }

        [Fact]
        public void The_dot_mode_has_no_arrow_to_straighten()
        {
            Assert.Equal(CutDirection.Any, DirectionAt("R4sbs", 1, DirectionMode.Dot));
        }

        [Theory]
        [InlineData("R4b", "R4b")]
        [InlineData("4b", "R4b")]
        [InlineData("L4b", "L4b")]
        [InlineData("R4bs", "R4bs")]
        [InlineData("R4sbs", "R4sbs")]
        [InlineData("l9c", "L9c")]
        // 昔の形もそのまま読める。書き戻すと短い形になる。
        [InlineData("R:4>bs", "R4bs")]
        [InlineData("L:1>9", "L19")]
        public void The_compact_form_means_the_same_as_the_arrow_form(string compact, string expected)
        {
            Assert.Equal(expected, SequenceParser.ParseOne(compact).ToString());
        }

        [Fact]
        public void The_marker_survives_a_round_trip_through_the_text()
        {
            var seq = SequenceParser.ParseOne("R4sbs");

            // 盤面を触っても打ち込んだ s が落ちないよう、書き戻せる必要がある。
            Assert.Equal("R4sbs", seq.ToString());
            Assert.Equal(seq.ToString(), SequenceParser.ParseOne(seq.ToString()).ToString());
        }

        [Fact]
        public void Mirroring_keeps_the_marker()
        {
            var mirrored = SequenceParser.Mirror(SequenceParser.ParseOne("R4sbs"), Hand.Left);

            // 左右を返しても、矢印の作り方まで変わってはいけない。
            Assert.True(mirrored.Steps[0].Straighten);
            Assert.True(mirrored.Steps[1].Straighten);
        }

        [Theory]
        [InlineData("R4@ulb")]    // 短い形で @ は切り出せない
        [InlineData("R4")]        // 1点だけ
        [InlineData("R4z")]       // グリッドに無い記号
        public void A_spec_that_cannot_be_read_is_rejected(string spec)
        {
            Assert.Throws<System.FormatException>(() => SequenceParser.ParseOne(spec));
        }

        [Fact]
        public void The_name_shows_the_marker()
        {
            var options = new DrillOptions
            {
                Sequences = SequenceParser.ParseAll("R4sbs"),
                IntervalMs = 100,
            };

            Assert.Equal("Drill R4sbs axis 300 BPM 30s", DrillNaming.StripId(DrillNaming.Compose(options)));
        }

        [Fact]
        public void The_marker_makes_it_a_different_drill()
        {
            var plain = new DrillOptions { Sequences = SequenceParser.ParseAll("R4b"), IntervalMs = 100 };
            var straight = new DrillOptions { Sequences = SequenceParser.ParseAll("R4sbs"), IntervalMs = 100 };

            // 矢印が変われば振り方も変わる。同じ行にまとめてはいけない。
            Assert.NotEqual(
                DrillNaming.ExtractId(DrillNaming.Compose(plain)),
                DrillNaming.ExtractId(DrillNaming.Compose(straight)));
        }
    }
}
