using System.Linq;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;
using JumpDrill.Replays;
using JumpDrill.Serialization;
using Xunit;

namespace JumpDrill.Core.Tests
{
    /// <summary>
    /// タワーの採点。端の点だけのドリルと同じに数え、Acc（精度）だけは全ノーツで出す。
    /// </summary>
    public class TowerScoringTests
    {
        private static DrillMap Generate(string spec, bool tower, DirectionMode mode = DirectionMode.Axis,
            double intervalMs = 300, HandPattern pattern = HandPattern.Sync)
        {
            var options = new DrillOptions
            {
                Sequences = SequenceParser.ParseAll(spec),
                IntervalMs = intervalMs,
                HandPattern = pattern,
                DurationSeconds = 12,
                Direction = mode,
                Tower = tower,
            };
            options.Name = DrillNaming.Compose(options);
            return DrillGenerator.Generate(options);
        }

        /// <summary>
        /// 譜面どおりに叩いたリプレイ。端の点（振りの行き先）は <paramref name="endCut"/>、
        /// 間のノーツは <paramref name="middleCut"/> で切る。軌道はどちらも同じ往復。
        /// </summary>
        private static Replay Play(DrillMap map, float middleCut = 1f, float endCut = 1f)
        {
            var replay = new Replay();
            replay.Info.SongName = map.Name;

            var ends = map.Notes.GroupBy(n => n.StepIndex)
                .ToDictionary(g => g.Key, g => g.First());   // 生成は本来の点を先に置く

            foreach (var n in map.Notes)
            {
                bool end = ReferenceEquals(ends[n.StepIndex], n);
                float rating = end ? endCut : middleCut;
                replay.Notes.Add(new ReplayNote
                {
                    NoteId = n.Position.LineIndex * 1000 + n.Position.LineLayer * 100 + (int)n.Hand * 10 + (int)n.Direction,
                    SpawnTime = (float)n.TimeSeconds,
                    EventTime = (float)n.TimeSeconds,
                    EventType = NoteEventType.Good,
                    Cut = new NoteCutInfo
                    {
                        SaberType = (int)n.Hand,
                        BeforeCutRating = rating,
                        AfterCutRating = rating,
                        CutDistanceToCenter = 0f,
                    },
                });
            }

            // 3 と b の間の上下の往復。
            var steps = ends.Values.OrderBy(n => n.TimeSeconds).ToList();
            for (int i = 0; i + 1 < steps.Count; i++)
            {
                float t0 = (float)steps[i].TimeSeconds, t1 = (float)steps[i + 1].TimeSeconds;
                float y0 = steps[i].Position.LineLayer * 0.3f, y1 = steps[i + 1].Position.LineLayer * 0.3f;
                for (int f = 0; f < 8; f++)
                {
                    float p = f / 8f;
                    replay.Frames.Add(new ReplayFrame
                    {
                        Time = t0 + (t1 - t0) * p,
                        Right = new ReplayTransform { Position = new Vector3 { X = 0.3f, Y = 1.0f + y0 + (y1 - y0) * p, Z = 0.5f } },
                    });
                }
            }
            return replay;
        }

        private static ReplayScore Analyze(DrillMap map, Replay replay)
        {
            MapNotes.Counts counts;
            Assert.True(MapNotes.TryCountText(BeatmapSerializer.WriteDifficulty(map), out counts));

            var score = SwingAnalyzer.Analyze(replay);
            SwingAnalyzer.ApplyMapNotes(score, counts.Left, counts.Right, counts.SwingLeft, counts.SwingRight);
            return score;
        }

        [Fact]
        public void Map_counts_one_swing_per_tower()
        {
            var tower = Generate("R3b", true);
            MapNotes.Counts counts;
            Assert.True(MapNotes.TryCountText(BeatmapSerializer.WriteDifficulty(tower), out counts));

            Assert.Equal(tower.Notes.Count, counts.Right);
            Assert.Equal(tower.Notes.Count / 3, counts.SwingRight);
        }

        [Fact]
        public void Collapse_keeps_the_end_the_swing_arrives_at()
        {
            var map = Generate("R3b", true);
            var kept = TowerReplay.Collapse(Play(map)).Notes;

            // b ▼、3 ▲ の交互。間の 7 は残らない。
            Assert.Equal(map.Notes.Count / 3, kept.Count);
            for (int i = 0; i < kept.Count; i++)
                Assert.Equal(i % 2 == 0 ? 'b' : '3', new GridPosition(kept[i].LineIndex, kept[i].LineLayer).ToToken());
        }

        [Fact]
        public void Dot_towers_alternate_from_the_first_target_in_the_name()
        {
            var map = Generate("R3b", true, DirectionMode.Dot);
            var kept = TowerReplay.Collapse(Play(map)).Notes;

            for (int i = 0; i < kept.Count; i++)
                Assert.Equal(i % 2 == 0 ? 'b' : '3', new GridPosition(kept[i].LineIndex, kept[i].LineLayer).ToToken());
        }

