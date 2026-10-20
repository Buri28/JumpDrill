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

        /// <summary>
        /// 明るくする。GUI の <c>ControlPaint.Light</c> と同じ計算（色合いと鮮やかさは変えず、明るさだけを
        /// 白までの残りの半分 × <paramref name="amount"/> だけ上げる。HLS で、明るさの上限が 240）。
        /// 白へまっすぐ寄せると色が抜けて灰色に見え、GUI の扇（手の色の付いた薄い面）と違って見えた。
        /// </summary>
        public static byte[] Light(byte[] rgb, double amount)
        {
            double r = rgb[0] / 255.0, g = rgb[1] / 255.0, b = rgb[2] / 255.0;
            double max = System.Math.Max(r, System.Math.Max(g, b)), min = System.Math.Min(r, System.Math.Min(g, b));
            double l = (max + min) / 2, h = 0, s = 0;
            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h /= 6;
            }

            l += (1 - l) * 0.5 * amount;

            if (s == 0) return new[] { ToByte(l), ToByte(l), ToByte(l) };
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            return new[] { ToByte(Hue(p, q, h + 1.0 / 3)), ToByte(Hue(p, q, h)), ToByte(Hue(p, q, h - 1.0 / 3)) };
        }

        private static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        private static byte ToByte(double v)
        {
            return (byte)System.Math.Max(0, System.Math.Min(255, (int)System.Math.Round(v * 255)));
        }

        /// <summary>向きの印の色。左手のフォアだけ明るい赤。</summary>
        public static byte[] MarkOf(int hand, bool forehand)
        {
            return hand == 0 && forehand ? LeftMark : Of(hand, forehand);
        }
    }
}
