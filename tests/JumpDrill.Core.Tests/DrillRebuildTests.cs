using System.IO;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;
using Xunit;

namespace JumpDrill.Tests
{
    public class DrillRebuildTests
    {
        private static DrillOptions Options()
        {
            var sequences = SequenceParser.ParseAll("R:8>b");
            sequences.Add(SequenceParser.Mirror(sequences[0], Hand.Left));
            var options = new DrillOptions
            {
                Sequences = sequences,
                IntervalMs = Tempo.IntervalMsFromBpm(175, 2.0),
                NotesPerBeat = 2.0,
                DurationSeconds = 12,
                Sets = 2,
                HandPattern = HandPattern.AlternateRoundTrip,
                Tower = true,
                FlickPercent = 25,
                Direction = DirectionMode.Perpendicular,
                Bpm = 175,
                Njs = 17,
                JumpDistance = 20,
                Click = ClickMode.All,
                CountInClicks = 4,
            };
            options.Name = DrillNaming.Compose(options);
            return options;
        }

        [Fact]
        public void Rewrites_the_same_drill_from_the_folder()
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-rebuild-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var options = Options();
                var before = DrillGenerator.Generate(options);
                string folder = LevelWriter.Write(before, root);

                var read = DrillRebuild.Read(folder);
                Assert.Equal(options.Name, read.Name);
                Assert.Equal(options.IntervalMs, read.IntervalMs, 9);
                Assert.Equal(12.0, read.DurationSeconds);
                Assert.Equal(2, read.Sets);
                Assert.Equal(HandPattern.AlternateRoundTrip, read.HandPattern);
                Assert.True(read.Tower);
                Assert.Equal(25.0, read.FlickPercent);
                Assert.Equal(DirectionMode.Perpendicular, read.Direction);
                Assert.Equal(17.0, read.Njs);
                Assert.Equal(ClickMode.All, read.Click);
                Assert.Equal(4, read.CountInClicks);

                var after = DrillGenerator.Generate(read);
                Assert.Equal(before.Notes.Count, after.Notes.Count);
                for (int i = 0; i < before.Notes.Count; i++)
                    Assert.Equal(before.Notes[i].TimeSeconds, after.Notes[i].TimeSeconds, 4);   // Info.dat のオフセットは丸めて書く
                Assert.Equal(before.Jump.JumpDistance, after.Jump.JumpDistance, 3);

                var same = DrillRebuild.Rewrite(folder);
                Assert.Equal(folder, same.Folder);
                Assert.False(same.Renamed);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Alternate_reads_back_the_note_interval()
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-rebuild-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var options = Options();
                options.HandPattern = HandPattern.Alternate;
                options.Tower = false;
                options.FlickPercent = 0;
                options.Name = DrillNaming.Compose(options);
                string folder = LevelWriter.Write(DrillGenerator.Generate(options), root);

                Assert.Equal(options.IntervalMs, DrillRebuild.Read(folder).IntervalMs, 9);

                // 以前の書き方（BPM が片手の速さ＝半分）の名前でも同じ間隔で読む
                string current = options.Name;
                string old = current.Replace(" 175 BPM ", " 87.5 BPM ");
                Assert.NotEqual(current, old);
                options.Name = old;
                string oldFolder = LevelWriter.Write(DrillGenerator.Generate(options), root, "old");
                var read = DrillRebuild.Read(oldFolder);
                Assert.Equal(options.IntervalMs, read.IntervalMs, 9);
                Assert.Equal(old, read.Name);
                Assert.Equal(current, DrillNaming.CurrentName(old));
                Assert.Equal(DrillNaming.ExtractId(current), DrillNaming.ExtractId(old));

                // 作り直すと今の名前に付け替え、フォルダも今の名前のものに移す
                var result = DrillRebuild.Rewrite(oldFolder);
                Assert.True(result.Renamed);
                Assert.Equal(current, result.NewName);
                Assert.False(Directory.Exists(oldFolder));
                Assert.Equal(current, DrillLibrary.ReadSongName(Path.Combine(result.Folder, "Info.dat")));
                Assert.Equal(LevelWriter.SanitizeFolderName(current), Path.GetFileName(result.Folder));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Theory]
        [InlineData("[ABCDE] Drill R8b L5a axis 300 BPM 30s", 30.0, 1)]
        [InlineData("[ABCDE] Drill R8b L5a axis 300 BPM 10s x3", 10.0, 3)]
        public void Reads_the_length_from_the_name(string name, double seconds, int sets)
        {
            double s; int n;
            Assert.True(DrillRebuild.ParseLength(name, out s, out n));
            Assert.Equal(seconds, s);
            Assert.Equal(sets, n);
        }
    }
}
