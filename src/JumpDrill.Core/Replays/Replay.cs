using System;
using System.Collections.Generic;

namespace JumpDrill.Replays
{
    public struct Vector3
    {
        public float X, Y, Z;

        public static double Distance(Vector3 a, Vector3 b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public override string ToString()
        {
            return "(" + X + ", " + Y + ", " + Z + ")";
        }
    }

    public struct Quaternion
    {
        public float X, Y, Z, W;

        /// <summary>この向きで (0,0,1) を回したもの。セイバーの向いている先。</summary>
        public Vector3 Forward
        {
            get
            {
                return new Vector3
                {
                    X = 2f * (X * Z + W * Y),
                    Y = 2f * (Y * Z - W * X),
                    Z = 1f - 2f * (X * X + Y * Y),
                };
            }
        }
    }

    public struct ReplayTransform
    {
        /// <summary>コントローラの位置。<b>ノーツを切る所ではない。</b></summary>
        public Vector3 Position;

        public Quaternion Rotation;

        /// <summary>
        /// ブレードの長さ (m)。本体の既定のセイバーはこの長さ。
        ///
        /// リプレイに入っているのはコントローラの位置なので、
        /// 切った点とは1 m ほど離れる。実測でも、コントローラのままだと
        /// 打点との距離が平均 0.92-1.06 m、先端を取ると 0.25-0.40 m まで縮む。
        /// </summary>
        public const float BladeLength = 1.0f;

        /// <summary>ブレード先端の位置。ノーツに当たるのはこちら。</summary>
        public Vector3 Tip
        {
            get
            {
                var forward = Rotation.Forward;
                return new Vector3
                {
                    X = Position.X + forward.X * BladeLength,
                    Y = Position.Y + forward.Y * BladeLength,
                    Z = Position.Z + forward.Z * BladeLength,
                };
            }
        }
    }

    public sealed class ReplayFrame
    {
        public float Time;
        public int Fps;
        public ReplayTransform Head;
        public ReplayTransform Left;
        public ReplayTransform Right;

        /// <summary>手ごとのコントローラの位置と向き。</summary>
        public ReplayTransform Saber(int saberType)
        {
            return saberType == 0 ? Left : Right;
        }

        /// <summary>手ごとのブレード先端。ノーツとの位置関係を見るならこちら。</summary>
        public Vector3 SaberTip(int saberType)
        {
            return Saber(saberType).Tip;
        }
    }

    public enum NoteEventType
    {
        Good = 0,
        Bad = 1,
        Miss = 2,
        Bomb = 3,
    }

    public sealed class NoteCutInfo
    {
        public bool SpeedOk;
        public bool DirectionOk;
        public bool SaberTypeOk;
        public bool WasCutTooSoon;
        public float SaberSpeed;
        public Vector3 SaberDirection;

        /// <summary>0 = 左（赤）、1 = 右（青）。</summary>
        public int SaberType;

        /// <summary>ノーツの中心からどれだけ早い／遅いか (秒)。</summary>
        public float TimeDeviation;

        public float CutDirDeviation;

        /// <summary>ノーツ座標系での切った点。ばらつきの評価にこれを使う。</summary>
        public Vector3 CutPoint;

        public Vector3 CutNormal;

        /// <summary>ノーツ中心からの距離 (m)。</summary>
        public float CutDistanceToCenter;

        public float CutAngle;
        public float BeforeCutRating;
        public float AfterCutRating;
    }

    public sealed class ReplayNote
    {
        public int NoteId;

        /// <summary>実際に切った時刻 (秒)。</summary>
        public float EventTime;

        public float SpawnTime;
        public NoteEventType EventType;

        /// <summary>Good / Bad のときだけ入る。</summary>
        public NoteCutInfo Cut;

