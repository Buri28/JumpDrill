using System.Collections.Generic;
using System.IO;
using System.Linq;
using JumpDrill.Model;
using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Core.Tests
{
    /// <summary>カスタムステージ（作ったドリルを並べたメダルの表）と、その設定ファイル。</summary>
    public class CustomStageTests
    {
        private const string Drill300 = "[Q7K3M] Drill R8b L5a axis 300 BPM 30s";
        private const string Drill250 = "{R3bL2a 250} [ABCDE] Drill R3b L2a axis 250 BPM 10s";

        private static string NewFolder()
        {
            return Path.Combine(Path.GetTempPath(), "jumpdrill-stages-" + System.Guid.NewGuid().ToString("N"));
        }

        [Fact]
        public void Each_stage_is_saved_to_its_own_file_named_after_the_stage()
        {
            string folder = NewFolder();
            try
            {
                var book = new CustomStageBook();
                book.Stages.Add(new CustomStage
                {
                    Name = "苦手 横",
                    Rows = { new CustomStageRow { Name = "R8bL5a", Drills = { Drill300, Drill250 } } },
                });
                book.Stages.Add(new CustomStage { Name = "CUSTOM10" });
                book.Stages.Add(new CustomStage { Name = "CUSTOM2" });
                book.Save(folder);

                Assert.Contains(Drill300, File.ReadAllText(Path.Combine(folder, "苦手 横.json")));
                Assert.True(File.Exists(Path.Combine(folder, "CUSTOM2.json")));

                var loaded = CustomStageBook.Load(folder);
                Assert.Equal(new[] { "CUSTOM2", "CUSTOM10", "苦手 横" }, loaded.Stages.Select(s => s.Name));
                Assert.Equal(new[] { Drill300, Drill250 }, loaded.Find("苦手 横").Rows[0].Drills);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void Renaming_and_deleting_remove_the_old_files_but_leave_files_made_elsewhere()
        {
            string folder = NewFolder();
            try
            {
                var book = new CustomStageBook();
                book.Stages.Add(new CustomStage { Name = "A" });
                book.Stages.Add(new CustomStage { Name = "B" });
                book.Save(folder);

                var loaded = CustomStageBook.Load(folder);
                File.WriteAllText(Path.Combine(folder, "C.json"), "{ \"rows\": [] }");   // 読んだあとで MOD が作った

                loaded.Find("A").Name = "A2";
                loaded.Stages.Remove(loaded.Find("B"));
                loaded.Save(folder);

                Assert.Equal(new[] { "A2.json", "C.json" },
                    Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void A_broken_file_is_skipped_kept_and_its_name_stays_taken()
        {
            string folder = NewFolder();
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "Broken.json"), "{ not json");
                File.WriteAllText(Path.Combine(folder, "Good.json"), "{ \"rows\": [ { \"name\": \"R\", \"drills\": [ \"" + Drill300 + "\" ] } ] }");

                var book = CustomStageBook.Load(folder);
                Assert.Equal("Good", Assert.Single(book.Stages).Name);
                Assert.Equal("Broken", Assert.Single(book.Unreadable));
                Assert.True(book.IsTaken("broken"));

                book.Save(folder);
                Assert.Equal("{ not json", File.ReadAllText(Path.Combine(folder, "Broken.json")));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void The_old_single_file_is_split_into_one_file_per_stage()
        {
            string folder = NewFolder();
            string single = folder + "-stages.json";
            try
            {
                File.WriteAllText(single,
                    "{ \"stages\": [ { \"name\": \"苦手 \\\"横\\\"\", \"rows\": [ { \"name\": \"R\", \"drills\": [ \"" + Drill300 + "\" ] } ] }," +
                    " { \"name\": \"CUSTOM1\", \"rows\": [] } ] }");

                CustomStageBook.SplitSingleFile(single, folder);

                Assert.False(File.Exists(single));
                var book = CustomStageBook.Load(folder);
                Assert.Equal(new[] { "CUSTOM1", "苦手 _横_" }, book.Stages.Select(s => s.Name));
                Assert.Equal(Drill300, book.Find("苦手 _横_").Rows[0].Drills[0]);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                if (File.Exists(single)) File.Delete(single);
            }
        }

        [Theory]
        [InlineData("My stage", true)]
        [InlineData("苦手", true)]
        [InlineData("a/b", false)]
        [InlineData("a?", false)]
        [InlineData("CON", false)]
        [InlineData("end.", false)]
        public void Stage_names_must_be_usable_as_file_names(string name, bool ok)
        {
            Assert.Equal(ok, CustomStageBook.CheckFileName(name) == null);
        }

        [Fact]
        public void Adding_splits_new_rows_by_direction_and_keeps_the_limits()
        {
            var stage = new CustomStage();
            var picked = Enumerable.Range(1, 10).Select(i => "[AAA" + i.ToString("00") + "] Drill R8b L5a axis " + (i * 50) + " BPM 11s").ToList();
            picked.Add("[BBBBB] Drill R4c L19 axis 50 BPM 11s");

            int skipped = stage.AddDrills(0, picked);

            Assert.Equal(2, skipped);
            Assert.Equal(new[] { "R8bL5a", "R4cL19" }, stage.Rows.Select(r => r.Name));
            Assert.Equal(8, stage.Rows[0].Drills.Count);
            var more = Enumerable.Range(1, 9).Select(i => "[CCC" + i.ToString("00") + "] Drill R4c L19 axis " + (i * 50 + 25) + " BPM 11s").ToList();
            Assert.Equal(2, stage.AddDrills(1, more));   // 1本入っているので残り7本
            Assert.Equal("R8bL5a", CustomStage.DirectionOf(stage.Rows[0]));

            var fast = CustomStage.PickBySpeed(picked, "R8bL5a", highest: true, limit: 3);
            Assert.Equal(new double?[] { 400, 450, 500 }, fast.Select(DrillNaming.ParseBpm));
        }

        [Fact]
        public void A_new_row_fills_the_row_of_the_same_direction_first()
        {
            var stage = new CustomStage();
            stage.Rows.Add(new CustomStageRow
            {
                Name = "R8bL5a",
                Drills = Enumerable.Range(1, 6).Select(i => "[AAA" + i.ToString("00") + "] Drill R8b L5a axis " + (i * 50) + " BPM 11s").ToList(),
            });
            stage.Rows.Add(new CustomStageRow { Name = "R4cL19", Drills = { "[BBBBB] Drill R4c L19 axis 50 BPM 11s" } });

            var picked = new List<string>
            {
                "[CCC01] Drill R8b L5a axis 400 BPM 11s",
                "[CCC02] Drill R8b L5a axis 450 BPM 11s",
                "[CCC03] Drill R8b L5a axis 500 BPM 11s",
                "[DDDDD] Drill R3b L2a axis 50 BPM 11s",
            };

            Assert.Equal(0, stage.AddDrills(stage.Rows.Count, picked));

            // 空き2つに入り、あふれた1本と別の方向は新しい行へ
            Assert.Equal(new[] { "R8bL5a", "R4cL19", "R8bL5a", "R3bL2a" }, stage.Rows.Select(r => r.Name));
            Assert.Equal(8, stage.Rows[0].Drills.Count);
            Assert.Equal(new[] { "[CCC03] Drill R8b L5a axis 500 BPM 11s" }, stage.Rows[2].Drills);
        }

        [Fact]
        public void Adding_skips_drills_already_in_the_stage_and_keeps_the_speed_order()
        {
            var stage = new CustomStage();
            stage.Rows.Add(new CustomStageRow
            {
                Name = "R8bL5a",
                Drills = { "[AAA01] Drill R8b L5a axis 100 BPM 11s", "[AAA02] Drill R8b L5a axis 300 BPM 11s" },
            });

            var picked = new List<string>
            {
                "[AAA02] Drill R8b L5a axis 300 BPM 11s",   // 既にある
                "[CCC01] Drill R8b L5a axis 200 BPM 11s",
                "[CCC02] Drill R8b L5a axis 50 BPM 11s",
            };

            Assert.Equal(0, stage.AddDrills(stage.Rows.Count, picked));

            Assert.Single(stage.Rows);
            Assert.Equal(new double?[] { 50, 100, 200, 300 }, stage.Rows[0].Drills.Select(DrillNaming.ParseBpm));
        }

        [Fact]
        public void New_stages_are_numbered()
        {
            var book = new CustomStageBook();
            book.Stages.Add(new CustomStage { Name = "CUSTOM1" });
            Assert.Equal("CUSTOM2", book.NextDefaultName());
        }

        [Fact]
        public void A_missing_file_is_an_empty_book()
        {
            Assert.Empty(CustomStageBook.Load(Path.Combine(Path.GetTempPath(), "no-such-stages-folder")).Stages);
        }

        [Fact]
        public void Speed_and_sequences_are_read_from_the_song_name()
        {
            Assert.Equal(300.0, DrillNaming.ParseBpm(Drill300));
            Assert.Equal(250.0, DrillNaming.ParseBpm(Drill250));
            Assert.Null(DrillNaming.ParseBpm("[Q7K3M] Drill custom"));

            Assert.Equal(new[] { "R8b", "L5a" }, DrillNaming.ParseSequences(Drill300).Select(s => s.ToString()));
            Assert.Empty(DrillNaming.ParseSequences("Some song"));
        }

        [Fact]
        public void Other_lists_each_direction_by_speed_and_leaves_out_the_standard_drills()
        {
            var names = new List<string>();
            // 尺を 11 秒にする（10 秒だと標準のステージと同じ ID になって外れる）
            for (int bpm = 100; bpm <= 1000; bpm += 100)
                names.Add("[" + DrillNaming.MakeId("R8b L5a axis", 30000.0 / bpm, 11, 1) + "] Drill R8b L5a axis " + bpm + " BPM 11s");
            names.Add(names[3]);                                  // 別の場所にある同じ譜面
            names.Add("[ABCDE] Drill R4c L19 axis 50 BPM 11s");
            names.Add(DrillSet.All()[0].Options.Name);            // 標準のステージのもの
            names.Add("Drill R:8>b L:5>a axis 100ms");           // BPM の無い古い名前

            // 8 本を超えた方向は2行目に続く
            var other = CustomStage.Other(names);
            Assert.Equal(new[] { "R4cL19", "R8bL5a", "R8bL5a" }, other.Rows.Select(r => r.Name));
            Assert.Equal(new double?[] { 100, 200, 300, 400, 500, 600, 700, 800 }, other.Rows[1].Drills.Select(DrillNaming.ParseBpm));
            Assert.Equal(new double?[] { 900, 1000 }, other.Rows[2].Drills.Select(DrillNaming.ParseBpm));

            // カスタムステージに入れたものは出さない
            var stage = new CustomStage { Rows = { new CustomStageRow { Drills = { "[ABCDE] Drill R4c L19 axis 50 BPM 11s" } } } };
            Assert.Equal(new[] { "R8bL5a", "R8bL5a" }, CustomStage.Other(names, new[] { stage }).Rows.Select(r => r.Name));

            // All は標準のステージのものも出す
            var all = CustomStage.Other(names, includeDrillSet: true);
            Assert.Contains(all.Rows.SelectMany(r => r.Drills), n => n == DrillSet.All()[0].Options.Name);
            Assert.Equal(other.Rows.Sum(r => r.Drills.Count) + 1, all.Rows.Sum(r => r.Drills.Count));
        }

        [Fact]
        public void Medals_are_looked_up_by_the_id_in_the_name()
        {
            var stage = new CustomStage
            {
                Name = "test",
                Rows =
                {
                    new CustomStageRow { Name = "a", Drills = { Drill300, Drill250 } },
                    new CustomStageRow { Name = "empty" },
                },
            };

            var rows = DrillSetMedals.Evaluate(stage, new Dictionary<string, double> { { "Q7K3M", 85.0 } });

            Assert.Equal(2, rows.Count);
            Assert.Equal(Medal.Silver, rows[0].Cells[0].Medal);
            Assert.Null(rows[0].Cells[1].BestPercent);
            Assert.Equal(2, rows[0].Level);
            Assert.Equal(6, rows[0].MaxLevel);
            Assert.Equal(300.0, rows[0].Cells[0].Bpm);
            Assert.Empty(rows[1].Cells);
        }
    }
}
