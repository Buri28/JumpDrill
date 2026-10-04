namespace JumpDrill.Model
{
    /// <summary>音源に焼き込むクリック1発。</summary>
    public struct ClickEvent
    {
        public double TimeSeconds { get; }

        /// <summary>アクセント音（高い方）で鳴らすか。</summary>
        public bool Accent { get; }

        public ClickEvent(double timeSeconds, bool accent)
        {
            TimeSeconds = timeSeconds;
            Accent = accent;
        }
    }
}
