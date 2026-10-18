using System;
using System.IO;
using System.Linq;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>カスタムステージのエクスポートと、取り込む側で譜面を作り直すこと。</summary>
    public class CustomStageExportTests
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
                Direction = DirectionMode.Perpendicular,
                Bpm = 175,
                Njs = 19,
                JumpDistance = 22,
                Click = ClickMode.All,
                CountInClicks = 4,
                TailSeconds = 3,
            };
            options.Name = DrillNaming.Compose(options);
            return options;
        }

        private static string TempPath(string suffix)
        {
            return Path.Combine(Path.GetTempPath(), "jd-export-" + Guid.NewGuid().ToString("N") + suffix);
        }

        [Fact]
        public void A_missing_drill_is_made_with_the_same_settings_as_the_exported_one()
        {
            string root = TempPath("");
            string file = TempPath(".json");
            try
            {
                var options = Options();
                var before = DrillGenerator.Generate(options);
                string folder = LevelWriter.Write(before, root);

                var stage = new CustomStage { Name = "苦手", Rows = { new CustomStageRow { Name = "R8b", Drills = { options.Name } } } };
                CustomStageExport.Write(file, stage, name => DrillRebuild.Read(folder));

                var exported = CustomStageExport.Read(file);
                Assert.Equal("苦手", exported.Stage.Name);

                var plan = CustomStageExport.Plan(exported, Enumerable.Empty<string>());
                var made = Assert.Single(plan.ToGenerate);
                Assert.Equal(options.Name, made.Name);
                Assert.Equal(19.0, made.Njs);
                Assert.Equal(ClickMode.All, made.Click);
                Assert.Equal(4, made.CountInClicks);
                Assert.Equal(3.0, made.TailSeconds);

                var after = DrillGenerator.Generate(made);
                Assert.Equal(before.Notes.Count, after.Notes.Count);
                for (int i = 0; i < before.Notes.Count; i++)
                    Assert.Equal(before.Notes[i].TimeSeconds, after.Notes[i].TimeSeconds, 4);
                Assert.Equal(before.Jump.JumpDistance, after.Jump.JumpDistance, 3);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                if (File.Exists(file)) File.Delete(file);
            }
        }

        [Fact]
        public void A_drill_already_here_is_not_made_and_uses_the_local_name()
        {
            var exported = new CustomStageExport.Exported();
            var stage = new CustomStage { Name = "S", Rows = { new CustomStageRow { Name = "R", Drills = { "[Q7K3M] Drill R8b L5a axis 300 BPM 10s" } } } };
            typeof(CustomStageExport.Exported).GetProperty("Stage").SetValue(exported, stage);

            var plan = CustomStageExport.Plan(exported, new[] { "{R8bL5a 300} [Q7K3M] Drill R8b L5a axis 300 BPM 10s" });

            Assert.Empty(plan.ToGenerate);
            Assert.Equal("{R8bL5a 300} [Q7K3M] Drill R8b L5a axis 300 BPM 10s", plan.Stage.Rows[0].Drills[0]);
        }

        [Fact]
        public void A_name_that_cannot_be_read_is_reported_and_kept_in_the_stage()
        {
            string file = TempPath(".json");
            try
            {
                File.WriteAllText(file, "{ \"jumpDrillStage\": 1, \"name\": \"S\", \"rows\": [ { \"name\": \"R\", \"drills\": [ \"[ZZZZZ] Something new\" ] } ], \"futureKey\": 5 }");

                var plan = CustomStageExport.Plan(CustomStageExport.Read(file), Enumerable.Empty<string>());

                Assert.Empty(plan.ToGenerate);
                Assert.Equal("[ZZZZZ] Something new", Assert.Single(plan.CannotGenerate));
                Assert.Equal("[ZZZZZ] Something new", plan.Stage.Rows[0].Drills[0]);
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Fact]
        public void A_file_from_a_newer_format_is_refused()
        {
            string file = TempPath(".json");
            try
            {
                File.WriteAllText(file, "{ \"jumpDrillStage\": 2, \"name\": \"S\", \"rows\": [] }");
                Assert.Throws<FormatException>(() => CustomStageExport.Read(file));

                File.WriteAllText(file, "{ \"rows\": [] }");   // ステージのファイル（エクスポートしたものではない）
                Assert.Throws<FormatException>(() => CustomStageExport.Read(file));
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}
