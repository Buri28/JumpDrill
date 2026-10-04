using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using JumpDrill.Output;

namespace JumpDrill.Gui
{
    /// <summary>
    /// Beat Saber に入れる JumpDrillMod.dll を用意する。MOD はゲームの版ごとにビルドが分かれている。
    ///
    /// 探す順は、exe の隣の mod\&lt;ゲームの版&gt;\（開発中のビルド）→ 前に取ってきたもの →
    /// GitHub のリリース。リリースはアプリと同じ版（v + version.json）のものを使う。
    /// GUI と MOD は記録を同じ形で読み書きするので、版をずらさない。
    /// 資産の名前は JumpDrillMod-v&lt;版&gt;-bs&lt;ゲームの版&gt;.zip（build-release.ps1 が作る）。
    /// </summary>
    internal static class ModPackage
    {
        public const string DllName = "JumpDrillMod.dll";

        private const string ReleaseByTagUrl = "https://api.github.com/repos/Buri28/JumpDrill/releases/tags/v";

        /// <summary>取り込み済みになった昔の DLL。以前の版が Libs に置いていた。</summary>
        private static readonly string[] ObsoleteLibs = { "JumpDrill.Core.dll", "OggVorbisEncoder.dll" };

        /// <summary>リリースの資産（名前 → URL）。開いている間は取り直さない。</summary>
        private static Dictionary<string, string> _assets;

        private static string LocalFolder => Path.Combine(AppContext.BaseDirectory, "mod");

        private static string CacheFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JumpDrill", "mod", "v" + Version);

        /// <summary>入れる MOD の版。アプリと同じ。</summary>
        public static string Version => UpdateCheck.CurrentVersion();

        /// <summary>版が分からないと、どのリリースから取ればよいか決まらない。</summary>
        public static bool Available => Version != null;

        /// <summary>そのインストールに入っている MOD のバージョン。入っていなければ null。</summary>
        public static string InstalledVersion(LevelWriter.BeatSaberInstall install)
        {
            return DllVersion(Path.Combine(install.PluginsPath, DllName));
        }

        /// <summary>アプリの方が新しいか。入っていないものは数えない。</summary>
        public static bool IsOutdated(LevelWriter.BeatSaberInstall install)
        {
            System.Version installed, current;
            return System.Version.TryParse(InstalledVersion(install) ?? "", out installed)
                && System.Version.TryParse(Version ?? "", out current)
                && current > installed;
        }

        /// <summary>そのインストールのゲームが起動中か。起動中は DLL を上書きできない。</summary>
        public static bool IsGameRunning(LevelWriter.BeatSaberInstall install)
        {
            string exe = Path.Combine(install.Root, "Beat Saber.exe");
            foreach (var process in Process.GetProcessesByName("Beat Saber"))
            {
                try
                {
                    if (string.Equals(process.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
            }
            return false;
        }

        /// <summary>
        /// そのゲームの版の DLL を用意して、場所を返す。手元に無ければリリースから取ってくる。
        /// 用意できなければ、読める文言の例外を投げる。
        /// </summary>
        public static async Task<string> PrepareAsync(string game)
        {
            foreach (string folder in new[] { LocalFolder, CacheFolder })
            {
                string path = Path.Combine(folder, game, DllName);
                if (File.Exists(path)) return path;
            }

            var assets = await ReleaseAssetsAsync();
            string url;
            if (!assets.TryGetValue(AssetName(game), out url))
            {
                var games = SupportedGames(assets);
                throw new InvalidOperationException(Lang.T(
                    "このバージョンには対応していません。" + (games.Count > 0 ? "（対応: " + string.Join(", ", games) + "）" : ""),
                    "This version is not supported." + (games.Count > 0 ? " (Supported: " + string.Join(", ", games) + ")" : "")));
            }

            byte[] zip;
            using (var client = CreateClient(TimeSpan.FromMinutes(2)))
                zip = await client.GetByteArrayAsync(url);

            string target = Path.Combine(CacheFolder, game, DllName);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, DllName, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    throw new InvalidDataException(Lang.T(AssetName(game) + " に " + DllName + " が入っていません。",
                                                          AssetName(game) + " does not contain " + DllName + "."));
                // 書きかけを残さないよう、別名で書いてから置き換える。
                string partial = target + ".part";
                entry.ExtractToFile(partial, true);
                File.Copy(partial, target, true);
                File.Delete(partial);
            }
            return target;
        }

        /// <summary>
        /// 用意できる MOD の、対応する Beat Saber のバージョン（古い順）。
        /// 手元にあるもの（開発中のビルド・前に取ってきたもの）と、リリースにあるもの。
        /// リリースを読めなくても、手元にあるものだけで返す。
        /// </summary>
        public static async Task<List<string>> AvailableGamesAsync()
        {
            var games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in new[] { LocalFolder, CacheFolder })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string dir in Directory.GetDirectories(folder))
                    if (File.Exists(Path.Combine(dir, DllName))) games.Add(Path.GetFileName(dir));
            }

