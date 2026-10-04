namespace JumpDrill.Model
{
    /// <summary>
    /// Beat Saber の _cutDirection。値は「セイバーが進む向き」で、
    /// ノーツの矢印が指す向きと同じ。
    /// </summary>
    public enum CutDirection
    {
        Up = 0,
        Down = 1,
        Left = 2,
        Right = 3,
        UpLeft = 4,
        UpRight = 5,
        DownLeft = 6,
        DownRight = 7,
        Any = 8,
    }

    public static class CutDirectionExtensions
    {
        /// <summary>切り下げ側か（Down / DownLeft / DownRight）。</summary>
        public static bool IsDownward(this CutDirection d)
        {
            return d == CutDirection.Down || d == CutDirection.DownLeft || d == CutDirection.DownRight;
        }

        /// <summary>切り上げ側か（Up / UpLeft / UpRight）。</summary>
        public static bool IsUpward(this CutDirection d)
        {
            return d == CutDirection.Up || d == CutDirection.UpLeft || d == CutDirection.UpRight;
        }

        /// <summary>単位ベクトル (dx, dy)。dy は上が正。Any は (0,0)。</summary>
        public static void ToVector(this CutDirection d, out int dx, out int dy)
        {
            switch (d)
            {
                case CutDirection.Up: dx = 0; dy = 1; return;
                case CutDirection.Down: dx = 0; dy = -1; return;
                case CutDirection.Left: dx = -1; dy = 0; return;
                case CutDirection.Right: dx = 1; dy = 0; return;
                case CutDirection.UpLeft: dx = -1; dy = 1; return;
                case CutDirection.UpRight: dx = 1; dy = 1; return;
                case CutDirection.DownLeft: dx = -1; dy = -1; return;
                case CutDirection.DownRight: dx = 1; dy = -1; return;
                default: dx = 0; dy = 0; return;
            }
        }

        /// <summary>正反対の向き。Any は Any のまま。</summary>
        public static CutDirection Opposite(this CutDirection d)
        {
            switch (d)
            {
                case CutDirection.Up: return CutDirection.Down;
                case CutDirection.Down: return CutDirection.Up;
                case CutDirection.Left: return CutDirection.Right;
                case CutDirection.Right: return CutDirection.Left;
                case CutDirection.UpLeft: return CutDirection.DownRight;
                case CutDirection.UpRight: return CutDirection.DownLeft;
                case CutDirection.DownLeft: return CutDirection.UpRight;
                case CutDirection.DownRight: return CutDirection.UpLeft;
                default: return CutDirection.Any;
            }
        }
    }
}
