namespace JumpDrill.Model
{
    /// <summary>生成された1ノーツ。</summary>
    public sealed class DrillNote
    {
        /// <summary>曲頭からの秒。クリック音の焼き込みもこれを基準にする。</summary>
        public double TimeSeconds { get; }

        /// <summary>_time に書く拍。BPM から換算した実数値（量子化しない）。</summary>
        public double Beat { get; }

        public GridPosition Position { get; }
        public Hand Hand { get; }
        public CutDirection Direction { get; }

        /// <summary>ドリルの何ステップ目か。0 始まり。</summary>
        public int StepIndex { get; }

        public DrillNote(double timeSeconds, double beat, GridPosition position, Hand hand, CutDirection direction, int stepIndex)
        {
            TimeSeconds = timeSeconds;
            Beat = beat;
            Position = position;
            Hand = hand;
            Direction = direction;
            StepIndex = stepIndex;
        }
    }
}
