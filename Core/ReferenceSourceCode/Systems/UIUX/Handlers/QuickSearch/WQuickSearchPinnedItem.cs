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
    public sealed class WQuickSearchPinnedItem : WBase, IPointerEnterHandler,
            IPointerExitHandler, ICustomPoolable {
        [SerializeField] private WIconText wIconText;
        [SerializeField] private WButton wButton;
        [SerializeField] private WButtonIcon wButtonPin;
        [SerializeField] private WQuickSearchPinnedItemDragHandle wDragHandle;
        public Image Image => wIconText.Image;
        private Action<bool> onHover;
        private bool isHovering;
        private double hoverStartTime;
        private Lifetime life;
        private Def def;
        private BaseSearchResult result;
        public BaseSearchResult Result => result;

        public void SetDef(Lifetime life, Def def) {
            this.life = life;
            this.def = def;
        }

        public void SetOnHover(Action<bool> onHover) {
            this.onHover = onHover;
        }

        public void Load(string icon, string title, Action onClick, BaseSearchResult res) {
            wIconText.Load(icon, title);
            result = res;
            wButton.Load(onClick);
            wButtonPin.Load(IconId.WClose, () => A.S.UIQuery.PinQuickSearchResult.Ask(res));
            wButtonPin.SetActiveFast(false);
        }
        public void Load(Sprite sprite, string title, Action onClick, BaseSearchResult res) {
            result = res;
            wIconText.Load(sprite, title);
            wButton.Load(onClick);
            wButtonPin.Load(IconId.WClose, () => A.S.UIQuery.PinQuickSearchResult.Ask(res));
            wButtonPin.SetActiveFast(false);
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (isHovering) { return; }
            hoverStartTime = GameTimer.realtimeSinceStartupAsDouble;
            isHovering = true;
            if (onHover != null) {
                onHover(true);
            } else if (def != null && !eventData.dragging) {
                A.S.UIQuery.ShowCodexTooltipFor.Ask(life,
                    CodexTooltip.CreateFor(Rect.parent.GetComponentInParent<WBase>(),
                        def, optional: false, showDelay: Tooltip.DelaySeconds));
            }
            wButtonPin.SetActiveFast(true);
        }

        public bool IsFor(BaseSearchResult res) {
            return Equals(res, result);
        }

        private void Update() {
            if (!isHovering || wDragHandle.IsShowing) { return; }
            if (GameTimer.realtimeSinceStartupAsDouble - hoverStartTime > 2f) {
                wDragHandle.FadeIn();
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (!isHovering) { return; }
            wDragHandle.HideUnlessDragging();
            isHovering = false;
            if (onHover != null) {
                onHover(false);
            } else if (def != null) {
                A.S.UIQuery.HideCodexTooltip.Ask();
            }
            wButtonPin.SetActiveFast(false);
        }

        private void OnDisable() {
            OnPointerExit(null);
            isHovering = false;
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
            result = null;
            def = null;
            life = null;
            onHover = null;
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
        }
    }
}
