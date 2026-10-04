using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JumpDrill.Model;

namespace JumpDrill.Generation
{
    /// <summary>指定からノーツ列とクリック列を組み立てる。</summary>
    public static class DrillGenerator
    {
        public static DrillMap Generate(DrillOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Validate();

            double intervalSec = options.IntervalMs / 1000.0;
            double offset = ResolveOffset(options);
            var jump = JumpMath.Compute(options.Njs, options.Bpm, offset);

            // Split では尺の指定が「手あたりの秒数」になる。
            // 片手で丸ごと1本叩いてから次の手に移るので、1セットはその手数ぶん。
            bool split = options.HandPattern == HandPattern.Split;
            int blocksPerSet = split ? options.Sequences.Count : 1;
            int blockCount = blocksPerSet * options.Sets;

            // ノーツ数は速さどおりに数える。30 秒 × 100ms なら 300 本。
            // 両端に置いて 301 本にすると、2点の往復で下段だけ1本多い非対称な譜面になる。
            int stepsPerBlock = (int)Math.Round(options.DurationSeconds / intervalSec);
            if (stepsPerBlock < 2) stepsPerBlock = 2;
            int stepCount = stepsPerBlock * blockCount;

            // まず先頭を 0 秒、ブロック間の間も 0 として組む。
            // どのステップで鳴らすかは時刻に依らないので、これで確定できる。
            var notesByStep = BuildSteps(options, stepCount, stepsPerBlock);

            var plan = ClickPlanner.Plan(notesByStep, options.Click);
            double clickPeriodSec = DeriveClickPeriod(plan, intervalSec);

            double leadIn = ResolveLeadIn(options, jump, clickPeriodSec);
            // 手が1つしかなければブロックも1つ。間を持つ意味がない。
            double gapSec = blockCount > 1 ? ResolveGap(options, clickPeriodSec) : 0.0;

            var notes = new List<DrillNote>();
            double beatsPerSecond = options.Bpm / 60.0;
            foreach (var step in notesByStep)
            {
                foreach (var n in step)
                {
                    double t = StepTime(n.StepIndex, stepsPerBlock, intervalSec, leadIn, gapSec);
                    notes.Add(new DrillNote(t, t * beatsPerSecond, n.Position, n.Hand, n.Direction, n.StepIndex));
                }
            }

            var clicks = new List<ClickEvent>();
            // カウントイン。クリック周期のまま前に伸ばすので、そのまま予備動作になる。
            AddCountIn(clicks, leadIn, clickPeriodSec, options.CountInClicks, 0.0);

            // 2ブロック目以降にも同じカウントインを置く。間を数えずに入れると
            // 手を替えた直後の1発目が合わない。
            for (int block = 1; block < blockCount; block++)
            {
                double blockStart = StepTime(block * stepsPerBlock, stepsPerBlock, intervalSec, leadIn, gapSec);
                double previousEnd = StepTime(block * stepsPerBlock - 1, stepsPerBlock, intervalSec, leadIn, gapSec);
                AddCountIn(clicks, blockStart, clickPeriodSec, options.CountInClicks, previousEnd);
            }

            foreach (var entry in plan)
                clicks.Add(new ClickEvent(StepTime(entry.StepIndex, stepsPerBlock, intervalSec, leadIn, gapSec), entry.Accent));

            clicks.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));

            double lastNote = notes.Count > 0 ? notes[notes.Count - 1].TimeSeconds : leadIn;

