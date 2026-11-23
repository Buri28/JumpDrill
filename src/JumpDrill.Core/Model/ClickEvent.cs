namespace JumpDrill.Model
{
    /// <summary>音源に焼き込むクリック1発。</summary>
    public struct ClickEvent
    {
        public double TimeSeconds { get; }

        /// <summary>アクセント音（高い方）で鳴らすか。</summary>
        public bool Accent { get; }

        /// <summary>カウントインの1発か。クリックの音色に関係なく、決まった音で鳴らす。</summary>
        public bool CountIn { get; }

        /// <summary>弱く短い音で鳴らすか（粒度 Beat の拍の間）。Accent・CountIn とは同時に立たない。</summary>
        public bool Weak { get; }

        public ClickEvent(double timeSeconds, bool accent)
            : this(timeSeconds, accent, false) { }

        public ClickEvent(double timeSeconds, bool accent, bool countIn)
            : this(timeSeconds, accent, countIn, false) { }

        public ClickEvent(double timeSeconds, bool accent, bool countIn, bool weak)
        {
            TimeSeconds = timeSeconds;
            Accent = accent;
            CountIn = countIn;
            Weak = weak && !accent && !countIn;
        }
    }
}
