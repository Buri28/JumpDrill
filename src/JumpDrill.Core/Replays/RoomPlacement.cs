using System;

namespace JumpDrill.Replays
{
    /// <summary>空間に出す振り（3D 表示）をどちらに向けて置くか。</summary>
    public enum RoomFacing
    {
        /// <summary>正面（プレイしたときと同じ向き）。</summary>
        Front,

        /// <summary>左のパネル（メニューの左の画面）の方。</summary>
        LeftPanel,
    }

    /// <summary>空間に出す振りの置き方。床の上の位置 (X, Z) と、上から見た回転（度、右回りが正。Unity の Y 回転と同じ）。</summary>
    public struct RoomPose
    {
        public float X;
        public float Z;
        public float YawDegrees;

        /// <summary>実際に前へずらした量 (m)。パネルに重なるときは頼まれた量より小さくなる。</summary>
        public float Forward;
    }

    /// <summary>
    /// 空間に出す振りの置き方を決める。正面ならプレイしたときの位置から前へずらすだけ。
    /// 左パネル側なら、プレイヤーの立ち位置（原点）を軸に、左パネルが向いている向きまで丸ごと回す。
    /// 左を向くと、正面を見ていたときと同じ形で正対して見える。
    /// </summary>
    public static class RoomPlacement
    {
        /// <summary>設定に書く値。正面は空。</summary>
        public const string LeftPanelSetting = "left";

        /// <summary>振りの奥の端とパネルの間に空ける距離 (m)。</summary>
        public const float PanelMargin = 0.3f;

        public static RoomFacing Parse(string value)
        {
            return string.Equals(value?.Trim(), LeftPanelSetting, StringComparison.OrdinalIgnoreCase) ? RoomFacing.LeftPanel : RoomFacing.Front;
        }

        public static string ToSetting(RoomFacing facing)
        {
            return facing == RoomFacing.LeftPanel ? LeftPanelSetting : string.Empty;
        }

        /// <summary>
        /// 置き方を求める。
        /// </summary>
        /// <param name="forward">前へずらす量 (m)。</param>
        /// <param name="facing">向き。</param>
        /// <param name="panelX">左パネルの中心の X (m)。分からなければ null（正面に置く）。</param>
        /// <param name="panelZ">左パネルの中心の Z (m)。</param>
        /// <param name="panelYawDegrees">左パネルの Y 回転（度）。パネルを見るときに向く向き。</param>
        /// <param name="sceneFarZ">振りの奥の端（ずらす前、プレイしたときの位置での Z、m）。</param>
        public static RoomPose Place(float forward, RoomFacing facing, float? panelX, float? panelZ, float? panelYawDegrees, float sceneFarZ)
        {
            if (facing != RoomFacing.LeftPanel || panelX == null || panelZ == null || panelYawDegrees == null
                || float.IsNaN(panelX.Value) || float.IsNaN(panelZ.Value) || float.IsNaN(panelYawDegrees.Value))
                return new RoomPose { X = 0f, Z = forward, YawDegrees = 0f, Forward = forward };

            float yaw = Normalize(panelYawDegrees.Value);
            double rad = yaw * Math.PI / 180.0;
            float dirX = (float)Math.Sin(rad);
            float dirZ = (float)Math.Cos(rad);

            // パネルまでの、向いた方向の距離。振りの奥の端がそこを越えないよう、ずらす量を抑える
            float used = forward;
            float toPanel = panelX.Value * dirX + panelZ.Value * dirZ;
            if (!float.IsNaN(sceneFarZ) && toPanel > 0f)
            {
                float limit = toPanel - PanelMargin - sceneFarZ;
                if (used > limit) used = Math.Max(0f, limit);
            }

            return new RoomPose { X = dirX * used, Z = dirZ * used, YawDegrees = yaw, Forward = used };
        }

        /// <summary>-180 より大きく 180 以下にそろえる。</summary>
        private static float Normalize(float degrees)
        {
            float d = degrees % 360f;
            if (d > 180f) d -= 360f;
            if (d <= -180f) d += 360f;
            return d;
        }
    }
}
