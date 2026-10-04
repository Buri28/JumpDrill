using System;
using System.Linq;
using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Core.Tests
{
    /// <summary>
    /// 左右をまとめて1つの数字にするところ。
    ///
    /// 手ごとに均等に平均する。振りの本数で重み付けすると、
    /// 片手ずつのドリルで<b>崩れてミスした手ほど重みが軽くなる</b>。
    /// 実測でも左 32 本 / 右 272 本のとき左の重みが 10.5% しか無く、
    /// 再現性 左 26.5 / 右 87.7 が全体 81.3 と出ていた。
    /// </summary>
    public class CombineHandsTests
    {
        /// <summary>
        /// 左右で<b>本数も乱れ方も違う</b>ドリルを組み立てる。
        /// 右は多くて綺麗、左は少なくて雑、という偏りを作る。
        /// </summary>
        private static Replay BuildTwoHands(int rightSwings, int leftSwings, double rightJitter, double leftJitter,
            int missAt = -1, int missAlsoAt = -1)
        {
            var random = new Random(7);
            var replay = new Replay();
            replay.Info.SongName = "Drill R:8>b axis 300ms";

            const float Interval = 0.3f;
            float time = 1.0f;

            // 最初のノーツへ入ってくるぶんの助走。実際のリプレイでもカウントインの間の
            // フレームが入っているので、1本目も振りとして切り出せる。
            for (int f = 0; f < 8; f++)
            {
                float progress = f / 8f;
                replay.Frames.Add(new ReplayFrame
                {
                    Time = time - Interval + Interval * progress,
                    Right = At(progress * 0.4f, random, rightJitter),
                    Left = At(progress * 0.4f, random, leftJitter),
                });
            }

            for (int i = 0; i < rightSwings; i++)
            {
                bool toB = i % 2 == 0;
                int lineIndex = toB ? 2 : 3;
                int lineLayer = toB ? 0 : 1;

                AddNote(replay, lineIndex, lineLayer, colorType: 1, time: time, missed: i == missAt || i == missAlsoAt);
                if (i < leftSwings) AddNote(replay, lineIndex, lineLayer, colorType: 0, time: time);

                for (int f = 0; f < 8; f++)
                {
                    float progress = f / 8f;
                    float x = toB ? 0.4f - progress * 0.4f : progress * 0.4f;

                    replay.Frames.Add(new ReplayFrame
                    {
                        Time = time + Interval * f / 8f,
                        Right = At(x, random, rightJitter),
                        Left = At(x, random, leftJitter),
                    });
                }

                time += Interval;
            }

            return replay;
        }

        /// <summary>取り逃しが判定されるまでの遅れ（秒）。ノーツの間隔 0.3 秒より長い。</summary>
        private const float MissJudgedLate = 0.45f;

        private static void AddNote(Replay replay, int lineIndex, int lineLayer, int colorType, float time,
            bool missed = false)
        {
            replay.Notes.Add(new ReplayNote
            {
                NoteId = lineIndex * 1000 + lineLayer * 100 + colorType * 10 + (lineLayer == 0 ? 6 : 5),
                SpawnTime = time,

                // 取り逃しはノーツが通り過ぎてから判定されるので、記録の時刻は次のノーツより後になる
                // （本物のリプレイと同じ）。並びをこの時刻で作ると、ミスの前後がつながってしまう
                EventTime = missed ? time + MissJudgedLate : time,
                EventType = missed ? NoteEventType.Miss : NoteEventType.Good,
                Cut = missed ? null : new NoteCutInfo
                {
                    SaberType = colorType,
                    CutDistanceToCenter = 0.05f,
                },
            });
        }

        /// <summary>
        /// 乱れは Y だけに乗せる。振りは X 方向に進むので、
        /// X にも同じだけ乗せると<b>線に沿って動くだけ</b>になり、直線からのずれが出ない。
        /// </summary>
        private static ReplayTransform At(float x, Random random, double jitter)
        {
            float wobble = (float)((random.NextDouble() - 0.5) * jitter);
            return new ReplayTransform
            {
                Position = new Vector3 { X = x, Y = 1.2f + wobble, Z = 0.5f },
            };
        }

        [Fact]
        public void The_hands_count_the_same_even_when_one_swung_far_less()
        {
            // 右 80 本きれい / 左 10 本ぶれぶれ。本数で重み付けすると左は 11% しか効かない。
            var score = SwingAnalyzer.Analyze(BuildTwoHands(80, 10, rightJitter: 0.004, leftJitter: 1.2));

            double left = score.ScoreFor(0).Value;
            double right = score.ScoreFor(1).Value;

            Assert.True(left < right - 20, "左が崩れている前提のテスト: 左 " + left + " / 右 " + right);
            Assert.Equal((left + right) / 2.0, score.Score, 6);
        }

        [Fact]
        public void The_swing_count_no_longer_pulls_the_result()
        {
            var score = SwingAnalyzer.Analyze(BuildTwoHands(80, 10, rightJitter: 0.004, leftJitter: 1.2));

            int total = score.PerHand.Sum(h => h.SwingCount);
            double weighted = score.PerHand.Sum(h => h.Score * h.SwingCount) / total;

            // 本数で重み付けした値とは、はっきり別の数字になっていること。
            Assert.True(Math.Abs(weighted - score.Score) > 5.0,
                "重み付けの違いが出ていない: 均等 " + score.Score + " / 本数 " + weighted);

            // 均等なら、崩れている左の側へ寄る。
            Assert.True(score.Score < weighted);
        }

        [Fact]
        public void The_raw_spread_is_averaged_the_same_way()
        {
            var score = SwingAnalyzer.Analyze(BuildTwoHands(80, 10, rightJitter: 0.004, leftJitter: 1.2));

            Assert.Equal(score.PerHand.Average(h => h.TrajectorySpread), score.TrajectorySpread, 9);
            Assert.Equal(score.PerHand.Average(h => h.PathScatter), score.PathScatter, 9);
            Assert.Equal(score.PerHand.Average(h => h.AngleErrorMean), score.AngleErrorMean, 9);
            Assert.Equal(score.PerHand.Average(h => h.TimeDeviationMean), score.TimeDeviationMean, 9);
        }

        [Fact]
        public void The_beat_saber_score_stays_a_per_note_average()
        {
            // 総合 /115 と TD は BeatLeader の成績画面と突き合わせる欄。
            // あちらは全ノーツを平均しているので、手ごとに均してはいけない。
            var score = SwingAnalyzer.Analyze(BuildTwoHands(80, 10, rightJitter: 0.004, leftJitter: 1.2));

            int total = score.PerHand.Sum(h => h.SwingCount);

            Assert.Equal(score.PerHand.Sum(h => h.AverageCut * h.SwingCount) / total, score.AverageCut, 9);
            Assert.Equal(score.PerHand.Sum(h => h.TimeDependence * h.SwingCount) / total, score.TimeDependence, 9);
        }

        [Fact]
        public void The_ranking_value_counts_how_many_swings_landed()
        {
            var score = SwingAnalyzer.Analyze(BuildTwoHands(80, 10, rightJitter: 0.004, leftJitter: 1.2));

            // 再現スコア = 手ごとの「再現性 % × 振り数」の合計。
            Assert.Equal(score.PerHand.Sum(h => h.Score * h.SwingCount), score.ReproducibilityScore, 6);

            // 再現スコア % = 再現スコア / (100 × 振れるはずの数) × 100。
            int swingable = score.PerHand.Sum(h => h.SwingableCount);
            Assert.Equal(score.ReproducibilityScore / (100.0 * swingable) * 100.0, score.ReproducibilityPercent, 9);

            // 手ごとに見れば、綺麗さだけの再現性より必ず低いか同じ。
            // 振り数はノーツ数を超えられないので、振れなかったぶんが引かれる。
            foreach (var hand in score.PerHand)
            {
                // 循環なので最初のノーツにも前の点が決まっている。満点はノーツ数と同じ。
                Assert.Equal(hand.NoteCount, hand.SwingableCount);
                Assert.True(hand.SwingCount <= hand.SwingableCount);
                Assert.True(hand.ReproducibilityPercent <= hand.Score);
            }

            // 全体では均等平均の再現性と大小が決まらない。
            // 再現性は手ごとに均等、こちらはノーツ数の割合なので、別物として見る。
        }

        [Fact]
        public void Swinging_less_can_no_longer_rank_higher()
        {
            // 同じ譜面（左右 80 本ずつ）を叩いた2つの記録。
            // 綺麗だが左を 6 本で切り上げたものと、少し粗いが全部振り切ったもの。
            var few = SwingAnalyzer.Analyze(BuildTwoHands(80, 6, rightJitter: 0.004, leftJitter: 0.004));
            var many = SwingAnalyzer.Analyze(BuildTwoHands(80, 80, rightJitter: 0.05, leftJitter: 0.05));

            SwingAnalyzer.ApplyMapNotes(few, leftNotes: 80, rightNotes: 80);
            SwingAnalyzer.ApplyMapNotes(many, leftNotes: 80, rightNotes: 80);

            // 再現 % そのものだと、6 本しか振っていない方が上に来てしまう。
            Assert.True(few.Score > many.Score);

            // 順位に使う値ではひっくり返る。
            Assert.True(many.ReproducibilityPercent > few.ReproducibilityPercent);
        }

        [Fact]
        public void The_accuracy_counts_a_miss_as_zero()
        {
            var score = SwingAnalyzer.Analyze(BuildTwoHands(80, 10, rightJitter: 0.004, leftJitter: 1.2));
            var right = score.Hand(1);

            // 総合 /115 は切れたノーツだけの平均。精度はミスも数える。
            Assert.Equal(
                right.AverageCut * right.GoodCount / (115.0 * right.NoteCount) * 100.0,
                right.Accuracy, 9);

            Assert.Equal(right.GoodCount + right.MissCount, right.NoteCount);
        }

        [Fact]
        public void The_ratios_are_summed_before_dividing_not_averaged_per_hand()
        {
            var score = SwingAnalyzer.Analyze(BuildTwoHands(80, 10, rightJitter: 0.004, leftJitter: 1.2));

            // ノーツ数の違う手を手ごとに均すと割合が壊れる。足してから割る。
            double perHandMean = score.PerHand.Average(h => h.ReproducibilityPercent);
            Assert.NotEqual(perHandMean, score.ReproducibilityPercent, 3);

            int swingable = score.PerHand.Sum(h => h.SwingableCount);
            Assert.Equal(score.PerHand.Sum(h => h.ReproducibilityScore) / swingable, score.ReproducibilityPercent, 9);
        }

        [Fact]
        public void A_clean_run_with_no_misses_reaches_a_hundred()
        {
            // ミス 0 で全部振り切れば、再現スコア % は再現性 % と同じ値になる。
            // 引かれるものが何も無いため。再現性が 100 ならここも 100 になる。
            //
            // ノーツ数をそのまま分母にしていた頃は「最初の1本は振りにならない」ぶんが
            // 常に引かれ、自動プレイの完全な記録でも 176 本中 175 本で 99.4% だった。
            var score = SwingAnalyzer.Analyze(BuildTwoHands(80, 80, rightJitter: 0.0, leftJitter: 0.0));

            Assert.Equal(0, score.MissCount);

            foreach (var hand in score.PerHand)
            {
                Assert.Equal(hand.SwingableCount, hand.SwingCount);
                Assert.Equal(hand.Score, hand.ReproducibilityPercent, 9);
            }

            // 左右のノーツ数が同じなら、まとめた値も一致する。
            Assert.Equal(score.Score, score.ReproducibilityPercent, 9);
        }

        [Theory]
        // 譜面ファイルからノーツを数える。_events にも _type があるので巻き込まない。
        [InlineData("{\"_events\":[{\"_type\":0},{\"_type\":1}],\"_notes\":[{\"_type\":0},{\"_type\":1},{\"_type\":1}]}", 1, 2)]
        // 爆弾 (_type 3) は振る相手ではない。
        [InlineData("{\"_notes\":[{\"_type\":1},{\"_type\":3},{\"_type\":0}]}", 1, 1)]
        // 間隔が空いていても読める。
        [InlineData("{ \"_notes\" : [ { \"_type\" : 1 } ] }", 0, 1)]
        public void The_map_notes_are_counted_per_colour(string text, int left, int right)
        {
            JumpDrill.Output.MapNotes.Counts counts;
            Assert.True(JumpDrill.Output.MapNotes.TryCountText(text, out counts));

            Assert.Equal(left, counts.Left);
            Assert.Equal(right, counts.Right);
        }

        [Theory]
        [InlineData("{\"_notes\":[]}")]
        [InlineData("{\"_events\":[{\"_type\":1}]}")]
        [InlineData("")]
        public void A_file_with_no_notes_is_rejected(string text)
        {
            JumpDrill.Output.MapNotes.Counts counts;
            Assert.False(JumpDrill.Output.MapNotes.TryCountText(text, out counts));
        }

        [Fact]
        public void The_map_decides_the_maximum_not_the_replay()
        {
            // 右だけ振って途中でやめた記録。左のノーツはリプレイに1件も残らない。
            var score = SwingAnalyzer.Analyze(BuildTwoHands(80, 0, rightJitter: 0.0, leftJitter: 0.0));

            Assert.Single(score.PerHand);
            double alone = score.ReproducibilityPercent;

            // 譜面は左右 80 本ずつだった、と入れ直す。
            SwingAnalyzer.ApplyMapNotes(score, leftNotes: 80, rightNotes: 80);

            Assert.Equal(160, score.NoteCount);
            Assert.Equal(160, score.SwingableCount);          // 最初の1本も振りとして数える
            Assert.Equal(80, score.MapNotesLeft);

            // 振っていない左のぶんが満点に入るので、およそ半分に落ちる。
            Assert.True(score.ReproducibilityPercent < alone / 1.9,
                "片手ぶんが満点に入っていない: " + alone + " → " + score.ReproducibilityPercent);
        }

        [Fact]
        public void A_run_cut_short_no_longer_scores_as_if_it_finished()
        {
            // 右 40 本で切り上げた記録。譜面は右 80 本あった。
            var score = SwingAnalyzer.Analyze(BuildTwoHands(40, 0, rightJitter: 0.0, leftJitter: 0.0));
            double asRecorded = score.ReproducibilityPercent;

            SwingAnalyzer.ApplyMapNotes(score, leftNotes: 0, rightNotes: 80);

            Assert.True(score.ReproducibilityPercent < asRecorded);
            Assert.Equal(score.ReproducibilityScore / 80.0, score.ReproducibilityPercent, 9);
        }

        [Fact]
        public void The_accuracy_uses_the_map_total_too()
        {
            var score = SwingAnalyzer.Analyze(BuildTwoHands(80, 0, rightJitter: 0.0, leftJitter: 0.0));
            SwingAnalyzer.ApplyMapNotes(score, leftNotes: 80, rightNotes: 80);

            var right = score.Hand(1);
            Assert.Equal(
                right.AverageCut * right.GoodCount / (115.0 * 160) * 100.0,
                score.Accuracy, 9);
        }

        [Fact]
        public void Missing_the_very_first_note_costs_two_swings_like_any_other()
        {
            // 「最初のノーツは振りにならない」ので満点から1つ引いている。
            // ではその1本をミスしたら無傷なのか、という話。
            // 振りに使うのは隣り合う2つがどちらも切れたときだけなので、
            // 最初のノーツに入る（仮の前の点からの）振りも、そこから出る振りも数えない。
            // 途中のミスと同じく2本ぶん落ちる。
            var clean = SwingAnalyzer.Analyze(BuildTwoHands(60, 0, 0.0, 0.0));
            var missed = SwingAnalyzer.Analyze(BuildTwoHands(60, 0, 0.0, 0.0, missAt: 0));

            var a = clean.Hand(1);
            var b = missed.Hand(1);

            // ノーツ数は変わらない。切れた数が1つ減り、振りは2つ減る。
            Assert.Equal(a.NoteCount, b.NoteCount);
            Assert.Equal(a.GoodCount - 1, b.GoodCount);
            Assert.Equal(a.SwingCount - 2, b.SwingCount);

            // 満点は同じまま。無傷ではなく、ちょうど2本ぶん落ちる。
            Assert.Equal(a.SwingableCount, b.SwingableCount);
            Assert.True(b.ReproducibilityPercent < a.ReproducibilityPercent);

            Assert.Equal(a.Score * (a.SwingCount - 2) / a.SwingableCount, b.ReproducibilityPercent, 6);
        }

        [Fact]
        public void Missing_a_note_in_the_middle_costs_two_swings()
        {
            // 途中のミスは、そのノーツに入る振りと出る振りの両方を壊す。
            // 2点の往復では、飛ばした先が同じ位置に戻るので跳んでいない扱いにもなる。
            var clean = SwingAnalyzer.Analyze(BuildTwoHands(60, 0, 0.0, 0.0));
            var missed = SwingAnalyzer.Analyze(BuildTwoHands(60, 0, 0.0, 0.0, missAt: 30));

            Assert.Equal(clean.Hand(1).SwingCount - 2, missed.Hand(1).SwingCount);
        }

        [Fact]
        public void Two_misses_in_a_row_do_not_join_the_notes_around_them()
        {
            // 下 → 上（ミス）→ 下（ミス）→ 上 と続くと、切れたノーツだけをつなげば
            // 下から上への1往復半が「下 → 上」の1本として束に入ってしまう。
            // 隣り合う2つがどちらも切れたときだけを振りにするので、
            // ミスに入る振り・ミスどうしの振り・ミスから出る振りの3本が抜けるだけになる。
            var clean = SwingAnalyzer.Analyze(BuildTwoHands(60, 0, 0.0, 0.0));
            var missed = SwingAnalyzer.Analyze(BuildTwoHands(60, 0, 0.0, 0.0, missAt: 30, missAlsoAt: 31));

            Assert.Equal(clean.Hand(1).SwingCount - 3, missed.Hand(1).SwingCount);

            // 残った振りは全部まともな1本なので、束も往復もきれいなまま
            Assert.Equal(clean.Hand(1).Score, missed.Hand(1).Score, 6);
        }

        [Fact]
        public void One_hand_alone_is_just_that_hand()
        {
            var score = SwingAnalyzer.Analyze(BuildTwoHands(40, 0, rightJitter: 0.01, leftJitter: 0.01));

            Assert.Single(score.PerHand);
            Assert.Equal(score.PerHand[0].Score, score.Score, 9);
        }
    }
}
