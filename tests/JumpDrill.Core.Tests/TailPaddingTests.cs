using System.Linq;
using JumpDrill.Generation;
using JumpDrill.Model;
using Xunit;

namespace JumpDrill.Tests
{
    public class TailPaddingTests
    {
        [Theory]
        [InlineData(0.0)]
        [InlineData(2.3)]
        [InlineData(10.0)]
        public void Selected_padding_reaches_each_generated_map(double tail)
        {
            var entries = DrillSet.All(ClickMode.Down, 8, ClickTone.Standard, tail);
            Assert.Equal(112, entries.Count);
            foreach (var entry in entries)
            {
                var map = DrillGenerator.Generate(entry.Options);
                Assert.Equal(tail, map.Options.TailSeconds, 5);
                Assert.Equal(tail, map.TotalSeconds - map.Notes.Last().TimeSeconds, 5);
            }
        }

        [Fact]
        public void Single_entry_keeps_original_and_uses_selected_padding()
        {
            var original = DrillSet.All()[0];
            var changed = original.WithClick(ClickMode.Down, 8, ClickTone.Standard, 2.3);
            Assert.Equal(1.5, original.Options.TailSeconds);
            var map = DrillGenerator.Generate(changed.Options);
            Assert.Equal(2.3, map.TotalSeconds - map.Notes.Last().TimeSeconds, 5);
        }
    }
}
