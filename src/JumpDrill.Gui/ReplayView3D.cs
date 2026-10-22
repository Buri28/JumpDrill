using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using JumpDrill.Replays;
using Note = JumpDrill.Replays.SwingScene.Note;
using Swing = JumpDrill.Replays.SwingScene.Swing;

namespace JumpDrill.Gui
{
    /// <summary>
    /// 振りを 3D で見る図。視点をマウスで変えられる。
    ///
    /// 正面図と横の図は奥行きか左右のどちらかを潰すので、手元・刃・先端・ノーツの前後が読めない。
    /// ここでは刃（手元から先端まで）とノーツを同じ空間に置き、回して確かめられるようにする。
    /// </summary>
    /// <remarks>
    /// 左ドラッグで回転、ホイールで拡大・縮小、右ドラッグで移動、ダブルクリックで最初の向きに戻す。
    /// 座標は本体と同じ（X が右、Y が上、Z が前）。描く順は奥から（画家のやり方）。
    /// </remarks>
    public sealed class ReplayView3D : Control
    {
        private static readonly Color Background = Color.FromArgb(0x14, 0x16, 0x1A);
        private static readonly Color FloorColor = Color.FromArgb(0x2A, 0x2E, 0x36);

        private static readonly Color White = Color.FromArgb(0xF2, 0xF5, 0xFA);

        /// <summary>ノーツの一辺 (m)。</summary>
        private const float NoteSize = 0.5f;

        /// <summary>刃の細い線を何本に1本描くか（1本の振りの点の数に対して）。</summary>
        private const int RibEvery = 3;

        /// <summary>描く中身（Core の <see cref="SwingScene"/>。MOD の VR 表示と同じもの）。</summary>
        private SwingScene _scene = new SwingScene();

        private List<Swing> _swings { get { return _scene.Swings; } }
        private List<Swing> _means { get { return _scene.Means; } }
        private List<Note> _notes { get { return _scene.Notes; } }
        private Vector3 _head { get { return _scene.Head; } }
        private bool _hasHead { get { return _scene.HasHead; } }

        public ReplayView3D()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            ResetCamera();
        }

        /// <summary>片手だけ出すとき 0（左）か 1（右）。両手なら null。</summary>
        public int? OnlyHand { get; set; }

        /// <summary>平均だけを描くか。</summary>
        public bool ShowMeanOnly { get; set; } = true;

        /// <summary>
        /// フォアだけ（true）かバックだけ（false）を出す。両方なら null。
        /// タワー・スタックでは行きと帰りの扇が同じ所で重なって読めないので、片方ずつ見られるようにする。
        /// 振りは区間ごとに、刃が動いている向きで分ける（色と同じ決め方）。
        /// </summary>
        public bool? OnlyForehand { get; set; }

        /// <summary>ノーツを良い切りの当たり判定の大きさで描くか（<see cref="NoteBox.Hitbox"/>）。false なら見た目の大きさ。</summary>
        public bool Hitbox { get; set; }

        private NoteBox Box { get { return Hitbox ? NoteBox.Hitbox : NoteBox.Visual; } }

        private bool ShownSide(bool forehand) { return !OnlyForehand.HasValue || OnlyForehand.Value == forehand; }

        /// <param name="replay">軌道に使うリプレイ（タワー・スタックは端の点だけにまとめたもの）。</param>
        /// <param name="notesFrom">ノーツを置くリプレイ（まとめる前）。null なら <paramref name="replay"/>。</param>
        /// <param name="noteJumpSpeed">譜面の NJS。分かればノーツの向きを本体と同じ計算で出す。</param>
        public void Load(Replay replay, Replay notesFrom = null, float? noteJumpSpeed = null)
        {
            _scene = SwingScene.Build(replay, notesFrom, noteJumpSpeed: noteJumpSpeed);

            FitTarget();
            Invalidate();
        }

        // ───────── 視点 ─────────

        /// <summary>見ている中心（世界座標）。</summary>
        private Vector3 _target;

        /// <summary>左右の回転（度）。0 でプレイヤーの後ろから前を見る。</summary>
        private float _yaw;

        /// <summary>上下の回転（度）。正で上から見下ろす。</summary>
        private float _pitch;

