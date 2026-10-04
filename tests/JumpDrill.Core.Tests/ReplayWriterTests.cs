using System.Collections.Generic;
using System.IO;
using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Tests
{
    public class ReplayWriterTests
    {
        private static Vector3 V(float x, float y, float z)
        {
            return new Vector3 { X = x, Y = y, Z = z };
        }

        private static ReplayTransform T(float x, float y, float z)
        {
            return new ReplayTransform
            {
                Position = V(x, y, z),
                Rotation = new Quaternion { X = 0.1f, Y = 0.2f, Z = 0.3f, W = 0.9f },
            };
        }

        private static Replay Sample()
        {
            return new Replay
            {
                Info = new ReplayInfo
                {
                    Version = "JumpDrillMod 0.1.0",
                    GameVersion = "1.40.8",
                    Timestamp = "1788706284",
                    PlayerId = ReplayLibrary.OwnPlayerId,
                    PlayerName = "",
                    Platform = "steam",
                    TrackingSystem = "OpenXR",
                    Hmd = "Index",
                    Controller = "Knuckles",
                    Hash = "1D5484A22F02FB7F7359ED78C6C0148ED68E4C01 WIP",
                    SongName = "[8M0XZ] Drill R_8_b axis 150 BPM 30s",
                    Mapper = "JumpDrill",
                    Difficulty = "ExpertPlus",
                    Score = 12345,
                    Mode = "Standard",
                    Environment = "DefaultEnvironment",
                    Modifiers = "",
                    JumpDistance = 18f,
                    LeftHanded = false,
                    Height = 1.7f,
                    StartTime = 0f,
                    FailTime = 0f,
                    Speed = 1f,
                },
                Frames = new List<ReplayFrame>
                {
                    new ReplayFrame { Time = 0.5f, Fps = 90, Head = T(0, 1.7f, 0), Left = T(-0.3f, 1f, 0.2f), Right = T(0.3f, 1f, 0.2f) },
                    new ReplayFrame { Time = 0.511f, Fps = 90, Head = T(0, 1.71f, 0), Left = T(-0.31f, 1f, 0.2f), Right = T(0.32f, 1f, 0.2f) },
                },
                Notes = new List<ReplayNote>
                {
                    new ReplayNote
                    {
                        NoteId = 31201, EventTime = 2.01f, SpawnTime = 2f, EventType = NoteEventType.Good,
                        Cut = new NoteCutInfo
                        {
                            SpeedOk = true, DirectionOk = true, SaberTypeOk = true, WasCutTooSoon = false,
                            SaberSpeed = 5.5f, SaberDirection = V(0, -1, 0), SaberType = 1,
                            TimeDeviation = -0.01f, CutDirDeviation = 3f,
                            CutPoint = V(0.1f, 1.1f, 0.9f), CutNormal = V(1, 0, 0),
                            CutDistanceToCenter = 0.05f, CutAngle = 120f,
                            BeforeCutRating = 1f, AfterCutRating = 0.8f,
                        },
                    },
                    new ReplayNote { NoteId = 30100, EventTime = 2.6f, SpawnTime = 2.5f, EventType = NoteEventType.Miss },
                },
            };
        }

        [Fact]
        public void Written_replay_reads_back_the_same()
        {
            var original = Sample();

            var stream = new MemoryStream();
            ReplayWriter.Write(original, stream);
            stream.Position = 0;
            var read = ReplayReader.Read(stream);

            Assert.Equal(original.Info.SongName, read.Info.SongName);
            Assert.Equal(original.Info.Hash, read.Info.Hash);
            Assert.Equal(original.Info.Timestamp, read.Info.Timestamp);
            Assert.Equal(original.Info.Score, read.Info.Score);
            Assert.Equal(original.Info.Speed, read.Info.Speed);
            Assert.Equal(original.Info.Controller, read.Info.Controller);
            Assert.False(read.Info.LooksLikeAutoplay);

            Assert.Equal(2, read.Frames.Count);
            Assert.Equal(0.511f, read.Frames[1].Time);
            Assert.Equal(0.32f, read.Frames[1].Right.Position.X);
            Assert.Equal(0.9f, read.Frames[1].Right.Rotation.W);

            Assert.Equal(2, read.Notes.Count);
            Assert.Equal(NoteEventType.Good, read.Notes[0].EventType);
            Assert.Equal(1, read.Notes[0].Cut.SaberType);
            Assert.Equal(0.9f, read.Notes[0].Cut.CutPoint.Z);
            Assert.Equal(0.8f, read.Notes[0].Cut.AfterCutRating);
            Assert.Equal(NoteEventType.Miss, read.Notes[1].EventType);
            Assert.Null(read.Notes[1].Cut);
        }

        [Fact]
        public void Written_replay_is_byte_identical_after_a_second_pass()
        {
            var first = new MemoryStream();
            ReplayWriter.Write(Sample(), first);

            first.Position = 0;
            var second = new MemoryStream();
            ReplayWriter.Write(ReplayReader.Read(first), second);

            Assert.Equal(first.ToArray(), second.ToArray());
        }

        [Fact]
        public void Own_file_name_follows_the_BeatLeader_shape()
        {
            var info = Sample().Info;
            Assert.Equal(
                "JumpDrill-practice-[8M0XZ] Drill R_8_b axis 150 BPM 30s-ExpertPlus-Standard-1D5484A22F02FB7F7359ED78C6C0148ED68E4C01 WIP-1788706284.bsor",
                ReplayLibrary.OwnFileName(info, exited: false));
        }

        [Theory]
        [InlineData(0f, true)]      // 通常プレイ（練習の設定が無い）
        [InlineData(1f, true)]      // 練習モード・等速
        [InlineData(0.9f, false)]
        [InlineData(1.2f, false)]
        public void Changing_the_practice_speed_does_not_count_as_a_record(float speed, bool counts)
        {
            var info = Sample().Info;
            info.Speed = speed;

            Assert.Equal(!counts, info.SpeedChanged);
            Assert.Equal(counts, info.CountsAsRecord);
        }

        [Theory]
        [InlineData("", true)]
        [InlineData("DA,GN", true)]
        [InlineData("FS", false)]
        [InlineData("DA,SS", false)]
        [InlineData("SF,NF", false)]
        public void Song_speed_modifiers_do_not_count_as_a_record(string modifiers, bool counts)
        {
            var info = Sample().Info;
            info.Modifiers = modifiers;

            Assert.Equal(counts, info.CountsAsRecord);
            Assert.Equal(!counts, info.SongSpeedChanged);
        }

        private static string Bl(string name) => Path.Combine("I", "UserData", "BeatLeader", "Replays", name);
        private static string Ll(string name) => Path.Combine("I", "UserData", "LocalLeaderboard", "Replays", name);
        private static string Own(string name) => Path.Combine("I", "UserData", "JumpDrillMod", "Replays", name);

        [Fact]
        public void Own_recording_is_dropped_when_BeatLeader_kept_the_same_play()
        {
            var files = new[]
            {
                Bl("7656-practice-[8M0XZ] Drill R_8_b-ExpertPlus-Standard-ABC WIP-1788706284.bsor"),
                Ll("7656-practice-[8M0XZ] Drill R_8_b-ExpertPlus-Standard-ABC WIP-1788706284_639243006966333586.bsor"),
                Own("JumpDrill-practice-[8M0XZ] Drill R_8_b-ExpertPlus-Standard-ABC WIP-1788706285.bsor"),
                // BeatLeader に無いプレイ（練習の保存を切っていた、上書きで消えた）は残る
                Own("JumpDrill-practice-[8M0XZ] Drill R_8_b-ExpertPlus-Standard-ABC WIP-1788709999.bsor"),
                // 同じ時刻でも別の譜面なら別のプレイ
                Own("JumpDrill-practice-[AAAAA] Drill L_1_a-ExpertPlus-Standard-DEF WIP-1788706284.bsor"),
            };

            var kept = ReplayLibrary.RemoveDuplicates(files);

            Assert.Equal(new[] { files[0], files[3], files[4] }, kept);
        }

        [Fact]
        public void A_quick_restart_is_not_mistaken_for_the_same_play()
        {
            // 1回目は BeatLeader が保存しなかった（途中でやめた）。すぐやり直した2回目だけが BeatLeader にある
            var files = new[]
            {
                Bl("7656-practice-[8M0XZ] Drill R_8_b-ExpertPlus-Standard-ABC WIP-1788706292.bsor"),
                Own("JumpDrill-practice-[8M0XZ] Drill R_8_b-ExpertPlus-Standard-ABC WIP-1788706284.bsor"),
                Own("JumpDrill-practice-[8M0XZ] Drill R_8_b-ExpertPlus-Standard-ABC WIP-1788706291.bsor"),
            };

            Assert.Equal(new[] { files[0], files[1] }, ReplayLibrary.RemoveDuplicates(files));
        }

        [Fact]
        public void One_BeatLeader_file_hides_only_one_own_recording()
        {
            var files = new[]
            {
                Bl("7656-practice-[8M0XZ] Drill R_8_b-ExpertPlus-Standard-ABC WIP-1788706290.bsor"),
                Own("JumpDrill-practice-[8M0XZ] Drill R_8_b-ExpertPlus-Standard-ABC WIP-1788706288.bsor"),
                Own("JumpDrill-practice-[8M0XZ] Drill R_8_b-ExpertPlus-Standard-ABC WIP-1788706291.bsor"),
            };

            // 近い方（1 秒差）だけが同じプレイ
            Assert.Equal(new[] { files[0], files[1] }, ReplayLibrary.RemoveDuplicates(files));
        }

        [Fact]
        public void Own_recording_is_kept_when_nothing_else_has_it()
        {
            var files = new[]
            {
                Own("JumpDrill-practice-[8M0XZ] Drill R_8_b-ExpertPlus-Standard-ABC WIP-1788706285.bsor"),
            };

            Assert.Equal(files, ReplayLibrary.RemoveDuplicates(files));
        }
    }
}
