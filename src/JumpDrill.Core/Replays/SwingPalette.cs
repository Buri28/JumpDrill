namespace JumpDrill.Replays
{
    /// <summary>
    /// 振りの図の色（RGB）。GUI の正面・横・3D の図、MOD の図と VR 表示で同じ色を使う。
    /// </summary>
    public static class SwingPalette
    {
        public static readonly byte[] Background = { 0x14, 0x16, 0x1A };
        public static readonly byte[] Grid = { 0x2A, 0x2E, 0x36 };

        /// <summary>右手・左手（フォア＝切り下げ）。</summary>
        public static readonly byte[] Right = { 0x2E, 0x86, 0xD8 };
        public static readonly byte[] Left = { 0xD0, 0x3A, 0x3A };

        /// <summary>
        /// バック（切り上げ）は手の色をずらす。タワー・スタックでは行きと帰りが同じ所を通るので、色で分ける。
        /// </summary>
        public static readonly byte[] RightBack = { 0x2E, 0xC8, 0xC0 };
        public static readonly byte[] LeftBack = { 0xE8, 0x95, 0x2E };

        /// <summary>
        /// 左手のフォアの向きの印（「く」）。同じ彩度でも赤は暗く見える（目に見える明るさが赤 103 に対し青 117）ので、
        /// 細い線では赤を明るくして青と釣り合わせる。
        /// </summary>
        public static readonly byte[] LeftMark = { 0xE6, 0x55, 0x55 };

        /// <summary>打点・切った線・三角などの白。</summary>
        public static readonly byte[] White = { 0xF2, 0xF5, 0xFA };

        /// <summary>手とフォア・バックの色。</summary>
        public static byte[] Of(int hand, bool forehand)
        {
            return hand == 0 ? (forehand ? Left : LeftBack) : (forehand ? Right : RightBack);
        }

        /// <summary>向きの印の色。左手のフォアだけ明るい赤。</summary>
        public static byte[] MarkOf(int hand, bool forehand)
        {
            return hand == 0 && forehand ? LeftMark : Of(hand, forehand);
        }
    }
}
