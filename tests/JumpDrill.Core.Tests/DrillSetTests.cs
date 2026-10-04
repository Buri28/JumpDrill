using System.Linq;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Core.Tests
{
    /// <summary>
    /// 方向ごとの速さの Stage。名前の頭の印でまとまり、記録は ID で積み上がる。
    /// </summary>
    public class DrillSetTests
    {
        [Fact]
        public void Fourteen_directions_times_eight_stages()
        {
            var all = DrillSet.All();

            Assert.Equal(112, all.Count);
            Assert.Equal(112, all.Select(r => DrillNaming.ExtractId(r.Options.Name)).Distinct().Count());
        }

        [Fact]
        public void A_direction_is_the_right_hand_and_its_mirror()
        {
            var entries = DrillSet.Entries("3b");

            Assert.Equal("R3bL2a", entries[0].Direction);
            Assert.Equal("{R3bL2a 100} ", entries[0].Options.Name.Substring(0, 13));
            Assert.Equal(HandPattern.Split, entries[0].Options.HandPattern);
            Assert.Equal(Hand.Right, entries[0].Options.Sequences[0].Hand);
        }

        [Fact]
        public void Stages_run_from_100_to_400_then_the_long_350()
        {
            var entries = DrillSet.Entries("8b");

            Assert.Equal(new[] { 100.0, 150, 200, 250, 300, 350, 400, 350 }, entries.Select(r => r.Bpm));
            Assert.Equal(new[] { 14.0, 15, 16, 17, 18, 19, 20, 19 }, entries.Select(r => r.Options.Njs));
            Assert.Equal(new[] { 10.0, 10, 10, 10, 10, 10, 10, 30 }, entries.Select(r => r.Options.DurationSeconds));
            Assert.Equal("R8bL5a 350 30s", entries[7].Tag);
        }

        [Theory]
        [InlineData("49", 60.0, "R49L1c 060")]
        [InlineData("89", 80.0, "R89L5c 080")]
        [InlineData("4a", 100.0, "R4aL1b 100")]
        [InlineData("82", 60.0, "R82L53 060")]
        [InlineData("3c", 100.0, "R3cL29 100")]
        [InlineData("3b", 100.0, "R3bL2a 100")]
        public void Far_and_sideways_directions_start_slower(string direction, double firstBpm, string tag)
        {
            var first = DrillSet.Entries(direction)[0];

            Assert.Equal(firstBpm, first.Bpm, 2);
            Assert.Equal(tag, first.Tag);
        }

        [Fact]
        public void The_bpm_in_the_name_is_the_one_hand_speed()
        {
            // 片手ずつなので BPM 1/2 ＝ 片手の速さ。100 BPM は片手 300ms。
            var entry = DrillSet.Entries("3b")[0];

            Assert.Equal(300.0, entry.Options.IntervalMs, 6);
            Assert.EndsWith("axis 100 BPM 10s", entry.Options.Name);
            Assert.Equal(300.0, SwingAnalyzer.DeclaredHandInterval(entry.Options.Name), 6);
        }

        [Fact]
        public void The_tag_does_not_hide_the_id_or_the_drill()
        {
            string name = DrillSet.Entries("3b")[0].Options.Name;

            Assert.Equal("R3bL2a 100", DrillNaming.ExtractSetTag(name));
            Assert.Equal("Drill R3b L2a axis 100 BPM 10s", DrillNaming.StripId(name));
            Assert.True(DrillLibrary.IsDrill(name));
        }

        [Fact]
        public void An_entry_shares_its_record_with_the_same_drill_made_by_hand()
        {
            // 印は ID に入らない。同じ設定なら GUI で作ったドリルと同じ行に積み上がる。
            var entry = DrillSet.Entries("3b")[0].Options;
            var plain = new DrillOptions
            {
                Sequences = entry.Sequences,
                HandPattern = HandPattern.Split,
                IntervalMs = 300.0,
                DurationSeconds = 10.0,
            };

            Assert.Equal(DrillNaming.ExtractId(DrillNaming.Compose(plain)), DrillNaming.ExtractId(entry.Name));
        }
    }
}
