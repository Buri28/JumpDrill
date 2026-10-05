using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// 設定から JumpDrill.Core を呼んで譜面を書き出すだけの層。
    /// </summary>
    /// <remarks>
    /// <b>ここに生成の知識を持たせない。</b> 遷移のパース・ノーツの配置・
    /// ジャンプ距離の逆算・音源とカバーの生成・Info.dat の書式は全て JumpDrill.Core にある。
    /// このMODが足すのは「ゲーム内のどこへ書くか」だけ。
    /// 譜面が正しいかどうかは JumpDrill の CLI とテストで検証する。
    ///
    /// 組み立て方は JumpDrill の CLI（<c>Program.BuildSequences</c> / <c>BuildOptions</c>）に
    /// 合わせてある。同じ指定なら CLI と同じ譜面が出るようにしておかないと、
    /// CLI で詰めた設定をゲーム内で再現できない。
    /// </remarks>
    internal class DrillGenerationService
    {
        /// <summary>
        /// 1拍あたりのノーツ数。<b>1/2 に固定してある。</b>
        /// </summary>
        /// <remarks>
        /// 選ばせずに固定することで「BPM だけでは間隔が決まらない」曖昧さを潰す。
        /// 1/2 なら BPM がそのまま EBPM と同じ値になるので、単位を2つ持つ必要もない。
        /// JumpDrill の GUI と同じ扱い。
        /// </remarks>
        private const double NotesPerBeat = 2.0;

        private readonly DrillPackLocator pack;

        internal DrillGenerationService(DrillPackLocator pack)
        {
            this.pack = pack;
        }

        /// <summary>1本ぶんの生成結果。</summary>
        internal sealed class Result
        {
            /// <summary>書き出した譜面フォルダ。失敗したときは null。</summary>
            public string? Folder { get; internal set; }

            /// <summary>曲一覧に出る名前。失敗したときは null。</summary>
            public string? SongName { get; internal set; }

            /// <summary>UI にそのまま出す1行。成功でも失敗でも入る。</summary>
            public string Message { get; internal set; } = string.Empty;

            public bool Ok => Folder != null;

            /// <summary>既にあったフォルダを書き直したか。</summary>
            public bool Overwrote { get; internal set; }
        }

        /// <summary>
        /// 設定の内容で1本作って専用パックに書き出す。
        /// 例外は投げずに <see cref="Result"/> に畳む。押した直後に落ちるとログを見に行く羽目になるため。
        /// </summary>
        internal Result Generate(PluginConfig config)
        {
            try
            {
                var map = DrillGenerator.Generate(BuildOptions(config));
                string root = pack.ResolveOutputFolder(config.PackName);

                // 名前は「遷移・向き・片手の間隔・尺・セット数」から決まる。クリック音などは入らないので、
                // それだけ変えて作ると同じフォルダになる。上書きしないなら書かずに知らせる
                // 既にあるかは Info.dat で見る（一括生成と同じ）。書きかけで止まったフォルダは作り直せるように
                bool exists = File.Exists(Path.Combine(root, LevelWriter.SanitizeFolderName(map.Name), "Info.dat"));
                if (exists && !config.Overwrite)
                    return new Result { Message = "Already exists. Turn on Overwrite to write it again." };

                string folder = LevelWriter.Write(map, root, null, new LevelWriteOptions
                {
                    Format = AudioFormat.Ogg,
                    Overwrite = true,
                });

                Plugin.LogDebug("generated: " + folder);
                return new Result
                {
                    Folder = folder,
                    SongName = map.Name,
                    Message = map.Name,
                    Overwrote = exists,
                };
            }
            catch (FormatException e)
            {
                // 遷移記法が読めない。UI から組んでいる限り起きないが、
                // 設定ファイルを手で直したときに来る
                return Failure("Invalid transition", e);
            }
            catch (ArgumentException e)
            {
                // 点が1つしかない等。DrillOptions.Validate より手前で出る
                return Failure("Invalid transition", e);
            }
            catch (InvalidOperationException e)
            {
                // DrillOptions.Validate の指摘
                return Failure("Invalid settings", e);
            }
            catch (IOException e)
            {
                return Failure("Could not write the level", e);
            }
            catch (UnauthorizedAccessException e)
            {
                return Failure("Could not write the level", e);
            }
        }

        /// <remarks>
        /// UI に出す文は英語で短く固定し、原因は例外ごとログへ出す。
        /// JumpDrill.Core のメッセージは日本語なので、そのまま画面に出すと混ざる。
        /// </remarks>
        private static Result Failure(string message, Exception e)
        {
            Plugin.Log?.Error("generate failed: " + e);
            return new Result { Message = message };
        }

        /// <summary>設定を JumpDrill.Core の指定に移す。</summary>
        private static DrillOptions BuildOptions(PluginConfig config)
        {
            // 設定ファイルを手で 0 以下にされても換算で落ちないようにする。
            // 値の妥当性そのものは JumpDrill.Core の Validate が見る
            double bpm = config.IntervalBpm > 0 ? config.IntervalBpm : 1;

            var options = new DrillOptions
            {
                Sequences = BuildSequences(config),
                IntervalMs = Tempo.IntervalMsFromBpm(bpm, NotesPerBeat),
                // 生成には効かない。BPM 表示を指定どおりの細分化で出すためだけに持たせる（JumpDrill の CLI と同じ）
                NotesPerBeat = NotesPerBeat,
                // 譜面の基準 BPM にも指定した BPM をそのまま使う。
                // 別の値にすると曲一覧の BPM が指定と食い違う。
                // ノーツの実時刻は ms から決まるので譜面自体は変わらない
                Bpm = bpm,
                DurationSeconds = config.DurationSeconds,
                Njs = config.Njs,
                JumpDistance = config.JumpDistance,
                Sets = config.Sets,
                CountInClicks = config.CountInClicks,
                Direction = ParseEnum(config.Direction, DirectionMode.Axis),
                HandPattern = ParseEnum(config.HandPattern, HandPattern.Split),
                Click = ParseEnum(config.Click, ClickMode.Down),
            };

            options.Validate();

            // 譜面名は JumpDrill.Core に作らせる。CLI と GUI もここを通っていて、
            // 名前の頭に入る ID は「遷移・向き・片手の間隔・尺・セット数」から決まる。
            // 自前で名前を付けると ID が入らず、リプレイから譜面へ辿れなくなるうえ、
            // CLI で作った同じドリルと別フォルダになってしまう。
            options.Name = DrillNaming.Compose(options);
            return options;
        }

        /// <summary>
        /// 遷移を組む。両手のときは指定した手の遷移を左右反転してもう片方に足す
        /// （CLI の <c>--mirror</c>）。
        /// </summary>
        private static List<HandSequence> BuildSequences(PluginConfig config)
        {
            var sequences = SequenceParser.ParseAll(config.Sequence);

            if (config.Mirror && sequences.Count == 1)
            {
                var source = sequences[0];
                var other = source.Hand == Hand.Right ? Hand.Left : Hand.Right;
                sequences.Add(SequenceParser.Mirror(source, other));
            }

            // 右手を先に。GUI の「セットの順番」の既定（右 → 左）と同じにする。
            // 順番は ID に入るので、揃えないと同じ形でも GUI と別のドリルになり記録が混ざらない
            return sequences.OrderBy(s => s.Hand == Hand.Right ? 0 : 1).ToList();
        }

        /// <summary>
        /// 設定ファイルには enum を名前で持たせている。手で書き換えられたときに
        /// 落とさず既定へ倒す（設定が1つ壊れただけで生成できなくなると困るため）。
        /// </summary>
        private static T ParseEnum<T>(string? text, T fallback) where T : struct
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            T parsed;
            if (Enum.TryParse(text!.Trim(), ignoreCase: true, result: out parsed)) return parsed;

            Plugin.Log?.Warn($"unknown {typeof(T).Name}: '{text}'. falling back to {fallback}");
            return fallback;
        }
    }
}
