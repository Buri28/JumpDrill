using JumpDrill.Model;
using JumpDrill.Parsing;
using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Core.Tests
{
    /// <summary>
    /// 譜面名の組み立てと読み戻し。
    ///
    /// 名前はリプレイから譜面へ辿れる唯一の手がかりなので、
    /// 「同じドリルなら必ず同じ ID」が崩れると記録の履歴がその場で割れる。
    /// </summary>
    public class DrillNamingTests
    {
        private static DrillOptions Options(string seq, double intervalMs,
            HandPattern pattern = HandPattern.Sync, DirectionMode direction = DirectionMode.Axis,
            double seconds = 30.0, int sets = 1)
        {
            return new DrillOptions
            {
                Sequences = SequenceParser.ParseAll(seq),
                IntervalMs = intervalMs,
                HandPattern = pattern,
                Direction = direction,
                DurationSeconds = seconds,
                Sets = sets,
            };
        }

        [Fact]
        public void The_name_carries_the_id_the_sequences_and_the_bpm()
        {
            string name = DrillNaming.Compose(Options("R:8>b,L:5>a", 100));

            Assert.Equal("Drill R8b L5a axis 300 BPM 30s", DrillNaming.StripId(name));
            Assert.Equal(DrillNaming.IdLength, DrillNaming.ExtractId(name).Length);
        }

        [Fact]
        public void The_length_is_part_of_the_name_and_of_the_id()
        {
            var half = DrillNaming.Compose(Options("R8b", 100, seconds: 30));
            var full = DrillNaming.Compose(Options("R8b", 100, seconds: 60));

            Assert.Equal("Drill R8b axis 300 BPM 30s", DrillNaming.StripId(half));
            Assert.Equal("Drill R8b axis 300 BPM 60s", DrillNaming.StripId(full));

            // 30 秒と 60 秒は別のドリル。同じ行に積んではいけない。
            Assert.NotEqual(DrillNaming.ExtractId(half), DrillNaming.ExtractId(full));
        }

        [Fact]
        public void The_set_count_shows_only_when_it_repeats()
        {
            // 既定の 1 で書くと、今あるほとんどの名前が意味なく伸びる。
            Assert.Equal("Drill R8b axis 300 BPM 30s", DrillNaming.StripId(DrillNaming.Compose(Options("R8b", 100))));

            var three = DrillNaming.Compose(Options("R8b", 100, sets: 3));
            Assert.Equal("Drill R8b axis 300 BPM 30s x3", DrillNaming.StripId(three));

            Assert.NotEqual(
                DrillNaming.ExtractId(DrillNaming.Compose(Options("R8b", 100))),
                DrillNaming.ExtractId(three));
        }

        [Fact]
        public void A_length_with_a_fraction_still_reads_back()
        {
            Assert.Equal("Drill R8b axis 300 BPM 22.5s",
                DrillNaming.StripId(DrillNaming.Compose(Options("R8b", 100, seconds: 22.5))));
        }

        [Fact]
        public void The_same_settings_always_give_the_same_id()
        {
            Assert.Equal(
                DrillNaming.ExtractId(DrillNaming.Compose(Options("R:8>b,L:5>a", 100))),
                DrillNaming.ExtractId(DrillNaming.Compose(Options("R:8>b,L:5>a", 100))));
        }

        [Theory]
        // 遷移、向き、速さ。どれが変わっても別の譜面。
        [InlineData("R:4>b,L:5>a", 100, DirectionMode.Axis)]
        [InlineData("R:8>b,L:5>a", 120, DirectionMode.Axis)]
        [InlineData("R:8>b,L:5>a", 100, DirectionMode.Dot)]
        public void A_different_setting_gives_a_different_id(string seq, double interval, DirectionMode direction)
        {
            string baseline = DrillNaming.ExtractId(DrillNaming.Compose(Options("R:8>b,L:5>a", 100)));

            Assert.NotEqual(baseline,
                DrillNaming.ExtractId(DrillNaming.Compose(Options(seq, interval, direction: direction))));
        }

        [Fact]
        public void Alternating_hands_halve_the_bpm_because_each_hand_waits_its_turn()
        {
            // 同時・片手ずつは軸の間隔がそのまま片手の間隔。
            Assert.Equal(100.0, DrillNaming.HandIntervalMs(Options("R:8>b,L:5>a", 100)));

            // 両手交互は自分の番が2つおき。片手から見れば 200ms＝150 BPM。
            var alt = Options("R:8>b,L:5>a", 100, HandPattern.Alternate);
            Assert.Equal(200.0, DrillNaming.HandIntervalMs(alt));
            Assert.Equal("Drill R8b L5a axis 150 BPM 30s", DrillNaming.StripId(DrillNaming.Compose(alt)));
        }

        [Fact]
        public void Names_written_before_the_switch_to_bpm_still_read_back()
        {
            // 以前は同じ値を EBPM と書いていた。ID は変わらないので記録は続くが、速さも読めること。
            Assert.Equal(100.0, SwingAnalyzer.DeclaredHandInterval("[Q7K3M] Drill R8b L5a axis 300 EBPM 30s"), 6);
            Assert.Equal(100.0, SwingAnalyzer.DeclaredHandInterval("[Q7K3M] Drill R8b L5a axis 300 BPM 30s"), 6);
        }

        [Fact]
        public void The_bpm_in_the_name_reads_back_as_the_hand_interval()
        {
            // 171ms のような割り切れない値でも、表示 1 桁で 0.1ms も狂わずに戻る。
            foreach (double interval in new[] { 85.0, 100.0, 110.0, 171.0, 300.0 })
            {
                string name = DrillNaming.Compose(Options("R:8>b", interval));
                Assert.Equal(interval, SwingAnalyzer.DeclaredHandInterval(name), 1);
            }
        }

        [Fact]
        public void Records_from_before_the_id_land_on_the_same_row()
        {
            // 旧い名前が持っているのはノーツ間隔。片手の間隔はリプレイの実測から渡す。
            string legacy = "Drill R:8>b L:5>a axis 100ms";
            string body = DrillNaming.LegacyBody(legacy);

            Assert.Equal("R:8>b L:5>a axis", body);

            // 昔の名前どうしは、記法が違っても1つにまとまる。
            Assert.Equal(
                DrillNaming.LegacyId("R8b L5a axis", 100.0),
                DrillNaming.LegacyId(body, 100.0));

            // ただし当時の名前は尺を持っていないので、いま作る譜面とは別の行になる。
            Assert.NotEqual(
                DrillNaming.ExtractId(DrillNaming.Compose(Options("R8b,L5a", 100))),
                DrillNaming.LegacyId(body, 100.0));
        }

        [Fact]
        public void Changing_how_the_notation_is_written_does_not_split_the_history()
        {
            // 記法は R:8>b から R8b に変わった。字面で鍵を作ると、
            // 表記を変えた所で同じドリルの記録が2つに割れる。
            Assert.Equal(
                DrillNaming.MakeId("R8b L5a axis", 100.0, 30.0, 1),
                DrillNaming.MakeId("R:8>b L:5>a axis", 100.0, 30.0, 1));
        }

        [Fact]
        public void A_name_given_by_hand_is_left_alone()
        {
            // --name の文字は遷移ではないので、読み直しの対象にしない。
            Assert.Equal(
                DrillNaming.MakeId("my drill", 100.0, 30.0, 1),
                DrillNaming.MakeId("my drill", 100.0, 30.0, 1));

            Assert.NotEqual(
                DrillNaming.MakeId("my drill", 100.0, 30.0, 1),
                DrillNaming.MakeId("my drill", 200.0, 30.0, 1));
        }

        [Fact]
        public void The_rung_number_on_an_old_name_is_dropped_too()
        {
            Assert.Equal("R:8>b L:5>a axis", DrillNaming.LegacyBody("Drill R:8>b L:5>a axis 100ms #02"));
        }

        [Theory]
        [InlineData("Cendrillon")]
        [InlineData("Drill R:8>b L:5>a axis 300 EBPM")]   // 新しい名前は ms 表記ではない
        [InlineData("")]
        public void A_name_that_is_not_the_old_form_has_no_legacy_body(string songName)
        {
            Assert.Null(DrillNaming.LegacyBody(songName));
        }

        [Theory]
        [InlineData("Drill R:8>b axis 300 EBPM")]         // ID が付いていない
        [InlineData("[Q7K] Drill R:8>b axis 300 EBPM")]   // 桁が足りない
        [InlineData("[QUILO] Drill R:8>b axis 300 EBPM")] // 使わない文字が入っている
        [InlineData("Cendrillon")]
        public void Only_a_well_formed_prefix_counts_as_an_id(string songName)
        {
            Assert.Null(DrillNaming.ExtractId(songName));
            Assert.Equal(songName, DrillNaming.StripId(songName));
        }

        [Fact]
        public void The_id_uses_only_characters_that_cannot_be_misread()
        {
            // Crockford の base32。I L O U は 1 0 と紛れるので使わない。
            for (double interval = 60; interval <= 400; interval += 1)
            {
                string id = DrillNaming.ExtractId(DrillNaming.Compose(Options("R:8>b,L:5>a", interval)));

                Assert.Equal(DrillNaming.IdLength, id.Length);
                Assert.DoesNotContain(id, c => "ILOU".IndexOf(c) >= 0);
            }
        }
    }
}
