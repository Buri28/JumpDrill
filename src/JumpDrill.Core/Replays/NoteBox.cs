using System;
using System.Collections.Generic;
using System.Linq;

namespace JumpDrill.Replays
{
    /// <summary>
    /// 図に描くノーツの箱。見た目の立方体か、良い切りの当たり判定か。
    /// 寸法はノーツの模型の軸で持つ（幅＝矢印と直角、高さ＝矢印の向き、奥行き＝プレイヤーから離れる向き）。
    /// </summary>
    /// <remarks>
    /// 本体 1.40.8 の <c>NormalGameNote</c> の prefab から読んだ値。
    /// <list type="bullet">
    /// <item>見た目: 立方体のメッシュ（CubeNoteSmoothHD）は一辺 0.495 m、中心はノーツの中心。</item>
    /// <item>当たり判定: 良い切りを取る箱（BigCuttable の BoxCollider）は 幅 0.8 × 高さ 0.5 × 奥行き 1.0 m で、
    /// 中心がプレイヤー側へ 0.25 m 寄っている（手前は見た目より 0.5 m 先まで届く）。
    /// 刃が1フレームで通った三角形がこの箱に触れれば切った判定になる（<c>NoteCutter.Cut</c>）。
    /// 向きを問わず当たる小さい箱（SmallCuttable、一辺 0.35 m）はバッドカット用なので描かない。</item>
    /// </list>
    /// </remarks>
    public struct NoteBox
    {
        /// <summary>幅の半分 (m)。矢印と直角（模型の X）。</summary>
        public float HalfWidth;

        /// <summary>高さの半分 (m)。矢印の向き（模型の Y）。</summary>
        public float HalfHeight;

        /// <summary>奥行きの半分 (m)。模型の Z。</summary>
        public float HalfDepth;

        /// <summary>箱の中心の、ノーツの中心からの奥へのずれ (m)。負ならプレイヤー側。</summary>
        public float Offset;

        /// <summary>見た目の立方体。</summary>
        public static readonly NoteBox Visual = new NoteBox { HalfWidth = 0.25f, HalfHeight = 0.25f, HalfDepth = 0.25f };

        /// <summary>良い切りの当たり判定。</summary>
        public static readonly NoteBox Hitbox = new NoteBox { HalfWidth = 0.4f, HalfHeight = 0.25f, HalfDepth = 0.5f, Offset = -0.25f };

        /// <summary>プレイヤー側の面の、ノーツの中心からの奥行き（負）。</summary>
        public float Front { get { return Offset - HalfDepth; } }

        /// <summary>
        /// 8 隅。番号の 1・2・4 の桁が幅・高さ・奥行きの正負（0 が負）。
        /// <paramref name="right"/>・<paramref name="up"/>・<paramref name="forward"/> はノーツの面の右・矢印・奥の向き。
        /// </summary>
        public Vector3[] Corners(Vector3 center, Vector3 right, Vector3 up, Vector3 forward)
        {
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                float u = (i & 1) == 0 ? -HalfWidth : HalfWidth;
                float v = (i & 2) == 0 ? -HalfHeight : HalfHeight;
                float w = Offset + ((i & 4) == 0 ? -HalfDepth : HalfDepth);
                corners[i] = new Vector3
                {
                    X = center.X + u * right.X + v * up.X + w * forward.X,
                    Y = center.Y + u * right.Y + v * up.Y + w * forward.Y,
                    Z = center.Z + u * right.Z + v * up.Z + w * forward.Z,
                };
            }
            return corners;
        }

        /// <summary>箱の 12 本の辺（<see cref="Corners"/> の番号の組）。1つの桁だけ違う隅どうし。</summary>
        public static IEnumerable<int[]> Edges()
        {
            for (int i = 0; i < 8; i++)
                for (int bit = 1; bit < 8; bit <<= 1)
                    if ((i & bit) == 0) yield return new[] { i, i | bit };
        }

