using System;

namespace JumpDrill.Model
{
    /// <summary>グリッド上のベクトルと切る方向の相互変換。</summary>
    public static class Geometry
    {
        // 角度 0°(=Right) から 45° 刻みで反時計回り。
        private static readonly CutDirection[] ByOctant =
        {
            CutDirection.Right,     //    0°
            CutDirection.UpRight,   //   45°
            CutDirection.Up,        //   90°
            CutDirection.UpLeft,    //  135°
            CutDirection.Left,      //  180°
            CutDirection.DownLeft,  //  225°
            CutDirection.Down,      //  270°
            CutDirection.DownRight, //  315°
        };

        /// <summary>
        /// ベクトル (dx, dy) を最も近い8方向に丸める。dy は上が正。
        /// 例: Δ(1,2) の対角2層跳びは UpRight になる。
        /// </summary>
        public static CutDirection FromVector(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return CutDirection.Any;

            double octant = Math.Atan2(dy, dx) / (Math.PI / 4.0);
            int index = (int)Math.Round(octant, MidpointRounding.AwayFromZero);
            index = ((index % 8) + 8) % 8;
            return ByOctant[index];
        }

        /// <summary>
        /// ベクトル (dx, dy) を<b>上下左右の4方向だけ</b>に丸める。記法の <c>s</c> 用。
        ///
        /// 8方向に丸めてから直す形にはできない。Δ(-1,-2) は DownLeft になるが、
        /// そこからでは Down と Left のどちらへ倒すべきか分からなくなる。
        /// <b>元のベクトルの縦横どちらが大きいか</b>で決める必要がある。
        ///
        /// 縦横が同じ長さ（ちょうど 45°）のときは縦を採る。
        /// ジャンプの練習で欲しいのは縦振りの方なので。
        /// </summary>
        public static CutDirection StraightFromVector(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return CutDirection.Any;

            if (Math.Abs(dy) >= Math.Abs(dx))
                return dy > 0 ? CutDirection.Up : CutDirection.Down;

            return dx > 0 ? CutDirection.Right : CutDirection.Left;
        }

        /// <summary>from から to へ向かうベクトルを丸める。</summary>
        /// <param name="straight">上下左右の4方向だけに倒す（記法の <c>s</c>）。</param>
        public static CutDirection FromTransition(GridPosition from, GridPosition to, bool straight = false)
        {
            int dx = to.LineIndex - from.LineIndex;
            int dy = to.LineLayer - from.LineLayer;

            return straight ? StraightFromVector(dx, dy) : FromVector(dx, dy);
        }

        /// <summary>
        /// from→to の軸に垂直な方向。<paramref name="counterClockwise"/> で
        /// 直交する2方向のどちらを取るかを選ぶ。
        /// </summary>
        /// <param name="straight">上下左右の4方向だけに倒す（記法の <c>s</c>）。</param>
        public static CutDirection PerpendicularTo(GridPosition from, GridPosition to, bool counterClockwise, bool straight = false)
        {
            int dx = to.LineIndex - from.LineIndex;
            int dy = to.LineLayer - from.LineLayer;
            if (dx == 0 && dy == 0) return CutDirection.Any;

            // (dx, dy) を 90° 回す。反時計回りなら (-dy, dx)。
            int rx = counterClockwise ? -dy : dy;
            int ry = counterClockwise ? dx : -dx;

            return straight ? StraightFromVector(rx, ry) : FromVector(rx, ry);
        }
    }
}
