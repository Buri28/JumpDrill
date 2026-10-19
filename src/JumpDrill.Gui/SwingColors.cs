using System.Drawing;
using JumpDrill.Replays;

namespace JumpDrill.Gui
{
    /// <summary>
    /// 振りとノーツの色。フォア（切り下げ）は手の色、バック（切り上げ）は手の色をずらした色。
    /// タワー・スタックでは行きと帰りが同じ所を通るので、色で分ける。正面・横・3D の図で同じ色を使う。
    /// </summary>
    internal static class SwingColors
    {
        public static readonly Color Right = Color.FromArgb(0x2E, 0x86, 0xD8);
        public static readonly Color Left = Color.FromArgb(0xD0, 0x3A, 0x3A);
        public static readonly Color RightBack = Color.FromArgb(0x2E, 0xC8, 0xC0);
        public static readonly Color LeftBack = Color.FromArgb(0xE8, 0x95, 0x2E);

        public static Color Of(int hand, bool forehand)
        {
            return hand == 0 ? (forehand ? Left : LeftBack) : (forehand ? Right : RightBack);
        }

        /// <summary>
        /// 振りの1区間（先端が <paramref name="a"/> から <paramref name="b"/> へ動いた所）がフォアか。
        ///
        /// 刃が動いている向きで決める。次に切るノーツの矢印の向きに動いていればそのノーツの種類、
        /// 逆向きなら前に切ったノーツの種類。縦の振りなら、下へ動いている所がフォア、上へ動いている所がバックになる。
        /// 1本の振りは「前に切った瞬間から次に切る瞬間まで」なので、前半は前のノーツの振り抜き、後半は次のノーツの振りかぶり。
        /// 行き先だけで1色にすると、振り抜きの所まで次のノーツの色になり、図の上と下で色が分かれて見えた。
        /// </summary>
        /// <param name="toDirection">次に切るノーツの矢印の向き（世界座標の X-Y、上が正）。向き指定なしはゼロ。</param>
        public static bool SegmentForehand(Vector3 a, Vector3 b, PointF toDirection, bool toForehand, bool fromForehand)
        {
            if (toDirection.X == 0 && toDirection.Y == 0) return toForehand;
            double along = (b.X - a.X) * toDirection.X + (b.Y - a.Y) * toDirection.Y;
            return along >= 0 ? toForehand : fromForehand;
        }

        /// <summary>矢印の向き（世界座標の X-Y、上が正、長さは気にしない）。向き指定なしはゼロ。</summary>
        public static PointF ArrowOf(int cutDirection)
        {
            switch (cutDirection)
            {
                case 0: return new PointF(0, 1);     // Up
                case 1: return new PointF(0, -1);    // Down
                case 2: return new PointF(-1, 0);    // Left
                case 3: return new PointF(1, 0);     // Right
                case 4: return new PointF(-1, 1);    // UpLeft
                case 5: return new PointF(1, 1);     // UpRight
                case 6: return new PointF(-1, -1);   // DownLeft
                case 7: return new PointF(1, -1);    // DownRight
                default: return PointF.Empty;        // Any
            }
        }
    }
}
