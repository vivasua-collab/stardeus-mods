using System;
using Game.Constants;
using Game.Data.Factions;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.UI;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WInputRebind : WIconText {
        private MultiBinding binding;
        [SerializeField] private TMP_Text keyboardLabel;
        [SerializeField] private TMP_Text mouseLabel;
        [SerializeField] private TMP_Text gamepadLabel;
        [SerializeField] private TMP_Text rebindLabel;
        [SerializeField] private GameObject rebindBG;
        [SerializeField] private WButtonSubmenu popoutButton;
        [SerializeField] private InputRebindButton[] keyboardButtons;
        [SerializeField] private InputRebindButton[] gamepadButtons;
        [SerializeField] private InputRebindButton mouseButton;
        [SerializeField] private WButtonText btnToggle;
        [SerializeField] private WButtonText btnReset;

        private Action reloadTabAction;


        private void Awake() {
            keyboardLabel.SetText(T.InputKeyboard);
            mouseLabel.SetText(T.InputMouse);
            gamepadLabel.SetText(T.InputGamepad);
            // rebindBG.SetActiveFast(false);
            rebindLabel.SetText(string.Empty);
            btnReset.Load(T.Reset, ResetBinding);
            foreach (var b in keyboardButtons) {
                b.SetOnUnbind(OnUnbind);
            }
            foreach (var b in gamepadButtons) {
                b.SetOnUnbind(OnUnbind);
            }
            mouseButton.SetOnUnbind(OnUnbind);
        }

        // Reloads every slot of the row: unbinding the last binding of an action
        // replaces its InputAction, and a slot with two bindings shows the next one
        private void OnUnbind(BindingSlot slot) {
            Load(binding, reloadTabAction);
            OnEndRebind(slot);
        }

        private void ResetBinding() {
            if (!binding.InputAction.enabled) {
                Toggle();
            }
            binding.ResetToDefaults();
            The.SysSig.BindingChanged.Send(binding.Action);
            if (binding.AutoSwapWithAction != ActionType.None) {
                var autoSwap = The.Bindings.GetBinding(binding.AutoSwapWithAction);
                The.Bindings.SwapIfSame(binding, autoSwap, BindingSlot.Keyboard);
                The.Bindings.SwapIfSame(binding, autoSwap, BindingSlot.Mouse);
                The.Bindings.SwapIfSame(binding, autoSwap, BindingSlot.Gamepad);
            }
            if (binding.Action == ActionType.Click) {
                UIPoolRoot.Activator.SetClickAction(binding.InputAction);
            }
            Load(binding, reloadTabAction);
            popoutButton.HideSubmenu();
        }

        private void Toggle() {
            if (binding.InputAction.enabled) {
                binding.Disable();
            } else {
                binding.Enable();
            }
            Load(binding, reloadTabAction);
            UISounds.PlayDigitalConfirm();
            popoutButton.HideSubmenu();
            The.SysSig.BindingChanged.Send(binding.Action);
        }

        public void Load(MultiBinding b, Action reloadTabAction) {
            if (b.IsHidden) {
                D.Throw("Trying to load hidden binding: {0}", b);
            }
            this.reloadTabAction = reloadTabAction;
            image.SetActiveFast(false);
            SetImage(IconId.CWarning);
            binding = b;
            // var glyphs = b.AllControlGlyphs(out var tooltip, showEveryting: true);
            if (b.Type == BindingType.Vector2) {
                SetupVector2();
            } else if (b.Type == BindingType.Axis) {
                SetupAxis();
            } else if (b.Type == BindingType.Button) {
                SetupButton();
            } else {
                D.Err("Unknown binding type: {0}", b.Type);
            }
            if (b.InputAction.enabled) {
                SetText(b.ActionName);
                btnToggle.Load(T.Deactivate, Toggle);
                rebindLabel.SetText(string.Empty);
            } else {
                SetText(Texts.Strike(b.ActionName));
                btnToggle.Load(T.Activate, Toggle);
                rebindLabel.SetText(T.StateDisabled);
            }
            RefreshConflicts();
        }

        // A rebind of any row can create or resolve a conflict on this one
        public void RefreshConflicts() {
            image.SetActiveFast(The.Bindings.HasConflicts(binding));
        }

        private void SetupAxis() {
            if (ShowKeyboard(2) && !InputRebindButton.TryLoadAxis(binding,
                    BindingSlot.Keyboard, keyboardButtons[0], keyboardButtons[1],
                    OnStartRebind, OnEndRebind)) {
                HideKeyboard();
            }
            if (ShowMouse()) {
                mouseButton.Load(binding, BindingSlot.Mouse,
                    OnStartRebind, OnEndRebind);
            }
            if (ShowGamepad(2) && !InputRebindButton.TryLoadAxis(binding,
                    BindingSlot.Gamepad, gamepadButtons[0], gamepadButtons[1],
                    OnStartRebind, OnEndRebind)) {
                HideGamepad();
            }
        }

        private void SetupButton() {
            if (ShowKeyboard(1)) {
                keyboardButtons[0].Load(binding, BindingSlot.Keyboard,
                    OnStartRebind, OnEndRebind);
            }
            if (ShowMouse()) {
                mouseButton.Load(binding, BindingSlot.Mouse,
                    OnStartRebind, OnEndRebind);
            }
            if (ShowGamepad(1)) {
                gamepadButtons[0].Load(binding, BindingSlot.Gamepad,
                    OnStartRebind, OnEndRebind);
            }
        }

        private void SetupVector2() {
            if (ShowKeyboard(4) && !InputRebindButton.TryLoadComposite(binding,
                    BindingSlot.Keyboard, keyboardButtons[0], keyboardButtons[1],
                    keyboardButtons[2], keyboardButtons[3], OnStartRebind, OnEndRebind)) {
                HideKeyboard();
            }
            mouseLabel.SetActiveFast(false);
            mouseButton.SetActiveFast(false);
            if (ShowGamepad(1)) {
                gamepadButtons[0].Load(binding, BindingSlot.Gamepad,
                    OnStartRebind, OnEndRebind);
            }
        }

        private bool ShowMouse() {
            var constr = binding.GetConstraints(BindingSlot.Mouse);
            var show = !constr.IsHidden && binding.InputAction.enabled;
            mouseButton.SetActiveFast(show);
            mouseLabel.SetActiveFast(show);
            return show;
        }

        private bool ShowKeyboard(int numButtons = 1) {
            var constr = binding.GetConstraints(BindingSlot.Keyboard);
            var show = !constr.IsHidden && binding.InputAction.enabled;
            for (int i = 0; i < 4; i++) {
                keyboardButtons[i].SetActiveFast(show && i < numButtons);
            }
            keyboardLabel.SetActiveFast(show);
            return show;
        }
        // A slot with no composite of its own: nothing there can be bound
        private void HideKeyboard() {
            foreach (var b in keyboardButtons) {
                b.SetActiveFast(false);
            }
            keyboardLabel.SetActiveFast(false);
        }

        private void HideGamepad() {
            foreach (var b in gamepadButtons) {
                b.SetActiveFast(false);
            }
            gamepadLabel.SetActiveFast(false);
        }

        private bool ShowGamepad(int numButtons = 1) {
            var constr = binding.GetConstraints(BindingSlot.Gamepad);
            var show = !constr.IsHidden && binding.InputAction.enabled;
            for (int i = 0; i < 2; i++) {
                gamepadButtons[i].SetActiveFast(show && i < numButtons);
            }
            gamepadLabel.SetActiveFast(show);
            return show;
        }
        private void OnEndRebind(BindingSlot slotType) {
            // rebindBG.SetActiveFast(false);
            rebindLabel.SetText(string.Empty);
            bool needsReload = false;
            if (binding.AutoSwapWithAction != ActionType.None) {
                var autoSwapBind = The.Bindings.GetBinding(binding.AutoSwapWithAction);
                The.Bindings.SwapIfSame(binding, autoSwapBind, slotType);
                needsReload = true;
            }
            if (binding.Action == ActionType.Click) {
                UIPoolRoot.Activator.SetClickAction(binding.InputAction);
            }
            if (needsReload) {
                reloadTabAction.Invoke();
            }
            The.Bindings.ValidateConflicts();
            The.SysSig.BindingChanged.Send(binding.Action);
        }

        private void OnStartRebind() {
            rebindLabel.SetText(T.ClickInputToRebind);
            // rebindBG.SetActiveFast(true);
        }


        public override string ToString() {
            return $"WInputRebind[{lastText}]";
        }
    }
}
