using System;
using System.Collections.Generic;
using System.IO;
using JumpDrill.Output;
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
        /// 縦横比は Leaderboard.bsml の図の枠（86 × 23）に合わせる（全体の幅 904 × 23 / 86 ≒ 242）。
        /// 合っていないと図が横に伸びて見える。
        /// </summary>
        private const int Height = 242;

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
        /// <param name="hitbox">ノーツを当たり判定の大きさで描くか。</param>
        internal Sprite? For(string? replayPath, bool meanOnly, bool? onlyForehand, bool hitbox)
        {
            if (string.IsNullOrEmpty(replayPath)) return null;

            // 同じ名前で書き直されたリプレイは別物として描き直す
            long written;
            try { written = System.IO.File.GetLastWriteTimeUtc(replayPath).Ticks; }
            catch (Exception) { written = 0; }

            string key = replayPath + "|" + written + (meanOnly ? "|mean" : "|all")
                + (onlyForehand == true ? "|fore" : onlyForehand == false ? "|back" : "|both")
                + (hitbox ? "|hitbox" : "|visual");

            Sprite? found;
            if (cache.TryGetValue(key, out found))
            {
                // 使ったものは新しい扱いにする（出ている図を先に捨てないように）
                order.Remove(key);
                order.Add(key);
                return found;
            }

            float? njs;
            found = Render(replayPath!, meanOnly, onlyForehand, hitbox, out njs);
            if (speeds.Count > 256) speeds.Clear();   // 記録を見るたびに増えるので、増えすぎたら忘れる（描き直すとまた覚える）
            speeds[SpeedKey(replayPath!)] = njs;
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

        /// <summary>図を描いたときに使った NJS（譜面から読んだもの）。描いていないか、譜面が見つからなければ null。</summary>
        /// <remarks>図と同じ値を出すため、また選ぶたびに譜面のファイルを読み直さないために、描いたときの値を覚えておく。</remarks>
        internal float? NoteJumpSpeedOf(string? replayPath)
        {
            float? njs;
            return !string.IsNullOrEmpty(replayPath) && speeds.TryGetValue(SpeedKey(replayPath!), out njs) ? njs : null;
        }

        private readonly Dictionary<string, float?> speeds = new Dictionary<string, float?>(StringComparer.OrdinalIgnoreCase);

        private static string SpeedKey(string replayPath)
        {
            long written;
            try { written = File.GetLastWriteTimeUtc(replayPath).Ticks; }
            catch (Exception) { written = 0; }
            return replayPath + "|" + written;
        }

        private static Sprite? Render(string replayPath, bool meanOnly, bool? onlyForehand, bool hitbox, out float? njs)
        {
            njs = null;
            try
            {
                if (!File.Exists(replayPath)) return null;

                var replay = ReplayReader.Read(replayPath);
                njs = NoteFlight.NoteJumpSpeedOf(replay, name => DrillLibrary.FindInInstall(name, InstallPaths.Root));
                byte[] png = SwingDiagram.RenderBoth(replay, new SwingDiagram.Options
                {
                    Width = PaneWidth * 2 + 4,
                    Height = Height,
                    MeanOnly = meanOnly,
                    OnlyForehand = onlyForehand,
                    Hitbox = hitbox,
                    NoteJumpSpeed = njs,
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
