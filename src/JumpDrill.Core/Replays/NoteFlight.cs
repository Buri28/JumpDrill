using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using JumpDrill.Output;
using JumpDrill.Serialization;

namespace JumpDrill.Replays
{
    /// <summary>
    /// ノーツの飛び方と向きを、本体（Beat Saber）と同じ計算でなぞる。図のノーツの向きはここで出す。
    /// </summary>
    /// <remarks>
    /// 本体の <c>NoteJump.ManualUpdate</c>・<c>NoteJump.Init</c>・<c>VariableMovementDataProvider</c>・
    /// <c>BeatmapObjectSpawnMovementData</c>（1.40 で確認）と同じ式を使う。
    /// <list type="bullet">
    /// <item>飛ぶ時間は <c>jumpDuration = jumpDistance / NJS</c>。ノーツの時刻から半分戻った時刻に跳び始め、
    /// 進み具合 <c>n = (t − 跳び始め) / jumpDuration</c> が 0.5 のときがノーツの時刻。</item>
    /// <item>位置: 横は列の位置（<c>(列 − 1.5) × 0.6</c>）、奥行きは <c>頭の z + 0.9 + jumpDistance × (0.5 − n)</c>、
    /// 高さは跳び始めの段の高さから放物線で、ノーツの時刻にいちばん高い所（段ごとの高さ＋身長の下駄）に着く。
    /// 跳び始めの段は、同じ拍・同じ列のノーツを下から数えた順番（列に1つだけなら、どの段のノーツも一番下から跳ぶ）。
    /// 同じ拍に赤と青が1つずつあり赤が青より右なら、相手の列から跳び始めて n &lt; 0.25 の間に自分の列へ寄り、
    /// その間は上か下へ少しよける（<c>BeatmapObjectsInTimeRowProcessor</c>・<c>NoteData.SetNoteFlipToNote</c>）。</item>
    /// <item>向き: n &lt; 0.5 の間、毎フレーム「その時の本来の向き」と「頭の方を向いた向き」を n × 2 の割合で混ぜる
    /// （<c>Quaternion.Lerp</c>）。本来の向きは、最初の 1/8 で何もない向きから少しずらした向き（乱数表のぶれ ×20°）へ、
    /// その後は矢印の向き（＋角度オフセット）へ寄っていく（<c>Slerp</c>、割合は sin）。
    /// 頭の方を向いた向きは、頭の高さを 8 割ノーツ側に寄せた点からノーツへの向きを前に、前のフレームの上向きを上にして作る。
    /// n が 0.5 を過ぎると向きは止まる。</item>
    /// </list>
    /// フレームは本体と同じく、リプレイに記録された1フレームずつで進める（前のフレームの上向きを使うため）。
    /// 奥行きの基準（<see cref="CenterZ"/>）はコードの既定ではなく、実際のノーツの位置から確かめた値。
    /// 跳び始める前の向き（床を進んでいる間）は回っていない。
    /// 跳び始めの段と入れ替わりは、リプレイに残ったノーツから決める（当たらなかった爆弾とチェーンは記録に無いので数えない）。
    /// チェーンの頭は本体でも頭の方を向かないので、ここでは出さない。
    /// </remarks>
    public static class NoteFlight
    {
        /// <summary>
        /// 本体の <c>BeatmapObjectSpawnMovementData._centerPos</c> の奥行き。コードの既定は 0.65 だが、場面に保存された値で上書きされている。
        /// 切った瞬間のノーツの中心（打点と切った面から戻した点）を集めると、どのプレイでも
        /// <c>頭の z + 0.900 + jumpDistance × (0.5 − n)</c> にばらつき 1 cm 前後で乗る（傾き 1.00）ので、0.9 とする。
        /// </summary>
        private const float CenterZ = 0.9f;

