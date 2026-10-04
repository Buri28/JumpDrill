using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace JumpDrill.Replays
{
    /// <summary>
    /// BeatLeader のリプレイ (.bsor v1) を読む。
    ///
    /// ブロック単位の素直な形式で、先頭に magic とバージョン、
    /// 以降は「ブロック種別1バイト + 中身」が並ぶ。
    /// ドリルの評価に要るのは info / frames / notes の3つだけなので、
    /// walls などは読み飛ばす。
    /// </summary>
    public static class ReplayReader
    {
        private const int Magic = 0x442d3d69;

        public static Replay Read(string path)
        {
            using (var stream = File.OpenRead(path))
                return Read(stream, path);
        }

        public static Replay Read(Stream stream, string path = null)
        {
            var replay = new Replay { Path = path };

            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                int magic = reader.ReadInt32();
                if (magic != Magic)
                    throw new InvalidDataException(Lang.T("BeatLeader のリプレイではありません（先頭の識別子が違う）。", "Not a BeatLeader replay (wrong header)."));

                byte version = reader.ReadByte();
                if (version != 1)
                    throw new InvalidDataException(Lang.T("未対応の .bsor バージョンです: ", "Unsupported .bsor version: ") + version);

                while (stream.Position < stream.Length)
                {
                    int blockId = reader.ReadByte();
                    switch (blockId)
                    {
                        case 0: ReadInfo(reader, replay); break;
                        case 1: ReadFrames(reader, replay); break;
                        case 2: ReadNotes(reader, replay); break;

                        // walls / heights / pauses。ドリルの評価には使わないが、
                        // 位置を進めないと後続が読めないので形だけ追う。
                        case 3: SkipWalls(reader); break;
                        case 4: SkipHeights(reader); break;
                        case 5: SkipPauses(reader); break;

                        default:
                            // 知らないブロックが出たら、そこから先は解釈できない。
                            return replay;
                    }
                }
            }

            return replay;
        }

        private static void ReadInfo(BinaryReader reader, Replay replay)
        {
            var info = new ReplayInfo();
            info.Version = ReadString(reader);
            info.GameVersion = ReadString(reader);
            info.Timestamp = ReadString(reader);
            info.PlayerId = ReadString(reader);
            info.PlayerName = ReadString(reader);
            info.Platform = ReadString(reader);
            info.TrackingSystem = ReadString(reader);
            info.Hmd = ReadString(reader);
            info.Controller = ReadString(reader);
            info.Hash = ReadString(reader);
            info.SongName = ReadString(reader);
            info.Mapper = ReadString(reader);
            info.Difficulty = ReadString(reader);
            info.Score = reader.ReadInt32();
            info.Mode = ReadString(reader);
            info.Environment = ReadString(reader);
            info.Modifiers = ReadString(reader);
            info.JumpDistance = reader.ReadSingle();
            info.LeftHanded = reader.ReadBoolean();
            info.Height = reader.ReadSingle();
            info.StartTime = reader.ReadSingle();
            info.FailTime = reader.ReadSingle();
            info.Speed = reader.ReadSingle();

            replay.Info = info;
        }

        private static void ReadFrames(BinaryReader reader, Replay replay)
        {
            int count = reader.ReadInt32();
            var frames = new List<ReplayFrame>(count);

            for (int i = 0; i < count; i++)
            {
                var frame = new ReplayFrame
                {
                    Time = reader.ReadSingle(),
                    Fps = reader.ReadInt32(),
                    Head = ReadTransform(reader),
                    Left = ReadTransform(reader),
                    Right = ReadTransform(reader),
                };
                frames.Add(frame);
            }

            replay.Frames = frames;
        }

        private static void ReadNotes(BinaryReader reader, Replay replay)
        {
            int count = reader.ReadInt32();
            var notes = new List<ReplayNote>(count);

            for (int i = 0; i < count; i++)
            {
                var note = new ReplayNote
                {
                    NoteId = reader.ReadInt32(),
                    EventTime = reader.ReadSingle(),
                    SpawnTime = reader.ReadSingle(),
                    EventType = (NoteEventType)reader.ReadInt32(),
                };

                // 当たった／外した場合だけ切り方の詳細が続く。
                if (note.EventType == NoteEventType.Good || note.EventType == NoteEventType.Bad)
                    note.Cut = ReadCutInfo(reader);

                notes.Add(note);
            }

            replay.Notes = notes;
        }

        private static NoteCutInfo ReadCutInfo(BinaryReader reader)
        {
            return new NoteCutInfo
            {
                SpeedOk = reader.ReadBoolean(),
                DirectionOk = reader.ReadBoolean(),
                SaberTypeOk = reader.ReadBoolean(),
                WasCutTooSoon = reader.ReadBoolean(),
                SaberSpeed = reader.ReadSingle(),
                SaberDirection = ReadVector(reader),
                SaberType = reader.ReadInt32(),
                TimeDeviation = reader.ReadSingle(),
                CutDirDeviation = reader.ReadSingle(),
                CutPoint = ReadVector(reader),
                CutNormal = ReadVector(reader),
                CutDistanceToCenter = reader.ReadSingle(),
                CutAngle = reader.ReadSingle(),
                BeforeCutRating = reader.ReadSingle(),
                AfterCutRating = reader.ReadSingle(),
            };
        }

        private static void SkipWalls(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                reader.ReadInt32();     // wallId
                reader.ReadSingle();    // energy
                reader.ReadSingle();    // time
                reader.ReadSingle();    // spawnTime
            }
        }

        private static void SkipHeights(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                reader.ReadSingle();    // height
                reader.ReadSingle();    // time
            }
        }

        private static void SkipPauses(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                reader.ReadInt64();     // duration
                reader.ReadSingle();    // time
            }
        }

        private static ReplayTransform ReadTransform(BinaryReader reader)
        {
            return new ReplayTransform
            {
                Position = ReadVector(reader),
                Rotation = new Quaternion
                {
                    X = reader.ReadSingle(),
                    Y = reader.ReadSingle(),
                    Z = reader.ReadSingle(),
                    W = reader.ReadSingle(),
                },
            };
        }

        private static Vector3 ReadVector(BinaryReader reader)
        {
            return new Vector3
            {
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle(),
            };
        }

        /// <summary>長さ付きの UTF-8 文字列。</summary>
        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > 1_000_000)
                throw new InvalidDataException("文字列の長さが不正です: " + length);

            return Encoding.UTF8.GetString(reader.ReadBytes(length));
        }
    }
}
