using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JumpDrill.Model;

namespace JumpDrill.Replays
{
    /// <summary>
    /// 打点ごと・遷移ごとに出す欄。<b>片手まとめ</b>（<see cref="SwingScore"/>）でも
    /// <b>片側だけ</b>（<see cref="DirectionScore"/>）でも同じ形で読めるようにしておく。
    /// 表がこの2つを同じ行に並べるので、欄ごとに出し分けを書かずに済む。
    ///
    /// <b>往復で比べて出す欄（再現 /100・往復ずれ・再現スコア）は入れない。</b>
    /// 行きと帰りを突き合わせて出す値なので、片側だけでは定義できない。
    /// </summary>
    public interface ICutStats
    {
        /// <summary>切れたノーツ数。</summary>
        int GoodCount { get; }

        /// <summary>ミス数。</summary>
        int MissCount { get; }

        /// <summary>
        /// <b>有効振り数</b>。再現性の計算に実際に使った振りの本数。
        ///
        /// 切れたノーツ数とは一致しない。同じ遷移が
        /// <see cref="SwingAnalyzer.MinimumSwingsPerGroup"/> 本に満たない組は、
        /// 平均が当てにならないので数えていない。
        /// </summary>
        int SwingCount { get; }

        /// <summary>1ノーツあたりの平均スコア (0-115)。</summary>
        double AverageCut { get; }

        /// <summary>振りかぶりの点の平均 (0-70)。</summary>
        double BeforeCutScore { get; }

        /// <summary>振り抜きの点の平均 (0-30)。</summary>
        double AfterCutScore { get; }

        /// <summary>中心点の平均 (0-15)。</summary>
        double CutDistanceScore { get; }

        /// <summary>時間依存。BeatLeader の <c>TD</c>。</summary>
        double TimeDependence { get; }

        /// <summary>振りかぶりの角度 (%)。<c>PRE</c>。</summary>
        double PreSwing { get; }

        /// <summary>振り抜きの角度 (%)。<c>POST</c>。</summary>
        double PostSwing { get; }

        /// <summary>要求された向きからの角度ずれの平均 (度)。</summary>
        double AngleErrorMean { get; }

        /// <summary>角度。100点満点。</summary>
        double AnglePercent { get; }

        /// <summary>ノーツ中心からの距離の平均 (m)。</summary>
        double CutDistanceMean { get; }

        /// <summary>打点ずれの平均 (ms)。＋が遅れ。</summary>
        double TimeDeviationMean { get; }

        /// <summary>打点ずれのばらつき (ms)。</summary>
        double TimeDeviationSpread { get; }

        /// <summary>同じ振りを重ねたときの束の幅 (m)。</summary>
        double PathScatter { get; }
    }

    /// <summary>
    /// 片手の<b>フォアだけ</b>／<b>バックだけ</b>の成績。
    ///
    /// 往復ドリルは行きと帰りで別の振りなので、平均すると両方の癖が打ち消し合う。
    /// 実測でも、角度ずれ 左 24.7° の内訳がフォア 12.4° / バック 37.0° で、
    /// 直すべきなのは片側だけだった。まとめた数字からはこれが読めない。
    ///
    /// <b>往復で比べる欄（再現 /100・往復ずれ）は持たない。</b>
    /// あれは行きと帰りを突き合わせて出す値なので、片側だけでは定義できない。
    /// </summary>
    public sealed class DirectionScore : ICutStats
    {
        /// <summary>フォア（切り下げ）側か。</summary>
        public bool Forehand { get; internal set; }

        /// <summary>この向きで切れたノーツ数。</summary>
        public int GoodCount { get; internal set; }

        /// <summary>この向きのミス数。</summary>
        public int MissCount { get; internal set; }

        /// <summary>この向きの有効振り数。計算に使った本数。</summary>
        public int SwingCount { get; internal set; }

        /// <summary>1ノーツあたりの平均スコア (0-115)。</summary>
        public double AverageCut { get; internal set; }

        /// <summary>振りかぶりの点の平均 (0-70)。</summary>
        public double BeforeCutScore { get; internal set; }

        /// <summary>振り抜きの点の平均 (0-30)。</summary>
        public double AfterCutScore { get; internal set; }

        /// <summary>中心点の平均 (0-15)。</summary>
        public double CutDistanceScore { get; internal set; }

        /// <summary>時間依存。BeatLeader の <c>TD</c>。</summary>
        public double TimeDependence { get; internal set; }

        /// <summary>振りかぶりの角度 (%)。<c>PRE</c>。</summary>
        public double PreSwing { get; internal set; }

        /// <summary>振り抜きの角度 (%)。<c>POST</c>。</summary>
        public double PostSwing { get; internal set; }

        /// <summary>要求された向きからの角度ずれの平均 (度)。</summary>
        public double AngleErrorMean { get; internal set; }

        /// <summary>角度。100点満点。</summary>
        public double AnglePercent { get { return SwingAnalyzer.AnglePercentOf(AngleErrorMean); } }

        /// <summary>ノーツ中心からの距離の平均 (m)。</summary>
        public double CutDistanceMean { get; internal set; }

        /// <summary>打点ずれの平均 (ms)。＋が遅れ。</summary>
        public double TimeDeviationMean { get; internal set; }

        /// <summary>打点ずれのばらつき (ms)。</summary>
        public double TimeDeviationSpread { get; internal set; }

        /// <summary>同じ振りを重ねたときの束の幅 (m)。</summary>
        public double PathScatter { get; internal set; }
    }

    /// <summary>
    /// ドリル1本ぶんの成績。切る位置の当たり外れより、
    /// <b>同じ振りをどれだけ同じ軌道で繰り返せているか</b>を主に見る。
    /// </summary>
    public sealed class SwingScore : ICutStats
    {
        /// <summary>0 = 左（赤）、1 = 右（青）。</summary>
        public int SaberType { get; internal set; }

        /// <summary>フォア（切り下げ）だけの内訳。その向きの振りが無ければ null。</summary>
        public DirectionScore Fore { get; internal set; }

        /// <summary>バック（切り上げ）だけの内訳。その向きの振りが無ければ null。</summary>
        public DirectionScore Back { get; internal set; }

        /// <summary>有効振り数。計算に使った本数（3本に満たない遷移は入らない）。</summary>
        public int SwingCount { get; internal set; }

        /// <summary>
        /// 振りが直線からどれだけ外れたか (m)。ノーツとノーツを結ぶ直線からの距離を
        /// RMS で取ったもの。<b>小さいほど良い。</b>
        /// </summary>
        public double TrajectorySpread { get; internal set; }

        /// <summary>
        /// 同じ振りを重ねたときの束の幅 (m)。平均軌道からのずれ。
        /// 直線から外れていても、毎回同じように外れていれば小さい。
        /// </summary>
        public double PathScatter { get; internal set; }

        /// <summary>
        /// 往路と復路のずれ (m)。<b>ノーツ2点の間だけ</b>で測る。
        ///
        /// 振りは手首を中心に回るので必ず湾曲する。曲がること自体は減点せず、
        /// <b>行きと帰りが同じ曲線に重なるか</b>だけを見る。
        ///
        /// 往路・復路それぞれの平均軌道を先に作ってから比べる。
        /// 1本ずつを共通の平均と比べる形も試したが、繰り返しのばらつきが混ざるぶん
        /// 値が全体の平均あたりに寄り、左右の差が読み取りにくくなった。
        /// ばらつきの方は<see cref="PathScatter"/>で別に見る。
        ///
        /// ノーツの外側（振り抜きの尾）は入れない。そこは折り返しなので往復で必ず
        /// 食い違い、強く振る手ほど尾が長くなって不利に出てしまう。
        /// 実測でも、尾を含めると上手い方の手が 2 倍悪く出た。
        /// </summary>
        public double RoundTripGap { get; internal set; }



        /// <summary>
        /// 打点ずれの平均 (ms)。<b>＋が遅れ、−が早い。</b>
        ///
        /// 打点間隔のばらつきではなく、ノーツ1個ごとの判定中心からのずれを見る。
        /// 間隔で測るとミスした所とセットの切れ目で跳ねて、そこが全部ばらつきになる。
        /// </summary>
        public double TimeDeviationMean { get; internal set; }

