using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using JumpDrill.Output;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>
    /// folders.xml への登録。WIP のレベルはプレイリストから引けない
    /// （levelID が custom_level_&lt;HASH&gt; WIP になり、プレイリストが組み立てる
    /// custom_level_&lt;HASH&gt; と一致しない）ので、まとめる手段はこちらになる。
    /// </summary>
    public class SongCoreFoldersTests : IDisposable
    {
        private readonly string _root;
        private readonly string _xml;

        private const string Template = @"<!--
        Syntax for folder entries (Entries with name Example will be skipped)
-->
<folders>
  <folder>
    <Name>Example</Name>
    <Path>ExamplePath</Path>
    <Pack>0</Pack>
    <WIP>False</WIP>
  </folder>
</folders>
";

        public SongCoreFoldersTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "JumpDrillFolders", Guid.NewGuid().ToString("N"));
            _xml = Path.Combine(_root, "UserData", "SongCore", "folders.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(_xml));
            File.WriteAllText(_xml, Template);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Fact]
        public void Builds_the_path_from_the_install_root()
        {
            Assert.Equal(_xml, SongCoreFolders.FoldersXmlFor(_root));
            Assert.Null(SongCoreFolders.FoldersXmlFor(null));
        }

        [Fact]
        public void Reads_existing_entries()
        {
            var entries = SongCoreFolders.Read(_xml);

            Assert.Single(entries);
            Assert.Equal("Example", entries[0].Name);
            Assert.Equal(0, entries[0].Pack);
            Assert.True(entries[0].IsExample);
        }

        [Fact]
        public void Missing_file_reads_as_empty_instead_of_throwing()
        {
            Assert.Empty(SongCoreFolders.Read(Path.Combine(_root, "nope.xml")));
        }

        [Fact]
        public void Registers_a_wip_pack()
        {
            string folder = Path.Combine(_root, "JumpDrill");

            Assert.True(SongCoreFolders.Register(_xml, "JumpDrill", folder));
            Assert.True(Directory.Exists(folder));

            var added = SongCoreFolders.Read(_xml).Single(e => e.Name == "JumpDrill");
            Assert.Equal(folder, added.Path);
            Assert.Equal(2, added.Pack);      // 2 = 独立パック
            Assert.True(added.Wip);           // WIP のままなのでスコア送信の対象外
            Assert.False(added.IsExample);
        }

        [Fact]
        public void Keeps_the_original_entries_and_the_comment()
        {
            SongCoreFolders.Register(_xml, "JumpDrill", Path.Combine(_root, "JumpDrill"));

            string raw = File.ReadAllText(_xml);
            Assert.Contains("Syntax for folder entries", raw);   // 注釈が消えていない
            Assert.Equal(2, SongCoreFolders.Read(_xml).Count);
        }

        [Fact]
        public void Stays_valid_xml()
        {
            SongCoreFolders.Register(_xml, "JumpDrill", Path.Combine(_root, "JumpDrill"));
            var doc = XDocument.Parse(File.ReadAllText(_xml));
            Assert.Equal(2, doc.Root.Elements("folder").Count());
        }

        [Fact]
        public void Registering_twice_is_a_no_op()
        {
            string folder = Path.Combine(_root, "JumpDrill");

            Assert.True(SongCoreFolders.Register(_xml, "JumpDrill", folder));
            Assert.False(SongCoreFolders.Register(_xml, "JumpDrill", folder));
            Assert.Single(SongCoreFolders.Read(_xml), e => e.Name == "JumpDrill");
        }

        [Fact]
        public void Path_comparison_ignores_case_and_trailing_separator()
        {
            string folder = Path.Combine(_root, "JumpDrill");
            SongCoreFolders.Register(_xml, "JumpDrill", folder);

            Assert.True(SongCoreFolders.IsRegistered(_xml, folder.ToUpperInvariant()));
            Assert.True(SongCoreFolders.IsRegistered(_xml, folder + Path.DirectorySeparatorChar));
            Assert.False(SongCoreFolders.IsRegistered(_xml, Path.Combine(_root, "Other")));
        }

        [Fact]
        public void Takes_a_backup_before_the_first_write()
        {
            SongCoreFolders.Register(_xml, "JumpDrill", Path.Combine(_root, "JumpDrill"));

            string backup = _xml + ".bak-jumpdrill";
            Assert.True(File.Exists(backup));
            Assert.Equal(Template, File.ReadAllText(backup));
        }

        [Fact]
        public void Escapes_characters_that_would_break_the_xml()
        {
            SongCoreFolders.Register(_xml, "A & B <drill>", Path.Combine(_root, "amp"));

            XDocument.Parse(File.ReadAllText(_xml));   // 壊れていなければ例外にならない
            Assert.Contains(SongCoreFolders.Read(_xml), e => e.Name == "A & B <drill>");
        }

        [Fact]
        public void Registration_writes_a_pack_cover_and_points_at_it()
        {
            string folder = Path.Combine(_root, "JumpDrill");
            SongCoreFolders.Register(_xml, "JumpDrill", folder);

            string cover = Path.Combine(folder, "pack-cover.png");
            Assert.True(File.Exists(cover));

            var entry = SongCoreFolders.Read(_xml).Single(e => e.Name == "JumpDrill");
            Assert.Equal(cover, entry.ImagePath);

            // PNG のシグネチャが立っていること。
            var head = new byte[8];
            using (var stream = File.OpenRead(cover)) stream.Read(head, 0, head.Length);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, head);
        }

        [Fact]
        public void Cover_can_be_skipped()
        {
            string folder = Path.Combine(_root, "NoCover");
            SongCoreFolders.Register(_xml, "NoCover", folder, wip: true, withCover: false);

            Assert.False(File.Exists(Path.Combine(folder, "pack-cover.png")));
            Assert.Null(SongCoreFolders.Read(_xml).Single(e => e.Name == "NoCover").ImagePath);
        }

        [Fact]
        public void Rejects_a_missing_folders_file()
        {
            Assert.Throws<FileNotFoundException>(
                () => SongCoreFolders.Register(Path.Combine(_root, "nope.xml"), "X", Path.Combine(_root, "X")));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Rejects_an_empty_name(string name)
        {
            Assert.Throws<ArgumentException>(
                () => SongCoreFolders.Register(_xml, name, Path.Combine(_root, "X")));
        }
    }
}
