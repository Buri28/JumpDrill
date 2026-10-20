using System;
using BeatSaberMarkupLanguage.Parser;
using TMPro;

namespace JumpDrillMod.UI
{
    /// <summary>
    /// 削除の前に出す確かめの窓（BSML の modal）。押し間違えで消さないように、何を消すかを書いて「Delete」を押させる。
    /// </summary>
    /// <remarks>
    /// 窓のマークアップは <see cref="Markup"/>。ビューの BSML のルート直下に置き、
    /// <c>confirm-text</c> をビューの欄で受けて <see cref="Ask"/> に渡し、<c>confirm-yes</c>・<c>confirm-no</c> を <see cref="Yes"/>・<see cref="No"/> につなぐ。
    /// </remarks>
    internal sealed class ConfirmPrompt
    {
        private Action? pending;

        /// <summary>BSML の modal。<paramref name="width"/> × <paramref name="height"/> の大きさ。</summary>
        internal static string Markup(int width = 64, int height = 26)
        {
            return "<modal id='confirm-modal' show-event='show-confirm' hide-event='hide-confirm' click-off-closes='true'"
                 + " move-to-center='true' size-delta-x='" + width + "' size-delta-y='" + height + "'>"
                 + "<vertical pad='3' spacing='2' child-expand-height='false'>"
                 + "<text id='confirm-text' text='' font-size='3.4' align='Center' word-wrapping='true'/>"
                 + "<horizontal spacing='3' pref-height='7'>"
                 + "<button text='Cancel' on-click='confirm-no' pref-width='22' pref-height='7'/>"
                 + "<button text='&lt;color=#FF8060&gt;Delete&lt;/color&gt;' on-click='confirm-yes' pref-width='22' pref-height='7'/>"
                 + "</horizontal>"
                 + "</vertical>"
                 + "</modal>";
        }

        /// <summary>窓を出す。「Delete」を押したら <paramref name="onYes"/> を呼ぶ。窓が出せなければ何もしない（消さない）。</summary>
        internal void Ask(BSMLParserParams? parserParams, TextMeshProUGUI? text, string message, Action onYes)
        {
            if (parserParams == null || text == null) return;
            pending = onYes;
            text.text = message;
            parserParams.EmitEvent("show-confirm");
        }

        internal void Yes(BSMLParserParams? parserParams)
        {
            var action = pending;
            pending = null;
            parserParams?.EmitEvent("hide-confirm");
            action?.Invoke();
        }

        internal void No(BSMLParserParams? parserParams)
        {
            pending = null;
            parserParams?.EmitEvent("hide-confirm");
        }
    }
}
