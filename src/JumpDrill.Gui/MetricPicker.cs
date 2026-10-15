using System;
using System.Windows.Forms;
using JumpDrill.Replays;

namespace JumpDrill.Gui
{
    /// <summary>
    /// 順位・ベスト・メダルを再現精度 % で見るか、精度 %（ゲームの Acc）で見るかのプルダウン。
    /// スコア画面とメダル画面で同じものを使う。
    /// </summary>
    /// <remarks>
    /// ボタンで切り替えると、いまどちらを出しているのかが分かりにくいので、プルダウンにしてある。
    /// 選んだものは2つの画面で揃え、次の起動まで覚えておく。
    /// </remarks>
    internal sealed class MetricPicker : ComboBox
    {
        private const string SettingKey = "rankMetric";

        /// <summary>どちらかの画面で選び直された。もう一方も合わせて出し直す。</summary>
        public static event Action Changed;

        /// <summary>いま見ている数字。</summary>
        public static RankMetric Current { get; private set; }

        private static bool _loaded;

        private readonly SettingsStore _settings;
        private readonly ToolTip _tips = new ToolTip();
        private bool _syncing;

        public MetricPicker(SettingsStore settings)
        {
            _settings = settings;
            DropDownStyle = ComboBoxStyle.DropDownList;

            // 並びは RankMetric の値と同じ
            Items.Add(CaptionOf(RankMetric.Reproducibility));
            Items.Add(CaptionOf(RankMetric.Accuracy));

            if (!_loaded)
            {
                _loaded = true;
                Current = settings != null && settings.GetString(SettingKey, "") == "acc"
                    ? RankMetric.Accuracy : RankMetric.Reproducibility;
            }

            SelectCurrent();
            _tips.SetToolTip(this, Lang.T("順位・ベスト・メダルを決める数字", "The number used for rank, best and medals"));

            SelectedIndexChanged += (s, e) => OnPicked();
            Changed += SelectCurrent;
        }

        /// <summary>いま見ている数字の名前（見出しなどに使う）。</summary>
        public static string Caption
        {
            get { return CaptionOf(Current); }
        }

        private static string CaptionOf(RankMetric metric)
        {
            return metric == RankMetric.Accuracy ? Lang.T("精度 %", "Acc %") : Lang.T("再現精度 %", "Repro acc %");
        }

        private void SelectCurrent()
        {
            if (IsDisposed) return;
            _syncing = true;
            try { SelectedIndex = (int)Current; }
            finally { _syncing = false; }
        }

        private void OnPicked()
        {
            if (_syncing || SelectedIndex < 0) return;

            var picked = (RankMetric)SelectedIndex;
            if (picked == Current) return;
            Current = picked;

            if (_settings != null)
            {
                _settings.Set(SettingKey, Current == RankMetric.Accuracy ? "acc" : "repro");
                _settings.Save();
            }

            Changed?.Invoke();
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
            if (disposing)
            {
                Changed -= SelectCurrent;
                _tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
