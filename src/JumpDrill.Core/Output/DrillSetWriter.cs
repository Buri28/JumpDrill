using System;
using System.Collections.Generic;
using System.IO;
using JumpDrill.Generation;
using JumpDrill.Model;

namespace JumpDrill.Output
{
    /// <summary>
    /// 一括生成の全ドリルをまとめて書き出す。
    ///
    /// 譜面は MOD に同梱せず、初めて使うときにここで一括生成する前提
    /// （112本の ogg を配ると重い）。CLI / GUI も同じここを呼ぶ。
    /// </summary>
    public static class DrillSetWriter
    {
        /// <summary>
        /// 全マスを <paramref name="root"/> の下に書く。書いたフォルダを並びどおりに返す。
        /// </summary>
        /// <param name="progress">1本書くごとに（書いた数, 全体の数, 譜面名）で呼ぶ。</param>
        /// <param name="skipExisting">
        /// Info.dat が既にあるマスは書き直さない。ID は設定から決まるので、
        /// 同じ名前のフォルダがあれば中身も同じ。途中で止めた続きから再開できる。
        /// </param>
        public static List<string> WriteAll(string root, LevelWriteOptions options,
            Action<int, int, string> progress = null, bool skipExisting = false)
        {
            return Write(root, DrillSet.All(), options, progress, skipExisting);
        }

        /// <summary>
        /// 指定したマスだけを書く。1本だけ作る・作り直すときに使う。
        /// 引数の意味は <see cref="WriteAll"/> と同じ。
        /// </summary>
        public static List<string> Write(string root, IReadOnlyList<DrillSetEntry> entries, LevelWriteOptions options,
            Action<int, int, string> progress = null, bool skipExisting = false)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException(Lang.T("出力先が空です。", "The output folder is empty."), nameof(root));
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            var folders = new List<string>(entries.Count);

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                string folder = Path.Combine(root, LevelWriter.SanitizeFolderName(entry.Options.Name));

                if (!(skipExisting && File.Exists(Path.Combine(folder, "Info.dat"))))
                    folder = LevelWriter.Write(DrillGenerator.Generate(entry.Options), root, null, options);

                folders.Add(folder);
                if (progress != null) progress(i + 1, entries.Count, entry.Options.Name);
            }

            return folders;
        }
    }
}
