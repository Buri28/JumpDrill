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
        /// ドリルを足す。<paramref name="row"/> が行の数と同じなら新しい行を作る。
        /// 新しい行で違う方向を混ぜて選んだら、方向ごとに行を分ける。行の名前は方向（<c>R8bL5a</c>）。
        /// 行と1行の本数の上限を超える分は足さない。足せなかった本数を返す。
        /// </summary>
        /// <remarks>既にある行への方向の絞り込みは、選ばせる側でやる（<see cref="DirectionOf"/>）。</remarks>
        public int AddDrills(int row, IReadOnlyList<string> picked)
        {
            int skipped = 0;
            if (row < Rows.Count)
            {
                var drills = Rows[row].Drills;
                int room = Math.Max(0, MaxDrillsPerRow - drills.Count);
                drills.AddRange(picked.Take(room));
                return Math.Max(0, picked.Count - room);
            }

            foreach (var group in picked.GroupBy(DrillNaming.DirectionOf))
            {
                if (Rows.Count >= MaxRows) { skipped += group.Count(); continue; }

                string name = group.Key.Length > 0 ? group.Key : "Row " + (Rows.Count + 1);
                Rows.Add(new CustomStageRow { Name = name, Drills = group.Take(MaxDrillsPerRow).ToList() });
                skipped += Math.Max(0, group.Count() - MaxDrillsPerRow);
            }
            return skipped;
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
        /// 1方向で <see cref="MaxDrillsPerRow"/> 本を超えたら、続きは同じ方向の2行目・3行目に送る。
        /// </summary>
        /// <remarks>
        /// 行は方向の名前順。曲名に BPM の無いもの（ID を入れる前の古い名前）は速さで並べられないので入れない。
        /// 同じ ID の譜面が複数の場所にあっても1本と数える。行の数に上限は付けない（画面側で送る）。
        /// </remarks>
        /// <param name="assigned">
        /// カスタムステージ。ここに並べたドリルは Other に出さない（どこかのステージに入っていないものが Other）。
        /// </param>
        public static CustomStage Other(IEnumerable<string> songNames, IEnumerable<CustomStage> assigned = null)
        {
            if (songNames == null) throw new ArgumentNullException(nameof(songNames));

            var setIds = new HashSet<string>(
                DrillSet.All().Select(e => DrillNaming.ExtractId(e.Options.Name)), StringComparer.Ordinal);
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
    /// カスタムステージの設定ファイル（JSON）。
    ///
    /// <code>
    /// {
    ///   "stages": [
    ///     { "name": "My stage", "rows": [ { "name": "R8bL5a", "drills": [ "[Q7K3M] Drill R8b L5a axis 300 BPM 10s" ] } ] }
    ///   ]
    /// }
    /// </code>
    ///
    /// 標準のステージ（<see cref="DrillSet"/>）はここに入れない。ID が生成の指定から決まるので、
    /// ファイルで変えられると過去の記録と合わなくなる。
    /// </summary>
    public sealed class CustomStageBook
    {
        public List<CustomStage> Stages { get; } = new List<CustomStage>();

        public CustomStage Find(string name)
        {
            return Stages.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        }

        /// <summary>
        /// 設定ファイルの置き場所。GUI と MOD で同じファイルを使い、どちらで作ったステージも両方に出す。
        /// </summary>
        public static string DefaultPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JumpDrill", "stages.json");
        }

        /// <summary>
        /// 名前を入れさせずに作るときの名前。<c>CUSTOM1</c>、<c>CUSTOM2</c>… の空いている番号。
        /// </summary>
        public string NextDefaultName()
        {
            for (int i = 1; ; i++)
            {
                string name = "CUSTOM" + i;
                if (Find(name) == null) return name;
            }
        }

        /// <summary>ファイルを読む。無ければ空。壊れていれば例外。</summary>
        public static CustomStageBook Load(string path)
        {
            var book = new CustomStageBook();
            if (!File.Exists(path)) return book;

            var root = JsonReader.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) throw new FormatException(Lang.T("ステージの設定ファイルの形が違います。", "The stage file is not in the expected format."));

            foreach (var stageObj in ListOf(root, "stages"))
            {
                var stageMap = stageObj as Dictionary<string, object>;
                if (stageMap == null) continue;

                var stage = new CustomStage { Name = StringOf(stageMap, "name") };
                foreach (var rowObj in ListOf(stageMap, "rows"))
                {
                    var rowMap = rowObj as Dictionary<string, object>;
                    if (rowMap == null) continue;

                    stage.Rows.Add(new CustomStageRow
                    {
                        Name = StringOf(rowMap, "name"),
                        Drills = ListOf(rowMap, "drills").OfType<string>().Where(d => d.Length > 0).ToList(),
                    });
                }
                book.Stages.Add(stage);
            }

            return book;
        }

        public void Save(string path)
        {
            var json = new JsonWriter(indent: true, asciiOnly: false);
            json.StartObject().Name("stages").StartArray();
            foreach (var stage in Stages)
            {
                json.StartObject().Property("name", stage.Name).Name("rows").StartArray();
                foreach (var row in stage.Rows)
                {
                    json.StartObject().Property("name", row.Name).Name("drills").StartArray();
                    foreach (var drill in row.Drills) json.Value(drill);
                    json.EndArray().EndObject();
                }
                json.EndArray().EndObject();
            }
            json.EndArray().EndObject();

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // 書きかけで落ちても元のファイルが残るよう、別名に書いてから置き換える
            string temp = path + ".tmp";
            File.WriteAllText(temp, json.ToString() + "\n", new UTF8Encoding(false));
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
