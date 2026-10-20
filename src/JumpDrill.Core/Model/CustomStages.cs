using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using JumpDrill.Serialization;

namespace JumpDrill.Model
{
    /// <summary>カスタムステージの1行。行ごとに名前と、並べたドリル。</summary>
    public sealed class CustomStageRow
    {
        public string Name { get; set; } = "";

        /// <summary>
        /// 並べたドリルの曲名（<c>[Q7K3M] Drill R8b L5a axis 300 BPM 30s</c>）。
        /// 記録は曲名に埋めた ID で引くので、曲名だけ持てばよい。
        /// </summary>
        public List<string> Drills { get; set; } = new List<string>();
    }

    /// <summary>メダル画面に出す、自分で並べたドリルの表。</summary>
    public sealed class CustomStage
    {
        /// <summary>行の上限。標準のステージの方向の数に揃え、表の大きさをステージで変えない。</summary>
        public const int MaxRows = 14;

        /// <summary>1行に並べられるドリルの上限。標準のステージの Stage の数（Stage 1〜8）に揃える。</summary>
        public const int MaxDrillsPerRow = 8;

        public string Name { get; set; } = "";
        public List<CustomStageRow> Rows { get; set; } = new List<CustomStageRow>();

        /// <summary>行の方向。最初のドリルで決まる。空の行なら null（どの方向でも入れられる）。</summary>
        public static string DirectionOf(CustomStageRow row)
        {
            return row.Drills.Count > 0 ? DrillNaming.DirectionOf(row.Drills[0]) : null;
        }

        /// <summary>
        /// ドリルを足す。<paramref name="row"/> が行の数と同じなら新しい行に足す。
        /// 新しい行に足すときは、同じ方向の行があればまずその空きに入れ、残りで行を作る（同じ方向の行が増えないように）。
        /// 違う方向を混ぜて選んだら、方向ごとに行を分ける。行の名前は方向（<c>R8bL5a</c>）。
        /// 既にある行に入れるときは BPM の順の位置に差し込む（並べ替えた行の順はそのまま）。
        /// ステージに既にあるドリル（ID で比べる）は足さない。同じ行に同じドリルが2つ並ぶのを防ぐ。
        /// 行と1行の本数の上限を超える分は足さない。上限で足せなかった本数を返す（既にあって外したものは数えない）。
        /// </summary>
        /// <remarks>既にある行への方向の絞り込みは、選ばせる側でやる（<see cref="DirectionOf"/>）。</remarks>
        public int AddDrills(int row, IReadOnlyList<string> picked)
        {
            var placed = new HashSet<string>(Rows.SelectMany(r => r.Drills).Select(Key), StringComparer.Ordinal);
            var fresh = picked.Where(n => placed.Add(Key(n))).ToList();

            int skipped = 0;
            if (row < Rows.Count)
            {
                var drills = Rows[row].Drills;
                int room = Math.Max(0, MaxDrillsPerRow - drills.Count);
                foreach (var name in fresh.Take(room)) InsertBySpeed(drills, name);
                return Math.Max(0, fresh.Count - room);
            }

            foreach (var group in fresh.GroupBy(DrillNaming.DirectionOf))
            {
                var rest = group.ToList();

                // 方向の読めないドリル（空文字）は、同じ方向とみなさない
                if (group.Key.Length > 0)
                {
                    foreach (var existing in Rows)
                    {
                        if (rest.Count == 0) break;
                        if (DirectionOf(existing) != group.Key) continue;

                        int room = Math.Max(0, MaxDrillsPerRow - existing.Drills.Count);
                        foreach (var name in rest.Take(room)) InsertBySpeed(existing.Drills, name);
                        rest = rest.Skip(room).ToList();
                    }
                }
                if (rest.Count == 0) continue;

                if (Rows.Count >= MaxRows) { skipped += rest.Count; continue; }

                string rowName = group.Key.Length > 0 ? group.Key : "Row " + (Rows.Count + 1);
                Rows.Add(new CustomStageRow { Name = rowName, Drills = rest.Take(MaxDrillsPerRow).ToList() });
                skipped += Math.Max(0, rest.Count - MaxDrillsPerRow);
            }
            return skipped;
        }

