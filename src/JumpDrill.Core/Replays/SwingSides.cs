using JumpDrill.Model;

namespace JumpDrill.Replays
{
    /// <summary>
    /// 図の上でのフォア（切り下げ）とバック（切り上げ）の分け方。GUI の図と MOD の図（<see cref="SwingDiagram"/>）で同じものを使う。
    /// </summary>
    /// <remarks>
    /// 1本の振りは「前に切った瞬間から次に切る瞬間まで」なので、前半は前のノーツの振り抜き、後半は次のノーツの振りかぶり。
    /// その境目を、次に切るノーツの矢印の向きで見ていちばん手前に戻った点（折り返し点）1か所にする。
    /// 区間ごとに刃の動く向きで分けると、折り返しの近く（刃がほぼ横に動く所）で判定が細かく入れ替わり、
    /// フォアだけ・バックだけを出したときに振りが途切れ途切れになった。
    /// </remarks>
    public static class SwingSides
    {
        /// <summary>矢印の向き（世界座標の X-Y、上が正、長さは気にしない）。向き指定なしは (0, 0) で false。</summary>
        public static bool ArrowOf(int cutDirection, out double x, out double y)
        {
            switch (cutDirection)
            {
                case 0: x = 0; y = 1; return true;     // Up
                case 1: x = 0; y = -1; return true;    // Down
                case 2: x = -1; y = 0; return true;    // Left
                case 3: x = 1; y = 0; return true;     // Right
                case 4: x = -1; y = 1; return true;    // UpLeft
                case 5: x = 1; y = 1; return true;     // UpRight
                case 6: x = -1; y = -1; return true;   // DownLeft
                case 7: x = 1; y = -1; return true;    // DownRight
                default: x = 0; y = 0; return false;   // Any
            }
        }

        /// <summary>
        /// 振りの折り返し点の番号。次に切るノーツの矢印の向き（<paramref name="dirX"/>, <paramref name="dirY"/>）で見て、
        /// いちばん手前に戻った点。向き指定なし（ゼロ）なら 0（全部が次のノーツの振り）。
        /// </summary>
        public static int TurnIndex(Vector3[] path, double dirX, double dirY)
        {
            if (dirX == 0 && dirY == 0) return 0;

            int turn = 0;
            double lowest = double.MaxValue;
            for (int i = 0; i < path.Length; i++)
            {
                double along = path[i].X * dirX + path[i].Y * dirY;
                if (along < lowest) { lowest = along; turn = i; }
            }
            return turn;
        }

        /// <summary>振りの i 番目の区間（i-1 → i）がフォアか。折り返し点の前は前のノーツ、後は次のノーツの種類。</summary>
        public static bool SegmentForehand(int i, int turn, bool toForehand, bool fromForehand)
        {
            return i > turn ? toForehand : fromForehand;
        }

        /// <summary>
        /// 矢印でフォアかバックかを決める（採点と同じ、<see cref="SwingAnalyzer.IsForehand"/>）。決まらなければ false。
        /// 下向きの成分があればフォア、上向きの成分があればバック。左右だけの矢印は、上下を 90° 回したものとして
        /// 右手（1）は右から左（←）、左手（0）は左から右（→）をフォアにする。向き指定なしは決まらない。
        /// </summary>
        public static bool TryForehand(int cutDirection, int hand, out bool forehand)
        {
            var d = (CutDirection)cutDirection;
            forehand = false;
            if (d.IsDownward()) { forehand = true; return true; }
            if (d.IsUpward()) return true;
            if (cutDirection == 2) { forehand = hand == 1; return true; }   // Left（右から左）
            if (cutDirection == 3) { forehand = hand == 0; return true; }   // Right（左から右）
            return false;
        }

        /// <summary>ノーツがフォアか（<see cref="TryForehand"/>）。向き指定なしはフォア扱い。</summary>
        public static bool NoteForehand(int cutDirection, int hand)
        {
            bool forehand;
            return !TryForehand(cutDirection, hand, out forehand) || forehand;
        }

        /// <summary>
        /// 切る向きでフォアかバックかが決まるノーツか。向き指定なしだけは決まらない（採点では何回目の振りかで交互に決める）。
        /// フォアだけ・バックだけを出すときは、どちらでも出す（片方で振りが出てノーツが消える、という食い違いを避ける）。
        /// </summary>
        public static bool NoteHasSide(int cutDirection, int hand)
        {
            bool forehand;
            return TryForehand(cutDirection, hand, out forehand);
        }

        /// <summary>フォアだけ（true）・バックだけ（false）・両方（null）のとき、このノーツを出すか。</summary>
        public static bool NoteShown(int cutDirection, int hand, bool? onlyForehand)
        {
            return !onlyForehand.HasValue || !NoteHasSide(cutDirection, hand) || NoteForehand(cutDirection, hand) == onlyForehand.Value;
        }

        /// <summary>
        /// 切る向きの角度（上向きから右回りのラジアン）。<see cref="ArrowOf"/> の向きを角度にしたもの。向き指定なしは NaN。
        /// </summary>
        public static double ArrowAngle(int cutDirection)
        {
            double x, y;
            return ArrowOf(cutDirection, out x, out y) ? System.Math.Atan2(x, y) : double.NaN;
        }
    }
}
