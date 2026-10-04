namespace JumpDrill.Audio
{
    /// <summary>
    /// クリック1発の音色。無音背景なので短い減衰正弦波にする。
    /// Beat Saber 自身のヒット音と帯域が被らないようにしてあり、
    /// 被らなければクリックとヒット音のズレがフラムとして耳で聞こえる。
    /// </summary>
    public struct ClickVoice
    {
        public double FrequencyHz;
        public double Amplitude;
        public double DurationSeconds;
        public double AttackSeconds;

        public ClickVoice(double frequencyHz, double amplitude, double durationSeconds = 0.030, double attackSeconds = 0.0015)
        {
            FrequencyHz = frequencyHz;
            Amplitude = amplitude;
            DurationSeconds = durationSeconds;
            AttackSeconds = attackSeconds;
        }

        // 強拍は振幅がほぼ上限なので、大きく聞かせるには鳴っている長さを延ばす。
        // 長くしても減衰は指数で、速い間隔（75ms 前後）でも次のクリックまでにほぼ消える。
        // 強拍は Vorbis の誤差で 1 を超えて割れないように少し下げてある。
        public static ClickVoice Normal { get { return new ClickVoice(1000.0, 0.85, 0.050); } }
        public static ClickVoice Accent { get { return new ClickVoice(2000.0, 0.95, 0.050); } }
    }
}
