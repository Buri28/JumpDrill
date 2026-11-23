using System;
using System.IO;
using System.Linq;
using JumpDrill.Model;
using JumpDrill.Output;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>
    /// GUI の「生成」で、上書きしないときに生成オプションのウィンドウより前に「既にあります」を出すための判定。
    /// ウィンドウで変えられるクリック音・音色・カウントイン・末尾の余白は名前に入らないので、ウィンドウの前の判定と書く前の判定が食い違わない。
    /// </summary>
    public class LevelExistsTests
    {
        [Fact]
        public void Written_drill_exists_whatever_the_sound_settings()
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-exists-" + Guid.NewGuid().ToString("N"));
            try
            {
                var entries = DrillSet.Entries("3b");
                var entry = entries[3];
                Assert.False(LevelWriter.Exists(root, entry.Options.Name));

                var options = new LevelWriteOptions { Format = AudioFormat.Ogg };
                string folder = DrillSetWriter.Write(root, new[] { entry.WithClick(ClickMode.Down, 8, ClickTone.Kick, 1.5) }, options).Single();
                Assert.Equal(Path.Combine(root, LevelWriter.SanitizeFolderName(entry.Options.Name)), folder);

                Assert.True(LevelWriter.Exists(root, entry.Options.Name));
                // ウィンドウで選び直した音の指定でも同じ譜面として見つかる
                Assert.True(LevelWriter.Exists(root, entry.WithClick(ClickMode.None, 0, ClickTone.SynthBass, 0).Options.Name));
                Assert.False(LevelWriter.Exists(root, entries[4].Options.Name));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Folder_without_info_dat_does_not_count()
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-exists-" + Guid.NewGuid().ToString("N"));
            try
            {
                var entry = DrillSet.Entries("3b")[0];
                Directory.CreateDirectory(Path.Combine(root, LevelWriter.SanitizeFolderName(entry.Options.Name)));
                Assert.False(LevelWriter.Exists(root, entry.Options.Name));
                Assert.False(LevelWriter.Exists("", entry.Options.Name));
                Assert.False(LevelWriter.Exists(null, entry.Options.Name));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