        /// <summary>本体の <c>NoteJump._randomRotations</c>。</summary>
        private static readonly float[][] RandomRotations =
        {
            new[] { -0.9543871f, -0.1183784f, 0.2741019f },
            new[] { 0.7680854f, -0.08805521f, 0.6342642f },
            new[] { -0.6780157f, 0.306681f, -0.6680131f },
            new[] { 0.1255014f, 0.9398643f, 0.3176546f },
            new[] { 0.365105f, -0.3664974f, -0.8557909f },
            new[] { -0.8790653f, -0.06244748f, -0.4725934f },
            new[] { 0.01886305f, -0.8065798f, 0.5908241f },
            new[] { -0.1455435f, 0.8901445f, 0.4318099f },
            new[] { 0.07651193f, 0.9474725f, -0.3105508f },
            new[] { 0.1306983f, -0.2508438f, -0.9591639f },
        };

        /// <summary>段の跳び始めの高さ（<c>LineYPosForLineLayer</c>）。</summary>
        private static float LineY(int layer) { return layer <= 0 ? 0.25f : layer == 1 ? 0.85f : 1.45f; }

        /// <summary>段のいちばん高い所（身長の下駄を除く、<c>HighestJumpPosYForLineLayerWithoutJumpOffset</c>）。</summary>
        private static float HighestY(int layer) { return layer <= 0 ? 0.85f : layer == 1 ? 1.4f : 1.9f; }

        /// <summary>ノーツごとの跳び方（跳び始めの段と、入れ替わりの相手の列・よける向き）。</summary>
        public sealed class Spawns
        {
            internal readonly Dictionary<ReplayNote, Spawn> ByNote = new Dictionary<ReplayNote, Spawn>();

            /// <summary>跳び始めの段。リプレイに無いノーツならそのノーツの段。</summary>
            public int BeforeJumpLayerOf(ReplayNote note)
            {
                Spawn spawn;
                return ByNote.TryGetValue(note, out spawn) ? spawn.BeforeJumpLayer : note.LineLayer;
            }

            /// <summary>跳び始めの列（入れ替わるときは相手の列）。</summary>
            public int StartLaneOf(ReplayNote note)
            {
                Spawn spawn;
                return ByNote.TryGetValue(note, out spawn) ? spawn.FlipLane : note.LineIndex;
            }
        }

        internal struct Spawn
        {
            public int BeforeJumpLayer;
            public int FlipLane;
            public float FlipYSide;
        }

        /// <summary>
        /// リプレイのノーツから、跳び始めの段と入れ替わりを決める（本体の <c>BeatmapObjectsInTimeRowProcessor</c>）。
        /// 同じ拍は、拍の最初のノーツから 1 ms 以内。
        /// </summary>
        public static Spawns PlanSpawns(Replay replay)
        {
            var spawns = new Spawns();
            if (replay == null) return spawns;

            // チェーンの輪（5・8）は別の物なので数えない
            var notes = replay.Notes.Where(n => n.ScoringType != 5 && n.ScoringType != 8)
                                    .OrderBy(n => n.SpawnTime).ToList();
            int start = 0;
            while (start < notes.Count)
            {
                float time = notes[start].SpawnTime;
                int end = start;
                while (end < notes.Count && notes[end].SpawnTime <= time + 0.001f) end++;
                var row = notes.GetRange(start, end - start);
                start = end;

                // 列ごとに段の低い順に並べた順番が跳び始めの段。同じ段は来た順のまま（本体は「上の段より前に入れる」）
                foreach (var column in row.GroupBy(n => n.LineIndex))
                {
                    var ordered = new List<ReplayNote>();
                    foreach (var note in column)
                    {
                        int at = ordered.FindIndex(o => o.LineLayer > note.LineLayer);
                        if (at < 0) ordered.Add(note); else ordered.Insert(at, note);
                    }
                    for (int i = 0; i < ordered.Count; i++)
                        spawns.ByNote[ordered[i]] = new Spawn { BeforeJumpLayer = i, FlipLane = ordered[i].LineIndex };
                }

                // 色のノーツがちょうど赤と青の2つで、赤が青より右なら入れ替わる
                var colored = row.Where(n => n.ColorType == 0 || n.ColorType == 1).ToList();
                if (colored.Count == 2)
                {
                    var a = colored[0];
                    var b = colored[1];
                    if (a.ColorType != b.ColorType &&
                        ((a.ColorType == 0 && a.LineIndex > b.LineIndex) || (a.ColorType == 1 && a.LineIndex < b.LineIndex)))
                    {
                        Flip(spawns, a, b);
                        Flip(spawns, b, a);
                    }
                }
            }
            return spawns;
        }

