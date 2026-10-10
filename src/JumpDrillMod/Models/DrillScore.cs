using System;

namespace JumpDrillMod.Models
{
    /// <summary>
    /// ドリル1回ぶんの記録。リーダーボードの1行。
    /// </summary>
    /// <remarks>
    /// 保存はしない。BeatLeader / LocalLeaderboard が残したリプレイを
    /// JumpDrill.Core（<c>SwingAnalyzer</c>） に通した結果を、画面に出す形に移し替えただけのもの。
    /// 元が同じなので JumpDrill の GUI のスコアボードと同じ数字になる。
    /// </remarks>
    public class DrillScore
    {
        /// <summary>譜面名そのもの。ID だけでは何のドリルか読めないため。</summary>
        public string SongName { get; set; } = string.Empty;

        /// <summary>
        /// 元のリプレイ。軌道の図を描くときに読み直す。
        /// </summary>
        /// <remarks>
        /// 読んだ <c>Replay</c> は持たない。1本ぶんで数千フレームあり、
        /// 記録の数だけ抱えるとメニューに居るだけで膨らむ。図が要るときに読み直す。
        /// </remarks>
        public string ReplayPath { get; set; } = string.Empty;

        public DateTime PlayedAt { get; set; }

        /// <summary>
        /// 再現精度 %。<b>順位を決める値。</b>
        /// JumpDrill の GUI のスコアボードと同じ指標で、<c>再現性 % × 振り数 ÷ ノーツ数</c>。
        /// </summary>
        public double ReproducibilityPercent { get; set; }

        /// <summary>
        /// 左手だけの再現精度 %。その手の振りが無ければ null。
        /// </summary>
        /// <remarks>
        /// 全体の再現精度は両手の合計から出すので、片手が崩れていても
        /// もう片方が良ければそれなりの数字になる。どちらの手が足を引っ張っているかは
        /// 手ごとに見ないと分からない（JumpDrill の GUI の内訳の「左」「右」と同じ値）。
        /// </remarks>
        public double? LeftReproducibilityPercent { get; set; }

        /// <summary>右手だけの再現精度 %。その手の振りが無ければ null。</summary>
        public double? RightReproducibilityPercent { get; set; }

        /// <summary>左手だけの精度 %。全体の精度 % を左手のノーツだけで出したもの（コンボ倍率込み）。</summary>
        public double? LeftAccuracy { get; set; }

        /// <summary>右手だけの精度 %。</summary>
        public double? RightAccuracy { get; set; }

        /// <summary>左手のミス数。</summary>
        public int? LeftMissCount { get; set; }

        /// <summary>右手のミス数。</summary>
        public int? RightMissCount { get; set; }

        /// <summary>再現性 %。全ての振りが1本の線に重なれば 100。本数は見ない。</summary>
        public double Reproducibility { get; set; }

        /// <summary>精度 %。ゲームの結果画面・BeatLeader の Acc と同じ値（コンボ倍率込みのスコア ÷ 譜面の満点）。</summary>
        public double Accuracy { get; set; }

        /// <summary>実際に振った数。</summary>
        public int SwingCount { get; set; }

        /// <summary>振れるはずだった数（＝譜面のノーツ数）。再現精度 % の分母。</summary>
        public int SwingableCount { get; set; }

        public int MissCount { get; set; }

        /// <summary>叩いた長さ (秒)。</summary>
        public double DurationSeconds { get; set; }

        /// <summary>
        /// 途中から始めた・速度を変えた記録か。
        /// ドリルは WIP なので練習モードでしか叩けないが、既定のまま頭から叩けば false。
        /// </summary>
        public bool ModifiedPractice { get; set; }

        /// <summary>
        /// 自動プレイのリプレイか。ヘッドセットもコントローラも動かないので
        /// 再現性がほぼ満点になり、人の記録と並べると上位に居座る（JumpDrill.Core の <c>LooksLikeAutoplay</c>）。
        /// </summary>
        public bool Autoplay { get; set; }

        /// <summary>曲の速さを変えて叩いた記録か（練習の速度を 100% 以外に、または Faster / Slower / Super Fast）。</summary>
        public bool SpeedChanged { get; set; }

        /// <summary>
        /// 記録として数えるか（メダル・順位）。自動プレイと曲の速さを変えたものは数えない
        /// （JumpDrill.Core の <c>ReplayInfo.CountsAsRecord</c>）。一覧には薄く出す。
        /// </summary>
        public bool CountsAsRecord { get; set; }

        /// <summary>左手の 再現・点数・角度（正面図に添える）。その手の振りが無ければ null。</summary>
        public HandFigures? LeftHand { get; set; }

        /// <summary>右手の 再現・点数・角度。</summary>
        public HandFigures? RightHand { get; set; }

        /// <summary>左手フォアの PRE / POST。その向きの振りが無ければ null。</summary>
        public SwingAngles? LeftFore { get; set; }

        /// <summary>左手バックの PRE / POST。</summary>
        public SwingAngles? LeftBack { get; set; }

        /// <summary>右手フォアの PRE / POST。</summary>
        public SwingAngles? RightFore { get; set; }

        /// <summary>右手バックの PRE / POST。</summary>
        public SwingAngles? RightBack { get; set; }
    }

    /// <summary>
    /// 片手の成績。正面図に添える（JumpDrill の GUI の正面図の数字と同じ値）。
    /// </summary>
    public class HandFigures
    {
        /// <summary>再現性 (/100)。本数は見ない。</summary>
        public double Reproducibility { get; set; }

        /// <summary>1ノーツあたりの平均スコア (/115)。</summary>
        public double AverageCut { get; set; }

        /// <summary>角度 (/100)。</summary>
        public double AnglePercent { get; set; }
    }

    /// <summary>
    /// 片側（フォアかバック）の振りかぶりと振り抜き (%)。横から見た図に添える
    /// （JumpDrill の GUI の横図の数字と同じ値）。
    /// </summary>
    public class SwingAngles
    {
        public double PreSwing { get; set; }

        public double PostSwing { get; set; }
    }
}
