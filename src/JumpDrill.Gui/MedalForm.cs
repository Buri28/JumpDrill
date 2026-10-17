using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Replays;

namespace JumpDrill.Gui
{
    /// <summary>
    /// 一括生成のドリルのメダルと Lv。1行が1方向、1マスが1本（速さの Stage）。
    ///
    /// 上のプルダウンでステージを切り替える。「標準」は一括生成の 14方向 × 8 Stage で、
    /// ほかは作ったドリルを自分で並べたカスタムステージ（<see cref="CustomStageBook"/>、stages.json）。
    /// カスタムステージでは表の「+」でドリルを足し、右クリックで変更・削除・並べ替えをする。
    ///
    /// 記録は成績（ローカルリーダーボード）と同じ読み方で、ID で引く。
    /// 同じ設定を画面から作って叩いた記録もここに入る。
    ///
    /// メダルは<b>色の付いた円</b>で描く。WinForms（GDI）は絵文字をカラーで描けず、
    /// 🥇🥈🥉 が白黒の輪郭になって見分けが付かない。
    /// </summary>
    public sealed class MedalForm : Form
    {
        private readonly string _workspaceRoot;
        private readonly MedalBoard _board = new MedalBoard { Dock = DockStyle.Fill };
        private readonly Label _total = new Label { AutoSize = true, Font = new Font("Yu Gothic UI", 11f, FontStyle.Bold), Margin = new Padding(0) };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        private readonly Button _rescan = new Button { Text = Lang.T("再スキャン", "Rescan"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 1, 6, 1) };
        private readonly Button _preview = new Button { Text = Lang.T("プレビュー", "Preview"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 1, 6, 1), Enabled = false };
        private readonly Button _generate = new Button { Text = Lang.T("生成", "Generate"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 1, 6, 1), Enabled = false };
        private readonly Button _generateAll = new Button { Text = Lang.T("すべて生成", "Generate all"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 1, 6, 1) };

        /// <summary>
        /// 既にある譜面を書き直すか。開くたびに外す（全部の上書きは時間がかかるので、うっかり押さないように）。
        /// </summary>
        private readonly CheckBox _overwrite = new CheckBox { Text = Lang.T("上書き", "Overwrite"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 3, 0, 3) };
        private readonly ToolTip _tips = new ToolTip();

        /// <summary>この画面から生成している最中か。</summary>
        private bool _generating;

        /// <summary>ドリルを出力先へ書く（メイン画面の一括生成と同じもの）。書いたら true。</summary>
        private readonly Func<IWin32Window, IReadOnlyList<DrillSetEntry>, bool, Action<string>, Task<bool>> _generateSet;
        private readonly Button _replay = new Button { Text = Lang.T("スコア", "Scores"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 1, 6, 1), Enabled = false };
        private TableLayoutPanel _top;

        /// <summary>曲名を渡して ArcViewer で開く。</summary>
        private readonly Action<IWin32Window, string> _openPreview;

        /// <summary>譜面の ID を渡して、その譜面を選んだ状態でスコア画面を開く（開いていれば前に出す）。</summary>
        private readonly Action<string> _openReplay;

        /// <summary>スコア画面が開いていれば、その譜面に切り替える。前には出さない。</summary>
        private readonly Action<string> _followReplay;

        /// <summary>記録を読む Beat Saber。</summary>
        private readonly InstallPicker _install;

        /// <summary>メダルを再現精度 % で決めるか、精度 % で決めるか。</summary>
        private readonly MetricPicker _metric;

        /// <summary>読んでいる間に見る版が変わった。読み終えたら読み直す。</summary>
        private bool _reloadPending;

