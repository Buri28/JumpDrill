using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using JumpDrill.Replays;

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

        /// <summary>1本の振り。先端と手元を同じ瞬間どうしで並べたもの。</summary>
        private sealed class Swing
        {
            public int SaberType;

            /// <summary>次に切るノーツ・前に切ったノーツがフォア（切り下げ）か（採点と同じ決め方）。</summary>
            public bool ToForehand;
            public bool FromForehand;

            /// <summary>次に切るノーツの矢印の向き（世界座標の X-Y）。区間の色を決めるのに使う。</summary>
            public PointF ToDirection;
            public Vector3[] Tips;
            public Vector3[] Hilts;
        }

        /// <summary>切った瞬間のノーツ（平均）。</summary>
        private sealed class Note
        {
            public int ColorType;
            public bool Forehand;
            public int CutDirection;
            public Vector3 Center;
        }

        private readonly List<Swing> _swings = new List<Swing>();
        private readonly List<Swing> _means = new List<Swing>();
        private readonly List<Note> _notes = new List<Note>();
        private Vector3 _head;
        private bool _hasHead;

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

        private bool ShownSide(bool forehand) { return !OnlyForehand.HasValue || OnlyForehand.Value == forehand; }

        /// <param name="replay">軌道に使うリプレイ（タワー・スタックは端の点だけにまとめたもの）。</param>
        /// <param name="notesFrom">ノーツを置くリプレイ（まとめる前）。null なら <paramref name="replay"/>。</param>
        public void Load(Replay replay, Replay notesFrom = null)
        {
            _swings.Clear();
            _means.Clear();
            _notes.Clear();
            _hasHead = false;

            if (replay != null)
            {
                foreach (int saberType in new[] { 0, 1 }) LoadHand(replay, saberType);
                LoadNotes(notesFrom ?? replay);
                LoadHead(notesFrom ?? replay);
            }

            FitTarget();
            Invalidate();
        }

        // ───────── データ ─────────

        /// <summary>遷移ごとに振りを束ね、平均を取る（正面図と同じ決め方）。</summary>
        private void LoadHand(Replay replay, int saberType)
        {
            var groups = new Dictionary<string, List<Swing>>();
            var sequence = SwingAnalyzer.HandSequence(replay, saberType);
            foreach (var pair in SwingAnalyzer.CleanPairs(sequence, saberType))
            {
                var swing = Extract(replay, saberType, CutMoments.Find(replay, pair.Key), CutMoments.Find(replay, pair.Value));
                if (swing == null) continue;
                swing.ToForehand = SwingAnalyzer.IsForehand(pair.Value, sequence.IndexOf(pair.Value));
                swing.FromForehand = SwingAnalyzer.IsForehand(pair.Key, sequence.IndexOf(pair.Key));
                swing.ToDirection = SwingColors.ArrowOf(pair.Value.CutDirection);

                string key = pair.Key.LineIndex + "," + pair.Key.LineLayer + ">" + pair.Value.LineIndex + "," + pair.Value.LineLayer;
                List<Swing> list;
                if (!groups.TryGetValue(key, out list)) groups[key] = list = new List<Swing>();
                list.Add(swing);
            }

            foreach (var group in groups.Values)
            {
                if (group.Count < 2) continue;
                _swings.AddRange(group);
                _means.Add(new Swing
                {
                    SaberType = saberType,
                    ToForehand = group[0].ToForehand,
                    FromForehand = group[0].FromForehand,
                    ToDirection = group[0].ToDirection,
                    Tips = Mean(group.Select(s => s.Tips).ToList()),
                    Hilts = Mean(group.Select(s => s.Hilts).ToList()),
                });
            }
        }

        /// <summary>
        /// 切った瞬間から次に切った瞬間までの刃。先端の弧長で等間隔に取り、手元も同じ瞬間で取る。
        /// </summary>
        private static Swing Extract(Replay replay, int saberType, float from, float to)
        {
            var tips = new List<Vector3>();
            var hilts = new List<Vector3>();

            Vector3 hilt, tip;
            CutMoments.BladeAt(replay, saberType, from, out hilt, out tip);
            tips.Add(tip);
            hilts.Add(hilt);
            foreach (var frame in replay.Frames)
            {
                if (frame.Time <= from) continue;
                if (frame.Time >= to) break;
                tips.Add(frame.SaberTip(saberType));
                hilts.Add(frame.Saber(saberType).Position);
            }
            CutMoments.BladeAt(replay, saberType, to, out hilt, out tip);
            tips.Add(tip);
            hilts.Add(hilt);
            if (tips.Count < 4) return null;

            // 先端の弧長で取り直し、手元も同じ位置（同じ瞬間）で取る
            int samples = SwingAnalyzer.SamplesPerSwing;
            var cumulative = new double[tips.Count];
            for (int i = 1; i < tips.Count; i++) cumulative[i] = cumulative[i - 1] + Vector3.Distance(tips[i - 1], tips[i]);
            double total = cumulative[tips.Count - 1];

            var outTips = new Vector3[samples];
            var outHilts = new Vector3[samples];
            int cursor = 0;
            for (int i = 0; i < samples; i++)
            {
                double target = total * i / (samples - 1);
                while (cursor < tips.Count - 2 && cumulative[cursor + 1] < target) cursor++;
                double segment = cumulative[cursor + 1] - cumulative[cursor];
                float t = segment <= 1e-9 ? 0f : (float)((target - cumulative[cursor]) / segment);
                outTips[i] = CutMoments.Lerp(tips[cursor], tips[cursor + 1], t);
                outHilts[i] = CutMoments.Lerp(hilts[cursor], hilts[cursor + 1], t);
            }

            return new Swing { SaberType = saberType, Tips = outTips, Hilts = outHilts };
        }

        private static Vector3[] Mean(List<Vector3[]> paths)
        {
            int samples = paths[0].Length;
            var mean = new Vector3[samples];
            for (int i = 0; i < samples; i++)
                mean[i] = new Vector3
                {
                    X = paths.Average(p => p[i].X),
                    Y = paths.Average(p => p[i].Y),
                    Z = paths.Average(p => p[i].Z),
                };
            return mean;
        }

        /// <summary>
        /// 切った瞬間のノーツの中心を、配置（手・マス・向き）ごとに平均する。
        /// 中心は「打点 ± 法線 × 距離」の2つの候補のうち、マスに近い方（正面図と同じ）。
        /// </summary>
        private void LoadNotes(Replay replay)
        {
            double residual;
            var grid = NoteGrid.FitTo(replay, out residual);

            foreach (var group in replay.Notes
                .Where(n => n.EventType == NoteEventType.Good && n.Cut != null && !n.IsStandIn)
                .GroupBy(n => new { n.ColorType, n.LineIndex, n.LineLayer, n.CutDirection }))
            {
                float gx = grid.X(group.Key.LineIndex), gy = grid.Y(group.Key.LineLayer);
                double x = 0, y = 0, z = 0;
                int count = 0;
                foreach (var note in group)
                {
                    var p = note.Cut.CutPoint;
                    if (p.X == 0 && p.Y == 0 && p.Z == 0) continue;
                    var n = note.Cut.CutNormal;
                    double d = note.Cut.CutDistanceToCenter;
                    double plus = Sq(p.X + n.X * d - gx) + Sq(p.Y + n.Y * d - gy);
                    double minus = Sq(p.X - n.X * d - gx) + Sq(p.Y - n.Y * d - gy);
                    double sign = plus <= minus ? 1.0 : -1.0;
                    x += p.X + sign * n.X * d;
                    y += p.Y + sign * n.Y * d;
                    z += p.Z + sign * n.Z * d;
                    count++;
                }
                if (count == 0) continue;

                _notes.Add(new Note
                {
                    ColorType = group.Key.ColorType,
                    Forehand = SwingAnalyzer.IsForehand(group.First(), 0),
                    CutDirection = group.Key.CutDirection,
                    Center = new Vector3 { X = (float)(x / count), Y = (float)(y / count), Z = (float)(z / count) },
                });
            }
        }

        private static double Sq(double v) { return v * v; }

        /// <summary>切った瞬間の頭の位置（平均）。プレイヤーがどこに立っていたかの目印。</summary>
        private void LoadHead(Replay replay)
        {
            var cuts = replay.Notes.Where(n => n.EventType == NoteEventType.Good && n.Cut != null).ToList();
            if (cuts.Count == 0 || replay.Frames.Count == 0) return;

            double x = 0, y = 0, z = 0;
            foreach (var note in cuts)
            {
                int i = replay.Frames.FindIndex(f => f.Time >= note.EventTime);
                var head = replay.Frames[i < 0 ? replay.Frames.Count - 1 : i].Head.Position;
                x += head.X; y += head.Y; z += head.Z;
            }
            _head = new Vector3 { X = (float)(x / cuts.Count), Y = (float)(y / cuts.Count), Z = (float)(z / cuts.Count) };
            _hasHead = true;
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

            // 全部が画角に収まる距離。いちばん遠い点（とノーツの大きさ）を半径の球として測る
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
                if (Shown(note.ColorType) && ShownSide(note.Forehand)) AddNote(items, onTop, note);

            if (!ShowMeanOnly)
                foreach (var swing in _swings)
                    if (Shown(swing.SaberType)) AddPath(items, swing.Tips, i => Faded(VisibleColor(swing, i), 70), 1.2f);

            foreach (var mean in _means)
            {
                if (!Shown(mean.SaberType)) continue;
                var swing = mean;
                AddBand(items, mean, i => VisibleColor(swing, i));
                AddBandInsideNotes(onTop, mean);
                AddPath(items, mean.Hilts, i => Faded(Lighter(VisibleColor(swing, i), 0.2f), 200), 2f);
                AddPath(items, mean.Tips, i => Faded(Lighter(VisibleColor(swing, i), 0.4f), 255), 3f);
                if (!VisibleColor(swing, mean.Tips.Length - 1).IsEmpty)
                    AddCut(items, mean, SegmentColor(swing, mean.Tips.Length - 1));
            }

            foreach (var item in items.OrderByDescending(i => i.Depth)) item.Draw(g);

            // 刃がノーツの中を通った所とノーツの中心の点は、立方体に隠れるので上に重ねる
            foreach (var item in onTop.OrderByDescending(i => i.Depth)) item.Draw(g);

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
            bool forehand = SwingColors.SegmentForehand(i, SwingColors.TurnIndex(swing.Tips, swing.ToDirection), swing.ToForehand, swing.FromForehand);
            return ShownSide(forehand) ? HandColor(swing.SaberType, forehand) : Color.Empty;
        }

        private static Color Faded(Color color, int alpha) { return color.IsEmpty ? color : Color.FromArgb(alpha, color); }

        private static Color Lighter(Color color, float amount) { return color.IsEmpty ? color : ControlPaint.Light(color, amount); }

        /// <summary>
        /// 振りの i 番目の区間（i-1 → i）の色。折り返し点の前後でフォア・バックを決める（<see cref="SwingColors.TurnIndex"/>）。
        /// 手元の道のりも、同じ瞬間の先端の動きで決める。
        /// </summary>
        private static Color SegmentColor(Swing swing, int i)
        {
            i = Math.Max(1, Math.Min(swing.Tips.Length - 1, i));
            return HandColor(swing.SaberType,
                SwingColors.SegmentForehand(i, SwingColors.TurnIndex(swing.Tips, swing.ToDirection), swing.ToForehand, swing.FromForehand));
        }

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

        /// <summary>切った瞬間の刃（振りの終わり）。白い線と、先端の白丸。</summary>
        private void AddCut(List<Item> items, Swing mean, Color color)
        {
            var hilt = mean.Hilts[mean.Hilts.Length - 1];
            var tip = mean.Tips[mean.Tips.Length - 1];
            items.Add(new Item
            {
                Depth = (DepthOf(hilt) + DepthOf(tip)) / 2f - 0.01f,
                Draw = g =>
                {
                    using (var pen = new Pen(Color.FromArgb(230, White), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        DrawLine3(g, pen, hilt, tip);
                    PointF s;
                    if (!ToScreen(tip, out s)) return;
                    using (var brush = new SolidBrush(White))
                    using (var edge = new Pen(Color.FromArgb(0x10, 0x12, 0x16), 1.4f))
                    {
                        g.FillEllipse(brush, s.X - 4f, s.Y - 4f, 8f, 8f);
                        g.DrawEllipse(edge, s.X - 4f, s.Y - 4f, 8f, 8f);
                    }
                },
            });
        }

        /// <summary>
        /// ノーツの立方体。面ごとに奥行きで並べ、プレイヤー側の面（手前の面）に切る向きの三角を描く。
        /// 向きは矢印の 8 方向だけで回し、プレイヤーの方へ向く分は入れない。
        /// </summary>
        private void AddNote(List<Item> items, List<Item> onTop, Note note)
        {
            Color color = HandColor(note.ColorType, note.Forehand);
            float h = NoteSize / 2;
            double angle = ArrowAngle(note.CutDirection);
            double turn = double.IsNaN(angle) ? 0.0 : angle;   // 向き指定なしは回さない
            float ca = (float)Math.Cos(turn), sa = (float)Math.Sin(turn);

            // 面の上の (u, v) と前後 w → 世界。v が矢印の向き（切る向き）
            Func<float, float, float, Vector3> at = (u, v, w) => new Vector3
            {
                X = note.Center.X + u * ca + v * sa,
                Y = note.Center.Y - u * sa + v * ca,
                Z = note.Center.Z + w,
            };

            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
                corners[i] = at((i & 1) == 0 ? -h : h, (i & 2) == 0 ? -h : h, (i & 4) == 0 ? -h : h);

            int[][] faces =
            {
                new[] { 0, 1, 3, 2 }, // 手前（プレイヤー側、w = -h）
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
                var outward = Normalize(new Vector3 { X = faceCenter.X - note.Center.X, Y = faceCenter.Y - note.Center.Y, Z = faceCenter.Z - note.Center.Z });
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

                        // 三角は切る向きと反対側に置き、先が切る向きを指す（正面図と同じ）
                        var tri = new[] { at(0f, -h * 0.24f, -h), at(-h * 0.68f, -h * 0.8f, -h), at(h * 0.68f, -h * 0.8f, -h) };
                        var tp = new PointF[3];
                        for (int k = 0; k < 3; k++) if (!ToScreen(tri[k], out tp[k])) return;
                        using (var head = new SolidBrush(Color.FromArgb(170, White)))
                            g.FillPolygon(head, tp);
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
        /// 扇のうち、同じ手のノーツの立方体と重なる所（刃がノーツの中を通った所）を白で塗る。
        /// 扇の区間ごとの四角を、立方体の 6 枚の面で順に切り取ったもの（凸な多角形の切り取り）。
        /// 刃がノーツのどこを、どれだけの深さまで切ったかが、白い所の形で見える。
        /// 立方体の色と混ざると読めないので、立方体より上に描く。
        /// </summary>
        private void AddBandInsideNotes(List<Item> items, Swing mean)
        {
            foreach (var note in _notes)
            {
                if (note.ColorType != mean.SaberType || !ShownSide(note.Forehand)) continue;

                double turn = ArrowAngle(note.CutDirection);
                if (double.IsNaN(turn)) turn = 0.0;
                // 立方体の軸（AddNote と同じ回し方）
                var axisU = new Vector3 { X = (float)Math.Cos(turn), Y = (float)-Math.Sin(turn), Z = 0f };
                var axisV = new Vector3 { X = (float)Math.Sin(turn), Y = (float)Math.Cos(turn), Z = 0f };
                var axisW = new Vector3 { Z = 1f };
                var center = note.Center;

                for (int i = 1; i < mean.Tips.Length; i++)
                {
                    if (VisibleColor(mean, i).IsEmpty) continue;

                    var polygon = new List<Vector3> { mean.Hilts[i - 1], mean.Tips[i - 1], mean.Tips[i], mean.Hilts[i] };
                    foreach (var axis in new[] { axisU, axisV, axisW })
                        foreach (float sign in new[] { 1f, -1f })
                        {
                            // 中心からの、この軸の向きの距離が NoteSize / 2 以下の側を残す
                            Func<Vector3, float> inside = p => NoteSize / 2 - sign * Dot(axis,
                                new Vector3 { X = p.X - center.X, Y = p.Y - center.Y, Z = p.Z - center.Z });
                            polygon = ClipPolygon(polygon, inside);
                            if (polygon.Count < 3) break;
                        }
                    if (polygon.Count < 3) continue;

                    var clipped = polygon.ToArray();
                    items.Add(new Item
                    {
                        Depth = clipped.Average(p => DepthOf(p)),
                        Draw = g =>
                        {
                            var screen = new PointF[clipped.Length];
                            for (int k = 0; k < clipped.Length; k++) if (!ToScreen(clipped[k], out screen[k])) return;
                            // 縁は引かない。薄い白では区間の継ぎ目で縁が二重になり、筋に見えた
                            using (var fill = new SolidBrush(Color.FromArgb(InsideNoteAlpha, White)))
                                g.FillPolygon(fill, screen);
                        },
                    });
                }
            }
        }

        /// <summary>刃がノーツの中を通った所の濃さ (0-255)。</summary>
        private const int InsideNoteAlpha = 70;

        /// <summary>多角形のうち <paramref name="inside"/> が 0 以上の側だけを残す（Sutherland–Hodgman の1枚ぶん）。</summary>
        private static List<Vector3> ClipPolygon(List<Vector3> polygon, Func<Vector3, float> inside)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                float da = inside(a), db = inside(b);
                if (da >= 0) result.Add(a);
                if ((da >= 0) != (db >= 0)) result.Add(CutMoments.Lerp(a, b, da / (da - db)));
            }
            return result;
        }

        /// <summary>
        /// 切る向きの角度（上向きから右回りのラジアン）。面の v 軸がこの向きになる（(sin, cos)）。
        /// 三角は v の負の側に置き、先が v の向き（切る向き）を指す。向き指定なしは NaN。
        /// </summary>
        private static double ArrowAngle(int cutDirection)
        {
            switch (cutDirection)
            {
                case 0: return 0.0;                // Up
                case 1: return Math.PI;            // Down
                case 2: return -Math.PI / 2;       // Left
                case 3: return Math.PI / 2;        // Right
                case 4: return -Math.PI / 4;       // UpLeft
                case 5: return Math.PI / 4;        // UpRight
                case 6: return -Math.PI * 3 / 4;   // DownLeft
                case 7: return Math.PI * 3 / 4;    // DownRight
                default: return double.NaN;
            }
        }

        private void DrawHint(Graphics g)
        {
            string text = Lang.T("ドラッグで回転・ホイールで拡大・右ドラッグで移動・ダブルクリックで戻す",
                "Drag to rotate, wheel to zoom, right-drag to move, double-click to reset");
            using (var brush = new SolidBrush(Color.FromArgb(0x70, 0x78, 0x88)))
                g.DrawString(text, Font, brush, 8f, Height - Font.GetHeight(g) - 6f);
        }
    }
}
