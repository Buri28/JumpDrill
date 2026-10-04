using System;
using System.Collections.Generic;

namespace JumpDrill.Model
{
    /// <summary>ドリル1本ぶんの指定（設計メモ §2「指定項目」）。</summary>
    public sealed class DrillOptions
    {
        /// <summary>手ごとの循環。1本以上。</summary>
        public IReadOnlyList<HandSequence> Sequences { get; set; }

        /// <summary>ノーツ間隔 (ms)。BPM ではなく ms で直接指定する。</summary>
        public double IntervalMs { get; set; } = 300.0;

        /// <summary>ドリル本体の尺 (秒)。カウントインとテールは含まない。</summary>
        public double DurationSeconds { get; set; } = 30.0;

        /// <summary>
        /// 表示のためだけの細分化（1拍あたりのノーツ数）。生成には一切効かない。
        /// BPM で指定したときに、指定した細分化のまま BPM を出し直すために持つ。
        /// 300BPM 1/2 と入れたものが 600BPM と表示されると読み替えが要るため。
        /// </summary>
        public double NotesPerBeat { get; set; } = 1.0;

        public DirectionMode Direction { get; set; } = DirectionMode.Axis;

        public HandPattern HandPattern { get; set; } = HandPattern.Sync;

        /// <summary>
        /// 時間軸の基準にするだけの BPM。ノーツ間隔とは独立で、
        /// _time は ms から実数拍に換算して書く（量子化しない）。
        /// </summary>
        public double Bpm { get; set; } = 120.0;

        public double Njs { get; set; } = 16.0;

        /// <summary>目標ジャンプ距離 (m)。指定するとオフセットを逆算する。</summary>
        public double? JumpDistance { get; set; }

        /// <summary>目標反応時間 (ms)。指定するとオフセットを逆算する。</summary>
        public double? ReactionTimeMs { get; set; }

        /// <summary>_noteJumpStartBeatOffset を直接指定する場合。</summary>
        public double? NoteJumpStartBeatOffset { get; set; }

        public ClickMode Click { get; set; } = ClickMode.Down;

        /// <summary>カウントインのクリック数。クリック周期で先頭に並ぶ。</summary>
        public int CountInClicks { get; set; } = 8;

        /// <summary>
        /// 曲頭の空白の下限 (秒)。ノーツのスポーンが曲開始前になると壊れるので、
        /// ジャンプ時間から要求される値とこの値の大きい方を採る。
        /// </summary>
        public double MinLeadInSeconds { get; set; } = 2.0;

        /// <summary>最後のノーツの後の余白 (秒)。ポストスイングの登録に要る。</summary>
        public double TailSeconds { get; set; } = 1.5;

        /// <summary>
        /// ブロックとブロックの間に空ける秒数。null なら
        /// カウントインがちょうど収まる長さを自動で採る。
        /// </summary>
        public double? GapSeconds { get; set; }

        /// <summary>
        /// セットの繰り返し回数。1セットは Split なら「各手1本ずつ」、
        /// それ以外なら「本体1本」。既定は1。
        /// </summary>
        public int Sets { get; set; } = 1;

        /// <summary>曲名。省略すると内容から自動生成する。</summary>
        public string Name { get; set; }

        public void Validate()
        {
            if (Sequences == null || Sequences.Count == 0)
                throw new InvalidOperationException(Lang.T("遷移が指定されていません。", "No sequence is specified."));
            if (IntervalMs <= 0)
                throw new InvalidOperationException(Lang.T("ノーツ間隔は正の値。", "Note interval must be positive."));
            if (DurationSeconds <= 0)
                throw new InvalidOperationException(Lang.T("尺は正の値。", "Length must be positive."));
            if (Bpm <= 0)
                throw new InvalidOperationException(Lang.T("BPM は正の値。", "BPM must be positive."));
            if (Njs <= 0)
                throw new InvalidOperationException(Lang.T("NJS は正の値。", "NJS must be positive."));
            if (CountInClicks < 0)
                throw new InvalidOperationException(Lang.T("カウントイン数は 0 以上。", "Count-in must be 0 or more."));
            if (Sets < 1)
                throw new InvalidOperationException(Lang.T("セット数は 1 以上。", "Sets must be 1 or more."));

            int specified = 0;
            if (JumpDistance.HasValue) specified++;
            if (ReactionTimeMs.HasValue) specified++;
            if (NoteJumpStartBeatOffset.HasValue) specified++;
            if (specified > 1)
                throw new InvalidOperationException(Lang.T("ジャンプ距離 / 反応時間 / オフセットは同時に1つだけ指定できます。", "Specify only one of jump distance, reaction time and offset."));
        }
    }
}
