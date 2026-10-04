using System.Collections.Generic;
using System.Linq;
using JumpDrill.Model;
using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Core.Tests
{
    /// <summary>
    /// 一括生成のドリルのメダルと Lv。再現精度で 🥉70 / 🥈80 / 🥇90、Lv はポイントの合計。
    /// </summary>
    public class DrillSetMedalsTests
    {
        [Theory]
        [InlineData(69.9, Medal.None)]
        [InlineData(70.0, Medal.Bronze)]
        [InlineData(79.9, Medal.Bronze)]
        [InlineData(80.0, Medal.Silver)]
        [InlineData(90.0, Medal.Gold)]
        [InlineData(100.0, Medal.Gold)]
        public void Medals_follow_the_reproducibility_percent(double percent, Medal expected)
        {
            Assert.Equal(expected, DrillSetMedals.MedalOf(percent));
        }

        [Fact]
        public void Nothing_played_is_level_zero_with_every_cell_empty()
        {
            var rows = DrillSetMedals.Evaluate(new Dictionary<string, double>());

            Assert.Equal(14, rows.Count);
            Assert.All(rows, r => Assert.Equal(0, r.Level));
            Assert.All(rows.SelectMany(r => r.Cells), c => Assert.Null(c.BestPercent));
            Assert.Equal(336, DrillSetMedals.MaxLevel);
        }

        [Fact]
        public void The_level_is_the_sum_of_medal_points_in_any_order()
        {
            // 遅い Stage を飛ばして速い Stage だけ取っても数える。
            var entries = DrillSet.Entries("3b");
            var best = new Dictionary<string, double>
            {
                { DrillNaming.ExtractId(entries[1].Options.Name), 95.0 },   // 🥇 3
                { DrillNaming.ExtractId(entries[4].Options.Name), 82.0 },   // 🥈 2
                { DrillNaming.ExtractId(entries[6].Options.Name), 71.0 },   // 🥉 1
                { DrillNaming.ExtractId(entries[7].Options.Name), 50.0 },   // 取れていない
            };

            var row = DrillSetMedals.Evaluate(best).Single(r => r.Direction == "R3bL2a");

            Assert.Equal(6, row.Level);
            Assert.Null(row.Cells[0].BestPercent);
            Assert.Equal(Medal.Gold, row.Cells[1].Medal);
            Assert.Equal(Medal.None, row.Cells[7].Medal);
            Assert.Equal(50.0, row.Cells[7].BestPercent);
        }

        [Fact]
        public void Other_directions_are_untouched()
        {
            var entry = DrillSet.Entries("3b")[0];
            var best = new Dictionary<string, double> { { DrillNaming.ExtractId(entry.Options.Name), 95.0 } };

            var rows = DrillSetMedals.Evaluate(best);

            Assert.Equal(3, rows.Sum(r => r.Level));
        }
    }
}