        /// <summary>
        /// 見る向きから見える辺（見えている面に接する辺）。奥に隠れる辺は返さない。
        /// <paramref name="toViewer"/> は箱から見る人への向き（平行に見る図なら一定）。
        /// </summary>
        public static IEnumerable<int[]> VisibleEdges(Vector3 right, Vector3 up, Vector3 forward, Vector3 toViewer)
        {
            var axes = new[] { right, up, forward };
            foreach (var edge in Edges())
            {
                int along = edge[0] ^ edge[1];
                bool visible = false;
                for (int k = 0; k < 3; k++)
                {
                    int bit = 1 << k;
                    if (bit == along) continue;
                    // この辺が乗る面: 桁 k が立っていれば +軸、でなければ −軸の向きの面
                    float sign = (edge[0] & bit) != 0 ? 1f : -1f;
                    var a = axes[k];
                    if (sign * (a.X * toViewer.X + a.Y * toViewer.Y + a.Z * toViewer.Z) > 1e-4f) visible = true;
                }
                if (visible) yield return edge;
            }
        }

        /// <summary>
        /// 箱の面に貼る切る向きの印（世界座標の多角形）。見える面のうち、矢印の向きを含む面（手前の面と左右の面）に描く。
        /// 手前の面は三角（▽、3D の図と同じく矢印と反対側の半分）、左右の面は軸の付いた矢印（⇩）で、
        /// どれも面の上で矢印の向き（<paramref name="up"/>）を指す。大きさは見た目のノーツ（一辺 0.5 m）に合わせる。
        /// </summary>
        public List<Vector3[]> ArrowDecals(Vector3 center, Vector3 right, Vector3 up, Vector3 forward, Vector3 toViewer)
        {
            const float H = 0.25f;
            var result = new List<Vector3[]>();
            Func<float, float, float, Vector3> at = (u, v, w) => new Vector3
            {
                X = center.X + u * right.X + v * up.X + w * forward.X,
                Y = center.Y + u * right.Y + v * up.Y + w * forward.Y,
                Z = center.Z + u * right.Z + v * up.Z + w * forward.Z,
            };
            Func<Vector3, float> facing = n => n.X * toViewer.X + n.Y * toViewer.Y + n.Z * toViewer.Z;

            // 手前の面（法線は −奥）
            float front = Front - 0.002f;
            if (facing(new Vector3 { X = -forward.X, Y = -forward.Y, Z = -forward.Z }) > 1e-4f)
                result.Add(new[] { at(0f, -H * 0.24f, front), at(-H * 0.68f, -H * 0.8f, front), at(H * 0.68f, -H * 0.8f, front) });

            // 左右の面（法線は ±右）。面の上の横は奥行き
            foreach (float side in new[] { 1f, -1f })
            {
                if (facing(new Vector3 { X = side * right.X, Y = side * right.Y, Z = side * right.Z }) <= 1e-4f) continue;
                // 横の面は手前の面（▽）と見分けられるよう、軸の付いた矢印（⇩）にする。面の真ん中に、矢印の向きへ
                float u = side * (HalfWidth + 0.002f);
                float[][] shape =
                {
                    new[] { -0.4f, -0.11f }, new[] { 0.03f, -0.11f }, new[] { 0.03f, -0.28f }, new[] { 0.45f, 0f },
                    new[] { 0.03f, 0.28f }, new[] { 0.03f, 0.11f }, new[] { -0.4f, 0.11f },
                };
                float middle = Offset;
                result.Add(shape.Select(p => at(u, p[0] * H, middle + p[1] * H)).ToArray());
            }
            return result;
        }

