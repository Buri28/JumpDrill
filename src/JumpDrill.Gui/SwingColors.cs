using System.Drawing;
using JumpDrill.Replays;

namespace JumpDrill.Gui
{
    /// <summary>
    /// 振りとノーツの色。Core の <see cref="SwingPalette"/>（MOD の図・VR 表示と同じ色）を GDI の色にしたもの。
    /// フォア（切り下げ）は手の色、バック（切り上げ）は手の色をずらした色。
    /// フォアとバックの分け方は Core の <see cref="SwingSides"/>。
    /// </summary>
    internal static class SwingColors
    {
        public static readonly Color Right = From(SwingPalette.Right);
        public static readonly Color Left = From(SwingPalette.Left);
        public static readonly Color RightBack = From(SwingPalette.RightBack);
        public static readonly Color LeftBack = From(SwingPalette.LeftBack);
        public static readonly Color LeftMark = From(SwingPalette.LeftMark);

        public static Color Of(int hand, bool forehand)
        {
            return From(SwingPalette.Of(hand, forehand));
        }

        private static Color From(byte[] rgb)
        {
            return Color.FromArgb(rgb[0], rgb[1], rgb[2]);
        }
    }
}
