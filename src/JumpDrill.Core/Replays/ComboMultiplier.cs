namespace JumpDrill.Replays
{
    /// <summary>
    /// コンボ倍率。本体の <c>ScoreMultiplierCounter</c> と同じ動き。
    ///
    /// 倍率は ×1 → ×2 → ×4 → ×8。いまの倍率の 2 倍の個数を続けて切ると 1 段上がる。
    /// Bad Cut・ミス・爆弾で 1 段下がり、上がるまでの数え直しになる。
    ///
    /// 本体の <c>ScoreController.LateUpdate</c> は倍率を<b>上げてから</b>そのノーツに掛ける。
    /// なので ×2 は 2 個目から、×8 は 14 個目から掛かる。
    /// </summary>
    public sealed class ComboMultiplier
    {
        private int _progress;

        /// <summary>いまの倍率。</summary>
        public int Multiplier { get; private set; } = 1;

        /// <summary>切れたノーツ 1 個ぶん。倍率を進めてから、そのノーツに掛かる倍率を返す。</summary>
        public int Hit()
        {
            if (Multiplier < NoteScore.MaxMultiplier)
            {
                _progress++;
                if (_progress >= Multiplier * 2)
                {
                    Multiplier *= 2;
                    _progress = 0;
                }
            }
            return Multiplier;
        }

        /// <summary>Bad Cut・ミス・爆弾。</summary>
        public void Break()
        {
            if (Multiplier > 1) Multiplier /= 2;
            _progress = 0;
        }
    }
}
