using System;
using System.Collections.Generic;
using System.IO;
using JumpDrill.Replays;
using UnityEngine;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// 軌道の図を作る。
    /// </summary>
    /// <remarks>
    /// 描くのは JumpDrill.Core（<c>SwingDiagram</c>）。GUI の図と同じ投影・同じ色で、
    /// 画像ライブラリを使わずに PNG のバイト列を返してくる。
    /// このMODがやるのは <c>Texture2D</c> に読ませて <c>Sprite</c> にするところだけ。
    ///
    /// 1枚あたりリプレイを読み直して数千フレームを走らせるので、
    /// 作ったものは持っておく。曲を選び直すたびに描き直すと待たされる。
    /// </remarks>
    internal class DiagramService
    {
        /// <summary>
        /// 図の大きさ (px)。枠に出す寸法より大きめに描いて縮小させる。
        /// 等倍だと VR で見たときに線がざらつく。
        /// 縦横比は Leaderboard.bsml の図の枠（86 × 28）に合わせる（全体の幅 904 × 28 / 86 ≒ 294）。
        /// 合っていないと図が横に伸びて見える。
        /// </summary>
        private const int Height = 294;

        /// <summary>1枚ぶんの幅。正面と横を並べるので、全体はこの倍に仕切りを足したもの。</summary>
        private const int PaneWidth = 450;

        private readonly Dictionary<string, Sprite?> cache =
            new Dictionary<string, Sprite?>(StringComparer.OrdinalIgnoreCase);

        /// <summary><see cref="cache"/> に入れた順。古いものから捨てる（1枚が 904×294 の Texture なので際限なく持たない）。</summary>
        private readonly List<string> order = new List<string>();

        /// <summary>持っておく図の数。記録の選び直しと、平均だけ・フォアとバックの切り替えで行き来する分。</summary>
        private const int MaxCached = 24;

        /// <summary>
        /// そのリプレイの図（正面と横を並べたもの）。描けなければ null。
        /// </summary>
        /// <param name="meanOnly">平均軌道だけを描くか。1本1本の束を出さない。</param>
        /// <param name="onlyForehand">フォアだけ（true）かバックだけ（false）。両方なら null。</param>
        internal Sprite? For(string? replayPath, bool meanOnly, bool? onlyForehand)
        {
            if (string.IsNullOrEmpty(replayPath)) return null;

            // 同じ名前で書き直されたリプレイは別物として描き直す
            long written;
            try { written = System.IO.File.GetLastWriteTimeUtc(replayPath).Ticks; }
            catch (Exception) { written = 0; }

            string key = replayPath + "|" + written + (meanOnly ? "|mean" : "|all")
                + (onlyForehand == true ? "|fore" : onlyForehand == false ? "|back" : "|both");

            Sprite? found;
            if (cache.TryGetValue(key, out found))
            {
                // 使ったものは新しい扱いにする（出ている図を先に捨てないように）
                order.Remove(key);
                order.Add(key);
                return found;
            }

            found = Render(replayPath!, meanOnly, onlyForehand);
            cache[key] = found;
            order.Add(key);
            while (order.Count > MaxCached)
            {
                Sprite? old;
                if (cache.TryGetValue(order[0], out old) && old != null)
                {
                    // 出ている図は使ったときに新しい扱いにしているので、捨てるのは見えていないもの
                    UnityEngine.Object.Destroy(old.texture);
                    UnityEngine.Object.Destroy(old);
                }
                cache.Remove(order[0]);
                order.RemoveAt(0);
            }
            return found;
        }

        private static Sprite? Render(string replayPath, bool meanOnly, bool? onlyForehand)
        {
            try
            {
                if (!File.Exists(replayPath)) return null;

                var replay = ReplayReader.Read(replayPath);
                byte[] png = SwingDiagram.RenderBoth(replay, new SwingDiagram.Options
                {
                    Width = PaneWidth * 2 + 4,
                    Height = Height,
                    MeanOnly = meanOnly,
                    OnlyForehand = onlyForehand,
                });

                return SpriteFactory.FromPng(png);
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not draw the diagram: " + e);
                return null;
            }
        }
    }
}
