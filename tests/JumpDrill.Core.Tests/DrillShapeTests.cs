using JumpDrill.Model;
using JumpDrill.Parsing;
using Xunit;

namespace JumpDrill.Tests
{
    public class DrillShapeTests
    {
        [Fact]
        public void Reads_back_the_row_direction()
        {
            var shape = DrillShape.Parse("R8bL5a perp tower alt tight25");
            Assert.NotNull(shape);
            Assert.Equal("R8b", shape.Sequences[0].ToString());
            Assert.Equal("L5a", shape.Sequences[1].ToString());
            Assert.Equal(DirectionMode.Perpendicular, shape.Direction);
            Assert.True(shape.Tower);
            Assert.Equal(HandPattern.Alternate, shape.HandPattern);
            Assert.Equal(25.0, shape.FlickPercent);
            Assert.True(shape.IsMirrored);
        }

        [Fact]
        public void Two_hands_without_a_pattern_are_split()
        {
            var shape = DrillShape.Parse("R8bL5a");
            Assert.Equal(HandPattern.Split, shape.HandPattern);
            Assert.Equal(DirectionMode.Axis, shape.Direction);
        }

        [Theory]
        [InlineData("R8bL5a alt-trip")]
        [InlineData("R8bL5a sync")]
        [InlineData("R4sbsL1sas")]
        public void Round_trips_through_the_drill_name(string direction)
        {
            var shape = DrillShape.Parse(direction);
            var options = new DrillOptions
            {
                Sequences = shape.Sequences, Direction = shape.Direction, Tower = shape.Tower,
                HandPattern = shape.HandPattern, FlickPercent = shape.FlickPercent, IntervalMs = 100,
            };
            Assert.Equal(direction, DrillNaming.DirectionOf(DrillNaming.Compose(options)));
        }

        [Theory]
        [InlineData("My jumps")]
        [InlineData("R8bL5a fast")]
        [InlineData("")]
        public void Unknown_names_are_not_read(string direction)
        {
            Assert.Null(DrillShape.Parse(direction));
        }

        [Fact]
        public void Shapes_that_are_not_a_mirror_are_reported()
        {
            Assert.False(DrillShape.Parse("R43L56").IsMirrored);
            Assert.False(DrillShape.Parse("R8b").IsMirrored);
        }
    }
}
