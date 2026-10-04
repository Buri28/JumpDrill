using System.Collections.Generic;

namespace JumpDrill.Model
{
    /// <summary>生成結果。譜面・クリック・ジャンプ諸元をまとめて持つ。</summary>
    public sealed class DrillMap
    {
        public DrillOptions Options { get; internal set; }
        public IReadOnlyList<DrillNote> Notes { get; internal set; }
        public IReadOnlyList<ClickEvent> Clicks { get; internal set; }

        public double Bpm { get; internal set; }
        public double Njs { get; internal set; }
        public double NoteJumpStartBeatOffset { get; internal set; }
        public JumpMath.JumpResult Jump { get; internal set; }

        /// <summary>最初のノーツの時刻 (秒)。</summary>
        public double LeadInSeconds { get; internal set; }

        /// <summary>音源全体の長さ (秒)。</summary>
        public double TotalSeconds { get; internal set; }

        /// <summary>クリックの周期 (秒)。カウントインもこの周期で並ぶ。</summary>
        public double ClickPeriodSeconds { get; internal set; }

        /// <summary>ブロックの総数。1セットあたりのブロック数 × セット数。</summary>
        public int BlockCount { get; internal set; } = 1;

        /// <summary>1セットに入るブロック数。Split なら手の数、それ以外は 1。</summary>
        public int BlocksPerSet { get; internal set; } = 1;

        /// <summary>セットの繰り返し回数。</summary>
        public int Sets { get; internal set; } = 1;

        /// <summary>ブロック間に空けた秒数。Split 以外は 0。</summary>
        public double GapSeconds { get; internal set; }

        /// <summary>曲名 / 難易度ラベルに使う短い説明。</summary>
        public string Name { get; internal set; }
    }
}
