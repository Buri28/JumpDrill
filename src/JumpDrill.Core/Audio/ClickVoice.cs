using System;
using JumpDrill.Model;

namespace JumpDrill.Audio
{
    /// <summary>クリック1発の波形の種類。</summary>
    public enum ClickShape
    {
        /// <summary>減衰する正弦波（標準・低音）。</summary>
        Sine,
        /// <summary>音程が下がるキック。</summary>
        Kick,
        /// <summary>デューティ比 25% の矩形波。音量は段階的に下がる。</summary>
        Pulse,
        /// <summary>ノコギリ波を、閉じていくローパスに通したベース。</summary>
        SawBass,
    }

    /// <summary>
    /// クリック1発の音色。無音背景なので短い音にする（標準・低音は減衰正弦波）。
    /// Beat Saber 自身のヒット音と帯域が被らないようにしてあり、
    /// 被らなければクリックとヒット音のズレがフラムとして耳で聞こえる。
    /// </summary>
    public struct ClickVoice
    {
        public double FrequencyHz;
        public double Amplitude;
        public double DurationSeconds;
        public double AttackSeconds;
        public ClickShape Shape;

        public ClickVoice(double frequencyHz, double amplitude, double durationSeconds = 0.030, double attackSeconds = 0.0015)
            : this(ClickShape.Sine, frequencyHz, amplitude, durationSeconds, attackSeconds) { }

        public ClickVoice(ClickShape shape, double frequencyHz, double amplitude, double durationSeconds, double attackSeconds)
        {
            Shape = shape;
            FrequencyHz = frequencyHz;
            Amplitude = amplitude;
            DurationSeconds = durationSeconds;
            AttackSeconds = attackSeconds;
        }

        // 強拍は振幅がほぼ上限なので、大きく聞かせるには鳴っている長さを延ばす。
        // 長くしても減衰は指数で、速い間隔（75ms 前後）でも次のクリックまでにほぼ消える。
        // 強拍は Vorbis の誤差で 1 を超えて割れないように少し下げてある。
        // 低音も短いアタックで拍の位置を保つ。75ms 間隔でも尾が重ならない長さ。
        public static ClickVoice LowNormal { get { return new ClickVoice(160.0, 0.85, 0.060); } }
        public static ClickVoice LowAccent { get { return new ClickVoice(240.0, 0.95, 0.060); } }

        public static ClickVoice Normal { get { return new ClickVoice(1000.0, 0.85, 0.050); } }
        public static ClickVoice Accent { get { return new ClickVoice(2000.0, 0.95, 0.050); } }

        // カウントインはクリックの音色に関係なく、標準の強拍と同じ高い短い音（2000Hz 正弦）。
        // 低音・ベース音を選んでも、カウントインと本体のクリックを聞き分けられる。
        public static ClickVoice CountIn { get { return Accent; } }

        // 以下の音は 110ms 以内に鳴り終わる。もっと間隔が短いときは、ClickTrackRenderer が次のクリックの手前で消す。
        // 周波数は FrequencyHz が基音（キックは鳴り終わりの音程）。強拍は 5 度上（1.5 倍）。
        // 矩形波は角が Vorbis で行き過ぎて大きく聞こえるので、振幅を下げてある。
        public static ClickVoice KickNormal { get { return new ClickVoice(ClickShape.Kick, 50.0, 0.90, 0.110, 0.0008); } }
        public static ClickVoice KickAccent { get { return new ClickVoice(ClickShape.Kick, 75.0, 0.95, 0.110, 0.0008); } }
        public static ClickVoice RetroNormal { get { return new ClickVoice(ClickShape.Pulse, 98.0, 0.42, 0.075, 0.0005); } }
        public static ClickVoice RetroAccent { get { return new ClickVoice(ClickShape.Pulse, 147.0, 0.48, 0.075, 0.0005); } }
        public static ClickVoice SynthBassNormal { get { return new ClickVoice(ClickShape.SawBass, 82.4, 0.85, 0.105, 0.003); } }
        public static ClickVoice SynthBassAccent { get { return new ClickVoice(ClickShape.SawBass, 123.5, 0.92, 0.105, 0.003); } }

        /// <summary>弱いクリックの音量（ふつうの音に対する倍率）。約 -9 dB。</summary>
        public const double WeakGain = 0.35;

        /// <summary>弱いクリックの長さ（ふつうの音に対する倍率）。短くして次の拍の頭を目立たせる。</summary>
        public const double WeakLength = 0.6;

        /// <summary>
        /// 粒度 Beat の拍の間に鳴らす弱い音。ふつうの音と同じ音程・波形で、音量を下げて短くする。
        /// </summary>
        public static ClickVoice WeakOf(ClickVoice normal)
        {
            var v = normal;
            v.Amplitude = normal.Amplitude * WeakGain;
            v.DurationSeconds = normal.DurationSeconds * WeakLength;
            return v;
        }

