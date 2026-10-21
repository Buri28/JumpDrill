using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>ノーツの向き（本体の NoteJump と同じ計算）、跳び始め、譜面からの NJS の読み取り、当たり判定の箱。</summary>
    public class NoteFlightTests
    {
        private static Replay StillHead(float until)
        {
            var replay = new Replay();
            replay.Info.JumpDistance = 18f;
            for (int i = 0; i * (1f / 90f) <= until; i++)
                replay.Frames.Add(new ReplayFrame
                {
                    Time = i / 90f,
                    Head = new ReplayTransform
                    {
                        Position = new Vector3 { X = 0f, Y = 1.6f, Z = 0f },
                        Rotation = new Quaternion { W = 1f },
                    },
                });
            return replay;
        }

        // noteID: 列×1000 + 段×100 + 色×10 + 向き（古い形式。先頭の scoringType 無し）
        private static ReplayNote Note(float time, int lane, int layer, int color, int direction)
        {
            return new ReplayNote { SpawnTime = time, EventTime = time, NoteId = lane * 1000 + layer * 100 + color * 10 + direction };
        }

        [Fact]
        public void An_outer_lane_note_turns_toward_the_head_by_the_cut()
        {
            var replay = StillHead(3f);
            var note = Note(2f, 3, 0, 1, 1);   // 右端の列・下段・青・下向き。ノーツの時刻 2 秒で切った
            replay.Notes.Add(note);

            Quat q;
            Assert.True(NoteFlight.TryRotationAtCut(replay, NoteFlight.PlanSpawns(replay), note, 0f, 18f, 0f, out q));

            // 面の奥向き（模型の +Z）が、頭からノーツへ向かう向きに揃う
            var forward = q.Rotate(new Vector3 { Z = 1f });
            var toNote = NotePose.Normalize(new Vector3 { X = 0.9f, Y = 0f, Z = 1f });
            Assert.True(forward.X > 0.3f, "外側の列は内へ向く: " + forward.X);
            Assert.True(forward.X * toNote.X + forward.Z * toNote.Z > 0.95f);

            // 矢印（模型の −Y）はおおむね下のまま
            var arrow = q.Rotate(new Vector3 { Y = -1f });
            Assert.True(arrow.Y < -0.9f, "矢印: " + arrow.Y);
        }

        [Fact]
        public void Without_frames_jump_distance_or_speed_no_rotation_is_made()
        {
            var note = Note(2f, 3, 0, 1, 1);
            Quat q;
            Assert.False(NoteFlight.TryRotationAtCut(new Replay(), null, note, 0f, 18f, 0f, out q));
            Assert.False(NoteFlight.TryRotationAtCut(StillHead(3f), null, note, 0f, 0f, 0f, out q));

            var noDistance = StillHead(3f);
            noDistance.Info.JumpDistance = 0f;
            Assert.False(NoteFlight.TryRotationAtCut(noDistance, null, note, 0f, 18f, 0f, out q));

            // 記録が跳び始めより後からしか無い
            var late = StillHead(3f);
            late.Frames.RemoveAll(f => f.Time < 2.5f);
            Assert.False(NoteFlight.TryRotationAtCut(late, null, note, 0f, 18f, 0f, out q));
        }

        [Fact]
        public void A_chain_head_is_not_turned_toward_the_head()
        {
            var note = new ReplayNote { SpawnTime = 2f, EventTime = 2f, NoteId = (4 + 2) * 10000 + 3011 };   // ChainHead
            Assert.Equal(4, note.ScoringType);
            Quat q;
            Assert.False(NoteFlight.TryRotationAtCut(StillHead(3f), null, note, 0f, 18f, 0f, out q));
        }

        [Fact]
        public void A_note_alone_in_its_column_jumps_from_the_bottom_layer()
        {
            var replay = new Replay();
            var top = Note(1f, 1, 2, 0, 0);       // 上段に1つだけ
            var low = Note(2f, 2, 0, 1, 1);       // 同じ列に下段と上段
            var high = Note(2f, 2, 2, 1, 1);
            replay.Notes.AddRange(new[] { top, high, low });

            var spawns = NoteFlight.PlanSpawns(replay);
            Assert.Equal(0, spawns.BeforeJumpLayerOf(top));
            Assert.Equal(0, spawns.BeforeJumpLayerOf(low));
            Assert.Equal(1, spawns.BeforeJumpLayerOf(high));
        }

        [Fact]
        public void Red_right_of_blue_at_the_same_time_starts_from_the_other_lane()
        {
            var replay = new Replay();
            var red = Note(1f, 2, 0, 0, 1);
            var blue = Note(1f, 1, 0, 1, 1);
            var apart = Note(2f, 0, 0, 0, 1);     // 赤が左なら入れ替わらない
            var apartBlue = Note(2f, 3, 0, 1, 1);
            replay.Notes.AddRange(new[] { red, blue, apart, apartBlue });

            var spawns = NoteFlight.PlanSpawns(replay);
            Assert.Equal(1, spawns.StartLaneOf(red));
            Assert.Equal(2, spawns.StartLaneOf(blue));
            Assert.Equal(0, spawns.StartLaneOf(apart));
            Assert.Equal(3, spawns.StartLaneOf(apartBlue));
        }

        [Fact]
        public void The_cut_direction_deviation_matches_the_game_formula()
        {
            // 回っていない下向きのノーツを、真下に切れば 0°、少し右下なら正
            Assert.Equal(0f, NoteFlight.CutDirDeviation(Quat.Identity, new Vector3 { Y = -1f }), 3);
            Assert.True(NoteFlight.CutDirDeviation(Quat.Identity, NotePose.Normalize(new Vector3 { X = 0.2f, Y = -1f })) > 0f);
        }

        [Fact]
        public void The_speed_of_the_played_difficulty_is_read()
        {
            string folder = Path.Combine(Path.GetTempPath(), "jumpdrill-njs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(Path.Combine(folder, "Info.dat"),
                    "{ \"_difficultyBeatmapSets\": [ { \"_beatmapCharacteristicName\": \"Standard\", \"_difficultyBeatmaps\": [" +
                    " { \"_difficulty\": \"Expert\", \"_noteJumpMovementSpeed\": 16 }," +
                    " { \"_difficulty\": \"ExpertPlus\", \"_noteJumpMovementSpeed\": 20 } ] } ] }");

                Assert.Equal(20f, NoteFlight.NoteJumpSpeedOf(folder, "Standard", "ExpertPlus"));
                Assert.Null(NoteFlight.NoteJumpSpeedOf(folder, "Standard", "Hard"));   // 遊んだ難易度が無い＝別の譜面
                Assert.Equal(16f, NoteFlight.NoteJumpSpeedOf(folder));                 // 指定が無ければ最初
                Assert.Null(NoteFlight.NoteJumpSpeedOf(Path.Combine(folder, "none")));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void The_hitbox_is_wider_than_the_note_and_reaches_toward_the_player()
        {
            var center = new Vector3 { Y = 1f, Z = 2f };
            var right = new Vector3 { X = 1f };
            var up = new Vector3 { Y = 1f };
            var forward = new Vector3 { Z = 1f };

            var corners = NoteBox.Hitbox.Corners(center, right, up, forward);
            Assert.Equal(-0.4f, corners.Min(c => c.X), 4);
            Assert.Equal(0.4f, corners.Max(c => c.X), 4);
            Assert.Equal(0.75f, corners.Min(c => c.Y), 4);
            Assert.Equal(1.25f, corners.Max(c => c.Y), 4);
            Assert.Equal(1.25f, corners.Min(c => c.Z), 4);   // 手前に 0.75
            Assert.Equal(2.25f, corners.Max(c => c.Z), 4);   // 奥に 0.25

            var hull = NoteBox.Hull(corners.Select(c => new double[] { c.X, c.Y }));
            Assert.Equal(4, hull.Count);
        }

        [Fact]
        public void A_bomb_id_is_read_with_the_borrowed_digits()
        {
            // 爆弾は colorType = -1、cutDirection = 9。(0 + 2)*10000 + 列 2*1000 + 段 1*100 - 10 + 9
            var bomb = new ReplayNote { NoteId = 20000 + 2000 + 100 - 10 + 9 };
            Assert.True(bomb.IsBombId);
            Assert.Equal(2, bomb.LineIndex);
            Assert.Equal(1, bomb.LineLayer);
            Assert.Equal(-1, bomb.ColorType);
            Assert.Equal(0, bomb.ScoringType);

            var note = new ReplayNote { NoteId = 30000 + 3000 + 200 + 10 + 8 };
            Assert.False(note.IsBombId);
            Assert.Equal(3, note.LineIndex);
            Assert.Equal(2, note.LineLayer);
            Assert.Equal(1, note.ColorType);
            Assert.Equal(8, note.CutDirection);
        }
    }
}