        /// <summary>同じドリルかを見る鍵。曲名の付け方が変わっても拾えるよう ID で比べる。</summary>
        private static string Key(string songName) => DrillNaming.ExtractId(songName) ?? songName;

        /// <summary>
        /// 行の中の、BPM が自分より大きい最初のドリルの前に差し込む。BPM が読めなければ末尾。
        /// 既にあるドリルどうしの順は動かさない（手で並べ替えた行を崩さない）。
        /// </summary>
        private static void InsertBySpeed(List<string> drills, string name)
        {
            double? bpm = DrillNaming.ParseBpm(name);
            int at = bpm.HasValue
                ? drills.FindIndex(d => DrillNaming.ParseBpm(d) is double other && other > bpm.Value)
                : -1;
            if (at < 0) drills.Add(name);
            else drills.Insert(at, name);
        }

        /// <summary>
        /// 同じ方向のドリルから、BPM の小さい方（<paramref name="highest"/> なら大きい方）の
        /// <paramref name="limit"/> 本を選び、遅い順に並べて返す。BPM の読めない曲名は外す。
        /// </summary>
        public static List<string> PickBySpeed(IEnumerable<string> candidates, string direction, bool highest, int limit)
        {
            var bySpeed = candidates
                .Where(n => DrillNaming.DirectionOf(n) == direction)
                .Select(n => new { Name = n, Bpm = DrillNaming.ParseBpm(n) })
                .Where(d => d.Bpm.HasValue)
                .OrderBy(d => d.Bpm.Value)
                .ToList();

            var chosen = highest ? bySpeed.Skip(Math.Max(0, bySpeed.Count - limit)) : bySpeed.Take(limit);
            return chosen.Select(d => d.Name).ToList();
        }

        /// <summary>
        /// 「Other」の表。標準のステージ以外のドリルを方向ごとにまとめ、遅い順に左から並べる。
        /// 「All」の表も同じ並べ方で、標準のステージのドリルも含める（<paramref name="includeDrillSet"/>）。
        /// 1方向で <see cref="MaxDrillsPerRow"/> 本を超えたら、続きは同じ方向の2行目・3行目に送る。
        /// </summary>
        /// <remarks>
        /// 行は方向の名前順。曲名に BPM の無いもの（ID を入れる前の古い名前）は速さで並べられないので入れない。
        /// 同じ ID の譜面が複数の場所にあっても1本と数える。行の数に上限は付けない（画面側で送る）。
        /// </remarks>
        /// <param name="assigned">
        /// カスタムステージ。ここに並べたドリルは Other に出さない（どこかのステージに入っていないものが Other）。
        /// </param>
        /// <param name="includeDrillSet">標準のステージ（112本）のドリルも出すか。All のとき true。</param>
        public static CustomStage Other(IEnumerable<string> songNames, IEnumerable<CustomStage> assigned = null,
            bool includeDrillSet = false)
        {
            if (songNames == null) throw new ArgumentNullException(nameof(songNames));

            var setIds = includeDrillSet
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(DrillSet.All().Select(e => DrillNaming.ExtractId(e.Options.Name)), StringComparer.Ordinal);
            if (assigned != null)
                foreach (var name in assigned.SelectMany(s => s.Rows).SelectMany(r => r.Drills))
                    setIds.Add(DrillNaming.ExtractId(name) ?? name);

            var drills = songNames
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => new { Name = n, Id = DrillNaming.ExtractId(n) ?? n, Bpm = DrillNaming.ParseBpm(n) })
                .Where(d => d.Bpm.HasValue && !setIds.Contains(d.Id))
                .GroupBy(d => d.Id, StringComparer.Ordinal)
                .Select(g => g.First());

