using Game.Constants;
using Game.UI;
using UnityEngine.EventSystems;

namespace Game.Systems.UIUX {
    public sealed class WStarmapContent : WBase, IPointerDownHandler {
        private StarmapContext ctx;
        public void Initialize(StarmapContext ctx) {
            this.ctx = ctx;
        }

        public void OnPointerDown(PointerEventData eventData) {
            if (ctx.Layer != UniverseLayer.Sector) { return; }
            // A press on a space object bubbles up here too, but the object handles its own
            // click (UISpaceObject.OnClick). Arming the background latch as well made one RMB
            // order the same flight twice, stacking its popups (STA-1602).
            var hit = eventData.pointerCurrentRaycast.gameObject;
            if (hit != null && hit.GetComponentInParent<UISpaceObject>() != null) { return; }
            G.HasClickedDownInStarmap = true;
        }
    }
}
