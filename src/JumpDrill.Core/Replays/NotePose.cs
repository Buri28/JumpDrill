using System;

namespace JumpDrill.Replays
{
    /// <summary>
    /// ノーツの向き。本体の <c>NoteJump::ManualUpdate</c> と同じ出し方をする。
    ///
    /// ノーツは飛びながら<b>プレイヤーの頭の方を向く</b>:
    /// <code>
    /// head.y = Mathf.Lerp(head.y, note.y, 0.8)
    /// forward = (note - head).normalized
    /// rotation = Lerp(baseRotation, LookRotation(forward, up), t * 2)
    /// </code>
    /// 重みが <c>t * 2</c> なので、<b>飛行の半分を過ぎた時点で向き切る</b>。
    /// 叩く位置ではもう 100% なので、ここでは LookRotation をそのまま使う。
    ///
    /// 高さを 8 割ノーツ側に寄せているので、回るのはほぼ横方向だけ。
    /// 外側の列ほど効く。
    /// </summary>
    public static class NotePose
    {
        /// <summary>見る先の高さをノーツ側へ寄せる割合。</summary>
        public const float LookAtHeightBlend = 0.8f;

        /// <summary>
        /// ノーツの面の向きを出す。
        /// </summary>
        /// <param name="center">ノーツの中心（世界座標）。</param>
        /// <param name="head">頭の位置（世界座標）。</param>
        /// <param name="baseUp">回る前の「上」＝矢印の向き（世界座標、z=0）。</param>
        public static void LookAtPlayer(Vector3 center, Vector3 head, Vector3 baseUp,
                                        out Vector3 right, out Vector3 up)
        {
            var target = new Vector3
            {
                X = head.X,
                Y = head.Y + (center.Y - head.Y) * LookAtHeightBlend,
                Z = head.Z,
            };

            var forward = Normalize(Subtract(center, target));

            // Unity は左手系。LookRotation(forward, up) は right = up × forward。
            right = Normalize(Cross(baseUp, forward));
            up = Cross(forward, right);
        }

        public static Vector3 Cross(Vector3 a, Vector3 b)
        {
            return new Vector3
            {
                X = a.Y * b.Z - a.Z * b.Y,
                Y = a.Z * b.X - a.X * b.Z,
                Z = a.X * b.Y - a.Y * b.X,
            };
        }

        public static Vector3 Subtract(Vector3 a, Vector3 b)
        {
            return new Vector3 { X = a.X - b.X, Y = a.Y - b.Y, Z = a.Z - b.Z };
        }

        public static Vector3 Normalize(Vector3 v)
        {
            double length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (length < 1e-9) return new Vector3 { X = 0, Y = 0, Z = 1 };

            return new Vector3
            {
                X = (float)(v.X / length),
                Y = (float)(v.Y / length),
                Z = (float)(v.Z / length),
            };
        }
    }
}
