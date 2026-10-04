using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using JumpDrill.Audio;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;
using JumpDrill.Replays;

namespace JumpDrill.Cli
{
    public static class Program
    {
        private static readonly HashSet<string> KnownOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "seq","interval","div","sec","dir","hands","mirror","order","sets",
            "njs","jd","rt","offset","bpm",
            "click","count-in","tail","lead-in","gap",
            "name","out","install","register-pack","zip","wav","quality","rate",
            "bulk","skip-existing","medals",
            "dry-run","h","help",
        };

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { /* リダイレクト先によっては失敗する */ }

            if (args.Length == 0 || args.Any(a => a == "-h" || a == "--help"))
            {
                Console.WriteLine(HelpText);
                return args.Length == 0 ? 1 : 0;
            }

            try
            {
                return Run(new CommandLine(args));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("エラー: " + ex.Message);
                return 1;
            }
        }

        private static int Run(CommandLine cmd)
        {
            var unknown = cmd.UnknownKeys(KnownOptions).ToList();
            if (unknown.Count > 0)
                throw new FormatException("知らないオプション: " + string.Join(", ", unknown.Select(u => "--" + u).ToArray()));

            if (cmd.Has("bulk"))
                return RunBulk(cmd);

            if (cmd.Has("medals"))
                return RunMedals();

            var sequences = BuildSequences(cmd);
            var template = BuildOptions(cmd, sequences);
            bool dryRun = cmd.Has("dry-run");
            string outRoot = ResolveOutputRoot(cmd, dryRun);

            var writeOptions = new LevelWriteOptions
            {
                Format = cmd.Has("wav") ? AudioFormat.Wav : AudioFormat.Ogg,
                SampleRate = cmd.GetInt("rate") ?? ClickTrackRenderer.DefaultSampleRate,
                OggQuality = (float)(cmd.GetDouble("quality") ?? OggEncoder.DefaultQuality),
                ZipDirectory = cmd.Has("zip")
                    ? (cmd.GetOptionalString("zip") ?? LevelZipWriter.DefaultDirectory())
                    : null,
            };

            string explicitName = cmd.GetString(new[] { "name" });

            template.Name = DrillNaming.Compose(template, explicitName);

            var map = DrillGenerator.Generate(template);

            Console.WriteLine(map.Name);
            Console.Write(LevelWriter.Summarize(map));

            if (!dryRun)
            {
                string folder = LevelWriter.Write(map, outRoot, null, writeOptions);
                Console.WriteLine("  出力        " + folder);
                if (LevelWriter.LastZipPath != null)
                    Console.WriteLine("  zip         " + LevelWriter.LastZipPath);
            }
            Console.WriteLine();

            if (dryRun)
                Console.WriteLine("(--dry-run のため書き出していません)");

            return 0;
        }

        /// <summary>
        /// 一括生成のドリルを全部書く。中身は固定なので、使うのは出力先の指定だけ。
        /// </summary>
        private static int RunBulk(CommandLine cmd)
        {
            bool dryRun = cmd.Has("dry-run");

            if (dryRun)
            {
                foreach (var entry in DrillSet.All())
                    Console.WriteLine(entry.Options.Name + "  NJS" + entry.Options.Njs.ToString("0.#", CultureInfo.InvariantCulture));
                Console.WriteLine("(--dry-run のため書き出していません)");
                return 0;
            }

            string outRoot = ResolveOutputRoot(cmd, false);
            var writeOptions = new LevelWriteOptions
            {
                ZipDirectory = cmd.Has("zip")
                    ? (cmd.GetOptionalString("zip") ?? LevelZipWriter.DefaultDirectory())
                    : null,
            };

            DrillSetWriter.WriteAll(outRoot, writeOptions,
                (done, total, name) => Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  [{0,2}/{1}] {2}", done, total, name)),
                cmd.Has("skip-existing"));

            Console.WriteLine("  出力        " + outRoot);
            return 0;
        }

        /// <summary>
        /// 一括生成のドリルのメダルと Lv を出す。記録は GUI の成績と同じ読み方。
        /// </summary>
        private static int RunMedals()
        {
            var scores = DrillRecords.Load(Workspace.DefaultRoot());
            var rows = DrillSetMedals.Evaluate(DrillSetMedals.BestById(scores));
            var ci = CultureInfo.InvariantCulture;

            Console.WriteLine(string.Format(ci, "総合 Lv {0} / {1}", rows.Sum(r => r.Level), DrillSetMedals.MaxLevel));
            Console.WriteLine();

            foreach (var row in rows)
            {
                Console.WriteLine(string.Format(ci, "{0,-8} Lv {1}", row.Direction, row.Level));

                var bpm = new StringBuilder("  ");
                var medals = new StringBuilder("  ");
                foreach (var cell in row.Cells)
                {
                    string label = string.Format(ci, "{0:0}", cell.Entry.Bpm) +
                        (cell.Entry.Options.DurationSeconds > DrillSet.ShortSeconds ? "*" : "");
                    bpm.Append(label.PadLeft(5));
                    // 絵文字も「・」も端末で2桁ぶん取るので、5桁の BPM の欄の右に寄せる。
                    medals.Append("   ").Append(DrillSetMedals.Emoji(cell.Medal));
                }
                Console.WriteLine(bpm.ToString());
                Console.WriteLine(medals.ToString());
            }

            Console.WriteLine();
            Console.WriteLine("  * は 30 秒の Stage 8。🥉 70% / 🥈 80% / 🥇 90% 以上（再現精度）");
            return 0;
        }

        private static List<HandSequence> BuildSequences(CommandLine cmd)
        {
            string spec = cmd.GetString(new[] { "seq" });
            if (spec == null)
                throw new FormatException("--seq が指定されていません。例: --seq \"R:8>b\"");

            var sequences = SequenceParser.ParseAll(spec);

            if (cmd.Has("mirror"))
            {
                if (sequences.Count != 1)
                    throw new FormatException("--mirror は片手ぶんだけ指定しているときに使えます。");
                var source = sequences[0];
                var other = source.Hand == Hand.Right ? Hand.Left : Hand.Right;
                sequences.Add(SequenceParser.Mirror(source, other));
            }

            ApplyOrder(sequences, cmd.GetString(new[] { "order" }));
            return sequences;
        }

        /// <summary>
        /// どちらの手から始めるか。split ではブロックの順番がそのまま
        /// 「右→左」か「左→右」かになる。
        /// </summary>
        private static void ApplyOrder(List<HandSequence> sequences, string order)
        {
            if (order == null) return;

            Hand first;
            switch (order.ToLowerInvariant())
            {
                case "rl": case "r": case "right": first = Hand.Right; break;
                case "lr": case "l": case "left": first = Hand.Left; break;
                default: throw new FormatException("--order は rl / lr です。");
            }

            var ordered = sequences.OrderBy(s => s.Hand == first ? 0 : 1).ToList();
            sequences.Clear();
            sequences.AddRange(ordered);
        }

        private static DrillOptions BuildOptions(CommandLine cmd, IReadOnlyList<HandSequence> sequences)
        {
            var options = new DrillOptions { Sequences = sequences };

            double notesPerBeat = NotesPerBeat(cmd);
            string intervalText = cmd.GetString(new[] { "interval" });
            if (intervalText != null)
                options.IntervalMs = IntervalParser.ParseIntervalMs(intervalText, notesPerBeat);
            // 生成には効かない。BPM 表示を指定どおりの細分化で出すためだけに持たせる。
            options.NotesPerBeat = notesPerBeat;
            options.DurationSeconds = cmd.GetDouble("sec") ?? options.DurationSeconds;
            options.Direction = ParseDirectionMode(cmd.GetString(new[] { "dir" }));
            options.HandPattern = ParseHandPattern(cmd.GetString(new[] { "hands" }));
            options.Bpm = cmd.GetDouble("bpm") ?? options.Bpm;
            options.Njs = cmd.GetDouble("njs") ?? options.Njs;
            options.JumpDistance = cmd.GetDouble("jd");
            options.ReactionTimeMs = cmd.GetDouble("rt");
            options.NoteJumpStartBeatOffset = cmd.GetDouble("offset");
            options.Click = ParseClickMode(cmd.GetString(new[] { "click" }));
            options.CountInClicks = cmd.GetInt("count-in") ?? options.CountInClicks;
            options.TailSeconds = cmd.GetDouble("tail") ?? options.TailSeconds;
            options.MinLeadInSeconds = cmd.GetDouble("lead-in") ?? options.MinLeadInSeconds;
            options.GapSeconds = cmd.GetDouble("gap");
            options.Sets = cmd.GetInt("sets") ?? options.Sets;

            options.Validate();
            return options;
        }

        private static DrillOptions Clone(DrillOptions source)
        {
            return new DrillOptions
            {
                Sequences = source.Sequences,
                IntervalMs = source.IntervalMs,
                DurationSeconds = source.DurationSeconds,
                NotesPerBeat = source.NotesPerBeat,
                Direction = source.Direction,
                HandPattern = source.HandPattern,
                Bpm = source.Bpm,
                Njs = source.Njs,
                JumpDistance = source.JumpDistance,
                ReactionTimeMs = source.ReactionTimeMs,
                NoteJumpStartBeatOffset = source.NoteJumpStartBeatOffset,
                Click = source.Click,
                CountInClicks = source.CountInClicks,
                TailSeconds = source.TailSeconds,
                MinLeadInSeconds = source.MinLeadInSeconds,
                GapSeconds = source.GapSeconds,
                Sets = source.Sets,
                Name = source.Name,
            };
        }

        /// <summary>
        /// bpm 指定のときの細分化。BPM だけでは 1/1 と 1/2 で間隔が倍違うので、
        /// ここを明示できるようにしてある（設計メモ §2）。
        /// </summary>
        private static double NotesPerBeat(CommandLine cmd)
        {
            string text = cmd.GetString(new[] { "div" });
            if (text == null) return 1.0;
            return IntervalParser.ParseNotesPerBeat(text);
        }

        private static double ParseDouble(string text, string what)
        {
            double value;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException(what + " '" + text + "' が数値として読めません。");
            return value;
        }

        private static string ResolveOutputRoot(CommandLine cmd, bool dryRun)
        {
            if (dryRun) return null;

            string outDir = cmd.GetString(new[] { "out" });
            if (outDir != null) return Path.GetFullPath(outDir);

            string packName = cmd.GetString(new[] { "register-pack" });
            if (packName != null)
            {
                // 独立パックとして曲一覧に出す。WIP 扱いのままなのでスコアは送信されない。
                string install = LevelWriter.GuessWipLevelsDirectory();
                if (install == null)
                    throw new IOException("Beat Saber のインストール先が見つかりませんでした。--out で場所を指定してください。");

                string installRoot = Path.GetDirectoryName(Path.GetDirectoryName(install));
                string folder = Path.Combine(installRoot, packName);
                string xml = SongCoreFolders.FoldersXmlFor(installRoot);

                if (SongCoreFolders.Register(xml, packName, folder))
                    Console.WriteLine("  パック登録  " + packName + " → " + xml);
                else
                    Console.WriteLine("  パック登録  " + packName + " は登録済み");

                return folder;
            }

            if (cmd.Has("install"))
            {
                // 既定は WIP。CustomLevels に置くと ScoreSaber などが
                // 自作ドリルのスコアを未ランクのリーダーボードに上げてしまう。
                string kind = (cmd.GetOptionalString("install") ?? "wip").ToLowerInvariant();

                switch (kind)
                {
                    case "wip":
                        string wip = LevelWriter.GuessWipLevelsDirectory();
                        if (wip == null)
                            throw new IOException("CustomWIPLevels が見つかりませんでした。--out で場所を指定してください。");
                        return wip;

                    case "normal":
                    case "custom":
                        string custom = LevelWriter.GuessCustomLevelsDirectory();
                        if (custom == null)
                            throw new IOException("CustomLevels が見つかりませんでした。--out で場所を指定してください。");
                        return custom;

                    default:
                        throw new FormatException("--install は wip / normal です。");
                }
            }

            return Path.GetFullPath("out");
        }

        private static DirectionMode ParseDirectionMode(string text)
        {
            if (text == null) return DirectionMode.Axis;
            switch (text.ToLowerInvariant())
            {
                case "axis": case "1": case "lv1": return DirectionMode.Axis;
                case "perp": case "perpendicular": case "2": case "lv2": return DirectionMode.Perpendicular;
                case "dot": case "3": case "lv3": return DirectionMode.Dot;
                case "explicit": case "exp": return DirectionMode.Explicit;
                default: throw new FormatException("--dir は axis / perp / dot / explicit です。");
            }
        }

        private static HandPattern ParseHandPattern(string text)
        {
            if (text == null) return HandPattern.Sync;
            switch (text.ToLowerInvariant())
            {
                case "sync": case "both": case "same": return HandPattern.Sync;
                case "alt": case "alternate": return HandPattern.Alternate;
                case "split": case "seq": case "serial": case "sequential": case "solo": return HandPattern.Split;
                default: throw new FormatException("--hands は sync / alt / split です。");
            }
        }

        private static ClickMode ParseClickMode(string text)
        {
            if (text == null) return ClickMode.Down;
            switch (text.ToLowerInvariant())
            {
                case "none": case "off": case "0": return ClickMode.None;
                case "all": case "1": case "lv1": return ClickMode.All;
                case "down": case "2": case "lv2": return ClickMode.Down;
                case "up": case "3": case "lv3": return ClickMode.Up;
                case "every4": case "4": case "lv4": return ClickMode.Every4;
                default: throw new FormatException("--click は none / all / down / up / every4 です。");
            }
        }

        private const string HelpText = @"JumpDrill - ジャンプ練習譜面ジェネレータ

  drill --seq ""R8b, La1"" --interval 110 --sec 20 --dir axis
  drill --seq ""L194c""    --interval 130 --sec 30 --install
  drill --seq ""R4sbs""    --interval 110 --sec 30
  drill --seq ""R8b"" --interval 350bpm --div 1/2 --sec 20
  drill --bulk --register-pack ""JumpDrill""
  drill --seq ""R8b"" --mirror --hands split --sec 30
  drill --seq ""R8b"" --mirror --hands split --sec 30 --order rl --sets 3

