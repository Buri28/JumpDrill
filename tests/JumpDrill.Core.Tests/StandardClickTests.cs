using System.IO;
using System.Linq;
using JumpDrill.Model;
using JumpDrill.Output;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>標準のステージをクリック音を変えて作っても、同じドリルとして記録が続くか。</summary>
    public class StandardClickTests
    {
        [Theory]
        [InlineData(ClickMode.None)]
        [InlineData(ClickMode.All)]
        [InlineData(ClickMode.Up)]
        [InlineData(ClickMode.Every4)]
        public void The_click_does_not_change_the_name_or_the_id(ClickMode click)
        {
            var standard = DrillSet.All();
            var changed = DrillSet.All(click);

            Assert.Equal(standard.Count, changed.Count);
            for (int i = 0; i < standard.Count; i++)
            {
                Assert.Equal(standard[i].Options.Name, changed[i].Options.Name);
                Assert.Equal(DrillNaming.ExtractId(standard[i].Options.Name), DrillNaming.ExtractId(changed[i].Options.Name));
                Assert.Equal(standard[i].Stage, changed[i].Stage);
                Assert.Equal(standard[i].Options.Njs, changed[i].Options.Njs);
                Assert.Equal(standard[i].Options.IntervalMs, changed[i].Options.IntervalMs);
                Assert.Equal(standard[i].Options.CountInClicks, changed[i].Options.CountInClicks);
                Assert.Equal(click, changed[i].Options.Click);
                Assert.Equal(ClickMode.Down, standard[i].Options.Click);
            }
        }

        [Fact]
        public void WithClick_leaves_the_original_entry_alone()
        {
            var entry = DrillSet.Entries("3b")[0];
            var none = entry.WithClick(ClickMode.None);

            Assert.Equal(ClickMode.Down, entry.Options.Click);
            Assert.Equal(ClickMode.None, none.Options.Click);
            Assert.NotSame(entry.Options, none.Options);
            Assert.Equal(entry.Tag, none.Tag);
            Assert.Equal(entry.Direction, none.Direction);
            Assert.Equal(entry.Bpm, none.Bpm);
        }

        [Theory]
        [InlineData(ClickMode.None)]
        [InlineData(ClickMode.Down)]
        [InlineData(ClickMode.Every4)]
        public void Rebuilding_a_standard_drill_keeps_its_click(ClickMode click)
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-standard-click-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var entry = DrillSet.Entries("8b")[2].WithClick(click);
                string folder = DrillSetWriter.Write(root, new[] { entry }, new LevelWriteOptions()).Single();

                var read = DrillRebuild.Read(folder);
                Assert.Equal(click, read.Click);
                Assert.Equal(entry.Options.Name, read.Name);
                Assert.Equal(entry.Options.Njs, read.Njs);
                Assert.Equal(entry.Options.CountInClicks, read.CountInClicks);

                var rebuilt = DrillRebuild.Rewrite(folder);
                Assert.False(rebuilt.Renamed);
                Assert.Equal(click, DrillRebuild.Read(rebuilt.Folder).Click);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Theory]
        [InlineData("\"click\": \"123\",")]
        [InlineData("")]
        public void A_standard_drill_without_a_valid_click_is_rebuilt_with_down(string replacement)
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-standard-click-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var entry = DrillSet.Entries("3b")[1].WithClick(ClickMode.None);
                string folder = DrillSetWriter.Write(root, new[] { entry }, new LevelWriteOptions()).Single();
                string info = Path.Combine(folder, "Info.dat");
                string text = File.ReadAllText(info);
                Assert.Contains("\"click\": \"None\",", text);
                File.WriteAllText(info, text.Replace("\"click\": \"None\",", replacement));

                Assert.Equal(ClickMode.Down, DrillRebuild.Read(folder).Click);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Overwriting_with_another_click_keeps_the_folder()
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-standard-click-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var entry = DrillSet.Entries("4a")[0];
                string down = DrillSetWriter.Write(root, new[] { entry }, new LevelWriteOptions()).Single();

                // 上書きしなければそのまま
                string skipped = DrillSetWriter.Write(root, new[] { entry.WithClick(ClickMode.None) }, new LevelWriteOptions(), skipExisting: true).Single();
                Assert.Equal(down, skipped);
                Assert.Equal(ClickMode.Down, DrillRebuild.Read(down).Click);

                string none = DrillSetWriter.Write(root, new[] { entry.WithClick(ClickMode.None) }, new LevelWriteOptions()).Single();
                Assert.Equal(down, none);
                Assert.Equal(ClickMode.None, DrillRebuild.Read(none).Click);
                Assert.Single(Directory.GetDirectories(root));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    
        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(16)]
        public void The_count_in_does_not_change_the_name_or_the_id(int countIn)
        {
            var standard = DrillSet.All();
            var changed = DrillSet.All(ClickMode.None, countIn);

            Assert.Equal(standard.Count, changed.Count);
            for (int i = 0; i < standard.Count; i++)
            {
                Assert.Equal(standard[i].Options.Name, changed[i].Options.Name);
                Assert.Equal(countIn, changed[i].Options.CountInClicks);
                Assert.Equal(ClickMode.None, changed[i].Options.Click);
                Assert.Equal(8, standard[i].Options.CountInClicks);
            }
        }

        [Fact]
        public void None_with_no_count_in_writes_no_clicks()
        {
            var entry = DrillSet.Entries("8b")[3].WithClick(ClickMode.None, 0);
            var map = JumpDrill.Generation.DrillGenerator.Generate(entry.Options);
            Assert.Empty(map.Clicks);

            var withCountIn = JumpDrill.Generation.DrillGenerator.Generate(DrillSet.Entries("8b")[3].WithClick(ClickMode.None, 4).Options);
            Assert.Equal(4 * withCountIn.BlockCount, withCountIn.Clicks.Count);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(16)]
        public void Rebuilding_a_standard_drill_keeps_its_count_in(int countIn)
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-standard-countin-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var entry = DrillSet.Entries("4a")[1].WithClick(ClickMode.Up, countIn);
                string folder = DrillSetWriter.Write(root, new[] { entry }, new LevelWriteOptions()).Single();

                var read = DrillRebuild.Read(folder);
                Assert.Equal(countIn, read.CountInClicks);
                Assert.Equal(ClickMode.Up, read.Click);

                var rebuilt = DrillRebuild.Rewrite(folder);
                Assert.False(rebuilt.Renamed);
                Assert.Equal(countIn, DrillRebuild.Read(rebuilt.Folder).CountInClicks);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