            try
            {
                foreach (string game in SupportedGames(await ReleaseAssetsAsync())) games.Add(game);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is HttpRequestException
                                       || ex is TaskCanceledException || ex is JsonException || ex is KeyNotFoundException)
            {
                // リリースを読めないときは手元のものだけ
            }

            return games.OrderBy(g => ParseGame(g)).ThenBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 合う MOD が無いときに最初に選んでおくもの。
        /// そのゲームより古い中でいちばん新しいもの、無ければいちばん新しいもの。
        /// </summary>
        public static string ClosestGame(string game, IList<string> games)
        {
            if (games.Count == 0) return null;
            var target = ParseGame(game ?? "");
            var older = games.Where(g => ParseGame(g) <= target).ToList();
            return older.Count > 0 ? older[older.Count - 1] : games[games.Count - 1];
        }

        private static System.Version ParseGame(string game)
        {
            System.Version version;
            return System.Version.TryParse(game, out version) ? version : new System.Version(0, 0);
        }

        public static void Install(LevelWriter.BeatSaberInstall install, string dllPath)
        {
            Directory.CreateDirectory(install.PluginsPath);
            File.Copy(dllPath, Path.Combine(install.PluginsPath, DllName), true);

            string libs = Path.Combine(install.Root, "Libs");
            foreach (string name in ObsoleteLibs)
            {
                string old = Path.Combine(libs, name);
                if (File.Exists(old)) File.Delete(old);
            }
        }

        private static string AssetName(string game)
        {
            return "JumpDrillMod-v" + Version + "-bs" + game + ".zip";
        }

        /// <summary>リリースに置いてある MOD のゲームの版。</summary>
        private static List<string> SupportedGames(Dictionary<string, string> assets)
        {
            var pattern = new Regex("^JumpDrillMod-v" + Regex.Escape(Version) + @"-bs(.+)\.zip$", RegexOptions.IgnoreCase);
            return assets.Keys
                .Select(name => pattern.Match(name))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)
                .OrderBy(v => v)
                .ToList();
        }