グリッド記法
  1 2 3 4     左上から 1、下段は 9 a b c。
  5 6 7 8     手の接頭辞 R: / L: を付ける（省略すると右手）。
  9 a b c     カンマで両手ぶんを並べられる。2点でも3点以上でもよい。
              : と > は省ける。""R:4>b"" は ""R4b"" と書いてもよい。

遷移と方向
  --seq <spec>       遷移。例 ""R8b""、""L194c""、""R8b, La1""
                     点の後ろに s を付けると、その点の矢印を
                     上下左右のいちばん近い向きに倒す（Straight / Square）
                       R4b    4→b は Δ(-1,-2) なので b は斜めの ↙
                       R4bs   b だけ ▼ に倒す（4 は ↗ のまま）
                       R4sbs  両端とも倒す。b が ▼、4 が ▲
                     向きは動きが決めるので、配置と矛盾する矢印にはならない。
                     s は短い形でも > の形でも書ける（""R:4>bs""）
  --dir <mode>       axis  配置ベクトルと一致（既定・一直線の往復）
                     perp  配置ベクトルに垂直（移動軸と振り軸が直交）
                     dot   ドット（方向の制約なし）
                     explicit  記法内の @ 指定を使う
  --mirror           片手指定を左右反転して反対の手にも同じ形を組む
  --hands <mode>     sync   両手同時（既定）
                     split  片手ずつ。右で1本叩いてから左で1本叩く。
                            ジャンプ練習で普通に欲しいのはこれ。
                            このとき --sec は「手あたりの秒数」になる
                     alt    1ステップごとに交互。同じ配置を続けて往復しないので
                            ジャンプ練習にはならない
  --order <rl|lr>    split でどちらの手から始めるか。rl = 右→左 / lr = 左→右
                     省略すると --seq に書いた順（--mirror では元の手が先）
  --sets <n>         セットの繰り返し回数。既定 1
                     split の1セットは「各手1本ずつ」。--sets 3 なら
                     右→左→右→左→右→左 と並ぶ
  --gap <秒>         ブロックの切れ目の間。既定はカウントインが収まる長さ

