namespace JumpDrill.Model
{
    /// <summary>クリックの粒度（設計メモ §3）。</summary>
    public enum ClickMode
    {
        /// <summary>鳴らさない。</summary>
        None = 0,

        /// <summary>Lv1: 全ノーツ（弱）＋切り下げにアクセント。</summary>
        All = 1,

        /// <summary>Lv2: 切り下げのみ。標準。</summary>
        Down = 2,

        /// <summary>Lv3: 切り上げのみ。苦手側をアンカーにする。</summary>
        Up = 3,

        /// <summary>Lv4: 4ノーツに1回。</summary>
        Every4 = 4,

        /// <summary>
        /// 全ノーツを3段の強さで鳴らす。小節の頭（4拍ごと）は強拍、拍の頭はふつう、拍の間は弱く短く。
        /// 拍は NotesPerBeat（1拍あたりのノーツ数）とブロックの頭から数える。速い BPM で拍の頭を見失わないようにするため。
        /// </summary>
        Beat = 5,
    }
}
