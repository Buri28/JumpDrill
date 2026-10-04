using System;

namespace JumpDrill.Model
{
    /// <summary>
    /// Beat Saber の BeatmapObjectSpawnMovementData と同じジャンプ計算。
    /// NJS・ジャンプ距離・反応時間を相互換算するために持っている。
    /// </summary>
    public static class JumpMath
    {
        /// <summary>ゲーム側の上限。これを超えないところまで半ジャンプ拍を半分にしていく。</summary>
        public const double MaxHalfJumpDistance = 17.999;

        /// <summary>半ジャンプ拍の初期値。</summary>
        public const double StartHalfJumpDurationInBeats = 4.0;

        public struct JumpResult
        {
            /// <summary>オフセット適用後の半ジャンプ拍。</summary>
            public double HalfJumpBeats;

            /// <summary>スポーンから通過までの秒。</summary>
            public double JumpDurationSeconds;

            /// <summary>ジャンプ距離 (m)。JDFixer が表示するのと同じ量。</summary>
            public double JumpDistance;

            /// <summary>反応時間 (秒)。ジャンプ時間の半分。</summary>
            public double ReactionTimeSeconds;
        }

        /// <summary>オフセットを足す前の半ジャンプ拍。</summary>
        public static double HalfJumpBase(double njs, double bpm)
        {
            double secondsPerBeat = 60.0 / bpm;
            double halfJump = StartHalfJumpDurationInBeats;
            while (njs * secondsPerBeat * halfJump > MaxHalfJumpDistance)
                halfJump /= 2.0;
            return halfJump;
        }

        public static JumpResult Compute(double njs, double bpm, double startBeatOffset)
        {
            double secondsPerBeat = 60.0 / bpm;
            double halfJump = HalfJumpBase(njs, bpm) + startBeatOffset;
            if (halfJump < 0.25) halfJump = 0.25;

            JumpResult r;
            r.HalfJumpBeats = halfJump;
            r.JumpDurationSeconds = secondsPerBeat * halfJump * 2.0;
            r.JumpDistance = njs * r.JumpDurationSeconds;
            r.ReactionTimeSeconds = r.JumpDurationSeconds / 2.0;
            return r;
        }

        /// <summary>目標ジャンプ距離 (m) を出すための _noteJumpStartBeatOffset。</summary>
        public static double OffsetForJumpDistance(double njs, double bpm, double jumpDistance)
        {
            double secondsPerBeat = 60.0 / bpm;
            double wanted = jumpDistance / (2.0 * njs * secondsPerBeat);
            return wanted - HalfJumpBase(njs, bpm);
        }

        /// <summary>目標反応時間 (秒) を出すための _noteJumpStartBeatOffset。</summary>
        public static double OffsetForReactionTime(double njs, double bpm, double reactionTimeSeconds)
        {
            // reactionTime = secondsPerBeat * halfJumpBeats
            double secondsPerBeat = 60.0 / bpm;
            double wanted = reactionTimeSeconds / secondsPerBeat;
            return wanted - HalfJumpBase(njs, bpm);
        }
    }
}
