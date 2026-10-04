using System;

namespace JumpDrill.Output
{
    /// <summary>
    /// 画像ライブラリを足さずに済ませるための最小の描画面。
    /// 8bit RGB のピクセル列を持ち、そのまま <see cref="PngWriter"/> に渡す。
    /// </summary>
    public sealed class PixelCanvas
    {
        public byte[] Pixels { get; }
        private readonly int _width, _height;

        public PixelCanvas(int width, int height)
        {
            _width = width;
            _height = height;
            Pixels = new byte[width * height * 3];
        }

        public void Fill(byte r, byte g, byte b)
        {
            for (int i = 0; i < Pixels.Length; i += 3)
            {
                Pixels[i] = r; Pixels[i + 1] = g; Pixels[i + 2] = b;
            }
        }

        public void Set(int x, int y, byte r, byte g, byte b)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height) return;
            int i = (y * _width + x) * 3;
            Pixels[i] = r; Pixels[i + 1] = g; Pixels[i + 2] = b;
        }

        public void Rect(int x, int y, int w, int h, byte r, byte g, byte b, bool filled)
        {
            if (filled)
            {
                for (int yy = y; yy < y + h; yy++)
                    for (int xx = x; xx < x + w; xx++)
                        Set(xx, yy, r, g, b);
                return;
            }

            const int Thickness = 2;
            for (int t = 0; t < Thickness; t++)
            {
                for (int xx = x; xx < x + w; xx++)
                {
                    Set(xx, y + t, r, g, b);
                    Set(xx, y + h - 1 - t, r, g, b);
                }
                for (int yy = y; yy < y + h; yy++)
                {
                    Set(x + t, yy, r, g, b);
                    Set(x + w - 1 - t, yy, r, g, b);
                }
            }
        }

        /// <summary>
        /// 既にある色に重ねる。<paramref name="alpha"/> は 0-1。
        /// </summary>
        /// <remarks>
        /// 軌道の図で要る。1本1本を薄く重ねて、<b>束の太さがそのまま再現性に見える</b>
        /// ようにするので、不透明で塗ると全部つぶれて1色の塊になる。
        /// </remarks>
        public void Blend(int x, int y, byte r, byte g, byte b, double alpha)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height) return;
            if (alpha <= 0.0) return;
            if (alpha > 1.0) alpha = 1.0;

            int i = (y * _width + x) * 3;
            Pixels[i] = Mix(Pixels[i], r, alpha);
            Pixels[i + 1] = Mix(Pixels[i + 1], g, alpha);
            Pixels[i + 2] = Mix(Pixels[i + 2], b, alpha);
        }

        /// <summary>画素の座標にする。int に収まらない値（無限大など）は端に寄せる。</summary>
        private static int ClampToInt(double value)
        {
            if (value < int.MinValue / 2) return int.MinValue / 2;
            if (value > int.MaxValue / 2) return int.MaxValue / 2;
            return (int)value;
        }

        private static byte Mix(byte under, byte over, double alpha)
        {
            return (byte)(under + (over - under) * alpha + 0.5);
        }

        /// <summary>
        /// 太さと濃さを指定して線を引く。端は丸い。
        /// </summary>
        /// <remarks>
        /// 線分までの距離で濃さを決める。ギザギザのまま VR に出すと目立つので、
        /// 縁を1px ぶん薄くしてなまらせる。Bresenham では太さも濃さも作れない。
        /// </remarks>
        /// <param name="thickness">線の太さ (px)。1 未満は 1 として扱う。</param>
        /// <param name="alpha">濃さ 0-1。</param>
        public void LineAA(double x0, double y0, double x1, double y1,
                           byte r, byte g, byte b, double thickness = 1.0, double alpha = 1.0)
        {
            if (thickness < 1.0) thickness = 1.0;

            double half = thickness / 2.0;
            double pad = half + 1.0;

            if (double.IsNaN(x0) || double.IsNaN(y0) || double.IsNaN(x1) || double.IsNaN(y1)) return;

            int left = ClampToInt(Math.Floor(Math.Min(x0, x1) - pad));
            int right = ClampToInt(Math.Ceiling(Math.Max(x0, x1) + pad));
            int top = ClampToInt(Math.Floor(Math.Min(y0, y1) - pad));
            int bottom = ClampToInt(Math.Ceiling(Math.Max(y0, y1) + pad));

            // 画面の外は描かない。範囲を切らないと、画面から大きく外れた点を渡されたとき
            // その距離に比例した回数だけ空回りする
            if (left < 0) left = 0;
            if (top < 0) top = 0;
            if (right > _width - 1) right = _width - 1;
            if (bottom > _height - 1) bottom = _height - 1;
            if (left > right || top > bottom) return;

            double dx = x1 - x0, dy = y1 - y0;
            double lengthSquared = dx * dx + dy * dy;

            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    double distance = DistanceToSegment(x, y, x0, y0, dx, dy, lengthSquared);

                    // 芯は満濃度、そこから 1px かけて 0 へ落とす
                    double coverage = half + 0.5 - distance;
                    if (coverage <= 0.0) continue;
                    if (coverage > 1.0) coverage = 1.0;

                    Blend(x, y, r, g, b, alpha * coverage);
                }
            }
        }

        private static double DistanceToSegment(double px, double py,
                                                double x0, double y0, double dx, double dy, double lengthSquared)
        {
            if (lengthSquared <= 0.0)
            {
                double ax = px - x0, ay = py - y0;
                return Math.Sqrt(ax * ax + ay * ay);
            }

            double t = ((px - x0) * dx + (py - y0) * dy) / lengthSquared;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;

            double ex = px - (x0 + t * dx);
            double ey = py - (y0 + t * dy);
            return Math.Sqrt(ex * ex + ey * ey);
        }

        /// <summary>
        /// 多角形を塗る。ノーツの四角と矢印の三角に使う。
        /// </summary>
        /// <remarks>
        /// 画素を 2x2 に割って中に入った数で濃さを決める。
        /// 縁を丸ごと塗るか捨てるかにすると、回したノーツの角がギザギザに出る。
        /// </remarks>
        public void FillPolygon(double[] xs, double[] ys, byte r, byte g, byte b, double alpha = 1.0)
        {
            if (xs == null || ys == null || xs.Length < 3 || xs.Length != ys.Length) return;

            double minX = xs[0], maxX = xs[0], minY = ys[0], maxY = ys[0];
            for (int i = 1; i < xs.Length; i++)
            {
                if (xs[i] < minX) minX = xs[i];
                if (xs[i] > maxX) maxX = xs[i];
                if (ys[i] < minY) minY = ys[i];
                if (ys[i] > maxY) maxY = ys[i];
            }

            if (double.IsNaN(minX) || double.IsNaN(maxX) || double.IsNaN(minY) || double.IsNaN(maxY)) return;

            int left = ClampToInt(Math.Floor(minX)), right = ClampToInt(Math.Ceiling(maxX));
            int top = ClampToInt(Math.Floor(minY)), bottom = ClampToInt(Math.Ceiling(maxY));

            // 画面の外は描かない。範囲を切らないと、画面から大きく外れた点を渡されたとき
            // その距離に比例した回数だけ空回りする
            if (left < 0) left = 0;
            if (top < 0) top = 0;
            if (right > _width - 1) right = _width - 1;
            if (bottom > _height - 1) bottom = _height - 1;
            if (left > right || top > bottom) return;

            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            if (Contains(xs, ys, x + 0.25 + sx * 0.5, y + 0.25 + sy * 0.5)) inside++;

                    if (inside == 0) continue;
                    Blend(x, y, r, g, b, alpha * inside / 4.0);
                }
            }
        }

        private static bool Contains(double[] xs, double[] ys, double px, double py)
        {
            bool inside = false;
            for (int i = 0, j = xs.Length - 1; i < xs.Length; j = i++)
            {
                if ((ys[i] > py) == (ys[j] > py)) continue;
                if (px < (xs[j] - xs[i]) * (py - ys[i]) / (ys[j] - ys[i]) + xs[i]) inside = !inside;
            }
            return inside;
        }

        /// <summary>破線。実線と区別したい補助線に使う。</summary>
        public void DashedLine(double x0, double y0, double x1, double y1,
                               byte r, byte g, byte b, double thickness = 1.0, double alpha = 1.0,
                               double dash = 6.0)
        {
            double dx = x1 - x0, dy = y1 - y0;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.0) return;

            dx /= length;
            dy /= length;

            for (double at = 0; at < length; at += dash * 2)
            {
                double end = Math.Min(at + dash, length);
                LineAA(x0 + dx * at, y0 + dy * at, x0 + dx * end, y0 + dy * end, r, g, b, thickness, alpha);
            }
        }

        /// <summary>塗りつぶした円。打点の印に使う。</summary>
        public void Disc(double cx, double cy, double radius, byte r, byte g, byte b, double alpha = 1.0)
        {
            if (double.IsNaN(cx) || double.IsNaN(cy) || double.IsNaN(radius)) return;

            int left = ClampToInt(Math.Floor(cx - radius - 1));
            int right = ClampToInt(Math.Ceiling(cx + radius + 1));
            int top = ClampToInt(Math.Floor(cy - radius - 1));
            int bottom = ClampToInt(Math.Ceiling(cy + radius + 1));

            // 画面の外は描かない。範囲を切らないと、画面から大きく外れた点を渡されたとき
            // その距離に比例した回数だけ空回りする
            if (left < 0) left = 0;
            if (top < 0) top = 0;
            if (right > _width - 1) right = _width - 1;
            if (bottom > _height - 1) bottom = _height - 1;
            if (left > right || top > bottom) return;

            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    double ex = x - cx, ey = y - cy;
                    double coverage = radius + 0.5 - Math.Sqrt(ex * ex + ey * ey);
                    if (coverage <= 0.0) continue;
                    if (coverage > 1.0) coverage = 1.0;

                    Blend(x, y, r, g, b, alpha * coverage);
                }
            }
        }

        public void Line(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                // 3px 幅にして縮小表示でも見えるようにする。
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                        Set(x0 + ox, y0 + oy, r, g, b);

                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
