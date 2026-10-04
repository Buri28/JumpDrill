using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;
using JumpDrill.Replays;

namespace JumpDrill.Gui
{
    /// <summary>
    /// ドリル1本を組み立てて書き出す画面。
    /// 生成そのものは JumpDrill.Core に任せ、ここは指定と確認だけを持つ。
    /// </summary>
    public sealed class MainForm : Form
    {
        private readonly GridPicker _picker = new GridPicker();
        private readonly RadioButton _handRight = new RadioButton { Text = Lang.T("右手 (R)", "Right (R)"), AutoSize = true, Checked = true };
        private readonly RadioButton _handLeft = new RadioButton { Text = Lang.T("左手 (L)", "Left (L)"), AutoSize = true };
        private readonly Button _clearButton = new Button { Text = Lang.T("全部消す", "Clear all"), AutoSize = true };

        /// <summary>
        /// 点ごとの「矢印をまっすぐ」ボタン。編集中の手の点のぶんだけ並べ直す。
        /// 記法の <c>s</c> と同じもので、押した点の矢印を上下左右に倒す。
        /// </summary>
        private readonly FlowLayoutPanel _straightRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
        };

        /// <summary>並べ直している間はボタンの合図を無視する（押し戻しで無限に回らないように）。</summary>
        private bool _buildingStraight;

        private readonly ToolTip _tips = new ToolTip();
        private readonly TextBox _seqText = new TextBox { Dock = DockStyle.Fill };

        // 速さは BPM だけで指定する。ビーセイで速さを言う単位がこれで、
        // ms も EBPM も普段は目にしない。
        private readonly NumericUpDown _interval = Spin(30, 600, 150, 0);

        /// <summary>
        /// 1拍あたりのノーツ数。<b>1/2 に固定してある。</b>
        ///
        /// 「BPM だけでは細分化が決まらない」という曖昧さは、選ばせるのではなく
        /// 固定して潰す。1/2 に固定すると BPM がそのまま EBPM と同じ値になる
        /// （EBPM = 30000/片手の間隔、BPM(1/2) = 60000/(間隔×2) = 30000/間隔）ので、
        /// ScoreSaber の基準と突き合わせるのにも読み替えが要らない。
        /// 1/4 で叩きたければ BPM を倍にすれば間隔は同じ。
        /// </summary>
        private const double NotesPerBeat = 2.0;
        private readonly NumericUpDown _seconds = Spin(1, 600, 30, 0);
        private readonly ComboBox _direction = Combo(Lang.T("axis  配置ベクトルと一致", "axis  along the jump"), Lang.T("perp  垂直", "perp  perpendicular"), Lang.T("dot  ドット", "dot  dot notes"), Lang.T("explicit  記法内の @ 指定", "explicit  @ in the sequence"));
        private readonly ComboBox _hands = Combo(Lang.T("sync   両手同時", "sync   both hands together"), Lang.T("split  片手ずつ（右で1本→左で1本）", "split  one hand at a time (right, then left)"), Lang.T("alt    1ステップごとに交互", "alt    alternate every step"));
        private readonly CheckBox _mirror = new CheckBox { Text = Lang.T("左右反転して反対の手にも同じ形を組む", "Mirror the pattern onto the other hand"), AutoSize = true };
        private readonly ComboBox _order = Combo(Lang.T("右 → 左", "Right → Left"), Lang.T("左 → 右", "Left → Right"));
        private readonly NumericUpDown _sets = Spin(1, 50, 1, 0);

        private readonly NumericUpDown _njs = Spin(1, 40, 16, 1);
        private readonly ComboBox _jumpMode = Combo(Lang.T("既定 (オフセット 0)", "Default (offset 0)"), Lang.T("ジャンプ距離 (m)", "Jump distance (m)"), Lang.T("反応時間 (ms)", "Reaction time (ms)"), Lang.T("オフセット (拍)", "Offset (beats)"));
        private readonly NumericUpDown _jumpValue = Spin(-10, 100, 18, 2);

        private readonly ComboBox _click = Combo(Lang.T("none  鳴らさない", "none  off"), Lang.T("all   Lv1 全ノーツ+アクセント", "all   Lv1 every note + accent"), Lang.T("down  Lv2 切り下げのみ", "down  Lv2 down cuts only"), Lang.T("up    Lv3 切り上げのみ", "up    Lv3 up cuts only"), Lang.T("every4 Lv4 4ノーツに1回", "every4 Lv4 once every 4 notes"));
        private readonly NumericUpDown _countIn = Spin(0, 32, 8, 0);
        private readonly NumericUpDown _tail = Spin(0, 10, 1.5m, 1);


