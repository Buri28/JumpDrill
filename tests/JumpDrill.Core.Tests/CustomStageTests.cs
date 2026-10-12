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

        [Fact]
        public void The_stage_file_round_trips_including_japanese_names()
        {
            string path = Path.Combine(Path.GetTempPath(), "jumpdrill-stages-" + System.Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var book = new CustomStageBook();
                book.Stages.Add(new CustomStage
                {
                    Name = "苦手 \"横\"",
                    Rows = { new CustomStageRow { Name = "R8bL5a", Drills = { Drill300, Drill250 } } },
                });
                book.Save(path);

                Assert.Contains("苦手", File.ReadAllText(path));

                var loaded = CustomStageBook.Load(path);
                var stage = Assert.Single(loaded.Stages);
                Assert.Equal("苦手 \"横\"", stage.Name);
                Assert.Equal(new[] { Drill300, Drill250 }, stage.Rows[0].Drills);
            }
            finally
            {
                File.Delete(path);
            }
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
            Assert.Equal(2, stage.AddDrills(1, picked.Take(9).ToList()));   // 1本入っているので残り7本
            Assert.Equal("R8bL5a", CustomStage.DirectionOf(stage.Rows[0]));

            var fast = CustomStage.PickBySpeed(picked, "R8bL5a", highest: true, limit: 3);
            Assert.Equal(new double?[] { 400, 450, 500 }, fast.Select(DrillNaming.ParseBpm));
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
            Assert.Empty(CustomStageBook.Load(Path.Combine(Path.GetTempPath(), "no-such-stages.json")).Stages);
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
