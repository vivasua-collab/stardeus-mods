using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class WallOfTextWidget : BaseWidget {
        [SerializeField] private WButton closeButton;
        [SerializeField] private WText wallOfText;
        [SerializeField] private ScrollRect scroll;

        public void Load(string text, Action onClose, bool centered = false) {
            wallOfText.SetText(text);
            scroll.verticalNormalizedPosition = 1f;
            if (centered) {
                wallOfText.Text.alignment = TMPro.TextAlignmentOptions.Center;
            } else {
                wallOfText.Text.alignment = TMPro.TextAlignmentOptions.TopLeft;
            }
            closeButton.Load(onClose);
        }
    }
}