        /// <summary>打点ずれのばらつき (ms)。早い遅いが揃っているか。小さいほど良い。</summary>
        public double TimeDeviationSpread { get; internal set; }

        /// <summary>この手のミス数。</summary>
        public int MissCount { get; internal set; }

        /// <summary>この手が切れたノーツ数。</summary>
        public int GoodCount { get; internal set; }

        /// <summary>
        /// この手のノーツ数。<b>満点の分母。</b>
        ///
        /// 既定はリプレイに出たぶん（切れたもの＋ミス）。譜面が見つかれば
        /// <see cref="SwingAnalyzer.ApplyMapNotes"/> が譜面の数で上書きする。
        /// 途中でやめた記録は、そこから先のノーツがリプレイに残らないので、
        /// リプレイ由来のままだと振らなかったぶんが満点から消えてしまう。
        /// </summary>
        public int NoteCount { get; internal set; }

        /// <summary>
        /// 振りになり得るノーツ数。<b>ノーツ数と同じ。</b>
        ///
        /// ドリルは循環なので、その手の最初のノーツにも<b>1つ前の点が決まっている</b>
        /// （<c>8&gt;b</c> の往復なら b の前は 8）。プレイヤーもカウントインに合わせて
        /// そこへ実際に振り込んでいるので、1本の振りとして数える。
        /// 前の点が無いからと外すと、全部叩いても満点に届かなくなる。
        /// </summary>
        public int SwingableCount { get { return NoteCount; } }

        /// <summary>ノーツ中心からの距離の平均 (m)。<b>小さいほど良い。</b></summary>
        public double CutDistanceMean { get; internal set; }

        /// <summary>要求された向きからの角度ずれの平均 (度)。<b>小さいほど良い。</b></summary>
        public double AngleErrorMean { get; internal set; }

        /// <summary>角度。100点満点。要求された向きに乗れば 100、直角にずれたら 0。</summary>
        public double AnglePercent { get { return SwingAnalyzer.AnglePercentOf(AngleErrorMean); } }

        /// <summary>角度点の平均 (0-100)。振りかぶり 70 + 振り抜き 30。</summary>
        public double SwingAngleScore { get; internal set; }

        /// <summary>振りかぶりの点の平均 (0-70)。BeatLeader のリングの外側・上。</summary>
        public double BeforeCutScore { get; internal set; }

        /// <summary>中心点の平均 (0-15)。BeatLeader のリングの外側・中。</summary>
        public double CutDistanceScore { get; internal set; }

        /// <summary>振り抜きの点の平均 (0-30)。BeatLeader のリングの外側・下。</summary>
        public double AfterCutScore { get; internal set; }

        /// <summary>1ノーツあたりの平均スコア (0-115)。BeatLeader のリングの中の数字。</summary>
        public double AverageCut { get; internal set; }

        /// <summary>
        /// 振りかぶりの角度 (%)。BeatLeader の <c>PRE</c>。
        /// クランプしていない生の rating の平均なので、<b>100% を超える</b>。
        /// </summary>
        public double PreSwing { get; internal set; }

        /// <summary>振り抜きの角度 (%)。BeatLeader の <c>POST</c>。</summary>
        public double PostSwing { get; internal set; }

        /// <summary>時間依存。BeatLeader の <c>TD</c>。小さいほど良い。</summary>
        public double TimeDependence { get; internal set; }

        /// <summary>1振りの平均の長さ (m)。振り込みと振り抜きを含む道のり。</summary>
        public double SwingLength { get; internal set; }

        /// <summary>
        /// ノーツからノーツまでの距離 (m)。<b>再現性を割合に直すときの分母。</b>
        ///
        /// 道のり（<see cref="SwingLength"/>）で割ると分母が大きくなりすぎる。
        /// ブレード先端は振り抜きでノーツのはるか先まで回るので、
        /// 同じ「ずれ」でも点が甘く出る。跳ぶ距離で割る方が見た目と合う。
        /// </summary>
        public double JumpSpan { get; internal set; }

        /// <summary>
        /// 再現性 (%)。<b>すべての振りが1本の線に重なれば 100</b>。
        /// ずれが振り幅と同じだけあれば 0。
        ///
        /// <b>本数を見ていない。</b>3本だけ綺麗に振っても 100 になるので、
        /// 順位を付けるなら <see cref="ReproducibilityPercent"/>（再現精度 %）の方を使う。
        /// </summary>
        public double Score { get; internal set; }

        /// <summary>
        /// 再現スコア。<c>再現性 % × 振り数</c>。
        /// 綺麗さと本数の両方が要る形にした素点。
        /// </summary>
        public double ReproducibilityScore { get { return Score * SwingCount; } }

        /// <summary>
        /// 再現精度 %。満点は「振れるはずのノーツを全部、再現性 100 で振る」。
        ///
        /// <code>再現スコア / (100 × 振れるはずの数) × 100</code>
        ///
        /// 分母が<b>実際に振れた数ではない</b>のが要点。
        /// 再現性そのものは本数を見ないので、少ししか振らずに崩れる前にやめた記録が
        /// 上位に出てしまう。ミスして振れなかったぶんはここで引かれる。
        ///
        /// 分母は <see cref="SwingableCount"/>（＝ノーツ数）。
        /// ミス0・再現100 ならちょうど 100% になる。
        /// </summary>
        public double ReproducibilityPercent
        {
            get { return SwingableCount == 0 ? 0.0 : ReproducibilityScore / SwingableCount; }
        }

        /// <summary>
        /// 精度 %。<b>ミスを 0 点として数えた</b>素点の割合。
        ///
        /// <code>切れたノーツの素点の合計 / (115 × ノーツ数) × 100</code>
        ///
        /// 総合 /115 は切れたノーツだけの平均なので、ミスが多くても下がらない。
        /// 実際にどれだけ取れているかはこちらで見る。
        /// コンボ倍率は入っていない（1ノーツあたりで見る値）。
        /// </summary>
        public double Accuracy
        {
            get
            {
                return NoteCount == 0 ? 0.0
                    : AverageCut * GoodCount / (NoteScore.Max * (double)NoteCount) * 100.0;
            }
        }
    }

    public sealed class ReplayScore : ICutStats
    {
        public Replay Replay { get; internal set; }
        public List<SwingScore> PerHand { get; internal set; } = new List<SwingScore>();

        /// <summary>両手をまとめた、直線からのずれ (m)。</summary>
        public double TrajectorySpread { get; internal set; }

        /// <summary>両手をまとめた束の幅 (m)。</summary>
        public double PathScatter { get; internal set; }

        /// <summary>両手をまとめた往復のずれ (m)。</summary>
        public double RoundTripGap { get; internal set; }



        /// <summary>打点ずれの平均 (ms)。＋が遅れ。両手ぶん。</summary>
        public double TimeDeviationMean { get; internal set; }

        /// <summary>打点ずれのばらつき (ms)。両手ぶん。</summary>
        public double TimeDeviationSpread { get; internal set; }

        /// <summary>
        /// 再現性 (%)。1本線で 100。左右を<b>手ごとに均等</b>に平均したもの。
        /// <b>本数を見ていない</b>ので、順位付けには <see cref="ReproducibilityPercent"/>（再現精度 %）を使う。
        /// </summary>
        public double Score { get; internal set; }

        /// <summary>再現スコア。手ごとの「再現性 % × 振り数」の合計。</summary>
        public double ReproducibilityScore { get; internal set; }

        /// <summary>
        /// 再現精度 %。<c>再現スコア / (100 × 両手の振れるはずの数) × 100</c>。
        /// <b>順位はこれで付ける。</b>綺麗さと本数の両方が要る。
        /// </summary>
        public double ReproducibilityPercent { get; internal set; }

        /// <summary>精度 %。ミスを 0 点として数えた素点の割合。</summary>
        public double Accuracy { get; internal set; }

        /// <summary>
        /// 譜面のノーツ数（両手ぶん）。<b>満点の分母。</b>
        /// 譜面が見つからなければリプレイに出たぶん。
        /// </summary>
        public int NoteCount { get; internal set; }

