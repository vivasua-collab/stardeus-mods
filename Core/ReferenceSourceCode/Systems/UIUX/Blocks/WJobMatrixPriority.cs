using System;
using Game.UI;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Systems.UIUX {
    // Job Matrix cell: the job priority number with the skill level below it. Left click
    // raises the priority, right click lowers it (WButtonText.SetOnRightClick)
    public sealed class WJobMatrixPriority : WButtonText, IPointerEnterHandler,
            IPointerExitHandler {
        [SerializeField] private TMP_Text priorityText;
        [SerializeField] private UIThemedImage themedImage;
        [SerializeField] private string invisibleTheme;
        [SerializeField] private MonoBehaviour[] disableWhenInactive;
        private Func<string> tooltipFunc;
        private string lastPriority = ".";

        public void Load(string levelText, string priority, Action onRaise, Action onLower) {
            Load(levelText, onRaise);
            SetOnRightClick(onLower);
            SetPriority(priority);
        }

        public void SetPriority(string priority) {
            UIUtils.SetText(priorityText, priority, ref lastPriority, false);
        }

        public void SetTooltip(Func<string> func) {
            tooltipFunc = func;
        }

        public void SetVisible(bool isVisible) {
            if (!isVisible) {
                themedImage.Override(invisibleTheme);
            } else {
                themedImage.ClearOverride();
            }
            foreach (var comp in disableWhenInactive) {
                comp.enabled = isVisible;
            }
            priorityText.SetActiveFast(isVisible);
            textMain.SetActiveFast(isVisible);
        }

        public void OnPointerEnter(PointerEventData data) {
            if (tooltipFunc != null) {
                The.SysSig.ShowDynamicTooltip.Send(tooltipFunc, true);
            }
        }

        public void OnPointerExit(PointerEventData data) {
            The.SysSig.HideTooltip.Send();
        }

        public override string ToString() {
            return $"WJobMatrixPriority[{lastPriority}|{lastText}]";
        }
    }
}
