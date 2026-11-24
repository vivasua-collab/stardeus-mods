using System;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Systems.UIUX {
    public sealed class WJobMatrixToggle : WToggleText, IPointerEnterHandler,
            IPointerExitHandler {
        private Func<string> tooltipFunc;
        [SerializeField] private UIThemedImage themedImage;
        [SerializeField] private string invisibleTheme;
        [SerializeField] private MonoBehaviour[] disableWhenInactive;
        [SerializeField] private GameObject checkmark;

        public void SetTooltip(Func<string> func) {
            tooltipFunc = func;
        }

        public void SetVisible(bool isToggleVisible) {
            if (!isToggleVisible) {
                themedImage.Override(invisibleTheme);
            } else {
                themedImage.ClearOverride();
            }
            foreach (var comp in disableWhenInactive) {
                comp.enabled = isToggleVisible;
            }
            checkmark.SetActiveFast(isToggleVisible);
            textMain.SetActiveFast(isToggleVisible);
        }

        public void OnPointerEnter(PointerEventData data) {
            if (tooltipFunc != null) {
                The.SysSig.ShowDynamicTooltip.Send(tooltipFunc, true);
            }
        }

        public void OnPointerExit(PointerEventData data) {
            The.SysSig.HideTooltip.Send();
        }
    }
}
