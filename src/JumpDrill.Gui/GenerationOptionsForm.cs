using System.Linq;
using System.Windows.Forms;
using JumpDrill.Model;

namespace JumpDrill.Gui
{
    internal sealed class GenerationOptionsForm : Form
    {
        private readonly ComboBox click = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox tone = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown countIn = new NumericUpDown { Maximum = 32, Width = 72 };
        private readonly NumericUpDown tail = new NumericUpDown { Minimum = 0, Maximum = 10, DecimalPlaces = 1, Increment = 0.1m, Width = 72 };
        internal decimal Tail => tail.Value;
        /// <summary>開いたときに欄へ入れた値（範囲に合わせたあと）。</summary>
        internal decimal InitialTail { get; }
        internal int ClickIndex => click.SelectedIndex;
        internal int Tone => tone.SelectedIndex;
        internal decimal CountIn => countIn.Value;

        /// <param name="note">対象の下に出す1行。null なら標準の一括生成の説明（全件のときだけ）。</param>
        /// <param name="okText">生成のボタンの文字。null なら「生成」「すべて生成」。</param>
        internal GenerationOptionsForm(string target, bool all, int currentClick, int currentTone, decimal currentCountIn, decimal currentTail,
            string note = null, string okText = null)
        {
            Text = Lang.T("生成オプション", "Generation options");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ShowInTaskbar = false;
            MinimizeBox = MaximizeBox = false;

            click.Items.AddRange(MainForm.ClickItems());
            tone.Items.AddRange(MainForm.ClickToneItems());
            click.SelectedIndex = currentClick;
            tone.SelectedIndex = currentTone;
            // 手で保存値を増やしていた場合も、開いたときの値はそのまま表示する
            countIn.Maximum = System.Math.Max(countIn.Maximum, currentCountIn);
            countIn.Value = System.Math.Max(0, currentCountIn);
            tail.Value = System.Math.Max(tail.Minimum, System.Math.Min(tail.Maximum, currentTail));
            InitialTail = tail.Value;

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(14) };
            Controls.Add(layout);
            AddLine(Lang.T("対象：", "Target: ") + target, 0);
            if (note == null && all) note = Lang.T("この設定は生成する全曲に適用されます。既存曲の変更には上書きが必要です。", "These settings apply to all maps written. Enable overwrite to change existing maps.");
            if (!string.IsNullOrEmpty(note)) AddLine(note, 1);
            AddRow(Lang.T("クリック音", "Click sound"), click, 2);
            AddRow(Lang.T("音色", "Tone"), tone, 3);
            AddRow(Lang.T("カウントイン（発）", "Count-in (clicks)"), countIn, 4);
            AddLine(Lang.T("カウントインはクリック音とは別に鳴ります。", "Count-in plays independently of the click sound."), 5);
            AddRow(Lang.T("末尾の余白（秒）", "Tail padding (seconds)"), tail, 6);
            click.SelectedIndexChanged += (s, e) => UpdateTone();
            UpdateTone();

            var ok = new Button { Text = okText ?? (all ? Lang.T("すべて生成", "Generate all") : Lang.T("生成", "Generate")), AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = Lang.T("キャンセル", "Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons, 0, 7);
            layout.SetColumnSpan(buttons, 2);
            AcceptButton = ok;
            CancelButton = cancel;
            Theme.Apply(this);

            void AddLine(string text, int row)
            {
                var label = new Label { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 6) };
                layout.Controls.Add(label, 0, row);
                layout.SetColumnSpan(label, 2);
            }

            void AddRow(string text, Control editor, int row)
            {
                layout.Controls.Add(new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
                layout.Controls.Add(editor, 1, row);
                if (editor is ComboBox combo)
                    combo.Width = combo.Items.Cast<string>().Max(item => TextRenderer.MeasureText(item, combo.Font).Width)
                        + SystemInformation.VerticalScrollBarWidth + 18;
            }

            void UpdateTone() => tone.Enabled = ClickIndex != (int)ClickMode.None;
        }
    }
}
