namespace JumpDrill.Model
{
    /// <summary>複数の手を指定したときの噛み合わせ方。</summary>
    public enum HandPattern
    {
        /// <summary>同時。各ステップで全ての手にノーツが出る。</summary>
        Sync,

        /// <summary>交互。ステップごとに手が入れ替わる（手あたりの実効間隔は倍）。</summary>
        Alternate,

        /// <summary>
        /// 片手ずつ。1つ目の手で丸ごと1本ぶん叩いてから、間を空けて次の手に移る。
        /// ジャンプ練習で普通に欲しいのはこれ。同じ配置を連続で往復しないと
        /// 軌道が作られないので、交互では練習にならない。
        /// この形のときは尺の指定が「手あたりの秒数」になる。
        /// </summary>
        Split,
    }
}
