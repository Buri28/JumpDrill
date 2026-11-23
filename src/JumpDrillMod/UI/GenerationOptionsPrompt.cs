using System;
using System.Collections.Generic;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.Parser;
using JumpDrill.Model;
using JumpDrillMod.Services;
using TMPro;
using UnityEngine;

namespace JumpDrillMod.UI
{
    internal sealed class GenerationOptionsPrompt
    {
        private const string Resource = "JumpDrillMod.Resources.GenerationOptions.bsml";

        [UIParams] private BSMLParserParams? parser = null;
        [UIComponent("generation-target")] private TextMeshProUGUI? targetText = null;
        [UIComponent("generation-tone")] private ListSetting? toneSetting = null;
        [UIComponent("generation-count-in")] private IncrementSetting? countInSetting = null;
        [UIComponent("generate-confirm")] private TextMeshProUGUI? generateButtonText = null;

        private GameObject? parsedParent;
        private Action<string, string, int, float>? pending;
        private string click = nameof(ClickMode.Down);

        [UIValue("click-options")]
        public List<object> ClickOptions { get; } = new List<object> { "Down", "Up", "All", "Every4", "None", "Beat" };

        [UIValue("tone-options")]
        public List<object> ToneOptions { get; } = new List<object>(Enum.GetNames(typeof(ClickTone)));

        [UIValue("click")]
        public string Click
        {
            get => click;
            set
            {
                click = value;
                if (toneSetting != null) toneSetting.Interactable = click != nameof(ClickMode.None);
            }
        }

        [UIAction("click-changed")]
        public void ClickChanged(object value) => Click = (string)value;

        [UIValue("tone")] public string Tone { get; set; } = nameof(ClickTone.Standard);
        [UIValue("count-in")] public int CountIn { get; set; } = 8;
        [UIValue("tail")] public float Tail { get; set; } = 1.5f;

        internal void Show(GameObject? parent, string target, bool all, string currentClick, string currentTone,
            int currentCountIn, float currentTail, Action<string, string, int, float> onGenerate,
            string? note = null, string? okText = null)
        {
            if (parent == null) return;
            if (parsedParent != parent)
            {
                pending = null;
                parser = BSMLParser.Instance.Parse(
                    Utilities.GetResourceContent(typeof(GenerationOptionsPrompt).Assembly, Resource), parent, this);
                parsedParent = parent;
            }
            if (parser == null || targetText == null || generateButtonText == null || countInSetting == null) return;

            Click = currentClick;
            Tone = currentTone;
            // 手で保存値を増やしていた場合も、開いたときの値はそのまま表示する。
            countInSetting.MaxValue = Math.Max(DrillSetGenerationService.MaxCountIn, currentCountIn);
            CountIn = Math.Max(0, currentCountIn);
            Tail = Math.Max(0, Math.Min(10, currentTail));
            if (note == null && all) note = "Applies to all maps written. Enable overwrite to change existing maps.";
            targetText.text = string.IsNullOrEmpty(note) ? target : target + "\n" + note;
            generateButtonText.text = okText ?? (all ? "Generate all" : "Generate");
            pending = onGenerate;
            parser.EmitEvent("load-options");
            parser.EmitEvent("show-options");
        }

        [UIAction("cancel")]
        public void Cancel()
        {
            pending = null;
            parser?.EmitEvent("hide-options");
        }

        [UIAction("generate")]
        public void Generate()
        {
            var generate = pending;
            Cancel();
            generate?.Invoke(Click, Tone, CountIn, Tail);
        }
    }
}
