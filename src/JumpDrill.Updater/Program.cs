using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace JumpDrill.Updater
{
    /// <summary>
    /// JumpDrill の配布版を GitHub Releases の zip で更新する。
    ///
    /// 起動のしかたは2通り。
    ///   Update.exe [--tag v1.2.3]      リリースを選ぶ画面を出す
    ///   Update.exe --apply-staged ...  画面から呼ばれる。本体の終了を待って差し替え、再起動する
    ///
    /// 差し替え中は自分の exe も上書きされるので、適用は一時フォルダへ
    /// コピーした Update.exe が行う。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; // TLS 1.3

            Options options;
            try
            {
                options = Options.Parse(args);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, UpdateDialog.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (options.ApplyStaged)
            {
                try
                {
                    Installer.ApplyStagedPackage(options);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("更新できませんでした。\r\n" + ex.Message, UpdateDialog.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UpdateDialog(options));
        }
    }

    internal static class Product
    {
        public const string Owner = "Buri28";
        public const string Repo = "JumpDrill";

        /// <summary>更新する本体。Update.exe と同じフォルダにある。</summary>
        public const string AppExeName = "JumpDrillGui.exe";

        /// <summary>リリースの zip は JumpDrill-v1.2.3.zip のように名付ける。</summary>
        public const string AssetPrefix = "JumpDrill";

        public const string VersionFileName = "version.json";

        /// <summary>前回の更新で置いたファイルの一覧。次の更新で消えたファイルを片付けるのに使う。</summary>
        public const string ManifestFileName = "installed-files.txt";

        /// <summary>利用者のデータが入るフォルダ。更新では触らない。</summary>
        public static readonly string[] PreservedDirectories = { "workspace" };
    }

    internal sealed class Options
    {
        public string InstallDir;
        public string ExePath;
        public string Tag;
        public bool ApplyStaged;
        public string ZipPath;
        public int WaitPid;
        public bool NoRestart;
        public string CleanupDir;
        public string TargetVersion;

        public static Options Parse(string[] args)
        {
            var options = new Options
            {
                InstallDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            };

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--tag": options.Tag = Value(args, ref i); break;
                    case "--install-dir": options.InstallDir = Value(args, ref i); break;
                    case "--apply-staged": options.ApplyStaged = true; break;
                    case "--zip": options.ZipPath = Value(args, ref i); break;
                    case "--wait-pid": options.WaitPid = int.Parse(Value(args, ref i), CultureInfo.InvariantCulture); break;
                    case "--no-restart": options.NoRestart = true; break;
                    case "--cleanup-dir": options.CleanupDir = Value(args, ref i); break;
                    case "--target-version": options.TargetVersion = Value(args, ref i); break;
                    default: throw new InvalidOperationException("知らない引数です: " + arg);
                }
            }

            options.InstallDir = Path.GetFullPath(options.InstallDir);
            options.ExePath = Path.Combine(options.InstallDir, Product.AppExeName);
            if (!File.Exists(options.ExePath))
            {
                throw new InvalidOperationException(
                    Product.AppExeName + " が見つかりません。\r\n"
                    + "Update.exe は " + Product.AppExeName + " と同じフォルダに置いて使ってください。\r\n"
                    + "フォルダ: " + options.InstallDir);
            }

            if (options.ApplyStaged)
            {
                if (string.IsNullOrEmpty(options.ZipPath))
                    throw new InvalidOperationException("--apply-staged には --zip が必要です。");
                options.ZipPath = Path.GetFullPath(options.ZipPath);
                if (!string.IsNullOrEmpty(options.CleanupDir))
                    options.CleanupDir = Path.GetFullPath(options.CleanupDir);
            }
            return options;
        }

        private static string Value(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
                throw new InvalidOperationException(args[index] + " の値がありません。");
            return args[++index];
        }
    }

    internal sealed class ReleaseInfo
    {
        public string TagName;
        public string Title;
        public string Body;
        public string PublishedAt;
        public string ZipUrl;
        public bool Prerelease;

        public string Version => (TagName ?? string.Empty).TrimStart('v', 'V');

        public string PublishedAtLocal()
        {
            DateTimeOffset parsed;
            if (!DateTimeOffset.TryParse(PublishedAt ?? string.Empty, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out parsed))
                return PublishedAt ?? string.Empty;
            return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        public override string ToString()
        {
            string text = TagName;
            if (!string.IsNullOrEmpty(Title) && Title != TagName)
                text += "  " + Title;
            if (Prerelease)
                text += "  (プレリリース)";
            if (!string.IsNullOrEmpty(PublishedAt))
                text += "  " + PublishedAtLocal();
            return text;
        }
    }

    internal sealed class UpdateDialog : Form
    {
        public const string Title = "JumpDrill の更新";

        private readonly Options _options;
        // 長いフォルダのパスで列が広がらないよう、収まらない分は「…」で切る
        private readonly Label _header = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), AutoEllipsis = true };
        private readonly Label _status = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };
        private readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        private readonly TextBox _notes = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly ProgressBar _progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false, Height = 14 };
        private readonly Button _reload = new Button { Text = "再読み込み", AutoSize = true };
        private readonly Button _close = new Button { Text = "閉じる", AutoSize = true };
        private readonly Button _apply = new Button { Text = "更新する", AutoSize = true };
        private readonly BackgroundWorker _loadWorker = new BackgroundWorker();
        private readonly BackgroundWorker _applyWorker = new BackgroundWorker();
        private List<ReleaseInfo> _releases = new List<ReleaseInfo>();

        public UpdateDialog(Options options)
        {
            _options = options;

            Text = Title;
            Font = new Font("Yu Gothic UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ClientSize = new Size(620, 520);
            MinimumSize = new Size(480, 400);

            _header.Text = "フォルダ: " + options.InstallDir + "\r\n"
                         + "今のバージョン: " + (Installer.ReadCurrentVersion(options.InstallDir) ?? "不明");

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 8, 0, 0),
            };
            buttons.Controls.Add(_apply);
            buttons.Controls.Add(_close);
            buttons.Controls.Add(_reload);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
            // 列は窓の幅に合わせる。既定（中身に合わせる）だと、長いパスに引っぱられて右端が窓の外に出る
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_header, 0, 0);
            root.Controls.Add(_status, 0, 1);
            root.Controls.Add(_list, 0, 2);
            root.Controls.Add(_notes, 0, 3);
            root.Controls.Add(_progress, 0, 4);
            root.Controls.Add(buttons, 0, 5);
            Controls.Add(root);
            DarkTheme.Apply(this);
            // 最初は一覧を選んでおく（読むだけのリリースノートに入力のカーソルが出ないように）
            ActiveControl = _list;

            AcceptButton = _apply;
            CancelButton = _close;

            _list.SelectedIndexChanged += (s, e) => ShowNotes();
            _list.DoubleClick += (s, e) => BeginApply();
            _reload.Click += (s, e) => LoadReleases();
            _close.Click += (s, e) => Close();
            _apply.Click += (s, e) => BeginApply();

            _loadWorker.DoWork += (s, e) => e.Result = GitHubReleases.List(30);
            _loadWorker.RunWorkerCompleted += LoadCompleted;
            _applyWorker.DoWork += ApplyWork;
            _applyWorker.RunWorkerCompleted += ApplyCompleted;

            Shown += (s, e) => LoadReleases();
        }

        private void SetBusy(bool busy, string message)
        {
            _status.Text = message;
            _progress.Visible = busy;
            _reload.Enabled = !busy;
            _apply.Enabled = !busy;
            _close.Enabled = !busy;
            _list.Enabled = !busy;
        }

        private void LoadReleases()
        {
            if (_loadWorker.IsBusy) return;
            SetBusy(true, "リリースを取得しています...");
            _loadWorker.RunWorkerAsync();
        }

        private void LoadCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                SetBusy(false, "リリースを取得できませんでした。");
                MessageBox.Show(this, "リリースを取得できませんでした。\r\n" + e.Error.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _releases = (List<ReleaseInfo>)e.Result;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var release in _releases)
                _list.Items.Add(release);
            _list.EndUpdate();
            SelectInitial();
            SetBusy(false, _releases.Count == 0 ? "リリースがありません。" : _releases.Count + " 件のリリースがあります。");
        }

        /// <summary>--tag で渡されたものを選ぶ。なければ一番新しいもの。</summary>
        private void SelectInitial()
        {
            if (_list.Items.Count == 0) return;
            string wanted = GitHubReleases.NormalizeTag(_options.Tag);
            for (int i = 0; i < _list.Items.Count; i++)
            {
                if (string.Equals(((ReleaseInfo)_list.Items[i]).TagName, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    _list.SelectedIndex = i;
                    return;
                }
            }
            _list.SelectedIndex = 0;
        }

        private void ShowNotes()
        {
            var release = _list.SelectedItem as ReleaseInfo;
            if (release == null)
            {
                _notes.Text = string.Empty;
                return;
            }
            var text = new StringBuilder();
            text.AppendLine(release.ToString());
            text.AppendLine();
            string body = string.IsNullOrEmpty(release.Body) ? "リリースノートはありません。" : release.Body;
            text.Append(body.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            _notes.Text = text.ToString();
        }

        private void BeginApply()
        {
            if (_applyWorker.IsBusy || _loadWorker.IsBusy) return;

            var release = _list.SelectedItem as ReleaseInfo;
            if (release == null)
            {
                MessageBox.Show(this, "リリースを選んでください。", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (string.IsNullOrEmpty(release.ZipUrl))
            {
                MessageBox.Show(this, release.TagName + " にはダウンロードできる zip がありません。", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Installer.IsDevelopmentFolder(_options.InstallDir))
            {
                MessageBox.Show(this, "開発中のビルドなので更新しません。" + Environment.NewLine + "一覧とリリースノートの確認だけできます。",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (Installer.IsProcessRunning(_options.ExePath))
            {
                MessageBox.Show(this, "JumpDrill を閉じてから更新してください。", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, release.TagName + " に更新します。よろしいですか？", Title,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            SetBusy(true, "ダウンロードしています...");
            _applyWorker.RunWorkerAsync(release);
        }

        private void ApplyWork(object sender, DoWorkEventArgs e)
        {
            var release = (ReleaseInfo)e.Argument;
            string stageDir = Installer.CreateTempDirectory("JumpDrillUpdate_");
            string zipPath = Path.Combine(stageDir, "release.zip");
            GitHubReleases.Download(release.ZipUrl, zipPath);

            // 中身を先に確かめておく。壊れた zip で本体を消してしまわないように。
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                if (!zip.Entries.Any(entry => string.Equals(entry.Name, Product.AppExeName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("zip の中に " + Product.AppExeName + " がありません。");
            }

            string helper = Installer.CopySelfToTempDirectory();
            Installer.LaunchHelper(helper, _options, zipPath, stageDir, release.Version, Process.GetCurrentProcess().Id);
            e.Result = release;
        }

        private void ApplyCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                SetBusy(false, "更新の準備に失敗しました。");
                MessageBox.Show(this, "更新の準備に失敗しました。\r\n" + e.Error.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var release = (ReleaseInfo)e.Result;
            SetBusy(false, "準備ができました。");
            MessageBox.Show(this, release.TagName + " の準備ができました。\r\nこの画面を閉じると更新して JumpDrill を起動します。", Title,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }

    internal static class GitHubReleases
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static List<ReleaseInfo> List(int limit)
        {
            string url = "https://api.github.com/repos/" + Product.Owner + "/" + Product.Repo + "/releases?per_page=" + limit;
            var releases = Serializer.Deserialize<List<ReleaseDto>>(DownloadText(url)) ?? new List<ReleaseDto>();
            return releases
                .Where(dto => dto != null && !dto.draft && !string.IsNullOrEmpty(dto.tag_name))
                .Select(dto => new ReleaseInfo
                {
                    TagName = dto.tag_name,
                    Title = string.IsNullOrEmpty(dto.name) ? dto.tag_name : dto.name,
                    Body = dto.body ?? string.Empty,
                    PublishedAt = dto.published_at ?? string.Empty,
                    Prerelease = dto.prerelease,
                    ZipUrl = FindZipUrl(dto.assets),
                })
                .ToList();
        }

        public static string NormalizeTag(string tag)
        {
            string value = (tag ?? string.Empty).Trim();
            if (value.Length > 0 && !value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                value = "v" + value;
            return value;
        }

        public static void Download(string url, string path)
        {
            using (var client = CreateClient())
                client.DownloadFile(url, path);
        }

        private static string DownloadText(string url)
        {
            using (var client = CreateClient())
                return client.DownloadString(url);
        }

        private static WebClient CreateClient()
        {
            var client = new WebClient { Encoding = Encoding.UTF8 };
            client.Headers[HttpRequestHeader.UserAgent] = "JumpDrill-Update";
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        /// <summary>JumpDrill.zip か JumpDrill-*.zip を探す。</summary>
        private static string FindZipUrl(List<AssetDto> assets)
        {
            foreach (var asset in assets ?? new List<AssetDto>())
            {
                string name = asset.name ?? string.Empty;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, Product.AssetPrefix + ".zip", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(Product.AssetPrefix + "-", StringComparison.OrdinalIgnoreCase))
                    return asset.browser_download_url;
            }
            return null;
        }

        private sealed class ReleaseDto
        {
            public string tag_name { get; set; }
            public string name { get; set; }
            public string body { get; set; }
            public string published_at { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
            public List<AssetDto> assets { get; set; }
        }

        private sealed class AssetDto
        {
            public string name { get; set; }
            public string browser_download_url { get; set; }
        }
    }

    internal static class Installer
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string ReadCurrentVersion(string installDir)
        {
            try
            {
                string path = Path.Combine(installDir, Product.VersionFileName);
                if (!File.Exists(path)) return null;
                Match match = Regex.Match(File.ReadAllText(path, Encoding.UTF8), @"""version""\s*:\s*""([^""]+)""");
                return match.Success ? match.Groups[1].Value.TrimStart('v', 'V') : null;
            }
            catch
            {
                return null;
            }
        }

        public static string CreateTempDirectory(string prefix)
        {
            string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        public static string CopySelfToTempDirectory()
        {
            string source = Assembly.GetExecutingAssembly().Location;
            string dest = Path.Combine(CreateTempDirectory("JumpDrillUpdateHelper_"), Path.GetFileName(source));
            File.Copy(source, dest, true);
            return dest;
        }

        public static void LaunchHelper(string helperExe, Options options, string zipPath, string stageDir, string version, int waitPid)
        {
            var args = new List<string>
            {
                "--apply-staged",
                "--zip", Quote(zipPath),
                "--install-dir", Quote(options.InstallDir),
                "--wait-pid", waitPid.ToString(CultureInfo.InvariantCulture),
                "--cleanup-dir", Quote(stageDir),
            };
            if (!string.IsNullOrEmpty(version))
            {
                args.Add("--target-version");
                args.Add(Quote(version));
            }
            Process.Start(new ProcessStartInfo(helperExe, string.Join(" ", args))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }

        /// <summary>
        /// リポジトリの bin の中か。上書きすると単一 exe の配布物とビルド出力が混ざるので、更新はしない。
        /// </summary>
        public static bool IsDevelopmentFolder(string installDir)
        {
            for (var dir = new DirectoryInfo(installDir); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "JumpDrill.sln")))
                    return true;
            }
            return false;
        }

        public static void ApplyStagedPackage(Options options)
        {
            if (IsDevelopmentFolder(options.InstallDir))
                throw new InvalidOperationException("開発中のビルドなので更新しません。");
            WaitForExit(options.WaitPid);
            WaitUntilAppClosed(options.ExePath);

            string stageDir = !string.IsNullOrEmpty(options.CleanupDir) ? options.CleanupDir : Path.GetDirectoryName(options.ZipPath);
            string sourceRoot = Extract(options.ZipPath, Path.Combine(stageDir, "unzipped"));
            Install(sourceRoot, options.InstallDir);
            if (!string.IsNullOrEmpty(options.TargetVersion))
                WriteVersion(options.InstallDir, options.TargetVersion);

            if (!string.IsNullOrEmpty(options.CleanupDir))
                TryDeleteDirectory(options.CleanupDir);
            if (!options.NoRestart && File.Exists(options.ExePath))
                Process.Start(new ProcessStartInfo(options.ExePath) { UseShellExecute = true, WorkingDirectory = options.InstallDir });
        }

        public static bool IsProcessRunning(string exePath)
        {
            string fullPath = Path.GetFullPath(exePath);
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(fullPath)))
            {
                try
                {
                    if (string.Equals(process.MainModule.FileName, fullPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                    // 他のユーザーのプロセスなどは覗けない。無関係として扱う。
                }
                finally
                {
                    process.Dispose();
                }
            }
            return false;
        }

        private static void WaitForExit(int pid)
        {
            if (pid <= 0) return;
            try
            {
                using (Process process = Process.GetProcessById(pid))
                    process.WaitForExit(60000);
            }
            catch
            {
                // もう終わっている。
            }
        }

        /// <summary>本体が開いたままだと上書きできない。閉じるまで尋ね続ける。</summary>
        private static void WaitUntilAppClosed(string exePath)
        {
            while (IsProcessRunning(exePath))
            {
                for (int i = 0; i < 20 && IsProcessRunning(exePath); i++)
                    Thread.Sleep(250);
                if (!IsProcessRunning(exePath)) return;
                if (MessageBox.Show("JumpDrill を閉じてから OK を押してください。", UpdateDialog.Title,
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                    throw new OperationCanceledException("更新を取りやめました。");
            }
        }

        /// <summary>zip を展開し、exe のあるフォルダを返す（zip の直下でもサブフォルダでもよい）。</summary>
        private static string Extract(string zipPath, string extractDir)
        {
            TryDeleteDirectory(extractDir);
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            string exe = Directory.GetFiles(extractDir, Product.AppExeName, SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .FirstOrDefault();
            if (exe == null)
                throw new InvalidOperationException("zip の中に " + Product.AppExeName + " がありません。");
            return Path.GetDirectoryName(exe);
        }

        /// <summary>
        /// 新しいファイルを上書きし、前回の更新で置いたのに今回は無いファイルを消す。
        /// 利用者が自分で置いたファイルと workspace には触らない。
        /// </summary>
        private static void Install(string sourceDir, string destDir)
        {
            // 通常は一時フォルダの複製が動いているが、直接起動されたときは自分を上書きできない。
            string self = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            var installed = new List<string>();
            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string rel = RelativePath(sourceDir, file);
                if (IsPreserved(rel)) continue;
                string target = Path.Combine(destDir, rel);
                if (string.Equals(Path.GetFullPath(target), self, StringComparison.OrdinalIgnoreCase))
                {
                    installed.Add(rel);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                CopyWithRetry(file, target);
                installed.Add(rel);
            }

            var current = new HashSet<string>(installed, StringComparer.OrdinalIgnoreCase);
            string manifest = Path.Combine(destDir, Product.ManifestFileName);
            if (File.Exists(manifest))
            {
                foreach (string rel in File.ReadAllLines(manifest, Encoding.UTF8))
                {
                    string trimmed = rel.Trim();
                    if (trimmed.Length == 0 || current.Contains(trimmed) || IsPreserved(trimmed) || !IsInside(destDir, trimmed)) continue;
                    try
                    {
                        File.Delete(Path.Combine(destDir, trimmed));
                    }
                    catch
                    {
                        // 消せなくても動作に支障はない。
                    }
                }
            }
            File.WriteAllLines(manifest, installed, Utf8NoBom);
        }

        private static void CopyWithRetry(string source, string target)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Copy(source, target, true);
                    return;
                }
                catch (IOException) when (attempt < 20)
                {
                    // 終了直後はまだ exe が掴まれていることがある。
                    Thread.Sleep(250);
                }
                catch (UnauthorizedAccessException) when (attempt < 20)
                {
                    Thread.Sleep(250);
                }
            }
        }

        private static bool IsPreserved(string rel)
        {
            string[] parts = rel.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 && Product.PreservedDirectories.Contains(parts[0], StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsInside(string root, string rel)
        {
            string full = Path.GetFullPath(Path.Combine(root, rel));
            string prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string RelativePath(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            return Path.GetFullPath(path).Substring(prefix.Length);
        }

        private static void WriteVersion(string installDir, string version)
        {
            string json = "{\r\n  \"version\": \"" + version.TrimStart('v', 'V') + "\"\r\n}\r\n";
            File.WriteAllText(Path.Combine(installDir, Product.VersionFileName), json, Utf8NoBom);
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).TrimEnd('\\') + "\"";
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
                // 一時フォルダなので残っても害はない。
            }
        }
    }
}
