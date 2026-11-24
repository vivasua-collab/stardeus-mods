using System;
using Game.Constants;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WButtonText : WText, IPointerClickHandler, IPointerDownHandler {
        [SerializeField] protected Button button;
        [SerializeField] protected UIThemedButton buttonTheme;
        public Button Button => button;
        private Action onClick;
        // Button.onClick only hears the left button; the right one comes through
        // IPointerClickHandler on the same object (job priorities: right click lowers)
        private Action onRightClick;
        private void Awake() {
            RigButton();
        }

        protected void RigButton() {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }
        public void SetInteractable(bool yes) {
            button.interactable = yes;
        }
        public void SetRaycastable(bool yes) {
            button.targetGraphic.raycastTarget = yes;
        }

        public void OverrideButtonTheme(string id) {
            if (buttonTheme == null) {
                D.Err("Button theme not linked for button: {0}", D.GOPath(transform));
                buttonTheme = GetComponent<UIThemedButton>();
            }
            if (id == null) {
                buttonTheme.ClearOverride();
            } else {
                buttonTheme.Override(id);
            }
        }

        public void Load(string text, Action act) {
            UIActivator.EnsureDeselected(gameObject);
            SetInteractable(true);
            if (buttonTheme != null) {
                OverrideButtonTheme(null);
            }
            // panel headers use this and some of them won't have text
            if (textMain != null) {
                SetText(text);
            }
            SetOnClick(act);
            onRightClick = null;
        }

        public void SetOnClick(Action act) {
            // D.Warn("Setting on click: {0} -> {1}", D.GOPath(transform), act?.Target);
            onClick = act;
        }

        protected void OnClick() {
            // D.Warn("Clicking: {0}", D.GOPath(transform));
            if (onClick == null) {
                D.Err("OnClick not rigged for {0} -> {1}", this, D.GOPath(transform));
            } else {
                onClick.Invoke();
            }
        }

        public void SetOnRightClick(Action act) {
            onRightClick = act;
        }

        public void OnPointerClick(PointerEventData eventData) {
            if (!TakesRightClick(eventData)) { return; }
            onRightClick.Invoke();
        }

        // The right button is also the game's cancel, which closes the UI under the cursor
        // (UserInputEscHandler.CloseAnyUI): marked on press, as the release can come before
        // this object hears the click
        public void OnPointerDown(PointerEventData eventData) {
            if (!TakesRightClick(eventData)) { return; }
            G.RightClickTakenByUI = true;
        }

        private bool TakesRightClick(PointerEventData eventData) {
            if (onRightClick == null) { return false; }
            if (eventData.button != PointerEventData.InputButton.Right) { return false; }
            // a hidden cell only disables its Button, and keeps the callback of the row
            // it showed before (pooled rows)
            return button.isActiveAndEnabled && button.IsInteractable();
        }

        public void Click() {
            onClick?.Invoke();
        }
        public override string ToString() {
            return $"WButtonText[{lastText}]";
        }
    }
}
