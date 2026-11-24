using System;
using System.Collections.Generic;
using Game.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Input {
    // Decides which binding owns a key press when several layers bind the same
    // control. The decision is a snapshot taken once per frame: a handler can
    // change the context mid-frame (a Selection hotkey activating a tool), and
    // without the snapshot the same press would then also fire the lower layer
    // binding it was shadowing a moment ago.
    public sealed class InputLayerResolver {
        private const int LayerCount = 5;
        private readonly Func<bool>[] layerChecks = new Func<bool>[LayerCount];
        private readonly List<MultiBinding> layered = new();
        private readonly List<MultiBinding> pressed = new();
        private int resolvedFrame = -1;

        public InputLayerResolver() {
            layerChecks[(int) InputLayer.Always] = AlwaysActive;
            layerChecks[(int) InputLayer.World] = AlwaysActive;
            layerChecks[(int) InputLayer.Selection] = IsSelectionActive;
            layerChecks[(int) InputLayer.Tool] = IsToolActive;
            layerChecks[(int) InputLayer.ShipCombat] = IsShipCombatActive;
        }

        private static bool AlwaysActive() {
            return true;
        }

        private static bool IsShipCombatActive() {
            return UIShowing.ShipCombatMode;
        }

        private static bool IsToolActive() {
            var toolbox = The.R?.Toolbox;
            if (toolbox?.ActiveTool == null || UIShowing.FullScreenWidget) {
                return false;
            }
            return !toolbox.IsFreeSelect;
        }

        private static bool IsSelectionActive() {
            var toolbox = The.R?.Toolbox;
            if (toolbox?.ActiveTool == null || UIShowing.FullScreenWidget) {
                return false;
            }
            return toolbox.IsFreeSelect && toolbox.S.Query.IsAnythingSelected.Ask();
        }

        public void SetLayerCheck(InputLayer layer, Func<bool> check) {
            layerChecks[(int) layer] = check;
            Invalidate();
        }

        public void Register(MultiBinding binding) {
            // Always layer bindings never take part in shadowing, keeping them
            // out means composites, axes and vmouse never reach the resolver
            if (binding.Layer != InputLayer.Always) {
                layered.Add(binding);
            }
        }

        public void Clear() {
            layered.Clear();
            pressed.Clear();
            Invalidate();
        }

        public void Invalidate() {
            resolvedFrame = -1;
        }

        public bool IsLayerActive(InputLayer layer) {
            return layerChecks[(int) layer].Invoke();
        }

        public bool IsLive(MultiBinding binding) {
            // Modifiers are polled on every hover, keep their path free of delegates
            if (binding.Layer == InputLayer.Always) {
                return binding.Availability == null || binding.Availability.Invoke();
            }
            if (!IsLayerActive(binding.Layer)) {
                return false;
            }
            return binding.Availability == null || binding.Availability.Invoke();
        }

        public bool IsJustPressed(MultiBinding binding) {
            if (binding.Layer == InputLayer.Always) {
                return WasPressedThisFrame(binding, out _) && IsLive(binding);
            }
            var frame = Time.frameCount;
            if (resolvedFrame != frame) {
                ResolveFrame(frame);
            }
            return binding.WonFrame == frame;
        }

        private void ResolveFrame(int frame) {
            resolvedFrame = frame;
            pressed.Clear();
            var count = layered.Count;
            for (int i = 0; i < count; i++) {
                var b = layered[i];
                if (!WasPressedThisFrame(b, out var control) || !IsLive(b)) {
                    continue;
                }
                b.TriggerControl = control;
                pressed.Add(b);
            }
            count = pressed.Count;
            for (int i = 0; i < count; i++) {
                var b = pressed[i];
                if (!IsShadowed(b)) {
                    b.WonFrame = frame;
                }
            }
        }

        private bool IsShadowed(MultiBinding binding) {
            var count = pressed.Count;
            for (int i = 0; i < count; i++) {
                var other = pressed[i];
                if (other.Layer > binding.Layer && SharesControl(binding, other)) {
                    return true;
                }
            }
            return false;
        }

        private static bool SharesControl(MultiBinding a, MultiBinding b) {
            if (a.TriggerControl != null && b.TriggerControl != null) {
                return a.TriggerControl == b.TriggerControl;
            }
            // activeControl is gone when press and release land in one frame
            var aControls = a.InputAction.controls;
            var bControls = b.InputAction.controls;
            for (int i = aControls.Count - 1; i >= 0; i--) {
                for (int j = bControls.Count - 1; j >= 0; j--) {
                    if (aControls[i] == bControls[j]) {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool WasPressedThisFrame(MultiBinding binding, out InputControl control) {
            #if !PRODUCTION_BUILD
            if (simPressFrame == Time.frameCount
                    && TryGetSimControl(binding, simPressed, out control)) {
                return true;
            }
            #endif
            if (binding.InputAction.WasPressedThisFrame()) {
                control = binding.InputAction.activeControl;
                return true;
            }
            control = null;
            return false;
        }

        public bool WasReleasedThisFrame(MultiBinding binding) {
            #if !PRODUCTION_BUILD
            if (simReleaseFrame == Time.frameCount
                    && TryGetSimControl(binding, simReleased, out _)) {
                return true;
            }
            #endif
            return binding.InputAction.WasReleasedThisFrame();
        }

        public bool IsPressed(MultiBinding binding) {
            #if !PRODUCTION_BUILD
            if (simHeld.Count > 0 && TryGetSimControl(binding, simHeld, out _)) {
                return true;
            }
            #endif
            return binding.InputAction.IsPressed();
        }

        #if !PRODUCTION_BUILD
        // Fakes device state only, so that tests run the real binding to control
        // resolution, layers, shadowing and dispatch. Presses apply to the whole
        // next frame, because test code runs after UserInput.Update.
        private readonly HashSet<InputControl> simPressed = new();
        private readonly HashSet<InputControl> simReleased = new();
        private readonly HashSet<InputControl> simHeld = new();
        private int simPressFrame = -1;
        private int simReleaseFrame = -1;

        public bool HasSimInput => simPressFrame >= Time.frameCount
            || simReleaseFrame >= Time.frameCount;

        public void SimulateControlPress(string controlPath) {
            var frame = Time.frameCount + 1;
            if (simPressFrame != frame) {
                simPressed.Clear();
                simPressFrame = frame;
            }
            AddSimControls(controlPath, simPressed);
            AddSimControls(controlPath, simHeld);
        }

        public void SimulateControlRelease(string controlPath) {
            var frame = Time.frameCount + 1;
            if (simReleaseFrame != frame) {
                simReleased.Clear();
                simReleaseFrame = frame;
            }
            AddSimControls(controlPath, simReleased);
            using (var found = InputSystem.FindControls(controlPath)) {
                foreach (var c in found) {
                    simHeld.Remove(c);
                }
            }
        }

        public void ClearSimControls() {
            simPressed.Clear();
            simReleased.Clear();
            simHeld.Clear();
            simPressFrame = -1;
            simReleaseFrame = -1;
        }

        private static void AddSimControls(string controlPath, HashSet<InputControl> into) {
            using (var found = InputSystem.FindControls(controlPath)) {
                if (found.Count == 0) {
                    D.Err("No control to simulate for path {0}", controlPath);
                }
                foreach (var c in found) {
                    into.Add(c);
                }
            }
        }

        private static bool TryGetSimControl(MultiBinding binding,
                HashSet<InputControl> sim, out InputControl control) {
            if (binding.InputAction.enabled) {
                var controls = binding.InputAction.controls;
                for (int i = controls.Count - 1; i >= 0; i--) {
                    if (sim.Contains(controls[i])) {
                        control = controls[i];
                        return true;
                    }
                }
            }
            control = null;
            return false;
        }
        #endif
    }
}
