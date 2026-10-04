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
        /// </summary>
        private const int Height = 380;

        /// <summary>1枚ぶんの幅。正面と横を並べるので、全体はこの倍に仕切りを足したもの。</summary>
        private const int PaneWidth = 450;

        private readonly Dictionary<string, Sprite?> cache =
            new Dictionary<string, Sprite?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// そのリプレイの図（正面と横を並べたもの）。描けなければ null。
        /// </summary>
        /// <param name="meanOnly">平均軌道だけを描くか。1本1本の束を出さない。</param>
        internal Sprite? For(string? replayPath, bool meanOnly)
        {
            if (string.IsNullOrEmpty(replayPath)) return null;

            // 同じ名前で書き直されたリプレイは別物として描き直す
            long written;
            try { written = System.IO.File.GetLastWriteTimeUtc(replayPath).Ticks; }
            catch (Exception) { written = 0; }

            string key = replayPath + "|" + written + (meanOnly ? "|mean" : "|all");

            Sprite? found;
            if (cache.TryGetValue(key, out found)) return found;

            found = Render(replayPath!, meanOnly);
            cache[key] = found;
            return found;
        }

        private static Sprite? Render(string replayPath, bool meanOnly)
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
