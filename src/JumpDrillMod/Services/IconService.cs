using System;
using System.Collections.Generic;
using System.Linq;
using JumpDrill.Model;
using JumpDrill.Output;
using UnityEngine;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// 遷移を 4x3 の格子に描いた小さな絵。GUI のメダル画面の行頭にあるものと同じ。
    /// </summary>
    /// <remarks>
    /// 描くのは JumpDrill.Core の <c>SequenceIcon</c>。同じ遷移は同じ絵なので、遷移の記法を鍵にして持っておく。
    /// </remarks>
    internal class IconService
    {
        /// <summary>格子1マスの画素数。表示より大きめに描いて縮小させる（等倍だとざらつく）。</summary>
        private const int Cell = 16;

        private readonly Dictionary<string, Sprite?> cache =
            new Dictionary<string, Sprite?>(StringComparer.Ordinal);

        /// <summary>その遷移の絵。描けなければ null。</summary>
        internal Sprite? For(IReadOnlyList<HandSequence>? sequences)
        {
            if (sequences == null || sequences.Count == 0) return null;

            string key = string.Join(" ", sequences.Select(s => s.ToString()).ToArray());

            Sprite? found;
            if (cache.TryGetValue(key, out found)) return found;

            try
            {
                found = SpriteFactory.FromPng(SequenceIcon.Render(sequences, Cell));
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not draw the icon: " + e);
                found = null;
            }

            cache[key] = found;
            return found;
        }
    }
}