        /// <summary>
        /// 譜面が要求する振りの数。<b>両手のノーツ数と同じ。</b>
        /// 循環なので最初のノーツにも前の点が決まっていて、そこも1本の振りになる。
        /// </summary>
        public int SwingableCount { get; internal set; }

        /// <summary>その手の譜面ノーツ数。振っていない手ぶんもここに入る。</summary>
        public int MapNotesFor(int saberType)
        {
            return saberType == 0 ? MapNotesLeft : MapNotesRight;
        }

        /// <summary>左手ぶんの譜面ノーツ数。</summary>
        public int MapNotesLeft { get; internal set; }

        /// <summary>右手ぶんの譜面ノーツ数。</summary>
        public int MapNotesRight { get; internal set; }

        /// <summary>要求された向きからの角度ずれの平均 (度)。両手ぶん。</summary>
        public double AngleErrorMean { get; internal set; }

        /// <summary>角度。100点満点。要求された向きに乗れば 100、直角にずれたら 0。</summary>
        public double AnglePercent { get { return SwingAnalyzer.AnglePercentOf(AngleErrorMean); } }

        /// <summary>ノーツ中心からの距離の平均 (m)。両手ぶん。</summary>
        public double CutDistanceMean { get; internal set; }

        /// <summary>角度点の平均 (0-100)。両手ぶん。</summary>
        public double SwingAngleScore { get; internal set; }

        /// <summary>1ノーツあたりの平均スコア (0-115)。両手ぶん。</summary>
        public double AverageCut { get; internal set; }

        /// <summary>振りかぶりの点の平均 (0-70)。両手ぶん。</summary>
        public double BeforeCutScore { get; internal set; }

        /// <summary>振り抜きの点の平均 (0-30)。両手ぶん。</summary>
        public double AfterCutScore { get; internal set; }

        /// <summary>振りかぶりの角度 (%)。<c>PRE</c>。両手ぶん。</summary>
        public double PreSwing { get; internal set; }

        /// <summary>振り抜きの角度 (%)。<c>POST</c>。両手ぶん。</summary>
        public double PostSwing { get; internal set; }

        /// <summary>有効振り数。両手ぶんの合計。</summary>
        public int SwingCount { get; internal set; }

        /// <summary>同じ振りを重ねたときの束の幅 (m)。<see cref="PathScatter"/> と同じ。</summary>
        double ICutStats.PathScatter { get { return PathScatter; } }

        /// <summary>時間依存 (TD)。両手ぶん。</summary>
        public double TimeDependence { get; internal set; }

        /// <summary>中心点の平均 (0-15)。両手ぶん。</summary>
        public double CutDistanceScore { get; internal set; }

        /// <summary>手ごとの再現性 (%)。見つからない手は null。</summary>
        public double? ScoreFor(int saberType)
        {
            foreach (var hand in PerHand)
                if (hand.SaberType == saberType) return hand.Score;
            return null;
        }

        public SwingScore Hand(int saberType)
        {
            foreach (var hand in PerHand)
                if (hand.SaberType == saberType) return hand;
            return null;
        }

        /// <summary>
        /// ノーツ間隔 (ms)。曲名に書いてある指定値（<c>... axis 100ms</c>）。
        /// 実測の中央値は打点のぶれを含むので、表示には使わない
        /// （100ms 指定が 99.7ms と出ても意味がない）。
        /// 曲名から読めなければ実測の中央値にする。
        /// BPM に直すのは <c>Tempo.BpmFromIntervalMs</c>（細分化の指定が要る）。
        /// </summary>
        public double NoteIntervalMs { get; internal set; }

        /// <summary>
        /// 同じ手の打点の間隔 (ms)。<b>片手が何 ms ごとに振っているか。</b>
        ///
        /// 軸の間隔（<see cref="NoteIntervalMs"/>）と同じとは限らない。
        /// 片手ずつ（split）なら同じ値になるが、両手交互（alt）なら倍になる。
        /// 振りの速さを見るならこちら。
        /// </summary>
        public double HandIntervalMs { get; internal set; }

