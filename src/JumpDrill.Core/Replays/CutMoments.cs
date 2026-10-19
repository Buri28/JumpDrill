using System;

namespace JumpDrill.Replays
{
    /// <summary>
    /// 切った瞬間の刃。リプレイに記録された切った時刻はフレームの刻みで、
    /// その時刻の刃は打点（<c>cutPoint</c>）から最大 0.6 m ほど離れている（実測）。
    /// 切る瞬間の先端は 1 フレーム（約 11 ms）で 0.2〜0.5 m 進むため。
    /// </summary>
    /// <remarks>
    /// 前後 ±30 ms を 1 ms 刻みで見て、打点が刃の線にいちばん近づく時刻を探すと、
    /// 打点と刃の距離は平均 3 cm まで縮む。決まった量のずれではない
    /// （全部を同じだけずらすと、かえって離れる）ので、1回ごとに探す。
    /// </remarks>
    public static class CutMoments
    {
        /// <summary>探す幅 (ms)。記録された時刻の前後。</summary>
        private const int WindowMs = 30;

        /// <summary>打点と刃の線がこれより離れたままなら、探した時刻を使わない (m)。実測ではほとんどが数 cm。</summary>
        private const double MaxDistance = 0.2;

        /// <summary>打点が刃の線にいちばん近づく時刻 (秒)。切っていなければ記録された時刻。</summary>
        public static float Find(Replay replay, ReplayNote note)
        {
            if (note.Cut == null || replay.Frames.Count == 0) return note.EventTime;

            // 打点が入っていないリプレイ（作ったデータなど）は探しようがない
            var point = note.Cut.CutPoint;
            if (point.X == 0 && point.Y == 0 && point.Z == 0) return note.EventTime;

            // 記録の時刻に近い方から見て、同じ近さなら近い時刻を取る
            float best = note.EventTime;
            double bestDistance = double.MaxValue;
            for (int step = 0; step <= WindowMs * 2; step++)
            {
                int ms = (step + 1) / 2 * (step % 2 == 0 ? 1 : -1);
                float time = note.EventTime + ms / 1000f;
                Vector3 hilt, tip;
                BladeAt(replay, note.Cut.SaberType, time, out hilt, out tip);
                double distance = DistanceToLine(point, hilt, tip);
                if (distance < bestDistance) { bestDistance = distance; best = time; }
            }

            // それでも刃に近づかないなら、打点と刃が食い違っている。記録の時刻のままにする
            return bestDistance <= MaxDistance ? best : note.EventTime;
        }

        /// <summary>その時刻の刃（手元と先端）。前後のフレームの間を直線で補う。</summary>
        public static void BladeAt(Replay replay, int saberType, float time, out Vector3 hilt, out Vector3 tip)
        {
            var frames = replay.Frames;
            int lo = 0, hi = frames.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (frames[mid].Time < time) lo = mid + 1; else hi = mid;
            }

            var after = frames[lo].Saber(saberType);
            if (lo == 0 || frames[lo].Time < time)
            {
                hilt = after.Position;
                tip = after.Tip;
                return;
            }

            var before = frames[lo - 1].Saber(saberType);
            float span = frames[lo].Time - frames[lo - 1].Time;
            float t = span > 1e-6f ? (time - frames[lo - 1].Time) / span : 1f;
            hilt = Lerp(before.Position, after.Position, t);
            tip = Lerp(before.Tip, after.Tip, t);
        }

        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        {
            return new Vector3 { X = a.X + (b.X - a.X) * t, Y = a.Y + (b.Y - a.Y) * t, Z = a.Z + (b.Z - a.Z) * t };
        }

        /// <summary>点 <paramref name="p"/> から、<paramref name="a"/> と <paramref name="b"/> を通る直線までの距離。</summary>
        private static double DistanceToLine(Vector3 p, Vector3 a, Vector3 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length < 1e-9) return Vector3.Distance(p, a);
            double px = p.X - a.X, py = p.Y - a.Y, pz = p.Z - a.Z;
            double cx = py * dz - pz * dy, cy = pz * dx - px * dz, cz = px * dy - py * dx;
            return Math.Sqrt(cx * cx + cy * cy + cz * cz) / length;
        }
    }
}
