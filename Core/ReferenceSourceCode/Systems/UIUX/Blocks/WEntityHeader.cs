using Game.Data.Factions;
using Game.Utils;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WEntityHeader : WButtonIconText2 {
        [SerializeField] private UITextTitleSubtitleResizer resizer;
        [SerializeField] private Image factionIcon;
        public UIHoverCallback HoverCallback;
        private FactionDef lastFaction;
        protected override bool IsPoolable => false;
        public void SetTexts(string title, string subtitle) {
            SetText(title);
            SetText2(subtitle);
            if (resizer != null && resizer.enabled) {
                resizer.AutoResize();
            }
        }

        public void SetFactionIcon(FactionDef faction) {
            if (faction == null) {
                factionIcon.SetActiveFast(false);
            }
            if (lastFaction == faction) {
                return;
            }
            lastFaction = faction;
            if (faction == null) {
                factionIcon.SetActiveFast(false);
            } else {
                factionIcon.sprite = Images.Sprite(faction.FlagIcon);
                factionIcon.SetActiveFast(true);
            }
        }

        public void AutoResize() {
            if (resizer != null && resizer.enabled) {
                resizer.AutoResize();
            }
        }
        public override string ToString() {
            return $"WEntityheader[{lastText}]";
        }
    }
}
