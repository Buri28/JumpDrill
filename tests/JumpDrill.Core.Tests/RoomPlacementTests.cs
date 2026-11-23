using System;
using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Tests
{
    public class RoomPlacementTests
    {
        [Theory]
        [InlineData(null, RoomFacing.Front)]
        [InlineData("", RoomFacing.Front)]
        [InlineData("left", RoomFacing.LeftPanel)]
        [InlineData(" Left ", RoomFacing.LeftPanel)]
        [InlineData("right", RoomFacing.Front)]
        public void ParsesSetting(string value, RoomFacing expected)
        {
            Assert.Equal(expected, RoomPlacement.Parse(value));
        }

        [Theory]
        [InlineData(RoomFacing.Front)]
        [InlineData(RoomFacing.LeftPanel)]
        public void SettingRoundTrips(RoomFacing facing)
        {
            Assert.Equal(facing, RoomPlacement.Parse(RoomPlacement.ToSetting(facing)));
        }

        [Fact]
        public void FrontKeepsTheOldPlacement()
        {
            var pose = RoomPlacement.Place(1.5f, RoomFacing.Front, -3f, 2f, -55f, 2f);
            Assert.Equal(0f, pose.X);
            Assert.Equal(1.5f, pose.Z);
            Assert.Equal(0f, pose.YawDegrees);
            Assert.Equal(1.5f, pose.Forward);
        }

        [Fact]
        public void LeftWithoutPanelFallsBackToFront()
        {
            var pose = RoomPlacement.Place(1f, RoomFacing.LeftPanel, null, null, null, 2f);
            Assert.Equal(0f, pose.X);
            Assert.Equal(1f, pose.Z);
            Assert.Equal(0f, pose.YawDegrees);
        }

        [Fact]
        public void LeftTurnsToThePanelYaw()
        {
            // 左の画面は Y 回転 305°（= -55°）で、原点から見てその向き 4 m 先にある
            double rad = -55 * Math.PI / 180;
            float px = (float)(Math.Sin(rad) * 4), pz = (float)(Math.Cos(rad) * 4);
            var pose = RoomPlacement.Place(1f, RoomFacing.LeftPanel, px, pz, 305f, 2f);

            Assert.Equal(-55f, pose.YawDegrees, 3);
            Assert.Equal(1f, pose.Forward, 3);
            Assert.Equal((float)Math.Sin(rad), pose.X, 3);
            Assert.Equal((float)Math.Cos(rad), pose.Z, 3);
        }

        [Fact]
        public void LeftKeepsTheSwingsBeforeThePanel()
        {
            // パネルまで 4 m、振りの奥の端が 2 m。ずらせるのは 4 - 0.3 - 2 = 1.7 m まで
            var pose = RoomPlacement.Place(2f, RoomFacing.LeftPanel, -4f, 0f, -90f, 2f);
            Assert.Equal(1.7f, pose.Forward, 3);
            Assert.Equal(-1.7f, pose.X, 3);
            Assert.Equal(0f, pose.Z, 3);

            // 前へずらさなくても届くなら、プレイした位置より手前には寄せない
            var close = RoomPlacement.Place(1f, RoomFacing.LeftPanel, -2f, 0f, -90f, 2f);
            Assert.Equal(0f, close.Forward, 3);
        }
    }
}