        /// <summary>
        /// 見える面（<see cref="Corners"/> の番号4つ、外向きの法線）。面ごとに明るさを変えて塗ると、
        /// 輪郭と辺だけのときのように立方体が裏返って見える（手前と奥を取り違える）ことが減る。
        /// </summary>
        public static List<KeyValuePair<int[], Vector3>> VisibleFaces(Vector3 right, Vector3 up, Vector3 forward, Vector3 toViewer)
        {
            var axes = new[] { right, up, forward };
            var result = new List<KeyValuePair<int[], Vector3>>();
            for (int k = 0; k < 3; k++)
            {
                int bit = 1 << k, a = 1 << ((k + 1) % 3), b = 1 << ((k + 2) % 3);
                foreach (float sign in new[] { -1f, 1f })
                {
                    var n = new Vector3 { X = sign * axes[k].X, Y = sign * axes[k].Y, Z = sign * axes[k].Z };
                    if (n.X * toViewer.X + n.Y * toViewer.Y + n.Z * toViewer.Z <= 1e-4f) continue;
                    int on = sign > 0 ? bit : 0;
                    result.Add(new KeyValuePair<int[], Vector3>(new[] { on, on | a, on | a | b, on | b }, n));
                }
            }
            return result;
        }

        /// <summary>
        /// 面の明るさ (0〜1)。上を向いた面ほど明るく、見る人の方を向いていない面ほど暗い（3D の図の陰と同じ考え方）。
        /// </summary>
        public static float FaceShade(Vector3 normal, Vector3 toViewer)
        {
            float facing = normal.X * toViewer.X + normal.Y * toViewer.Y + normal.Z * toViewer.Z;
            float lit = Math.Max(0f, normal.Y);
            return Math.Max(0f, Math.Min(1f, 0.35f + 0.35f * lit + 0.3f * facing));
        }

        /// <summary>
        /// 平面（<paramref name="planePoint"/> を通り <paramref name="planeNormal"/> に直角）で箱を切った断面（凸な多角形、周りの順）。
        /// 交わらなければ空。
        /// </summary>
        public List<Vector3> PlaneSection(Vector3 center, Vector3 right, Vector3 up, Vector3 forward, Vector3 planePoint, Vector3 planeNormal)
        {
            var corners = Corners(center, right, up, forward);
            Func<Vector3, float> side = p => (p.X - planePoint.X) * planeNormal.X + (p.Y - planePoint.Y) * planeNormal.Y + (p.Z - planePoint.Z) * planeNormal.Z;
            var points = new List<Vector3>();
            foreach (var edge in Edges())
            {
                var a = corners[edge[0]];
                var b = corners[edge[1]];
                float da = side(a), db = side(b);
                if (Math.Abs(da) < 1e-6f) points.Add(a);
                if ((da < 0) != (db < 0) && Math.Abs(da - db) > 1e-9f)
                {
                    float t = da / (da - db);
                    points.Add(new Vector3 { X = a.X + (b.X - a.X) * t, Y = a.Y + (b.Y - a.Y) * t, Z = a.Z + (b.Z - a.Z) * t });
                }
            }
            if (points.Count < 3) return new List<Vector3>();

            // 平面の上の2軸で角度を出して、周りの順に並べる
            var mid = new Vector3 { X = points.Average(p => p.X), Y = points.Average(p => p.Y), Z = points.Average(p => p.Z) };
            var axisA = NotePose.Normalize(NotePose.Subtract(points[0], mid));
            var axisB = NotePose.Cross(planeNormal, axisA);
            return points.OrderBy(p =>
            {
                var d = NotePose.Subtract(p, mid);
                return Math.Atan2(d.X * axisB.X + d.Y * axisB.Y + d.Z * axisB.Z, d.X * axisA.X + d.Y * axisA.Y + d.Z * axisA.Z);
            }).ToList();
        }

        /// <summary>平面上の点の凸包（反時計回り）。箱を正面・横から見た輪郭に使う。</summary>
        public static List<double[]> Hull(IEnumerable<double[]> points)
        {
            var sorted = points.OrderBy(p => p[0]).ThenBy(p => p[1]).ToList();
            if (sorted.Count < 3) return sorted;

            Func<double[], double[], double[], double> cross = (o, a, b) =>
                (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);

            var hull = new List<double[]>();
            foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
            {
                int start = hull.Count;
                foreach (var p in pass)
                {
                    while (hull.Count >= start + 2 && cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }
    }
}