            return new DrillMap
            {
                Options = options,
                Notes = notes,
                Clicks = clicks,
                Bpm = options.Bpm,
                Njs = options.Njs,
                NoteJumpStartBeatOffset = offset,
                Jump = jump,
                LeadInSeconds = leadIn,
                TotalSeconds = lastNote + options.TailSeconds,
                ClickPeriodSeconds = clickPeriodSec,
                BlockCount = blockCount,
                BlocksPerSet = blocksPerSet,
                Sets = options.Sets,
                GapSeconds = gapSec,
                Name = options.Name ?? DescribeName(options),
            };
        }

        private static List<IReadOnlyList<DrillNote>> BuildSteps(DrillOptions options, int stepCount, int stepsPerBlock)
        {
            var sequences = options.Sequences;
            double intervalSec = options.IntervalMs / 1000.0;
            var byStep = new List<IReadOnlyList<DrillNote>>(stepCount);

            for (int step = 0; step < stepCount; step++)
            {
                double t = step * intervalSec;
                var atStep = new List<DrillNote>();

                // セットを繰り返しても各ブロックが同じ中身になるよう、
                // 循環はブロック内の位置で数える。
                int block = step / stepsPerBlock;
                int inBlock = step % stepsPerBlock;

                if (options.HandPattern == HandPattern.Split)
                {
                    // ブロック1つにつき手1つ。その手だけが自分の循環を進める。
                    var seq = sequences[block % sequences.Count];
                    atStep.Add(MakeNote(seq, inBlock, options.Direction, t, step));
                }
                else if (options.HandPattern == HandPattern.Alternate)
                {
                    // 手番を回す。その手にとっては 1/handCount の頻度なので
                    // 循環の進み方も handCount ステップに1回。
                    var seq = sequences[inBlock % sequences.Count];
                    int cycleIndex = inBlock / sequences.Count;
                    atStep.Add(MakeNote(seq, cycleIndex, options.Direction, t, step));
                }
                else
                {
                    foreach (var seq in sequences)
                        atStep.Add(MakeNote(seq, inBlock, options.Direction, t, step));
                }

                byStep.Add(atStep);
            }

            return byStep;
        }

        /// <param name="playIndex">その手が何回目に叩くか。0 始まり。</param>
        private static DrillNote MakeNote(HandSequence seq, int playIndex, DirectionMode mode, double timeSeconds, int stepIndex)
        {
            // 記法 8>b は「8 から b へ振る」という意味なので、最初に叩くのは b の方。
            // 矢印は入ってくる動きで決まるので、1点目から始めると
            // いきなり戻りの振り（b→8）から入ることになって噛み合わない。
            int cycleIndex = playIndex + 1;

            var pos = seq.StepAt(cycleIndex).Position;
            var dir = DirectionResolver.Resolve(seq, cycleIndex, mode);
            // Beat はここでは仮。leadIn を足したあとで確定させる。
            return new DrillNote(timeSeconds, 0.0, pos, seq.Hand, dir, stepIndex);
        }

        /// <summary>ブロック番号ぶんの間を足した実時刻。</summary>
        private static double StepTime(int stepIndex, int stepsPerBlock, double intervalSec, double leadIn, double gapSec)
        {
            int block = stepIndex / stepsPerBlock;
            return leadIn + stepIndex * intervalSec + block * gapSec;
        }

        /// <summary>
        /// 目標時刻の手前にクリック周期どおりのカウントインを置く。
        /// <paramref name="notBefore"/> より前には置かない（前のブロックに食い込ませない）。
        /// </summary>
        private static void AddCountIn(List<ClickEvent> clicks, double target, double periodSec, int count, double notBefore)
        {
            for (int k = count; k >= 1; k--)
            {
                double t = target - k * periodSec;
                if (t >= notBefore && t >= 0.0) clicks.Add(new ClickEvent(t, true));
            }
        }

        /// <summary>
        /// ブロックの切れ目で空ける秒数。既定はカウントインがちょうど収まる長さ。
        /// 手を持ち替える間がないと、次のブロックの入りが崩れる。
        /// </summary>
        private static double ResolveGap(DrillOptions options, double clickPeriodSec)
        {
            if (options.GapSeconds.HasValue)
                return Math.Max(0.0, options.GapSeconds.Value);

            return Math.Max(2.0, options.CountInClicks * clickPeriodSec);
        }

        private static double ResolveOffset(DrillOptions options)
        {
            if (options.NoteJumpStartBeatOffset.HasValue)
                return options.NoteJumpStartBeatOffset.Value;
            if (options.JumpDistance.HasValue)
                return JumpMath.OffsetForJumpDistance(options.Njs, options.Bpm, options.JumpDistance.Value);
            if (options.ReactionTimeMs.HasValue)
                return JumpMath.OffsetForReactionTime(options.Njs, options.Bpm, options.ReactionTimeMs.Value / 1000.0);
            return 0.0;
        }

        /// <summary>クリックの間隔。最初の2発から測る。無ければノーツ間隔。</summary>
        private static double DeriveClickPeriod(IReadOnlyList<ClickPlanEntry> plan, double intervalSec)
        {
            if (plan.Count >= 2)
                return (plan[1].StepIndex - plan[0].StepIndex) * intervalSec;
            return intervalSec;
        }

        /// <summary>
        /// 曲頭の空白。ノーツはジャンプ時間ぶん手前でスポーンするので、
        /// そこに1秒の余裕を足したものと、カウントインが収まる長さの大きい方。
        /// </summary>
        private static double ResolveLeadIn(DrillOptions options, JumpMath.JumpResult jump, double clickPeriodSec)
        {
            double spawnSafe = jump.JumpDurationSeconds + 1.0;
            double countIn = options.CountInClicks * clickPeriodSec + 0.25;
            return Math.Max(Math.Max(options.MinLeadInSeconds, spawnSafe), countIn);
        }

        /// <summary>
        /// 名前を指定されなかったときの間に合わせ。CLI・GUI・MOD はどれも
        /// <c>DrillNaming.Compose</c> を通すので、通常はここに来ない。
        /// </summary>
        /// <remarks>速さは BPM で書く。ms は普段目にしない単位なので出さない。</remarks>
        private static string DescribeName(DrillOptions options)
        {
            string seq = string.Join(" ", options.Sequences.Select(s => s.ToString()).ToArray());
            string dir = options.Direction.ToString().ToLowerInvariant();
            double notesPerBeat = options.NotesPerBeat > 0 ? options.NotesPerBeat : 1.0;
            return string.Format(CultureInfo.InvariantCulture, "{0} {1:0.#}BPM {2}",
                seq, Tempo.BpmFromIntervalMs(options.IntervalMs, notesPerBeat), dir);
        }
    }
}
