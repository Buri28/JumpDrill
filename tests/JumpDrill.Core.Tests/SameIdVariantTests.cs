using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using NVorbis;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>
    /// 標準のステージと同じ形・同じ速さ・同じ尺で、NJS などだけ変えて作ったドリル（同じ ID の別の譜面）の扱い。
    /// ID は記録をまとめる鍵なので変えない。一覧と作り直しは曲名で見分ける。
    /// </summary>
    public class SameIdVariantTests
    {
        /// <summary>Mods タブ・GUI の1本の生成と同じ作り方（名前は DrillNaming.Compose、並び順の印は付かない）。</summary>
        private static DrillOptions Variant(DrillSetEntry entry, double njs, ClickMode click = ClickMode.Down, int countIn = 8)
        {
            var o = entry.Options;
            var options = new DrillOptions
            {
                Sequences = o.Sequences.ToList(),
                IntervalMs = o.IntervalMs,
                NotesPerBeat = o.NotesPerBeat,
                Bpm = o.Bpm,
                DurationSeconds = o.DurationSeconds,
                Sets = o.Sets,
                Direction = o.Direction,
                HandPattern = o.HandPattern,
                Tower = o.Tower,
                FlickPercent = o.FlickPercent,
                Njs = njs,
                Click = click,
                CountInClicks = countIn,
            };
            options.Name = DrillNaming.Compose(options);
            return options;
        }

        private static string TempRoot() => Path.Combine(Path.GetTempPath(), "jd-same-id-" + Guid.NewGuid().ToString("N"));

        private static float MaxAbs(string folder)
        {
            float max = 0;
            using (var reader = new VorbisReader(Path.Combine(folder, "song.ogg")))
            {
                var buffer = new float[8192];
                int count;
                while ((count = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
                    for (int i = 0; i < count; i++) max = Math.Max(max, Math.Abs(buffer[i]));
            }
            return max;
        }

        [Fact]
        public void A_variant_has_the_same_id_and_another_name()
        {
            var entry = DrillSet.Entries("8a")[2];
            var variant = Variant(entry, entry.Options.Njs + 2);

            Assert.Equal(DrillNaming.ExtractId(entry.Options.Name), DrillNaming.ExtractId(variant.Name));
            Assert.NotEqual(entry.Options.Name, variant.Name);
            Assert.True(DrillSet.IsStandardName(entry.Options.Name));
            Assert.False(DrillSet.IsStandardName(variant.Name));
        }

        [Fact]
        public void Other_lists_a_variant_that_shares_the_id_of_a_standard_drill()
        {
            var entry = DrillSet.Entries("8a")[2];
            var variant = Variant(entry, entry.Options.Njs + 2);
            var names = new[] { entry.Options.Name, variant.Name };

            var other = CustomStage.Other(names).Rows.SelectMany(r => r.Drills).ToList();
            Assert.Equal(new[] { variant.Name }, other);

            var all = CustomStage.Other(names, includeDrillSet: true).Rows.SelectMany(r => r.Drills).ToList();
            Assert.Equal(2, all.Count);
            Assert.Contains(entry.Options.Name, all);
            Assert.Contains(variant.Name, all);
        }

        [Fact]
        public void Other_keeps_hiding_old_ebpm_copies_of_standard_drills()
        {
            var entry = DrillSet.Entries("8b")[4];
            string legacy = entry.Options.Name.Substring(entry.Options.Name.IndexOf('}') + 1).TrimStart().Replace(" BPM ", " EBPM ");
            Assert.Equal(DrillNaming.ExtractId(entry.Options.Name), DrillNaming.ExtractId(legacy));

            Assert.Empty(CustomStage.Other(new[] { entry.Options.Name, legacy }).Rows);

            // All では今の名前の1本だけ
            var all = CustomStage.Other(new[] { legacy, entry.Options.Name }, includeDrillSet: true).Rows.SelectMany(r => r.Drills).ToList();
            Assert.Equal(new[] { entry.Options.Name }, all);
        }

        [Fact]
        public void An_old_name_of_a_standard_drill_finds_the_current_entry()
        {
            var entry = DrillSet.Entries("8b")[4];
            string legacy = entry.Options.Name.Substring(entry.Options.Name.IndexOf('}') + 1).TrimStart().Replace(" BPM ", " EBPM ");
            Assert.Equal(entry.Options.Name, DrillSet.FindStandard(legacy).Options.Name);
            Assert.Null(DrillSet.FindStandard(null));
            Assert.False(DrillSet.IsStandardName(null));
        }

        [Fact]
        public void The_same_name_in_two_places_is_one_drill()
        {
            var entry = DrillSet.Entries("8a")[2];
            var variant = Variant(entry, entry.Options.Njs + 2);

            var other = CustomStage.Other(new[] { variant.Name, variant.Name }).Rows.SelectMany(r => r.Drills).ToList();
            Assert.Equal(new[] { variant.Name }, other);
        }

        [Fact]
        public void A_custom_stage_hides_only_the_drill_it_holds()
        {
            var entry = DrillSet.Entries("8a")[2];
            var variant = Variant(entry, entry.Options.Njs + 2);
            var otherVariant = Variant(DrillSet.Entries("8a")[3], 18);

            var holdsStandard = new CustomStage { Name = "S" };
            holdsStandard.Rows.Add(new CustomStageRow { Name = "R8aL5b", Drills = new List<string> { entry.Options.Name } });
            var holdsVariant = new CustomStage { Name = "V" };
            holdsVariant.Rows.Add(new CustomStageRow { Name = "R8aL5b", Drills = new List<string> { otherVariant.Name } });

            var names = new[] { entry.Options.Name, variant.Name, otherVariant.Name };
            var other = CustomStage.Other(names, new[] { holdsStandard, holdsVariant }).Rows.SelectMany(r => r.Drills).ToList();
            Assert.Equal(new[] { variant.Name }, other);
        }

        [Fact]
        public void A_custom_stage_cell_with_a_stale_name_still_hides_the_drill_with_its_id()
        {
            var options = Variant(DrillSet.Entries("8a")[3], 18);
            string stale = options.Name.Replace(" Drill ", " Drill  ");   // 曲名の付け方が変わって、もう読み込まれていない名前
            Assert.Equal(DrillNaming.ExtractId(options.Name), DrillNaming.ExtractId(stale));

            var stage = new CustomStage { Name = "S" };
            stage.Rows.Add(new CustomStageRow { Name = "R8aL5b", Drills = new List<string> { stale } });

            Assert.Empty(CustomStage.Other(new[] { options.Name }, new[] { stage }).Rows);
        }

        [Fact]
        public void Rebuilding_a_variant_keeps_its_own_settings()
        {
            string root = TempRoot();
            try
            {
                var entry = DrillSet.Entries("4b")[1];
                var variant = Variant(entry, entry.Options.Njs + 3, ClickMode.None, 0);
                Assert.Null(DrillRebuild.StandardOptions(variant.Name));
                Assert.NotNull(DrillRebuild.StandardOptions(entry.Options.Name));

                string folder = LevelWriter.Write(DrillGenerator.Generate(variant), root);
                var read = DrillRebuild.Read(folder);
                Assert.Equal(variant.Name, read.Name);
                Assert.Equal(variant.Njs, read.Njs);
                Assert.Equal(ClickMode.None, read.Click);
                Assert.Equal(0, read.CountInClicks);

                var rebuilt = DrillRebuild.Rewrite(folder);
                Assert.False(rebuilt.Renamed);
                Assert.Equal(variant.Name, rebuilt.NewName);
                Assert.Equal(variant.Njs, DrillRebuild.Read(rebuilt.Folder).Njs);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public void A_variant_and_the_standard_drill_are_written_to_different_folders()
        {
            string root = TempRoot();
            try
            {
                var entry = DrillSet.Entries("8a")[2];
                string standard = DrillSetWriter.Write(root, new[] { entry }, new LevelWriteOptions()).Single();
                string variant = LevelWriter.Write(DrillGenerator.Generate(Variant(entry, entry.Options.Njs + 2)), root);

                Assert.NotEqual(standard, variant);
                Assert.Equal(2, Directory.GetDirectories(root).Length);
                Assert.Equal(entry.Options.Njs, DrillRebuild.Read(standard).Njs);
                Assert.Equal(entry.Options.Njs + 2, DrillRebuild.Read(variant).Njs);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        /// <summary>メダル画面の標準ステージで None・カウントイン 0 を選んで上書きしたとき（DrillSetGenerationService と同じ呼び方）。</summary>
        [Fact]
        public void Overwriting_a_standard_drill_with_none_and_no_count_in_makes_it_silent()
        {
            string root = TempRoot();
            try
            {
                var entry = DrillSet.Entries("8a")[2];
                string down = DrillSetWriter.Write(root, new[] { entry.WithClick(ClickMode.Down, 8) }, new LevelWriteOptions()).Single();
                Assert.True(MaxAbs(down) > 0.05f);

                // 上書きしないなら書き直さない
                DrillSetWriter.Write(root, new[] { entry.WithClick(ClickMode.None, 0) }, new LevelWriteOptions(), skipExisting: true);
                Assert.Equal(ClickMode.Down, DrillRebuild.Read(down).Click);
                Assert.Equal(8, DrillRebuild.Read(down).CountInClicks);

                string none = DrillSetWriter.Write(root, new[] { entry.WithClick(ClickMode.None, 0) }, new LevelWriteOptions(), skipExisting: false).Single();
                Assert.Equal(down, none);
                var read = DrillRebuild.Read(none);
                Assert.Equal(ClickMode.None, read.Click);
                Assert.Equal(0, read.CountInClicks);
                Assert.Equal(entry.Options.Name, read.Name);
                Assert.True(MaxAbs(none) < 1e-3f, "max " + MaxAbs(none));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        /// <summary>Mods タブで None・カウントイン 0 にして上書きしたとき（DrillGenerationService と同じ呼び方）。</summary>
        [Fact]
        public void Overwriting_a_variant_changes_its_click_and_count_in()
        {
            string root = TempRoot();
            try
            {
                var entry = DrillSet.Entries("8a")[2];
                var write = new LevelWriteOptions { Overwrite = true };
                string down = LevelWriter.Write(DrillGenerator.Generate(Variant(entry, 18, ClickMode.Down, 8)), root, null, write);
                Assert.True(MaxAbs(down) > 0.05f);

                string none = LevelWriter.Write(DrillGenerator.Generate(Variant(entry, 18, ClickMode.None, 0)), root, null, write);
                Assert.Equal(down, none);
                var read = DrillRebuild.Read(none);
                Assert.Equal(ClickMode.None, read.Click);
                Assert.Equal(0, read.CountInClicks);
                Assert.True(MaxAbs(none) < 1e-3f, "max " + MaxAbs(none));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
