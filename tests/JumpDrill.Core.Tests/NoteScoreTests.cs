using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>
    /// 素点は本体の <c>ScoreModel</c> と同じ出し方をする。
    /// 115点の内訳は 振りかぶり70 + 振り抜き30 + 中心15。
    /// </summary>
    public class NoteScoreTests
    {
        private static NoteCutInfo Cut(double before, double after, double distance)
        {
            return new NoteCutInfo
            {
                BeforeCutRating = (float)before,
                AfterCutRating = (float)after,
                CutDistanceToCenter = (float)distance,
            };
        }

        [Fact]
        public void A_perfect_cut_is_a_hundred_and_fifteen()
        {
            var cut = Cut(1, 1, 0);
            Assert.Equal(100, NoteScore.SwingAngle(cut));
            Assert.Equal(15, NoteScore.CutDistance(cut));
            Assert.Equal(115, NoteScore.Total(cut));
        }

        [Theory]
        [InlineData(1.0, 1.0, 100)]
        [InlineData(1.0, 0.0, 70)]   // 振りかぶりだけ
        [InlineData(0.0, 1.0, 30)]   // 振り抜きだけ
        [InlineData(0.5, 0.5, 50)]
        [InlineData(0.0, 0.0, 0)]
        public void The_angle_score_is_seventy_before_plus_thirty_after(double before, double after, int expected)
        {
            Assert.Equal(expected, NoteScore.SwingAngle(Cut(before, after, 0)));
        }

        [Theory]
        // 30 cm 外すと 0。そこから先はマイナスにならない。
        [InlineData(0.00, 15)]
        [InlineData(0.06, 12)]
        [InlineData(0.24, 3)]
        [InlineData(0.30, 0)]
        [InlineData(0.90, 0)]
        public void The_centre_score_falls_off_over_thirty_centimetres(double distance, int expected)
        {
            Assert.Equal(expected, NoteScore.CutDistance(Cut(1, 1, distance)));
        }

        [Fact]
        public void Ratings_outside_the_range_do_not_push_past_the_maximum()
        {
            // リプレイ側が範囲外の値を持っていても満点を超えない。
            Assert.Equal(100, NoteScore.SwingAngle(Cut(1.4, 2.0, 0)));
            Assert.Equal(0, NoteScore.SwingAngle(Cut(-1, -1, 0)));
            Assert.Equal(15, NoteScore.CutDistance(Cut(1, 1, -0.2)));
        }

        [Fact]
        public void The_angle_score_splits_into_the_two_halves_BeatLeader_shows()
        {
            // BeatLeader はリングの外側に 振りかぶり/70・中心/15・振り抜き/30 を出す。
            var cut = Cut(0.5, 0.5, 0);

            Assert.Equal(35, NoteScore.BeforeCut(cut));
            Assert.Equal(15, NoteScore.AfterCut(cut));
            Assert.Equal(NoteScore.SwingAngle(cut), NoteScore.BeforeCut(cut) + NoteScore.AfterCut(cut));
        }

        [Fact]
        public void Time_dependence_is_how_far_the_cut_plane_leans_into_the_depth()
        {
            // BeatLeader の TD。ReplayStatisticUtils::Accuracy が Math.Abs(cutNormal.z) を平均する。
            var cut = Cut(1, 1, 0);
            cut.CutNormal = new Vector3 { X = 0.6f, Y = 0f, Z = -0.8f };

            Assert.Equal(0.8, NoteScore.TimeDependence(cut), 6);
        }

        [Fact]
        public void A_cut_straight_across_has_no_time_dependence()
        {
            var cut = Cut(1, 1, 0);
            cut.CutNormal = new Vector3 { X = 1f, Y = 0f, Z = 0f };

            Assert.Equal(0.0, NoteScore.TimeDependence(cut), 9);
        }

        [Fact]
        public void A_missing_cut_scores_nothing_instead_of_throwing()
        {
            Assert.Equal(0, NoteScore.Total(null));
            Assert.Equal(0.0, NoteScore.TimeDependence(null), 9);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 115 * 1)]                     // ×1 が 1 個
        [InlineData(5, 115 * (1 + 2 * 4))]           // ×2 が 4 個
        [InlineData(13, 115 * (1 + 8 + 4 * 8))]      // ×4 が 8 個
        [InlineData(14, 115 * (1 + 8 + 32 + 8))]     // 14 個目から ×8
        public void The_max_score_follows_the_combo_multiplier(int notes, long expected)
        {
            Assert.Equal(expected, NoteScore.MaxMultipliedScore(notes));
        }

        [Fact]
        public void The_multiplier_goes_up_before_it_is_applied()
        {
            // 本体は倍率を上げてからそのノーツに掛ける。
            var combo = new ComboMultiplier();
            Assert.Equal(1, combo.Hit());
            Assert.Equal(2, combo.Hit());
            for (int i = 0; i < 11; i++) combo.Hit();
            Assert.Equal(8, combo.Hit());   // 14 個目
        }

        [Fact]
        public void A_break_drops_the_multiplier_one_step()
        {
            var combo = new ComboMultiplier();
            for (int i = 0; i < 14; i++) combo.Hit();

            combo.Break();
            Assert.Equal(4, combo.Multiplier);

            // ×4 から ×8 へは、また 8 個続けて切る。8 個目から ×8 が掛かる。
            for (int i = 0; i < 7; i++) Assert.Equal(4, combo.Hit());
            Assert.Equal(8, combo.Hit());
        }
    }

    /// <summary>
    /// 自動プレイの見分け。ボットの記録を人の記録と並べると、
    /// 再現性がほぼ満点で出るので上位の行として読めてしまう。
    /// </summary>
    public class AutoplayTests
    {
        private static ReplayInfo Info(string tracking, string hmd, string controller)
        {
            return new ReplayInfo { TrackingSystem = tracking, Hmd = hmd, Controller = controller };
        }

        [Fact]
        public void Autoplay_leaves_every_tracking_field_unknown()
        {
            Assert.True(Info("Unknown", "Unknown", "Unknown").LooksLikeAutoplay);
        }

        [Fact]
        public void A_real_play_records_the_hardware()
        {
            Assert.False(Info("OpenXR", "\"Oculus\"\"Meta Quest 3\"",
                              "Oculus Touch Controller OpenXR").LooksLikeAutoplay);
        }

        [Theory]
        // 1つでも埋まっていれば人が動かしている。
        [InlineData("OpenXR", "Unknown", "Unknown")]
        [InlineData("Unknown", "Meta Quest 3", "Unknown")]
        [InlineData("Unknown", "Unknown", "Oculus Touch Controller OpenXR")]
        public void One_known_field_is_enough_to_call_it_a_real_play(string t, string h, string c)
        {
            Assert.False(Info(t, h, c).LooksLikeAutoplay);
        }

        [Fact]
        public void Empty_fields_count_as_unknown()
        {
            // 古い BeatLeader は空文字で書くことがある。
            Assert.True(Info("", null, "unknown").LooksLikeAutoplay);
        }
    }
}
