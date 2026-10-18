using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using JumpDrill.Model;
using JumpDrill.Serialization;

namespace JumpDrill.Output
{
    /// <summary>
    /// カスタムステージのエクスポート・インポートのファイル（GUI のメダル画面）。
    ///
    /// <code>
    /// {
    ///   "jumpDrillStage": 1,
    ///   "name": "My stage",
    ///   "rows": [ { "name": "R8bL5a", "drills": [ "[Q7K3M] Drill R8b L5a axis 300 BPM 10s" ] } ],
    ///   "drills": { "[Q7K3M] Drill R8b L5a axis 300 BPM 10s": { "njs": 16, "click": "Down", ... } }
    /// }
    /// </code>
    ///
    /// 譜面は入れない。取り込む側で持っていないドリルは、曲名と <c>drills</c> の指定から作る
    /// （<see cref="DrillRebuild.FromName"/> / <see cref="DrillRebuild.ApplySettings"/>）。
    /// 曲名に入らない指定（NJS・オフセット・クリックなど）を <c>drills</c> に書くので、作った人と同じ指定になる。
    /// </summary>
    /// <remarks>
    /// 版の違い:
    /// <list type="bullet">
    /// <item><c>jumpDrillStage</c> が読める形の番号より大きければ読まない（新しい版で書いたファイル）。
    ///   項目を足すだけなら番号は上げない。知らない項目は読み飛ばす。</item>
    /// <item>曲名から指定を読めないドリル（新しい版の書き方の名前など）は作らずに知らせる。</item>
    /// <item>譜面は取り込む側の版の生成の決まりで作る（再作成と同じ）。ID は曲名で決まるので記録はそろう。</item>
    /// </list>
    /// </remarks>
    public static class CustomStageExport
    {
        /// <summary>この版が書く・読める形の番号。</summary>
        public const int Format = 1;

        private const string FormatKey = "jumpDrillStage";

        /// <summary>読んだファイルの中身。</summary>
        public sealed class Exported
        {
            public CustomStage Stage { get; internal set; }

            /// <summary>曲名ごとの指定（<see cref="DrillRebuild.WriteSettings"/> の形）。無いドリルもある。</summary>
            public Dictionary<string, Dictionary<string, object>> Settings { get; } =
                new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        }

        /// <summary>
        /// 書く。<paramref name="settingsOf"/> は曲名からその譜面の指定を返す（譜面が見つからなければ null で、指定は書かない）。
        /// </summary>
        public static void Write(string path, CustomStage stage, Func<string, DrillOptions> settingsOf)
        {
            var json = new JsonWriter(indent: true, asciiOnly: false);
            json.StartObject();
            json.Property(FormatKey, Format);
            json.Property("name", stage.Name);
            CustomStageBook.WriteRows(json, stage);

            json.Name("drills").StartObject();
            foreach (string drill in stage.Rows.SelectMany(r => r.Drills).Distinct(StringComparer.Ordinal))
            {
                var options = settingsOf(drill);
                if (options == null) continue;
                json.Name(drill);
                DrillRebuild.WriteSettings(json, options);
            }
            json.EndObject();
            json.EndObject();

            File.WriteAllText(path, json.ToString() + "\n", new UTF8Encoding(false));
        }

        /// <summary>読む。形が違う・新しすぎるときは理由付きの <see cref="FormatException"/>。</summary>
        public static Exported Read(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            var root = JsonReader.Parse(text) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue(FormatKey, out var f) || !(f is double format))
                throw new FormatException(Lang.T("カスタムステージのファイルではありません。", "This is not a custom stage file."));
            if (format > Format)
                throw new FormatException(Lang.T("新しい版の JumpDrill で書き出したファイルです。JumpDrill を更新してください。",
                    "This file was exported by a newer JumpDrill. Please update JumpDrill."));

            var exported = new Exported { Stage = CustomStageBook.FromJson(text, Path.GetFileNameWithoutExtension(path)) };
            if (root.TryGetValue("drills", out var d) && d is Dictionary<string, object> drills)
                foreach (var pair in drills)
                    if (pair.Value is Dictionary<string, object> settings) exported.Settings[pair.Key] = settings;
            return exported;
        }

        /// <summary>取り込みの段取り。</summary>
        public sealed class ImportPlan
        {
            /// <summary>取り込むステージ。持っているドリルは手元の曲名に付け替えてある。</summary>
            public CustomStage Stage { get; internal set; }

            /// <summary>作るドリルの指定。</summary>
            public List<DrillOptions> ToGenerate { get; } = new List<DrillOptions>();

            /// <summary>曲名から指定を読めず、作れないドリル。ステージには残す。</summary>
            public List<string> CannotGenerate { get; } = new List<string>();
        }

        /// <summary>
        /// 手元のドリル（<paramref name="localSongNames"/>）と突き合わせて、作るものを決める。
        /// 同じ ID のドリルを持っていれば作らず、そちらの曲名に付け替える（手元の NJS などはそのまま）。
        /// </summary>
        public static ImportPlan Plan(Exported exported, IEnumerable<string> localSongNames)
        {
            var local = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in localSongNames)
            {
                string id = DrillNaming.ExtractId(name);
                if (id != null && !local.ContainsKey(id)) local[id] = name;
            }

            var plan = new ImportPlan { Stage = exported.Stage };
            var planned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in exported.Stage.Rows)
                for (int i = 0; i < row.Drills.Count; i++)
                {
                    string drill = row.Drills[i];
                    string id = DrillNaming.ExtractId(drill);
                    if (id != null && local.TryGetValue(id, out var mine))
                    {
                        row.Drills[i] = mine;
                        continue;
                    }
                    if (!planned.Add(id ?? drill)) continue;

                    try
                    {
                        var options = DrillRebuild.StandardOptions(drill);
                        if (options == null)
                        {
                            options = DrillRebuild.FromName(drill);
                            if (exported.Settings.TryGetValue(drill, out var settings)) DrillRebuild.ApplySettings(options, settings);
                        }
                        plan.ToGenerate.Add(options);
                    }
                    catch (FormatException)
                    {
                        plan.CannotGenerate.Add(drill);
                    }
                }
            return plan;
        }
    }
}