        /// <summary>視点から中心までの距離 (m)。</summary>
        private float _distance;

        /// <summary>見る範囲の広さ（縦の画角、度）。</summary>
        private const float FieldOfView = 40f;

        /// <summary>最初の向き。正面図と同じくプレイヤーの後ろから前を見て、床が見える程度に少しだけ上から。</summary>
        private const float DefaultYaw = 0f;
        private const float DefaultPitch = 10f;
        private const float DefaultDistance = 3.6f;

        private void ResetCamera()
        {
            _yaw = DefaultYaw;
            _pitch = DefaultPitch;
            _distance = DefaultDistance;
        }

        /// <summary>中心を、描くもの全部の真ん中に置く。</summary>
        private void FitTarget()
        {
            var points = _means.SelectMany(m => m.Tips).Concat(_notes.Select(n => n.Center)).ToList();
            if (points.Count == 0) { _target = new Vector3 { X = 0f, Y = 1.2f, Z = 0.8f }; return; }

            _target = new Vector3
            {
                X = (points.Min(p => p.X) + points.Max(p => p.X)) / 2f,
                Y = (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2f,
                Z = (points.Min(p => p.Z) + points.Max(p => p.Z)) / 2f,
            };

            // 全部が画角に収まる距離。いちばん遠い点（とノーツの大きさ）を半径の球として測る。
            // ヒットボックスでも見た目のノーツで測る（切り替えで拡大率が変わると見比べられない）
            var target = _target;
            float radius = points.Max(p => (float)Vector3.Distance(p, target)) + NoteSize / 2;
            _distance = Math.Max(1.5f, radius / (float)Math.Sin(FieldOfView * Math.PI / 360.0) * 1.05f);
        }

        /// <summary>視点の向き。前・右・上（どれも長さ 1）。</summary>
        private void Basis(out Vector3 forward, out Vector3 right, out Vector3 up)
        {
            double yaw = _yaw * Math.PI / 180.0, pitch = _pitch * Math.PI / 180.0;
            forward = new Vector3
            {
                X = (float)(Math.Sin(yaw) * Math.Cos(pitch)),
                Y = (float)(-Math.Sin(pitch)),
                Z = (float)(Math.Cos(yaw) * Math.Cos(pitch)),
            };
            right = new Vector3 { X = (float)Math.Cos(yaw), Y = 0f, Z = (float)(-Math.Sin(yaw)) };
            up = Cross(forward, right);
        }

        private static Vector3 Cross(Vector3 a, Vector3 b)
        {
            return new Vector3 { X = a.Y * b.Z - a.Z * b.Y, Y = a.Z * b.X - a.X * b.Z, Z = a.X * b.Y - a.Y * b.X };
        }

        private static float Dot(Vector3 a, Vector3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }

        private static Vector3 Normalize(Vector3 v)
        {
            float length = (float)Math.Sqrt(Dot(v, v));
            return length < 1e-6f ? v : new Vector3 { X = v.X / length, Y = v.Y / length, Z = v.Z / length };
        }

        /// <summary>ノーツの面の濃さ (0-255)。後ろの軌跡がうっすら見える程度。</summary>
        private const int NoteOpacity = 210;

        /// <summary>色の明るさを <paramref name="factor"/> 倍にする（不透明）。</summary>
        private static Color Shade(Color color, float factor)
        {
            Func<int, int> f = c => Math.Max(0, Math.Min(255, (int)(c * factor)));
            return Color.FromArgb(255, f(color.R), f(color.G), f(color.B));
        }

        /// <summary>この描画の間だけ使う、視点の位置と向き。</summary>
        private Vector3 _eye, _forward, _right, _up;
        private float _focal;

        private void SetupFrame()
        {
            Basis(out _forward, out _right, out _up);
            _eye = new Vector3
            {
                X = _target.X - _forward.X * _distance,
                Y = _target.Y - _forward.Y * _distance,
                Z = _target.Z - _forward.Z * _distance,
            };
            _focal = (float)(Math.Min(Width, Height) / 2.0 / Math.Tan(FieldOfView * Math.PI / 360.0));
        }

        /// <summary>視点から見た奥行き (m)。</summary>
        private float DepthOf(Vector3 p)
        {
            return Dot(new Vector3 { X = p.X - _eye.X, Y = p.Y - _eye.Y, Z = p.Z - _eye.Z }, _forward);
        }

        /// <summary>画面の位置。視点より後ろ（またはすぐ前）なら false。</summary>
        private bool ToScreen(Vector3 p, out PointF screen)
        {
            var d = new Vector3 { X = p.X - _eye.X, Y = p.Y - _eye.Y, Z = p.Z - _eye.Z };
            float z = Dot(d, _forward);
            screen = PointF.Empty;
            if (z < 0.05f) return false;
            screen = new PointF(Width / 2f + _focal * Dot(d, _right) / z, Height / 2f - _focal * Dot(d, _up) / z);
            return true;
        }

        // ───────── マウス ─────────

        private Point _lastMouse;
        private MouseButtons _dragging = MouseButtons.None;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            _dragging = e.Button;
            _lastMouse = e.Location;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = MouseButtons.None;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging == MouseButtons.None) return;

