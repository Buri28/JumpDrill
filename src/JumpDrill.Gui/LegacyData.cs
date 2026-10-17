using System;
using System.IO;
using JumpDrill.Model;
using JumpDrill.Output;
using System.Threading.Tasks;
using JumpDrill.Replays;

namespace JumpDrill.Gui
{
    /// <summary>
    /// 0.1.0 までがユーザーのフォルダに置いていたものを、Beat Saber とアプリのフォルダへ移す。
    /// </summary>
    /// <remarks>
    /// 移す先:
    /// <list type="bullet">
    /// <item>settings.txt → アプリの workspace（<see cref="SettingsStore.DefaultPath"/>）</item>
    /// <item>stages.json → 1つの Beat Saber の UserData\JumpDrill（<see cref="StagesInstall"/>）</item>
    /// <item>out・zip（古い出力） → アプリの workspace の out・zip</item>
    /// <item>mod（取ってきた DLL） → 消す。要るときにまた取ってくる</item>
    /// <item>MOD のリプレイ（Beat Saber の UserData\JumpDrillMod\Replays） → UserData\JumpDrill\Replays
    ///   （<see cref="ReplayLibrary.MoveLegacyRecordings"/>。見つかった Beat Saber 全部。こちらは毎回見る）</item>
    /// </list>
    /// 移し終えたら %LOCALAPPDATA%\JumpDrill を消す。
    /// 0.1.0 の Update.exe が %TEMP% に残した作業フォルダと、新しい Update.exe がアプリのフォルダに残した .update も消す。
    /// どれも失敗しても起動は止めない（次の起動でもう一度試す）。
    /// </remarks>
    internal static class LegacyData
    {
        private static string LegacyRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JumpDrill");

        public static void Migrate()
        {
            Try(CleanUpdateFolders);

            // 0.1.0 の MOD が UserData\JumpDrillMod\Replays に残したリプレイを、UserData\JumpDrill\Replays へ移す。
            // MOD も起動時に移すが、GUI だけ新しくしてゲームを起動していないこともある。
            // Beat Saber を探すのに全ドライブを見る（ネットワークドライブなどで数秒かかる）ので、窓を出すのを待たせない
            Task.Run(() =>
            {
                foreach (var install in LevelWriter.FindInstalls())
                    Try(() => ReplayLibrary.MoveLegacyRecordings(install.Root));
            });

            string root = LegacyRoot;
            if (!Directory.Exists(root)) return;

            Try(() => MoveSettings(root));
            Try(() => MoveStages());
            Try(() => MergeFolder(Path.Combine(root, "out"), Path.Combine(Workspace.DefaultRoot(), "out")));
            Try(() => MergeFolder(Path.Combine(root, "zip"), Path.Combine(Workspace.DefaultRoot(), "zip")));
            Try(() =>
            {
                string mod = Path.Combine(root, "mod");
                if (Directory.Exists(mod)) Directory.Delete(mod, true);
            });
            Try(() => DeleteIfEmpty(root));
        }

        private static void MoveSettings(string root)
        {
            string legacy = Path.Combine(root, "settings.txt");
            if (!File.Exists(legacy)) return;

            string path = SettingsStore.DefaultPath();
            if (File.Exists(path))
            {
                File.Delete(legacy);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.Move(legacy, path);
        }

        /// <summary>
        /// 1つの Beat Saber にだけ移す。移す先は、出力先の入っている Beat Saber、
        /// 無ければメダル画面で選んでいた Beat Saber、それも無ければ Beat Saber が1つだけのときはそれ。
        /// 決まらなければ消さずに残す（中身を失わないため）。
        /// </summary>
        private static void MoveStages()
        {
            string legacy = CustomStageBook.LegacyPath();
            if (!File.Exists(legacy)) return;

            string install = StagesInstall();
            if (install == null) return;

            string path = CustomStageBook.PathFor(install);
            if (File.Exists(path))
            {
                File.Delete(legacy);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.Move(legacy, path);
        }

        private static string StagesInstall()
        {
            var settings = new SettingsStore(SettingsStore.DefaultPath());
            settings.Load();

            string outDir = settings.GetString("out.dir", "");
            string fromOut = outDir.Length > 0 ? CustomStageBook.InstallRootOf(outDir) : null;
            if (fromOut != null) return fromOut;

            string selected = settings.GetString("recordsInstall", "");
            if (selected.Length > 0 && File.Exists(Path.Combine(selected, "Beat Saber.exe"))) return selected;

            var installs = LevelWriter.FindInstalls();
            return installs.Count == 1 ? installs[0].Root : null;
        }

        /// <summary>
        /// <paramref name="source"/> の中身を <paramref name="target"/> へ移す。
        /// 同じ名前が既にあれば、後ろに番号を付けて移す（どちらも消さない）。
        /// </summary>
        private static void MergeFolder(string source, string target)
        {
            if (!Directory.Exists(source)) return;
            Directory.CreateDirectory(target);

            foreach (string dir in Directory.GetDirectories(source))
                MoveDirectory(dir, FreeName(Path.Combine(target, Path.GetFileName(dir))));
            foreach (string file in Directory.GetFiles(source))
                File.Move(file, FreeName(Path.Combine(target, Path.GetFileName(file))));

            DeleteIfEmpty(source);
        }

        /// <summary>Directory.Move はドライブをまたげないので、そのときは写してから消す。</summary>
        private static void MoveDirectory(string source, string target)
        {
            if (string.Equals(Path.GetPathRoot(Path.GetFullPath(source)), Path.GetPathRoot(Path.GetFullPath(target)),
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(source, target);
                return;
            }

            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
                MoveDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
            Directory.Delete(source, true);
        }

        private static string FreeName(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;

            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                string candidate = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
        }

        /// <summary>中が空のフォルダだけなら、まとめて消す。</summary>
        private static void DeleteIfEmpty(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string sub in Directory.GetDirectories(dir)) DeleteIfEmpty(sub);
            if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
        }

        /// <summary>更新の作業フォルダ。更新が終わってアプリが起動していれば、もう要らない。</summary>
        private static void CleanUpdateFolders()
        {
            string work = Path.Combine(AppContext.BaseDirectory, ".update");
            if (Directory.Exists(work)) Try(() => Directory.Delete(work, true));

            string temp = Path.GetTempPath();
            foreach (string pattern in new[] { "JumpDrillUpdate_*", "JumpDrillUpdateHelper_*" })
                foreach (string dir in Directory.GetDirectories(temp, pattern))
                    Try(() => Directory.Delete(dir, true));
        }

        private static void Try(Action action)
        {
            try { action(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
