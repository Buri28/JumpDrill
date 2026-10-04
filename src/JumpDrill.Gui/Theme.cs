using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace JumpDrill.Gui
{
    /// <summary>
    /// 画面の色。<b>暗い方に寄せる</b>。
    ///
    /// 軌道の図は真っ黒の上に束を描くので、周りが明るいままだと図を見るたびに
    /// 目を明順応させ直すことになる。数字と図を突き合わせて見る画面なので、
    /// そこが行き来のたびに眩しいのは効く。
    ///
    /// WinForms には暗色の指定がまるごと無い（.NET 8 の時点）。
    /// 部品ごとに色を入れて回るしかないので、その置き場をここにまとめる。
    /// 色を配るのは <see cref="Apply"/> ひとつだけにして、
    /// 画面側は「作って Apply する」以上のことを知らなくていいようにしてある。
    /// </summary>
    internal static class Theme
    {
        /// <summary>地の色。窓とパネル。</summary>
        public static readonly Color Background = Color.FromArgb(0x1E, 0x20, 0x24);

        /// <summary>表・入力欄・ボタンの面。地よりわずかに明るくして、境目を色で出す。</summary>
        public static readonly Color Surface = Color.FromArgb(0x26, 0x2A, 0x30);

        /// <summary>押している間のボタンの面。</summary>
        public static readonly Color Pressed = Color.FromArgb(0x34, 0x39, 0x42);

        /// <summary>指している間のボタンの面。</summary>
        public static readonly Color Hover = Color.FromArgb(0x2E, 0x33, 0x3B);

        /// <summary>本文の字。</summary>
        public static readonly Color Text = Color.FromArgb(0xD8, 0xDC, 0xE2);

        /// <summary>添え物の字。見出しの単位や注記。</summary>
        public static readonly Color Faint = Color.FromArgb(0x8A, 0x93, 0xA3);

        /// <summary>枠線と仕切り。</summary>
        public static readonly Color Line = Color.FromArgb(0x3C, 0x42, 0x4C);

        /// <summary>表の見出しの面。行より暗くして、行の始まりを分からせる。</summary>
        public static readonly Color Header = Color.FromArgb(0x2C, 0x31, 0x39);

        /// <summary>選んでいる行。その表に入力の焦点があるとき。</summary>
        public static readonly Color Selection = Color.FromArgb(0x2F, 0x5E, 0x9E);

        /// <summary>
        /// 選んでいる行。焦点が<b>他の表に移っているとき</b>。
        ///
        /// 既定は白地に黒字の反転で、暗い画面の中でそこだけ紙のように光る。
        /// 焦点の有無は色味で分かればいいので、青と並べて読み分けられる緑にする。
        /// どちらも字は白のまま（地だけ変える）。
        /// </summary>
        public static readonly Color SelectionIdle = Color.FromArgb(0x2E, 0x7D, 0x52);

        /// <summary>選んでいる行の字。地が濃いので白で通す。</summary>
        public static readonly Color SelectionText = Color.FromArgb(0xF2, 0xF5, 0xFA);

        /// <summary>カーソルを合わせた行の字。既定の描画だと黒字になって読めない。</summary>
        public static readonly Color HoverText = Color.FromArgb(0x5C, 0xE1, 0xE6);

        /// <summary>
        /// 部品の木を辿って色を入れる。作り終えてから1度呼ぶ。
        ///
        /// 既に色を入れてある部品（軌道の図など）は、自分の色を持っているので触らない。
        /// </summary>
        public static void Apply(Control root)
        {
            if (root == null) return;

            AllowDarkMode();

            var form = root as Form;
            if (form != null)
            {
                form.BackColor = Background;
                form.ForeColor = Text;
                DarkenTitleBar(form);
            }

            foreach (Control child in root.Controls) ApplyTo(child);
        }

        private static void ApplyTo(Control control)
        {
            // 仕切りの帯は色を残す（掴める所が見えなくなる）。面の方は地の色に戻す。
            var split = control as SplitContainer;
            if (split != null)
            {
                split.Panel1.BackColor = Background;
                split.Panel2.BackColor = Background;

                foreach (Control child in split.Panel1.Controls) ApplyTo(child);
                foreach (Control child in split.Panel2.Controls) ApplyTo(child);
                return;
            }

            // 入れ物は地の色。親から明るい色を引き継ぐと、そこだけ帯になって残る。
            if (control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel)
            {
                control.BackColor = Background;
                control.ForeColor = Text;
            }

            var button = control as Button;
            if (button != null) { Style(button); return; }

            var list = control as ListView;
            if (list != null) { Style(list); return; }

            var text = control as TextBoxBase;
            if (text != null)
            {
                text.BackColor = Surface;
                text.ForeColor = Text;
                text.BorderStyle = BorderStyle.FixedSingle;
                DarkenScrollBars(text);
                return;
            }

            var combo = control as ComboBox;
            if (combo != null)
            {
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = Surface;
                combo.ForeColor = Text;
                return;
            }

            var number = control as NumericUpDown;
            if (number != null)
            {
                number.BackColor = Surface;
                number.ForeColor = Text;
                number.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            var check = control as CheckBox;
            if (check != null) { check.FlatStyle = FlatStyle.Flat; check.ForeColor = Text; return; }

            var radio = control as RadioButton;
            if (radio != null) { radio.FlatStyle = FlatStyle.Flat; radio.ForeColor = Text; return; }

            var group = control as GroupBox;
            if (group != null)
            {
                group.FlatStyle = FlatStyle.Flat;
                group.ForeColor = Text;
            }

            var label = control as Label;
            if (label != null)
            {
                // 添え物として薄く出してある字は、薄いまま暗色側へ移す。
                label.ForeColor = label.ForeColor == SystemColors.GrayText ? Faint : Text;
                return;
            }

            foreach (Control child in control.Controls) ApplyTo(child);
        }

        /// <summary>
        /// スクロールバーとタイトルバーを暗くする。
        ///
        /// この2つは<b>色を指定できる部品ではない</b>。窓の枠と同じで OS が描くので、
        /// 暗い表の脇に白い棒が残る。Windows 10 1903 以降には暗色版があり、
        /// 「この窓は暗色でよい」と申告すれば使ってくれる。
        ///
        /// 申告する口が<b>序数でしか公開されていない</b>（<c>uxtheme.dll</c> の #135）。
        /// 将来の Windows で消えても画面が明るいままになるだけなので、
        /// 失敗は黙って飲む。
        /// </summary>
        private static void AllowDarkMode()
        {
            if (_darkModeAsked) return;
            _darkModeAsked = true;

            try { SetPreferredAppMode(ForceDark); FlushMenuThemes(); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }

        private static bool _darkModeAsked;

        /// <summary>暗色を許す (2 = ForceDark)。</summary>
        private const int ForceDark = 2;

        /// <summary>タイトルバーを暗くする指定。</summary>
        private const int UseImmersiveDarkMode = 20;

        [DllImport("uxtheme.dll", EntryPoint = "#135", CharSet = CharSet.Unicode)]
        private static extern int SetPreferredAppMode(int mode);

        [DllImport("uxtheme.dll", EntryPoint = "#136", CharSet = CharSet.Unicode)]
        private static extern void FlushMenuThemes();

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string application, string id);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private static void DarkenTitleBar(Form form)
        {
            WhenReady(form, () =>
            {
                int on = 1;
                try { DwmSetWindowAttribute(form.Handle, UseImmersiveDarkMode, ref on, sizeof(int)); }
                catch (DllNotFoundException) { }
            });
        }

        /// <summary>
        /// その部品のスクロールバーを暗色版に差し替える。
        /// 窓が出来てからでないと効かないので、出来ていなければ出来たときに。
        /// </summary>
        private static void DarkenScrollBars(Control control)
        {
            WhenReady(control, () =>
            {
                try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); }
                catch (DllNotFoundException) { }
            });
        }

        private static void WhenReady(Control control, Action action)
        {
            if (control.IsHandleCreated) action();
            else control.HandleCreated += (s, e) => action();
        }

        private static void Style(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Surface;
            button.ForeColor = Text;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderColor = Line;
            button.FlatAppearance.MouseOverBackColor = Hover;
            button.FlatAppearance.MouseDownBackColor = Pressed;

            // 無効なボタンは既定だと暗い灰色の文字になり、暗い背景では読めない。薄い色で描き直す。
            button.Paint += (s, e) =>
            {
                if (button.Enabled) return;

                var rect = button.ClientRectangle;
                using (var back = new SolidBrush(Background))
                    e.Graphics.FillRectangle(back, rect);
                using (var border = new Pen(Line))
                    e.Graphics.DrawRectangle(border, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                TextRenderer.DrawText(e.Graphics, button.Text, button.Font, rect, Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            };
        }

        /// <summary>
        /// 表は見出しだけ自前で描く。
        ///
        /// <c>ListView</c> の見出しは <c>BackColor</c> を見てくれず、明るいままになる。
        /// 行だけ暗くすると、表の上端に白い帯が残って余計に目立つ。
        /// 行の方は既定の描画に任せる（1行ずつの色分け・選択の反転をこちらで
        /// 作り直すと、自動プレイの灰色などを全部持ち込むことになる）。
        /// </summary>
        private static void Style(ListView list)
        {
            DarkenScrollBars(list);

            list.BackColor = Surface;
            list.ForeColor = Text;
            list.BorderStyle = BorderStyle.FixedSingle;

            // 格子は色を選べず、暗い面の上では白い網になって数字より目立つ。
            list.GridLines = false;

            // 幅が変わると見出しの帯の右端に地が出るので、最後の列で埋め直す。
            list.Resize += (s, e) => ListColumns.FillLast(list);

            list.OwnerDraw = true;
            list.DrawColumnHeader += DrawHeader;

            // 選んでいない行は既定の描画に任せる（自動プレイの灰色などを持ち込まない）。
            // 選んでいる行とカーソルを合わせた行だけ、地と字をこちらで塗る。
            ListViewItem hovered = null;
            Action<ListViewItem> setHover = item =>
            {
                if (item == hovered) return;
                var old = hovered;
                hovered = item;
                if (old != null && old.ListView == list) list.Invalidate(old.Bounds);
                if (item != null) list.Invalidate(item.Bounds);
            };
            list.MouseMove += (s, e) => setHover(list.GetItemAt(e.X, e.Y));
            list.MouseLeave += (s, e) => setHover(null);

            list.DrawItem += (s, e) => e.DrawDefault = !e.Item.Selected && e.Item != hovered;
            list.DrawSubItem += (s, e) => DrawCell(list, e, e.Item == hovered);

            // 焦点が移ると色が変わるので、その場で塗り直す。
            list.GotFocus += (s, e) => list.Invalidate();
            list.LostFocus += (s, e) => list.Invalidate();
        }

        private static void DrawCell(ListView list, DrawListViewSubItemEventArgs e, bool hovered)
        {
            if (!e.Item.Selected && !hovered) { e.DrawDefault = true; return; }

            Color back = e.Item.Selected ? (list.Focused ? Selection : SelectionIdle) : Hover;
            using (var brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, e.Bounds);

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

            // 先頭の列だけは本体が必ず左寄せで描くので、列の指定を見ない。
            switch (e.ColumnIndex == 0 ? HorizontalAlignment.Left : list.Columns[e.ColumnIndex].TextAlign)
            {
                case HorizontalAlignment.Right: flags |= TextFormatFlags.Right; break;
                case HorizontalAlignment.Center: flags |= TextFormatFlags.HorizontalCenter; break;
                default: flags |= TextFormatFlags.Left; break;
            }

            var bounds = Rectangle.Inflate(e.Bounds, -4, 0);

            // チェックボックスのある表は、先頭の列にチェックボックスも描く。
            // 塗りつぶしたままだと、選んだ行やカーソルを合わせた行だけチェックボックスが消えて押せない
            if (e.ColumnIndex == 0 && list.CheckBoxes)
            {
                Rectangle label = e.Item.GetBounds(ItemBoundsPortion.Label);
                var state = e.Item.Checked
                    ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal
                    : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
                Size glyph = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
                int boxArea = Math.Max(glyph.Width, label.Left - e.Bounds.Left);
                // 既定の描画と同じ位置に揃える（チェックボックスは少し右、字はラベルの左端から）
                var at = new Point(e.Bounds.Left + (boxArea - glyph.Width) / 2 + 2,
                                   e.Bounds.Top + (e.Bounds.Height - glyph.Height) / 2);
                CheckBoxRenderer.DrawCheckBox(e.Graphics, at, state);

                int textLeft = label.Left - 2;
                bounds = new Rectangle(textLeft, e.Bounds.Top, Math.Max(0, e.Bounds.Right - textLeft), e.Bounds.Height);
            }

            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.SubItem.Font ?? list.Font,
                                  bounds, e.Item.Selected ? SelectionText : HoverText, flags);
        }

        private static void DrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var back = new SolidBrush(Header))
            using (var pen = new Pen(Line))
            {
                e.Graphics.FillRectangle(back, e.Bounds);

                // 右端に1本だけ。見出しの区切りが無いと、どの数字がどの列か辿れない。
                e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 3,
                                         e.Bounds.Right - 1, e.Bounds.Bottom - 3);
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1,
                                         e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            switch (e.Header.TextAlign)
            {
                case HorizontalAlignment.Right: flags |= TextFormatFlags.Right; break;
                case HorizontalAlignment.Center: flags |= TextFormatFlags.HorizontalCenter; break;
                default: flags |= TextFormatFlags.Left; break;
            }

            // 列の幅は見出しの実寸ぎりぎりに詰めてあるので、余白を取りすぎると
            // 見出しの方が先に「…」で切れる（ListColumns.Slack と釣り合わせる）。
            var bounds = Rectangle.Inflate(e.Bounds, -3, 0);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font ?? Control.DefaultFont, bounds, Faint, flags);
        }
    }
}
