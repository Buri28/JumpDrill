using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>
    /// BeatSaver 形式の zip 書き出し。BeatLeader のリプレイ表示は譜面を
    /// BeatSaver からハッシュで探すので、未公開の自作ドリルでは見つからない。
    /// その画面に読ませるための zip を作る。
    /// </summary>
    public class LevelZipTests : IDisposable
    {
        private readonly string _root;

        public LevelZipTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "JumpDrillZip", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private static DrillMap Sample()
        {
            return DrillGenerator.Generate(new DrillOptions
            {
                Sequences = SequenceParser.ParseAll("R:8>b"),
                IntervalMs = 300,
                DurationSeconds = 3,
            });
        }

        [Fact]
        public void Puts_the_files_at_the_root_of_the_zip()
        {
            string levels = Path.Combine(_root, "levels");
            string folder = LevelWriter.Write(Sample(), levels);

            string zip = LevelZipWriter.Write(folder, Path.Combine(_root, "zip"));

            using (var archive = ZipFile.OpenRead(zip))
            {
                var names = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray();

                // BeatSaver の配布物と同じで、1階層深くしない。
                Assert.All(names, n => Assert.DoesNotContain("/", n));
                Assert.Contains("Info.dat", names);
                Assert.Contains("ExpertPlusStandard.dat", names);
                Assert.Contains("song.ogg", names);
                Assert.Contains("cover.png", names);
            }
        }

        [Fact]
        public void Names_the_zip_after_the_level_folder()
        {
            string folder = LevelWriter.Write(Sample(), Path.Combine(_root, "levels"));
            string zip = LevelZipWriter.Write(folder, Path.Combine(_root, "zip"));

            Assert.Equal(Path.GetFileName(folder) + ".zip", Path.GetFileName(zip));
        }

        [Fact]
        public void Contents_survive_the_round_trip()
        {
            string folder = LevelWriter.Write(Sample(), Path.Combine(_root, "levels"));
            string zip = LevelZipWriter.Write(folder, Path.Combine(_root, "zip"));

            using (var archive = ZipFile.OpenRead(zip))
            {
                var entry = archive.GetEntry("Info.dat");
                using (var reader = new StreamReader(entry.Open()))
                    Assert.Equal(File.ReadAllText(Path.Combine(folder, "Info.dat")), reader.ReadToEnd());

                Assert.Equal(
                    new FileInfo(Path.Combine(folder, "song.ogg")).Length,
                    archive.GetEntry("song.ogg").Length);
            }
        }

        [Fact]
        public void Rewrites_instead_of_appending()
        {
            string folder = LevelWriter.Write(Sample(), Path.Combine(_root, "levels"));
            string zipDir = Path.Combine(_root, "zip");

            LevelZipWriter.Write(folder, zipDir);
            string zip = LevelZipWriter.Write(folder, zipDir);

            using (var archive = ZipFile.OpenRead(zip))
                Assert.Equal(4, archive.Entries.Count);   // 二重に入っていない
        }

        [Fact]
        public void The_writer_can_produce_the_zip_in_one_step()
        {
            string zipDir = Path.Combine(_root, "zip");
            var options = new LevelWriteOptions { ZipDirectory = zipDir };

            string folder = LevelWriter.Write(Sample(), Path.Combine(_root, "levels"), null, options);

            Assert.NotNull(LevelWriter.LastZipPath);
            Assert.True(File.Exists(LevelWriter.LastZipPath));
            Assert.Equal(Path.GetFileName(folder) + ".zip", Path.GetFileName(LevelWriter.LastZipPath));
        }

        [Fact]
        public void No_zip_unless_asked_for()
        {
            LevelWriter.Write(Sample(), Path.Combine(_root, "levels"), null, new LevelWriteOptions());
            Assert.Null(LevelWriter.LastZipPath);
        }

        [Fact]
        public void Missing_folder_is_reported()
        {
            Assert.Throws<DirectoryNotFoundException>(
                () => LevelZipWriter.Write(Path.Combine(_root, "nope"), Path.Combine(_root, "zip")));
        }

        [Fact]
        public void Default_directory_stays_out_of_the_game_folders()
        {
            // ゲームのフォルダに zip を置くと、譜面フォルダと紛れる。
            Assert.Contains("JumpDrill", LevelZipWriter.DefaultDirectory());
            Assert.EndsWith("zip", LevelZipWriter.DefaultDirectory());
        }
    }
}
