using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JumpDrill.Model;
using JumpDrill.Output;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>
    /// メダル画面の Overwrite で上書きしても音が変わらなかった件（2026-11-23）の回帰テスト。
    /// 同じ譜面を続けて叩くとゲームが前の音をパスで覚えたままになるので、MOD は読み直しのたびに
    /// 書き換わった音のパス（<see cref="SongFileSnapshot"/>）を求めて覚えを外す。
    /// </summary>
    public class OverwriteRememberTests
    {
        [Fact]
        public void Overwritten_audio_is_reported_as_changed()
        {
            string root = Path.Combine(Path.GetTempPath(), "jd-snapshot-" + Guid.NewGuid().ToString("N"));
            try
            {
                var entry = DrillSet.Entries("3b")[3];
                var options = new LevelWriteOptions { Format = AudioFormat.Ogg };
                string folder = DrillSetWriter.Write(root, new[] { entry.WithClick(ClickMode.Down, 8, ClickTone.Kick, 1.5) }, options).Single();
                string song = Path.Combine(folder, "song.ogg");

                // 初めて控えたときは比べる相手が無いので、何も外さない
                var first = SongFileSnapshot.Take(new[] { folder });
                Assert.Empty(SongFileSnapshot.Changed(null, first));
                Assert.True(first.ContainsKey(song));

                // 書き直していなければ外さない
                var same = SongFileSnapshot.Take(new[] { folder });
                Assert.Empty(SongFileSnapshot.Changed(first, same));

                // 音色を変えて同じフォルダへ上書きすると、そのフォルダの音が外す対象になる
                DrillSetWriter.Write(root, new[] { entry.WithClick(ClickMode.Down, 8, ClickTone.Retro, 1.5) }, options);
                var after = SongFileSnapshot.Take(new[] { folder });
                Assert.Equal(new[] { song }, SongFileSnapshot.Changed(first, after));

                // 消してから作り直した（前に無かった）ものも対象
                Assert.Equal(new[] { song }, SongFileSnapshot.Changed(new Dictionary<string, string>(), after));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Only_audio_files_are_watched()
        {
            string folder = Path.Combine(Path.GetTempPath(), "jd-snapshot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(Path.Combine(folder, "Info.dat"), "{}");
                File.WriteAllBytes(Path.Combine(folder, "song.egg"), new byte[] { 1 });
                var before = SongFileSnapshot.Take(new[] { folder, Path.Combine(folder, "missing") });
                Assert.Equal(new[] { Path.Combine(folder, "song.egg") }, before.Keys.ToArray());

                File.WriteAllText(Path.Combine(folder, "Info.dat"), "{ \"changed\": true }");
                Assert.Empty(SongFileSnapshot.Changed(before, SongFileSnapshot.Take(new[] { folder })));

                File.WriteAllBytes(Path.Combine(folder, "song.egg"), new byte[] { 1, 2 });
                Assert.Single(SongFileSnapshot.Changed(before, SongFileSnapshot.Take(new[] { folder })));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        /// <summary>
        /// メダル画面の Overwrite は設定に残し、開き直し・プレイの後・生成の後に OFF へ戻さない。
        /// MOD はゲームの中でしか動かないので、元のコードの形で確かめる（ゲーム内の動きは fpfc で確認済み）。
        /// </summary>
        [Fact]
        public void Medal_screen_overwrite_is_remembered_in_the_config()
        {
            string mod = Path.Combine(RepoRoot(), "src", "JumpDrillMod");
            string view = File.ReadAllText(Path.Combine(mod, "UI", "Medals", "MedalViewController.cs"));
            string config = File.ReadAllText(Path.Combine(mod, "PluginConfig.cs"));

            Assert.Matches(@"public virtual bool StandardOverwrite \{ get; set; \}", config);
            Assert.Matches(@"get => PluginConfig\.Instance\?\.StandardOverwrite", view);
            Assert.Matches(@"PluginConfig\.Instance\.StandardOverwrite = value", view);
            // OFF に戻す代入が無いこと（以前は開くたび・プレイから戻るたびに外していた）
            Assert.DoesNotMatch(@"\bOverwrite\s*=\s*false\s*;", view);

            // MOD の画面のどこにも、上書きを OFF に戻す代入が無いこと
            foreach (var file in Directory.GetFiles(Path.Combine(mod, "UI"), "*.cs", SearchOption.AllDirectories))
                Assert.False(Regex.IsMatch(File.ReadAllText(file), @"Overwrite\s*=\s*false\s*;"), file);
        }

        private static string RepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "JumpDrill.sln"))) return dir.FullName;
            throw new DirectoryNotFoundException("JumpDrill.sln");
        }
    }
}
