using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JumpDrill.Replays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Note = JumpDrill.Replays.SwingScene.Note;
using Swing = JumpDrill.Replays.SwingScene.Swing;
using UVector3 = UnityEngine.Vector3;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// 振りを VR の空間に実物大で置く（GUI の 3D 表示と同じ中身。JumpDrill.Core の <c>SwingScene</c>）。
    /// </summary>
    /// <remarks>
    /// 平らな画像では奥行きが潰れて、刃がノーツのどこをどの深さで通ったかが読めない。
    /// VR なら空間に置くだけで、歩いたり首を振ったりして横からも上からも見られる。
    ///
    /// 座標はリプレイのまま（本体と同じ、X が右・Y が上・Z が前、m）。プレイしたときの立ち位置に戻れば、振りの中に立つ形になる。
    ///
    /// 素材はどの版にも入っている Unity 標準の <c>Hidden/Internal-Colored</c>（頂点の色をそのまま使い、半透明にできる）。
    /// 描く順は物ごとに Unity が決めるので、半透明が重なる所の前後は GUI の図ほど正確ではない。
    /// </remarks>
    internal class SwingSceneService
    {
        /// <summary>
        /// 空間に置くものは1つだけ。リーダーボードは曲選択の枠と Jump Drill 画面の2か所にあり、
        /// それぞれが持つと、両方が出ているときに振りが2重に置かれる。
        /// </summary>
        internal static readonly SwingSceneService Shared = new SwingSceneService();

        // 図と同じ色（JumpDrill.Core の SwingPalette）
        private static readonly Color White = Rgb(SwingPalette.White);

        /// <summary>先端の線の太さ (m)。</summary>
        private const float TipWidth = 0.012f;

        /// <summary>手元の線の太さ (m)。</summary>
        private const float HiltWidth = 0.008f;

        /// <summary>扇の白さ（手の色をどれだけ白に寄せるか）と濃さ。GUI と同じ。</summary>
        private const float BandWhiteness = 0.9f;
        private const float BandAlpha = 70f / 255f;

        /// <summary>ノーツの面の濃さ。GUI と同じ。</summary>
        private const float NoteAlpha = 210f / 255f;

        /// <summary>刃がノーツの中を通った所（白）の濃さ。GUI と同じ。</summary>
        private const float InsideAlpha = 70f / 255f;

        /// <summary>刃の細い線を何本に1本描くか。</summary>
        private const int RibEvery = 3;

        private GameObject? root;
        private string? shownKey;
        private Material? material;

        /// <summary>置いているものの Mesh。GameObject を消しても Mesh は残るので、消すときに一緒に破棄する。</summary>
        private readonly List<Mesh> meshes = new List<Mesh>();

        /// <summary>中身を作り直した結果を持っておく。同じリプレイを選び直すたびに読み直すと待たされる。</summary>
        private readonly Dictionary<string, SwingScene?> scenes = new Dictionary<string, SwingScene?>(StringComparer.OrdinalIgnoreCase);

        /// <summary><see cref="scenes"/> に入れた順。古いものから捨てる。</summary>
        private readonly List<string> sceneOrder = new List<string>();

        /// <summary>中身を持っておくリプレイの数。</summary>
        private const int MaxScenes = 8;

        private SwingSceneService()
        {
            // 曲を始めるなどで場面が変わったら消す。リーダーボードが隠れる知らせが来ない場合にも、プレイ中に振りが残らないように
            SceneManager.activeSceneChanged += (from, to) => Hide();
        }

        /// <summary>
        /// そのリプレイの振りを空間に出す。前に出していたものは消す。
        /// </summary>
        /// <param name="onlyForehand">フォアだけ（true）かバックだけ（false）。両方なら null。</param>
        internal void Show(string? replayPath, bool? onlyForehand)
        {
            if (string.IsNullOrEmpty(replayPath))
            {
                Hide();
                return;
            }

            // 同じ名前で書き直されたリプレイは別物として置き直す
            string key = SceneKey(replayPath!) + (onlyForehand == true ? "|fore" : onlyForehand == false ? "|back" : "|both");
            if (root != null && key == shownKey) return;

            Hide();

            var scene = SceneFor(replayPath!);
            if (scene == null || scene.Means.Count == 0) return;

            try
            {
                root = new GameObject("JumpDrillSwingScene");
                Build(root.transform, scene, onlyForehand);
                shownKey = key;
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not place the swings in the room: " + e);
                Hide();
            }
        }

        /// <summary>出していたものを消す。</summary>
        internal void Hide()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            foreach (var mesh in meshes) UnityEngine.Object.Destroy(mesh);
            meshes.Clear();
            root = null;
            shownKey = null;
        }

        private static string SceneKey(string replayPath)
        {
            long written;
            try { written = File.GetLastWriteTimeUtc(replayPath).Ticks; }
            catch (Exception) { written = 0; }
            return replayPath + "|" + written;
        }

        private SwingScene? SceneFor(string replayPath)
        {
            string key = SceneKey(replayPath);
            SwingScene? scene;
            if (scenes.TryGetValue(key, out scene)) return scene;

            try
            {
                if (!File.Exists(replayPath)) return null;
                var original = ReplayReader.Read(replayPath);
                scene = SwingScene.Build(TowerReplay.Collapse(original), original);
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not read the replay for the room view: " + e);
                scene = null;
            }
            scenes[key] = scene;
            sceneOrder.Add(key);
            while (sceneOrder.Count > MaxScenes)
            {
                scenes.Remove(sceneOrder[0]);
                sceneOrder.RemoveAt(0);
            }
            return scene;
        }

        // ───────── 置く ─────────

        private void Build(Transform parent, SwingScene scene, bool? onlyForehand)
        {
            Func<bool, bool> shown = fore => !onlyForehand.HasValue || onlyForehand.Value == fore;

            // 左右・向き指定なしのノーツは切る向きで決まらないので、どちらでも出す
            Func<Note, bool> noteShown = note => SwingSides.NoteShown(note.CutDirection, onlyForehand);

            foreach (var note in scene.Notes)
                if (noteShown(note)) AddNote(parent, note);

            foreach (var mean in scene.Means)
            {
                Func<int, Color?> colorAt = i =>
                {
                    bool fore = mean.SegmentForehand(i);
                    return shown(fore) ? SideColor(mean.SaberType, fore) : (Color?)null;
                };

                AddBand(parent, mean, colorAt);
                AddInsideNotes(parent, mean, scene.Notes.Where(n => n.ColorType == mean.SaberType && noteShown(n)), colorAt);
                AddPath(parent, mean.Hilts, i => Fade(Lighten(colorAt(i), 0.2f), 200f / 255f), HiltWidth, marks: false);
                AddPath(parent, mean.Tips, i => Lighten(colorAt(i), 0.4f), TipWidth, marks: true);

                // 切った瞬間の刃と先端の白丸（振りの終わり＝次に切るノーツ）
                if (shown(mean.ToForehand)) AddCut(parent, mean);
            }
        }

        /// <summary>折れ線を、出す色が続く所ごとに1本の線にする。向きの印（「く」）を真ん中に1つ。</summary>
        private void AddPath(Transform parent, JumpDrill.Replays.Vector3[] path, Func<int, Color?> colorAt, float width, bool marks)
        {
            int start = 0;
            for (int i = 1; i < path.Length; i++)
            {
                var color = colorAt(i);
                bool last = i == path.Length - 1;
                if (!last && Same(colorAt(i + 1), color)) continue;

                if (color.HasValue)
                {
                    var points = new List<UVector3>();
                    for (int k = start; k <= i; k++) points.Add(ToUnity(path[k]));
                    AddLine(parent, points, color.Value, width);
                }
                start = i;
            }

            if (!marks) return;

            int mid = path.Length / 2;
            var markColor = colorAt(mid);
            if (mid < 1 || !markColor.HasValue) return;

            var tip = ToUnity(path[mid]);
            var direction = (tip - ToUnity(path[mid - 1])).normalized;
            var side = UVector3.Cross(direction, UVector3.up);
            if (side.sqrMagnitude < 1e-4f) side = UVector3.Cross(direction, UVector3.right);
            side.Normalize();
            const float Arm = 0.05f;
            AddLine(parent, new List<UVector3> { tip - direction * Arm + side * Arm * 0.7f, tip, tip - direction * Arm - side * Arm * 0.7f },
                    markColor.Value, width);
        }

        private static bool Same(Color? a, Color? b)
        {
            if (!a.HasValue || !b.HasValue) return a.HasValue == b.HasValue;
            return a.Value == b.Value;
        }

        private void AddLine(Transform parent, List<UVector3> points, Color color, float width)
        {
            var go = new GameObject("Line");
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = Material;
            line.positionCount = points.Count;
            line.SetPositions(points.ToArray());
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
        }

        /// <summary>刃が通った面。白っぽい薄い帯と、数本おきの刃そのもの。</summary>
        private void AddBand(Transform parent, Swing mean, Func<int, Color?> colorAt)
        {
            var vertices = new List<UVector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();

            for (int i = 1; i < mean.Tips.Length; i++)
            {
                var color = colorAt(i);
                if (!color.HasValue) continue;
                var fill = Fade(Lighten(color, BandWhiteness), BandAlpha)!.Value;

                AddQuad(vertices, colors, triangles, fill,
                    ToUnity(mean.Hilts[i - 1]), ToUnity(mean.Tips[i - 1]), ToUnity(mean.Tips[i]), ToUnity(mean.Hilts[i]));
            }
            AddMesh(parent, "Band", vertices, colors, triangles);

            for (int i = 0; i < mean.Tips.Length; i += RibEvery)
            {
                var color = colorAt(Math.Max(1, i));
                if (!color.HasValue) continue;
                AddLine(parent, new List<UVector3> { ToUnity(mean.Hilts[i]), ToUnity(mean.Tips[i]) },
                        Fade(Lighten(color, 0.5f), 80f / 255f)!.Value, 0.003f);
            }
        }

        /// <summary>扇のうち、ノーツの立方体の中を通った所（刃がノーツの中を通った所）を白で塗る。</summary>
        private void AddInsideNotes(Transform parent, Swing mean, IEnumerable<Note> notes, Func<int, Color?> colorAt)
        {
            var vertices = new List<UVector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            var fill = new Color(White.r, White.g, White.b, InsideAlpha);

            foreach (var note in notes)
                foreach (var piece in SwingScene.InsideNote(mean, note))
                {
                    if (!colorAt(piece.Key).HasValue) continue;

                    // 凸な多角形なので、最初の点から扇形に三角形へ分ける
                    int first = vertices.Count;
                    foreach (var p in piece.Value)
                    {
                        vertices.Add(ToUnity(p));
                        colors.Add(fill);
                    }
                    for (int k = 1; k < piece.Value.Length - 1; k++)
                    {
                        triangles.Add(first);
                        triangles.Add(first + k);
                        triangles.Add(first + k + 1);
                    }
                }
            AddMesh(parent, "InsideNotes", vertices, colors, triangles);
        }

        /// <summary>切った瞬間の刃（振りの終わり）の白い線と、先端の白丸。</summary>
        private void AddCut(Transform parent, Swing mean)
        {
            var hilt = ToUnity(mean.Hilts[mean.Hilts.Length - 1]);
            var tip = ToUnity(mean.Tips[mean.Tips.Length - 1]);
            AddLine(parent, new List<UVector3> { hilt, tip }, new Color(White.r, White.g, White.b, 230f / 255f), 0.006f);
            AddDot(parent, tip, 0.015f, White);
        }

        /// <summary>
        /// ノーツの立方体。面ごとに向きで濃淡を付ける（上から光が当たっている形）。
        /// プレイヤー側の面に切る向きの三角、中心に白い点。向きは矢印の 8 方向だけで回す（GUI と同じ）。
        /// </summary>
        private void AddNote(Transform parent, Note note)
        {
            var color = SideColor(note.ColorType, note.Forehand);
            var c = note.Corners().Select(ToUnity).ToArray();

            int[][] faces =
            {
                new[] { 0, 1, 3, 2 }, // 手前（プレイヤー側）
                new[] { 4, 5, 7, 6 }, // 奥
                new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 },
                new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 },
            };

            var center = ToUnity(note.Center);
            var light = new UVector3(0.3f, 0.8f, -0.5f).normalized;
            var vertices = new List<UVector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            foreach (var face in faces)
            {
                var faceCenter = (c[face[0]] + c[face[1]] + c[face[2]] + c[face[3]]) / 4f;
                float shade = 0.55f + 0.45f * Mathf.Abs(UVector3.Dot((faceCenter - center).normalized, light));
                var shaded = new Color(color.r * shade, color.g * shade, color.b * shade, NoteAlpha);
                AddQuad(vertices, colors, triangles, shaded, c[face[0]], c[face[1]], c[face[2]], c[face[3]]);
            }
            AddMesh(parent, "Note", vertices, colors, triangles);

            // 三角は切る向きと反対側に置き、先が切る向きを指す。手前の面から少しだけ浮かせる
            if (!double.IsNaN(SwingSides.ArrowAngle(note.CutDirection)))
            {
                float h = SwingScene.NoteSize / 2;
                var tri = new[]
                {
                    ToUnity(note.At(0f, -h * 0.24f, -h - 0.002f)),
                    ToUnity(note.At(-h * 0.68f, -h * 0.8f, -h - 0.002f)),
                    ToUnity(note.At(h * 0.68f, -h * 0.8f, -h - 0.002f)),
                };
                var white = new Color(White.r, White.g, White.b, 200f / 255f);
                AddMesh(parent, "Arrow", tri.ToList(), new List<Color> { white, white, white }, new List<int> { 0, 1, 2 });
            }

            AddDot(parent, center, 0.02f, White);
        }

        private static void AddQuad(List<UVector3> vertices, List<Color> colors, List<int> triangles, Color color,
                                    UVector3 a, UVector3 b, UVector3 c, UVector3 d)
        {
            int first = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int k = 0; k < 4; k++) colors.Add(color);
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }

        private void AddMesh(Transform parent, string name, List<UVector3> vertices, List<Color> colors, List<int> triangles)
        {
            if (triangles.Count == 0) return;

            var mesh = new Mesh();
            meshes.Add(mesh);
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>小さな球（白丸）。8 方向の 2 段の多面体で足りる大きさ。</summary>
        private void AddDot(Transform parent, UVector3 center, float radius, Color color)
        {
            var vertices = new List<UVector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            const int Around = 10, Rings = 6;

            for (int r = 0; r <= Rings; r++)
            {
                float theta = Mathf.PI * r / Rings;
                for (int a = 0; a <= Around; a++)
                {
                    float phi = 2f * Mathf.PI * a / Around;
                    vertices.Add(center + radius * new UVector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi)));
                    colors.Add(color);
                }
            }
            for (int r = 0; r < Rings; r++)
                for (int a = 0; a < Around; a++)
                {
                    int i0 = r * (Around + 1) + a, i1 = i0 + 1, i2 = i0 + Around + 1, i3 = i2 + 1;
                    triangles.Add(i0); triangles.Add(i2); triangles.Add(i1);
                    triangles.Add(i1); triangles.Add(i2); triangles.Add(i3);
                }
            AddMesh(parent, "Dot", vertices, colors, triangles);
        }

        /// <summary>
        /// 頂点の色をそのまま使う半透明の素材。<c>Hidden/Internal-Colored</c> は Unity が必ず持っているシェーダーで、
        /// 混ぜ方・裏面・奥行きの書き込みを外から変えられる。裏面も描き、奥行きは書かない（半透明どうしが消し合わないように）。
        /// </summary>
        private Material Material
        {
            get
            {
                if (material != null) return material;

                material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_Cull", (int)CullMode.Off);
                material.SetInt("_ZWrite", 0);
                material.renderQueue = (int)RenderQueue.Transparent;
                return material;
            }
        }

        private static Color SideColor(int hand, bool forehand)
        {
            return Rgb(SwingPalette.Of(hand, forehand));
        }

        /// <summary>白へ寄せる（GUI の ControlPaint.Light と同じ効き方）。</summary>
        private static Color? Lighten(Color? color, float amount)
        {
            if (!color.HasValue) return null;
            var c = color.Value;
            return new Color(c.r + (1f - c.r) * amount, c.g + (1f - c.g) * amount, c.b + (1f - c.b) * amount, c.a);
        }

        private static Color? Fade(Color? color, float alpha)
        {
            if (!color.HasValue) return null;
            var c = color.Value;
            return new Color(c.r, c.g, c.b, alpha);
        }

        private static Color Rgb(byte[] rgb) { return new Color(rgb[0] / 255f, rgb[1] / 255f, rgb[2] / 255f, 1f); }

        private static UVector3 ToUnity(JumpDrill.Replays.Vector3 p) { return new UVector3(p.X, p.Y, p.Z); }
    }
}
