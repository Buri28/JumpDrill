using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;
using JumpDrill.Audio;
using JumpDrill.Model;
using JumpDrill.Serialization;

namespace JumpDrill.Output
{
    public enum AudioFormat
    {
        /// <summary>Beat Saber が読むのはこちら。</summary>
        Ogg,

        /// <summary>検証用。Beat Saber では読めない。</summary>
        Wav,
    }

    public sealed class LevelWriteOptions
    {
        public AudioFormat Format { get; set; } = AudioFormat.Ogg;
        public int SampleRate { get; set; } = ClickTrackRenderer.DefaultSampleRate;
        public float OggQuality { get; set; } = OggEncoder.DefaultQuality;

        /// <summary>既存フォルダがあれば中身を上書きする。</summary>
        public bool Overwrite { get; set; } = true;

        /// <summary>
        /// BeatSaver 形式の zip も書き出す場所。null なら書かない。
        /// BeatLeader のリプレイ表示に読ませるためのもの。
        /// </summary>
        public string ZipDirectory { get; set; }
    }

    /// <summary>CustomLevels に置けるフォルダ一式を書き出す。</summary>
    public static class LevelWriter
    {
        /// <summary>直前の書き出しで作った zip。作らなかったときは null。</summary>
        public static string LastZipPath { get; private set; }

        public static string Write(DrillMap map, string rootDirectory, string folderName = null, LevelWriteOptions options = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (string.IsNullOrEmpty(rootDirectory)) throw new ArgumentException(Lang.T("出力先が空です。", "The output folder is empty."), nameof(rootDirectory));
            options = options ?? new LevelWriteOptions();

            string folder = Path.Combine(rootDirectory, SanitizeFolderName(folderName ?? map.Name));
            if (Directory.Exists(folder) && !options.Overwrite)
                throw new IOException(Lang.T("すでに存在します: ", "Already exists: ") + folder);
            Directory.CreateDirectory(folder);

            string songFile = options.Format == AudioFormat.Ogg
                ? BeatmapSerializer.SongFileName
                : "song.wav";

            var samples = ClickTrackRenderer.Render(map, options.SampleRate);
            byte[] audio = options.Format == AudioFormat.Ogg
                ? OggEncoder.ToBytes(samples, options.SampleRate, options.OggQuality)
                : WavWriter.ToBytes(samples, options.SampleRate);

            File.WriteAllBytes(Path.Combine(folder, songFile), audio);
            File.WriteAllBytes(Path.Combine(folder, BeatmapSerializer.CoverFileName), CoverRenderer.Render(map));

            // BOM 無し UTF-8。Beat Saber のパーサは BOM を嫌う。
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(folder, "Info.dat"), BeatmapSerializer.WriteInfo(map, map.Name, songFile), utf8);
            File.WriteAllText(Path.Combine(folder, BeatmapSerializer.DifficultyFileName), BeatmapSerializer.WriteDifficulty(map), utf8);

            if (!string.IsNullOrEmpty(options.ZipDirectory))
                LastZipPath = LevelZipWriter.Write(folder, options.ZipDirectory);
            else
                LastZipPath = null;

            return folder;
        }

