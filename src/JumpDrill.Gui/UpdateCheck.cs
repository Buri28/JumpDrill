using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JumpDrill.Gui
{
    /// <summary>
    /// 配布版の更新確認。GitHub Releases の最新版と、exe の隣の version.json を比べる。
    /// 実際の差し替えは同じフォルダの Update.exe が行う。
    /// 開発中のビルド（Update.exe が隣に無い）では何もしない。
    /// </summary>
    internal static class UpdateCheck
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/Buri28/JumpDrill/releases/latest";

        private static string AppDir => AppContext.BaseDirectory;

        private static string UpdaterPath => Path.Combine(AppDir, "Update.exe");

        public static bool Available => File.Exists(UpdaterPath);

        /// <summary>version.json に書かれた今のバージョン。読めなければ null。</summary>
        public static string CurrentVersion()
        {
            try
            {
                string path = Path.Combine(AppDir, "version.json");
                if (!File.Exists(path)) return null;
                Match match = Regex.Match(File.ReadAllText(path), @"""version""\s*:\s*""([^""]+)""");
                return match.Success ? match.Groups[1].Value.TrimStart('v', 'V') : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>最新リリースのタグ（v1.2.3）を返す。</summary>
        public static async Task<string> LatestTagAsync()
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("JumpDrill-Gui");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                string json = await client.GetStringAsync(LatestReleaseUrl).ConfigureAwait(false);
                using (var doc = JsonDocument.Parse(json))
                    return doc.RootElement.GetProperty("tag_name").GetString();
            }
        }

        /// <summary>latest が current より新しいか。どちらかが読めなければ false。</summary>
        public static bool IsNewer(string latest, string current)
        {
            Version a, b;
            return Version.TryParse(Normalize(latest), out a)
                && Version.TryParse(Normalize(current), out b)
                && a > b;
        }

        /// <summary>Update.exe を開く。tag を渡すとそのリリースを選んだ状態で開く。</summary>
        public static void LaunchUpdater(string tag)
        {
            var start = new ProcessStartInfo(UpdaterPath) { UseShellExecute = false, WorkingDirectory = AppDir };
            if (!string.IsNullOrEmpty(tag))
            {
                start.ArgumentList.Add("--tag");
                start.ArgumentList.Add(tag);
            }
            Process.Start(start);
        }

        private static string Normalize(string version)
        {
            // 1.2.3-beta のような後ろの付記は比べない。
            string value = (version ?? string.Empty).Trim().TrimStart('v', 'V');
            int cut = value.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0) value = value.Substring(0, cut);
            return value.Contains('.') ? value : value + ".0";
        }
    }
}
