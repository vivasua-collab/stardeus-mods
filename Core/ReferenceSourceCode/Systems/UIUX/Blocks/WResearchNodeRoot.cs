using System;
using Game.Constants;
using Game.Research;
using Game.UI;
using Game.Utils;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WResearchNodeRoot : WBase {
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text hugeTitle;
        private Researchable researchable;
        public string DebugHugeTitle => hugeTitle.text;

        public void Load(Researchable res) {
            researchable = res;
            UpdateResearchable();
        }

        // The category title is centered over its category and kept inside it, so titles never
        // run into each other. One size fits every English title (the layout's minimum category
        // width is set for that); a longer translation shrinks to fit its category.
        // (Anchored to the middle of the root, which is a tile wide.)
        // A title may reach this far into the gap on each side; the gap between categories is
        // much wider, so neighbouring titles keep their distance
        private const float TitleBleed = 50f;

        public void SetLane(float width, float centerOffset) {
            if (width <= 0) { return; }
            var rt = hugeTitle.rectTransform;
            rt.sizeDelta = new Vector2(width + 2 * TitleBleed, rt.sizeDelta.y);
            rt.anchoredPosition = new Vector2(centerOffset, rt.anchoredPosition.y);
        }

        public void UpdateResearchable() {
            var res = researchable;
            var isDiscovered = A.S.Research.IsDiscoveredOrSandbox(res.Def.IdH);
            var name = isDiscovered ? res.Def.NameT : T.UndiscoveredResearch;
            var bigName = isDiscovered ? name : "???";
            background.color = isDiscovered ? WResearchNode.ColorAvailable : WResearchNode.ColorNotDiscovered;
            icon.sprite = isDiscovered ? res.Def.PreviewSprite : Images.Sprite(IconId.CQuestion);
            nameText.text = name;
            hugeTitle.text = bigName;
        }
    }
}
