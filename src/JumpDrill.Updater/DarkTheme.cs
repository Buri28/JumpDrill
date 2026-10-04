using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace JumpDrill.Updater
{
    /// <summary>
    /// 更新画面の色。JumpDrill 本体（JumpDrill.Gui の Theme）と同じ暗い配色にする。
    /// </summary>
    /// <remarks>
    /// 本体は .NET 8、こちらは .NET Framework 4.8 で別にビルドするので、本体の Theme は使えない。
    /// この画面にある部品（ラベル・一覧・テキスト・ボタン）の分だけをここに持つ。色の値は本体と揃えておく。
    /// </remarks>
    internal static class DarkTheme
    {
        private static readonly Color Background = Color.FromArgb(0x1E, 0x20, 0x24);
        private static readonly Color Surface = Color.FromArgb(0x26, 0x2A, 0x30);
        private static readonly Color Pressed = Color.FromArgb(0x34, 0x39, 0x42);
        private static readonly Color Hover = Color.FromArgb(0x2E, 0x33, 0x3B);
        private static readonly Color Text = Color.FromArgb(0xD8, 0xDC, 0xE2);
        private static readonly Color Faint = Color.FromArgb(0x8A, 0x93, 0xA3);
        private static readonly Color Line = Color.FromArgb(0x3C, 0x42, 0x4C);

        public static void Apply(Form form)
        {
            AllowDarkMode();

            form.BackColor = Background;
            form.ForeColor = Text;
            WhenReady(form, () =>
            {
                int on = 1;
                try { DwmSetWindowAttribute(form.Handle, UseImmersiveDarkMode, ref on, sizeof(int)); }
                catch (DllNotFoundException) { }
            });

            foreach (Control child in form.Controls) ApplyTo(child);
        }

        private static void ApplyTo(Control control)
        {
            if (control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel)
            {
                control.BackColor = Background;
                control.ForeColor = Text;
            }

            var button = control as Button;
            if (button != null) { Style(button); return; }

            var list = control as ListBox;
            if (list != null)
            {
                list.BackColor = Surface;
                list.ForeColor = Text;
                list.BorderStyle = BorderStyle.FixedSingle;
                DarkenScrollBars(list);
                return;
            }

            var text = control as TextBoxBase;
            if (text != null)
            {
                text.BackColor = Surface;
                text.ForeColor = Text;
                text.BorderStyle = BorderStyle.FixedSingle;
                DarkenScrollBars(text);
                return;
            }

            var label = control as Label;
            if (label != null) { label.ForeColor = Text; return; }

            foreach (Control child in control.Controls) ApplyTo(child);
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

            // 無効なボタンは既定だと暗い灰色の字になり、暗い地では読めない。薄い色で描き直す
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
        /// スクロールバーとタイトルバーは OS が描くので、色の指定が効かない。
        /// Windows 10 1903 以降の暗色版を使うよう申告する（序数でしか公開されていない口。失敗しても明るいままなだけ）。
        /// </summary>
        private static void AllowDarkMode()
        {
            try { SetPreferredAppMode(ForceDark); FlushMenuThemes(); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }

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

        private const int ForceDark = 2;
        private const int UseImmersiveDarkMode = 20;

        [DllImport("uxtheme.dll", EntryPoint = "#135", CharSet = CharSet.Unicode)]
        private static extern int SetPreferredAppMode(int mode);

        [DllImport("uxtheme.dll", EntryPoint = "#136", CharSet = CharSet.Unicode)]
        private static extern void FlushMenuThemes();

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string application, string id);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
