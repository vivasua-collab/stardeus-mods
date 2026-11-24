using System;
using Game.CodeGen;
using Game.Constants;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Uween;

namespace Game.Systems.Creatures {
    public sealed class BeingGroupUIWidget : WBase, ICustomPoolable {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private Button button;
        [SerializeField] private GameObject arrowGo;
        public int Number;
        private BeingGroup group;

        private void Awake() {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }

        public void Load(BeingGroup g, int number) {
            // D.Warn("Loading being group UI: {0}", g);
            #if UNITY_EDITOR
            name = $"BeingGroupUIWidget_{g.Id}";
            #endif
            Number = number;
            group = g;
            g.UI = this;
            if (title != null) {
                title.SetText(g.DecoratedName(false));
            }
            countText.SetText(Units.Num(g.Members.Count));
            if (arrowGo != null) {
                arrowGo.SetActiveFast(g.AnyMembersVisible);
            }
            if (icon != null) {
                if (group.IdH == BeingGroupIdH.Other) {
                    icon.sprite = Images.Sprite(IconId.CSkull);
                } else if (group.IdH == BeingGroupIdH.Enemy) {
                    icon.sprite = Images.Sprite(IconId.CCombat);
                } else if (group.IdH == BeingGroupIdH.Guest) {
                    // Widget is pooled - without an explicit sprite it would otherwise
                    // inherit whichever icon the previous occupant of this slot had.
                    icon.sprite = Images.Sprite(IconId.CPersona);
                }
            }
        }

        private void OnClick() {
            A.S.Query.ToggleBeingGroupUI.Ask(group);
        }

        public void PoolActivate() {
            gameObject.SetActive(true);
        }

        public void PoolDeactivate() {
            if (group != null) {
                group.UI = null;
                group = null;
            }
            gameObject.SetActive(false);
        }
    }
}
