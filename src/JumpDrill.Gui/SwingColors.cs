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
        /// 振りの折り返し点の番号。次に切るノーツの矢印の向きで見て、いちばん手前に戻った点。
        /// そこまでが前のノーツの振り抜き、そこからが次のノーツの振りかぶり。
        ///
        /// 区間ごとに動く向きで分けると、折り返しの近く（刃がほぼ横に動く所）で判定が細かく入れ替わり、
        /// フォアだけ・バックだけを出したときに振りが途切れ途切れになった。1本の振りの切り替わりは1か所にする。
        /// 向き指定なしのノーツなら 0（全部が次のノーツの振り）。
        /// </summary>
        public static int TurnIndex(Vector3[] path, PointF toDirection)
        {
            if (toDirection.X == 0 && toDirection.Y == 0) return 0;

            int turn = 0;
            double lowest = double.MaxValue;
            for (int i = 0; i < path.Length; i++)
            {
                double along = path[i].X * toDirection.X + path[i].Y * toDirection.Y;
                if (along < lowest) { lowest = along; turn = i; }
            }
            return turn;
        }

        /// <summary>振りの i 番目の区間（i-1 → i）がフォアか。折り返し点（<see cref="TurnIndex"/>）の前後で分ける。</summary>
        public static bool SegmentForehand(int i, int turn, bool toForehand, bool fromForehand)
        {
            return i > turn ? toForehand : fromForehand;
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
