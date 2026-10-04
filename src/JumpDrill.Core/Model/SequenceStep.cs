namespace JumpDrill.Model
{
    /// <summary>循環内の1点。方向を記法で明示した場合はそれを持つ。</summary>
    public struct SequenceStep
    {
        public GridPosition Position { get; }

        /// <summary>記法で <c>8@dl</c> のように明示された方向。無指定なら null。</summary>
        public CutDirection? ExplicitDirection { get; }

        /// <summary>
        /// 記法の <c>s</c>（Straight / Square）。この点の矢印を斜めにせず、
        /// <b>上下左右のいちばん近い向き</b>に倒す。
        ///
        /// 4↔b のように縦寄りだが少し横にずれた往復は、素直に軸へ合わせると
        /// ↙↗ の斜めになる。ジャンプの練習で欲しいのは縦振りなので、
        /// そこを ▼▲ に直すための印。向き自体は動きから決まるので、
        /// <b>配置と矛盾する矢印にはならない</b>。
        /// </summary>
        public bool Straighten { get; }

        public SequenceStep(GridPosition position, CutDirection? explicitDirection = null, bool straighten = false)
        {
            Position = position;
            ExplicitDirection = explicitDirection;
            Straighten = straighten;
        }

        public override string ToString()
        {
            if (ExplicitDirection.HasValue) return Position + "@" + ExplicitDirection.Value;
            return Straighten ? Position + "s" : Position.ToString();
        }
    }
}
