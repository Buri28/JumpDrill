using System;

namespace JumpDrill.Replays
{
    /// <summary>
    /// 1ノーツの素点。Beat Saber 本体の <c>ScoreModel</c> と同じ出し方をする。
    ///
    /// 115点の内訳は「振りかぶり 70 + 振り抜き 30 + 中心からの距離 15」。
    /// 前2つは振りの<b>角度</b>で決まるので、まとめて「角度点（100点満点）」として扱う。
    ///
    /// リプレイには角度そのものではなく本体が算出した rating (0-1) が入っているので、
    /// こちらで角度を測り直す必要はない。
    /// </summary>
    public static class NoteScore
    {
        /// <summary>振りかぶりの満点。</summary>
        public const int BeforeCutMax = 70;

        /// <summary>振り抜きの満点。</summary>
        public const int AfterCutMax = 30;

        /// <summary>角度点の満点。振りかぶり＋振り抜き。</summary>
        public const int SwingAngleMax = BeforeCutMax + AfterCutMax;

        /// <summary>中心点の満点。</summary>
        public const int CutDistanceMax = 15;

        /// <summary>1ノーツの満点。角度点 100 ＋ 中心点 15。</summary>
        public const int Max = SwingAngleMax + CutDistanceMax;

        /// <summary>これだけ中心から外れると中心点は 0 になる (m)。</summary>
        public const double CutDistanceRadius = 0.3;

        /// <summary>振りかぶりの点 (0-70)。</summary>
        public static int BeforeCut(NoteCutInfo cut)
        {
            if (cut == null) return 0;
            return (int)Math.Round(BeforeCutMax * Clamp01(cut.BeforeCutRating));
        }

        /// <summary>振り抜きの点 (0-30)。</summary>
        public static int AfterCut(NoteCutInfo cut)
        {
            if (cut == null) return 0;
            return (int)Math.Round(AfterCutMax * Clamp01(cut.AfterCutRating));
        }

        /// <summary>角度点 (0-100)。振りかぶり 70 + 振り抜き 30。</summary>
        public static int SwingAngle(NoteCutInfo cut)
        {
            return BeforeCut(cut) + AfterCut(cut);
        }

        /// <summary>
        /// 時間依存 (TD)。ノーツ面の法線の z 成分の大きさ。
        ///
        /// BeatLeader が <c>TD</c> として出しているもの
        /// （<c>ReplayStatisticUtils::Accuracy</c> が <c>Math.Abs(cutNormal.z)</c> を平均している）。
        /// 大きいほど、切る向きが奥行き方向に寝ている＝ノーツが飛んでくるのに合わせて
        /// 振りを合わせに行っている。小さいほど良い。
        /// </summary>
        public static double TimeDependence(NoteCutInfo cut)
        {
            if (cut == null) return 0.0;
            return Math.Abs(cut.CutNormal.Z);
        }

        /// <summary>中心点 (0-15)。ノーツ中心からの距離だけで決まる。</summary>
        public static int CutDistance(NoteCutInfo cut)
        {
            if (cut == null) return 0;
            double away = Clamp01(cut.CutDistanceToCenter / CutDistanceRadius);
            return (int)Math.Round(CutDistanceMax * (1.0 - away));
        }

        /// <summary>1ノーツの合計 (0-115)。</summary>
        public static int Total(NoteCutInfo cut)
        {
            return SwingAngle(cut) + CutDistance(cut);
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }
    }
}
