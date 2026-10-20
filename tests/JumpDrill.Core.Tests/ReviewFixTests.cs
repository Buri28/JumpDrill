using System;
using System.IO;
using System.Linq;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>レビューで見つかった問題の直し（ステージの移行・保存、取り込み、フレームの無いリプレイ、フォア・バックの絞り込み）。</summary>
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
        [InlineData(0, true, false)]    // Up はバック
        [InlineData(1, true, true)]     // Down はフォア
        [InlineData(2, true, true)]     // Left は切る向きで決まらないので、どちらでも出す
        [InlineData(2, false, true)]
        [InlineData(8, false, true)]    // 向き指定なしも同じ
        public void Notes_without_an_up_or_down_side_are_shown_for_both_forehand_and_backhand(int cutDirection, bool onlyForehand, bool shown)
        {
            Assert.Equal(shown, SwingSides.NoteShown(cutDirection, onlyForehand));
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
    }
}