            int dx = e.X - _lastMouse.X, dy = e.Y - _lastMouse.Y;
            _lastMouse = e.Location;

            if (_dragging == MouseButtons.Left)
            {
                _yaw += dx * 0.4f;
                _pitch = Math.Max(-85f, Math.Min(85f, _pitch + dy * 0.4f));
            }
            else if (_dragging == MouseButtons.Right)
            {
                // 画面の上で掴んだ所が指についてくるように、中心を視点の右・上へ動かす
                Vector3 forward, right, up;
                Basis(out forward, out right, out up);
                float scale = _distance / Math.Max(1f, (float)(Math.Min(Width, Height) / 2.0 / Math.Tan(FieldOfView * Math.PI / 360.0)));
                _target = new Vector3
                {
                    X = _target.X - right.X * dx * scale + up.X * dy * scale,
                    Y = _target.Y - right.Y * dx * scale + up.Y * dy * scale,
                    Z = _target.Z - right.Z * dx * scale + up.Z * dy * scale,
                };
            }
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _distance = Math.Max(0.8f, Math.Min(12f, _distance * (float)Math.Pow(0.9, e.Delta / 120.0)));
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            ResetCamera();
            FitTarget();
            Invalidate();
        }

        // ───────── 描く ─────────

        /// <summary>奥から順に描くための1つぶん。</summary>
        private struct Item
        {
            public float Depth;
            public Action<Graphics> Draw;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Background);

            if (_means.Count == 0)
            {
                using (var brush = new SolidBrush(Color.FromArgb(0x70, 0x78, 0x88)))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(Lang.T("軌道がありません", "No swings"), Font, brush, ClientRectangle, format);
                return;
            }

            SetupFrame();
            DrawFloor(g);

            var items = new List<Item>();
            var onTop = new List<Item>();
            if (_hasHead) AddHead(items);
            foreach (var note in _notes)
                if (Shown(note.ColorType) && SwingSides.NoteShown(note.CutDirection, note.ColorType, OnlyForehand)) AddNote(items, onTop, note);

            if (!ShowMeanOnly)
                foreach (var swing in _swings)
                    if (Shown(swing.SaberType)) AddPath(items, swing.Tips, i => Faded(VisibleColor(swing, i), 70), 1.6f);

            foreach (var mean in _means)
            {
                if (!Shown(mean.SaberType)) continue;
                var swing = mean;
                AddBand(items, mean, i => VisibleColor(swing, i));
                AddPath(items, mean.Hilts, i => Faded(Lighter(VisibleColor(swing, i), 0.2f), 200), 2f);
                AddPath(items, mean.Tips, i => Faded(Lighter(VisibleColor(swing, i), 0.4f), 255), 3f);
            }

            AddPlaneSections(onTop);
            AddHitBlades(items);
            foreach (var item in items.OrderByDescending(i => i.Depth)) item.Draw(g);

            // 刃がノーツの中を通った所とノーツの中心の点は、立方体に隠れるので上に重ねる
            foreach (var item in onTop.OrderByDescending(i => i.Depth)) item.Draw(g);

            DrawScoreLabels(g);
            DrawLegend(g);
            DrawHint(g);
        }

        /// <summary>フォアとバックの色の見本（左上）。</summary>
        private void DrawLegend(Graphics g)
        {
            float y = 8f, line = Font.GetHeight(g) + 4f;
            foreach (var row in new[]
            {
                new { Label = Lang.T("フォア", "Forehand"), Left = SwingColors.Left, Right = SwingColors.Right },
                new { Label = Lang.T("バック", "Backhand"), Left = SwingColors.LeftBack, Right = SwingColors.RightBack },
            })
            {
                float box = line - 6f;
                using (var left = new SolidBrush(row.Left))
                using (var right = new SolidBrush(row.Right))
                using (var text = new SolidBrush(Color.FromArgb(0xC8, 0xCC, 0xD4)))
                {
                    g.FillRectangle(left, 8f, y + 2f, box, box);
                    g.FillRectangle(right, 8f + box + 3f, y + 2f, box, box);
                    g.DrawString(row.Label, Font, text, 8f + box * 2 + 10f, y);
                }
                y += line;
            }
        }

        private bool Shown(int hand) { return !OnlyHand.HasValue || OnlyHand.Value == hand; }

        private static Color HandColor(int hand, bool forehand) { return SwingColors.Of(hand, forehand); }

        /// <summary>区間の色。フォアだけ・バックだけのときに出さない区間は <see cref="Color.Empty"/>。</summary>
        private Color VisibleColor(Swing swing, int i)
        {
            i = Math.Max(1, Math.Min(swing.Tips.Length - 1, i));
            bool forehand = swing.SegmentForehand(i);
            return ShownSide(forehand) ? HandColor(swing.SaberType, forehand) : Color.Empty;
        }

        private static Color Faded(Color color, int alpha) { return color.IsEmpty ? color : Color.FromArgb(alpha, color); }

        private static Color Lighter(Color color, float amount) { return color.IsEmpty ? color : ControlPaint.Light(color, amount); }

        /// <summary>床（高さ 0）の格子。50 cm 刻み。前後左右の向きが分かるように、プレイヤーの前を少し明るく。</summary>
        private void DrawFloor(Graphics g)
        {
            const float Extent = 2.5f, Step = 0.5f;
            float cx = _hasHead ? _head.X : 0f, cz = _hasHead ? _head.Z : 0f;
            using (var pen = new Pen(FloorColor, 1f))
            {
                for (float x = -Extent; x <= Extent + 1e-3f; x += Step)
                    DrawLine3(g, pen, new Vector3 { X = cx + x, Y = 0f, Z = cz - 1f }, new Vector3 { X = cx + x, Y = 0f, Z = cz + Extent + 1f });
                for (float z = -1f; z <= Extent + 1f + 1e-3f; z += Step)
                    DrawLine3(g, pen, new Vector3 { X = cx - Extent, Y = 0f, Z = cz + z }, new Vector3 { X = cx + Extent, Y = 0f, Z = cz + z });
            }
        }

        private void DrawLine3(Graphics g, Pen pen, Vector3 a, Vector3 b)
        {
            PointF sa, sb;
            if (ToScreen(a, out sa) && ToScreen(b, out sb)) g.DrawLine(pen, sa, sb);
        }

        /// <summary>頭の位置。丸と、床までの細い線。</summary>
        private void AddHead(List<Item> items)
        {
            var head = _head;
            items.Add(new Item
            {
                Depth = DepthOf(head),
                Draw = g =>
                {
                    PointF top, foot;
                    if (!ToScreen(head, out top)) return;
                    using (var pen = new Pen(Color.FromArgb(90, White), 1f) { DashStyle = DashStyle.Dot })
                        if (ToScreen(new Vector3 { X = head.X, Y = 0f, Z = head.Z }, out foot)) g.DrawLine(pen, top, foot);
                    float r = Math.Max(4f, _focal * 0.1f / Math.Max(0.1f, DepthOf(head)));
                    using (var pen = new Pen(Color.FromArgb(180, White), 1.5f))
                        g.DrawEllipse(pen, top.X - r, top.Y - r, r * 2, r * 2);
                },
            });
        }

        /// <summary>折れ線を区間ごとに分けて積む。区間ごとに奥行きで並べ替えるので、刃やノーツとの前後が合う。</summary>
        private void AddPath(List<Item> items, Vector3[] path, Func<int, Color> colorAt, float width)
        {
            for (int i = 1; i < path.Length; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                var color = colorAt(i);
                if (color.IsEmpty) continue;
                items.Add(new Item
                {
                    Depth = (DepthOf(a) + DepthOf(b)) / 2f,
                    Draw = g =>
                    {
                        using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            DrawLine3(g, pen, a, b);
                    },
                });
            }

            // 向きの印（「く」の字）を真ん中に1つ
            int mid = path.Length / 2;
            if (mid < 1) return;
            var from = path[mid - 1];
            var to = path[mid];
            var markColor = colorAt(mid);
            if (markColor.IsEmpty) return;
            items.Add(new Item
            {
                Depth = DepthOf(to) - 0.001f,
                Draw = g =>
                {
                    PointF sa, sb;
                    if (!ToScreen(from, out sa) || !ToScreen(to, out sb)) return;
                    float dx = sb.X - sa.X, dy = sb.Y - sa.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (len < 0.5f) return;
                    dx /= len; dy /= len;
                    const float Arm = 8f;
                    using (var pen = new Pen(markColor, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(pen, sb.X - dx * Arm - dy * Arm * 0.7f, sb.Y - dy * Arm + dx * Arm * 0.7f, sb.X, sb.Y);
                        g.DrawLine(pen, sb.X - dx * Arm + dy * Arm * 0.7f, sb.Y - dy * Arm - dx * Arm * 0.7f, sb.X, sb.Y);
                    }
                },
            });
        }

        /// <summary>扇の濃さ (0-255)。</summary>
        private const int BandAlpha = 70;

        /// <summary>扇の白さ（手の色をどれだけ白に寄せるか、0-1）。ノーツの立方体と見分けるため白に強く寄せる。</summary>
        private const float BandWhiteness = 0.9f;

        /// <summary>刃が通った面。薄い帯と、数本おきの刃そのもの。</summary>
        private void AddBand(List<Item> items, Swing mean, Func<int, Color> colorAt)
        {
            for (int i = 1; i < mean.Tips.Length; i++)
            {
                var color = colorAt(i);
                if (color.IsEmpty) continue;
                var quad = new[] { mean.Hilts[i - 1], mean.Tips[i - 1], mean.Tips[i], mean.Hilts[i] };
                items.Add(new Item
                {
                    Depth = quad.Average(p => DepthOf(p)),
                    Draw = g =>
                    {
                        var points = new PointF[4];
                        for (int k = 0; k < 4; k++) if (!ToScreen(quad[k], out points[k])) return;
                        // 手の色を白っぽくして濃く塗る。薄いと見えず、ノーツと同じ色だと立方体と重なる所が区別できなかった
                        // 濃さは振り全体で同じ（切る瞬間の近くだけ濃くすると、かえって見づらかった）
                        using (var fill = new SolidBrush(Color.FromArgb(BandAlpha, ControlPaint.Light(color, BandWhiteness))))
                            g.FillPolygon(fill, points);
                    },
                });
            }

            for (int i = 0; i < mean.Tips.Length; i += RibEvery)
            {
                var hilt = mean.Hilts[i];
                var tip = mean.Tips[i];
                var color = colorAt(i);
                if (color.IsEmpty) continue;
                items.Add(new Item
                {
                    Depth = (DepthOf(hilt) + DepthOf(tip)) / 2f,
                    Draw = g =>
                    {
                        using (var pen = new Pen(Color.FromArgb(80, ControlPaint.Light(color, 0.5f)), 1f))
                            DrawLine3(g, pen, hilt, tip);
                    },
                });
            }
        }

        /// <summary>ヒットボックスに当たったフレームの刃（ノーツごと、ノーツの色）。黄色い点（当たった所）はこの刃の上にある。</summary>
        private void AddHitBlades(List<Item> items)
        {
            foreach (var note in _notes)
            {
                if (!note.HasHitPoint) continue;
                if (!Shown(note.ColorType) || !SwingSides.NoteShown(note.CutDirection, note.ColorType, OnlyForehand)) continue;
                var hilt = note.HitHilt;
                var tip = note.HitTip;
                Color bladeColor = Lighter(HandColor(note.ColorType, note.Forehand), 0.3f);
                items.Add(new Item
                {
                    Depth = (DepthOf(hilt) + DepthOf(tip)) / 2f - 0.01f,
                    Draw = g =>
                    {
                        using (var pen = new Pen(Color.FromArgb(240, bladeColor), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            DrawLine3(g, pen, hilt, tip);
                    },
                });
            }
        }

        /// <summary>
        /// ノーツの立方体。面ごとに奥行きで並べ、プレイヤー側の面（手前の面）に切る向きの三角を描く。
        /// 向きは本体と同じく、矢印の向きに傾けたうえでプレイヤー（頭）の方を向く（SwingScene.Note の軸）。
        /// </summary>
        private void AddNote(List<Item> items, List<Item> onTop, Note note)
        {
            Color color = HandColor(note.ColorType, note.Forehand);
            float h = NoteSize / 2;
            double angle = SwingSides.ArrowAngle(note.CutDirection);

            // 面の上の (u, v) と前後 w → 世界。v が矢印の向き（切る向き）。プレイヤーの方を向いた後の軸（SwingScene.Note）
            Func<float, float, float, Vector3> at = note.At;
            var box = Box;
            var corners = note.Corners(box);
            var boxCenter = note.At(0f, 0f, box.Offset);

            int[][] faces =
            {
                new[] { 0, 1, 3, 2 }, // 手前（プレイヤー側）
                new[] { 4, 5, 7, 6 }, // 奥
                new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 },
                new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 },
            };

            for (int f = 0; f < faces.Length; f++)
            {
                var quad = faces[f].Select(i => corners[i]).ToArray();
                bool front = f == 0;

                // こちらを向いた面だけ描く。濃淡は面が視点を向いている度合いで付ける。
                // 不透明だと後ろの軌跡が隠れて読めないので、少しだけ透かす（NoteOpacity）
                var faceCenter = new Vector3 { X = quad.Average(p => p.X), Y = quad.Average(p => p.Y), Z = quad.Average(p => p.Z) };
                // 外向きは箱の中心から測る（ヒットボックスは中心がプレイヤー側へずれている）
                var outward = Normalize(new Vector3 { X = faceCenter.X - boxCenter.X, Y = faceCenter.Y - boxCenter.Y, Z = faceCenter.Z - boxCenter.Z });
                var toEye = Normalize(new Vector3 { X = _eye.X - faceCenter.X, Y = _eye.Y - faceCenter.Y, Z = _eye.Z - faceCenter.Z });
                float facing = Dot(outward, toEye);
                if (facing <= 0f) continue;
                var shaded = Color.FromArgb(NoteOpacity, Shade(color, 0.45f + 0.55f * facing));

                items.Add(new Item
                {
                    Depth = quad.Average(p => DepthOf(p)),
                    Draw = g =>
                    {
                        var points = new PointF[4];
                        for (int k = 0; k < 4; k++) if (!ToScreen(quad[k], out points[k])) return;
                        using (var fill = new SolidBrush(shaded))
                            g.FillPolygon(fill, points);
                        using (var edge = new Pen(Color.FromArgb(200, ControlPaint.Light(color, 0.35f)), 1.4f))
                            g.DrawPolygon(edge, points);

                        if (!front || double.IsNaN(angle)) return;

                        // 三角は切る向きと反対側に置き、先が切る向きを指す（正面図と同じ）。大きさは見た目のノーツのまま
                        float w = box.Front;
                        var tri = new[] { at(0f, -h * 0.24f, w), at(-h * 0.68f, -h * 0.8f, w), at(h * 0.68f, -h * 0.8f, w) };
                        var tp = new PointF[3];
                        for (int k = 0; k < 3; k++) if (!ToScreen(tri[k], out tp[k])) return;
                        using (var head = new SolidBrush(Color.FromArgb(170, White)))
                            g.FillPolygon(head, tp);
                    },
                });
            }

            // 刃がノーツに当たった所の点（正面・横の図と同じ）。点数のラベルの線もここへつなぐ
            if (note.HasContact)
            {
                var contact = note.Contact;
                onTop.Add(new Item
                {
                    Depth = DepthOf(contact),
                    Draw = g =>
                    {
                        PointF c, mid;
                        if (!ToScreen(contact, out c)) return;

                        // ヒットボックスに当たった所に点を打ち、そこから打点への点線。打点は、当たったフレームで刃が掃いた面を広げた平面の上
                        PointF hit;
                        if (note.HasHitPoint && ToScreen(note.HitPoint, out hit))
                        {
                            // 打点の側に矢印。矢尻が点に隠れないよう、点の縁で止める
                            float dx = c.X - hit.X, dy = c.Y - hit.Y;
                            float length = (float)Math.Sqrt(dx * dx + dy * dy);
                            if (length > 10f)
                                using (var head = new AdjustableArrowCap(3.2f, 3.6f, true))
                                using (var reach = new Pen(Color.FromArgb(230, 0xFF, 0xD0, 0x60), 1.7f) { DashStyle = DashStyle.Dot, CustomEndCap = head })
                                    g.DrawLine(reach, hit, new PointF(c.X - dx / length * 4f, c.Y - dy / length * 4f));
                            using (var hitBrush = new SolidBrush(Color.FromArgb(230, 0xFF, 0xD0, 0x60)))
                                g.FillEllipse(hitBrush, hit.X - 3f, hit.Y - 3f, 6f, 6f);
                        }

                        // 当たった所から中心への点線。長さが中心からのずれ（中心の 15 点に効く）
                        if (ToScreen(note.Center, out mid))
                            using (var dash = new Pen(Color.FromArgb(220, White), 1.4f) { DashStyle = DashStyle.Dash })
                                g.DrawLine(dash, c, mid);

                        // 打点（平面の上で中心に一番近い点）は当たった所と同じ黄色。ノーツの中心（白）と見分ける
                        using (var brush = new SolidBrush(Color.FromArgb(245, 0xFF, 0xD0, 0x60)))
                            g.FillEllipse(brush, c.X - 3f, c.Y - 3f, 6f, 6f);
                    },
                });
            }

            // 中心の点。当たり所（中心からの距離）を見る基準。立方体の中に隠れるので、上に重ねる
            var center = note.Center;
            onTop.Add(new Item
            {
                Depth = DepthOf(center),
                Draw = g =>
                {
                    PointF c;
                    if (!ToScreen(center, out c)) return;
                    float r = Math.Max(2.5f, _focal * 0.025f / Math.Max(0.1f, DepthOf(center)));
                    using (var brush = new SolidBrush(Color.FromArgb(235, White)))
                        g.FillEllipse(brush, c.X - r, c.Y - r, r * 2, r * 2);
                },
            });
        }

        /// <summary>
        /// 中心の点を測る平面（当たったフレームで刃が掃いた三角形を無限に広げた平面）と、ノーツの箱が交わる所を白で塗る。
        /// 中心の点はこの平面とノーツの中心の距離で決まり、刃が実際にどこを通ったかとは関係しない。
        /// </summary>
        private void AddPlaneSections(List<Item> items)
        {
            foreach (var note in _notes)
            {
                if (!Shown(note.ColorType) || !SwingSides.NoteShown(note.CutDirection, note.ColorType, OnlyForehand)) continue;
                var section = note.PlaneSection(Box);
                if (section.Count < 3) continue;
                items.Add(new Item
                {
                    Depth = section.Average(p => DepthOf(p)),
                    Draw = g =>
                    {
                        var screen = new PointF[section.Count];
                        for (int k = 0; k < section.Count; k++) if (!ToScreen(section[k], out screen[k])) return;
                        using (var fill = new SolidBrush(Color.FromArgb(PlaneSectionAlpha, White)))
                            g.FillPolygon(fill, screen);
                    },
                });
            }
        }

        /// <summary>
        /// ノーツごとの点の平均を、振りかぶり（70）・振り抜き（30）・中心（15）の順に横に並べて出す。軌道を隠さないよう、図の左右の外側（ノーツが図の中心より左なら左）へ
        /// 逃がして並べ、打点（中心の点を測る点）と細い線でつなぐ。同じ側で重なるものは上下にずらす。
        /// </summary>
        private void DrawScoreLabels(Graphics g)
        {
            PointF middle;
            if (!ToScreen(_target, out middle)) return;
            Vector3 forward, right, up;
            Basis(out forward, out right, out up);

            var labels = new List<KeyValuePair<PointF, Note>>();
            foreach (var note in _notes)
            {
                if (double.IsNaN(note.MeanScore)) continue;
                if (!Shown(note.ColorType) || !SwingSides.NoteShown(note.CutDirection, note.ColorType, OnlyForehand)) continue;
                // 線は打点へつなぐ（分からなければ中心）
                PointF p;
                if (ToScreen(note.HasContact ? note.Contact : note.Center, out p)) labels.Add(new KeyValuePair<PointF, Note>(p, note));
            }
            if (labels.Count == 0) return;

            using (var font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold))
            {
                float height = g.MeasureString("0", font).Height + 2;
                foreach (int side in new[] { -1, 1 })
                {
                    var group = labels.Where(l => (l.Key.X < middle.X ? -1 : 1) == side).OrderBy(l => l.Key.Y).ToList();
                    if (group.Count == 0) continue;

                    // 立方体の外側まで。いちばん外のノーツの立方体の端より外に、同じ側のラベルの列を揃える
                    float edge = side < 0 ? float.MaxValue : float.MinValue;
                    foreach (var l in group)
                    {
                        PointF rim;
                        var c = l.Value.Center;
                        if (!ToScreen(new Vector3 { X = c.X + right.X * side * 0.45f, Y = c.Y + right.Y * side * 0.45f, Z = c.Z + right.Z * side * 0.45f }, out rim)) continue;
                        edge = side < 0 ? Math.Min(edge, rim.X) : Math.Max(edge, rim.X);
                    }
                    if (edge == float.MaxValue || edge == float.MinValue) continue;
                    float column = edge + side * 14f;

                    // 先に縦の位置を決める。重なれば下へずらし、下の端からはみ出した分は全体を上へ戻す（上の端は越えない）
                    var ys = new float[group.Count];
                    float lastY = float.MinValue;
                    for (int k = 0; k < group.Count; k++)
                    {
                        ys[k] = Math.Max(Math.Max(2f, group[k].Key.Y - height / 2), lastY + height + 2);
                        lastY = ys[k];
                    }
                    float overflow = lastY + height - (Height - 24f);
                    if (overflow > 0)
                    {
                        overflow = Math.Min(overflow, ys[0] - 2f);
                        for (int k = 0; k < ys.Length; k++) ys[k] -= overflow;
                    }

                    for (int k = 0; k < group.Count; k++)
                    {
                        var l = group[k];
                        var note = l.Value;
                        string text = ScoreFormat.Floor(note.MeanBeforeCut, 1) + "  " + ScoreFormat.Floor(note.MeanAfterCut, 1)
                                      + "  " + ScoreFormat.Floor(note.MeanCutDistance, 1);
                        var size = g.MeasureString(text, font);
                        float y = ys[k];
                        float x = side < 0 ? column - size.Width - 6 : column;
                        x = Math.Max(4f, Math.Min(Width - size.Width - 10f, x));   // 枠からはみ出さない
                        var rect = new RectangleF(x, y, size.Width + 6, height);

                        Color color = HandColor(note.ColorType, note.Forehand);
                        var anchor = new PointF(side < 0 ? rect.Right : rect.Left, rect.Y + rect.Height / 2);
                        using (var line = new Pen(Color.FromArgb(140, ControlPaint.Light(color, 0.4f)), 1f))
                            g.DrawLine(line, anchor, l.Key);
                        using (var back = new SolidBrush(Color.FromArgb(200, 0x10, 0x12, 0x16)))
                            g.FillRectangle(back, rect);
                        using (var brush = new SolidBrush(ControlPaint.Light(color, 0.5f)))
                            g.DrawString(text, font, brush, rect.X + 3, rect.Y + 1);
                    }
                }
            }
        }

        /// <summary>中心の点を測る平面とノーツが交わる所の濃さ (0-255)。</summary>
        private const int PlaneSectionAlpha = 70;

        private void DrawHint(Graphics g)
        {
            string text = Lang.T("ドラッグで回転・ホイールで拡大・右ドラッグで移動・ダブルクリックで戻す",
                "Drag to rotate, wheel to zoom, right-drag to move, double-click to reset");
            using (var brush = new SolidBrush(Color.FromArgb(0x70, 0x78, 0x88)))
                g.DrawString(text, Font, brush, 8f, Height - Font.GetHeight(g) - 6f);
        }
    }
}