時間
  --interval <値>    ノーツ間隔。既定 300（単位なしは ms）
                     110 / 110ms   そのまま ms
                     300bpm        BPM で指定。--div と組で ms に換算する
                                   ジャンプなら --div 1/2 で、譜面名や
                                   ScoreSaber の「350 BPM」と同じ数え方になる
                     350ebpm       片手の速さで指定（= 片手ずつの 350bpm --div 1/2）
  --div <n>          bpm 指定のときの細分化。既定 1（ebpm では使わない）
                     音符でも1拍あたりのノーツ数でも書ける（1/2 と 2 は同じ）
                       350bpm --div 1/1  → 171.4ms  350 ノーツ/分
                       350bpm --div 1/2  →  85.7ms  700 ノーツ/分
                       350bpm --div 1/4  →  42.9ms 1400 ノーツ/分
                     BPM だけでは細分化で倍違うので、ここを明示する
  --sec <秒>         ドリル本体の尺。既定 30

ジャンプ（互いに独立に指定できる）
  --njs <n>          NJS。既定 16
  --jd <m>           目標ジャンプ距離。オフセットを逆算する
  --rt <ms>          目標反応時間。オフセットを逆算する
  --offset <拍>      _noteJumpStartBeatOffset を直接指定
  --bpm <n>          info.dat の _beatsPerMinute。既定 120。
                     時間軸の基準でしかなく、ノーツの速さは --interval で決まる

