using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Game.Data;
using Game.Utils;
using KL.UI;
using Game.Constants;
using Game.Systems.UIUX;

namespace Game.UI {
    /// <summary>
    /// Handles the horizontal tier lines in the research tree
    /// </summary>
    public class UIResearchTierMarker : WBase {
        private int Tier;
        private Tooltip tooltip;
        private Lifetime tooltipLife;
        public Image TierLine;
        public TMP_Text TierLabelText;
        public Vector2 Center;
        // public Node TierNode;
        public void Reposition(float x, float y, float width) {
            var rekt = Rect;
            rekt.RepositionTopLeft(x, y);
            rekt.Resize(width, rekt.sizeDelta.y);
        }

        public void Load(Lifetime lifetime, GameState s, int tier) {
            Tier = tier;
            var cnt = s.Research.ResearchTierCounts[Tier];
            var isUnlocked = s.Research.IsTierUnlocked(
                Tier, out var toUnlockBelow);
            var unlocked = cnt.Item1;
            var total = cnt.Item2;
            var tierText = T.TierNum_V1.F(Tier);
            var unlockPct = Units.Percentage(unlocked / (float) total);
            var xofy = Units.XOutOfY(unlocked, total);
            string tip;
            if (isUnlocked) {
                TierLine.color = The.UITheme.GetColor("ResearchTierLine_Unlocked", gameObject);
                var progressText = $"{xofy} ({unlockPct})";
                TierLabelText.SetText(
                    $"{tierText}\n<size=70%><alpha=#40>{progressText}");
                if (s.Research.IsTierUnlocked(Tier + 1, out var toUnlock)) {
                    tip = $"{Texts.Accented(tierText)}\n{Texts.Green(progressText)}";
                    // UIBuilder.AddTooltip(TierLabelText, tip);
                    // UIBuilder.AddTooltip(TierLine.gameObject, tip);
                } else {
                    tip = T.TierLockedTip_V3.F(toUnlock, Tier, Tier + 1);
                    tip = $"{Texts.Accented(tierText)}\n{Texts.Green(progressText)}\n{tip}";
                    // UIBuilder.AddTooltip(TierLabelText, tip);
                    // UIBuilder.AddTooltip(TierLine.gameObject, tip);
                }
            } else {
                TierLine.color = The.UITheme.GetColor("ResearchTierLine_Locked", gameObject);
                var progressText = $"{xofy} ({T.ResearchLocked})";
                TierLabelText.SetText($"{tierText}\n<size=70%><alpha=#40>{progressText}");
                tip = T.TierLockedTip_V3.F(toUnlockBelow, Tier - 1, Tier);
                tip = $"{Texts.Accented(tierText)}\n{Texts.Red(progressText)}\n{tip}";
            }
            if (tooltip != null && tooltipLife == lifetime) {
                tooltip.Tip = Texts.Beautify(tip);
            } else {
                tooltip = UIUtils.AddTip(lifetime, this, tip);
                tooltipLife = lifetime;
            }
        }
    }
}