        public int GoodCount { get; internal set; }
        public int MissCount { get; internal set; }
        public double DurationSeconds { get; internal set; }

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture,
                Lang.T("再現性 {0:0.0} %   角度点 {1:0.0}/100   中心点 {2:0.0}/15   打点ずれ {3:+0.0;-0.0;0.0} ms",
                       "Reproducibility {0:0.0} %   angle {1:0.0}/100   center {2:0.0}/15   timing {3:+0.0;-0.0;0.0} ms"),
                Score, SwingAngleScore, CutDistanceScore, TimeDeviationMean);
        }
    }

    /// <summary>
    /// リプレイから再現性を出す。
    ///
    /// 1振りぶんの軌道を「前のノーツを切った瞬間から次を切る瞬間まで」で切り出し、
    /// 弧長で等間隔に並べ直してから重ねる。時間で正規化すると振りの速さの違いが
    /// そのままずれになってしまうので、弧長で取る（設計メモ §8）。
    ///
    /// 同じ遷移（どこからどこへ振ったか）どうしでのみ比較する。
    /// 往路と復路を混ぜると、別物を平均することになる。
    /// </summary>
    public static class SwingAnalyzer
    {
        /// <summary>軌道を何点に並べ直すか。</summary>
        public const int SamplesPerSwing = 24;

        /// <summary>これ未満の本数しか無い遷移は、平均が当てにならないので使わない。</summary>
        public const int MinimumSwingsPerGroup = 3;

        public static ReplayScore Analyze(Replay replay)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));

            var result = new ReplayScore { Replay = replay };
            result.GoodCount = replay.Notes.Count(n => n.EventType == NoteEventType.Good);
            result.MissCount = replay.Notes.Count(n => n.EventType == NoteEventType.Miss);
            result.DurationSeconds = replay.Frames.Count > 0 ? replay.Frames[replay.Frames.Count - 1].Time : 0.0;
            ResolveTempo(replay, result);

            foreach (var saberType in new[] { 0, 1 })
            {
                var hand = AnalyzeHand(replay, saberType);
                if (hand != null) result.PerHand.Add(hand);
            }

            Combine(result);
            return result;
        }

        /// <summary>
        /// 左右をまとめて1つの数字にする。
        ///
        /// <b>手ごとに均等</b>に平均する。振りの本数で重み付けしてはいけない。
        /// 片手ずつのドリルは左右が同じ本数叩く前提なのに、ミスした手ほど
        /// 振りが少なくなって重みが軽くなる。実測でも、左 32 本 / 右 272 本のとき
        /// 左の重みは 10.5% しか無く、再現性 左 26.5 / 右 87.7 が全体 81.3 と出て
        /// <b>崩れている方の手がほとんど数字に出なかった</b>。
        ///
        /// ただし本体と同じ出し方の素点（総合 /115・TD）だけは<b>ノーツ単位の平均のまま</b>。
        /// BeatLeader の成績画面と同じ数字を出すのが目的の欄で、
        /// あちらは全ノーツを平均している。手ごとに均すと突き合わせられなくなる。
        ///
        /// 再現精度 % と精度 % も均等平均にしてはいけない。どちらも
        /// 「取った合計 ÷ 満点の合計」の割合なので、<b>足してから割る</b>。
        /// 手ごとに均すと、ノーツ数の違う手を同じ重みで数えることになって割合が壊れる。
        /// </summary>
        /// <summary>
        /// 譜面のノーツ数を満点として入れ直す。
        ///
        /// <b>振っていない手ぶんも満点に入る。</b>「右30秒 → 左30秒」のドリルを
        /// 右で切り上げた記録は、左のノーツがリプレイに1件も残らない。
        /// リプレイに出たぶんだけを満点にすると、その記録が片手だけで満点になってしまう。
        /// </summary>
        public static void ApplyMapNotes(ReplayScore score, int leftNotes, int rightNotes)
        {
            if (score == null) throw new ArgumentNullException(nameof(score));

            score.MapNotesLeft = Math.Max(leftNotes, 0);
            score.MapNotesRight = Math.Max(rightNotes, 0);

            foreach (var hand in score.PerHand)
            {
                int notes = score.MapNotesFor(hand.SaberType);

                // 譜面より多く記録されることは無いが、読み違いで縮めては困るので大きい方を採る。
                hand.NoteCount = Math.Max(notes, hand.GoodCount + hand.MissCount);
            }

            Rescale(score);
        }

        /// <summary>満点の分母が決まったところで、割合の欄を出し直す。</summary>
        private static void Rescale(ReplayScore score)
        {
            var hands = score.PerHand;

            score.NoteCount = score.MapNotesLeft + score.MapNotesRight;
            score.SwingableCount = score.NoteCount;

            score.ReproducibilityPercent = score.SwingableCount > 0
                ? score.ReproducibilityScore / score.SwingableCount
                : 0.0;

            score.Accuracy = score.NoteCount > 0
                ? hands.Sum(h => h.AverageCut * h.GoodCount) / (NoteScore.Max * (double)score.NoteCount) * 100.0
                : 0.0;
        }

        private static void Combine(ReplayScore result)
        {
            var hands = result.PerHand;
            if (hands.Count == 0) return;

            // 再現性は手ごとに跳び幅で割り終わっているので、点の方を平均する。
            // ずれを混ぜてから割ると、左右で跳び幅が違うドリルで意味が変わる。
            result.Score = hands.Average(h => h.Score);

            result.TrajectorySpread = hands.Average(h => h.TrajectorySpread);
            result.PathScatter = hands.Average(h => h.PathScatter);
            result.RoundTripGap = hands.Average(h => h.RoundTripGap);
            result.TimeDeviationMean = hands.Average(h => h.TimeDeviationMean);
            result.TimeDeviationSpread = hands.Average(h => h.TimeDeviationSpread);
            result.AngleErrorMean = hands.Average(h => h.AngleErrorMean);
            result.CutDistanceMean = hands.Average(h => h.CutDistanceMean);

            // 再現スコアと精度は割合なので、足し合わせてから割る。
            // 手ごとに均すと、ノーツ数の違う手を同じ重みで数えることになって割合が崩れる。
            result.ReproducibilityScore = hands.Sum(h => h.ReproducibilityScore);

            foreach (var hand in hands)
            {
                if (hand.SaberType == 0) result.MapNotesLeft = hand.NoteCount;
                else result.MapNotesRight = hand.NoteCount;
            }

            Rescale(result);

            int swings = hands.Sum(h => h.SwingCount);
            result.SwingCount = swings;
            if (swings == 0) return;

            result.SwingAngleScore = hands.Sum(h => h.SwingAngleScore * h.SwingCount) / swings;
            result.CutDistanceScore = hands.Sum(h => h.CutDistanceScore * h.SwingCount) / swings;
            result.AverageCut = hands.Sum(h => h.AverageCut * h.SwingCount) / swings;
            result.TimeDependence = hands.Sum(h => h.TimeDependence * h.SwingCount) / swings;

            // 素点と同じ重み付け。手ごとに均すと、本体の成績画面と突き合わせられなくなる。
            result.BeforeCutScore = hands.Sum(h => h.BeforeCutScore * h.SwingCount) / swings;
            result.AfterCutScore = hands.Sum(h => h.AfterCutScore * h.SwingCount) / swings;
            result.PreSwing = hands.Sum(h => h.PreSwing * h.SwingCount) / swings;
            result.PostSwing = hands.Sum(h => h.PostSwing * h.SwingCount) / swings;
        }

        /// <summary>
        /// 曲名に書いてある速さから間隔を決める。実測は打点のぶれを含むので、
        /// 指定値が読めるならそちらを使う（100ms 指定が 99.7ms と出ても意味がない）。
        ///
        /// 名前が <b>BPM</b>（<c>… axis 300 BPM</c>、旧 <c>300 EBPM</c>）なら<b>片手の間隔</b>がそのまま決まる。
        /// 軸の間隔はそこから、片手が軸の何倍で振っているかを実測で見て割り戻す。
        ///
        /// 名前が旧い <b>ms</b> 表記（<c>… axis 100ms</c>）なら書いてあるのは<b>軸の間隔</b>で、
        /// 片手の間隔は実測を軸の整数倍に丸めて出す。
        /// </summary>
        private static void ResolveTempo(Replay replay, ReplayScore result)
        {
            double hand = DeclaredHandInterval(replay.Info.SongName);
            if (hand > 0)
            {
                result.HandIntervalMs = hand;

                double axis = MedianNoteInterval(replay);
                double multiple = axis > 0 ? Math.Max(1.0, Math.Round(hand / axis)) : 1.0;
                result.NoteIntervalMs = hand / multiple;
                return;
            }

            double declared = DeclaredNoteInterval(replay.Info.SongName);
            result.NoteIntervalMs = declared > 0 ? declared : MedianNoteInterval(replay);
            result.HandIntervalMs = MedianHandInterval(replay, result.NoteIntervalMs);
        }

        /// <summary>
        /// 曲名に書いてある片手の速さから出した片手の間隔 (ms)。
        /// <c>… axis 300 BPM</c> なら 100。読めなければ 0。
        ///
        /// 名前の BPM は<b>片手の速さ</b>なので、細分化も手の並べ方も要らずに値が決まる
        /// （<c>30000 / 片手の間隔(ms)</c>）。以前は同じ値を <c>300 EBPM</c> と書いていたので、
        /// そちらも読む。"EBPM" の中にも "BPM" があるので EBPM を先に見る
        /// （BPM で探すと直前が "E" で数字にならず、読めずに終わる）。
        /// </summary>
        public static double DeclaredHandInterval(string songName)
        {
            double bpm = TrailingNumber(songName, "ebpm");
            if (bpm <= 0) bpm = TrailingNumber(songName, "bpm");
            return bpm > 0 ? Tempo.HandIntervalMsFromEbpm(bpm) : 0.0;
        }

        /// <summary>
        /// 曲名の末尾に書いてあるノーツ間隔 (ms)。<c>Drill R:4&gt;b L:1&gt;a axis 100ms</c> なら 100。
        /// 読めなければ 0。
        /// </summary>
        public static double DeclaredNoteInterval(string songName)
        {
            // BPM 表記には "ms" が出てこないので、取り違えの心配は要らない。
            return TrailingNumber(songName, "ms");
        }

        /// <summary>
        /// 曲名の中の「数値＋単位」を読む。単位は最後に出てくるものを見る。
        /// 単位の直前が数字でなければ 0（"Cendrillon" の "ms" のような当たりを外す）。
        /// </summary>
        private static double TrailingNumber(string songName, string unit)
        {
            if (string.IsNullOrEmpty(songName)) return 0.0;

            int end = songName.LastIndexOf(unit, StringComparison.OrdinalIgnoreCase);
            if (end <= 0) return 0.0;

            // 単位の手前の空白は空けて書くこともある（"300 BPM"）。
            int start = end;
            while (start > 0 && songName[start - 1] == ' ') start--;

            int digits = start;
            while (digits > 0 && (char.IsDigit(songName[digits - 1]) || songName[digits - 1] == '.')) digits--;
            if (digits == start) return 0.0;

            double value;
            return double.TryParse(songName.Substring(digits, start - digits),
                NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0.0;
        }

        /// <summary>
        /// 同じ手のノーツの間隔 (ms)。実測の中央値を、軸の間隔の整数倍に丸める。
        ///
        /// 片手ずつなら軸と同じ、両手交互なら倍。実測そのままだと端数が出るので、
        /// 何倍になっているかだけを実測から決めて、値は指定値から作る。
        ///
        /// <b>ミスしたノーツも数える。</b>これは譜面がどう並んでいるかの話で、
        /// 当たったかどうかとは関係ない。当たったものだけで測ると、
        /// ミスの多い手で間隔が飛んで、譜面より遅い値が出る。
        ///
        /// 左右のうち<b>短い方</b>を採る。ミスは間隔を伸ばす方にしか働かないので、
        /// 短い方が譜面の間隔に近い。
        /// </summary>
        public static double MedianHandInterval(Replay replay, double axisIntervalMs)
        {
            double shortest = 0.0;

            foreach (int colorType in new[] { 0, 1 })
            {
                var times = replay.Notes
                    .Where(n => (n.EventType == NoteEventType.Good || n.EventType == NoteEventType.Miss)
                                && n.ColorType == colorType)
                    .Select(n => (double)n.EventTime)
                    .OrderBy(t => t)
                    .ToList();
                if (times.Count < 2) continue;

                var gaps = new List<double>();
                for (int i = 1; i < times.Count; i++) gaps.Add((times[i] - times[i - 1]) * 1000.0);

                gaps.Sort();
                double median = gaps[gaps.Count / 2];

                if (shortest <= 0.0 || median < shortest) shortest = median;
            }

            if (shortest <= 0.0) return axisIntervalMs;
            if (axisIntervalMs <= 0) return shortest;

            // 軸の何倍か（片手ずつなら1、両手交互なら2）。
            double multiple = Math.Max(1.0, Math.Round(shortest / axisIntervalMs));
            return axisIntervalMs * multiple;
        }

        /// <summary>
        /// 隣り合う打点の間隔の中央値 (ms)。
        /// 平均ではなく中央値なのは、ミスやセットの切れ目で開いた所に引きずられないため。
        /// </summary>
        public static double MedianNoteInterval(Replay replay)
        {
            var times = replay.Notes
                .Where(n => n.EventType == NoteEventType.Good)
                .Select(n => (double)n.EventTime)
                .OrderBy(t => t)
                .ToList();

            if (times.Count < 2) return 0.0;

            var gaps = new List<double>();
            for (int i = 1; i < times.Count; i++) gaps.Add((times[i] - times[i - 1]) * 1000.0);

            gaps.Sort();
            return gaps[gaps.Count / 2];
        }

        private static SwingScore AnalyzeHand(Replay replay, int saberType)
        {
            var cuts = replay.Notes
                .Where(n => n.EventType == NoteEventType.Good && n.Cut != null && n.Cut.SaberType == saberType)
                .OrderBy(n => n.EventTime)
                .ToList();

            if (cuts.Count < MinimumSwingsPerGroup + 1) return null;

            // 遷移ごとに軌道を集める。キーは「前の配置 → 今の配置」。
            var groups = new Dictionary<string, List<Vector3[]>>();
            var spans = new Dictionary<string, double>();
            var lines = new Dictionary<string, Line>();

            // 振りの向き（フォア／バック）は<b>その振りが終わるノーツ</b>で決める。
            // 打点ごとの数字も遷移ごとの数字も同じノーツで振り分けられるので、
            // 2つの表が食い違わない。
            // 偶奇の当てにはミスも含めた並びを使う。切れたノーツだけで数えると、
            // ミスで1つ飛んだ所から先の偶奇がまるごと入れ替わってしまう。
            // 並べるのはノーツの時刻で（HandSequence と同じ理由。ミスは記録の時刻が遅れる）。
            var handNotes = replay.Notes
                .Where(n => n.ColorType == saberType &&
                       (n.EventType == NoteEventType.Good || n.EventType == NoteEventType.Miss))
                .OrderBy(n => n.SpawnTime)
                .ThenBy(n => n.EventTime)
                .ToList();

            var forehand = new Dictionary<ReplayNote, bool>();
            for (int i = 0; i < handNotes.Count; i++) forehand[handNotes[i]] = IsForehand(handNotes[i], i);

            var groupFore = new Dictionary<string, bool>();

            // 打点は間隔ではなくノーツごとの判定ずれで測る。
            var deviations = cuts.Select(c => c.Cut.TimeDeviation * 1000.0).ToList();

            // 最初の1本も振り。循環の1つ前の点から入ってくる。
            // その手の最初のノーツを切れていなければ、入ってくる振りは無い。
            var sequence = HandSequence(replay, saberType);
            var first = VirtualPredecessor(cuts);
            if (first != null && sequence.Count > 0 && sequence[0] == cuts[0])
                AddTransition(groups, spans, lines, groupFore, forehand, replay, saberType, first, cuts[0],
                    cuts[0].EventTime - (float)(MedianGap(cuts) / 1000.0));

            foreach (var pair in CleanPairs(sequence, saberType))
                AddTransition(groups, spans, lines, groupFore, forehand, replay, saberType, pair.Key, pair.Value,
                    pair.Key.EventTime);

            // 先に格子の当てはめ誤差を1つ求める。往復で反転する成分だけがそこに入る。
            var usedLines = new List<Line>();
            var usedOffsets = new List<double>();

            foreach (var pair in groups)
            {
                if (pair.Value.Count < MinimumSwingsPerGroup) continue;
                usedLines.Add(lines[pair.Key]);
                usedOffsets.Add(MeanSignedDistance(pair.Value, lines[pair.Key]));
            }

            double anchorX, anchorY;
            FitAnchor(usedLines, usedOffsets, out anchorX, out anchorY);

            double spreadSum = 0.0;
            double scatterSum = 0.0;
            double lengthSum = 0.0;
            double spanSum = 0.0;
            int spreadWeight = 0;

            foreach (var pair in groups)
            {
                var group = pair.Value;
                if (group.Count < MinimumSwingsPerGroup) continue;

                spreadSum += SpreadFromLine(group, lines[pair.Key], anchorX, anchorY) * group.Count;
                scatterSum += SpreadFromMean(group) * group.Count;
                lengthSum += group.Sum(PathLength);
                spanSum += spans[pair.Key] * group.Count;
                spreadWeight += group.Count;
            }

            if (spreadWeight == 0) return null;

            double spreadMean = spreadSum / spreadWeight;
            double scatterMean = scatterSum / spreadWeight;
            double lengthMean = lengthSum / spreadWeight;
            double spanMean = spanSum / spreadWeight;

            double roundTrip = RoundTripGapOf(groups, lines, spanMean);

            var misses = replay.Notes
                .Where(n => n.EventType == NoteEventType.Miss && n.ColorType == saberType)
                .ToList();

            return new SwingScore
            {
                SaberType = saberType,
                Fore = DirectionOf(true, cuts, misses, forehand, groups, groupFore),
                Back = DirectionOf(false, cuts, misses, forehand, groups, groupFore),
                SwingCount = spreadWeight,
                TrajectorySpread = spreadMean,
                PathScatter = scatterMean,
                RoundTripGap = roundTrip,
                SwingLength = lengthMean,
                JumpSpan = spanMean,
                TimeDeviationMean = deviations.Average(),
                TimeDeviationSpread = StandardDeviation(deviations),
                GoodCount = cuts.Count,
                MissCount = replay.Notes.Count(n => n.EventType == NoteEventType.Miss && n.ColorType == saberType),
                NoteCount = cuts.Count + replay.Notes.Count(n => n.EventType == NoteEventType.Miss && n.ColorType == saberType),
                CutDistanceMean = cuts.Average(c => (double)c.Cut.CutDistanceToCenter),
                AngleErrorMean = cuts.Average(c => Math.Abs((double)c.Cut.CutDirDeviation)),
                SwingAngleScore = cuts.Average(c => (double)NoteScore.SwingAngle(c.Cut)),
                BeforeCutScore = cuts.Average(c => (double)NoteScore.BeforeCut(c.Cut)),
                CutDistanceScore = cuts.Average(c => (double)NoteScore.CutDistance(c.Cut)),
                AfterCutScore = cuts.Average(c => (double)NoteScore.AfterCut(c.Cut)),
                AverageCut = cuts.Average(c => (double)NoteScore.Total(c.Cut)),

                // BeatLeader と同じで、rating はクランプせずに平均する。
                PreSwing = cuts.Average(c => (double)c.Cut.BeforeCutRating) * 100.0,
                PostSwing = cuts.Average(c => (double)c.Cut.AfterCutRating) * 100.0,
                TimeDependence = cuts.Average(c => NoteScore.TimeDependence(c.Cut)),
                Score = RoundTripPercent(roundTrip, spanMean),
            };
        }

        /// <summary>
        /// その振りがフォア（切り下げ）か。<b>振りが終わるノーツの矢印</b>で決める。
        ///
        /// 矢印に上下の成分が無いとき（水平・ドット）は決めようがないので、
        /// 循環の偶奇を代わりに使う。生成側でクリックを鳴らす向きを決めるときと
        /// 同じ扱い（<c>ClickPlanner.IsDownStep</c>）。
        /// </summary>
        /// <param name="index">その手が何回目に叩くノーツか。ミスも数に入れる。</param>
        public static bool IsForehand(ReplayNote note, int index)
        {
            var d = (CutDirection)note.CutDirection;

            if (d.IsDownward()) return true;
            if (d.IsUpward()) return false;

            return index % 2 == 0;
        }

        /// <summary>覚えておいた向き。並びから外れたノーツ（仮の前の点）は偶数扱い。</summary>
        private static bool ForehandOf(Dictionary<ReplayNote, bool> forehand, ReplayNote note)
        {
            bool fore;
            return forehand.TryGetValue(note, out fore) ? fore : IsForehand(note, 0);
        }

        /// <summary>
        /// 片側（フォアかバック）だけの内訳を作る。その向きの振りが無ければ null。
        ///
        /// 重みの付け方はまとめた数字と同じ。打点ごとの欄はノーツ単位の平均、
        /// 遷移ごとの欄は本数で重み付けした平均。
        /// </summary>
        private static DirectionScore DirectionOf(
            bool forehandSide,
            List<ReplayNote> cuts,
            List<ReplayNote> misses,
            Dictionary<ReplayNote, bool> forehand,
            Dictionary<string, List<Vector3[]>> groups,
            Dictionary<string, bool> groupFore)
        {
            var side = cuts.Where(c => ForehandOf(forehand, c) == forehandSide).ToList();
            if (side.Count == 0) return null;

            double scatterSum = 0.0;
            int weight = 0;

            foreach (var pair in groups)
            {
                var group = pair.Value;
                if (group.Count < MinimumSwingsPerGroup) continue;
                if (groupFore[pair.Key] != forehandSide) continue;

                scatterSum += SpreadFromMean(group) * group.Count;
                weight += group.Count;
            }

            var deviations = side.Select(c => c.Cut.TimeDeviation * 1000.0).ToList();

            return new DirectionScore
            {
                Forehand = forehandSide,
                GoodCount = side.Count,
                MissCount = misses.Count(m => ForehandOf(forehand, m) == forehandSide),
                SwingCount = weight,

                AverageCut = side.Average(c => (double)NoteScore.Total(c.Cut)),
                BeforeCutScore = side.Average(c => (double)NoteScore.BeforeCut(c.Cut)),
                AfterCutScore = side.Average(c => (double)NoteScore.AfterCut(c.Cut)),
                CutDistanceScore = side.Average(c => (double)NoteScore.CutDistance(c.Cut)),
                TimeDependence = side.Average(c => NoteScore.TimeDependence(c.Cut)),

                // まとめた数字と同じで、rating はクランプせずに平均する。
                PreSwing = side.Average(c => (double)c.Cut.BeforeCutRating) * 100.0,
                PostSwing = side.Average(c => (double)c.Cut.AfterCutRating) * 100.0,

                AngleErrorMean = side.Average(c => Math.Abs((double)c.Cut.CutDirDeviation)),
                CutDistanceMean = side.Average(c => (double)c.Cut.CutDistanceToCenter),
                TimeDeviationMean = deviations.Average(),
                TimeDeviationSpread = StandardDeviation(deviations),

                PathScatter = weight == 0 ? 0.0 : scatterSum / weight,
            };
        }

        /// <summary>
        /// 遷移を1本ぶん積む。
        /// </summary>
        /// <param name="from">前の点。最初の1本では循環から決めた仮の点。</param>
        /// <param name="fromTime">軌道を切り出す始まり。仮の点では1振りぶん手前。</param>
        private static void AddTransition(
            Dictionary<string, List<Vector3[]>> groups,
            Dictionary<string, double> spans,
            Dictionary<string, Line> lines,
            Dictionary<string, bool> groupFore,
            Dictionary<ReplayNote, bool> forehand,
            Replay replay, int saberType, ReplayNote from, ReplayNote to, float fromTime)
        {
            // ミスを挟むと同じノーツへ戻る。跳んでいないので振りとして数えない。
            if (from.LineIndex == to.LineIndex && from.LineLayer == to.LineLayer) return;

            var path = ExtractPath(replay, saberType, fromTime, to.EventTime);
            if (path == null) return;

            string key = from.LineIndex + "," + from.LineLayer + ">" +
                         to.LineIndex + "," + to.LineLayer;

            List<Vector3[]> list;
            if (!groups.TryGetValue(key, out list))
            {
                list = new List<Vector3[]>();
                groups[key] = list;
                spans[key] = GridDistance(from, to);
                lines[key] = Line.Between(from, to);
                groupFore[key] = ForehandOf(forehand, to);
            }
            list.Add(path);
        }

        /// <summary>
        /// その手が叩くはずだったノーツを、譜面の順に。切れたもの・切り損ねたもの・取り逃したものを全部。
        /// </summary>
        /// <remarks>
        /// 並べるのは<b>ノーツの時刻</b>（<c>SpawnTime</c>）。記録の時刻（<c>EventTime</c>）ではない。
        /// 取り逃しはノーツが通り過ぎてから判定されるので、記録の時刻では次のノーツを切った後に来る。
        /// その順で並べると「10 → 02（ミス）→ 10」が「10 → 10 → 02（ミス）」になり、
        /// ミスをはさんだ2つが隣どうしに見えてしまう。
        /// </remarks>
        public static List<ReplayNote> HandSequence(Replay replay, int saberType)
        {
            return replay.Notes
                .Where(n => n.ColorType == saberType &&
                       (n.EventType == NoteEventType.Good ||
                        n.EventType == NoteEventType.Bad ||
                        n.EventType == NoteEventType.Miss))
                .OrderBy(n => n.SpawnTime)
                .ThenBy(n => n.EventTime)
                .ToList();
        }

        /// <summary>
        /// 振りとして使う2つ組。その手のノーツの並びで<b>隣り合う2つがどちらも切れたときだけ</b>。
        /// </summary>
        /// <remarks>
        /// 切れたノーツだけをつなぐと、間でミスしたところは前後のノーツが1本の振りになる。
        /// 下 → 上（ミス）→ 下 なら下から下への往復まるごと、ミスが2つ続けば1往復半が
        /// 1本として束に入り、束の太さも往復のずれも実際の振りを表さなくなる。
        /// </remarks>
        public static List<KeyValuePair<ReplayNote, ReplayNote>> CleanPairs(List<ReplayNote> sequence, int saberType)
        {
            var pairs = new List<KeyValuePair<ReplayNote, ReplayNote>>();

            for (int i = 1; i < sequence.Count; i++)
            {
                if (IsCleanCut(sequence[i - 1], saberType) && IsCleanCut(sequence[i], saberType))
                    pairs.Add(new KeyValuePair<ReplayNote, ReplayNote>(sequence[i - 1], sequence[i]));
            }

            return pairs;
        }

        private static bool IsCleanCut(ReplayNote note, int saberType)
        {
            return note.EventType == NoteEventType.Good && note.Cut != null && note.Cut.SaberType == saberType;
        }

        /// <summary>
        /// その手の最初のノーツの、1つ前にあたる点。決められなければ null。
        ///
        /// ドリルは循環なので、最初のノーツと同じ位置が次に出てくるまでが1周。
        /// その<b>1つ手前が、最初のノーツに入ってくる向きを決める点</b>になる。
        /// <c>b 8 b 8 …</c> なら b の1周は2本ぶんなので、前の点は 8。
        /// <c>9 4 c 1 9 …</c> なら1周は4本ぶんなので、9 の前は 1。
        /// </summary>
        public static ReplayNote VirtualPredecessor(List<ReplayNote> cuts)
        {
            if (cuts == null || cuts.Count < 2) return null;

            var start = cuts[0];

            for (int i = 1; i < cuts.Count; i++)
            {
                if (cuts[i].LineIndex != start.LineIndex || cuts[i].LineLayer != start.LineLayer) continue;

                var previous = cuts[i - 1];

                // 同じ位置が続くだけなら循環になっていない。
                return previous.LineIndex == start.LineIndex && previous.LineLayer == start.LineLayer
                    ? null : previous;
            }

            return null;
        }

        /// <summary>隣り合う打点の間隔の中央値 (ms)。最初の振りを切り出す幅に使う。</summary>
        private static double MedianGap(List<ReplayNote> cuts)
        {
            var gaps = new List<double>();
            for (int i = 1; i < cuts.Count; i++)
                gaps.Add((cuts[i].EventTime - cuts[i - 1].EventTime) * 1000.0);

            if (gaps.Count == 0) return 0.0;

            gaps.Sort();
            return gaps[gaps.Count / 2];
        }

        /// <summary>
        /// 2つの打点の間のセイバー軌道を、弧長で等間隔に並べ直す。
        ///
        /// 見るのは<b>ブレード先端</b>。リプレイに入っているのはコントローラの位置で、
        /// ノーツを切る所とは1 m ほど離れている。手首の返しはそこで初めて効くので、
        /// コントローラのままだと「同じ振りができているか」を取り逃がす。
        /// </summary>
        private static Vector3[] ExtractPath(Replay replay, int saberType, float fromTime, float toTime)
        {
            var points = new List<Vector3>();
            foreach (var frame in replay.Frames)
            {
                if (frame.Time < fromTime) continue;
                if (frame.Time > toTime) break;
                points.Add(frame.SaberTip(saberType));
            }

            // 点が少なすぎると弧長も測れない。
            if (points.Count < 4) return null;
            return ResampleByArcLength(points, SamplesPerSwing);
        }

        /// <summary>
        /// 折れ線を弧長で等間隔にサンプルし直す。
        /// 速さの違いを畳んで「通った形」だけを残すのが狙い。
        /// </summary>
        public static Vector3[] ResampleByArcLength(IReadOnlyList<Vector3> points, int samples)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (points.Count < 2) throw new ArgumentException("2点以上必要です。", nameof(points));
            if (samples < 2) throw new ArgumentOutOfRangeException(nameof(samples));

            var cumulative = new double[points.Count];
            for (int i = 1; i < points.Count; i++)
                cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i - 1], points[i]);

            double total = cumulative[points.Count - 1];
            var result = new Vector3[samples];

            // 動いていない（総弧長ゼロ）ときは全点同じ。
            if (total <= 1e-9)
            {
                for (int i = 0; i < samples; i++) result[i] = points[0];
                return result;
            }

            int cursor = 0;
            for (int i = 0; i < samples; i++)
            {
                double target = total * i / (samples - 1);
                while (cursor < points.Count - 2 && cumulative[cursor + 1] < target) cursor++;

                double segment = cumulative[cursor + 1] - cumulative[cursor];
                double t = segment <= 1e-9 ? 0.0 : (target - cumulative[cursor]) / segment;

                result[i] = new Vector3
                {
                    X = (float)(points[cursor].X + (points[cursor + 1].X - points[cursor].X) * t),
                    Y = (float)(points[cursor].Y + (points[cursor + 1].Y - points[cursor].Y) * t),
                    Z = (float)(points[cursor].Z + (points[cursor + 1].Z - points[cursor].Z) * t),
                };
            }

            return result;
        }

        /// <summary>ノーツとノーツを結ぶ直線。</summary>
        public struct Line
        {
            public double X, Y, DirX, DirY;

            /// <summary>直線に直角な向き。往路と復路で符号が反転する。</summary>
            public double NormalX { get { return DirY; } }
            public double NormalY { get { return -DirX; } }

            /// <summary>直線からの符号つき距離。</summary>
            public double SignedDistance(Vector3 point)
            {
                return (point.X - X) * NormalX + (point.Y - Y) * NormalY;
            }

            public static Line Between(ReplayNote from, ReplayNote to)
            {
                double ax = (from.LineIndex - (NoteGrid.LineCount - 1) / 2.0) * NoteGrid.LineSpacing;
                double ay = NoteGrid.BaseY(from.LineLayer);
                double bx = (to.LineIndex - (NoteGrid.LineCount - 1) / 2.0) * NoteGrid.LineSpacing;
                double by = NoteGrid.BaseY(to.LineLayer);

                double length = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                if (length < 1e-9) length = 1.0;

                return new Line { X = ax, Y = ay, DirX = (bx - ax) / length, DirY = (by - ay) / length };
            }
        }

        /// <summary>
        /// 振りが直線からどれだけ外れたかを RMS で。単位は m。
        ///
        /// 直線はノーツとノーツを結んだもの。位置は本体の格子で決まるので、
        /// どこを切ったかに左右されない。
        ///
        /// <paramref name="anchor"/> は格子の当てはめ誤差ぶんの平行移動。
        /// これは<b>空間に固定された1つのずれ</b>なので、往路と復路で符号が反転する。
        /// 往復で同じ側に寄っているぶん（＝振りが輪を描いている）は反転しないので残る。
        /// グループごとに平均を引くと、その輪までまとめて消えてしまう。
        /// </summary>
        public static double SpreadFromLine(IReadOnlyList<Vector3[]> paths, Line line, double anchorX, double anchorY)
        {
            if (paths == null || paths.Count == 0) return 0.0;

            double offset = anchorX * line.NormalX + anchorY * line.NormalY;

            double squared = 0.0;
            int count = 0;

            foreach (var path in paths)
                foreach (var point in path)
                {
                    double d = line.SignedDistance(point) - offset;
                    squared += d * d;
                    count++;
                }

            return Math.Sqrt(squared / count);
        }

        /// <summary>
        /// 格子の当てはめ誤差を、1つの平行移動として最小二乗で求める。
        ///
        /// 各遷移について「直線からの平均のずれ = 平行移動 · 直角向き」が成り立つはず。
        /// 往路と復路で直角向きが反転するので、反転する成分だけがここに入る。
        /// </summary>
        public static void FitAnchor(IReadOnlyList<Line> lines, IReadOnlyList<double> offsets,
                                     out double anchorX, out double anchorY)
        {
            anchorX = 0.0;
            anchorY = 0.0;
            if (lines == null || lines.Count == 0) return;

            // 正規方程式。向きが1本ぶんしか無い場合に備えて、ごく小さい正則化を入れる。
            double a = 1e-6, b = 0.0, c = 1e-6, u = 0.0, v = 0.0;

            for (int i = 0; i < lines.Count; i++)
            {
                double nx = lines[i].NormalX, ny = lines[i].NormalY;
                a += nx * nx;
                b += nx * ny;
                c += ny * ny;
                u += nx * offsets[i];
                v += ny * offsets[i];
            }

            double determinant = a * c - b * b;
            if (Math.Abs(determinant) < 1e-12) return;

            anchorX = (u * c - v * b) / determinant;
            anchorY = (a * v - b * u) / determinant;
        }

        /// <summary>直線からの平均のずれ。</summary>
        public static double MeanSignedDistance(IReadOnlyList<Vector3[]> paths, Line line)
        {
            double sum = 0.0;
            int count = 0;

            foreach (var path in paths)
                foreach (var point in path)
                {
                    sum += line.SignedDistance(point);
                    count++;
                }

            return count == 0 ? 0.0 : sum / count;
        }

        /// <summary>軸に沿って何区間に割って往復を比べるか。</summary>
        public const int RoundTripBins = 20;

        /// <summary>
        /// 往路と復路のずれ。行き帰りの2組がそろっているときだけ出す。
        ///
        /// それぞれの<b>平均軌道</b>を先に作ってから、断面ごとに差を取る。
        /// 1本ずつを共通の平均と比べる形にすると、ばらつきも入るかわりに
        /// 値が全体の平均あたりに寄って、左右の差が読み取りにくくなる。
        /// 見たいのは「行きと帰りが同じ曲線に乗っているか」なので、
        /// 平均どうしを比べる。
        /// </summary>
        private static double RoundTripGapOf(Dictionary<string, List<Vector3[]>> groups,
                                             Dictionary<string, Line> lines, double span)
        {
            var used = new List<string>();
            foreach (var pair in groups)
                if (pair.Value.Count >= MinimumSwingsPerGroup) used.Add(pair.Key);

            if (used.Count != 2 || span <= 0) return 0.0;

            // 2組で同じ軸を使う。向きが逆の直線で測ると符号が揃わない。
            var line = lines[used[0]];

            var forward = AxisProfile(MeanPath(groups[used[0]]), line, span);
            var backward = AxisProfile(MeanPath(groups[used[1]]), line, span);

            double squared = 0.0;
            int count = 0;

            for (int i = 0; i < RoundTripBins; i++)
            {
                if (double.IsNaN(forward[i]) || double.IsNaN(backward[i])) continue;

                double d = forward[i] - backward[i];
                squared += d * d;
                count++;
            }

            return count == 0 ? 0.0 : Math.Sqrt(squared / count);
        }

        /// <summary>
        /// 軸に沿った位置ごとの横ずれ。0 がノーツA、1 がノーツB。
        /// ノーツの外側は通らないので NaN のまま返す。
        /// </summary>
        public static double[] AxisProfile(Vector3[] path, Line line, double span)
        {
            var result = new double[RoundTripBins];
            for (int i = 0; i < RoundTripBins; i++) result[i] = double.NaN;

            for (int i = 1; i < path.Length; i++)
            {
                double t0 = ((path[i - 1].X - line.X) * line.DirX + (path[i - 1].Y - line.Y) * line.DirY) / span;
                double t1 = ((path[i].X - line.X) * line.DirX + (path[i].Y - line.Y) * line.DirY) / span;

                double d0 = line.SignedDistance(path[i - 1]);
                double d1 = line.SignedDistance(path[i]);

                double low = Math.Min(t0, t1), high = Math.Max(t0, t1);

                for (int b = 0; b < RoundTripBins; b++)
                {
                    double t = (b + 0.5) / RoundTripBins;
                    if (t < low || t > high) continue;

                    double f = Math.Abs(t1 - t0) < 1e-9 ? 0.0 : (t - t0) / (t1 - t0);
                    result[b] = d0 + (d1 - d0) * f;
                }
            }

            return result;
        }

        /// <summary>点ごとに平均した軌道。</summary>
        public static Vector3[] MeanPath(IReadOnlyList<Vector3[]> paths)
        {
            int samples = paths[0].Length;
            var mean = new Vector3[samples];

            for (int i = 0; i < samples; i++)
            {
                double x = 0, y = 0, z = 0;
                foreach (var path in paths) { x += path[i].X; y += path[i].Y; z += path[i].Z; }

                mean[i] = new Vector3
                {
                    X = (float)(x / paths.Count),
                    Y = (float)(y / paths.Count),
                    Z = (float)(z / paths.Count),
                };
            }

            return mean;
        }

        /// <summary>平均軌道からのずれを RMS で。単位は m。</summary>
        public static double SpreadFromMean(IReadOnlyList<Vector3[]> paths)
        {
            if (paths == null || paths.Count == 0) return 0.0;

            int samples = paths[0].Length;
            var mean = new Vector3[samples];

            for (int i = 0; i < samples; i++)
            {
                double x = 0, y = 0, z = 0;
                foreach (var path in paths) { x += path[i].X; y += path[i].Y; z += path[i].Z; }
                mean[i] = new Vector3
                {
                    X = (float)(x / paths.Count),
                    Y = (float)(y / paths.Count),
                    Z = (float)(z / paths.Count),
                };
            }

            double squared = 0.0;
            foreach (var path in paths)
                for (int i = 0; i < samples; i++)
                {
                    double d = Vector3.Distance(path[i], mean[i]);
                    squared += d * d;
                }

            return Math.Sqrt(squared / (paths.Count * samples));
        }

        public static double StandardDeviation(IReadOnlyList<double> values)
        {
            if (values == null || values.Count < 2) return 0.0;

            double mean = values.Average();
            double squared = values.Sum(v => (v - mean) * (v - mean));
            return Math.Sqrt(squared / (values.Count - 1));
        }

        /// <summary>ノーツからノーツまでの距離 (m)。本体の格子で測る。</summary>
        public static double GridDistance(ReplayNote from, ReplayNote to)
        {
            double dx = (to.LineIndex - from.LineIndex) * NoteGrid.LineSpacing;
            double dy = NoteGrid.BaseY(to.LineLayer) - NoteGrid.BaseY(from.LineLayer);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>軌道の長さ (m)。</summary>
        public static double PathLength(Vector3[] path)
        {
            double total = 0.0;
            for (int i = 1; i < path.Length; i++) total += Vector3.Distance(path[i - 1], path[i]);
            return total;
        }

        /// <summary>
        /// これ以下のずれは直線とみなす（跳び幅に対する割合）。
        ///
        /// セイバー先端は手首・肘を中心に回るので、2つのノーツを結ぶ直線には乗らない。
        /// 自動プレイで測ると、両端でぴったり 0、真ん中で最大の弓形になり、
        /// RMS で跳び幅の 4.8%（0.0388 m / 0.81 m）だった。
        /// この弧は避けようがなく、目にも直線に見える。ここを満点にする。
        /// </summary>
        public const double StraightEnough = 0.05;

        /// <summary>ここまで外れたら 0 点（跳び幅に対する割合）。</summary>
        public const double WayOff = 0.50;

        /// <summary>
        /// 往復のずれがここまで来たら 0 点（跳び幅に対する割合）。
        ///
        /// <b>こちらに「避けようのないぶん」の許容は要らない。</b>
        /// 直線基準は手首の弧のぶん 5% を満点にしているが、往復のずれは
        /// 曲がっていても行き帰りが重なれば 0 になる。自動プレイの実測でも
        /// 両手とも 0.0000 m だった。人の記録は 1.9%〜25.6% に散っている。
        /// </summary>
        public const double RoundTripWayOff = 0.50;

        /// <summary>
        /// 再現性を割合に直す。
        ///
        /// ずれ (m) をそのまま出すと、跳ぶ距離の大きいドリルほど不利になる。
        /// ノーツからノーツまでの距離で割って相対量にすれば、配置が変わっても比べられる。
        ///
        /// 目盛りは <see cref="StraightEnough"/> で 100、<see cref="WayOff"/> で 0。
        /// 「1本の線に重なれば 100」を素直に取ると、避けようのない弧のぶんで
        /// 満点が出せず、実際に使う範囲が上に張り付いてしまう。
        /// </summary>
        /// <summary>角度ずれがここまで来たら 0 点 (度)。真横に振ったのと同じ。</summary>
        public const double AngleWayOff = 90.0;

        /// <summary>
        /// 角度ずれを割合に直す。要求された向きどおりなら 100%、
        /// <see cref="AngleWayOff"/> ずれたら 0%。
        /// </summary>
        public static double AnglePercentOf(double degrees)
        {
            double error = Math.Abs(degrees);
            if (error <= 0.0) return 100.0;
            if (error >= AngleWayOff) return 0.0;

            return 100.0 * (1.0 - error / AngleWayOff);
        }

        /// <summary>
        /// 往復のずれを割合に直す。<b>重なれば 100%</b>、跳び幅の
        /// <see cref="RoundTripWayOff"/> ぶん離れたら 0%。
        ///
        /// これが<b>再現性の本体</b>。手首の弧で必ず湾曲するので、直線からのずれで
        /// 測ると曲がっていること自体を減点してしまう。行きと帰りが同じ曲線に
        /// 重なるかだけを見れば、曲率は減点されない。
        /// </summary>
        public static double RoundTripPercent(double gap, double span)
        {
            if (span <= 1e-9) return 0.0;

            double ratio = gap / span;
            if (ratio <= 0.0) return 100.0;
            if (ratio >= RoundTripWayOff) return 0.0;

            return 100.0 * (1.0 - ratio / RoundTripWayOff);
        }

        public static double Reproducibility(double spread, double swingLength)
        {
            if (swingLength <= 1e-9) return 0.0;

            double ratio = spread / swingLength;
            if (ratio <= StraightEnough) return 100.0;
            if (ratio >= WayOff) return 0.0;

            return 100.0 * (WayOff - ratio) / (WayOff - StraightEnough);
        }
    }
}