        /// <summary>
        /// noteID には配置が埋め込まれている。
        /// scoringType*10000 + lineIndex*1000 + lineLayer*100 + colorType*10 + cutDirection
        /// （古い形式では先頭の scoringType が無い）。
        /// </summary>
        public int LineIndex { get { return (NoteId / 1000) % 10; } }
        public int LineLayer { get { return (NoteId / 100) % 10; } }
        public int ColorType { get { return (NoteId / 10) % 10; } }
        public int CutDirection { get { return NoteId % 10; } }
    }

    public sealed class ReplayInfo
    {
        public string Version;
        public string GameVersion;
        public string Timestamp;
        public string PlayerId;
        public string PlayerName;
        public string Platform;
        public string TrackingSystem;
        public string Hmd;
        public string Controller;

        /// <summary>譜面のハッシュ。WIP の場合は末尾に " WIP" が付く。</summary>
        public string Hash;

        public string SongName;
        public string Mapper;
        public string Difficulty;
        public int Score;
        public string Mode;
        public string Environment;
        public string Modifiers;
        public float JumpDistance;
        public bool LeftHanded;
        public float Height;

        /// <summary>練習モードで途中から始めた場合の開始位置 (秒)。0 なら頭から。</summary>
        public float StartTime;

        public float FailTime;
        public float Speed;

        /// <summary>
        /// 自動プレイ（ボット）で録られたリプレイか。
        ///
        /// 自動プレイではヘッドセットもコントローラも動かないので、
        /// BeatLeader は追跡系の3項目をどれも <c>Unknown</c> で書き出す。
        /// これを人の記録と並べると、再現性がボットの値に引っ張られて意味を失う。
        /// </summary>
        public bool LooksLikeAutoplay
        {
            get
            {
                return IsUnknown(TrackingSystem) && IsUnknown(Hmd) && IsUnknown(Controller);
            }
        }

        private static bool IsUnknown(string value)
        {
            return string.IsNullOrEmpty(value) ||
                   string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 速度や開始位置をいじって叩いたか。
        /// 練習モードでも既定のままだと StartTime=0 / Speed=1 になり、
        /// 通常プレイと区別が付かない（ファイル名の practice- で見分ける）。
        /// </summary>
        public bool HasPracticeSettings
        {
            get { return StartTime > 0f || SpeedChanged; }
        }

        /// <summary>
        /// 練習モードで速度を 100% 以外にして叩いたか。
        /// 通常プレイでは Speed が 0 で入るので、0 は変えていない扱い。
        /// </summary>
        public bool SpeedChanged
        {
            get { return Speed > 0f && Math.Abs(Speed - 1f) > 0.001f; }
        }

        /// <summary>
        /// 曲の速さを変えるモディファイア（Faster = FS / Slower = SS / Super Fast = SF）。無ければ null。
        /// <see cref="Modifiers"/> は BeatLeader の書き方（略号をカンマでつないだもの）。
        /// </summary>
        public string SpeedModifier
        {
            get
            {
                if (string.IsNullOrEmpty(Modifiers)) return null;

                foreach (var part in Modifiers.Split(','))
                {
                    string code = part.Trim();
                    if (code == "FS" || code == "SS" || code == "SF") return code;
                }
                return null;
            }
        }

        /// <summary>曲の速さを変えて叩いたか（練習の速度、または速さのモディファイア）。</summary>
        public bool SongSpeedChanged
        {
            get { return SpeedChanged || SpeedModifier != null; }
        }

        /// <summary>
        /// 記録として数えるか（メダル・ベスト）。自動プレイと、曲の速さを変えたプレイは数えない。
        /// ドリルは間隔（BPM）を決めて作るので、速さを変えると別の速さを叩いたことになる。
        /// 一覧には出すが、薄く出して印を付ける。
        /// </summary>
        public bool CountsAsRecord
        {
            get { return !LooksLikeAutoplay && !SongSpeedChanged; }
        }
    }

    public sealed class Replay
    {
        public string Path;
        public ReplayInfo Info = new ReplayInfo();
        public List<ReplayFrame> Frames = new List<ReplayFrame>();
        public List<ReplayNote> Notes = new List<ReplayNote>();
    }
}
