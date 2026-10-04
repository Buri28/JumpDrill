using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
    /// 自作ドリルのローカルリーダーボード。
    ///
    /// 主指標は<b>再現精度 %</b>。振りの綺麗さ（再現、100点満点）に本数を掛けて、
    /// <b>譜面が要求する振りの数</b>で割ったもの。<b>順位はこれで付ける。</b>
    ///
    /// <b>再現</b>は往路と復路のずれを RMS で取り、跳び幅で割って 100点満点に載せたもの。
    /// <b>割合ではないので % とは書かない。</b>
    /// <b>行きと帰りが同じ曲線に重なれば 100%</b>。振りは手首の弧で必ず湾曲するので、
    /// 曲がっていること自体は減点しない。
    /// ただし<b>本数を見ない</b>ので、これだけで並べると
    /// 少ししか振らずに崩れる前にやめた記録が上位に出る。
    /// 角度点 (100) と中心点 (15) は本体と同じ出し方の素点で、
    /// 形が揃っていても当て方が雑なときに落ちる。
    /// 一覧は両手まとめ、左右の内訳は軌道の右の表で見る。
    ///
    /// 一覧は<b>2段</b>。上が譜面（1行1譜面）、下がその譜面のプレイ。
    /// 上で譜面を選ぶと下がその譜面の記録に入れ替わる。
    /// 同じ譜面を何度も叩くと同じ行が並んで伸びるだけなので、上は<b>ベストだけ</b>にして、
    /// 何回目にどれだけ伸びたかは下で見る。
    /// </summary>
    public sealed class ScoreboardForm : Form
    {
        /// <summary>1段目。譜面ごとに1行。ここで選んだ譜面のプレイが<see cref="_plays"/>に出る。</summary>
        private readonly ListView _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            MultiSelect = false,
        };

        /// <summary>
        /// 2段目。1段目で選んだ譜面のプレイを並べる。既定は<b>再現精度 % の高い順</b>。
        ///
        /// 既定が日付順ではなく順位順なのは、伸びたかどうかを知りたいのは「今までの自分より上に行けたか」
        /// なので、上から読めばそれが分かるから。何回目かは「回目」の列に出る。
        /// 見出しを押すとその列で並べ替える（<see cref="SortPlays"/>）。
        /// </summary>
        private readonly ListView _plays = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            MultiSelect = false,
        };

        private readonly Label _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        private readonly Button _rescan = new Button { Text = Lang.T("再スキャン", "Rescan"), AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        private readonly Button _openReplay = new Button { Text = Lang.T("リプレイの場所を開く", "Open replay folder"), AutoSize = true, Padding = new Padding(10, 4, 10, 4), Enabled = false };
        private readonly Button _openZip = new Button { Text = Lang.T("zip を用意して開く", "Prepare zip and open"), AutoSize = true, Padding = new Padding(10, 4, 10, 4), Enabled = false };
        private readonly Button _openBeatLeader = new Button { Text = Lang.T("BeatLeader でリプレイ", "Replay in BeatLeader"), AutoSize = true, Padding = new Padding(10, 4, 10, 4), Enabled = false };
        private LocalFileServer _server;

        private readonly ReplayView _view = new ReplayView { Dock = DockStyle.Fill };

        /// <summary>
        /// 横から見た軌道。正面図では奥行きが潰れるので、PRE / POST はそこからは読めない。
        /// 打点の面を縦線で出して、その手前の伸びが振りかぶり、先の伸びが振り抜きになる。
        /// </summary>
        private readonly ReplayView _sideView = new ReplayView
        {
            Dock = DockStyle.Fill,
            Axis = ReplayView.ViewAxis.Side,
        };
        private readonly ComboBox _hand = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        // 既定で平均だけ。束を全部出すと向きの印が埋もれて読めない。
        private readonly CheckBox _meanOnly = new CheckBox { Text = Lang.T("平均だけ", "Mean only"), AutoSize = true, Checked = true };
        private readonly CheckBox _notesAtCuts = new CheckBox { Text = Lang.T("ノーツを打点に合わせる", "Align notes to hits"), AutoSize = true };

        /// <summary>横から見た図の左右。既定はプレイヤーが右を向いた形。</summary>
        private readonly CheckBox _faceLeft = new CheckBox { Text = Lang.T("横図を反転", "Flip side view"), AutoSize = true };
        private readonly Label _viewInfo = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };

        /// <summary>
        /// 選んだ行の内訳。一覧は両手まとめなので、左右とフォア／バックの差はここで見る。
        ///
        /// <b>フォアとバックを分ける。</b>往復ドリルは行きと帰りで別の振りなので、
        /// 平均すると両方の癖が打ち消し合う。実測でも、角度ずれ 左 24.7° の内訳が
        /// フォア 12.4° / バック 37.0° で、直すべきなのは片側だけだった。
        /// 手ごとのまとめも左端に残す。フォアとバックだけだと、その手全体でどうなのかを
        /// 足し戻しながら読むことになる。
        /// 往復で比べて出す欄（再現 /100・往復ずれ）は片側では定義できないので、
        /// まとめの列にだけ数字を置いて、フォアとバックは「—」にする。
        ///
        /// <b>当たったノーツだけで出した数字しか置かない</b>（Miss の件数だけは例外）。
        /// 満点を譜面のノーツ数に取る欄（再現精度 %・精度 %）を混ぜると、
        /// 同じ列に分母の違う割合が並ぶ。実測でも、右手が 総合 97.1/115 なのに
        /// 精度 84.5% と出て、当て方が悪いのか振らなかっただけなのか読めなかった。
        /// 振らなかったぶんの話は Miss の行と一覧の 再現精度 % で足りる。
        /// </summary>
        private readonly ListView _detail = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            MultiSelect = false,
        };

        private readonly List<ReplayScore> _scores = new List<ReplayScore>();

        /// <summary>譜面ごとにまとめた記録。一覧の 1 行がこの 1 つ。</summary>
        private readonly List<MapEntry> _maps = new List<MapEntry>();

        /// <summary>組み直している間は選択の変化を無視する（組み直しの中でまた組み直さない）。</summary>
        private bool _rebuilding;

        /// <summary>いま軌道と内訳に出しているプレイ。2段目で選べばベスト以外にもなる。</summary>
        private ReplayScore _shown;

        /// <summary>いま一覧で選んでいる譜面。</summary>
        private MapEntry _shownMap;

        /// <summary>2段目を入れ替えている間は、そこの選択の変化を無視する。</summary>
        private bool _fillingPlays;

        /// <summary>2段目を並べている列。0 が順位。譜面を選び直しても引き継ぐ。</summary>
        private int _playsSortColumn;

        /// <summary>2段目を大きい順（新しい順）に並べているか。</summary>
        private bool _playsSortDescending;

        /// <summary>プレイ日時。並べ替えのたびにファイルを叩かないよう覚えておく。</summary>
        private readonly Dictionary<ReplayScore, DateTime> _playedAt = new Dictionary<ReplayScore, DateTime>();

        private readonly string _workspaceRoot;

        /// <summary>同じ譜面のプレイをまとめたもの。</summary>
        private sealed class MapEntry
        {
            /// <summary>記録をまとめる鍵。譜面名の頭の ID。</summary>
            public string Key;

            /// <summary>一覧に出す ID。ドリル以外は空。</summary>
            public string Id;

            /// <summary>ID を落とした表示用の譜面名。</summary>
            public string Name;

            /// <summary>古い順。並びがそのまま「何回目」になる。</summary>
            public readonly List<ReplayScore> Plays = new List<ReplayScore>();

            /// <summary>この譜面のベスト。</summary>
            public ReplayScore Best;

            /// <summary>いちばん新しいプレイの日時。譜面の並び順に使う。</summary>
            public DateTime Latest;
        }

        /// <summary>
        /// 読み込んだあとで選ぶ譜面の ID。メダル画面から開いたときと、
        /// 読み込み中にメダル画面で選び直されたときに使う。
        /// </summary>
        private string _initialId;

        /// <summary>
        /// リプレイを読んでいる最中か（最初に読み終わるまでも含む）。
        /// この間は一覧が空なので、選ぶ譜面は控えておく。
        /// </summary>
        private bool _scanning = true;

        /// <summary>
        /// 指定した ID の譜面を選ぶ。メダル画面でマスを選んだときに呼ぶ。
        /// 読み込み前・読み込み中なら、読み終わってから選ぶ。一覧に無ければ（まだ叩いていない、
        /// 開いたあとに叩いた）選択はそのまま。
        /// </summary>
        public void SelectMap(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_scanning)
            {
                _initialId = id;
                return;
            }
            if (_shownMap != null && _shownMap.Key == id) return;

            foreach (ListViewItem item in _list.Items)
            {
                if (((MapEntry)item.Tag).Key != id) continue;

                // 選択の合図は2回飛ぶ（外れる・付く）ので、止めてから1回だけ出し直す。
                _rebuilding = true;
                _list.SelectedItems.Clear();
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
                _rebuilding = false;
                ShowMap(SelectedMap);
                return;
            }
        }

        /// <param name="selectId">開いたときに選んでおく譜面の ID。null なら先頭。</param>
        /// <summary>記録を読む Beat Saber。</summary>
        private InstallPicker _install;

        /// <summary>読んでいる間に見る版が変わった。読み終えたら読み直す。</summary>
        private bool _reloadPending;

        internal ScoreboardForm(string workspaceRoot, SettingsStore settings, string selectId = null)
        {
            _workspaceRoot = workspaceRoot;
            _install = new InstallPicker(settings) { Margin = new Padding(3, 4, 8, 3) };
            _initialId = selectId;

            Text = Lang.T("JumpDrill — スコア", "JumpDrill — Scores");
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Yu Gothic UI", 9f);
            MinimumSize = new Size(980, 560);
            // 出だしの大きさ。幅は記録を読んだあと、中身の実寸に合わせ直す
            // （FitWindowWidth）。ここは合わせ直しで見た目が飛ばない程度の近さでいい。
            Size = new Size(1700, 980);
            // Show（モードレス）では CenterParent が効かないので、Load で親の中央に置く。
            StartPosition = FormStartPosition.Manual;
            Load += (s, e) => CenterOnOwner();

            BuildLayout();
            Theme.Apply(this);
            _list.SelectedIndexChanged += (s, e) => OnSelectionChanged();
            _hand.SelectedIndexChanged += (s, e) => ApplyViewOptions();
            _meanOnly.CheckedChanged += (s, e) => ApplyViewOptions();
            _faceLeft.CheckedChanged += (s, e) => ApplyViewOptions();

            // 置き場所が変わると読み直しが要る。
            _notesAtCuts.CheckedChanged += (s, e) =>
            {
                _view.AnchorNotesToCuts = _notesAtCuts.Checked;
                _sideView.AnchorNotesToCuts = _notesAtCuts.Checked;
                ShowPlay(_shown);
            };
            _rescan.Click += async (s, e) => await ScanAsync();
            _openReplay.Click += (s, e) => OpenReplayLocation();
            _openZip.Click += (s, e) => OpenZip();
            _openBeatLeader.Click += (s, e) => OpenInBeatLeader();
            _list.DoubleClick += (s, e) => OpenInBeatLeader();
            _plays.SelectedIndexChanged += (s, e) => OnPlayPicked();
            _plays.ColumnClick += (s, e) => SortPlays(e.Column);
            _plays.DoubleClick += (s, e) => OpenInBeatLeader();
            // 見る版を変えたら読み直す（メダル画面で変えたときも）
            // 読んでいる最中なら、読み終えてからもう一度読む
            Action reload = async () =>
            {
                if (IsDisposed) return;
                if (!_rescan.Enabled) { _reloadPending = true; return; }
                await ScanAsync();
            };
            InstallPicker.Changed += reload;
            Load += (s, e) => _install.FitWidth(DeviceDpi);

            FormClosed += (s, e) =>
            {
                InstallPicker.Changed -= reload;
                if (_server != null) _server.Dispose();
            };

            Shown += async (s, e) => await ScanAsync();
        }

        /// <summary>親の中央に置く。はみ出すなら画面の内側へ。</summary>
        private void CenterOnOwner()
        {
            Rectangle area = Screen.FromControl(Owner ?? (Control)this).WorkingArea;
            Rectangle around = Owner != null ? Owner.Bounds : area;
            Size size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            int x = around.Left + (around.Width - size.Width) / 2;
            int y = around.Top + (around.Height - size.Height) / 2;
            Bounds = new Rectangle(
                Math.Max(area.Left, Math.Min(x, area.Right - size.Width)),
                Math.Max(area.Top, Math.Min(y, area.Bottom - size.Height)),
                size.Width, size.Height);
        }

        /// <summary>図と内訳の境目。内訳の列幅が決まってから、1度だけ実測に合わせる。</summary>
        private SplitContainer _lower;

        /// <summary>内訳の幅を一度合わせたか。合わせ直すと、掴んで動かした位置が戻ってしまう。</summary>
        private bool _detailSized;

        /// <summary>絵・内訳と、下の一覧の境目。</summary>
        private SplitContainer _split;

        /// <summary>譜面の一覧とプレイの一覧の境目。</summary>
        private SplitContainer _stages;

        /// <summary>譜面の一覧の幅を一度合わせたか。</summary>
        private bool _mapSized;

        /// <summary>窓の幅を一度合わせたか。以後は掴んで変えてもらう。</summary>
        private bool _windowSized;

        /// <summary>軌道と内訳に残す最低限の高さ。内訳の行が全部入るだけ要る。</summary>
        private const int ViewMinimum = 340;

        /// <summary>一覧（2段ぶん）に残す最低限の高さ。</summary>
        private const int ListMinimum = 140;

        /// <summary>
        /// 仕切りの太さ。掴める幅は要るが、太いと帯として目に入って中身の邪魔になる。
        /// 全部の仕切りで同じにする（場所ごとに違うと、掴める所を探すことになる）。
        /// </summary>
        private const int SplitterThickness = 4;

        /// <summary>
        /// 仕切りの色。<b>明るい側にも暗い側にも乗る中間の灰</b>。
        ///
        /// 既定のままだと親の地の色になり、軌道の図（ほぼ黒）と接する所で
        /// 溶けて見えなくなる。掴める所が見えないと、掴めることに気付けない。
        /// </summary>
        private static readonly Color SplitterColor = Color.FromArgb(0x9A, 0xA2, 0xB0);

        /// <summary>
        /// 仕切りのカーソルを OS の標準（<c>SizeWE</c> / <c>SizeNS</c>）に替える。
        ///
        /// WinForms の既定は <c>VSplit</c> / <c>HSplit</c> で、これは 32 px の
        /// ビットマップを抱えた<b>埋め込みリソース</b>。高 DPI ではそれを引き伸ばすので、
        /// 輪郭が階段状になって矢印の形に見えない。
        /// <c>SizeWE</c> / <c>SizeNS</c> は OS が倍率ごとの絵を持っているので、
        /// どの倍率でも輪郭が立つ。
        ///
        /// 中身の面まで矢印になっては困るので、パネル側は既定に戻しておく
        /// （<see cref="SplitContainer"/> のカーソルは子へ引き継がれる）。
        /// </summary>
        private static void UseSystemSplitterCursor(SplitContainer split)
        {
            split.Cursor = split.Orientation == Orientation.Vertical ? Cursors.SizeWE : Cursors.SizeNS;
            split.Panel1.Cursor = Cursors.Default;
            split.Panel2.Cursor = Cursors.Default;
        }

        /// <summary>譜面の一覧に残す最低限の幅。</summary>
        private const int MapMinimum = 180;

        /// <summary>プレイの一覧に残す最低限の幅。</summary>
        private const int PlaysMinimum = 320;

        /// <summary>正面図に残す最低限の幅。</summary>
        private const int FrontMinimum = 240;

        /// <summary>正面図の既定の幅。ノーツの格子と角度の数字がぶつからずに並ぶ大きさ。</summary>
        private const int FrontDefault = 450;

        /// <summary>
        /// 横から見た図の既定の幅。奥行きは振り1本ぶんしかないので、正面図より狭くていい。
        /// ここを空けた分は内訳に回る（内訳は列が増えても縮まない方が読みやすい）。
        /// </summary>
        private const int SideDefault = 340;

        /// <summary>横から見た図に残す最低限の幅。奥行きは振り1本ぶんしかない。</summary>
        private const int SideMinimum = 140;

        /// <summary>絵の2枚に残す最低限の幅。</summary>
        private const int FiguresMinimum = FrontMinimum + SideMinimum + SplitterThickness;

        /// <summary>絵の2枚の既定の幅。これより広い窓では、余った幅は内訳に回る。</summary>
        private const int FiguresDefault = FrontDefault + SideDefault + SplitterThickness;

        /// <summary>内訳に残す最低限の幅。</summary>
        private const int DetailMinimum = 160;

        /// <summary>
        /// 数字の列。2段目に出す。
        /// 細かい内訳は右の詳細表だけに置く。一覧に入れると横に溢れる。
        /// </summary>
        private static readonly string[] ScoreColumns =
        {
            "再現精度 %", "再現 /100", "精度 %", "総合 /115", "TD", "PRE", "POST",
            "Miss", "長さ",
        };

        /// <summary>2段目の見出し。先頭の3つのあとに <see cref="ScoreColumns"/> が続く。</summary>
        private static readonly string[] PlayColumns =
            new[] { "順位", "回目", "日時" }.Concat(ScoreColumns).ToArray();

        /// <summary>
        /// 見出しに出す文字。<see cref="PlayColumns"/> の文字は並べ替えの目印にも
        /// 使っているので、言語を切り替えても変えずに、出すときだけ訳す。
        /// </summary>
        private static string Caption(string key)
        {
            if (!Lang.English) return key;
            switch (key)
            {
                case "譜面": return "Map";
                case "順位": return "Rank";
                case "回目": return "Play #";
                case "日時": return "Date";
                case "再現精度 %": return "Repro acc %";
                case "再現 /100": return "Repro /100";
                case "精度 %": return "Acc %";
                case "総合 /115": return "Total /115";
                case "長さ": return "Length";
                default: return key;
            }
        }

        private void BuildLayout()
        {
            // 幅は仮。中身を入れてから見出しと数字の実寸に合わせ直す（FitColumns）。
            // 1段目は譜面を選ぶための段。数字は順位を決める再現精度 % だけ置いて、
            // 残りは全部 2段目に回す。並べても比べられない譜面どうしの数字が並ぶだけなので。
            _list.Columns.Add("ID", 56);
            _list.Columns.Add(Caption("譜面"), 240);
            _list.Columns.Add(Caption("再現精度 %"), 70, HorizontalAlignment.Right);

            // 2段目は ID と譜面を除いた全部。どれも同じ譜面の中での比較になる。
            _plays.Columns.Add(Caption("順位"), 52, HorizontalAlignment.Right);
            _plays.Columns.Add(Caption("回目"), 52, HorizontalAlignment.Right);
            _plays.Columns.Add(Caption("日時"), 104);
            foreach (var caption in ScoreColumns) _plays.Columns.Add(Caption(caption), 70, HorizontalAlignment.Right);
            MarkSortedColumn();

            // 左端が両手まとめ、そのあと手ごとに「まとめ → フォア → バック」。
            // 外から内へ絞る並びにしてある。まず全体を見て、次に左右のどちらが、
            // 最後にどちらの振りが、という順で読める。
            _detail.Columns.Add("", 110);
            _detail.Columns.Add(Lang.T("計", "All"), 68, HorizontalAlignment.Right);
            _detail.Columns.Add(Lang.T("左", "L"), 68, HorizontalAlignment.Right);
            _detail.Columns.Add(Lang.T("左 フォア", "L fore"), 68, HorizontalAlignment.Right);
            _detail.Columns.Add(Lang.T("左 バック", "L back"), 68, HorizontalAlignment.Right);
            _detail.Columns.Add(Lang.T("右", "R"), 68, HorizontalAlignment.Right);
            _detail.Columns.Add(Lang.T("右 フォア", "R fore"), 68, HorizontalAlignment.Right);
            _detail.Columns.Add(Lang.T("右 バック", "R back"), 68, HorizontalAlignment.Right);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            actions.Controls.Add(_install);
            actions.Controls.Add(_rescan);
            actions.Controls.Add(_openBeatLeader);
            actions.Controls.Add(_openReplay);
            actions.Controls.Add(_openZip);

            _hand.Items.AddRange(new object[] { Lang.T("両手", "Both hands"), Lang.T("右手だけ", "Right only"), Lang.T("左手だけ", "Left only") });
            _hand.SelectedIndex = 0;

            var viewTools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            viewTools.Controls.Add(_hand);
            viewTools.Controls.Add(_meanOnly);
            viewTools.Controls.Add(_notesAtCuts);
            viewTools.Controls.Add(_faceLeft);

            // 正面図の右に横から見た図。奥行きの幅は振り1本ぶんしかないので、
            // 正面図と同じ幅は要らない。
            var figures = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = SplitterThickness,
                BackColor = SplitterColor,
            };
            UseSystemSplitterCursor(figures);
            figures.Panel1.Controls.Add(_view);
            figures.Panel2.Controls.Add(_sideView);

            // 掴んで動かすまでは、幅が変わるたびに既定へ置き直す。
            // 1度きりにすると、置いた時点の幅で決まってしまう。組み上がる途中の
            // 幅はまだ本当の幅ではない（内訳の幅が決まると、ここも大きく変わる）。
            // 掴んだかどうかは SplitterMoving で見る。SplitterMoved は自分で位置を
            // 入れ直したときにも飛ぶうえ、下限を入れただけでも遅れて飛ぶので、
            // あれを合図にすると「組み上がる途中の幅で固定」に戻ってしまう。
            bool figuresMoved = false;
            figures.SplitterMoving += (s, e) => figuresMoved = true;

            figures.SizeChanged += (s, e) =>
            {
                if (figuresMoved || figures.Width < FrontMinimum + SideMinimum + figures.SplitterWidth) return;

                // 正面図は決め打ちの幅。割合にすると、内訳の列が増えたぶんだけ
                // 絵が痩せていく（列は読みやすさのために増えるので、そのたびに
                // 絵が縮むのは筋が違う）。
                int want = Math.Max(FrontMinimum,
                    Math.Min(FrontDefault, figures.Width - figures.SplitterWidth - SideMinimum));

                figures.Panel1MinSize = FrontMinimum;
                figures.Panel2MinSize = SideMinimum;
                if (figures.SplitterDistance != want) figures.SplitterDistance = want;
            };

            var viewPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            viewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            viewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            viewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            viewPanel.Controls.Add(figures, 0, 0);
            viewPanel.Controls.Add(viewTools, 0, 1);
            viewPanel.Controls.Add(_viewInfo, 0, 2);

            // 軌道の右に内訳を置く。軌道で左右の形を見て、隣で数字を突き合わせる。
            // ここも掴んで動かせるようにする。列が7本あるので、内訳を広げたい日と
            // 絵を広げたい日で要る幅が違う。
            var lower = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = SplitterThickness,
                BackColor = SplitterColor,
                Margin = new Padding(0, 0, 0, 8),
                // 窓を広げた分は絵にあげる。内訳は列が収まっていればいい。
                FixedPanel = FixedPanel.Panel2,
            };
            UseSystemSplitterCursor(lower);
            lower.Panel1.Controls.Add(viewPanel);
            lower.Panel2.Controls.Add(_detail);
            _detail.Margin = new Padding(0);
            _lower = lower;

            // 譜面は左、その譜面のプレイは右。上下に積むと、3列しかない譜面の一覧に
            // 窓の幅を丸ごと渡すことになって、譜面名と 再現精度 % が両端に離れる。
            // 横に並べれば、譜面は必要なぶんだけ、残りは全部プレイの列に回る。
            var stages = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = SplitterThickness,
                BackColor = SplitterColor,
                // 窓を広げた分はプレイの一覧にあげる。譜面は名前が収まっていればいい。
                FixedPanel = FixedPanel.Panel1,
            };
            UseSystemSplitterCursor(stages);
            stages.Panel1.Controls.Add(_list);
            stages.Panel2.Controls.Add(_plays);

            // 記録が少ないうちは一覧に高さを割いても余白になるだけなので、
            // 決め打ちの比率をやめて掴んで動かせるようにする。
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = SplitterThickness,
                BackColor = SplitterColor,
                // 窓を広げた分は軌道にあげる。一覧は必要なだけ見えていればいい。
                FixedPanel = FixedPanel.Panel2,
            };
            UseSystemSplitterCursor(split);

            // 一覧は下のボタンの真上に置く。選んで押す流れなので、離れていると探しに行くことになる。
            split.Panel1.Controls.Add(lower);
            split.Panel2.Controls.Add(stages);
            _split = split;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(split, 0, 0);
            root.Controls.Add(actions, 0, 1);
            root.Controls.Add(_status, 0, 2);

            // 下限も分割位置も、実際の高さが決まってからでないと入れられない。
            // 既定サイズ（高さ100）のまま Panel2MinSize を入れると、その場で例外になる。
            bool placed = false;
            split.SizeChanged += (s, e) =>
            {
                if (placed || split.Height < ViewMinimum + ListMinimum + split.SplitterWidth) return;
                placed = true;

                split.Panel1MinSize = ViewMinimum;
                split.Panel2MinSize = ListMinimum;

                int list = Math.Max(ListMinimum, Math.Min(300, split.Height / 3));
                int room = split.Height - split.SplitterWidth - ListMinimum;
                split.SplitterDistance = Math.Max(ViewMinimum, Math.Min(split.Height - split.SplitterWidth - list, room));
            };

            // 譜面の側は中身が収まる幅まで。決まるのは行が入ってからなので、
            // 幅合わせは RebuildRows から呼ぶ（FitMapWidth）。
            stages.SizeChanged += (s, e) =>
            {
                if (stages.Width < MapMinimum + PlaysMinimum + stages.SplitterWidth) return;

                stages.Panel1MinSize = MapMinimum;
                stages.Panel2MinSize = PlaysMinimum;
            };
            _stages = stages;

            Controls.Add(root);
        }

        /// <summary>
        /// リプレイを読み直す。<b>JumpDrill が作った譜面だけ</b>を対象にする。
        ///
        /// 以前は「JumpDrill の譜面だけ」を外せるようにしていたが、外しても使えなかった。
        ///
        /// 再現性は「同じ2点の間を往復し続ける」前提で出している。往路と復路の平均軌道を
        /// 突き合わせるので、遷移が2組ちょうどでないと往復ずれが出せず 0 が返り、
        /// そのまま<b>再現性 100 点</b>になる（<c>SwingAnalyzer.RoundTripGapOf</c>）。
        /// 普通の曲は遷移が何十組もあるので、叩けば叩くほど満点に近い記録として
        /// 一覧の先頭に並んでしまい、ドリルの記録が押し出される。
        ///
        /// 読み込みも重い。普通の曲は尺もノーツ数もドリルの比ではなく、
        /// リプレイのフレームを全部展開したうえで解析するので、
        /// 数百件あるライブラリだと固まったように見える。
        /// </summary>
        private async Task ScanAsync()
        {
            _rescan.Enabled = false;
            _status.Text = Lang.T("リプレイを読み込んでいます...", "Loading replays...");
            // 読み直しで MapEntry は作り直しになるので、選び直しは鍵で覚えておく。
            // 一覧を空にすると選択が外れるので、その前に控える。
            string keep = _initialId ?? (_shownMap == null ? null : _shownMap.Key);
            _initialId = null;
            _scanning = true;

            _list.Items.Clear();
            _scores.Clear();
            _playedAt.Clear();

            int failed = 0;

            string workspace = _workspaceRoot;

            // ドリル以外は読まない（→ ScanAsync のコメント）。
            string install = InstallPicker.Selected;
            var scores = await Task.Run(() => DrillRecords.Load(workspace, (path, ex) => failed++, install));
            _scores.AddRange(scores);

            GroupByMap();

            // 読んでいる間にメダル画面で選び直されたら、そちらを優先する。
            _scanning = false;
            RebuildRows(_initialId ?? keep);
            _initialId = null;

            int bots = _scores.Count(s => s.Replay.Info.LooksLikeAutoplay);
            int speed = _scores.Count(s => !s.Replay.Info.LooksLikeAutoplay && s.Replay.Info.SongSpeedChanged);

            // 読み方の説明はここには置かない。毎回同じ文言が出続けるだけで、
            // 2度目からは読まない。件数だけ出す。
            _status.Text = string.Format(CultureInfo.CurrentCulture,
                Lang.T("{0} 譜面 / {1} 件{2}{3}{4}", "{0} maps / {1} plays{2}{3}{4}"),
                _maps.Count, _scores.Count,
                bots > 0 ? Lang.T("   (自動プレイ：" + bots + "件)", "   (autoplay: " + bots + ")") : "",
                speed > 0 ? Lang.T("   (速度変更・記録外：" + speed + "件)", "   (speed changed, not counted: " + speed + ")") : "",
                failed > 0 ? Lang.T("   読めなかったもの " + failed + " 件", "   unreadable: " + failed) : "");

            _rescan.Enabled = true;

            if (_reloadPending && !IsDisposed)
            {
                _reloadPending = false;
                await ScanAsync();
            }
        }

        /// <summary>
        /// 同じ譜面のプレイをまとめる。鍵は譜面名の頭の ID（<see cref="DrillRecords.MapIdOf"/>）。
        /// </summary>
        private void GroupByMap()
        {
            _maps.Clear();

            var byKey = new Dictionary<string, MapEntry>(StringComparer.Ordinal);

            foreach (var score in _scores)
            {
                string name = score.Replay.Info.SongName ?? "";
                string id = DrillRecords.MapIdOf(score);

                MapEntry map;
                string key = id ?? name;
                if (!byKey.TryGetValue(key, out map))
                {
                    map = new MapEntry { Key = key, Id = id ?? "", Name = DrillNaming.StripId(name) };
                    byKey.Add(key, map);
                    _maps.Add(map);
                }

                map.Plays.Add(score);
            }

            foreach (var map in _maps)
            {
                map.Plays.Sort((a, b) => PlayedAt(a).CompareTo(PlayedAt(b)));
                map.Best = DrillRecords.PickBest(map.Plays);
                map.Latest = PlayedAt(map.Plays[map.Plays.Count - 1]);
            }

            // 譜面どうしは速さも並びも違って比べられないので、点ではなく直近に叩いた順。
            // いま練習している譜面が上に来る。
            _maps.Sort((a, b) => b.Latest.CompareTo(a.Latest));
        }

        /// <summary>プレイした日時。リプレイの更新時刻で見る。</summary>
        private DateTime PlayedAt(ReplayScore score)
        {
            DateTime when;
            if (_playedAt.TryGetValue(score, out when)) return when;

            try { when = File.GetLastWriteTime(score.Replay.Path); }
            catch (ArgumentException) { when = DateTime.MinValue; }

            _playedAt.Add(score, when);
            return when;
        }

        /// <summary>一覧を組み直す。1 行が 1 譜面で、出すのはその譜面のベスト。</summary>
        /// <param name="selectKey">組み直したあとで選び直す譜面の鍵。null なら先頭。</param>
        private void RebuildRows(string selectKey)
        {
            _rebuilding = true;
            _list.BeginUpdate();
            _list.Items.Clear();

            foreach (var map in _maps) _list.Items.Add(MapRow(map));

            _list.EndUpdate();

            // 3列とも中身の幅まで。余った幅を譜面名に全部渡すと、
            // 譜面名と 再現精度 % が窓の両端に離れて、目で結べなくなる。
            ListColumns.FitAll(_list, 0);

            // 選び直しも組み直しの一部。ここで選択の合図を通すと、
            // 組み直しの最中にまた組み直しが走って、いま触っている行がその場で無効になる。
            foreach (ListViewItem item in _list.Items)
            {
                if (selectKey != null && ((MapEntry)item.Tag).Key != selectKey) continue;

                item.Selected = true;
                item.EnsureVisible();
                break;
            }

            _rebuilding = false;
            ShowMap(SelectedMap);
        }

        /// <summary>1段目の 1 行。譜面ごとのベストを出す。</summary>
        private ListViewItem MapRow(MapEntry map)
        {
            var info = map.Best.Replay.Info;

            // 記録として数えないもの（自動プレイ・速度変更）は、人の記録と同じ濃さで並べない。
            // 自動プレイはヘッドセットが動かないぶん再現性がほぼ満点になり、上位の行として読めてしまう。
            var item = new ListViewItem(map.Id);
            if (!info.CountsAsRecord) item.ForeColor = SystemColors.GrayText;

            item.SubItems.Add((map.Name ?? "") + NotCountedMark(info));
            item.SubItems.Add(map.Best.ReproducibilityPercent.ToString("0.0", CultureInfo.InvariantCulture));

            item.Tag = map;
            return item;
        }

        /// <summary>2段目の 1 行。</summary>
        /// <param name="index">古い方から数えた位置。0 始まり。「何回目か」になる。</param>
        /// <param name="rank">再現精度 % の順位。1 始まり。</param>
        private ListViewItem RankingRow(MapEntry map, int index, int rank)
        {
            var score = map.Plays[index];

            // ★ は一覧に出しているベスト。自動プレイや速度を変えたプレイは順位の上に来ても
            // ベストには選ばないので、1位と ★ がずれることがある。
            var item = new ListViewItem(string.Format(CultureInfo.CurrentCulture,
                "{0}{1}", rank, score == map.Best ? " ★" : ""));

            item.SubItems.Add((index + 1).ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(WhenText(score) + NotCountedMark(score.Replay.Info));

            FillScores(item, score);
            item.Tag = score;
            return item;
        }

        /// <summary>
        /// 2段目を、選んだ譜面のプレイで埋める。<b>選ぶのはベスト</b>。
        ///
        /// 埋めている間は選択の合図を止める。ここで通すと、1段目の選択を処理している
        /// 最中に 2段目の選択が割り込んで、同じプレイを二度出しに行くことになる。
        /// </summary>
        /// <param name="keep">選んでおくプレイ。null ならベスト。並べ替えでは選んでいたものを残す。</param>
        private void FillPlays(MapEntry map, ReplayScore keep = null)
        {
            _fillingPlays = true;
            _plays.BeginUpdate();
            _plays.Items.Clear();

            if (map != null)
            {
                // 順位は再現精度 % の高い順で決まる。どの列で並べても順位の数字は変わらない。
                var byRank = new List<int>();
                for (int i = 0; i < map.Plays.Count; i++) byRank.Add(i);
                byRank.Sort((a, b) => map.Plays[b].ReproducibilityPercent
                    .CompareTo(map.Plays[a].ReproducibilityPercent));

                var rankOf = new Dictionary<int, int>();
                for (int r = 0; r < byRank.Count; r++) rankOf[byRank[r]] = r + 1;

                // 並べる列の値が同じなら順位の順
                var order = new List<int>(byRank);
                order.Sort((a, b) =>
                {
                    int c = SortKey(map, a, rankOf[a]).CompareTo(SortKey(map, b, rankOf[b]));
                    if (_playsSortDescending) c = -c;
                    return c != 0 ? c : rankOf[a].CompareTo(rankOf[b]);
                });

                foreach (int index in order)
                    _plays.Items.Add(RankingRow(map, index, rankOf[index]));
            }

            _plays.EndUpdate();
            ListColumns.FitAll(_plays, 0);
            FitWindowWidth();
            FitMapWidth();

            object wanted = keep != null && map != null && map.Plays.Contains(keep) ? keep : map?.Best;
            foreach (ListViewItem item in _plays.Items)
            {
                if (map != null && item.Tag != wanted) continue;
                item.Selected = true;
                item.EnsureVisible();
                break;
            }

            _fillingPlays = false;
        }

        /// <summary>
        /// 2段目を押した見出しの列で並べ替える。同じ列をもう一度押すと逆順。
        /// </summary>
        /// <remarks>
        /// 初めて押した列は「良い方が上」から始める。順位と Miss は小さい順、
        /// 日時と回目は新しい順、ほかの数字は大きい順。
        /// </remarks>
        private void SortPlays(int column)
        {
            if (column < 0 || column >= PlayColumns.Length) return;

            if (column == _playsSortColumn) _playsSortDescending = !_playsSortDescending;
            else
            {
                _playsSortColumn = column;
                string caption = PlayColumns[column];
                _playsSortDescending = caption != "順位" && caption != "Miss";
            }

            MarkSortedColumn();
            FillPlays(_shownMap, _shown);
        }

        /// <summary>並べている列の見出しに ▲（小さい順）/ ▼（大きい順）を付ける。</summary>
        private void MarkSortedColumn()
        {
            for (int i = 0; i < _plays.Columns.Count && i < PlayColumns.Length; i++)
            {
                string mark = i != _playsSortColumn ? "" : _playsSortDescending ? " ▼" : " ▲";
                _plays.Columns[i].Text = Caption(PlayColumns[i]) + mark;
            }
        }

        /// <summary>並べ替えに使う値。列の並びは <see cref="PlayColumns"/> と同じ。</summary>
        private double SortKey(MapEntry map, int index, int rank)
        {
            var score = map.Plays[index];

            switch (PlayColumns[_playsSortColumn])
            {
                case "回目": return index;
                case "日時": return PlayedAt(score).Ticks;
                case "再現精度 %": return score.ReproducibilityPercent;
                case "再現 /100": return score.Score;
                case "精度 %": return score.Accuracy;
                case "総合 /115": return score.AverageCut;
                case "TD": return score.TimeDependence;
                case "PRE": return HandAverage(score, h => h.PreSwing);
                case "POST": return HandAverage(score, h => h.PostSwing);
                case "Miss": return score.MissCount;
                case "長さ": return score.DurationSeconds;
                default: return rank;
            }
        }

        /// <summary>左右の平均。一覧では「左 / 右」と並べている値を1つにして並べる。</summary>
        private static double HandAverage(ReplayScore score, Func<SwingScore, double> value)
        {
            var values = new[] { score.Hand(0), score.Hand(1) }.Where(h => h != null).Select(value).ToList();
            return values.Count == 0 ? double.MinValue : values.Average();
        }

        /// <summary>2段目で行を選んだ。軌道と内訳をそのプレイに切り替える。</summary>
        private void OnPlayPicked()
        {
            if (_fillingPlays || _plays.SelectedItems.Count == 0) return;
            ShowPlay((ReplayScore)_plays.SelectedItems[0].Tag);
        }

        /// <summary>記録として数えない理由の印。数えるものは空。</summary>
        private static string NotCountedMark(ReplayInfo info)
        {
            if (info.LooksLikeAutoplay) return Lang.T("   〈自動プレイ〉", "   <autoplay>");
            if (info.SpeedChanged)
                return string.Format(CultureInfo.InvariantCulture, Lang.T("   〈速度 {0:0}%・記録外〉", "   <speed {0:0}%, not counted>"), info.Speed * 100f);
            if (info.SpeedModifier != null) return Lang.T("   〈" + info.SpeedModifier + "・記録外〉", "   <" + info.SpeedModifier + ", not counted>");
            return "";
        }

        private string WhenText(ReplayScore score)
        {
            var when = PlayedAt(score);
            return when == DateTime.MinValue ? "" : when.ToString("yy/MM/dd HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>2段目の数字の並び。<see cref="ScoreColumns"/> と同じ順。</summary>
        private static void FillScores(ListViewItem item, ReplayScore score)
        {
            var ci = CultureInfo.InvariantCulture;

            // 記録として数えないもの（自動プレイ・速度変更）は薄く出す。
            if (!score.Replay.Info.CountsAsRecord) item.ForeColor = SystemColors.GrayText;

            // 順位を決めている値を先頭に置く。
            item.SubItems.Add(score.ReproducibilityPercent.ToString("0.0", ci));
            item.SubItems.Add(score.Score.ToString("0.0", ci));
            item.SubItems.Add(score.Accuracy.ToString("0.00", ci));

            item.SubItems.Add(score.AverageCut.ToString("0.00", ci));
            item.SubItems.Add(score.TimeDependence.ToString("0.000", ci));
            item.SubItems.Add(PercentPair(score, h => h.PreSwing));
            item.SubItems.Add(PercentPair(score, h => h.PostSwing));

            // 往復ずれとタイミングはここには出さない。往復ずれは 再現 /100 の生の値で
            // 同じことを2度言っており、タイミングは左右で符号が違うと打ち消し合って
            // まとめた1つの数字にする意味が無い。どちらも右上の内訳で見る。
            item.SubItems.Add(score.MissCount.ToString(ci));
            item.SubItems.Add(TimeSpan.FromSeconds(score.DurationSeconds).ToString(@"m\:ss"));
        }

        /// <summary>PRE / POST は左右で意味が違うので、一覧では「左 / 右」と並べて出す。</summary>
        private static string PercentPair(ReplayScore score, Func<SwingScore, double> value)
        {
            var ci = CultureInfo.InvariantCulture;
            var left = score.Hand(0);
            var right = score.Hand(1);

            return (left == null ? "—" : value(left).ToString("0", ci)) + " / " +
                   (right == null ? "—" : value(right).ToString("0", ci)) + "%";
        }

        /// <summary>選んだ行の左右の内訳を埋める。</summary>
        private void ShowDetail(ReplayScore score)
        {
            _detail.BeginUpdate();
            _detail.Items.Clear();

            if (score != null)
            {
                var ci = CultureInfo.InvariantCulture;
                var left = score.Hand(0);
                var right = score.Hand(1);

                // まず BeatLeader の成績画面と同じ並び。
                AddSplit(Lang.T("総合 /115", "Total /115"), score, left, right, d => d.AverageCut.ToString("0.00", ci));
                // 振りの順に並べる。振りかぶり → 振り抜き → 当たり所。
                AddSplit(Lang.T("振りかぶり /70", "Pre-swing /70"), score, left, right, d => d.BeforeCutScore.ToString("0.00", ci));
                AddSplit(Lang.T("振り抜き /30", "Post-swing /30"), score, left, right, d => d.AfterCutScore.ToString("0.00", ci));
                AddSplit(Lang.T("中心 /15", "Center /15"), score, left, right, d => d.CutDistanceScore.ToString("0.00", ci));
                AddSplit("TD", score, left, right, d => d.TimeDependence.ToString("0.000", ci));
                // 単位は他の行と同じく見出し側に置く。
                AddSplit("PRE %", score, left, right, d => d.PreSwing.ToString("0.00", ci));
                AddSplit("POST %", score, left, right, d => d.PostSwing.ToString("0.00", ci));

                // ここから JumpDrill 独自。
                // 行きと帰りを突き合わせて出す値なので、片側だけでは定義できない。
                AddHand(Lang.T("再現 /100", "Repro /100"), score.Score.ToString("0.0", ci), left, right, h => h.Score.ToString("0.0", ci));
                // 再現 /100 × 振り数。振れた本数ぶんの素点なので、ここは残す。
                AddHand(Lang.T("再現スコア", "Repro score"), score.ReproducibilityScore.ToString("0", ci), left, right, h => h.ReproducibilityScore.ToString("0", ci));
                // 再現の元になる生の値。行きと帰りが何 m 離れているか。
                AddHand(Lang.T("往復ずれ m", "Round-trip gap m"), score.RoundTripGap.ToString("0.0000", ci), left, right, h => h.RoundTripGap.ToString("0.0000", ci));

                AddSplit(Lang.T("束の幅 m", "Path spread m"), score, left, right, d => d.PathScatter.ToString("0.0000", ci));
                AddSplit(Lang.T("角度 /100", "Angle /100"), score, left, right, d => d.AnglePercent.ToString("0.0", ci));
                AddSplit(Lang.T("角度ずれ °", "Angle error °"), score, left, right, d => d.AngleErrorMean.ToString("0.0", ci));
                AddSplit(Lang.T("中心ずれ cm", "Center offset cm"), score, left, right, d => (d.CutDistanceMean * 100.0).ToString("0.0", ci));

                // 判定中心からの時間のずれ。＋が遅れ、−が早い。
                // 「中心ずれ cm」は面のどこを切ったかで、こちらは時間。別の軸のずれなので
                // 名前を分けてある（どちらも「ずれ」だと読み違える）。
                // 平均だけだと早い遅いが打ち消し合うのでばらつきも出す。
                AddSplit(Lang.T("タイミング ms", "Timing ms"), score, left, right, d => d.TimeDeviationMean.ToString("+0.0;-0.0;0.0", ci));
                AddSplit(Lang.T("タイミングばらつき ms", "Timing spread ms"), score, left, right, d => d.TimeDeviationSpread.ToString("0.0", ci));

                // 当たっていないノーツの話はこの1行だけ。何本振り落としたかが分かれば、
                // 上の数字が「その手の振れたぶんの出来」だと読める。
                AddSplit("Miss", score, left, right, d => d.MissCount.ToString(ci));

                // 上の数字が何本ぶんの平均なのか。切れたノーツ数とは一致しない
                // （3本に満たない遷移は平均が当てにならないので数えていない）。
                AddSplit(Lang.T("有効振り数", "Valid swings"), score, left, right, d => d.SwingCount.ToString(ci));
            }

            _detail.EndUpdate();

            ListColumns.FitAll(_detail, 0);
            FitWindowWidth();
            FitDetailWidth();
        }

        /// <summary>
        /// その表が列ぶんちょうど収まる幅。縦スクロールバーの分も見ておく。
        ///
        /// 見るのは<b>伸ばす前の幅</b>。最後の列は余白を埋めるために伸ばしてあるので、
        /// そちらで測ると、測るたびに窓が広がっていく。
        /// </summary>
        private static int ContentWidth(ListView list)
        {
            return ListColumns.NaturalWidth(list) + SystemInformation.VerticalScrollBarWidth + 8;
        }

        /// <summary>
        /// 譜面の一覧が列ぶんちょうど収まる幅に、境目の方を合わせる。<b>最初の1回だけ。</b>
        /// 以後は掴んで変えてもらう（再スキャンのたびに戻ると、動かした意味が無い）。
        /// </summary>
        private void FitMapWidth()
        {
            if (_mapSized || _stages == null || _list.Items.Count == 0) return;

            int room = _stages.Width - _stages.SplitterWidth - PlaysMinimum;
            if (room < MapMinimum) return;          // まだ幅が決まっていない

            _mapSized = true;
            _stages.SplitterDistance = Math.Max(MapMinimum, Math.Min(ContentWidth(_list), room));
        }

        /// <summary>
        /// 窓の幅を、中身が横スクロール無しで収まる幅に合わせる。<b>最初の1回だけ。</b>
        ///
        /// 決め打ちの大きさにすると、列の数・フォント・DPI のどれかが変わるたびに
        /// 足りなかったり余ったりする。<b>余った幅を表に配って埋めるのは筋が悪い</b>
        /// （数字の列が伸びて桁が離れるか、見出しの列だけが間延びする）。
        /// 中身の実寸を足し上げて、窓の方をそこへ合わせる。
        ///
        /// 上の段（絵＋内訳）と下の段（譜面＋プレイ）の広い方に合わせる。
        /// 画面からはみ出す場合はそこで止める（そのときは掴んで詰めてもらう）。
        /// </summary>
        private void FitWindowWidth()
        {
            if (_windowSized || _detail.Items.Count == 0 || _list.Items.Count == 0) return;
            _windowSized = true;

            int upper = FiguresDefault + SplitterThickness + ContentWidth(_detail);
            int lower = ContentWidth(_list) + SplitterThickness + ContentWidth(_plays);

            // root の Padding(10) と、窓枠のぶん。
            int need = Math.Max(upper, lower) + 20 + (Width - ClientSize.Width);

            Width = Math.Min(need, Screen.FromControl(this).WorkingArea.Width);
        }

        /// <summary>
        /// 内訳の列がちょうど収まる幅に、境目の方を合わせる。<b>最初の1回だけ。</b>
        ///
        /// 行を選び直すたびに合わせ直すと、掴んで動かした位置がその場で戻ってしまう。
        /// 以後は掴んで変えてもらう。
        /// </summary>
        private void FitDetailWidth()
        {
            if (_detailSized || _lower == null || _detail.Items.Count == 0) return;

            int width = ContentWidth(_detail);

            int room = _lower.Width - _lower.SplitterWidth - DetailMinimum;
            if (room < FiguresMinimum) return;      // まだ幅が決まっていない

            _detailSized = true;
            _lower.Panel1MinSize = FiguresMinimum;
            _lower.Panel2MinSize = DetailMinimum;

            // 内訳は列ぶんちょうど。余りは絵に回す。
            // 窓は中身に合わせてあるので、ふつうは余り＝絵の既定の幅になる。
            _lower.SplitterDistance = Math.Max(FiguresMinimum,
                Math.Min(_lower.Width - _lower.SplitterWidth - width, room));

            FitDetailHeight();
        }

        /// <summary>
        /// 内訳の行が全部見える高さまで、上下の境目を下げる。<b>最初の1回だけ。</b>
        ///
        /// 行数も行の高さもフォントと DPI で変わるので、決め打ちの高さでは足りたり
        /// 余ったりする。実測して、下の一覧に残す分が確保できる範囲で譲る。
        /// </summary>
        private void FitDetailHeight()
        {
            if (_split == null || _detail.Items.Count == 0) return;

            // 組み立て直してから測る。最後の行の下端が表の中に収まっていれば足りている。
            int shortfall = _detail.Items[_detail.Items.Count - 1].Bounds.Bottom + 4
                          - _detail.ClientSize.Height;
            if (shortfall <= 0) return;

            int room = _split.Height - _split.SplitterWidth - ListMinimum;
            _split.SplitterDistance = Math.Min(_split.SplitterDistance + shortfall, room);
        }

        /// <summary>
        /// まとめ・フォア・バックを手ごとに3列出す行。
        /// 振っていない手や、その向きの振りが無い側は「—」。
        /// </summary>
        private void AddSplit(string caption, ReplayScore both, SwingScore left, SwingScore right,
                              Func<ICutStats, string> format)
        {
            var row = new ListViewItem(caption);
            row.SubItems.Add(Cell(both, format));

            foreach (var hand in new[] { left, right })
            {
                row.SubItems.Add(Cell(hand, format));
                row.SubItems.Add(Cell(hand == null ? null : hand.Fore, format));
                row.SubItems.Add(Cell(hand == null ? null : hand.Back, format));
            }

            _detail.Items.Add(row);
        }

        /// <summary>
        /// 手ごとに1つしかない行。フォアとバックを突き合わせて出す値なので、
        /// 片側では定義できない。まとめの列にだけ置いて、残り2列は「—」。
        /// </summary>
        private void AddHand(string caption, string both, SwingScore left, SwingScore right,
                             Func<SwingScore, string> format)
        {
            var row = new ListViewItem(caption);
            row.SubItems.Add(both);

            foreach (var hand in new[] { left, right })
            {
                row.SubItems.Add(hand == null ? "—" : format(hand));
                row.SubItems.Add("—");
                row.SubItems.Add("—");
            }

            _detail.Items.Add(row);
        }

        private static string Cell(ICutStats side, Func<ICutStats, string> format)
        {
            return side == null ? "—" : format(side);
        }

        private MapEntry SelectedMap
        {
            get { return _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as MapEntry; }
        }

        /// <summary>ボタンが相手にするのは、いま出しているプレイ。</summary>
        private ReplayScore Selected
        {
            get { return _shown; }
        }

        private void OnSelectionChanged()
        {
            if (_rebuilding) return;
            ShowMap(SelectedMap);
        }

        /// <summary>譜面を選び直す。2段目を入れ替えて、出すのはその譜面のベスト。</summary>
        private void ShowMap(MapEntry map)
        {
            _shownMap = map;
            FillPlays(map);
            ShowPlay(map == null ? null : map.Best);
        }

        private void UpdateButtons()
        {
            bool has = _shown != null;
            _openReplay.Enabled = has;
            _openZip.Enabled = has;
            _openBeatLeader.Enabled = has;
        }

        /// <summary>
        /// 1つのプレイの軌道を描く。振りを全部重ねるので束の太さが再現性になる。
        /// 1段目からはベストが、2段目からはそこで選んだプレイが渡ってくる。
        /// </summary>
        private void ShowPlay(ReplayScore score)
        {
            _shown = score;
            UpdateButtons();
            ShowDetail(score);

            if (score == null)
            {
                _view.LeftSummary = null;
                _view.RightSummary = null;
                _sideView.LeftSummary = null;
                _sideView.RightSummary = null;
                _view.Load(null);
                _sideView.Load(null);
                _viewInfo.Text = "";
                return;
            }

            _view.LeftSummary = SummaryOf(score.Hand(0));
            _view.RightSummary = SummaryOf(score.Hand(1));
            _sideView.LeftSummary = _view.LeftSummary;
            _sideView.RightSummary = _view.RightSummary;
            _view.Load(score.Replay);
            _sideView.Load(score.Replay);
            ApplyViewOptions();

            // 幅に収まる範囲に留める。細かい数字は右の表にある。
            _viewInfo.Text = string.Format(CultureInfo.CurrentCulture,
                Lang.T("{0}{1}   振り {2} 本{3}", "{0}{1}   {2} swings{3}"),
                DrillNaming.StripId(score.Replay.Info.SongName),
                NotCountedMark(score.Replay.Info),
                _view.SwingCount,
                score.HandIntervalMs > 0
                    ? string.Format(CultureInfo.InvariantCulture, Lang.T("   {0:0} BPM (片手 {1:0} ms)", "   {0:0} BPM (per hand {1:0} ms)"),
                        Tempo.EbpmFromHandIntervalMs(score.HandIntervalMs), score.HandIntervalMs)
                    : "");
        }

        /// <summary>絵の中に出す手ごとの数字。振っていない手は出さない。</summary>
        private static ReplayView.HandSummary SummaryOf(SwingScore hand)
        {
            if (hand == null) return null;

            return new ReplayView.HandSummary
            {
                Reproducibility = hand.Score,
                AverageCut = hand.AverageCut,
                AngleError = hand.AngleErrorMean,
                AnglePercent = hand.AnglePercent,
                Fore = AnglesOf(hand.Fore),
                Back = AnglesOf(hand.Back),
            };
        }

        /// <summary>横から見た図に添える PRE / POST。その向きの振りが無ければ null。</summary>
        private static ReplayView.SwingAngles AnglesOf(DirectionScore side)
        {
            if (side == null) return null;

            return new ReplayView.SwingAngles
            {
                PreSwing = side.PreSwing,
                PostSwing = side.PostSwing,
            };
        }

        /// <summary>手と平均の選びは2つの図で揃える。別々だと見比べにならない。</summary>
        private void ApplyViewOptions()
        {
            int? only = _hand.SelectedIndex == 1 ? 1 : _hand.SelectedIndex == 2 ? 0 : (int?)null;

            foreach (var view in new[] { _view, _sideView })
            {
                view.OnlyHand = only;
                view.ShowMeanOnly = _meanOnly.Checked;
                view.Invalidate();
            }

            // 左右の向きは横の図にしか無い。
            _sideView.FaceRight = !_faceLeft.Checked;
        }

        private void OpenReplayLocation()
        {
            var score = Selected;
            if (score == null || !File.Exists(score.Replay.Path)) return;

            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + score.Replay.Path + "\"")
            {
                UseShellExecute = true,
            });
        }

        /// <summary>
        /// BeatLeader のリプレイ表示は譜面を BeatSaver から探すので、
        /// 自作ドリルでは見つからない。読ませる zip を作ってその場所を開く。
        /// </summary>
        private void OpenZip()
        {
            var score = Selected;
            if (score == null) return;

            try
            {
                string folder = FindLevelFolder(score.Replay.Info.SongName);
                if (folder == null)
                {
                    MessageBox.Show(this,
                        Lang.T("この譜面のフォルダが見つかりませんでした。", "The folder for this map was not found.") + Environment.NewLine +
                        Lang.T("生成し直すか、出力先を確認してください。", "Generate it again or check the output folder."),
                        "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string zip = LevelZipWriter.Write(folder, new Workspace(_workspaceRoot).ZipFolder);
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + zip + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// BeatLeader の web リプレイヤーで開く。
        ///
        /// リプレイは <c>?link=</c>、譜面は <c>?mapLink=</c> で URL から読ませられる。
        /// どちらもローカルに立てた HTTP サーバから配るので、
        /// «Map was not found» のドロップは要らない。
        /// （mapLink は README に載っていないが zip-loader.js が受け取っている）
        /// </summary>
        private void OpenInBeatLeader()
        {
            var score = Selected;
            if (score == null) return;

            try
            {
                string folder = FindLevelFolder(score.Replay.Info.SongName);
                if (folder == null)
                {
                    MessageBox.Show(this,
                        Lang.T("この譜面のフォルダが見つかりませんでした。", "The folder for this map was not found.") + Environment.NewLine +
                        Lang.T("譜面を消したか移した場合は、生成し直してください。", "If you deleted or moved the map, generate it again."),
                        "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // zip はアプリの作業フォルダに作り、そこから配る。
                string zip = LevelZipWriter.Write(folder, new Workspace(_workspaceRoot).ZipFolder);

                if (_server == null) _server = new LocalFileServer();
                string replayUrl = _server.Publish(score.Replay.Path);
                string mapUrl = _server.Publish(zip);

                string url = "https://replay.beatleader.xyz/?link=" + Uri.EscapeDataString(replayUrl)
                           + "&mapLink=" + Uri.EscapeDataString(mapUrl);

                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                _status.Text = Lang.T("BeatLeader で開きました（譜面もローカルから渡しています）: ", "Opened in BeatLeader (map served locally): ") + zip;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>曲名から譜面フォルダを逆引きする。探し方は生成側と共通。</summary>
        private string FindLevelFolder(string songName)
        {
            return DrillLibrary.FindByName(songName, _workspaceRoot);
        }
    }
}
