using System;
using JumpDrill.Output;
using UnityEngine;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// リプレイを再生するボタンの絵。🔁（U+1F501）と同じ見た目。
    /// </summary>
    /// <remarks>
    /// <b>絵文字を文字で置かない。</b> 絵文字が出るかは文字のフォント次第で、
    /// 出なければ豆腐（□）になる。青い角丸の四角に白いループ矢印を自前で描き、
    /// 押せる画像（<c>clickable-image</c>）として置く。
    /// 描くのは JumpDrill.Core の <see cref="PixelCanvas"/>。1枚あれば全部の行で使い回せる。
    /// </remarks>
    internal static class ReplayIcon
    {
        private const int Size = 64;

        private static readonly byte[] Blue = { 0x3B, 0x88, 0xC3 };
        private static readonly byte[] White = { 0xFF, 0xFF, 0xFF };

        private static Sprite? cached;

        internal static Sprite? Sprite
        {
            get
            {
                if (cached != null) return cached;

                try
                {
                    cached = SpriteFactory.FromPng(Render());
                }
                catch (Exception e)
                {
                    Plugin.Log?.Error("could not draw the replay icon: " + e);
                }

                return cached;
            }
        }

        private static byte[] Render()
        {
            var canvas = new PixelCanvas(Size, Size);

            // 背景は周りのパネルと同じ暗い色。角丸の外が浮かないように
            canvas.Fill(0x14, 0x16, 0x1A);

            // 青い角丸の四角
            RoundedSquare(canvas, 3, 3, Size - 6, 12, Blue);

            // 白いループ。上の辺は右向き、下の辺は左向きに回り、両端を半円でつなぐ
            const double Left = 20, Right = 44, Top = 22, Bottom = 42, Thick = 5.5;
            double radius = (Bottom - Top) / 2.0;
            double cy = (Top + Bottom) / 2.0;

            canvas.LineAA(Left, Top, Right - 4, Top, White[0], White[1], White[2], Thick);
            canvas.LineAA(Right, Bottom, Left + 4, Bottom, White[0], White[1], White[2], Thick);

            Arc(canvas, Right, cy, radius, -90, 90, Thick);   // 右の半円（上から下へ）
            Arc(canvas, Left, cy, radius, 90, 270, Thick);    // 左の半円（下から上へ）

            // 矢尻。上の辺の右端は右向き、下の辺の左端は左向き
            canvas.FillPolygon(new[] { Right - 8, Right + 3, Right - 8 }, new[] { Top - 7, Top, Top + 7 },
                               White[0], White[1], White[2]);
            canvas.FillPolygon(new[] { Left + 8, Left - 3, Left + 8 }, new[] { Bottom - 7, Bottom, Bottom + 7 },
                               White[0], White[1], White[2]);

            return PngWriter.Encode(canvas.Pixels, Size, Size);
        }

        /// <summary>中心 (cx, cy)、半径 r の円弧を度で指定して描く。0° が右、画像は下が正。</summary>
        private static void Arc(PixelCanvas canvas, double cx, double cy, double r,
                                double fromDegrees, double toDegrees, double thickness)
        {
            const int Steps = 16;
            double previousX = 0, previousY = 0;

            for (int i = 0; i <= Steps; i++)
            {
                double degrees = fromDegrees + (toDegrees - fromDegrees) * i / Steps;
                double radians = degrees * Math.PI / 180.0;
                double x = cx + Math.Cos(radians) * r;
                double y = cy + Math.Sin(radians) * r;

                if (i > 0)
                    canvas.LineAA(previousX, previousY, x, y, White[0], White[1], White[2], thickness);

                previousX = x;
                previousY = y;
            }
        }

        private static void RoundedSquare(PixelCanvas canvas, double x, double y, double size, double radius, byte[] color)
        {
            // 角ごとに円弧を刻んで多角形にする
            var xs = new System.Collections.Generic.List<double>();
            var ys = new System.Collections.Generic.List<double>();

            void Corner(double cx, double cy, double from)
            {
                for (int i = 0; i <= 6; i++)
                {
                    double radians = (from + 90.0 * i / 6) * Math.PI / 180.0;
                    xs.Add(cx + Math.Cos(radians) * radius);
                    ys.Add(cy + Math.Sin(radians) * radius);
                }
            }

            Corner(x + size - radius, y + radius, -90);          // 右上
            Corner(x + size - radius, y + size - radius, 0);     // 右下
            Corner(x + radius, y + size - radius, 90);           // 左下
            Corner(x + radius, y + radius, 180);                 // 左上

            canvas.FillPolygon(xs.ToArray(), ys.ToArray(), color[0], color[1], color[2]);
        }
    }
}
