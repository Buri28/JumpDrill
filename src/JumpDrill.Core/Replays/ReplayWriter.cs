using System.Collections.Generic;
using System.IO;
using System.Text;

namespace JumpDrill.Replays
{
    /// <summary>
    /// リプレイを BeatLeader と同じ形式 (.bsor v1) で書く。<see cref="ReplayReader"/> の逆。
    ///
    /// BeatLeader が無い環境でも記録を残せるようにするためのもの。
    /// 同じ形式にしておけば、読む側（GUI・MOD・BeatLeader のリプレイ再生）を分けずに済む。
    ///
    /// 書くのは info / frames / notes と、中身の無い walls / heights / pauses。
    /// BeatLeader が後ろに足す saberOffsets と customData は書かない
    /// （読む側はそこで止まるだけで、無くても困らない）。
    /// </summary>
    public static class ReplayWriter
    {
        private const int Magic = 0x442d3d69;

        public static void Write(Replay replay, string path)
        {
            using (var stream = File.Create(path))
                Write(replay, stream);
        }

        public static void Write(Replay replay, Stream stream)
        {
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
            {
                writer.Write(Magic);
                writer.Write((byte)1);

                writer.Write((byte)0);
                WriteInfo(writer, replay.Info ?? new ReplayInfo());

                writer.Write((byte)1);
                WriteFrames(writer, replay.Frames ?? new List<ReplayFrame>());

                writer.Write((byte)2);
                WriteNotes(writer, replay.Notes ?? new List<ReplayNote>());

                // walls / heights / pauses。ドリルでは使わないので空で置く
                for (byte block = 3; block <= 5; block++)
                {
                    writer.Write(block);
                    writer.Write(0);
                }
            }
        }

        private static void WriteInfo(BinaryWriter writer, ReplayInfo info)
        {
            WriteString(writer, info.Version);
            WriteString(writer, info.GameVersion);
            WriteString(writer, info.Timestamp);
            WriteString(writer, info.PlayerId);
            WriteString(writer, info.PlayerName);
            WriteString(writer, info.Platform);
            WriteString(writer, info.TrackingSystem);
            WriteString(writer, info.Hmd);
            WriteString(writer, info.Controller);
            WriteString(writer, info.Hash);
            WriteString(writer, info.SongName);
            WriteString(writer, info.Mapper);
            WriteString(writer, info.Difficulty);
            writer.Write(info.Score);
            WriteString(writer, info.Mode);
            WriteString(writer, info.Environment);
            WriteString(writer, info.Modifiers);
            writer.Write(info.JumpDistance);
            writer.Write(info.LeftHanded);
            writer.Write(info.Height);
            writer.Write(info.StartTime);
            writer.Write(info.FailTime);
            writer.Write(info.Speed);
        }

        private static void WriteFrames(BinaryWriter writer, List<ReplayFrame> frames)
        {
            writer.Write(frames.Count);
            foreach (var frame in frames)
            {
                writer.Write(frame.Time);
                writer.Write(frame.Fps);
                WriteTransform(writer, frame.Head);
                WriteTransform(writer, frame.Left);
                WriteTransform(writer, frame.Right);
            }
        }

        private static void WriteNotes(BinaryWriter writer, List<ReplayNote> notes)
        {
            writer.Write(notes.Count);
            foreach (var note in notes)
            {
                writer.Write(note.NoteId);
                writer.Write(note.EventTime);
                writer.Write(note.SpawnTime);
                writer.Write((int)note.EventType);

                // 読む側と同じく、当たった／外したときだけ切り方が続く
                if (note.EventType == NoteEventType.Good || note.EventType == NoteEventType.Bad)
                    WriteCutInfo(writer, note.Cut ?? new NoteCutInfo());
            }
        }

        private static void WriteCutInfo(BinaryWriter writer, NoteCutInfo cut)
        {
            writer.Write(cut.SpeedOk);
            writer.Write(cut.DirectionOk);
            writer.Write(cut.SaberTypeOk);
            writer.Write(cut.WasCutTooSoon);
            writer.Write(cut.SaberSpeed);
            WriteVector(writer, cut.SaberDirection);
            writer.Write(cut.SaberType);
            writer.Write(cut.TimeDeviation);
            writer.Write(cut.CutDirDeviation);
            WriteVector(writer, cut.CutPoint);
            WriteVector(writer, cut.CutNormal);
            writer.Write(cut.CutDistanceToCenter);
            writer.Write(cut.CutAngle);
            writer.Write(cut.BeforeCutRating);
            writer.Write(cut.AfterCutRating);
        }

        private static void WriteTransform(BinaryWriter writer, ReplayTransform transform)
        {
            WriteVector(writer, transform.Position);
            writer.Write(transform.Rotation.X);
            writer.Write(transform.Rotation.Y);
            writer.Write(transform.Rotation.Z);
            writer.Write(transform.Rotation.W);
        }

        private static void WriteVector(BinaryWriter writer, Vector3 vector)
        {
            writer.Write(vector.X);
            writer.Write(vector.Y);
            writer.Write(vector.Z);
        }

        /// <summary>長さ付きの UTF-8 文字列。null は空文字として書く（BeatLeader と同じ）。</summary>
        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
    }
}