        /// <summary>見るステージ。先頭が標準、続いてカスタムステージ。</summary>
        private readonly ComboBox _stagePicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 4, 3), Width = 180 };
        private readonly Button _stageMenuButton = new Button { Text = Lang.T("ステージ ▾", "Stage ▾"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 1, 6, 1), Margin = new Padding(0, 3, 16, 3) };
        private readonly ContextMenuStrip _stageMenu = new ContextMenuStrip();
        private ToolStripMenuItem _newStage;
        private ToolStripMenuItem _renameStage;
        private ToolStripMenuItem _deleteStage;

        private readonly SettingsStore _settings;
        /// <summary>
        /// 見ている Beat Saber の stages.json。「すべてのバージョン」で Beat Saber が複数あるときは決まらず null
        /// （カスタムステージは出さず、組み替えもさせない）。
        /// </summary>
        private string _stagesPath;
        private CustomStageBook _book = new CustomStageBook();

        /// <summary>stages.json が読めなかった。上書きして中身を失わないよう、編集させない。</summary>
        private bool _bookBroken;

        /// <summary>作ったドリルの一覧（割り当てのダイアログに出す）。</summary>
        private readonly Func<List<DrillLibrary.DrillLevel>> _findDrills;

        /// <summary>
        /// 最後に読んだ記録の、ID ごとのベスト（再現精度 % と精度 %）。
        /// ステージや基準を切り替えても読み直さずに済むよう、両方持っておく。
        /// </summary>
        private Dictionary<string, double> _bestRepro = new Dictionary<string, double>();
        private Dictionary<string, double> _bestAcc = new Dictionary<string, double>();

        /// <summary>いまの基準でのベスト。</summary>
        private Dictionary<string, double> _best
        {
            get { return MetricPicker.Current == RankMetric.Accuracy ? _bestAcc : _bestRepro; }
        }
        private int _failed;

        /// <summary>記録を1度でも読み終えたか。読む前は状態の文にメダル数を出さない。</summary>
        private bool _loaded;

        private static string StandardName { get { return Lang.T("標準", "Standard"); } }
        private static string OtherName { get { return Lang.T("その他", "Other"); } }
        private static string AllName { get { return Lang.T("すべて", "All"); } }

        /// <summary>選んでいるカスタムステージ。標準・その他・すべてなら null。</summary>
        private CustomStage CurrentStage
        {
            get { return _stagePicker.SelectedIndex > 2 ? _book.Find((string)_stagePicker.SelectedItem) : null; }
        }

        /// <summary>
        /// 作ったドリルを方向ごとに並べた表（その他・すべて）を出しているか。
        /// その他はどのカスタムステージにも入っていないものだけ、すべてはカスタムステージに入っているものも出す。
        /// </summary>
        private bool IsOther { get { return _stagePicker.SelectedIndex == 1 || _stagePicker.SelectedIndex == 2; } }

        /// <summary>標準（112曲）を出しているか。</summary>
        private bool IsStandard { get { return _stagePicker.SelectedIndex == 0; } }

        /// <summary>すべて（カスタムステージに入っているものも含めて、標準以外のドリル全部）を出しているか。</summary>
        private bool IsAll { get { return _stagePicker.SelectedIndex == 2; } }

        /// <summary>その他の表に出すドリルの曲名。再スキャンで探し直す。</summary>
        private List<string> _otherNames;

        /// <summary>作ったばかりのドリルの曲名（新しい順）。<see cref="_otherNames"/> と一緒に探し直す。</summary>
        private List<string> _justMade = new List<string>();

        /// <summary>次に表を出すとき、いちばん新しく作ったドリルを選んで見せるか（開いたときと、ステージを切り替えたとき）。</summary>
        private bool _revealNewest = true;

        /// <summary>いま出している表の行。すべて再作成で使う。</summary>
        private List<MedalRow> _shownRows = new List<MedalRow>();

        /// <summary>再作成で書くときの指定（メイン画面の「zip も出す」など）。無ければ既定。</summary>
        internal Func<LevelWriteOptions> WriteOptions { get; set; }

        internal MedalForm(string workspaceRoot, SettingsStore settings, Action<IWin32Window, string> openPreview, Action<string> openReplay, Action<string> followReplay,
            Func<IWin32Window, IReadOnlyList<DrillSetEntry>, bool, Action<string>, Task<bool>> generateSet,
            Func<List<DrillLibrary.DrillLevel>> findDrills)
        {
            _settings = settings;
            _findDrills = findDrills;
            _generateSet = generateSet;
            _workspaceRoot = workspaceRoot;
            _install = new InstallPicker(settings) { Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 8, 3) };
            _metric = new MetricPicker(settings) { Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 8, 3) };
            _openPreview = openPreview;
            _openReplay = openReplay;
            _followReplay = followReplay;

            Text = Lang.T("JumpDrill — メダル", "JumpDrill — Medals");
            Icon = AppIcon.Value;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Yu Gothic UI", 9f);
            MinimumSize = new Size(560, 360);
            Size = new Size(820, 680);
            StartPosition = FormStartPosition.Manual;

            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, AutoSize = true, Padding = new Padding(8, 8, 8, 4) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var stageBox = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Left };
            stageBox.Controls.Add(_stagePicker);
            stageBox.Controls.Add(_stageMenuButton);
            top.Controls.Add(stageBox, 0, 0);
            top.Controls.Add(_total, 1, 0);
            // 総合 Lv とボタンがくっつかないよう、左に間を取る（窓の幅はこの段にも合わせるので、詰まったときもこの間は残る）
            _replay.Margin = new Padding(16, _replay.Margin.Top, _replay.Margin.Right, _replay.Margin.Bottom);
            top.Controls.Add(_replay, 3, 0);
            top.Controls.Add(_preview, 4, 0);
            top.Controls.Add(_rescan, 5, 0);
            // 状態の文は2段目に出す。1段目はステージの欄とボタンで埋まり、並べると切れる。
            // 生成のボタンは2段目の右。生成の進み具合はこの状態の文に出るので、並べて置く
            var viewBox = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Left };
            viewBox.Controls.Add(_install);
            viewBox.Controls.Add(new Label { Text = Lang.T("基準", "Rank by"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 4, 3) });
            viewBox.Controls.Add(_metric);
            top.Controls.Add(viewBox, 0, 1);
            top.Controls.Add(_status, 1, 1);
            top.SetColumnSpan(_status, 2);
            top.Controls.Add(_generate, 3, 1);
            top.Controls.Add(_generateAll, 4, 1);
            top.Controls.Add(_overwrite, 5, 1);

            // 1段目と2段目のボタンは列ごとに左右の端を揃える（幅は列の広い方に合わせて伸ばす）。
            // 上書きのチェックは、箱の左端を上の再スキャンの左端に揃える
            foreach (var button in new[] { _replay, _preview, _rescan, _generate, _generateAll })
                button.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _generate.Margin = new Padding(_replay.Margin.Left, _generate.Margin.Top, _replay.Margin.Right, _generate.Margin.Bottom);
            _overwrite.Margin = new Padding(_rescan.Margin.Left, 3, 0, 3);

            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            // 列は窓の幅に合わせる。中身の幅で広げると、上の段が広いときに表ごと窓の外へはみ出す
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.Controls.Add(top, 0, 0);
            outer.Controls.Add(_board, 0, 1);
            Controls.Add(outer);
            _top = top;

            Theme.Apply(this);
            BuildStageMenu();

            _rescan.Click += async (s, e) => await ScanAsync();
            Shown += async (s, e) => await ScanAsync();

            // 見る版を変えたら読み直す（スコア画面で変えたときも）
            // 読んでいる最中なら、読み終えてからもう一度読む
            Action reload = async () =>
            {
                if (IsDisposed) return;
                if (!_rescan.Enabled) { _reloadPending = true; return; }
                LoadStages();
                await ScanAsync();
            };
            InstallPicker.Changed += reload;

            // 基準を変えたら、読んである記録で出し直す（スコア画面で変えたときも）
            Action remetric = () =>
            {
                if (IsDisposed) return;
                ShowStage();
                UpdateButtons();
            };
            MetricPicker.Changed += remetric;
            FormClosed += (s, e) =>
            {
                InstallPicker.Changed -= reload;
                MetricPicker.Changed -= remetric;
            };
            Load += (s, e) => { _install.FitWidth(DeviceDpi); _metric.FitWidth(DeviceDpi); };

            // マスを選んでボタンを押す。ダブルクリックはスコア画面。スコアはプレイしたマスだけ。
            // スコア画面を開いたままなら、マスを選ぶだけでそちらも切り替わる。
            _board.SelectionChanged += (s, e) =>
            {
                UpdateButtons();
                var cell = _board.Selected;
                if (_replay.Enabled && _followReplay != null) _followReplay(cell.Id);
            };
            _preview.Click += (s, e) => OpenPreview();
            _generate.Click += async (s, e) =>
            {
                var cell = _board.Selected;
                if (cell == null) return;
                if (IsStandard) { if (cell.Entry != null) await Generate(new[] { cell.Entry }); }
                else await Rebuild(new[] { cell.SongName });
            };
            _generateAll.Click += async (s, e) =>
            {
                if (IsStandard) await Generate(DrillSet.All());
                else await Rebuild(_shownRows.SelectMany(r => r.Cells).Select(c => c.SongName).ToList());
            };
            _tips.SetToolTip(_preview, Lang.T("選んだマスの譜面を ArcViewer で開きます。", "Opens the selected map in ArcViewer."));
            _tips.SetToolTip(_generate, Lang.T("選んだマスの譜面を出力先に作成します。", "Writes the selected map to the output folder."));
            _tips.SetToolTip(_generateAll, Lang.T("14方向 × 8 Stage の譜面（112曲）を出力先に作成します。", "Writes 112 maps (14 directions × 8 stages) to the output folder."));
            _tips.SetToolTip(_overwrite, Lang.T(
                "オフ: 既にある譜面は書かない\r\nオン: 書き直す。記録は消えない",
                "Off: maps that already exist are left as is\r\nOn: they are written again. Records are kept"));
            _replay.Click += (s, e) => OpenReplay();
            _board.CellDoubleClicked += (s, e) => OpenReplay();
            _board.AddClicked += (s, hit) => AddDrills(hit.Row);
            _board.MenuRequested += (s, hit) => ShowBoardMenu(hit);
            _board.RowMoved += (s, move) => MoveRow(move.Item1, move.Item2);

            // 表の大きさは方向の数と画面の拡大率で決まるので、決め打ちせず中身に合わせる。
            Load += (s, e) =>
            {
                FitToContent();
                // 開いた時点では表の大きさが決まっていないので、作ったばかりのドリルへのスクロールをやり直す
                if (IsOther && _justMade.Count > 0 && _board.Selected?.SongName == _justMade[0]) _board.Reveal(_justMade[0]);
            };

            LoadStages();
            _stagePicker.SelectedIndexChanged += (s, e) =>
            {
                if (_stagePicker.SelectedIndex < 0) return;
                _settings.Set("medalStage", CurrentStage?.Name ?? "");
                _settings.Set("medalOther", IsOther && !IsAll);
                _settings.Set("medalAll", IsAll);
                if (IsOther) _otherNames = null;   // メイン画面で作った分なども拾い直す
                _board.ClearSelection();
                _revealNewest = true;
                ShowStage();
                if (_board.Selected == null) _board.AutoScrollPosition = Point.Empty;
            };

            // 叩く前でも空の表は見せる。どこを埋めればいいかがそのまま次の練習になる。
            ShowStage();
        }

        /// <summary>stages.json を読んでプルダウンを作る。前に見ていたステージを選び直す。</summary>
        private void LoadStages()
        {
            // MOD と同じファイル（Beat Saber ごとの UserData\JumpDrill\stages.json）。どちらで作ったステージも両方に出る
            _stagesPath = StagesPathFor(InstallPicker.Selected);
            _bookBroken = false;
            try
            {
                if (_stagesPath == null)
                {
                    _book = new CustomStageBook();
                    _bookBroken = true;
                    _status.Text = Lang.T("カスタムステージを使うには、Beat Saber のバージョンを選んでください。",
                        "Choose a Beat Saber version to use custom stages.");
                }
                else
                {
                    CustomStageBook.MoveLegacy(_stagesPath);
                    _book = CustomStageBook.Load(_stagesPath);
                }
            }
            catch (Exception ex)
            {
                _book = new CustomStageBook();
                _bookBroken = true;
                _status.Text = Lang.T("ステージの設定ファイルを読み込めませんでした: ", "Could not read the stage file: ") + ex.Message;
            }

            FillStagePicker(_settings.GetString("medalStage", ""), _settings.GetBool("medalOther", false), _settings.GetBool("medalAll", false));
        }

        /// <summary>選んでいる Beat Saber の stages.json。「すべて」なら、Beat Saber が1つだけのときはそれ。</summary>
        private static string StagesPathFor(string install)
        {
            if (string.IsNullOrEmpty(install))
            {
                var installs = LevelWriter.FindInstalls();
                if (installs.Count != 1) return null;
                install = installs[0].Root;
            }
            return CustomStageBook.PathFor(install);
        }

        /// <summary>
        /// 選んでいる Beat Saber のフォルダの中にあるドリル。「すべてのバージョン」なら全部。
        /// その他・ドリルの選択・ドリルの削除で使う（カスタムステージもメダルも版ごとなので、譜面も版で絞る）。
        /// </summary>
        private List<DrillLibrary.DrillLevel> FindDrills()
        {
            var all = _findDrills();
            string install = InstallPicker.Selected;
            if (string.IsNullOrEmpty(install)) return all;

            string root = Path.GetFullPath(install).TrimEnd('\\', '/');
            return all.Where(d =>
            {
                string owner = CustomStageBook.InstallRootOf(d.Path);
                return owner != null && string.Equals(Path.GetFullPath(owner).TrimEnd('\\', '/'), root, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        /// <summary>プルダウンは 標準・その他・すべて・カスタムステージの順。</summary>
        private void FillStagePicker(string select, bool other = false, bool all = false)
        {
            _stagePicker.BeginUpdate();
            _stagePicker.Items.Clear();
            _stagePicker.Items.Add(StandardName);
            _stagePicker.Items.Add(OtherName);
            _stagePicker.Items.Add(AllName);
            foreach (var stage in _book.Stages) _stagePicker.Items.Add(stage.Name);
            _stagePicker.EndUpdate();

            int custom = _book.Stages.FindIndex(st => st.Name == select);
            _stagePicker.SelectedIndex = all ? 2 : other ? 1 : custom >= 0 ? custom + 3 : 0;
        }

        /// <summary>
        /// その他の表に出すドリルの曲名。作ったドリルを置き場所から集めたもの。
        /// 探すのに少し時間がかかるので、その他に切り替えたときと再スキャンのときだけ探し直す。
        /// </summary>
        private List<string> OtherNames()
        {
            if (_otherNames == null)
            {
                try
                {
                    var drills = _findDrills == null ? new List<DrillLibrary.DrillLevel>() : FindDrills();
                    _otherNames = drills.Select(d => d.SongName).ToList();
                    _justMade = DrillLibrary.JustMade(
                        drills.Select(d => new KeyValuePair<string, DateTime>(d.SongName, d.Written)), DateTime.Now);
                }
                catch (Exception ex)
                {
                    _otherNames = new List<string>();
                    _justMade = new List<string>();
                    _status.Text = Lang.T("ドリルを探せませんでした: ", "Could not list the drills: ") + ex.Message;
                }
            }
            return _otherNames;
        }

        /// <summary>ステージの作成・名前の変更・削除。</summary>
        private void BuildStageMenu()
        {
            Theme.Apply(_stageMenu);
            _newStage = new ToolStripMenuItem(Lang.T("新しいステージ...", "New stage..."), null, (s, e) => NewStage());
            _renameStage = new ToolStripMenuItem(Lang.T("名前の変更...", "Rename..."), null, (s, e) => RenameStage());
            _deleteStage = new ToolStripMenuItem(Lang.T("削除", "Delete"), null, (s, e) => DeleteStage());
            _stageMenu.Items.Add(_newStage);
            _stageMenu.Items.Add(_renameStage);
            _stageMenu.Items.Add(_deleteStage);
            _stageMenu.Items.Add(new ToolStripSeparator());
            _stageMenu.Items.Add(Lang.T("設定ファイルの場所を開く", "Open the stage file location"), null, (s, e) => OpenStagesFolder());

            _stageMenu.Opening += (s, e) =>
            {
                _newStage.Enabled = !_bookBroken;
                _renameStage.Enabled = !_bookBroken && CurrentStage != null;
                _deleteStage.Enabled = !_bookBroken && CurrentStage != null;
            };
            _stageMenuButton.Click += (s, e) => _stageMenu.Show(_stageMenuButton, new Point(0, _stageMenuButton.Height));
        }

        /// <summary>ステージ名に使えなければ理由を返す。</summary>
        private string CheckStageName(string name, CustomStage self)
        {
            // 表示の言語によらず、標準・その他・すべての名前は使わせない（MOD は英語の名前でプルダウンを選ぶ）
            var reserved = new[] { "標準", "Standard", "その他", "Other", "すべて", "All" };
            if (reserved.Contains(name) || _book.Stages.Any(st => st != self && st.Name == name))
                return Lang.T("同じ名前のステージがあります。", "A stage with this name already exists.");
            return null;
        }

        private void NewStage()
        {
            string name = StageDialogs.AskName(this, Lang.T("新しいステージ", "New stage"), "", n => CheckStageName(n, null));
            if (name == null) return;

            _book.Stages.Add(new CustomStage { Name = name });
            if (SaveStages()) FillStagePicker(name);
        }

        private void RenameStage()
        {
            var stage = CurrentStage;
            if (stage == null) return;

            string name = StageDialogs.AskName(this, Lang.T("ステージ名の変更", "Rename stage"), stage.Name, n => CheckStageName(n, stage));
            if (name == null || name == stage.Name) return;

            stage.Name = name;
            if (SaveStages()) FillStagePicker(name);
        }

        private void DeleteStage()
        {
            var stage = CurrentStage;
            if (stage == null) return;

            if (MessageBox.Show(this,
                    string.Format(CultureInfo.CurrentCulture, Lang.T("ステージ「{0}」を削除しますか？\r\n譜面と記録は削除されません。", "Delete the stage \"{0}\"?\r\nMaps and records are kept."), stage.Name),
                    "JumpDrill", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            _book.Stages.Remove(stage);
            if (SaveStages()) FillStagePicker("");
        }

        private void OpenStagesFolder()
        {
            try
            {
                if (_stagesPath == null) return;
                string dir = Path.GetDirectoryName(_stagesPath);
                Directory.CreateDirectory(dir);
                string args = File.Exists(_stagesPath) ? "/select,\"" + _stagesPath + "\"" : "\"" + dir + "\"";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>stages.json に書く。書けなければ知らせて false。</summary>
        private bool SaveStages()
        {
            try
            {
                _book.Save(_stagesPath);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Lang.T("ステージを保存できませんでした。", "Could not save the stage.") + Environment.NewLine + ex.Message,
                    "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// 行の方向。1行には同じ方向のドリルだけを並べるので、最初のドリルで決まる。
        /// 空の行なら null（どの方向でも入れられる）。
        /// </summary>
        private static string DirectionOf(CustomStageRow row)
        {
            return CustomStage.DirectionOf(row);
        }

        /// <summary>
        /// 行にドリルを足す。既にドリルのある行には、その方向のドリルだけを選ばせる。
        /// <paramref name="row"/> が行の数と同じなら新しい行に足す。同じ方向の行に空きがあればまずそこへ入れ、
        /// 残りで行を作る。違う方向を混ぜて選んだら、方向ごとに行を分ける。行の名前は方向（<c>R8bL5a</c>）、読めなければ連番。
        /// 既にある行へは BPM の順の位置に入る。ステージに既にあるドリルは足さない（<see cref="CustomStage.AddDrills"/>）。
        /// </summary>
        private void AddDrills(int row)
        {
            var stage = CurrentStage;
            if (stage == null || _bookBroken || _findDrills == null) return;

            bool newRow = row >= stage.Rows.Count;
            int room = newRow ? CustomStage.MaxDrillsPerRow : CustomStage.MaxDrillsPerRow - stage.Rows[row].Drills.Count;
            var picked = StageDialogs.PickDrills(this, FindDrills(), multiple: true, direction: newRow ? null : DirectionOf(stage.Rows[row]),
                limit: Math.Max(1, room));
            if (picked == null) return;

            // 表は 14行 × 8 Stage まで。入りきらない分は足さずに知らせる
            int skipped = stage.AddDrills(row, picked);

            Edited();
            if (skipped > 0)
                _status.Text = string.Format(CultureInfo.CurrentCulture,
                    Lang.T("表は {0} 行 × {1} Stage までです。{2} 本は追加されませんでした。", "A stage holds up to {0} rows × {1} stages. {2} drills were not added."),
                    CustomStage.MaxRows, CustomStage.MaxDrillsPerRow, skipped);
        }

        /// <summary>
        /// 表の右クリックのメニュー。カスタムステージでは組み替えとドリルの削除、その他ではドリルの削除だけ。
        /// </summary>
        private void ShowBoardMenu(MedalBoard.Hit hit)
        {
            if (IsOther)
            {
                var cell = _board.CellOf(hit);
                if (cell == null || string.IsNullOrEmpty(cell.SongName)) return;

                var other = new ContextMenuStrip();
                Theme.Apply(other);
                other.Closed += (s, e) => BeginInvoke((Action)other.Dispose);
                other.Items.Add(Lang.T("ドリルを再作成", "Rebuild drill"), null, async (s, e) => await Rebuild(new[] { cell.SongName }));
                other.Items.Add(new ToolStripSeparator());
                other.Items.Add(Lang.T("ドリルを削除...", "Delete drill..."), null, (s, e) => DeleteDrill(cell.SongName));
                other.Show(Cursor.Position);
                return;
            }

            var stage = CurrentStage;
            if (stage == null || _bookBroken || hit.Row >= stage.Rows.Count) return;

            var row = stage.Rows[hit.Row];
            int r = hit.Row;
            var menu = new ContextMenuStrip();
            Theme.Apply(menu);
            menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);

            if (hit.Kind == MedalBoard.HitKind.Cell)
            {
                int c = hit.Column;
                AddItem(menu, Lang.T("ドリルを変更...", "Change drill..."), true, () =>
                {
                    // 行に1本しか無ければ方向ごと替えてよい。2本以上なら行の方向に揃える
                    string direction = row.Drills.Count > 1 ? DirectionOf(row) : null;
                    var picked = _findDrills == null ? null : StageDialogs.PickDrills(this, FindDrills(), multiple: false, direction: direction);
                    if (picked == null) return false;

                    // 方向ごと替えたら、方向のままの行の名前も付け替える
                    string before = DirectionOf(row);
                    row.Drills[c] = picked[0];
                    if (row.Name == before) row.Name = DirectionOf(row);
                    return true;
                });
                AddItem(menu, Lang.T("左へ移動", "Move left"), c > 0, () => { Swap(row.Drills, c, c - 1); return true; }, () => _board.Select(r, c - 1));
                AddItem(menu, Lang.T("右へ移動", "Move right"), c < row.Drills.Count - 1, () => { Swap(row.Drills, c, c + 1); return true; }, () => _board.Select(r, c + 1));
                menu.Items.Add(new ToolStripSeparator());
                AddItem(menu, Lang.T("ステージから外す", "Remove from stage"), true, () => { row.Drills.RemoveAt(c); _board.ClearSelection(); return true; });
                string songName = c < row.Drills.Count ? row.Drills[c] : null;
                menu.Items.Add(new ToolStripMenuItem(Lang.T("ドリルを再作成", "Rebuild drill"), null, async (s, e) => await Rebuild(new[] { songName }))
                {
                    Enabled = !string.IsNullOrEmpty(songName),
                });
                menu.Items.Add(new ToolStripMenuItem(Lang.T("ドリルを削除...", "Delete drill..."), null, (s, e) => DeleteDrill(songName))
                {
                    Enabled = !string.IsNullOrEmpty(songName),
                });
            }
            else
            {
                AddItem(menu, Lang.T("行の名前を変更...", "Rename row..."), true, () =>
                {
                    string name = StageDialogs.AskName(this, Lang.T("行の名前の変更", "Rename row"), row.Name, null);
                    if (name == null) return false;
                    row.Name = name;
                    return true;
                });
                menu.Items.Add(new ToolStripMenuItem(Lang.T("ドリルを追加...", "Add drills..."), null, (s, e) => AddDrills(r))
                {
                    Enabled = row.Drills.Count < CustomStage.MaxDrillsPerRow,
                });
                AddItem(menu, Lang.T("上へ移動", "Move up"), r > 0, () => { Swap(stage.Rows, r, r - 1); _board.ClearSelection(); return true; });
                AddItem(menu, Lang.T("下へ移動", "Move down"), r < stage.Rows.Count - 1, () => { Swap(stage.Rows, r, r + 1); _board.ClearSelection(); return true; });
                menu.Items.Add(new ToolStripSeparator());
                AddItem(menu, Lang.T("行を削除", "Delete row"), true, () =>
                {
                    if (row.Drills.Count > 0 && MessageBox.Show(this,
                            string.Format(CultureInfo.CurrentCulture, Lang.T("行「{0}」を削除しますか？", "Delete the row \"{0}\"?"), row.Name),
                            "JumpDrill", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return false;
                    stage.Rows.RemoveAt(r);
                    _board.ClearSelection();
                    return true;
                });
            }

            menu.Show(Cursor.Position);
        }

        /// <summary>
        /// ドリル（譜面のフォルダ）を消す。同じ曲名の譜面が置き場所ごとにあれば、全部消す。
        /// 記録（リプレイ）は残す。どのカスタムステージからも外す（消した譜面のマスを残しても叩けない）。
        /// ごみ箱には送らず、そのまま消す（リプレイの削除と同じ）。
        /// </summary>
        private void DeleteDrill(string songName)
        {
            if (string.IsNullOrEmpty(songName)) return;

            List<DrillLibrary.DrillLevel> copies;
            try
            {
                copies = (_findDrills == null ? new List<DrillLibrary.DrillLevel>() : FindDrills())
                    .Where(d => string.Equals(d.SongName, songName, StringComparison.Ordinal))
                    .ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            bool inStages = _book.Stages.Any(st => st.Rows.Any(r => r.Drills.Contains(songName)));
            if (copies.Count == 0 && !inStages)
            {
                MessageBox.Show(this, Lang.T("このドリルの譜面が見つかりませんでした。", "The map of this drill was not found."),
                    "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var message = new System.Text.StringBuilder();
            message.AppendLine(string.Format(CultureInfo.CurrentCulture,
                Lang.T("ドリル「{0}」を削除しますか？", "Delete the drill \"{0}\"?"), DrillNaming.DisplayName(songName)));
            message.AppendLine();
            if (copies.Count > 0)
            {
                message.AppendLine(Lang.T("次の譜面のフォルダを消します（元に戻せません）:", "These map folders are deleted (cannot be undone):"));
                foreach (var copy in copies) message.AppendLine("  " + copy.Path);
            }
            if (inStages) message.AppendLine(Lang.T("カスタムステージからも外します。", "It is also removed from custom stages."));
            message.Append(Lang.T("記録（リプレイ）は残ります。", "Records (replays) are kept."));

            if (MessageBox.Show(this, message.ToString(), "JumpDrill", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK) return;

            var failed = new List<string>();
            foreach (var copy in copies)
            {
                try { System.IO.Directory.Delete(copy.Path, true); }
                catch (Exception ex) { failed.Add(copy.Path + "  " + ex.Message); }
            }

            if (inStages && !_bookBroken)
            {
                foreach (var stage in _book.Stages)
                    foreach (var row in stage.Rows)
                        row.Drills.RemoveAll(d => d == songName);
                SaveStages();
            }

            _otherNames = null;
            _board.ClearSelection();
            ShowStage();

            _status.Text = failed.Count == 0
                ? string.Format(CultureInfo.CurrentCulture, Lang.T("ドリル「{0}」を削除しました。", "Deleted the drill \"{0}\"."), DrillNaming.DisplayName(songName))
                : Lang.T("消せなかった譜面があります。", "Some maps could not be deleted.");
            if (failed.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, failed), "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>カスタムステージの行を並べ替える（行の名前をドラッグ）。</summary>
        private void MoveRow(int from, int to)
        {
            var stage = CurrentStage;
            if (stage == null || _bookBroken) return;
            if (from < 0 || from >= stage.Rows.Count || to < 0 || to >= stage.Rows.Count) return;

            var row = stage.Rows[from];
            stage.Rows.RemoveAt(from);
            stage.Rows.Insert(to, row);
            _board.ClearSelection();
            Edited();
        }

        /// <summary>
        /// ほかの画面でドリルの譜面を消したとき。どのカスタムステージからも外し（<paramref name="removeFromStages"/>）、
        /// その他の一覧を探し直す。ステージはこの画面が持っている中身を直して保存する
        /// （ファイルだけ直すと、あとでこの画面が保存したときに元に戻ってしまう）。
        /// </summary>
        internal void DrillDeleted(string songName, bool removeFromStages)
        {
            if (removeFromStages && !_bookBroken)
            {
                bool changed = false;
                foreach (var stage in _book.Stages)
                    foreach (var row in stage.Rows)
                        changed |= row.Drills.RemoveAll(d => d == songName) > 0;
                if (changed) SaveStages();
            }

            _otherNames = null;
            _board.ClearSelection();
            ShowStage();
        }

        /// <summary>
        /// メニューの1項目。<paramref name="edit"/> が true を返したら保存して出し直し、
        /// 出し直した表で <paramref name="after"/>（選び直しなど）を呼ぶ。
        /// </summary>
        private void AddItem(ContextMenuStrip menu, string text, bool enabled, Func<bool> edit, Action after = null)
        {
            menu.Items.Add(new ToolStripMenuItem(text, null, (s, e) =>
            {
                if (!edit()) return;
                Edited();
                after?.Invoke();
            }) { Enabled = enabled });
        }

        private static void Swap<T>(List<T> list, int a, int b)
        {
            T t = list[a];
            list[a] = list[b];
            list[b] = t;
        }

        /// <summary>カスタムステージを変えたら、書いて表を出し直す。</summary>
        private void Edited()
        {
            SaveStages();
            ShowStage();
        }

        /// <summary>選んでいるステージを、最後に読んだ記録で出す。</summary>
        private void ShowStage()
        {
            var stage = CurrentStage;
            bool other = IsOther;
            var rows = other ? DrillSetMedals.Evaluate(CustomStage.Other(OtherNames(), IsAll ? null : _book.Stages), _best)
                : stage == null ? DrillSetMedals.Evaluate(_best)
                : DrillSetMedals.Evaluate(stage, _best);
            // NEW はその他・すべてでだけ出す（作ったばかりの一覧はそこを開いたときに作り直している）
            _board.NewNames = other ? new HashSet<string>(_justMade, StringComparer.Ordinal) : new HashSet<string>();
            ShowRows(rows, editable: stage != null && !_bookBroken);
            _board.CellMenus = other;

            // その他を開いたときは、いちばん新しく作ったドリルを選んで見せる
            if (other && _revealNewest && _board.Selected == null && _justMade.Count > 0) _board.Reveal(_justMade[0]);
            _revealNewest = false;

            // 標準は指定から生成する。その他・すべて・カスタムは作ったドリルを並べたものなので、同じ指定で作り直す
            bool standard = IsStandard;
            _generate.Text = standard ? Lang.T("生成", "Generate") : Lang.T("再作成", "Rebuild");
            _generateAll.Text = standard ? Lang.T("すべて生成", "Generate all") : Lang.T("すべて再作成", "Rebuild all");
            _tips.SetToolTip(_generate, standard
                ? Lang.T("選んだマスの譜面を出力先に作成します。", "Writes the selected map to the output folder.")
                : Lang.T("選んだマスの譜面を同じ指定で作り直します。記録は消えません。", "Writes the selected map again with the same settings. Records are kept."));
            _tips.SetToolTip(_generateAll, standard
                ? Lang.T("14方向 × 8 Stage の譜面（112曲）を出力先に作成します。", "Writes 112 maps (14 directions × 8 stages) to the output folder.")
                : Lang.T("この表の譜面をすべて同じ指定で作り直します。記録は消えません。", "Writes every map in this table again with the same settings. Records are kept."));
            _overwrite.Visible = standard;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var cell = _board.Selected;
            _preview.Enabled = cell != null;
            _generate.Enabled = cell != null && (cell.Entry != null || !IsStandard) && !_generating;
            _replay.Enabled = cell != null && cell.BestPercent.HasValue;
        }

        private async Task ScanAsync()
        {
            _rescan.Enabled = false;
            _status.Text = Lang.T("リプレイを読み込んでいます...", "Loading replays...");

            try
            {
                int failed = 0;
                string workspace = _workspaceRoot;
                string install = InstallPicker.Selected;
                _otherNames = null;
                var scores = await Task.Run(() => DrillRecords.Load(workspace, (path, ex) => failed++, install));
                _bestRepro = DrillSetMedals.BestById(scores, RankMetric.Reproducibility);
                _bestAcc = DrillSetMedals.BestById(scores, RankMetric.Accuracy);
                _failed = failed;
                _loaded = true;
                ShowStage();
            }
            catch (Exception ex)
            {
                _status.Text = Lang.T("読み込みに失敗しました: ", "Failed to load: ") + ex.Message;
            }
            finally
            {
                _rescan.Enabled = true;
            }

            if (_reloadPending && !IsDisposed)
            {
                _reloadPending = false;
                LoadStages();
                await ScanAsync();
            }
        }

        /// <summary>
        /// 表が全部入り、スクロールの出ない大きさにする。画面に入らなければ画面いっぱいまで。
        /// </summary>
        private void FitToContent()
        {
            // 寸法は画面の拡大率で変わる。窓ができてから測り直す。
            _board.Remeasure();
            PerformLayout();

            // 余白や見出しの高さを足し合わせるより、いま見えている広さとの差で合わせる方が確か。
            // 1回目はスクロールバーが出ている分だけ広く取るので、消えたあとでもう一度詰める。
            Rectangle area = Screen.FromControl(this).WorkingArea;
            var outer = Size;
            for (int pass = 0; pass < 2; pass++)
            {
                Size diff = _board.ContentSize - _board.ClientSize;
                // 上の段（ステージ・ボタン）が表より広ければ、そちらに合わせる。狭いと右のボタンが切れる
                int topExtra = _top.GetPreferredSize(Size.Empty).Width - _top.Width;
                outer = new Size(
                    Math.Min(Math.Max(Width + Math.Max(diff.Width, topExtra), MinimumSize.Width), area.Width),
                    Math.Min(Math.Max(Height + diff.Height, MinimumSize.Height), area.Height));
                Size = outer;
                PerformLayout();
            }

            // 大きさを変えたので置き直す。親の中央、はみ出すなら画面の内側へ。
            Rectangle around = Owner != null ? Owner.Bounds : area;
            int x = around.Left + (around.Width - outer.Width) / 2;
            int y = around.Top + (around.Height - outer.Height) / 2;
            Location = new Point(
                Math.Max(area.Left, Math.Min(x, area.Right - outer.Width)),
                Math.Max(area.Top, Math.Min(y, area.Bottom - outer.Height)));
        }

        /// <summary>譜面を書く。書いている間は生成のボタンを止める。</summary>
        private async Task Generate(IReadOnlyList<DrillSetEntry> entries)
        {
            if (_generateSet == null || _generating) return;

            _generating = true;
            _generate.Enabled = false;
            _generateAll.Enabled = false;
            try
            {
                await _generateSet(this, entries, _overwrite.Checked, text => { if (!IsDisposed) _status.Text = text; });
            }
            finally
            {
                _generating = false;
                if (!IsDisposed)
                {
                    UpdateButtons();
                    _generateAll.Enabled = true;
                }
            }
        }

        /// <summary>
        /// ドリルを同じ指定で作り直す（JumpDrill.Core の <c>DrillRebuild</c>）。同じ曲名の譜面が置き場所ごとにあれば全部。
        /// 生成の決まりを変えたあとで、今の決まりで書き直すのに使う。名前と ID は変わらないので記録はそのまま。
        /// </summary>
        private async Task Rebuild(IReadOnlyList<string> songNames)
        {
            if (_generating || _findDrills == null) return;

            var names = new HashSet<string>(songNames.Where(n => !string.IsNullOrEmpty(n)), StringComparer.Ordinal);
            List<DrillLibrary.DrillLevel> copies;
            try { copies = FindDrills().Where(d => names.Contains(d.SongName)).ToList(); }
            catch (Exception ex)
            {
                _status.Text = Lang.T("ドリルを探せませんでした: ", "Could not list the drills: ") + ex.Message;
                return;
            }
            if (copies.Count == 0)
            {
                _status.Text = Lang.T("作り直す譜面が見つかりません。", "No maps to rebuild.");
                return;
            }

            if (names.Count > 1 && MessageBox.Show(this,
                    string.Format(CultureInfo.CurrentCulture, Lang.T(
                        "{0} 本のドリルを同じ指定で作り直します。記録は消えません。",
                        "Rebuild {0} drills with the same settings? Records are kept."), names.Count),
                    "JumpDrill", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            _generating = true;
            _generate.Enabled = false;
            _generateAll.Enabled = false;
            var write = WriteOptions?.Invoke();   // 画面の値は UI のスレッドで読む
            // zip は曲ごとに1つでよい。同じ曲の2つ目以降のコピーでは書かない
            var noZip = write == null ? null : new LevelWriteOptions { Format = write.Format, SampleRate = write.SampleRate, OggQuality = write.OggQuality };
            var zipped = new HashSet<string>(StringComparer.Ordinal);
            var progress = new Progress<string>(text => { if (!IsDisposed) _status.Text = text; });
            IProgress<string> step = progress;
            var failures = new List<string>();
            var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < copies.Count; i++)
                    {
                        step.Report(string.Format(CultureInfo.CurrentCulture, Lang.T("再作成中... {0}/{1}  {2}", "Rebuilding... {0}/{1}  {2}"),
                            i + 1, copies.Count, DrillNaming.DisplayName(copies[i].SongName)));
                        try
                        {
                            var result = DrillRebuild.Rewrite(copies[i].Path, zipped.Add(copies[i].SongName) ? write : noZip);
                            if (result.Renamed) lock (renamed) renamed[result.OldName] = result.NewName;
                        }
                        catch (Exception ex) { failures.Add(DrillNaming.DisplayName(copies[i].SongName) + ": " + ex.Message); }
                    }
                });
            }
            finally
            {
                _generating = false;
                if (!IsDisposed)
                {
                    _generateAll.Enabled = true;
                    UpdateButtons();
                }
            }
            if (IsDisposed) return;

            // 曲名を今の書き方に付け替えたものは、カスタムステージの曲名も付け替える
            if (!_bookBroken && _book.RenameDrills(renamed)) SaveStages();

            // 置き場所の一覧を読み直して表を出し直す（再作成中の状態の文は下で上書きする）
            _otherNames = null;
            ShowStage();

            _status.Text = failures.Count == 0
                ? string.Format(CultureInfo.CurrentCulture, Lang.T("作り直しました: {0} 本", "Rebuilt: {0}"), copies.Count)
                : string.Format(CultureInfo.CurrentCulture, Lang.T("作り直しました: {0} 本（失敗 {1} 本）", "Rebuilt: {0} ({1} failed)"),
                    copies.Count - failures.Count, failures.Count);
            if (failures.Count > 0)
                MessageBox.Show(this, string.Join("\r\n", failures.Take(10)), "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OpenPreview()
        {
            var cell = _board.Selected;
            if (cell == null || _openPreview == null) return;
            _openPreview(this, cell.SongName);
        }

        private void OpenReplay()
        {
            var cell = _board.Selected;
            if (cell == null || !cell.BestPercent.HasValue || _openReplay == null) return;
            _openReplay(cell.Id);
        }

        private void ShowRows(List<MedalRow> rows, bool editable)
        {
            _total.Text = string.Format(CultureInfo.CurrentCulture, Lang.T("総合 Lv {0} / {1}", "Total Lv {0} / {1}"),
                rows.Sum(r => r.Level), rows.Sum(r => r.MaxLevel));
            _shownRows = rows;
            _board.SetRows(rows, editable);

            // 生成中は状態の行に進み具合が出ている。消すと生成中だと分からなくなる
            if (!_loaded || _generating) return;
            int medals = rows.Sum(r => r.Cells.Count(c => c.Medal != Medal.None));
            int total = rows.Sum(r => r.Cells.Count);
            _status.Text = string.Format(CultureInfo.CurrentCulture, Lang.T("メダル数 {0} / {1}{2}", "Medals {0} / {1}{2}"),
                medals, total, _failed > 0 ? Lang.T("   読み込めなかったリプレイ: " + _failed + " 件", "   Unreadable replays: " + _failed) : "");
        }

    }
}
