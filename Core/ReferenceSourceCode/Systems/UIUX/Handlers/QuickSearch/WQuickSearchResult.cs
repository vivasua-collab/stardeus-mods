using System;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class WQuickSearchResult : WBase, IPointerEnterHandler,
            IPointerExitHandler, ICustomPoolable {
        [SerializeField] private WIconText2 wIconText2;
        [SerializeField] private WButton wButton;
        [SerializeField] private WButtonIcon wButtonPin;
        public Image Image => wIconText2.Image;
        private Action<bool> onHover;
        private bool isHovering;
        private bool isPinnable;
        private Def def;
        private Lifetime life;

        public void SetDef(Lifetime life, Def def) {
            this.life = life;
            this.def = def;
        }
        public void SetOnHover(Action<bool> onHover) {
            this.onHover = onHover;
        }
        public void SetTint(Color color) {
            wIconText2.SetTint(color);
        }

        public void Load(string icon, string title, string desc) {
            wIconText2.Load(icon, title, desc);
        }

        public void Load(Sprite sprite, string title, string desc, Action onClick) {
            wIconText2.Load(sprite, title, desc);
            wButton.Load(onClick);
            if (wButtonPin != null) {
                wButtonPin.SetActiveFast(false);
            }
        }

        public void Load(string icon, string title, string desc, Action onClick) {
            wIconText2.Load(icon, title, desc);
            wButton.Load(onClick);
            if (wButtonPin != null) {
                wButtonPin.SetActiveFast(false);
            }
        }

        public void SetPinnable(BaseSearchResult res) {
            if (wButtonPin != null) {
                isPinnable = true;
                wButtonPin.Load(IconId.CPin,
                    () => A.S.UIQuery.PinQuickSearchResult.Ask(res));
                wButtonPin.SetActiveFast(false);
            }
        }

        public void SetNotPinnable() {
            isPinnable = false;
            if (!isPinnable) {
                wButtonPin?.SetActiveFast(false);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (isHovering) { return; }
            isHovering = true;
            if (onHover != null) {
                onHover(false);
            } else if (def != null) {
                A.S.UIQuery.ShowCodexTooltipFor.Ask(life,
                    CodexTooltip.CreateFor(this, def, optional: false,
                        showDelay: 0.1f));
            }
            if (isPinnable && wButtonPin != null) {
                wButtonPin.SetActiveFast(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (!isHovering) { return; }
            isHovering = false;
            if (onHover != null) {
                onHover(false);
            } else if (def != null) {
                A.S.UIQuery.HideCodexTooltip.Ask();
            }
            if (wButtonPin != null) {
                wButtonPin.SetActiveFast(false);
            }
        }

        private void OnDisable() {
            OnPointerExit(null);
            isHovering = false;
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
            def = null;
            life = null;
            onHover = null;
            isPinnable = false;
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
            if (wButtonPin != null) {
                wButtonPin.SetActiveFast(false);
            }
        }
    }
}