        /// <summary>本体の <c>NoteData.SetNoteFlipToNote</c>。</summary>
        private static void Flip(Spawns spawns, ReplayNote note, ReplayNote other)
        {
            var spawn = spawns.ByNote[note];
            spawn.FlipLane = other.LineIndex;
            float side = note.LineIndex > other.LineIndex ? 1f : -1f;
            if ((note.LineIndex > other.LineIndex && note.LineLayer < other.LineLayer) ||
                (note.LineIndex < other.LineIndex && note.LineLayer > other.LineLayer))
                side = -side;
            spawn.FlipYSide = side;
            spawns.ByNote[note] = spawn;
        }

        /// <summary>
        /// 切った瞬間のノーツの向き（本体の <c>_rotatedObject</c> の向き）。出せなければ false。
        /// </summary>
        /// <param name="spawns"><see cref="PlanSpawns"/> の結果（同じリプレイのもの）。</param>
        /// <param name="note">
        /// 切ったノーツ。<see cref="ReplayNote.SpawnTime"/> がノーツの時刻、<see cref="ReplayNote.EventTime"/> が本体の切った時刻
        /// （切ったフレームの時刻。本体の <c>timeDeviation = ノーツの時刻 − 切った時刻</c> と一致する）。
        /// 本体はフレームごとに向きを進め、切った判定はそのフレームの位置と向きで行う。
        /// </param>
        /// <param name="endRotation">矢印の向き＋角度オフセット（度、本体の <c>endRotation</c>）。</param>
        /// <param name="noteJumpSpeed">NJS。</param>
        /// <param name="jumpOffsetY">身長の下駄（<see cref="NoteGrid.OffsetY"/>）。</param>
        /// <returns>
        /// ジャンプ距離か NJS が無い、チェーンの頭、跳び始めから切るまでのフレームが無い（記録が欠けている）ときは false。
        /// </returns>
        public static bool TryRotationAtCut(Replay replay, Spawns spawns, ReplayNote note, float endRotation,
                                            float noteJumpSpeed, float jumpOffsetY, out Quat rotation)
        {
            rotation = Quat.Identity;
            var frames = replay.Frames;
            float jumpDistance = replay.Info.JumpDistance;
            if (frames.Count == 0 || noteJumpSpeed <= 0 || jumpDistance <= 0) return false;
            if (note.ScoringType == 4 || note.ScoringType == 7) return false;   // チェーンの頭は頭の方を向かない

            Spawn spawn;
            if (spawns == null || !spawns.ByNote.TryGetValue(note, out spawn))
                spawn = new Spawn { BeforeJumpLayer = note.LineLayer, FlipLane = note.LineIndex };

            float jumpDuration = jumpDistance / noteJumpSpeed;
            float half = jumpDuration * 0.5f;
            float noteTime = note.SpawnTime;

            var flight = new Flight
            {
                Start = noteTime - half,
                JumpDuration = jumpDuration,
                Half = half,
                JumpDistance = jumpDistance,
                StartX = LaneX(spawn.FlipLane),
                EndX = LaneX(note.LineIndex),
                StartY = LineY(spawn.BeforeJumpLayer),
                YAvoidance = spawn.FlipYSide > 0 ? spawn.FlipYSide * 0.45f : spawn.FlipYSide * 0.15f,
            };
            flight.Gravity = 2f * (HighestY(note.LineLayer) + jumpOffsetY - flight.StartY) / (half * half);
            flight.End = Quat.FromEuler(0f, 0f, endRotation);
            flight.Middle = MiddleRotation(endRotation, noteTime, flight.EndX, flight.StartY);

            var up = new Vector3 { Y = 1f };
            bool stepped = false;

            // 本体はフレームごとに進める。リプレイのフレーム（同じ時刻で記録している）で、切ったフレームまで。
            // 向きを変えるのはノーツの時刻（n = 0.5）まで
            float cutTime = note.EventTime;
            for (int i = FirstFrameAtOrAfter(frames, flight.Start); i < frames.Count && frames[i].Time <= cutTime + 1e-4f; i++)
            {
                if (frames[i].Time >= noteTime) break;
                rotation = flight.Step(frames[i].Time, frames[i].Head.Position, ref up);
                stepped = true;
            }
            return stepped;
        }