        /// <summary>遷移記法の記号がそのままではファイル名に使えないので置き換える。</summary>
        public static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "drill";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c == '>') sb.Append('-');
                else if (c == ':' || c == ',') sb.Append('_');
                else if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') sb.Append(c);
                else if (char.IsWhiteSpace(c)) sb.Append('_');
            }

            string result = sb.ToString().Trim('_', '.', ' ');
            if (result.Length == 0) result = "drill";
            if (result.Length > 96) result = result.Substring(0, 96);
            return result;
        }

        /// <summary>置き場所の種類。</summary>
        public enum LevelFolderKind
        {
            /// <summary>CustomLevels。ScoreSaber などにスコアが送信される。</summary>
            Custom,

            /// <summary>CustomWIPLevels。スコア送信の対象外。自作の練習譜面はこちら。</summary>
            Wip,

            /// <summary>folders.xml で登録した独立パック。WIP=True なら送信の対象外。</summary>
            Pack,
        }

        /// <summary>見つかった置き場所1件。</summary>
        public sealed class LevelFolder
        {
            public string Path { get; internal set; }
            public LevelFolderKind Kind { get; internal set; }

            /// <summary>どのインストールかを表す短い名前。</summary>
            public string InstallName { get; internal set; }

            /// <summary>Beat Saber_Data の親。共有フォルダのように特定できない場合は null。</summary>
            public string InstallRoot { get; internal set; }

            /// <summary>Pack のときの表示名。</summary>
            public string PackName { get; internal set; }

            public override string ToString() { return InstallName + " / " + Kind; }
        }

        /// <summary>
        /// 譜面の置き場所を探す。Steam の標準インストール先に加えて、
        /// BSManager のバージョン別インスタンスも見る（複数バージョンを併存させるので
        /// Steam のパスだけ見ていると1つも見つからない）。
        ///
        /// <b>WIP を先に返す。</b> ScoreSaber / BeatLeader は CustomLevels に置いた譜面なら
        /// 未公開・自作でも未ランクのリーダーボードを作ってスコアを送るが、
        /// CustomWIPLevels は送信の対象外。練習用のドリルは WIP に置くのが本来。
        /// </summary>
        public static List<LevelFolder> FindLevelFolders()
        {
            var custom = new List<LevelFolder>();
            var wip = new List<LevelFolder>();
            var packs = new List<LevelFolder>();
            var roots = DriveRoots();

            foreach (var manager in ManagerRoots(roots))
            {
                // BSManager は既定で CustomLevels を1か所に集め、各インスタンスからは
                // ジャンクションを張る。バージョンごとに並べても実体は同じなので1件にまとめる。
                // WIP は共有されずインスタンスごとに実体を持つので、こちらは版ごとに出す。
                string shared = Path.Combine(manager, "SharedContent", "SharedMaps", "CustomLevels");
                Add(custom, shared, LevelFolderKind.Custom, Lang.T("BSManager（全バージョン共有）", "BSManager (shared by all versions)"));

                string sharedWip = Path.Combine(manager, "SharedContent", "SharedMaps", "CustomWIPLevels");
                Add(wip, sharedWip, LevelFolderKind.Wip, Lang.T("BSManager（全バージョン共有）", "BSManager (shared by all versions)"));

                foreach (var instance in SortedInstances(Path.Combine(manager, "BSInstances")))
                {
                    string label = "BSManager " + Path.GetFileName(instance);
                    string data = Path.Combine(instance, "Beat Saber_Data");

                    AddPacks(packs, instance, label);

                    // 共有をやめて実体を持っているものだけ個別に出す。
                    AddIfReal(custom, Path.Combine(data, "CustomLevels"), LevelFolderKind.Custom, label, instance);
                    AddIfReal(wip, Path.Combine(data, "CustomWIPLevels"), LevelFolderKind.Wip, label, instance);
                }
            }

            foreach (var install in SteamInstalls(roots))
            {
                string data = Path.Combine(install, "Beat Saber_Data");
                AddPacks(packs, install, "Steam");
                Add(custom, Path.Combine(data, "CustomLevels"), LevelFolderKind.Custom, "Steam", install);
                Add(wip, Path.Combine(data, "CustomWIPLevels"), LevelFolderKind.Wip, "Steam", install);
            }

            // 専用パックが一番目的に合うので先頭。次に WIP、最後に通常。
            var result = new List<LevelFolder>();
            result.AddRange(packs);
            result.AddRange(wip);
            result.AddRange(custom);
            return result;
        }

        /// <summary>見つかった Beat Saber のインストール1件。</summary>
        public sealed class BeatSaberInstall
        {
            /// <summary>Beat Saber.exe のあるフォルダ。</summary>
            public string Root { get; internal set; }

            /// <summary>どのインストールかを表す短い名前。</summary>
            public string Name { get; internal set; }

            /// <summary>ゲームのバージョン。読めなければ null。</summary>
            public string GameVersion { get; internal set; }

            public string PluginsPath => Path.Combine(Root, "Plugins");
        }

        /// <summary>
        /// Beat Saber のインストールを探す。BSManager のインスタンス（新しい順）、Steam の順。
        /// 見る場所は <see cref="FindLevelFolders"/> と同じ。
        /// </summary>
        public static List<BeatSaberInstall> FindInstalls()
        {
            var result = new List<BeatSaberInstall>();
            var roots = DriveRoots();

            foreach (var manager in ManagerRoots(roots))
                foreach (var instance in SortedInstances(Path.Combine(manager, "BSInstances")))
                    AddInstall(result, instance, "BSManager " + Path.GetFileName(instance));

            foreach (var install in SteamInstalls(roots))
                AddInstall(result, install, "Steam");

            return result;
        }

        /// <summary>ゲームを置いたフォルダか。Beat Saber.exe と Beat Saber_Data があればそう見なす。</summary>
        public static bool IsBeatSaberFolder(string root)
        {
            return !string.IsNullOrEmpty(root)
                && File.Exists(Path.Combine(root, "Beat Saber.exe"))
                && Directory.Exists(Path.Combine(root, "Beat Saber_Data"));
        }

        /// <summary>指定したフォルダを1件として扱う。ゲームのフォルダでなければ null。</summary>
        public static BeatSaberInstall InstallAt(string root, string name)
        {
            var list = new List<BeatSaberInstall>();
            AddInstall(list, root, name);
            return list.Count > 0 ? list[0] : null;
        }

        private static void AddInstall(List<BeatSaberInstall> into, string root, string name)
        {
            if (!IsBeatSaberFolder(root)) return;
            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            foreach (var existing in into)
                if (string.Equals(existing.Root, full, StringComparison.OrdinalIgnoreCase)) return;

            into.Add(new BeatSaberInstall { Root = full, Name = name, GameVersion = ReadGameVersion(full) });
        }

        /// <summary>BeatSaberVersion.txt（BSIPA が書く）から読む。無ければ BSManager のフォルダ名。</summary>
        private static string ReadGameVersion(string root)
        {
            try
            {
                string file = Path.Combine(root, "BeatSaberVersion.txt");
                if (File.Exists(file))
                {
                    string text = File.ReadAllText(file).Trim();
                    int cut = text.IndexOf('_');
                    return cut > 0 ? text.Substring(0, cut) : text;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            Version parsed;
            string folder = Path.GetFileName(root);
            return Version.TryParse(folder, out parsed) ? folder : null;
        }

        private static List<string> DriveRoots()
        {
            var roots = new List<string>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady) continue;
                    roots.Add(drive.RootDirectory.FullName);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return roots;
        }

        private static List<string> ManagerRoots(List<string> driveRoots)
        {
            var managerRoots = new List<string>();
            foreach (var root in driveRoots)
                managerRoots.Add(Path.Combine(root, "BSManager"));
            managerRoots.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BSManager"));
            return managerRoots;
        }

        private static IEnumerable<string> SteamInstalls(List<string> driveRoots)
        {
            foreach (var root in driveRoots)
            {
                foreach (var tail in new[]
                {
                    Path.Combine("Program Files (x86)", "Steam", "steamapps", "common", "Beat Saber"),
                    Path.Combine("SteamLibrary", "steamapps", "common", "Beat Saber"),
                    Path.Combine("Steam", "steamapps", "common", "Beat Saber"),
                })
                    yield return Path.Combine(root, tail);
            }
        }

        private static void Add(List<LevelFolder> into, string path, LevelFolderKind kind, string installName, string installRoot = null, string packName = null)
        {
            if (!Directory.Exists(path)) return;
            foreach (var existing in into)
                if (string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase)) return;

            into.Add(new LevelFolder
            {
                Path = path,
                Kind = kind,
                InstallName = installName,
                InstallRoot = installRoot,
                PackName = packName,
            });
        }

        private static void AddIfReal(List<LevelFolder> into, string path, LevelFolderKind kind, string installName, string installRoot)
        {
            if (IsLink(path)) return;
            Add(into, path, kind, installName, installRoot);
        }

        /// <summary>folders.xml に登録済みの独立パック（WIP 扱いのもの）を拾う。</summary>
        private static void AddPacks(List<LevelFolder> into, string installRoot, string installName)
        {
            string xml = SongCoreFolders.FoldersXmlFor(installRoot);
            foreach (var entry in SongCoreFolders.Read(xml))
            {
                if (entry.IsExample || entry.Pack != 2) continue;
                Add(into, entry.Path, LevelFolderKind.Pack, installName, installRoot, entry.Name);
            }
        }

        /// <summary>ジャンクションやシンボリックリンクか。</summary>
        private static bool IsLink(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return false;
                return (new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>バージョン番号の新しい順。1.9 と 1.40 を文字列で比べると逆になる。</summary>
        private static List<string> SortedInstances(string instanceRoot)
        {
            var result = new List<string>();
            try
            {
                if (!Directory.Exists(instanceRoot)) return result;
                result.AddRange(Directory.GetDirectories(instanceRoot));
            }
            catch (IOException) { return result; }
            catch (UnauthorizedAccessException) { return result; }

            result.Sort((a, b) =>
            {
                Version va, vb;
                bool oka = Version.TryParse(Path.GetFileName(a), out va);
                bool okb = Version.TryParse(Path.GetFileName(b), out vb);
                if (oka && okb) return vb.CompareTo(va);
                if (oka) return -1;
                if (okb) return 1;
                return string.Compare(b, a, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private static string FirstOfKind(LevelFolderKind kind)
        {
            foreach (var folder in FindLevelFolders())
                if (folder.Kind == kind) return folder.Path;
            return null;
        }

        /// <summary>最有力の CustomWIPLevels。自作の練習譜面はここに置く。</summary>
        public static string GuessWipLevelsDirectory()
        {
            return FirstOfKind(LevelFolderKind.Wip);
        }

        /// <summary>最有力の CustomLevels。スコアが送信される点に注意。</summary>
        public static string GuessCustomLevelsDirectory()
        {
            return FirstOfKind(LevelFolderKind.Custom);
        }

        /// <summary>生成内容の要約。CLI がそのまま出す。</summary>
        public static string Summarize(DrillMap map)
        {
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;

            sb.AppendLine(string.Format(ci, Lang.T("  遷移        {0}", "  Sequence    {0}"), string.Join("  ", Describe(map.Options.Sequences))));
            sb.AppendLine(string.Format(ci, Lang.T("  切る方向    {0}", "  Direction   {0}"), map.Options.Direction));
            // 速さは BPM だけで言う。ms も EBPM も普段は目にしない単位で、
            // 同じ速さを3通りに書いて並べても読み替えの手間が増えるだけだった。
            sb.AppendLine(string.Format(ci, Lang.T("  テンポ      {0:0.#} BPM 1/{1}", "  Tempo       {0:0.#} BPM 1/{1}"),
                Tempo.BpmFromIntervalMs(map.Options.IntervalMs, map.Options.NotesPerBeat),
                FormatSubdivision(map.Options.NotesPerBeat)));
            if (map.BlockCount > 1)
            {
                sb.AppendLine(string.Format(ci, Lang.T("  構成        {0}（間 {1:0.#} 秒）", "  Blocks      {0} (gap {1:0.#} s)"), DescribeBlocks(map), map.GapSeconds));
                sb.AppendLine(string.Format(ci, Lang.T("  ノーツ数    {0}  ({1:0.#} 秒 × {2} ブロック)", "  Notes       {0}  ({1:0.#} s × {2} blocks)"), map.Notes.Count, map.Options.DurationSeconds, map.BlockCount));
            }
            else
            {
                sb.AppendLine(string.Format(ci, Lang.T("  ノーツ数    {0}  (本体 {1:0.#} 秒)", "  Notes       {0}  (body {1:0.#} s)"), map.Notes.Count, map.Options.DurationSeconds));
            }
            sb.AppendLine(string.Format(ci, Lang.T("  NJS         {0:0.##}   オフセット {1:0.###} 拍", "  NJS         {0:0.##}   offset {1:0.###} beats"), map.Njs, map.NoteJumpStartBeatOffset));
            sb.AppendLine(string.Format(ci, Lang.T("  ジャンプ    距離 {0:0.##} m   反応時間 {1:0} ms", "  Jump        distance {0:0.##} m   reaction time {1:0} ms"), map.Jump.JumpDistance, map.Jump.ReactionTimeSeconds * 1000.0));
            sb.AppendLine(string.Format(ci, Lang.T("  クリック    {0}  周期 {1:0} ms  カウントイン {2} 発", "  Click       {0}  period {1:0} ms  count-in {2}"), map.Options.Click, map.ClickPeriodSeconds * 1000.0, map.Options.CountInClicks));
            sb.AppendLine(string.Format(ci, Lang.T("  尺          先頭 {0:0.##} 秒 + 本体 + 末尾 {1:0.##} 秒 = {2:0.##} 秒", "  Length      lead-in {0:0.##} s + body + tail {1:0.##} s = {2:0.##} s"), map.LeadInSeconds, map.Options.TailSeconds, map.TotalSeconds));
            return sb.ToString();
        }

        /// <summary>
        /// ブロックの並びを「1セット = 右→左」の形で1行にする。
        /// セットを繰り返すときに何が何回来るのかが読めないと困る。
        /// </summary>
        private static string DescribeBlocks(DrillMap map)
        {
            var ci = CultureInfo.InvariantCulture;
            string order = string.Join("→", map.Options.Sequences
                .Take(map.BlocksPerSet)
                .Select(s => s.Hand == Hand.Right ? Lang.T("右", "R") : Lang.T("左", "L"))
                .ToArray());

            if (map.Sets <= 1)
                return string.Format(ci, Lang.T("片手ずつ {0}（1セット）", "one hand at a time {0} (1 set)"), order);

            return string.Format(ci, Lang.T("片手ずつ {0} を {1} セット", "one hand at a time {0} × {1} sets"), order, map.Sets);
        }

        /// <summary>1/2 のような分母を整数で出す。</summary>
        private static string FormatSubdivision(double notesPerBeat)
        {
            return notesPerBeat == Math.Floor(notesPerBeat)
                ? ((long)notesPerBeat).ToString(CultureInfo.InvariantCulture)
                : notesPerBeat.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string[] Describe(IReadOnlyList<HandSequence> sequences)
        {
            var result = new string[sequences.Count];
            for (int i = 0; i < sequences.Count; i++) result[i] = sequences[i].ToString();
            return result;
        }
    }
}
