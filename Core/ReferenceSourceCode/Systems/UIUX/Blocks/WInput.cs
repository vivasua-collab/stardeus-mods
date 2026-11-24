using System;
using Game.Input;
using Game.UI;
using KL.UI;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WInput : WBase, IPointerDownHandler {
        [SerializeField] private TMP_InputField input;
        [SerializeField] private TMP_Text placeholder;
        [SerializeField] private UITextInputFocus focus;
        public string Text => input.text;

        public bool IsFocused => input.isFocused;
        public TMP_InputField InputField => input;

        private Action<string> onChange;
        private Action<string> onSubmit;
        public Action OnClick;
        private void Awake() {
            input.onValueChanged.AddListener(OnChange);
            input.onSubmit.AddListener(OnSubmit);
            if (focus == null) {
                D.Err("WInput missing focus component: {0}", D.GOPath(transform));
            } else {
                focus.OnClearInput = OnClearInput;
            }
        }

        /// <summary>
        /// One use case is in trade ui, when we press X to clear input, we want
        /// a quick choice of what to replace the value with, that's done in
        /// on click callback
        /// </summary>
        private void OnClearInput() {
            OnClick?.Invoke();
        }

        private void OnSubmit(string val) {
            onSubmit?.Invoke(val);
        }

        private void OnChange(string val) {
            onChange?.Invoke(val);
        }

        public void Load(string placeholder, Action<string> onChange) {
            Load(placeholder, null, onChange);
        }
        public void Load(string placeholder, string value, Action<string> onChange) {
            ToggleClearButton(true);
            OnClick = null;
            // pooled: a reused input ran the previous owner's submit handler on Enter
            // (TileLabelTab's ReloadTab), keeping that tab alive
            onSubmit = null;
            SetPlaceholder(placeholder);
            SetValue(value, notify: false);
            SetOnChange(onChange);
        }

        public void ToggleClearButton(bool show) {
            focus.ToggleClearButton(show);
        }

        public void SetOnSubmit(Action<string> onSubmit) {
            this.onSubmit = onSubmit;
        }
        public void SetOnChange(Action<string> onChange) {
            this.onChange = onChange;
        }

        public void SetValue(string value, bool notify = true) {
            if (string.IsNullOrWhiteSpace(value)) {
                value = null;
            }
            if (notify) {
                input.text = value;
            } else {
                input.SetTextWithoutNotify(value);
            }
            focus.UpdateInputClearButton(value);
        }

        protected void Clear() {
            input.SetTextWithoutNotify(string.Empty);
            focus.UpdateInputClearButton(null);
        }

        public bool Focus() {
            UIUtils.FocusInput(input);
            return true;
        }
        public void Unfocus() {
            if (!input.isFocused) { return; }
            UIActivator.Deselect();
            Ready.SetTextInputFocus(true, input, "unfocusing");
        }

        public void SetPlaceholder(string v) {
            placeholder.SetText(v);
        }

        public void OnPointerDown(PointerEventData evt) {
            D.Warn("pointer down on WInputAmt: {0}", evt.button);
            if (evt.button == PointerEventData.InputButton.Left) {
                D.Warn("Invoking click on WInputAmt: {0}", OnClick);
                OnClick?.Invoke();
            }
        }
        public override string ToString() {
            return $"WInput[{placeholder.text}]";
        }

    }
}