        private readonly TextBox _outDir = new TextBox { Dock = DockStyle.Fill };
        private readonly Button _browse = new Button { Text = "...", Width = 32 };
        // 見つかった CustomLevels を選ばせる。BSManager はバージョンごとに
        // インスタンスが分かれるので、自動で1つ選ぶと違う版に入れてしまう。
        private readonly ComboBox _outPreset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left };
        private readonly List<string> _presetPaths = new List<string>();
        private readonly ToolTip _presetTip = new ToolTip();
        private readonly Button _registerPack = new Button { Text = Lang.T("専用パックとして登録", "Register as a pack"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 1, 6, 1), Anchor = AnchorStyles.Left };
        private readonly CheckBox _zip = new CheckBox { Text = Lang.T("リプレイ表示用の zip も出す", "Also write a zip for replay viewers"), AutoSize = true };
        // パックも zip もここの下に置く。Beat Saber の中に置くと
        // BSManager がインスタンスを作り直したときに消える。
        private readonly TextBox _workspace = new TextBox { Dock = DockStyle.Fill };
        private readonly Button _browseWorkspace = new Button { Text = "...", Width = 32 };

        private readonly TextBox _summary = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            // 折り返すと「テンポ」や「尺」の行が途中で切れて読めなくなる。
            // 横スクロールに逃がして1行を1行のまま保つ。
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 8.5f),
            BackColor = Color.FromArgb(0x1E, 0x20, 0x24),
            ForeColor = Color.FromArgb(0xD0, 0xD4, 0xDA),
            BorderStyle = BorderStyle.FixedSingle,
        };

        private readonly TextBox _cliText = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9f),
        };

        private readonly Button _generate = new Button { Text = Lang.T("生成", "Generate"), Height = 34, Dock = DockStyle.Fill };

        /// <summary>ドリル（14方向 × 8 Stage）を出力先へ一括で書く。画面の指定は使わない。</summary>
        private readonly Button _bulk = new Button { Text = Lang.T("ドリル一括生成", "Generate all drills"), Height = 34, Dock = DockStyle.Fill };
        private readonly Button _openOut = new Button { Text = Lang.T("出力先を開く", "Open output folder"), Height = 34, Dock = DockStyle.Fill };
        private readonly Button _preview = new Button { Text = Lang.T("ArcViewer でプレビュー", "Preview in ArcViewer"), Height = 34, Dock = DockStyle.Fill };

        /// <summary>ArcViewer に譜面 zip を渡すためのローカル HTTP サーバ。要るまで立てない。</summary>
        private LocalFileServer _server;

        /// <summary>前回プレビューした譜面。次に開いたとき選んだ状態にする。</summary>
        private string _lastPreviewFolder;
        private readonly Button _scoreboard = new Button { Text = Lang.T("スコア", "Scores"), Height = 34, Dock = DockStyle.Fill };
        private readonly Button _medals = new Button { Text = Lang.T("メダル", "Medals"), Height = 34, Dock = DockStyle.Fill };
        /// <summary>配布版だけに出す。確認が済むまでは押せない。</summary>
        private readonly Button _update = new Button { Text = Lang.T("更新を確認中", "Checking for updates"), Height = 34, Dock = DockStyle.Fill, Enabled = false };
        private string _latestTag;
        /// <summary>配布版だけに出す。同梱の MOD を Beat Saber へ入れる。</summary>
        private readonly Button _mod = new Button { Text = Lang.T("MOD を入れる", "Install MOD"), Height = 34, Dock = DockStyle.Fill };
        /// <summary>画面の言語。切り替えたら窓を作り直す（Program が開き直す）。</summary>
        private readonly ComboBox _language = Combo("日本語", "English");

        /// <summary>言語を切り替えて閉じたとき true。Program がこれを見て窓を開き直す。</summary>
        public bool RestartRequested { get; private set; }

        private readonly Label _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

        private bool _syncing;
        private bool _loading;
        private string _lastOutputFolder;
        private readonly SettingsStore _settings = new SettingsStore(SettingsStore.DefaultPath());

        public MainForm()
        {
            Text = "JumpDrill";
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Yu Gothic UI", 9f);

            _direction.SelectedIndex = 0;
            _hands.SelectedIndex = 0;
            _click.SelectedIndex = 2;   // Lv2 が標準
            _jumpMode.SelectedIndex = 0;
            _order.SelectedIndex = 0;
            _language.SelectedIndex = Lang.English ? 1 : 0;
            _jumpValue.Enabled = false;
            _workspace.Text = Workspace.DefaultRoot();
            _outDir.Text = DefaultOutputDirectory();
            PopulateOutputPresets();
            _combos.Add(_outPreset);

            BuildLayout();
            Theme.Apply(this);
            WireEvents();

            LoadSettings();
            ApplyHandPattern();
            RefreshPreview();

            SetUpUpdateButton();
            SetUpModButton();

            FormClosing += (s, e) => SaveSettings();
            Load += (s, e) => FitToText(true);
            // 拡大率の違う画面へ動かしたとき。大きさだけ直し、置き場所は動かした先のままにする
            DpiChanged += (s, e) => BeginInvoke(new Action(() => FitToText(false)));
        }

        /// <summary>
        /// 文字の実寸と画面の拡大率に合わせて、入力欄と窓の大きさを決める。
        /// 入力欄は列いっぱいに伸ばさず、中身が収まる幅にする。
        /// </summary>
        private void FitToText(bool center)
        {
            // 言語を切り替えると窓を作り直すので、前の窓の選択肢が残っている。
            _combos.RemoveAll(c => c.IsDisposed);

            // 選択肢は一番長いものが閉じた状態でも全部見える幅にする。
            foreach (var combo in _combos)
            {
                int widest = 0;
                foreach (var item in combo.Items)
                    widest = Math.Max(widest, TextRenderer.MeasureText(item.ToString(), combo.Font).Width);
                combo.Width = widest + SystemInformation.VerticalScrollBarWidth + Px(12);
                combo.DropDownWidth = combo.Width;
            }

            foreach (var spin in new[] { _interval, _seconds, _sets, _njs, _jumpValue, _countIn, _tail })
                spin.Width = Px(80);

            _browse.Width = Px(28);
            _browseWorkspace.Width = Px(28);

            // 出力先と作業フォルダは同じ幅に揃える。（参照ボタンの幅を先に決めておく。初回と2回目で幅が変わらないように）
            int pathWidth = Math.Max(_outPreset.Width - _browse.Width, Px(260));
            _outDir.Width = pathWidth;
            _workspace.Width = pathWidth;

            _root.ColumnStyles[0].Width = Px(LeftWidth);
            _leftPanel.RowStyles[0].Height = Px(LeftWidth) * 3 / 4;

            // CLI のコマンドは折り返して全部見せる。
            _cliText.Height = TextRenderer.MeasureText("A", _cliText.Font).Height * 2 + Px(8);

            FitWindow(center);
        }

        /// <summary>
        /// 窓を中身が収まる大きさにする。左の列と設定欄の幅、高い方の列の高さで決まる。
        /// </summary>
        private void FitWindow(bool center)
        {
            PerformLayout();

            Size parameters = _params.GetPreferredSize(Size.Empty);
            _rightPanel.RowStyles[0].Height = parameters.Height + Px(4);

            int width = _root.Padding.Horizontal + Px(LeftWidth) + _leftPanel.Margin.Horizontal
                      + parameters.Width + Px(24);

            // 高さは、左の列（グリッドと入力欄）と右の列（設定・確認・CLI・ボタン）の高い方。
            int left = _leftPanel.GetPreferredSize(new Size(Px(LeftWidth), 0)).Height;
            int right = parameters.Height + Px(170)
                      + _rightPanel.GetControlFromPosition(0, 2).GetPreferredSize(Size.Empty).Height
                      + _rightPanel.GetControlFromPosition(0, 3).GetPreferredSize(new Size(parameters.Width, 0)).Height
                      + Px(24);
            int height = _root.Padding.Vertical + Math.Max(left, right);

            Rectangle area = Screen.FromControl(this).WorkingArea;
            Size outer = SizeFromClientSize(new Size(width, height));
            outer = new Size(Math.Min(outer.Width, area.Width), Math.Min(outer.Height, area.Height));

            MinimumSize = outer;
            Size = outer;
            if (center)
                Location = new Point(area.Left + (area.Width - outer.Width) / 2, area.Top + (area.Height - outer.Height) / 2);
            else
                Location = new Point(
                    Math.Max(area.Left, Math.Min(Left, area.Right - outer.Width)),
                    Math.Max(area.Top, Math.Min(Top, area.Bottom - outer.Height)));
        }

        private int Px(int value)
        {
            return (int)Math.Round(value * DeviceDpi / 96.0);
        }

        // ---------------------------------------------------------------- layout

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(10),
            };
            // 左はグリッドが収まる幅に固定し、残りを設定欄に回す。幅は拡大率を掛けて FitToText で決める。
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LeftWidth));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _root = root;
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            root.Controls.Add(BuildLeftPanel(), 0, 0);
            root.Controls.Add(BuildRightPanel(), 1, 0);
            Controls.Add(root);
        }

        /// <summary>左の列の幅（96 dpi）。グリッドの高さはこの 3/4。</summary>
        private const int LeftWidth = 300;

        private TableLayoutPanel _root;
        private TableLayoutPanel _rightPanel;
        private TableLayoutPanel _leftPanel;
        private Panel _paramsScroll;
        private TableLayoutPanel _params;
        private static readonly List<ComboBox> _combos = new List<ComboBox>();

        private Control BuildLeftPanel()
        {
            var panel = _leftPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Margin = new Padding(0, 0, 10, 0),
            };
            panel.RowCount = 7;
            // ピッカーは 4:3 の升目しか描かないので、余った高さは黒い余白になる。
            // 高さを決め打ちして、余りは確認欄に回す。
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 330));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 編集する手
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 矢印をまっすぐ
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 左右反転
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 遷移（記法）
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 生成・出力先を開く
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _picker.Dock = DockStyle.Fill;
            panel.Controls.Add(_picker, 0, 0);

            // 幅が足りないときは折り返す。ボタンが見切れると押せなくなる。
            var handRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            handRow.Controls.Add(new Label { Text = Lang.T("編集する手:", "Hand:"), AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            handRow.Controls.Add(_handRight);
            handRow.Controls.Add(_handLeft);
            handRow.Controls.Add(_clearButton);
            panel.Controls.Add(handRow, 0, 1);

            panel.Controls.Add(_straightRow, 0, 2);
            RebuildStraightRow();

            panel.Controls.Add(_mirror, 0, 3);

            // 例文は見出しに入れると、左の列が狭いときに切れる。入力欄が空のときの薄い文字と、ツールチップに出す。
            var seqBox = new GroupBox
            {
                Text = Lang.T("遷移（記法）", "Sequence (notation)"),
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8),
            };
            _seqText.Font = new Font("Consolas", 10f);
            _seqText.Dock = DockStyle.Top;
            _seqText.PlaceholderText = Lang.T("例: R8b, La1 / L194c / R4sbs", "e.g. R8b, La1 / L194c / R4sbs");
            seqBox.Controls.Add(_seqText);

            _tips.SetToolTip(_seqText,
                Lang.T(
                "例: R8b, La1 / L194c / R4sbs\r\n" +
                "R / L で手を指定（省略すると右手）\r\n" +
                "カンマで両手ぶんを並べる:  R8b, La1\r\n" +
                "点は片手2点まで（その2点の間をどれだけ直線で振れたかを測る）\r\n" +
                "点の後ろの s で矢印を上下左右に倒す: R4bs（b が ▼）\r\n" +
                "上の［矢印をまっすぐ］のボタンと同じもの",
                "e.g. R8b, La1 / L194c / R4sbs\r\n" +
                "R / L picks the hand (right if omitted)\r\n" +
                "Separate the two hands with a comma:  R8b, La1\r\n" +
                "Up to 2 points per hand (measures how straight you swing between them)\r\n" +
                "s after a point snaps its arrow to up/down/left/right: R4bs (b becomes ▼)\r\n" +
                "Same as the [Straight arrows] buttons above"));
            panel.Controls.Add(seqBox, 0, 4);

            // 遷移を書いたらすぐ押せるよう、生成はその真下に大きく置く。
            var generateRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            generateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            generateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            _generate.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
            _generate.Height = 44;
            _generate.Margin = new Padding(3, 0, 4, 0);
            _openOut.Height = 44;
            _openOut.Margin = new Padding(4, 0, 3, 0);
            generateRow.Controls.Add(_generate, 0, 0);
            generateRow.Controls.Add(_openOut, 1, 0);
            panel.Controls.Add(generateRow, 0, 5);

            // 言語は左の列の下の空きに固定する。ボタンの並びに入れると、
            // ボタンの文字の幅で折り返し方が変わり、言語ごとに居場所が変わる。
            // バージョンは言語の横に、更新と MOD はその下に出す（アプリ自体の操作）。
            _language.Dock = DockStyle.None;
            _language.Margin = new Padding(0, 6, 0, 2);
            var bottomRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0),
            };
            bottomRow.Controls.Add(_language);
            string version = UpdateCheck.CurrentVersion();
            if (version != null)
                bottomRow.Controls.Add(new Label { Text = "v" + version, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(10, 11, 0, 0) });

            var appRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0) };
            foreach (var button in new[] { _update, _mod })
            {
                SizeButton(button);
                appRow.Controls.Add(button);
            }
            _update.Margin = new Padding(0, 3, 3, 3);

            var bottom = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                Margin = new Padding(0),
            };
            bottom.Controls.Add(appRow);
            bottom.Controls.Add(bottomRow);
            panel.Controls.Add(bottom, 0, 6);

            return panel;
        }

        /// <summary>確認欄。10行ほど出るので、読める高さを確保できる側に置く。</summary>
        private Control BuildPreviewBox()
        {
            var box = new GroupBox { Text = Lang.T("確認", "Summary"), Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 4) };
            box.Controls.Add(_summary);
            return box;
        }

        private Control BuildRightPanel()
        {
            // パラメータは2列に割る。1列に積むと縦に収まらず、
            // 下側のグループ（NJS など）がスクロールの外に隠れて
            // 「設定が無くなった」ようにしか見えない。
            // 画面の作業領域が 912px しかないので、縦に積める量には上限がある。
            // 左右の高さが揃うように振り分ける。ジャンプは右列の先頭に置いて
            // 埋もれないようにしてある。
            var left = Stack(
                Group(Lang.T("時間", "Timing"), Rows(
                    Row(Lang.T("BPM (1拍に2ノーツ)", "BPM (2 notes per beat)"), _interval),
                    Row(Lang.T("尺 (秒)", "Length (s)"), _seconds))),
                Group(Lang.T("切る方向 / 手", "Direction / hands"), Rows(
                    Row(Lang.T("切る方向", "Cut direction"), _direction),
                    Row(Lang.T("手の並べ方", "Hand pattern"), _hands),
                    Row(Lang.T("セットの順番", "Set order"), _order),
                    Row(Lang.T("セット数", "Sets"), _sets))),
                Group(Lang.T("クリック（音源に焼き込む）", "Click (baked into the audio)"), Rows(
                    Row(Lang.T("粒度", "Pattern"), _click),
                    Row(Lang.T("カウントイン", "Count-in"), _countIn),
                    Row(Lang.T("末尾の余白", "Tail padding"), _tail))));

            var outRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            outRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            outRow.Controls.Add(_outDir, 0, 0);
            outRow.Controls.Add(_browse, 1, 0);
            var outInner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
            outInner.Controls.Add(_outPreset, 0, 0);
            outInner.Controls.Add(outRow, 0, 1);
            var workRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
            workRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            workRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            workRow.Controls.Add(_workspace, 0, 0);
            workRow.Controls.Add(_browseWorkspace, 1, 0);

            // 行が増えると左列の「クリック」まで押し出されるので、見出しは
            // 独立した行にせず、他と同じラベル付き1行に収める。
            outInner.Controls.Add(_zip, 0, 2);
            outInner.Controls.Add(Rows(Row(Lang.T("作業フォルダ", "Workspace"), workRow)), 0, 3);
            outInner.Controls.Add(_registerPack, 0, 4);

            var right = Stack(
                Group(Lang.T("ジャンプ（間隔とは独立）", "Jump (independent of interval)"), Rows(
                    Row("NJS", _njs),
                    Row(Lang.T("指定の仕方", "Set by"), _jumpMode),
                    Row(Lang.T("値", "Value"), _jumpValue))),
                Group(Lang.T("出力", "Output"), outInner));

            // 入力欄は中身の幅なので、設定欄も中身の大きさで決まる。窓が狭ければスクロールする。
            var panel = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.Controls.Add(left, 0, 0);
            panel.Controls.Add(right, 1, 0);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            scroll.Controls.Add(panel);
            _paramsScroll = scroll;
            _params = panel;

            var cliRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true };
            cliRow.Controls.Add(new Label { Text = Lang.T("同じ内容の CLI コマンド:", "Equivalent CLI command:"), AutoSize = true, ForeColor = SystemColors.GrayText }, 0, 0);
            _cliText.Dock = DockStyle.Top;
            cliRow.Controls.Add(_cliText, 0, 1);

            // 記録（スコア・メダル）と譜面（プレビュー・一括生成）を分けて右から並べる。
            // 幅が足りなければ次の行に折り返す（見切れると押せない）。
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = true,
                Margin = new Padding(0),
            };
            foreach (var button in new[] { _bulk, _preview, _medals, _scoreboard })
            {
                SizeButton(button);
                buttons.Controls.Add(button);
            }
            _preview.Margin = new Padding(24, 3, 3, 3);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            actions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            actions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            // 長いパスも切らずに折り返す。
            _status.AutoSize = true;
            _status.AutoEllipsis = false;
            _status.Dock = DockStyle.None;
            actions.Resize += (s, e) => _status.MaximumSize = new Size(Math.Max(1, actions.ClientSize.Width), 0);
            actions.Controls.Add(_status, 0, 0);
            actions.Controls.Add(buttons, 0, 1);

            // 設定欄の高さは FitWindow で中身に合わせる。確認欄は横幅のある右側で残りの高さを使う。
            var outer = _rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, Px(300)));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.Controls.Add(scroll, 0, 0);
            outer.Controls.Add(BuildPreviewBox(), 0, 1);
            outer.Controls.Add(cliRow, 0, 2);
            outer.Controls.Add(actions, 0, 3);

            _openOut.Enabled = false;
            return outer;
        }

        /// <summary>下に並べるボタンは文字の幅に合わせる。</summary>
        private static void SizeButton(Button button)
        {
            button.AutoSize = true;
            button.Dock = DockStyle.None;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(6, 1, 6, 1);
        }

        /// <summary>グループボックスを縦に積む。</summary>
        private static TableLayoutPanel Stack(params Control[] groups)
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            for (int i = 0; i < groups.Length; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.Controls.Add(groups[i], 0, i);
            }
            return table;
        }

        private static GroupBox Group(string title, Control content)
        {
            // GrowOnly だと一度広がった枠が窓を狭めても縮まず、右端が切れる。
            var box = new GroupBox { Text = title, Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 4, 6, 4) };
            box.Controls.Add(content);
            return box;
        }

        /// <summary>数値と単位を1行に並べる。</summary>
        /// <summary>ラベルと入力欄の組。</summary>
        private struct Field
        {
            public string Label;
            public Control Control;
        }

        private static Field Row(string label, Control control)
        {
            return new Field { Label = label, Control = control };
        }

        /// <summary>
        /// ラベル列と入力列の2列グリッドを1枚だけ作る。
        /// 行ごとに TableLayoutPanel を入れ子にすると、AutoSize の伸縮で
        /// ラベルと入力欄が別の行に分かれて表示されることがある。
        /// </summary>
        private static TableLayoutPanel Rows(params Field[] fields)
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = fields.Length,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            // ラベルの列は文字の幅に合わせる。決め打ちの幅だと拡大率が上がったときに切れる。
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            for (int i = 0; i < fields.Length; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                // 文字の幅で列の幅が決まるよう AutoSize にする。左だけ留めると縦は中央に来る。
                table.Controls.Add(new Label
                {
                    Text = fields[i].Label,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0, 1, 6, 1),
                }, 0, i);

                // 入力欄は列いっぱいに伸ばさず、FitToText で決めた幅のまま左に寄せる。
                fields[i].Control.Dock = DockStyle.None;
                fields[i].Control.Anchor = AnchorStyles.Left;
                fields[i].Control.Margin = new Padding(0, 1, 0, 1);
                table.Controls.Add(fields[i].Control, 1, i);
            }

            return table;
        }

        private static NumericUpDown Spin(decimal min, decimal max, decimal value, int decimals)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                DecimalPlaces = decimals,
                Increment = decimals == 0 ? 1m : 0.1m,
                Dock = DockStyle.Fill,
            };
        }

        private static ComboBox Combo(params string[] items)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            combo.Items.AddRange(items);
            _combos.Add(combo);
            return combo;
        }

        // ---------------------------------------------------------------- update

        private void SetUpUpdateButton()
        {
            string current = UpdateCheck.CurrentVersion();
            if (current != null)
                Text = "JumpDrill v" + current;

            if (!UpdateCheck.Available)
            {
                _update.Visible = false;
                return;
            }

            _update.Click += (s, e) => OpenUpdater();
            Shown += async (s, e) =>
            {
                try
                {
                    _latestTag = await UpdateCheck.LatestTagAsync();
                    if (IsDisposed) return;
                    if (UpdateCheck.IsNewer(_latestTag, current))
                    {
                        _update.Text = Lang.T(_latestTag + " に更新", "Update to " + _latestTag);
                        _update.Font = new Font(_update.Font, FontStyle.Bold);
                        _tips.SetToolTip(_update, Lang.T("新しいバージョンがあります。", "A new version is available."));
                    }
                    else
                    {
                        _update.Text = Lang.T("最新版です", "Up to date");
                        _tips.SetToolTip(_update, Lang.T("ほかのバージョンに切り替えることもできます。", "You can also switch to another version."));
                    }
                }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException
                                           || ex is System.Text.Json.JsonException || ex is KeyNotFoundException)
                {
                    if (IsDisposed) return;
                    _update.Text = Lang.T("更新を確認", "Check for updates");
                    _tips.SetToolTip(_update, Lang.T("更新を確認できませんでした。", "Could not check for updates.") + Environment.NewLine + ex.Message);
                }
                _update.Enabled = true;
            };
        }

        /// <summary>Update.exe を開いて自分は閉じる。開いたままだと exe を差し替えられない。</summary>
        private void OpenUpdater()
        {
            if (MessageBox.Show(this, Lang.T("JumpDrill を閉じて、更新の画面を開きます。", "JumpDrill will close and open the updater."), Lang.T("更新", "Update"),
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;
            try
            {
                UpdateCheck.LaunchUpdater(_latestTag);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                MessageBox.Show(this, Lang.T("更新の画面を開けませんでした。", "Could not open the updater.") + Environment.NewLine + ex.Message, Lang.T("更新", "Update"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Close();
        }

        private void SetUpModButton()
        {
            if (!ModPackage.Available)
            {
                _mod.Visible = false;
                return;
            }

            _mod.Click += (s, e) =>
            {
                using (var form = new ModInstallForm(_settings))
                    form.ShowDialog(this);
                MarkOutdatedMod();
            };
            Shown += (s, e) => MarkOutdatedMod();
        }

        /// <summary>入れてある MOD が同梱のものより古ければ、ボタンで知らせる。</summary>
        private async void MarkOutdatedMod()
        {
            bool outdated = await Task.Run(() => LevelWriter.FindInstalls().Any(ModPackage.IsOutdated));
            if (IsDisposed) return;
            _mod.Text = outdated ? Lang.T("MOD を更新", "Update MOD") : Lang.T("MOD を入れる", "Install MOD");
            _mod.Font = new Font(_mod.Font, outdated ? FontStyle.Bold : FontStyle.Regular);
            _tips.SetToolTip(_mod, outdated ? Lang.T("入っている JumpDrillMod が古くなっています。", "The installed JumpDrillMod is out of date.") : "");
        }

        // ---------------------------------------------------------------- events

        private void WireEvents()
        {
            _picker.SelectionChanged += (s, e) => SyncTextFromPicker();
            _seqText.TextChanged += (s, e) => SyncPickerFromText();

            _handRight.CheckedChanged += (s, e) => { if (_handRight.Checked) { _picker.ActiveHand = Hand.Right; RebuildStraightRow(); } };
            _handLeft.CheckedChanged += (s, e) => { if (_handLeft.Checked) { _picker.ActiveHand = Hand.Left; RebuildStraightRow(); } };
            _clearButton.Click += (s, e) => { _picker.ClearAll(); };

            _order.SelectedIndexChanged += (s, e) => RefreshPreview();

            foreach (var c in new Control[] { _interval, _sets, _seconds, _njs, _jumpValue, _countIn, _tail })
                ((NumericUpDown)c).ValueChanged += (s, e) => RefreshPreview();

            foreach (var c in new[] { _direction, _click })
                c.SelectedIndexChanged += (s, e) => RefreshPreview();

            _hands.SelectedIndexChanged += (s, e) => { ApplyHandPattern(); RefreshPreview(); };
            _mirror.CheckedChanged += (s, e) => ApplyHandPattern();

            _jumpMode.SelectedIndexChanged += (s, e) =>
            {
                if (_loading) return;
                ApplyJumpMode();
                RefreshPreview();
            };

            foreach (var c in new[] { _mirror })
                c.CheckedChanged += (s, e) => RefreshPreview();

            _outDir.TextChanged += (s, e) => RefreshPreview();
            _outPreset.SelectedIndexChanged += (s, e) => ApplyOutputPreset();
            _registerPack.Click += (s, e) => RegisterAsPack();
            _zip.CheckedChanged += (s, e) => RefreshPreview();
            _workspace.TextChanged += (s, e) => RefreshPreview();
            _browseWorkspace.Click += (s, e) => BrowseWorkspace();
            new ToolTip().SetToolTip(_workspace, Lang.T("専用パックと zip をこの下に置く。", "Packs and zips are saved under this folder.") + Environment.NewLine + Lang.T("Beat Saber の中に置くと BSManager の更新で消える。", "Inside Beat Saber they are deleted when BSManager updates."));
            _browse.Click += (s, e) => Browse();
            _generate.Click += async (s, e) => await GenerateAsync();
            _bulk.Click += async (s, e) => await GenerateBulkAsync();
            _tips.SetToolTip(_bulk, Lang.T("14方向 × 8 Stage の譜面（112曲）を出力先に作成します。", "Writes 112 maps (14 directions × 8 stages) to the output folder."));
            _openOut.Click += (s, e) => OpenOutputFolder();
            _preview.Click += (s, e) => OpenInArcViewer();
            FormClosed += (s, e) => { if (_server != null) _server.Dispose(); };
            _scoreboard.Click += (s, e) => ShowScoreboard();
            _medals.Click += (s, e) => ShowMedals();
            _tips.SetToolTip(_language, "言語 / Language");
            _language.SelectedIndexChanged += (s, e) => SwitchLanguage();
        }

        /// <summary>
        /// 言語を切り替える。文言は窓を作るときに決まるので、指定を保存して窓を開き直す。
        /// </summary>
        private void SwitchLanguage()
        {
            bool english = _language.SelectedIndex == 1;
            if (english == Lang.English) return;

            Lang.English = english;
            RestartRequested = true;
            Close();
        }

        /// <summary>順番は手が2つあるときだけ意味がある。</summary>
        private void ApplyHandPattern()
        {
            bool split = _hands.SelectedIndex == 1;
            bool twoHands = _mirror.Checked || _seqText.Text.Contains(",");
            _order.Enabled = split && twoHands;
        }

        private static decimal Clamp(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>入力欄の BPM をノーツ間隔 (ms) に直す。細分化は 1/2 固定。</summary>
        private double ToIntervalMs(decimal shown)
        {
            return Tempo.IntervalMsFromBpm((double)shown, NotesPerBeat);
        }

        private void ApplyJumpMode()
        {
            ApplyJumpModeRanges();

            // 指定の仕方を変えたときだけ、その単位の妥当な初期値に置き換える。
            switch (_jumpMode.SelectedIndex)
            {
                case 1: _jumpValue.Value = 18; break;
                case 2: _jumpValue.Value = 550; break;
                case 3: _jumpValue.Value = 0; break;
            }
        }

        /// <summary>単位ごとの桁と範囲だけを合わせる。値には触らない。</summary>
        private void ApplyJumpModeRanges()
        {
            _jumpValue.Enabled = _jumpMode.SelectedIndex != 0;
            switch (_jumpMode.SelectedIndex)
            {
                case 1: // ジャンプ距離 (m)
                    _jumpValue.DecimalPlaces = 2;
                    _jumpValue.Minimum = 1; _jumpValue.Maximum = 60;
                    break;
                case 2: // 反応時間 (ms)
                    _jumpValue.DecimalPlaces = 0;
                    _jumpValue.Minimum = 100; _jumpValue.Maximum = 3000;
                    break;
                case 3: // オフセット (拍)
                    _jumpValue.DecimalPlaces = 3;
                    _jumpValue.Minimum = -10; _jumpValue.Maximum = 10;
                    break;
            }
        }

        private void SyncTextFromPicker()
        {
            if (_syncing) return;
            _syncing = true;
            try { _seqText.Text = ComposeSpec(); }
            finally { _syncing = false; }
            RebuildStraightRow();
            RefreshPreview();
        }

        /// <summary>
        /// 「矢印をまっすぐ」のボタンを並べ直す。編集中の手の点ごとに1つ。
        ///
        /// Ctrl+クリックだけだと、そんな操作があること自体が画面から読めない。
        /// 点の記号がそのままボタンの名前になっているので、
        /// <b>4 と b のどちらを倒しているか</b>が押した形で分かる。
        /// </summary>
        private void RebuildStraightRow()
        {
            _buildingStraight = true;
            _straightRow.SuspendLayout();

            var old = _straightRow.Controls.Cast<Control>().ToArray();
            _straightRow.Controls.Clear();
            foreach (var control in old) control.Dispose();

            var hand = _picker.ActiveHand;
            var steps = _picker.StepsFor(hand);

            _straightRow.Controls.Add(new Label
            {
                Text = steps.Count == 0
                    ? Lang.T("矢印をまっすぐ:  マスを置くと点ごとに出ます", "Straight arrows:  one appears per point you place")
                    : Lang.T("矢印をまっすぐ:", "Straight arrows:"),
                AutoSize = true,
                Margin = new Padding(0, 7, 6, 0),
            });

            for (int i = 0; i < steps.Count; i++)
            {
                int index = i;
                var step = steps[i];

                var toggle = new CheckBox
                {
                    Appearance = Appearance.Button,
                    Text = step.Position.ToToken().ToString(),
                    Checked = step.Straighten,
                    AutoSize = false,
                    Size = new Size(34, 26),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Consolas", 10f),
                };

                _tips.SetToolTip(toggle, string.Format(
                    Lang.T("{0} の矢印を上下左右のいちばん近い向きに倒す（記法の {0}s）", "Snap the arrow at {0} to the nearest straight direction ({0}s in notation)"),
                    step.Position.ToToken()));

                toggle.CheckedChanged += (s, e) =>
                {
                    if (_buildingStraight) return;
                    _picker.SetStraighten(hand, index, toggle.Checked);
                };

                _straightRow.Controls.Add(toggle);
            }

            _straightRow.ResumeLayout();
            _buildingStraight = false;
        }

        private void SyncPickerFromText()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                // 打ち途中は当然パースできないので、通ったときだけ picker に反映する。
                try { _picker.SetFrom(SequenceParser.ParseAll(_seqText.Text)); }
                catch { /* 表示は RefreshPreview のエラーに任せる */ }
            }
            finally { _syncing = false; }
            ApplyHandPattern();
            RebuildStraightRow();
            RefreshPreview();
        }

        private string ComposeSpec()
        {
            var parts = new List<string>();
            foreach (var hand in new[] { Hand.Right, Hand.Left })
            {
                var list = _picker.StepsFor(hand);
                if (list.Count == 0) continue;

                // 記法の組み立ては HandSequence に任せる。s も @ も書き戻すので、
                // 盤面を触っても打ち込んだ指定が落ちない。
                parts.Add(HandSequence.Compose(hand, list));
            }
            return string.Join(", ", parts);
        }

        // ---------------------------------------------------------------- options

        private DrillOptions BuildOptions(double intervalMs)
        {
            var sequences = SequenceParser.ParseAll(_seqText.Text);

            if (_mirror.Checked)
            {
                if (sequences.Count != 1)
                    throw new InvalidOperationException(Lang.T("左右反転は片手ぶんだけ組んでいるときに使えます。", "Mirror works only when one hand is specified."));
                var source = sequences[0];
                sequences.Add(SequenceParser.Mirror(source, source.Hand == Hand.Right ? Hand.Left : Hand.Right));
            }

            // ブロックの順番がそのまま「右→左」か「左→右」になる。
            var first = _order.SelectedIndex == 1 ? Hand.Left : Hand.Right;
            sequences = sequences.OrderBy(s => s.Hand == first ? 0 : 1).ToList();

            var options = new DrillOptions
            {
                Sequences = sequences,
                IntervalMs = intervalMs,
                DurationSeconds = (double)_seconds.Value,
                NotesPerBeat = NotesPerBeat,
                Sets = (int)_sets.Value,
                Direction = (DirectionMode)_direction.SelectedIndex,
                HandPattern = HandPatternFromIndex(_hands.SelectedIndex),
                // 譜面の基準 BPM にも指定した BPM をそのまま使う。
                // 別の値にすると曲一覧の BPM が指定と食い違い、
                // ノーツも拍の上に乗らない（1/2 固定なら半拍ごとにきれいに並ぶ）。
                // ノーツの実時刻は ms から決まるので、譜面そのものは変わらない。
                Bpm = (double)_interval.Value,
                Njs = (double)_njs.Value,
                Click = (ClickMode)_click.SelectedIndex,
                CountInClicks = (int)_countIn.Value,
                TailSeconds = (double)_tail.Value,
            };

            double value = (double)_jumpValue.Value;
            if (_jumpMode.SelectedIndex == 1) options.JumpDistance = value;
            else if (_jumpMode.SelectedIndex == 2) options.ReactionTimeMs = value;
            else if (_jumpMode.SelectedIndex == 3) options.NoteJumpStartBeatOffset = value;

            options.Validate();
            return options;
        }

        private static HandPattern HandPatternFromIndex(int index)
        {
            if (index == 1) return HandPattern.Split;
            if (index == 2) return HandPattern.Alternate;
            return HandPattern.Sync;
        }

        private void RefreshPreview()
        {
            if (_loading) return;

            try
            {
                var map = DrillGenerator.Generate(BuildOptions(ToIntervalMs(_interval.Value)));

                _summary.Text = LevelWriter.Summarize(map).Replace("\n", "\r\n");
                _summary.ForeColor = Color.FromArgb(0xD0, 0xD4, 0xDA);
                _cliText.Text = ComposeCommandLine();
                _generate.Enabled = true;
            }
            catch (Exception ex)
            {
                _summary.Text = ex.Message;
                _summary.ForeColor = Color.FromArgb(0xE8, 0x88, 0x88);
                _cliText.Text = string.Empty;
                _generate.Enabled = false;
            }
        }

        private string ComposeCommandLine()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("drill");
            sb.Append(" --seq \"").Append(_seqText.Text.Trim()).Append('"');

            // CLI は ms / ebpm / bpm を受けるので、単位と細分化を明示する。
            // --bpm も要る。GUI は基準 BPM を持たず、指定した BPM をそのまま使うため
            sb.AppendFormat(ci, " --interval {0:0}bpm --div 1/{1:0} --bpm {0:0}", _interval.Value, NotesPerBeat);

            sb.AppendFormat(ci, " --sec {0:0}", _seconds.Value);

            string[] dirNames = { "axis", "perp", "dot", "explicit" };
            if (_direction.SelectedIndex != 0) sb.Append(" --dir ").Append(dirNames[_direction.SelectedIndex]);
            if (_hands.SelectedIndex == 1) sb.Append(" --hands split");
            else if (_hands.SelectedIndex == 2) sb.Append(" --hands alt");
            if (_mirror.Checked) sb.Append(" --mirror");
            if (_order.Enabled) sb.Append(_order.SelectedIndex == 1 ? " --order lr" : " --order rl");
            if (_sets.Value != 1m) sb.AppendFormat(ci, " --sets {0:0}", _sets.Value);

            if (_njs.Value != 16m) sb.AppendFormat(ci, " --njs {0:0.##}", _njs.Value);
            if (_jumpMode.SelectedIndex == 1) sb.AppendFormat(ci, " --jd {0:0.##}", _jumpValue.Value);
            else if (_jumpMode.SelectedIndex == 2) sb.AppendFormat(ci, " --rt {0:0}", _jumpValue.Value);
            else if (_jumpMode.SelectedIndex == 3) sb.AppendFormat(ci, " --offset {0:0.###}", _jumpValue.Value);

            string[] clickNames = { "none", "all", "down", "up", "every4" };
            if (_click.SelectedIndex != 2) sb.Append(" --click ").Append(clickNames[_click.SelectedIndex]);
            if (_countIn.Value != 8m) sb.AppendFormat(ci, " --count-in {0:0}", _countIn.Value);
            if (_tail.Value != 1.5m) sb.AppendFormat(ci, " --tail {0:0.#}", _tail.Value);

            sb.Append(" --out \"").Append(_outDir.Text.Trim()).Append('"');
            if (_zip.Checked) sb.Append(" --zip");

            return sb.ToString();
        }

        // ---------------------------------------------------------------- settings

        /// <summary>
        /// 前回の指定を戻す。単位のような「選び直すのが面倒な項目」が
        /// 起動のたびに既定に戻ると、毎回同じ操作をやり直すことになる。
        /// </summary>
        private void LoadSettings()
        {
            _settings.Load();
            _loading = true;
            try
            {
                _interval.Value = LoadIntervalBpm();
                _seconds.Value = _settings.GetDecimal("seconds", 30, _seconds.Minimum, _seconds.Maximum);

                _direction.SelectedIndex = _settings.GetInt("direction", 0, 0, _direction.Items.Count - 1);
                _hands.SelectedIndex = _settings.GetInt("hands", 0, 0, _hands.Items.Count - 1);
                _order.SelectedIndex = _settings.GetInt("order", 0, 0, _order.Items.Count - 1);
                _sets.Value = _settings.GetDecimal("sets", 1, _sets.Minimum, _sets.Maximum);
                _mirror.Checked = _settings.GetBool("mirror", false);

                _njs.Value = _settings.GetDecimal("njs", 16, _njs.Minimum, _njs.Maximum);
                _jumpMode.SelectedIndex = _settings.GetInt("jump.mode", 0, 0, _jumpMode.Items.Count - 1);
                ApplyJumpModeRanges();
                _jumpValue.Value = _settings.GetDecimal("jump.value", _jumpValue.Value, _jumpValue.Minimum, _jumpValue.Maximum);

                _click.SelectedIndex = _settings.GetInt("click", 2, 0, _click.Items.Count - 1);
                _countIn.Value = _settings.GetDecimal("countIn", 8, _countIn.Minimum, _countIn.Maximum);
                _tail.Value = _settings.GetDecimal("tail", 1.5m, _tail.Minimum, _tail.Maximum);


                PopulateOutputPresets();
                SelectOutputPreset(_settings.GetString("out.dir", DefaultOutputDirectory()));
                _zip.Checked = _settings.GetBool("out.zip", false);
                // 以前の既定（ユーザーフォルダ）が保存されていたら、アプリ配下に移す。
                string savedWorkspace = _settings.GetString("paths.workspace", Workspace.DefaultRoot());
                if (savedWorkspace.IndexOf("AppData", StringComparison.OrdinalIgnoreCase) >= 0)
                    savedWorkspace = Workspace.DefaultRoot();
                _workspace.Text = savedWorkspace;

                _seqText.Text = _settings.GetString("seq", "R8b");
                _lastPreviewFolder = _settings.GetString("preview.dir", "");
                _picker.SetFrom(SafeParse(_seqText.Text));
            }
            finally { _loading = false; }
        }

        /// <summary>
        /// 保存してある間隔を BPM として読む。
        /// </summary>
        /// <remarks>
        /// 単位を選べた頃の設定が残っていると、ms や EBPM で書かれた数値を
        /// そのまま BPM として読んでしまう（300ms のつもりが 300BPM）。
        /// 古い <c>interval.unit</c> が残っていたら、その単位で ms に直してから
        /// 1/2 の BPM に換算する。読めたら新しい形で上書きされるので、移行は1度だけ。
        /// </remarks>
        private decimal LoadIntervalBpm()
        {
            decimal saved = _settings.GetDecimal("interval", 150, 1m, 100000m);

            // 0=ms / 1=EBPM / 2=BPM。無ければ新しい形（BPM）とみなす
            int unit = _settings.GetInt("interval.unit", -1, -1, 2);
            if (unit >= 0)
            {
                double ms;
                if (unit == 0)
                {
                    ms = (double)saved;
                }
                else if (unit == 1)
                {
                    ms = Tempo.HandIntervalMsFromEbpm((double)saved);
                }
                else
                {
                    // 旧 BPM 指定。そのときの細分化で ms に戻す
                    double[] divValues = { 1, 2, 3, 4, 6, 8 };
                    int div = _settings.GetInt("interval.div", 0, 0, divValues.Length - 1);
                    ms = Tempo.IntervalMsFromBpm((double)saved, divValues[div]);
                }

                saved = (decimal)Math.Round(Tempo.BpmFromIntervalMs(ms, NotesPerBeat));

                // 換算し終えたら古い項目を落とす。残したままだと
                // 次の起動でもう一度換算が走って値が二重に動く
                _settings.Remove("interval.unit");
                _settings.Remove("interval.unit.version");
                _settings.Remove("interval.div");
                _settings.Remove("bpm");
            }

            return Clamp(saved, _interval.Minimum, _interval.Maximum);
        }

        private static List<HandSequence> SafeParse(string spec)
        {
            try { return SequenceParser.ParseAll(spec); }
            catch { return new List<HandSequence>(); }
        }

        private void SaveSettings()
        {
            _settings.Set("interval", _interval.Value);
            _settings.Set("seconds", _seconds.Value);

            _settings.Set("direction", _direction.SelectedIndex);
            _settings.Set("hands", _hands.SelectedIndex);
            _settings.Set("order", _order.SelectedIndex);
            _settings.Set("sets", _sets.Value);
            _settings.Set("mirror", _mirror.Checked);

            _settings.Set("njs", _njs.Value);
            _settings.Set("jump.mode", _jumpMode.SelectedIndex);
            _settings.Set("jump.value", _jumpValue.Value);

            _settings.Set("click", _click.SelectedIndex);
            _settings.Set("countIn", _countIn.Value);
            _settings.Set("tail", _tail.Value);


            _settings.Set("out.dir", _outDir.Text);
            _settings.Set("out.zip", _zip.Checked);
            _settings.Set("paths.workspace", _workspace.Text);
            _settings.Set("seq", _seqText.Text);
            _settings.Set("preview.dir", _lastPreviewFolder ?? "");
            _settings.Set("lang", Lang.English ? "en" : "ja");

            _settings.Save();
        }

        // ---------------------------------------------------------------- output

        /// <summary>
        /// マイドキュメントは OneDrive にリダイレクトされていることが多く、
        /// 何本も作ると ogg が丸ごとクラウド同期に乗る。ローカル固定の場所に置く。
        /// </summary>
        /// <summary>今の作業フォルダ。空なら既定に戻す。</summary>
        private Workspace CurrentWorkspace()
        {
            string root = _workspace.Text.Trim();
            return new Workspace(root.Length == 0 ? Workspace.DefaultRoot() : root);
        }

        private void BrowseWorkspace()
        {
            using (var dialog = new FolderBrowserDialog { SelectedPath = _workspace.Text })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _workspace.Text = dialog.SelectedPath;
            }
        }

        private static string DefaultOutputDirectory()
        {
            return Workspace.Default().OutFolder;
        }
        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog { SelectedPath = _outDir.Text })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _outDir.Text = dialog.SelectedPath;
            }
        }

        private string ResolveOutputRoot()
        {
            string dir = _outDir.Text.Trim();
            if (dir.Length == 0) throw new InvalidOperationException(Lang.T("出力先が空です。", "The output folder is empty."));
            return Path.GetFullPath(dir);
        }

        /// <summary>
        /// 今の出力先を SongCore の独立パックとして登録する。
        /// WIP のままにしておけばスコアは送信されず、曲一覧では専用パックとして並ぶ。
        /// プレイリストは WIP を解決できないので、まとめ方としてはこれが本筋。
        /// </summary>
        private void RegisterAsPack()
        {
            try
            {
                // パックはインストールの外（作業フォルダの下）に置き、
                // 見つかった全ての Beat Saber から参照させる。
                // 中に置くと BSManager がインスタンスを作り直したときに消える。
                string name = PromptPackName();
                if (name == null) return;

                string folder = CurrentWorkspace().PackFolder(name);
                var results = SongCoreFolders.RegisterEverywhere(name, folder);

                if (results.Count == 0)
                {
                    MessageBox.Show(this,
                        Lang.T("Beat Saber のインストールが見つかりませんでした。", "No Beat Saber installation was found."),
                        "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var lines = new StringBuilder();
                lines.AppendLine(Lang.T("パック「" + name + "」", "Pack \"" + name + "\""));
                lines.AppendLine(folder).AppendLine();
                foreach (var result in results)
                    lines.Append(result.Added ? Lang.T("登録: ", "Registered: ") : Lang.T("登録済み: ", "Already registered: ")).AppendLine(result.InstallName);
                lines.AppendLine().Append(Lang.T("Beat Saber を起動し直すと曲一覧に出ます。", "Restart Beat Saber to see it in the song list."));

                MessageBox.Show(this, lines.ToString(), "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Information);

                PopulateOutputPresets();
                SelectOutputPreset(folder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>パック名を訊く。既定は JumpDrill。</summary>
        private string PromptPackName()
        {
            using (var dialog = new Form())
            {
                dialog.Text = Lang.T("パック名", "Pack name");
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ClientSize = new Size(360, 108);
                dialog.Font = Font;

                var box = new TextBox { Text = "JumpDrill", Left = 12, Top = 34, Width = 336 };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 192, Top = 70, Width = 75 };
                var cancel = new Button { Text = Lang.T("キャンセル", "Cancel"), DialogResult = DialogResult.Cancel, Left = 273, Top = 70, Width = 75 };

                dialog.Controls.Add(new Label { Text = Lang.T("曲一覧に出すパックの名前", "Pack name shown in the song list"), Left = 12, Top = 12, AutoSize = true });
                dialog.Controls.Add(box);
                dialog.Controls.Add(ok);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;

                if (dialog.ShowDialog(this) != DialogResult.OK) return null;

                string name = box.Text.Trim();
                return name.Length == 0 ? null : name;
            }
        }

        /// <summary>見つかった CustomLevels を選択肢に並べる。先頭は常に手動指定。</summary>
        private void PopulateOutputPresets()
        {
            _presetPaths.Clear();
            _outPreset.Items.Clear();

            _presetPaths.Add(null);
            _outPreset.Items.Add(Lang.T("手動指定", "Custom folder"));

            foreach (var folder in LevelWriter.FindLevelFolders())
            {
                _presetPaths.Add(folder.Path);
                _outPreset.Items.Add(DescribeLevelFolder(folder));
            }

            // WIP が見つかればそれを既定にする。CustomLevels に置くと
            // ScoreSaber などに自作ドリルのスコアが上がってしまう。
            _outPreset.SelectedIndex = _presetPaths.Count > 1 ? 1 : 0;
        }

        /// <summary>
        /// パスをそのまま並べると長すぎて読めないので、どのインストールかと
        /// 置き場所の種類だけを出す。スコアが上がるかどうかが一番効くので明記する。
        /// </summary>
        private static string DescribeLevelFolder(LevelWriter.LevelFolder folder)
        {
            // 幅が足りないと後ろが切れるので、効く情報から先に置く。
            switch (folder.Kind)
            {
                case LevelWriter.LevelFolderKind.Pack:
                    return Lang.T("専用パック「" + folder.PackName + "」・スコア送信なし   ",
                                  "Pack \"" + folder.PackName + "\" · no score upload   ") + folder.InstallName;
                case LevelWriter.LevelFolderKind.Wip:
                    return Lang.T("WIP・スコア送信なし   ", "WIP · no score upload   ") + folder.InstallName;
                default:
                    return Lang.T("通常・スコアが上がる   ", "Custom · scores are uploaded   ") + folder.InstallName;
            }
        }

        /// <summary>保存しておいた出力先に一致する選択肢があればそれを選ぶ。</summary>
        private void SelectOutputPreset(string path)
        {
            for (int i = 1; i < _presetPaths.Count; i++)
            {
                if (string.Equals(_presetPaths[i], path, StringComparison.OrdinalIgnoreCase))
                {
                    _outPreset.SelectedIndex = i;
                    _outDir.Text = path;
                    ApplyOutputPreset();
                    return;
                }
            }

            _outPreset.SelectedIndex = 0;
            _outDir.Text = path;
            ApplyOutputPreset();
        }

        /// <summary>
        /// 選択肢を選んだらその場所を出力先に入れ、手で触れないようにする。
        /// 手動指定のときだけテキスト欄と参照ボタンを開ける。
        /// </summary>
        private void ApplyOutputPreset()
        {
            int index = _outPreset.SelectedIndex;
            bool manual = index <= 0;

            _outDir.ReadOnly = !manual;
            _browse.Enabled = manual;

            if (!manual && index < _presetPaths.Count)
                _outDir.Text = _presetPaths[index];

            _presetTip.SetToolTip(_outPreset, _outDir.Text);
            RefreshPreview();
        }

        private async Task GenerateAsync()
        {
            DrillOptions options;
            string root;

            try
            {
                root = ResolveOutputRoot();
                options = BuildOptions(ToIntervalMs(_interval.Value));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _generate.Enabled = false;
            _openOut.Enabled = false;
            _status.Text = Lang.T("生成中...", "Generating...");

            try
            {
                // ogg のエンコードに数百 ms かかるので、UI を止めないよう別スレッドに出す。
                var writeOptions = new LevelWriteOptions
                {
                    ZipDirectory = _zip.Checked ? CurrentWorkspace().ZipFolder : null,
                };
                string folder = await Task.Run(() => Run(options, root, writeOptions));
                _lastOutputFolder = folder;
                _status.Text = _zip.Checked && LevelWriter.LastZipPath != null
                    ? string.Format(CultureInfo.CurrentCulture, Lang.T("書き出しました: {0}   zip: {1}", "Written: {0}   zip: {1}"),
                        folder, Path.GetDirectoryName(LevelWriter.LastZipPath))
                    : string.Format(CultureInfo.CurrentCulture, Lang.T("書き出しました: {0}", "Written: {0}"), folder);
                _openOut.Enabled = true;
            }
            catch (Exception ex)
            {
                _status.Text = Lang.T("失敗しました。", "Failed.");
                MessageBox.Show(this, ex.Message, "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _generate.Enabled = true;
            }
        }

        private async Task GenerateBulkAsync()
        {
            string root;
            try
            {
                root = ResolveOutputRoot();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _generate.Enabled = false;
            _bulk.Enabled = false;
            _openOut.Enabled = false;
            _status.Text = Lang.T("一括生成中...", "Generating all...");

            try
            {
                var writeOptions = new LevelWriteOptions
                {
                    ZipDirectory = _zip.Checked ? CurrentWorkspace().ZipFolder : null,
                };
                var progress = new Progress<string>(text => _status.Text = text);
                IProgress<string> report = progress;

                await Task.Run(() => DrillSetWriter.WriteAll(root, writeOptions,
                    (done, total, name) => report.Report(string.Format(CultureInfo.CurrentCulture,
                        Lang.T("一括生成中... {0}/{1}  {2}", "Generating all... {0}/{1}  {2}"), done, total, name))));

                _lastOutputFolder = root;
                _status.Text = string.Format(CultureInfo.CurrentCulture, Lang.T("一括生成が完了しました: {0}", "All drills written: {0}"), root);
                _openOut.Enabled = true;
            }
            catch (Exception ex)
            {
                _status.Text = Lang.T("失敗しました。", "Failed.");
                MessageBox.Show(this, ex.Message, "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _generate.Enabled = true;
                _bulk.Enabled = true;
            }
        }

        private static string Run(DrillOptions options, string root, LevelWriteOptions writeOptions)
        {
            options.Name = DrillNaming.Compose(options);
            return LevelWriter.Write(DrillGenerator.Generate(options), root, null, writeOptions);
        }

        /// <summary>
        /// 開いているスコア画面。1つだけ持つ。
        ///
        /// スコアとメダルはどちらも<b>モードレス</b>。メダル画面でマスを選ぶと
        /// スコア画面がその譜面に切り替わるので、並べて見比べられる。
        /// 片方をモーダルにすると、もう片方が触れなくなる。
        /// </summary>
        private ScoreboardForm _scoreboardForm;

        /// <summary>開いているメダル画面。1つだけ持つ。</summary>
        private MedalForm _medalForm;

        /// <summary>叩いたリプレイから再現性を出して並べる。</summary>
        private void ShowScoreboard()
        {
            OpenScoreboard(null);
        }

        /// <summary>スコア画面を、指定した譜面を選んだ状態で開く。開いていれば前に出して選び直す。</summary>
        private void OpenScoreboard(string id)
        {
            if (_scoreboardForm != null)
            {
                if (id != null) _scoreboardForm.SelectMap(id);
                if (_scoreboardForm.WindowState == FormWindowState.Minimized) _scoreboardForm.WindowState = FormWindowState.Normal;
                _scoreboardForm.Activate();
                return;
            }

            var form = new ScoreboardForm(CurrentWorkspace().Root, _settings, id);
            form.FormClosed += (s, e) => _scoreboardForm = null;
            _scoreboardForm = form;
            form.Show(this);
        }

        /// <summary>開いているスコア画面だけ、指定した譜面に切り替える。閉じていれば何もしない。</summary>
        private void FollowScoreboard(string id)
        {
            if (_scoreboardForm != null) _scoreboardForm.SelectMap(id);
        }

        /// <summary>一括生成のドリルのメダルと Lv。</summary>
        private void ShowMedals()
        {
            if (_medalForm != null)
            {
                if (_medalForm.WindowState == FormWindowState.Minimized) _medalForm.WindowState = FormWindowState.Normal;
                _medalForm.Activate();
                return;
            }

            var form = new MedalForm(CurrentWorkspace().Root, _settings, PreviewByName, OpenScoreboard, FollowScoreboard);
            form.FormClosed += (s, e) => _medalForm = null;
            _medalForm = form;
            form.Show(this);
        }

        /// <summary>
        /// ドリルを選んで ArcViewer（web）で開く。叩く前に配置と矢印を目で確かめるため。
        ///
        /// 相手は<b>自分が作ったドリル</b>だけなので、フォルダを辿らせない。
        /// JumpDrill が書く先（作業フォルダの out・いまの出力先・
        /// CustomLevels / CustomWIPLevels / 登録した専用パック）を全部見て、
        /// 見つかったドリルを新しい順に並べて選ばせる。
        ///
        /// 譜面は <c>?url=</c> で URL から読ませられる。BeatSaver に上げていない
        /// 自作の譜面でも、その場で zip にしてローカルの HTTP サーバから配れば通る。
        ///
        /// <b><c>noProxy=true</c> は必須。</b>ArcViewer は既定で URL を
        /// <c>cors.bsmg.dev</c> の CORS プロキシ越しに取りに行くので、
        /// 外から見えない 127.0.0.1 のサーバには永久に届かない。
        /// こちらは <c>Access-Control-Allow-Origin: *</c> を返しているのでプロキシは要らない。
        ///
        /// 難易度も渡して選択画面を飛ばす（JumpDrill が書くのは Standard の ExpertPlus だけ）。
        /// </summary>
        private void OpenInArcViewer()
        {
            string folder = ChooseDrill();
            if (folder == null) return;

            Preview(this, folder);
        }

        /// <summary>譜面フォルダを ArcViewer で開く。</summary>
        private void Preview(IWin32Window owner, string folder)
        {
            try
            {
                _lastPreviewFolder = folder;
                string zip = LevelZipWriter.Write(folder, CurrentWorkspace().ZipFolder);

                if (_server == null) _server = new LocalFileServer();
                string mapUrl = _server.Publish(zip);

                string url = "https://allpoland.github.io/ArcViewer/?url=" + Uri.EscapeDataString(mapUrl)
                           + "&noProxy=true&mode=Standard&difficulty=ExpertPlus";

                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                _status.Text = Lang.T("ArcViewer で開きました: ", "Opened in ArcViewer: ") + Path.GetFileName(folder);
            }
            catch (Exception ex)
            {
                _status.Text = Lang.T("プレビューを開けませんでした。", "Could not open the preview.");
                MessageBox.Show(owner, ex.Message, "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>曲名から譜面を探して ArcViewer で開く。メダル画面から呼ぶ。</summary>
        private void PreviewByName(IWin32Window owner, string songName)
        {
            string folder = DrillLibrary.FindByName(songName, true, CurrentWorkspace().Root, _outDir.Text.Trim(), _lastOutputFolder);
            if (folder == null)
            {
                MessageBox.Show(owner,
                    Lang.T("譜面が見つかりません。［ドリル一括生成］で生成してください。", "Map not found. Create it with [Generate all drills]."),
                    "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Preview(owner, folder);
        }

        /// <summary>プレビューするドリルを選ばせる。取り消したら null。</summary>
        private string ChooseDrill()
        {
            var drills = DrillLibrary.Find(CurrentWorkspace().Root, _outDir.Text.Trim(), _lastOutputFolder);

            if (drills.Count == 0)
            {
                MessageBox.Show(this,
                    Lang.T("JumpDrill が作った譜面が見つかりませんでした。", "No maps made by JumpDrill were found.") + Environment.NewLine +
                    Lang.T("先に生成するか、消したり移したりしていないか確認してください。", "Generate one first, or check that it was not deleted or moved."),
                    "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            return ChooseFromList(drills);
        }

        /// <summary>見つかったドリルを新しい順に並べて選ばせる。取り消したら null。</summary>
        private string ChooseFromList(List<DrillLibrary.DrillLevel> drills)
        {
            using (var form = new Form
            {
                Text = Lang.T("JumpDrill — どのドリルを見ますか", "JumpDrill — choose a drill"),
                Font = Font,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(720, 420),
            })
            {
                var list = new ListView
                {
                    Dock = DockStyle.Fill,
                    View = View.Details,
                    FullRowSelect = true,
                    MultiSelect = false,
                    HideSelection = false,
                };
                list.Columns.Add(Lang.T("譜面", "Map"), 380);
                list.Columns.Add(Lang.T("置き場所", "Location"), 150);
                list.Columns.Add(Lang.T("書き出し", "Written"), 120);

                foreach (var drill in drills)
                {
                    var item = new ListViewItem(DrillNaming.StripId(drill.SongName));
                    item.SubItems.Add(drill.Where);
                    item.SubItems.Add(drill.Written == DateTime.MinValue
                        ? "" : drill.Written.ToString("yy/MM/dd HH:mm", CultureInfo.InvariantCulture));
                    item.Tag = drill;
                    list.Items.Add(item);
                }

                // 前に見たものがあればそれ、無ければいちばん新しいもの。
                int start = 0;
                for (int i = 0; i < drills.Count; i++)
                    if (string.Equals(drills[i].Path, _lastPreviewFolder, StringComparison.OrdinalIgnoreCase)) start = i;

                list.Items[start].Selected = true;
                list.Items[start].EnsureVisible();

                var ok = new Button { Text = Lang.T("開く", "Open"), DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(16, 4, 16, 4) };
                var cancel = new Button { Text = Lang.T("やめる", "Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(16, 4, 16, 4) };
                list.DoubleClick += (s, e) => { if (list.SelectedItems.Count > 0) form.DialogResult = DialogResult.OK; };

                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    AutoSize = true,
                };
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);

                var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Controls.Add(list, 0, 0);
                root.Controls.Add(buttons, 0, 1);
                form.Controls.Add(root);

                form.AcceptButton = ok;
                form.CancelButton = cancel;
                Theme.Apply(form);

                if (form.ShowDialog(this) != DialogResult.OK || list.SelectedItems.Count == 0) return null;
                return ((DrillLibrary.DrillLevel)list.SelectedItems[0].Tag).Path;
            }
        }

        private void OpenOutputFolder()
        {
            string target = _lastOutputFolder;
            if (string.IsNullOrEmpty(target) || !Directory.Exists(target)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + target + "\"") { UseShellExecute = true });
        }
    }
}