クリック（音源に焼き込む）
  --click <mode>     none / all(Lv1) / down(Lv2・既定) / up(Lv3) / every4(Lv4)
  --count-in <n>     カウントインのクリック数。既定 8
  --lead-in <秒>     曲頭の空白の下限。既定 2（ジャンプ時間から自動で伸びる）
  --tail <秒>        末尾の余白。既定 1.5

ドリル一括生成（方向ごとの速さの Stage）
  --bulk             14方向 × 8 Stage = 112本をまとめて書く。--seq などは使わない
                     各 Stage 右10秒→左10秒、350 BPM だけ右30秒→左30秒の Stage 8 も置く
                     遠い配置（49 85 c9）は BPM ×1/2、
                     横・斜めの遠め（4a 89 86 ca）は ×2/3
                     曲名は {R3bL2a 100} [<ID>] Drill ... で方向・速さ順に並ぶ
  --skip-existing    すでに書いてある段は飛ばす（途中から再開するとき）
  --medals           一括生成のドリルのメダルと Lv を出す（リプレイから読む）
                     🥉 70% / 🥈 80% / 🥇 90% 以上（再現精度）。Lv は 🥉1 🥈2 🥇3 の合計

出力
  --out <dir>        出力先。既定 ./out
  --install [wip]    Beat Saber の CustomWIPLevels を探してそこに書く（既定）
  --register-pack <名前>
                     SongCore の folders.xml に独立パックとして登録し、そこに書く。
                     曲一覧に専用パックとして並び、WIP 扱いなのでスコアは送信されない。
                     プレイリストは WIP を解決できないので、まとめるならこちら
  --install normal   CustomLevels の方に書く。
                     ScoreSaber / BeatLeader は CustomLevels に置いた譜面なら
                     未公開・自作でも未ランクのリーダーボードにスコアを上げる。
                     練習用のドリルは WIP のままにしておくのが本来
  --name <s>         曲名のうち遷移と向きの部分を差し替える
                     頭の ID と末尾の BPM は付いたまま
                     （曲名 [<ID>] Drill <遷移> <向き> <BPM> BPM <尺>）
                     BPM は片手の速さ（1/2）。両手交互では入れた値より小さく出る
  --zip [dir]        BeatSaver 形式の zip も書き出す。既定は
                     %LOCALAPPDATA%\JumpDrill\zip
                     BeatLeader のリプレイ表示は譜面を BeatSaver から探すので、
                     未公開の自作ドリルでは «Map was not found» になる。
                     その画面でこの zip を読ませれば表示できる
  --wav              ogg ではなく wav で書く（検証用。ゲームでは読めない）
  --quality <f>      ogg の品質 0.0-1.0。既定 0.4
  --rate <hz>        サンプリングレート。既定 44100
  --dry-run          書き出さずに内容だけ表示する
";
    }
}
