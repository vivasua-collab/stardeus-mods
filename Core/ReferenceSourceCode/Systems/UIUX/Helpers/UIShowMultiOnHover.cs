using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Systems.UIUX {
    public sealed class UIShowMultiOnHover : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler {
        [SerializeField] private GameObject[] targets;
        private bool isShowing;
        private void Awake() {
            if (targets.Length == 0) {
                D.Throw("Targets not rigged for UIShowOnHover: {0}", D.GOPath(transform));
            }
            for (int i = targets.Length - 1; i >= 0; i--) {
                GameObject t = targets[i];
                t.SetActive(false);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (!isShowing) {
                isShowing = true;
                for (int i = targets.Length - 1; i >= 0; i--) {
                    GameObject t = targets[i];
                    t.SetActive(true);
                }
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (isShowing) {
                isShowing = false;
                for (int i = targets.Length - 1; i >= 0; i--) {
                    GameObject t = targets[i];
                    t.SetActive(false);
                }
            }
        }
        private void OnDisable() {
            OnPointerExit(null);
        }
    }
}