        /// <summary>音色の弱い音（拍の間）。</summary>
        public static ClickVoice Weak(ClickTone tone)
        {
            return WeakOf(For(tone, false));
        }

        /// <summary>音色と強拍かどうかから、鳴らす音を選ぶ。</summary>
        public static ClickVoice For(ClickTone tone, bool accent)
        {
            switch (tone)
            {
                case ClickTone.Low: return accent ? LowAccent : LowNormal;
                case ClickTone.Kick: return accent ? KickAccent : KickNormal;
                case ClickTone.Retro: return accent ? RetroAccent : RetroNormal;
                case ClickTone.SynthBass: return accent ? SynthBassAccent : SynthBassNormal;
                default: return accent ? Accent : Normal;
            }
        }

        /// <summary>
        /// 正弦波以外の1発分の波形を作る。最大の振れ幅が Amplitude になるように揃える。
        /// 正弦波は従来の描き方（ClickTrackRenderer）のまま変えない。
        /// </summary>
        public float[] Synthesize(int sampleRate)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            int count = Math.Max(1, (int)Math.Round(DurationSeconds * sampleRate));
            double[] wave;
            switch (Shape)
            {
                case ClickShape.Kick: wave = Kick(sampleRate, count, FrequencyHz, 0.040); break;
                case ClickShape.Pulse: wave = Pulse(sampleRate, count, FrequencyHz); break;
                case ClickShape.SawBass: wave = SawBass(sampleRate, count, FrequencyHz); break;
                default:
                    wave = new double[count];
                    double omega = 2.0 * Math.PI * FrequencyHz / sampleRate;
                    for (int i = 0; i < count; i++) wave[i] = Math.Sin(omega * i);
                    break;
            }

            // 立ち上がりのぶつ切りを避ける短いフェードインと、末尾 10ms のフェードアウト
            int attack = Math.Max(1, (int)Math.Round(AttackSeconds * sampleRate));
            int release = Math.Min(count, (int)Math.Round(0.010 * sampleRate));
            for (int i = 0; i < count; i++)
            {
                double g = 1.0;
                if (i < attack) g *= (double)i / attack;
                int fromEnd = count - 1 - i;
                if (fromEnd < release) g *= (double)fromEnd / release;
                wave[i] *= g;
            }

            double peak = 0;
            for (int i = 0; i < count; i++) peak = Math.Max(peak, Math.Abs(wave[i]));
            var result = new float[count];
            if (peak <= 0) return result;
            double scale = Amplitude / peak;
            for (int i = 0; i < count; i++) result[i] = (float)(wave[i] * scale);
            return result;
        }

        // 808 風：音程を高いところから endHz まで指数で下げ、音量も指数で落とす
        private static double[] Kick(int sampleRate, int count, double endHz, double decaySeconds)
        {
            var wave = new double[count];
            double startHz = endHz * 3.2;
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / sampleRate;
                double f = endHz + (startHz - endHz) * Math.Exp(-t / 0.018);
                wave[i] = Math.Sin(phase) * Math.Exp(-t / decaySeconds);
                phase += 2.0 * Math.PI * f / sampleRate;
            }
            return wave;
        }

        // ファミコン風：デューティ比 25% の矩形波。音量は 15 段の階段で下げる
        private static double[] Pulse(int sampleRate, int count, double hz)
        {
            var wave = new double[count];
            double hold = 0.045 * sampleRate;
            for (int i = 0; i < count; i++)
            {
                double cycle = (i * hz / sampleRate) % 1.0;
                // 平均が 0 になるように直流分（-0.5）を引く
                double v = (cycle < 0.25 ? 1.0 : -1.0) + 0.5;
                double env = i < hold ? 1.0 : Math.Max(0.0, 1.0 - (i - hold) / (count - hold));
                env = Math.Ceiling(env * 15.0) / 15.0;
                wave[i] = v * env;
            }
            return wave;
        }

        // シンセベース：ノコギリ波を 2 段のローパス（遮断周波数は 1.6kHz から 250Hz へ閉じる）に通す
        private static double[] SawBass(int sampleRate, int count, double hz)
        {
            var wave = new double[count];
            double low = 0, band = 0;
            const double damping = 0.9;
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / sampleRate;
                double saw = 2.0 * ((i * hz / sampleRate) % 1.0) - 1.0;
                double cutoff = 250.0 + 1350.0 * Math.Exp(-t / 0.025);
                double f = 2.0 * Math.Sin(Math.PI * cutoff / sampleRate);
                // Chamberlin の状態変数フィルター
                low += f * band;
                double high = saw - low - damping * band;
                band += f * high;
                wave[i] = low * Math.Exp(-t / 0.045);
            }
            return wave;
        }
    }
}