        private static float LaneX(int lane) { return (lane - 1.5f) * 0.6f; }

        /// <summary>1つのノーツの飛び方（本体の <c>NoteJump.Init</c> で決まる値）。</summary>
        private struct Flight
        {
            public float Start, JumpDuration, Half, JumpDistance, Gravity;
            public float StartX, EndX, StartY, YAvoidance;
            public Quat Middle, End;

            /// <summary>本体の <c>NoteJump.ManualUpdate</c> の1フレームぶん。</summary>
            public Quat Step(float time, Vector3 head, ref Vector3 up)
            {
                float num = time - Start;
                float n = num / JumpDuration;

                float x = StartX == EndX ? EndX : n < 0.25f ? StartX + (EndX - StartX) * InOutQuad(n * 4f) : EndX;
                float y = StartY + Gravity * Half * num - Gravity * num * num * 0.5f;
                if (YAvoidance != 0f && n < 0.25f)
                    y += (0.5f - (float)Math.Cos(n * 8f * Math.PI) * 0.5f) * YAvoidance;

                var position = new Vector3 { X = x, Y = y, Z = head.Z + CenterZ + JumpDistance * (0.5f - n) };

                var natural = n < 0.125f
                    ? Quat.Slerp(Quat.Identity, Middle, (float)Math.Sin(n * Math.PI * 4))
                    : Quat.Slerp(Middle, End, (float)Math.Sin((n - 0.125f) * Math.PI * 2));

                var target = new Vector3 { X = head.X, Y = head.Y + (position.Y - head.Y) * 0.8f, Z = head.Z };
                var forward = NotePose.Normalize(NotePose.Subtract(position, target));
                var look = Quat.LookRotation(forward, up);

                var rotation = Quat.Lerp(natural, look, Math.Max(0f, Math.Min(1f, n * 2f)));
                up = rotation.Rotate(new Vector3 { Y = 1f });
                return rotation;
            }
        }

        /// <summary>本体の <c>Easing.InOutQuad</c>。</summary>
        private static float InOutQuad(float t)
        {
            return t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;
        }

        /// <summary>本体の <c>_middleRotation</c>。矢印の向きに、乱数表のぶれ ×20° を足したオイラー角。</summary>
        private static Quat MiddleRotation(float endRotation, float noteTime, float endX, float endY)
        {
            // Unity の eulerAngles は 0〜360 に直した値
            float z = endRotation % 360f;
            if (z < 0) z += 360f;
            int index = Math.Abs((int)Math.Round(noteTime * 10f + endX * 2f + endY * 2f, MidpointRounding.ToEven) % RandomRotations.Length);
            var r = RandomRotations[index];
            return Quat.FromEuler(r[0] * 20f, r[1] * 20f, z + r[2] * 20f);
        }