        private static string KeptTokens(DrillMap map, Hand hand)
        {
            var kept = TowerReplay.Collapse(Play(map)).Notes.Where(n => n.ColorType == (int)hand);
            return string.Concat(kept.Select(n => new GridPosition(n.LineIndex, n.LineLayer).ToToken()));
        }

        private static string Alternating(char first, char second, int count)
        {
            return string.Concat(Enumerable.Range(0, count).Select(i => i % 2 == 0 ? first : second));
        }

        [Theory]
        [InlineData(DirectionMode.Dot)]
        [InlineData(DirectionMode.Perpendicular)]
        public void Slow_towers_keep_alternating(DirectionMode mode)
        {
            // 1 秒より間が空いても、ブロックの切れ目とは見なさない。
            var map = Generate("R3b", true, mode, intervalMs: 1100);
            string kept = KeptTokens(map, Hand.Right);
            Assert.Equal(Alternating('b', '3', kept.Length), kept);
        }

        [Fact]
        public void Straightened_perpendicular_diagonals_pick_the_far_end()
        {
            // 1>b の斜めを垂直にして上下左右に倒すと、矢印は線に沿わない。矢印では決められない。
            var map = Generate("R1bs", true, DirectionMode.Perpendicular);
            string kept = KeptTokens(map, Hand.Right);
            Assert.Equal(Alternating('b', '1', kept.Length), kept);
        }

        [Theory]
        [InlineData(DirectionMode.Dot)]
        [InlineData(DirectionMode.Perpendicular)]
        public void Split_blocks_restart_from_the_first_target(DirectionMode mode)
        {
            // 右で1本叩いてから左で1本。どちらのブロックも最初に叩くのは記法の2点目。
            var map = Generate("R14, L41", true, mode, pattern: HandPattern.Split);
            string right = KeptTokens(map, Hand.Right);
            string left = KeptTokens(map, Hand.Left);
            Assert.Equal(Alternating('4', '1', right.Length), right);
            Assert.Equal(Alternating('1', '4', left.Length), left);
        }

        [Fact]
        public void Plain_drills_are_left_alone()
        {
            var replay = Play(Generate("R3b", false));
            Assert.Same(replay, TowerReplay.Collapse(replay));
        }

        [Fact]
        public void A_tower_scores_like_the_plain_jump()
        {
            var plainMap = Generate("R3b", false);
            var towerMap = Generate("R3b", true);

            var plain = Analyze(plainMap, Play(plainMap, endCut: 0.8f));
            var tower = Analyze(towerMap, Play(towerMap, middleCut: 0.8f, endCut: 0.8f));

            Assert.Equal(plain.NoteCount, tower.NoteCount);
            // Good はゲームと同じく全ノーツで数える（タワーは 3 倍）。
            Assert.Equal(plain.GoodCount * 3, tower.GoodCount);
            Assert.Equal(plain.SwingCount, tower.SwingCount);
            Assert.Equal(plain.ReproducibilityPercent, tower.ReproducibilityPercent, 6);
            Assert.Equal(plain.Hand(1).ReproducibilityPercent, tower.Hand(1).ReproducibilityPercent, 6);
        }

        [Fact]
        public void Misses_in_the_middle_of_a_tower_are_counted_but_do_not_touch_reproducibility()
        {
            var map = Generate("R3b", true);
            var clean = Analyze(map, Play(map));

            // 間のノーツ（7）を3つ落とす。端は全部切れている。
            var replay = Play(map);
            var middles = replay.Notes.Where(n => n.LineLayer == 1).Take(3).ToList();
            foreach (var n in middles)
            {
                n.EventType = NoteEventType.Miss;
                n.Cut = null;
            }
            var missed = Analyze(map, replay);

            Assert.Equal(3, missed.MissCount);
            Assert.Equal(3, missed.Hand(1).MissCount);
            Assert.Equal(missed.Hand(1).MissCount, missed.Hand(1).Fore.MissCount + missed.Hand(1).Back.MissCount);
            Assert.Equal(missed.Hand(1).GoodCount, missed.Hand(1).Fore.GoodCount + missed.Hand(1).Back.GoodCount);
            Assert.Equal(map.Notes.Count - 3, missed.GoodCount);

            // 再現性は端の点だけで出す。
            Assert.Equal(clean.Hand(1).Score, missed.Hand(1).Score, 6);
            Assert.Equal(clean.SwingCount, missed.SwingCount);
        }

        [Fact]
        public void Acc_counts_every_note_of_the_tower()
        {
            var map = Generate("R3b", true);

            // 端は満点、間は 0.5 で切る。端だけで出すと 100% になってしまう。
            var score = Analyze(map, Play(map, middleCut: 0.5f, endCut: 1f));

            Assert.Equal(map.Notes.Count, score.AccuracyNoteCount);
            Assert.InRange(score.Accuracy, 1.0, 99.0);
            Assert.InRange(score.Hand(1).Accuracy, 1.0, 99.0);
            Assert.Equal(score.Accuracy, score.Hand(1).Accuracy, 6);

            // 全部満点なら 100%。
            var perfect = Analyze(map, Play(map));
            Assert.Equal(100.0, perfect.Accuracy, 6);
            Assert.Equal(100.0, perfect.Hand(1).Accuracy, 6);
        }
    }
}
