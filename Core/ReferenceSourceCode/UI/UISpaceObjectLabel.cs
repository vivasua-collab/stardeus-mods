using Game.Data.Space;
using Game.Systems.UIUX;
using Game.UI.Style;
using KL.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI {
    public sealed class UISpaceObjectLabel : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler {
        public UIClickableHoverable Button;
        [SerializeField] private UIThemedImage imageTheme;
        [SerializeField] private string colorHover;

        public void OnHover(bool isHovering) {
            if (isHovering) {
                imageTheme.OverrideColor(colorHover);
            } else {
                imageTheme.ClearColorOverride();
            }
        }

        public void OnPointerClick(PointerEventData eventData) {
            Button.OnPointerClick(eventData);
        }

        public void OnPointerEnter(PointerEventData eventData) {
            Button.OnPointerEnter(eventData);
        }

        public void OnPointerExit(PointerEventData eventData) {
            Button.OnPointerExit(eventData);
        }
    }
}