            var stage = new CustomStage { Name = "Other" };
            foreach (var group in drills
                .GroupBy(d => DrillNaming.DirectionOf(d.Name), StringComparer.Ordinal)
                .OrderBy(g => g.Key.Length == 0)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var bySpeed = group.OrderBy(d => d.Bpm.Value).ThenBy(d => d.Name, StringComparer.Ordinal).ToList();
                for (int i = 0; i < bySpeed.Count; i += MaxDrillsPerRow)
                {
                    stage.Rows.Add(new CustomStageRow
                    {
                        Name = group.Key.Length > 0 ? group.Key : "-",
                        Drills = bySpeed.Skip(i).Take(MaxDrillsPerRow).Select(d => d.Name).ToList(),
                    });
                }
            }

            return stage;
        }
    }

    /// <summary>
    /// カスタムステージの設定ファイル。1ステージ1ファイルで、ファイル名がステージ名。
    ///
    /// <code>
    /// UserData\JumpDrill\Stages\My stage.json
    /// {
    ///   "rows": [ { "name": "R8bL5a", "drills": [ "[Q7K3M] Drill R8b L5a axis 300 BPM 10s" ] } ]
    /// }
    /// </code>
    ///
    /// 標準のステージ（<see cref="DrillSet"/>）はここに入れない。ID が生成の指定から決まるので、
    /// ファイルで変えられると過去の記録と合わなくなる。
    /// </summary>
    public sealed class CustomStageBook
    {
        public List<CustomStage> Stages { get; } = new List<CustomStage>();

        /// <summary>読めなかったファイルのステージ名。名前は使わせず、ファイルにも触らない。</summary>
        public List<string> Unreadable { get; } = new List<string>();

        /// <summary>読んだときにあったファイルのステージ名。保存のとき、ここにあって今は無いものを消す。</summary>
        private readonly HashSet<string> _onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 読んだとき（と最後に書いたとき）のファイルの中身。保存のとき、ほか（MOD や別の窓）が中身を変えていないかを見る。
        /// </summary>
        private readonly Dictionary<string, string> _diskText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>読んだとき（と最後に書いたとき）のステージの中身（<see cref="ToJson"/>）。こちらで変えたかを見る。</summary>
        private readonly Dictionary<string, string> _loadedJson = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>ファイル名なので大文字と小文字は区別しない。</summary>
        public CustomStage Find(string name)
        {
            return Stages.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>その名前が使われているか（読めなかったファイルの名前も含む）。</summary>
        public bool IsTaken(string name)
        {
            return Find(name) != null || Unreadable.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>ステージ名にファイル名として使えない文字があれば、その理由。使えれば null。</summary>
        public static string CheckFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal) ||
                ReservedFileNames.Contains(name.ToUpperInvariant()))
                return Lang.T("ステージ名に使えない文字があります（\\ / : * ? \" < > | など）。",
                    "The stage name has characters that cannot be used (\\ / : * ? \" < > | etc.).");
            return null;
        }

        private static readonly HashSet<string> ReservedFileNames = new HashSet<string>(
            new[] { "CON", "PRN", "AUX", "NUL" }
                .Concat(Enumerable.Range(1, 9).SelectMany(i => new[] { "COM" + i, "LPT" + i })));

        /// <summary>名前の数字を数として比べる（CUSTOM2 が CUSTOM10 より前）。</summary>
        public void Sort()
        {
            Stages.Sort((a, b) => CompareNatural(a.Name, b.Name));
        }

        private static int CompareNatural(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
                    int c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                }
                else
                {
                    int c = string.Compare(a[i].ToString(), b[j].ToString(), StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }

        /// <summary>並べたドリルの曲名を付け替える（再作成で今の名前にしたとき）。変えたら true。</summary>
        public bool RenameDrills(IReadOnlyDictionary<string, string> renamed)
        {
            bool changed = false;
            if (renamed == null || renamed.Count == 0) return false;
            foreach (var row in Stages.SelectMany(s => s.Rows))
                for (int i = 0; i < row.Drills.Count; i++)
                {
                    string to;
                    if (renamed.TryGetValue(row.Drills[i], out to) && to != row.Drills[i])
                    {
                        row.Drills[i] = to;
                        changed = true;
                    }
                }
            return changed;
        }

        /// <summary>
        /// 設定ファイルを置くフォルダ。Beat Saber ごとの <c>UserData\JumpDrill\Stages</c>。
        /// GUI と MOD で同じフォルダを使い、どちらで作ったステージも両方に出す。
        /// </summary>
        public static string PathFor(string installRoot)
        {
            return Path.Combine(installRoot, "UserData", "JumpDrill", "Stages");
        }

        /// <summary>以前の版の置き場所（ユーザーのフォルダ）。全ステージを1ファイルに入れていた。</summary>
        public static string LegacyPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JumpDrill", "stages.json");
        }

        /// <summary>全ステージを1ファイルに入れていたころの、Beat Saber 側の置き場所。</summary>
        private static string SingleFilePath(string folder)
        {
            return Path.Combine(Path.GetDirectoryName(folder), "stages.json");
        }

        /// <summary>
        /// 全ステージを1ファイルに入れていたころのファイル（Beat Saber 側、無ければユーザーのフォルダ）が残っていれば、
        /// ステージごとのファイルに分けて <paramref name="folder"/> に置き、元のファイルを消す。
        /// 同じ名前のファイルが既にあればそのステージは移さない。移せなくても読み書きは止めない。
        /// </summary>
        public static void MoveLegacy(string folder)
        {
            foreach (string legacy in new[] { SingleFilePath(folder), LegacyPath() })
            {
                try
                {
                    if (!File.Exists(legacy)) continue;
                    SplitSingleFile(legacy, folder);

                    // 空になった以前のフォルダも消す（ユーザーのフォルダの方）
                    string legacyDir = Path.GetDirectoryName(legacy);
                    if (legacy == LegacyPath() && Directory.Exists(legacyDir) && Directory.GetFileSystemEntries(legacyDir).Length == 0)
                        Directory.Delete(legacyDir);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (FormatException) { }
            }
        }

        /// <summary>
        /// 全ステージを入れた1ファイルを、ステージごとのファイルに分けて <paramref name="folder"/> に置き、元を消す。
        /// 同じ名前のファイルが既にあれば、後ろに番号を付けた名前（<c>名前 (2)</c>）で書く。
        /// 旧版は名前の大文字と小文字を区別していたので「abc」と「ABC」が並んでいることがあり、
        /// ファイル名にすると同じになる（名前の置き換えで同じになる組や、2か所の旧ファイルに同じ名前がある場合も）。
        /// 飛ばしてから元を消すと、そのステージが失われる。読めなければ例外で、元のファイルは残る。
        /// </summary>
        public static void SplitSingleFile(string file, string folder)
        {
            var stages = ReadSingleFile(file);

            Directory.CreateDirectory(folder);
            foreach (var stage in stages)
            {
                string name = SafeFileName(stage.Name);
                string path = Path.Combine(folder, name + ".json");
                for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, name + " (" + i + ").json");
                WriteStage(path, stage);
            }
            File.Delete(file);
        }

        /// <summary>1ファイルに全ステージを入れていた形（<c>{ "stages": [ { "name", "rows" } ] }</c>）を読む。</summary>
        private static List<CustomStage> ReadSingleFile(string path)
        {
            var root = JsonReader.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) throw new FormatException("stages.json");

            var stages = new List<CustomStage>();
            foreach (var stageMap in ListOf(root, "stages").OfType<Dictionary<string, object>>())
            {
                var stage = new CustomStage { Name = StringOf(stageMap, "name") };
                stage.Rows.AddRange(RowsOf(stageMap));
                stages.Add(stage);
            }
            return stages;
        }

        /// <summary>ファイル名に使えない文字を _ に替える。空なら CUSTOM。</summary>
        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string safe = new string((name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            if (safe.Length == 0 || ReservedFileNames.Contains(safe.ToUpperInvariant())) safe = "CUSTOM" + safe;
            return safe;
        }

        /// <summary>
        /// <paramref name="folder"/> を含む Beat Saber のフォルダ（Beat Saber.exe のある階層）。無ければ null。
        /// </summary>
        public static string InstallRootOf(string folder)
        {
            try
            {
                for (var dir = new DirectoryInfo(folder); dir != null; dir = dir.Parent)
                    if (File.Exists(Path.Combine(dir.FullName, "Beat Saber.exe"))) return dir.FullName;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            return null;
        }

        /// <summary>
        /// 名前を入れさせずに作るときの名前。<c>CUSTOM1</c>、<c>CUSTOM2</c>… の空いている番号。
        /// </summary>
        public string NextDefaultName()
        {
            for (int i = 1; ; i++)
            {
                string name = "CUSTOM" + i;
                if (!IsTaken(name)) return name;
            }
        }

        /// <summary>
        /// フォルダの中のステージを全部読む。フォルダが無ければ空。
        /// 読めないファイルは飛ばして <see cref="Unreadable"/> に入れる（ほかのステージは使える）。
        /// </summary>
        public static CustomStageBook Load(string folder)
        {
            var book = new CustomStageBook();
            if (!Directory.Exists(folder)) return book;

            foreach (string path in Directory.GetFiles(folder, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    // 名前はファイル名。中に名前が書いてあっても使わない
                    string text = File.ReadAllText(path, Encoding.UTF8);
                    var stage = FromJson(text, name);
                    stage.Name = name;
                    book.Stages.Add(stage);
                    book._diskText[name] = text;
                    book._loadedJson[name] = ToJson(stage);
                }
                catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
                {
                    book.Unreadable.Add(name);
                }
                book._onDisk.Add(name);
            }

            book.Sort();
            return book;
        }

        private static IEnumerable<CustomStageRow> RowsOf(Dictionary<string, object> map)
        {
            return ListOf(map, "rows").OfType<Dictionary<string, object>>().Select(rowMap => new CustomStageRow
            {
                Name = StringOf(rowMap, "name"),
                Drills = ListOf(rowMap, "drills").OfType<string>().Where(d => d.Length > 0).ToList(),
            });
        }

        /// <summary>
        /// ステージごとにファイルへ書く。こちらで変えたステージだけを書き、読んだときから変えていないステージには触らない。
        /// 読んだときにあって今は無いステージ（削除・名前の変更）のファイルは消す。
        /// 読めなかったファイルには触らない。
        /// </summary>
        /// <remarks>
        /// GUI と MOD は同じフォルダを読み書きする。読んだ後でほかが中身を変えた（または作った・消した）ファイルを、
        /// こちらの古い中身で書き直したり消したりすると、ほかでの変更が失われる（消したステージが戻る、足したドリルが消える）。
        /// そういうファイルに書く・消す必要があるときは、何も書かずに <see cref="IOException"/> で断る（開き直してもらう）。
        /// 先に全部見てから書く（途中まで書いてから断ると、ステージの組がばらばらになる）。
        /// </remarks>
        public void Save(string folder)
        {
            Directory.CreateDirectory(folder);

            var writes = new List<CustomStage>();
            foreach (var stage in Stages)
            {
                string path = Path.Combine(folder, stage.Name + ".json");
                string loaded;
                if (_loadedJson.TryGetValue(stage.Name, out loaded))
                {
                    // こちらでは変えていない（大文字と小文字だけ変えた名前は、ファイル名の付け替えが要るので書く）
                    // （ほかで消されたファイルは、変えていなければ書き戻さない）
                    bool caseRename = Directory.GetFiles(folder, stage.Name + ".json").Any(f => Path.GetFileName(f) != stage.Name + ".json");
                    if (loaded == ToJson(stage) && !caseRename) continue;
                    if (ChangedElsewhere(path, stage.Name)) throw Conflict(stage.Name);
                }
                else if (!_onDisk.Contains(stage.Name) && File.Exists(path))
                {
                    throw new IOException(string.Format(Lang.T(
                        "ステージ「{0}」のファイルがほかで作られています。開き直してから、別の名前にしてください。",
                        "A file for the stage \"{0}\" was created elsewhere. Reopen and choose another name."), stage.Name));
                }
                writes.Add(stage);
            }

            var deletes = _onDisk
                .Where(name => Find(name) == null && !Unreadable.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToList();
            foreach (string name in deletes)
                if (ChangedElsewhere(Path.Combine(folder, name + ".json"), name)) throw Conflict(name);

            foreach (var stage in writes)
            {
                string path = Path.Combine(folder, stage.Name + ".json");

                // 大文字と小文字だけ変えた名前は同じファイルになるので、ファイル名も付け替える
                string existing = Directory.GetFiles(folder, stage.Name + ".json").FirstOrDefault();
                if (existing != null && Path.GetFileName(existing) != stage.Name + ".json")
                {
                    File.Move(existing, path + ".rename");
                    File.Move(path + ".rename", path);
                }

                WriteStage(path, stage);
                _onDisk.Add(stage.Name);
                _diskText[stage.Name] = ToJson(stage);
                _loadedJson[stage.Name] = ToJson(stage);
            }

            foreach (string name in deletes)
            {
                // 大文字と小文字だけ変えた名前の古い方は、上で付け替えたファイルそのもの。消さない
                if (Find(name) != null) continue;
                string path = Path.Combine(folder, name + ".json");
                if (File.Exists(path)) File.Delete(path);
                _onDisk.Remove(name);
                _diskText.Remove(name);
                _loadedJson.Remove(name);
            }

            Sort();
        }

        /// <summary>読んだ後で、ほかがそのステージのファイルの中身を変えたか消したか。</summary>
        private bool ChangedElsewhere(string path, string name)
        {
            string known;
            if (!_diskText.TryGetValue(name, out known)) return false;
            if (!File.Exists(path)) return true;
            return File.ReadAllText(path, Encoding.UTF8) != known;
        }

        private static IOException Conflict(string name)
        {
            return new IOException(string.Format(Lang.T(
                "ステージ「{0}」がほか（MOD など）で変わっています。上書きしないように保存をやめました。開き直してください。",
                "The stage \"{0}\" was changed elsewhere (e.g. in the mod). Not saved to keep that change. Please reopen."), name));
        }

        /// <summary>
        /// 1ステージぶんの JSON。ステージのファイルには名前を入れない（ファイル名が名前）。
        /// エクスポートした zip では、取り込む側が名前を知るために入れる（<paramref name="includeName"/>）。
        /// </summary>
        public static string ToJson(CustomStage stage, bool includeName = false)
        {
            var json = new JsonWriter(indent: true, asciiOnly: false);
            json.StartObject();
            if (includeName) json.Property("name", stage.Name);
            WriteRows(json, stage);
            json.EndObject();
            return json.ToString() + "\n";
        }

        /// <summary><c>"rows": [ ... ]</c> を書く。</summary>
        public static void WriteRows(JsonWriter json, CustomStage stage)
        {
            json.Name("rows").StartArray();
            foreach (var row in stage.Rows)
            {
                json.StartObject().Property("name", row.Name).Name("drills").StartArray();
                foreach (var drill in row.Drills) json.Value(drill);
                json.EndArray().EndObject();
            }
            json.EndArray();
        }

        /// <summary>
        /// <see cref="ToJson"/> を読む。名前は JSON にあればそれ、無ければ <paramref name="name"/>。
        /// 形が違えば <see cref="FormatException"/>。
        /// </summary>
        public static CustomStage FromJson(string text, string name)
        {
            var root = JsonReader.Parse(text) as Dictionary<string, object>;
            if (root == null) throw new FormatException(Lang.T("ステージのファイルの形が違います。", "The stage file is not in the expected format."));

            string inside = StringOf(root, "name");
            var stage = new CustomStage { Name = inside.Length > 0 ? inside : name };
            stage.Rows.AddRange(RowsOf(root));
            return stage;
        }

        private static void WriteStage(string path, CustomStage stage)
        {
            string text = ToJson(stage);
            if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == text) return;

            // 書きかけで落ちても元のファイルが残るよう、別名に書いてから置き換える
            string temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        private static List<object> ListOf(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) && value is List<object> list ? list : new List<object>();
        }

        private static string StringOf(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) && value is string text ? text : "";
        }
    }
}
