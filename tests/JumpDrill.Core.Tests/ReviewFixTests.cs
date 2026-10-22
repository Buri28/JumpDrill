using System;
using System.IO;
using System.Linq;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>ステージの移行・保存、取り込み、フレームの無いリプレイ、フォア・バックの絞り込みの境目の扱い。</summary>
    public class ReviewFixTests
    {
        private static string NewFolder()
        {
            return Path.Combine(Path.GetTempPath(), "jumpdrill-review-" + Guid.NewGuid().ToString("N"));
        }

        [Fact]
        public void Splitting_the_old_file_keeps_stages_whose_names_collide_as_file_names()
        {
            string folder = NewFolder();
            string single = folder + "-stages.json";
            try
            {
                // 旧版は大文字と小文字を区別していたので「abc」と「ABC」が並びうる。「A/B」と「A_B」は置き換えで同じ名前になる
                File.WriteAllText(single,
                    "{ \"stages\": [ { \"name\": \"abc\", \"rows\": [] }, { \"name\": \"ABC\", \"rows\": [] }," +
                    " { \"name\": \"A/B\", \"rows\": [] }, { \"name\": \"A_B\", \"rows\": [] } ] }");

                CustomStageBook.SplitSingleFile(single, folder);

                Assert.False(File.Exists(single));
                Assert.Equal(4, CustomStageBook.Load(folder).Stages.Count);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                if (File.Exists(single)) File.Delete(single);
            }
        }

        [Fact]
        public void Saving_does_not_overwrite_a_stage_file_made_elsewhere_after_loading()
        {
            string folder = NewFolder();
            try
            {
                var book = new CustomStageBook();
                book.Stages.Add(new CustomStage { Name = "A" });
                book.Save(folder);

                var loaded = CustomStageBook.Load(folder);
                File.WriteAllText(Path.Combine(folder, "B.json"), "{ \"rows\": [ { \"name\": \"mine\", \"drills\": [] } ] }");   // MOD が作った

                loaded.Find("A").Name = "B";
                Assert.Throws<IOException>(() => loaded.Save(folder));
                Assert.Contains("mine", File.ReadAllText(Path.Combine(folder, "B.json")));
                Assert.True(File.Exists(Path.Combine(folder, "A.json")));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void Importing_trims_rows_drills_and_duplicates_to_the_stage_limits()
        {
            var stage = new CustomStage { Name = "S" };
            for (int r = 0; r < CustomStage.MaxRows + 3; r++)
            {
                var row = new CustomStageRow { Name = "R" + r };
                for (int d = 0; d < CustomStage.MaxDrillsPerRow + 2; d++)
                    row.Drills.Add("[R" + r.ToString("00") + "D" + d + "] Drill R8b L5a axis " + (100 + d * 10) + " BPM 10s");
                stage.Rows.Add(row);
            }
            stage.Rows[1].Drills[0] = stage.Rows[0].Drills[0];   // 同じドリルが2つ

            var exported = new CustomStageExport.Exported();
            typeof(CustomStageExport.Exported).GetProperty("Stage").SetValue(exported, stage);
            var plan = CustomStageExport.Plan(exported, Enumerable.Empty<string>());

            Assert.Equal(CustomStage.MaxRows, plan.Stage.Rows.Count);
            Assert.All(plan.Stage.Rows, row => Assert.True(row.Drills.Count <= CustomStage.MaxDrillsPerRow));
            var ids = plan.Stage.Rows.SelectMany(r => r.Drills).Select(d => DrillNaming.ExtractId(d)).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void A_replay_without_frames_does_not_throw_when_reading_the_blade()
        {
            Vector3 hilt, tip;
            CutMoments.BladeAt(new Replay(), 1, 1.0f, out hilt, out tip);
            Assert.Equal(0f, tip.X);
        }

        [Theory]
        [InlineData(0, 1, true, false)]    // Up はバック
        [InlineData(1, 1, true, true)]     // Down はフォア
        [InlineData(2, 1, true, true)]     // 右手の Left（右から左）はフォア
        [InlineData(2, 1, false, false)]
        [InlineData(2, 0, true, false)]    // 左手の Left はバック
        [InlineData(3, 0, true, true)]     // 左手の Right（左から右）はフォア
        [InlineData(8, 1, false, true)]    // 向き指定なしは決まらないので、どちらでも出す
        [InlineData(8, 1, true, true)]
        public void Notes_are_shown_by_the_side_their_arrow_decides(int cutDirection, int hand, bool onlyForehand, bool shown)
        {
            Assert.Equal(shown, SwingSides.NoteShown(cutDirection, hand, onlyForehand));
        }

        [Fact]
        public void The_arrow_angle_follows_the_arrow_direction()
        {
            Assert.Equal(0.0, SwingSides.ArrowAngle(0), 6);                 // Up
            Assert.Equal(Math.PI, SwingSides.ArrowAngle(1), 6);             // Down
            Assert.Equal(-Math.PI / 2, SwingSides.ArrowAngle(2), 6);        // Left
            Assert.Equal(Math.PI * 3 / 4, SwingSides.ArrowAngle(7), 6);     // DownRight
            Assert.True(double.IsNaN(SwingSides.ArrowAngle(8)));
        }

        [Fact]
        public void Saving_keeps_a_stage_changed_or_deleted_elsewhere_and_writes_only_edited_stages()
        {
            string folder = NewFolder();
            try
            {
                var book = new CustomStageBook();
                book.Stages.Add(new CustomStage { Name = "X" });
                book.Stages.Add(new CustomStage { Name = "Y" });
                book.Save(folder);

                var gui = CustomStageBook.Load(folder);

                // 読んだ後で MOD が X に行を足し、Z を作り直すように消した
                File.WriteAllText(Path.Combine(folder, "X.json"), "{ \"rows\": [ { \"name\": \"mod\", \"drills\": [] } ] }");

                // GUI が Y だけ変えて保存しても、X は MOD の中身のまま
                gui.Find("Y").Rows.Add(new CustomStageRow { Name = "gui" });
                gui.Save(folder);
                Assert.Contains("mod", File.ReadAllText(Path.Combine(folder, "X.json")));
                Assert.Contains("gui", File.ReadAllText(Path.Combine(folder, "Y.json")));

                // GUI が X も変えようとしたら、上書きせずに断る
                gui.Find("X").Rows.Add(new CustomStageRow { Name = "gui" });
                Assert.Throws<IOException>(() => gui.Save(folder));
                Assert.DoesNotContain("gui", File.ReadAllText(Path.Combine(folder, "X.json")));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void A_stage_deleted_elsewhere_is_not_written_back()
        {
            string folder = NewFolder();
            try
            {
                var book = new CustomStageBook();
                book.Stages.Add(new CustomStage { Name = "X" });
                book.Stages.Add(new CustomStage { Name = "Y" });
                book.Save(folder);

                var gui = CustomStageBook.Load(folder);
                File.Delete(Path.Combine(folder, "X.json"));   // MOD で消した

                gui.Find("Y").Rows.Add(new CustomStageRow { Name = "gui" });
                gui.Save(folder);
                Assert.False(File.Exists(Path.Combine(folder, "X.json")));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void Importing_refuses_drills_with_extreme_settings()
        {
            string file = Path.Combine(Path.GetTempPath(), "jumpdrill-review-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                const string Drill = "[AAAAA] Drill R8b L5a axis 300 BPM 10s";
                File.WriteAllText(file, "{ \"jumpDrillStage\": 1, \"name\": \"S\", \"rows\": [ { \"name\": \"R\", \"drills\": [ \"" + Drill +
                    "\" ] } ], \"drills\": { \"" + Drill + "\": { \"tail\": 1000000 } } }");

                var plan = CustomStageExport.Plan(CustomStageExport.Read(file), Enumerable.Empty<string>());
                Assert.Empty(plan.ToGenerate);
                Assert.Equal(Drill, Assert.Single(plan.CannotGenerate));
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Fact]
        public void Very_deep_json_is_refused_instead_of_crashing()
        {
            string deep = new string('[', 100000) + new string(']', 100000);
            Assert.Throws<FormatException>(() => JumpDrill.Serialization.JsonReader.Parse(deep));
        }
    }
}