        private static async Task<Dictionary<string, string>> ReleaseAssetsAsync()
        {
            if (_assets != null) return _assets;

            using (var client = CreateClient(TimeSpan.FromSeconds(15)))
            using (var response = await client.GetAsync(ReleaseByTagUrl + Version))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new InvalidOperationException(Lang.T("MOD をダウンロードできませんでした。", "Could not download the MOD."));
                response.EnsureSuccessStatusCode();

                var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
                        assets[asset.GetProperty("name").GetString()] = asset.GetProperty("browser_download_url").GetString();
                }
                _assets = assets;
                return assets;
            }
        }

        private static HttpClient CreateClient(TimeSpan timeout)
        {
            var client = new HttpClient { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("JumpDrill-Gui");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        private static string DllVersion(string path)
        {
            if (!File.Exists(path)) return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            return string.Format("{0}.{1}.{2}", info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        }
    }

    /// <summary>
    /// 同梱の MOD を Beat Saber のフォルダへ入れる画面。
    /// 見つかったインストールを並べ、ほかの場所はフォルダを選んで足す。
    /// </summary>
    internal sealed class ModInstallForm : Form
    {
        private const string FoldersKey = "mod.folders";
        private const string CheckedKey = "mod.checked";

        private readonly SettingsStore _settings;
        private readonly ListView _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        private readonly Label _summary = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        private readonly Label _status = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        private readonly Button _add = new Button { Text = Lang.T("フォルダを追加...", "Add folder..."), AutoSize = true };
        private readonly Button _install = new Button { Text = Lang.T("インストール", "Install"), AutoSize = true, Padding = new Padding(14, 1, 14, 1) };
        private readonly Button _close = new Button { Text = Lang.T("閉じる", "Close"), AutoSize = true };

        public ModInstallForm(SettingsStore settings)
        {
            _settings = settings;

            Text = Lang.T("MOD のインストール", "Install MOD");
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Yu Gothic UI", 9f);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(720, 360);
            MinimumSize = new Size(520, 280);

            _summary.Text = "JumpDrillMod v" + ModPackage.Version + Environment.NewLine
                          + Lang.T("チェックしたフォルダに MOD をインストールします。", "Installs the MOD into the checked folders.");

            _list.Columns.Add(Lang.T("場所", "Location"));
            _list.Columns.Add(Lang.T("ゲーム", "Game"));
            _list.Columns.Add(Lang.T("入っている MOD", "Installed MOD"));
            _list.Columns.Add(Lang.T("フォルダ", "Folder"));

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 8, 0, 0),
            };
            buttons.Controls.Add(_close);
            buttons.Controls.Add(_install);
            buttons.Controls.Add(_add);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_summary, 0, 0);
            root.Controls.Add(_list, 0, 1);
            root.Controls.Add(_status, 0, 2);
            root.Controls.Add(buttons, 0, 3);
            Controls.Add(root);

            CancelButton = _close;
            _add.Click += (s, e) => AddFolder();
            _install.Click += async (s, e) => await InstallCheckedAsync();
            _close.Click += (s, e) => Close();
            FormClosing += (s, e) => SaveChoices();

            Theme.Apply(this);
            LoadInstalls();
        }

        /// <summary>自動で見つかったものと、前に追加したフォルダを並べる。</summary>
        private void LoadInstalls()
        {
            var installs = LevelWriter.FindInstalls();
            foreach (string folder in SavedList(FoldersKey))
            {
                var extra = LevelWriter.InstallAt(folder, Lang.T("追加したフォルダ", "Added folder"));
                if (extra != null && !installs.Any(i => SamePath(i.Root, extra.Root)))
                    installs.Add(extra);
            }

            var checkedRoots = SavedList(CheckedKey);
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var install in installs)
            {
                var item = AddItem(install);
                // 初めて開いたときは、もう MOD が入っているもの（更新する先）だけチェックしておく。
                item.Checked = checkedRoots.Count > 0
                    ? checkedRoots.Any(r => SamePath(r, install.Root))
                    : ModPackage.InstalledVersion(install) != null;
            }
            _list.EndUpdate();
            FitColumns();

            _status.Text = installs.Count == 0
                ? Lang.T("Beat Saber が見つかりませんでした。［フォルダを追加］で選んでください。", "Beat Saber was not found. Choose it with [Add folder].")
                : "";
        }

        /// <summary>
        /// 見出しと中身の長い方に合わせる。切れるとバージョンやパスが読めない。
        /// 見出しと選んだ行はテーマ側で余白を付けて描くので、そのぶん少し広げる。
        /// </summary>
        private void FitColumns()
        {
            int slack = (int)Math.Round(12 * DeviceDpi / 96.0);
            foreach (ColumnHeader column in _list.Columns)
            {
                _list.AutoResizeColumn(column.Index, ColumnHeaderAutoResizeStyle.HeaderSize);
                int header = column.Width;
                if (_list.Items.Count > 0)
                {
                    _list.AutoResizeColumn(column.Index, ColumnHeaderAutoResizeStyle.ColumnContent);
                    header = Math.Max(header, column.Width);
                }
                column.Width = header + slack;
            }

            // 列が全部見える幅まで窓を広げる（画面より広くはしない）。狭めはしない
            int columns = _list.Columns.Cast<ColumnHeader>().Sum(c => c.Width);
            int want = columns + SystemInformation.VerticalScrollBarWidth + _list.Margin.Horizontal + Padding.Horizontal
                       + (int)Math.Round(36 * DeviceDpi / 96.0);
            int limit = Screen.FromControl(this).WorkingArea.Width - (Width - ClientSize.Width);
            if (want > ClientSize.Width) ClientSize = new Size(Math.Min(want, limit), ClientSize.Height);
        }

        private ListViewItem AddItem(LevelWriter.BeatSaberInstall install)
        {
            string installed = ModPackage.InstalledVersion(install);
            var item = new ListViewItem(new[]
            {
                install.Name,
                install.GameVersion ?? Lang.T("不明", "Unknown"),
                installed == null ? Lang.T("なし", "None") : "v" + installed + (ModPackage.IsOutdated(install) ? Lang.T("（古い）", " (outdated)") : ""),
                install.Root,
            })
            { Tag = install };
            _list.Items.Add(item);
            return item;
        }

        private void AddFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = Lang.T("Beat Saber のフォルダ（Beat Saber.exe のある場所）を選んでください。", "Choose the Beat Saber folder (where Beat Saber.exe is)."), UseDescriptionForTitle = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var install = LevelWriter.InstallAt(dialog.SelectedPath, Lang.T("追加したフォルダ", "Added folder"));
                if (install == null)
                {
                    MessageBox.Show(this, Lang.T("Beat Saber のフォルダではありません。", "This is not a Beat Saber folder.") + Environment.NewLine + Lang.T("Beat Saber.exe のあるフォルダを選んでください。", "Choose the folder that contains Beat Saber.exe."),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                foreach (ListViewItem existing in _list.Items)
                {
                    if (SamePath(((LevelWriter.BeatSaberInstall)existing.Tag).Root, install.Root))
                    {
                        existing.Checked = true;
                        existing.Selected = true;
                        return;
                    }
                }

                AddItem(install).Checked = true;
                FitColumns();
                var folders = SavedList(FoldersKey);
                folders.Add(install.Root);
                _settings.Set(FoldersKey, string.Join("|", folders));
                _status.Text = "";
            }
        }

        private async Task InstallCheckedAsync()
        {
            var targets = _list.CheckedItems.Cast<ListViewItem>().Select(i => (LevelWriter.BeatSaberInstall)i.Tag).ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, Lang.T("インストール先をチェックしてください。", "Check where to install."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var done = new List<string>();
            var failed = new List<string>();
            _install.Enabled = false;
            _add.Enabled = false;
            try
            {
                _status.Text = Lang.T("MOD を確認しています...", "Checking the MOD...");
                var games = await ModPackage.AvailableGamesAsync();

                foreach (var target in targets)
                {
                    if (ModPackage.IsGameRunning(target))
                    {
                        failed.Add(target.Name + Lang.T(": ゲームを閉じてください", ": close the game first"));
                        continue;
                    }

                    // 合う MOD が無い（またはゲームのバージョンが分からない）ときは、どれを入れるか選んでもらう
                    string game = target.GameVersion;
                    if (game == null || !games.Contains(game, StringComparer.OrdinalIgnoreCase))
                    {
                        if (games.Count == 0)
                        {
                            failed.Add(target.Name + Lang.T(": インストールできる MOD がありません", ": no MOD is available"));
                            continue;
                        }

                        game = ChooseGame(target, games);
                        if (game == null)
                        {
                            failed.Add(target.Name + Lang.T(": インストールしませんでした", ": skipped"));
                            continue;
                        }
                    }

                    try
                    {
                        _status.Text = Lang.T("インストール中...", "Installing...");
                        string dll = await ModPackage.PrepareAsync(game);
                        ModPackage.Install(target, dll);
                        done.Add(target.Name);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                               || ex is InvalidOperationException || ex is HttpRequestException
                                               || ex is TaskCanceledException || ex is JsonException)
                    {
                        failed.Add(target.Name + ": " + ex.Message);
                    }
                }
            }
            finally
            {
                _install.Enabled = true;
                _add.Enabled = true;
            }

            SaveChoices();
            LoadInstalls();

            var message = new StringBuilder();
            if (done.Count > 0)
                message.AppendLine(Lang.T("インストールしました: ", "Installed: ") + string.Join(Lang.T("、", ", "), done));
            if (failed.Count > 0)
            {
                message.AppendLine(Lang.T("インストールできませんでした:", "Could not install:"));
                foreach (string line in failed) message.AppendLine("  " + line);
            }
            _status.Text = done.Count > 0 ? Lang.T("インストールしました: ", "Installed: ") + string.Join(Lang.T("、", ", "), done) : "";
            MessageBox.Show(this, message.ToString().TrimEnd(), Text, MessageBoxButtons.OK,
                failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        /// <summary>
        /// 合う MOD が無い Beat Saber に、どのバージョン用の MOD を入れるかを選ぶ。入れないなら null。
        /// </summary>
        private string ChooseGame(LevelWriter.BeatSaberInstall target, List<string> games)
        {
            using (var dialog = new Form
            {
                Text = Text,
                Font = Font,
                AutoScaleMode = AutoScaleMode.Font,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            })
            {
                // 名前にバージョンが入っていれば（BSManager 1.42.3 など）重ねて書かない
                string where = target.GameVersion != null && target.Name.Contains(target.GameVersion)
                    ? target.Name
                    : target.Name + Lang.T("（Beat Saber " + (target.GameVersion ?? "バージョン不明") + "）",
                                           " (Beat Saber " + (target.GameVersion ?? "unknown version") + ")");
                var message = new Label
                {
                    AutoSize = true,
                    MaximumSize = new Size(560, 0),
                    Margin = new Padding(0, 0, 0, 10),
                    Text = Lang.T(
                        where + " に合う MOD はありません。" + Environment.NewLine +
                        "どのバージョン用の MOD をインストールしますか？" + Environment.NewLine +
                        "バージョンが合わない MOD は、正しく動かないことがあります。",
                        "There is no MOD for " + where + "." + Environment.NewLine +
                        "Which version's MOD do you want to install?" + Environment.NewLine +
                        "A MOD for another version may not work correctly."),
                };

                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Margin = new Padding(0, 0, 0, 10) };
                foreach (string game in games) combo.Items.Add(Lang.T("Beat Saber " + game + " 用", "For Beat Saber " + game));
                combo.SelectedIndex = Math.Max(0, games.IndexOf(ModPackage.ClosestGame(target.GameVersion, games)));

                var ok = new Button { Text = Lang.T("インストール", "Install"), AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(14, 1, 14, 1) };
                var cancel = new Button { Text = Lang.T("インストールしない", "Skip"), AutoSize = true, DialogResult = DialogResult.Cancel };
                var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0) };
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);

                var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
                layout.Controls.Add(message, 0, 0);
                layout.Controls.Add(combo, 0, 1);
                layout.Controls.Add(buttons, 0, 2);
                dialog.Controls.Add(layout);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;

                Theme.Apply(dialog);
                return dialog.ShowDialog(this) == DialogResult.OK ? games[combo.SelectedIndex] : null;
            }
        }

        private void SaveChoices()
        {
            var roots = _list.CheckedItems.Cast<ListViewItem>().Select(i => ((LevelWriter.BeatSaberInstall)i.Tag).Root);
            _settings.Set(CheckedKey, string.Join("|", roots));
            _settings.Save();
        }

        private List<string> SavedList(string key)
        {
            return _settings.GetString(key, "")
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        private static bool SamePath(string a, string b)
        {
            return string.Equals(a?.TrimEnd('\\'), b?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
