using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Systems.UIUX {
    public sealed class UIShowOnHover : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler {
        [SerializeField] private GameObject target;
        private bool isShowing;
        private void Awake() {
            if (target == null) {
                D.Throw("Target not rigged for UIShowOnHover: {0}", D.GOPath(transform));
            }
            target.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (!isShowing) {
                target.SetActive(true);
                isShowing = true;
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (isShowing) {
                target.SetActive(false);
                isShowing = false;
            }
        }

        private void OnDisable() {
            OnPointerExit(null);
        }
    }
}