        private static int FirstFrameAtOrAfter(List<ReplayFrame> frames, float time)
        {
            int lo = 0, hi = frames.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (frames[mid].Time < time) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// 本体の「切った向きのずれ」（<c>NoteBasicCutInfoHelper.GetBasicCutInfo</c> の <c>cutDirDeviation</c>、度）。
        /// 刃の向きをノーツの向きで見て、矢印の向き（模型の −Y）からどれだけ回っているか。
        /// </summary>
        public static float CutDirDeviation(Quat noteRotation, Vector3 saberDirection)
        {
            var inverse = new Quat { X = -noteRotation.X, Y = -noteRotation.Y, Z = -noteRotation.Z, W = noteRotation.W };
            var local = inverse.Rotate(saberDirection);
            bool alongZ = Math.Abs(local.Z) > Math.Abs(local.X) * 10f && Math.Abs(local.Z) > Math.Abs(local.Y) * 10f;
            float deviation = alongZ ? 90f : (float)(Math.Atan2(local.Y, local.X) * 180.0 / Math.PI) + 90f;
            return deviation > 180f ? deviation - 360f : deviation;
        }

        /// <summary>
        /// 譜面フォルダの NJS（Info.dat の <c>_noteJumpMovementSpeed</c>）。読めなければ null。
        /// リプレイには NJS が入っていない（ジャンプ距離だけ）ので、ノーツの飛ぶ時間を出すのに譜面から読む。
        /// </summary>
        /// <param name="mode">リプレイのモード（Standard など）。</param>
        /// <param name="difficulty">リプレイの難易度（ExpertPlus など）。モードと難易度を渡したのに合うものが無ければ null（別の譜面）。
        /// どちらかが空なら最初の難易度。</param>
        public static float? NoteJumpSpeedOf(string levelFolder, string mode = null, string difficulty = null)
        {
            try
            {
                if (string.IsNullOrEmpty(levelFolder)) return null;
                string info = Path.Combine(levelFolder, "Info.dat");
                if (!File.Exists(info)) return null;
                var root = JsonReader.Parse(File.ReadAllText(info, Encoding.UTF8)) as Dictionary<string, object>;
                if (root == null) return null;

                bool pick = !string.IsNullOrEmpty(mode) && !string.IsNullOrEmpty(difficulty);
                foreach (var entry in InfoDat.Difficulties(root))
                {
                    object value;
                    if (!entry.Value.TryGetValue("_noteJumpMovementSpeed", out value) || !(value is double njs) || njs <= 0) continue;
                    if (!pick) return (float)njs;
                    entry.Value.TryGetValue("_difficulty", out value);
                    if (string.Equals(entry.Key, mode, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(value as string, difficulty, StringComparison.OrdinalIgnoreCase))
                        return (float)njs;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (FormatException) { }
            return null;
        }

        /// <summary>リプレイの譜面の NJS。譜面フォルダは <paramref name="findFolder"/>（曲名 → フォルダ）で引く。</summary>
        public static float? NoteJumpSpeedOf(Replay replay, Func<string, string> findFolder)
        {
            var info = replay == null ? null : replay.Info;
            if (info == null || string.IsNullOrEmpty(info.SongName) || findFolder == null) return null;
            return NoteJumpSpeedOf(findFolder(info.SongName), info.Mode, info.Difficulty);
        }
    }

    /// <summary>回転（Unity の Quaternion と同じ式）。</summary>
    public struct Quat
    {
        public float X, Y, Z, W;

        public static readonly Quat Identity = new Quat { W = 1f };

        public static Quat operator *(Quat a, Quat b)
        {
            return new Quat
            {
                X = a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                Y = a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
                Z = a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
                W = a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
            };
        }

        private static Quat Axis(float x, float y, float z, float degrees)
        {
            double h = degrees * Math.PI / 360.0;
            float s = (float)Math.Sin(h);
            return new Quat { X = x * s, Y = y * s, Z = z * s, W = (float)Math.Cos(h) };
        }

        /// <summary><c>Quaternion.Euler</c>。Z、X、Y の順に回す。</summary>
        public static Quat FromEuler(float x, float y, float z)
        {
            return Axis(0, 1, 0, y) * Axis(1, 0, 0, x) * Axis(0, 0, 1, z);
        }

        public Vector3 Rotate(Vector3 v)
        {
            // q v q*
            float tx = 2f * (Y * v.Z - Z * v.Y);
            float ty = 2f * (Z * v.X - X * v.Z);
            float tz = 2f * (X * v.Y - Y * v.X);
            return new Vector3
            {
                X = v.X + W * tx + (Y * tz - Z * ty),
                Y = v.Y + W * ty + (Z * tx - X * tz),
                Z = v.Z + W * tz + (X * ty - Y * tx),
            };
        }

        private static float Dot(Quat a, Quat b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W; }

        private Quat Normalized()
        {
            float n = (float)Math.Sqrt(Dot(this, this));
            return n < 1e-9f ? Identity : new Quat { X = X / n, Y = Y / n, Z = Z / n, W = W / n };
        }

        /// <summary><c>Quaternion.Lerp</c>（近い回りで混ぜて長さ 1 に直す）。</summary>
        public static Quat Lerp(Quat a, Quat b, float t)
        {
            float sign = Dot(a, b) < 0 ? -1f : 1f;
            return new Quat
            {
                X = a.X + (b.X * sign - a.X) * t,
                Y = a.Y + (b.Y * sign - a.Y) * t,
                Z = a.Z + (b.Z * sign - a.Z) * t,
                W = a.W + (b.W * sign - a.W) * t,
            }.Normalized();
        }

        /// <summary><c>Quaternion.Slerp</c>（近い回り）。t は 0〜1 に収める。</summary>
        public static Quat Slerp(Quat a, Quat b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            float dot = Dot(a, b);
            if (dot < 0) { b = new Quat { X = -b.X, Y = -b.Y, Z = -b.Z, W = -b.W }; dot = -dot; }
            if (dot > 0.9995f) return Lerp(a, b, t);
            double theta = Math.Acos(dot);
            double s = Math.Sin(theta);
            float wa = (float)(Math.Sin((1 - t) * theta) / s), wb = (float)(Math.Sin(t * theta) / s);
            return new Quat { X = a.X * wa + b.X * wb, Y = a.Y * wa + b.Y * wb, Z = a.Z * wa + b.Z * wb, W = a.W * wa + b.W * wb };
        }

        /// <summary><c>Quaternion.LookRotation(forward, up)</c>。前が <paramref name="forward"/>、上を <paramref name="up"/> にできるだけ合わせる。</summary>
        public static Quat LookRotation(Vector3 forward, Vector3 up)
        {
            var f = NotePose.Normalize(forward);
            var r = NotePose.Cross(up, f);
            float rl = (float)Math.Sqrt(r.X * r.X + r.Y * r.Y + r.Z * r.Z);
            if (rl < 1e-6f) r = NotePose.Cross(new Vector3 { Z = 1f }, f);
            r = NotePose.Normalize(r);
            var u = NotePose.Cross(f, r);
            return FromBasis(r, u, f);
        }

        /// <summary>右・上・前の3本の軸（列）から作る。</summary>
        private static Quat FromBasis(Vector3 r, Vector3 u, Vector3 f)
        {
            float m00 = r.X, m01 = u.X, m02 = f.X;
            float m10 = r.Y, m11 = u.Y, m12 = f.Y;
            float m20 = r.Z, m21 = u.Z, m22 = f.Z;
            float trace = m00 + m11 + m22;
            Quat q;
            if (trace > 0)
            {
                float s = (float)Math.Sqrt(trace + 1f) * 2f;
                q = new Quat { W = 0.25f * s, X = (m21 - m12) / s, Y = (m02 - m20) / s, Z = (m10 - m01) / s };
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                q = new Quat { W = (m21 - m12) / s, X = 0.25f * s, Y = (m01 + m10) / s, Z = (m02 + m20) / s };
            }
            else if (m11 > m22)
            {
                float s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                q = new Quat { W = (m02 - m20) / s, X = (m01 + m10) / s, Y = 0.25f * s, Z = (m12 + m21) / s };
            }
            else
            {
                float s = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
                q = new Quat { W = (m10 - m01) / s, X = (m02 + m20) / s, Y = (m12 + m21) / s, Z = 0.25f * s };
            }
            return q.Normalized();
        }

        /// <summary>いくつかの回転の平均（近い回りにそろえて足し、長さ 1 に直す）。</summary>
        public static Quat Average(IList<Quat> rotations)
        {
            if (rotations.Count == 0) return Identity;
            var first = rotations[0];
            var sum = new Quat();
            foreach (var q in rotations)
            {
                float sign = Dot(first, q) < 0 ? -1f : 1f;
                sum.X += q.X * sign; sum.Y += q.Y * sign; sum.Z += q.Z * sign; sum.W += q.W * sign;
            }
            return sum.Normalized();
        }
    }
}
