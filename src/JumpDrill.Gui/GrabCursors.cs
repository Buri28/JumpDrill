using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace JumpDrill.Gui
{
    /// <summary>
    /// つかんで動かせる所に出す手のカーソル。WinForms には「開いた手」「握った手」のカーソルが無いので、
    /// 記号（✋ ✊）を白地に黒の縁取りで描いて作る。作れなければ指さしのカーソルで代える。
    /// </summary>
    internal static class GrabCursors
    {
        private static Cursor _open;
        private static Cursor _closed;

        /// <summary>つかめる所に乗せたとき（開いた手）。</summary>
        public static Cursor Open { get { return _open ?? (_open = Make("✋")); } }

        /// <summary>つかんで動かしている間（握った手）。</summary>
        public static Cursor Closed { get { return _closed ?? (_closed = Make("✊")); } }

        private static Cursor Make(string glyph)
        {
            try
            {
                // 大きさは画面の拡大率に合わせる（拡大率 150% なら 48 画素）。手は枠いっぱい近くまで大きく描く
                float scale;
                using (var screen = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, screen.DpiX / 96f);
                int Size = (int)Math.Round(32 * scale);

                using (var bitmap = new Bitmap(Size, Size))
                using (var g = Graphics.FromImage(bitmap))
                using (var font = new Font("Segoe UI Symbol", Size * 0.82f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var outline = new SolidBrush(Color.Black))
                using (var fill = new SolidBrush(Color.White))
                {
                    g.Clear(Color.Transparent);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    var area = new RectangleF(0, 0, Size, Size);

                    // 白い手に黒い縁取り（Windows の矢印カーソルと同じ配色）。暗い画面でも明るい画面でも見える
                    int edge = Math.Max(1, (int)Math.Round(scale));
                    for (int dx = -edge; dx <= edge; dx++)
                        for (int dy = -edge; dy <= edge; dy++)
                            if (dx != 0 || dy != 0)
                                g.DrawString(glyph, font, outline, new RectangleF(dx, dy, Size, Size), format);
                    g.DrawString(glyph, font, fill, area, format);

                    // アイコンから作ったカーソルは、押す位置が絵の真ん中になる。つかむ手ならそれでよい
                    return new Cursor(bitmap.GetHicon());
                }
            }
            catch (Exception)
            {
                return Cursors.Hand;
            }
        }
    }
}
