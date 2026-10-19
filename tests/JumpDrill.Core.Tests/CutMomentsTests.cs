using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>切った瞬間（打点が刃の線に乗る時刻）を、フレームの間から探すこと。</summary>
    public class CutMomentsTests
    {
        private static Replay MovingBlade()
        {
            // 前を向いた刃が、11 ms で X に 0.5 m 動く。記録の時刻は後ろのフレーム
            var replay = new Replay();
            foreach (var (time, x) in new[] { (0.000f, 0.0f), (0.011f, 0.5f), (0.022f, 1.0f) })
                replay.Frames.Add(new ReplayFrame
                {
                    Time = time,
                    Right = new ReplayTransform
                    {
                        Position = new Vector3 { X = x, Y = 1.0f, Z = 0.0f },
                        Rotation = new Quaternion { W = 1f },
                    },
                });
            return replay;
        }

        [Fact]
        public void The_moment_is_found_between_frames_where_the_blade_passes_the_cut_point()
        {
            var replay = MovingBlade();
            var note = new ReplayNote
            {
                EventTime = 0.011f,
                EventType = NoteEventType.Good,
                Cut = new NoteCutInfo { SaberType = 1, CutPoint = new Vector3 { X = 0.25f, Y = 1.0f, Z = 0.9f } },
            };

            Assert.Equal(0.0055, CutMoments.Find(replay, note), 3);
        }

        [Fact]
        public void Without_a_cut_point_the_recorded_time_is_kept()
        {
            var replay = MovingBlade();
            var note = new ReplayNote { EventTime = 0.011f, EventType = NoteEventType.Good, Cut = new NoteCutInfo { SaberType = 1 } };

            Assert.Equal(0.011f, CutMoments.Find(replay, note));
        }

        [Fact]
        public void A_cut_point_far_from_the_blade_keeps_the_recorded_time()
        {
            var replay = MovingBlade();
            var note = new ReplayNote
            {
                EventTime = 0.011f,
                EventType = NoteEventType.Good,
                Cut = new NoteCutInfo { SaberType = 1, CutPoint = new Vector3 { X = 0.25f, Y = 2.0f, Z = 0.9f } },
            };

            Assert.Equal(0.011f, CutMoments.Find(replay, note));
        }
    }
}
