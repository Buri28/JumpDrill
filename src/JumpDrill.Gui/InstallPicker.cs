using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using JumpDrill.Output;

namespace JumpDrill.Gui
{
    /// <summary>
    /// 記録を読む Beat Saber を選ぶ欄。スコア画面とメダル画面で同じものを使う。
    /// </summary>
    /// <remarks>
    /// 記録（リプレイ）は Beat Saber ごとの UserData にある。BSManager でいくつも版を入れていると、
    /// 全部をまとめて見せたのでは、どの版で叩いた記録なのかが分からない。
    /// 選んだものは2つの画面で揃え、次の起動まで覚えておく。
    /// </remarks>
    internal sealed class InstallPicker : ComboBox
    {
        private const string SettingKey = "recordsInstall";

        /// <summary>どちらかの画面で選び直された。もう一方も合わせて読み直す。</summary>
        public static event Action Changed;

        /// <summary>いま選んでいる Beat Saber（Beat Saber.exe のあるフォルダ）。null は全部。</summary>
        public static string Selected { get; private set; }

        private static bool _loaded;

        private readonly SettingsStore _settings;
        private bool _syncing;

        private sealed class Entry
        {
            public string Root;
            public string Text;
            public override string ToString() => Text;
        }

        public InstallPicker(SettingsStore settings)
        {
            _settings = settings;
            DropDownStyle = ComboBoxStyle.DropDownList;

            Items.Add(new Entry { Root = null, Text = Lang.T("すべてのバージョン", "All versions") });
            foreach (var install in LevelWriter.FindInstalls())
            {
                string text = install.Name;
                if (!string.IsNullOrEmpty(install.GameVersion) && text.IndexOf(install.GameVersion, StringComparison.OrdinalIgnoreCase) < 0)
                    text += " (" + install.GameVersion + ")";
                Items.Add(new Entry { Root = install.Root, Text = text });
            }

            if (!_loaded)
            {
                _loaded = true;
                string saved = settings?.GetString(SettingKey, "") ?? "";
                Selected = saved.Length == 0 ? null : saved;
            }

            SelectCurrent();

            SelectedIndexChanged += (s, e) => OnPicked();
            Changed += OnChangedElsewhere;
        }

        /// <summary>覚えているものを選ぶ。もう無い（消したインスタンス）なら全部にする。</summary>
        private void SelectCurrent()
        {
            _syncing = true;
            try
            {
                int index = 0;
                for (int i = 0; i < Items.Count; i++)
                {
                    if (SameRoot(((Entry)Items[i]).Root, Selected)) { index = i; break; }
                }
                SelectedIndex = index;
                if (index == 0) Selected = null;
            }
            finally
            {
                _syncing = false;
            }
        }

        private void OnPicked()
        {
            if (_syncing) return;

            var entry = SelectedItem as Entry;
            Selected = entry?.Root;

            if (_settings != null)
            {
                _settings.Set(SettingKey, Selected ?? "");
                _settings.Save();
            }

            Changed?.Invoke();
        }

        private void OnChangedElsewhere()
        {
            if (IsDisposed) return;
            var entry = SelectedItem as Entry;
            if (!SameRoot(entry?.Root, Selected)) SelectCurrent();
        }

        /// <summary>選択肢がどれも切れずに見える幅にする。</summary>
        public void FitWidth(int deviceDpi)
        {
            int widest = 0;
            foreach (var item in Items)
                widest = Math.Max(widest, TextRenderer.MeasureText(item.ToString(), Font).Width);
            Width = widest + SystemInformation.VerticalScrollBarWidth + (int)Math.Round(12 * deviceDpi / 96.0);
            DropDownWidth = Width;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Changed -= OnChangedElsewhere;
            base.Dispose(disposing);
        }

        private static bool SameRoot(string a, string b)
        {
            if (a == null || b == null) return a == null && b == null;
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
