#if UNITY_EDITOR
// #define DEBUG_BINDINGS
#endif
using System.Collections.Generic;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine.InputSystem;
using UnityEngine;
using Game.Constants;
using Game.UI;
using System;
using Game.Data;

namespace Game.Input {
    public sealed class MultiBinding {
        public Bindings Bindings;
        public InputAction InputAction;
        public ActionType Action;
        // Names the pref keys and the label translation key. Equals Action.ToString()
        // for core bindings, so saved overrides keep working. Bindings created
        // with CreateNamed have no ActionType and are only reachable by Id.
        public string Id;
        public InputLayer Layer = InputLayer.Always;
        // Bindings of one layer whose masks do not overlap can never be live
        // together, so they may share a control. 0 = every context of the layer.
        public int ContextMask;
        public List<ActionType> AllowDuplicateWithActions;
        public ActionType AutoSwapWithAction;
        // When alias action is set, if we try to get action, we will also
        // check the alias one?
        public ActionType AliasAction;
        public BindingType Type;
        public InputActionType IAType;
        private int defaultBindingCount;
        public bool IsDrag;
        public string UIGroup;
        public bool IsHidden;
        private string actionName;
        private Func<string> labelFunc;
        public string ActionName => labelFunc != null ? labelFunc.Invoke()
            : actionName ??= $"input.{Id}".T();
        public readonly BindingConstraints[] Constraints = new BindingConstraints[3];
        private float lastClickTime;
        private int lastDoubleClickCheckFrame;
        // The binding only claims its controls while this returns true
        public Func<bool> Availability;
        public Action Handler;
        // Written by InputLayerResolver
        public int WonFrame = -1;
        public InputControl TriggerControl;

        public void ResetToDefaults() {
            ResetInputAction();
            The.Prefs.ClearMatching(BindingKeyStart(Id));
        }

        // Separate from ResetToDefaults, because Load restores from the saved
        // overrides and must not clear them before reading
        private void ResetInputAction() {
            // Erasing the last binding leaves the Input System with a stale control
            // count over a null control array, and InputAction.controls throws.
            // A fresh action has no such state.
            if (defaultBindingCount == 0 && InputAction.bindings.Count > 0) {
                RecreateInputAction();
                return;
            }
            InputAction.RemoveAllBindingOverrides();
            // Backwards, erasing shifts the indices of everything after it
            for (int i = InputAction.bindings.Count - 1; i >= defaultBindingCount; i--) {
                InputAction.ChangeBinding(i).Erase();
            }
        }

        private void RecreateInputAction() {
            var old = InputAction;
            var wasEnabled = old.enabled;
            old.Disable();
            old.Dispose();
            InputAction = new(old.name, old.type, null, old.interactions,
                old.processors, old.expectedControlType);
            if (wasEnabled) {
                InputAction.Enable();
            }
        }

        public void Disable() {
            if (InputAction.enabled) {
                InputAction.Disable();
            }
            The.Prefs.SetBool(BindingDisabledKey(Id), true);
        }

        public void Enable() {
            if (!InputAction.enabled) {
                InputAction.Enable();
            }
            The.Prefs.UnsetBool(BindingDisabledKey(Id));
        }

        public static MultiBinding CreateFor(ActionType act,
                BindingType bt = BindingType.Button,
                InputActionType iaType = InputActionType.Button) {
            var mb = new MultiBinding();
            mb.Type = bt;
            mb.Action = act;
            mb.Id = act.ToString();
            mb.InputAction = new(mb.Id, iaType);
            mb.InputAction.Enable();
            return mb;
        }

        // The label is a func, because translated strings change with the language
        public static MultiBinding CreateNamed(string id, Func<string> label) {
            var mb = new MultiBinding();
            mb.Type = BindingType.Button;
            mb.Action = ActionType.None;
            mb.Id = id;
            mb.labelFunc = label;
            mb.InputAction = new(id, InputActionType.Button);
            mb.InputAction.Enable();
            return mb;
        }

        public MultiBinding InLayer(InputLayer layer) {
            Layer = layer;
            return this;
        }

        public MultiBinding WithContextMask(int mask) {
            ContextMask = mask;
            return this;
        }

        public MultiBinding WithAvailability(Func<bool> availability) {
            Availability = availability;
            return this;
        }

        // Kept so that mods compiled against the old name still load
        [Obsolete("Use WithAvailability")]
        public MultiBinding WithCondition(Func<bool> condition) {
            return WithAvailability(condition);
        }

        public MultiBinding WithHandler(Action handler) {
            Handler = handler;
            return this;
        }

        public static MultiBinding CreateV2For(ActionType act) {
            return CreateFor(act, BindingType.Vector2, InputActionType.Value);
        }
        public static MultiBinding CreateFloatFor(ActionType act) {
            return CreateFor(act, BindingType.Axis, InputActionType.Value);
        }

        public MultiBinding Drag() {
            IsDrag = true;
            return this;
        }

        public BindingConstraints GetConstraints(BindingSlot slot) {
            int i = (int) slot;
            if (Constraints[i] == null) {
                Constraints[i] = new BindingConstraints() {
                    Type = Type,
                    Slot = slot,
                    IsRebindable = true,
                    IsHidden = false,
                };
            }
            return Constraints[i];
        }

        public void SaveDefaultState() {
            defaultBindingCount = InputAction.bindings.Count;
        }

        // Snapshot of the default (pre-override) bindings: the first
        // defaultBindingCount entries, by declaration order. Binding paths are
        // the original .path values - user overrides (overridePath) and
        // user-appended bindings are excluded.
        public void GetDefaultBindings(List<DefaultBindingInfo> into) {
            var bindings = InputAction.bindings;
            var count = defaultBindingCount > 0
                ? Mathf.Min(defaultBindingCount, bindings.Count)
                : bindings.Count;
            for (int i = 0; i < count; i++) {
                var b = bindings[i];
                into.Add(new DefaultBindingInfo {
                    Path = b.path,
                    IsComposite = b.isComposite,
                    IsPartOfComposite = b.isPartOfComposite,
                    HumanName = b.isComposite ? null
                        : InputControlPath.ToHumanReadableString(b.path,
                            InputControlPath.HumanReadableStringOptions.OmitDevice),
                });
            }
        }

        public MultiBinding WithUIGroup(string group) {
            UIGroup = group;
            return this;
        }

        public MultiBinding WithAllowedDuplicate(params ActionType[] actions) {
            (AllowDuplicateWithActions ??= new()).AddRange(actions);
            return this;
        }

        public bool AllowsDuplicateWith(ActionType action) {
            return AllowDuplicateWithActions != null
                && AllowDuplicateWithActions.Contains(action);
        }

        public MultiBinding WithAutoSwapWith(ActionType action) {
            AutoSwapWithAction = action;
            return this;
        }
        public MultiBinding WithNotRebindable(BindingSlot slot) {
            GetConstraints(slot).IsRebindable = false;
            return this;
        }
        public MultiBinding WithHiddenSlot(BindingSlot slot) {
            GetConstraints(slot).IsHidden = true;
            return this;
        }

        public MultiBinding Hidden() {
            IsHidden = true;
            return this;
        }

        public MultiBinding WithSlotType(BindingSlot slot, BindingType type) {
            GetConstraints(slot).Type = type;
            return this;
        }

        public MultiBinding WithAliasAction(ActionType alias) {
            AliasAction = alias;
            return this;
        }

        private string GroupOf(string path) {
            if (path == null) { return null; }
            if (!path.StartsWith("<")) { return null; }
            return path.Substring(1, path.IndexOf('>') - 1);
        }

        public MultiBinding BindWithProcessors(string path, params string[] processors) {
            var binding = InputAction.AddBinding(path).WithGroup(GroupOf(path));
            foreach (var p in processors) {
                try {
                    binding.WithProcessor(p);
                } catch (System.Exception e) {
                    D.LogEx(e, "Failed adding processor to binding: {0} -> {1}",
                        path, p);
                }
            }
            return this;
        }

        public MultiBinding Bind(string path) {
            InputAction.AddBinding(path).WithGroup(GroupOf(path));
            return this;
        }
        public bool TryGetCompositeBinding(out InputBinding b) {
            foreach (var bind in InputAction.bindings) {
                if (!bind.isComposite) { continue; }
                // we found the binding
                b = bind;
                return true;
            }
            b = default;
            return false;
        }
        public bool TryGetControlFor(BindingSlot slot, InputBinding part,
                out InputAction ia, out InputControl control) {
            ia = InputAction;
            var ctrls = InputSystem.FindControls(part.effectivePath);
            foreach (var ctrl in ctrls) {
                if (slot == BindingSlot.Keyboard && ctrl.device is Keyboard) {
                    control = ctrl;
                    return true;
                }
                if (slot == BindingSlot.Mouse && ctrl.device is Mouse) {
                    control = ctrl;
                    return true;
                }
                if (slot == BindingSlot.Gamepad) {
                    if (ctrl.device is Gamepad) {
                        control = ctrl;
                        return true;
                    } else if (ctrl.device is Mouse m && m.name.StartsWith("V")) {
                        // VMOUSE
                        var gamepadAct = ResolveVMouseActionType(ctrl.name);
                        if (gamepadAct != ActionType.None) {
                            var gamepadBinding = Bindings.All[gamepadAct];
                            if (gamepadBinding.TryGetControlFor(BindingSlot.Gamepad,
                                    out var ia2, out var c)) {
                                ia = gamepadBinding.InputAction;
                                control = c;
                                return true;
                            }
                        }
                    }
                }
            }
            control = null;
            return false;
        }

        public bool TryGetControlFor(BindingSlot slot,
                out InputAction ia, out InputControl control) {
            ia = InputAction;
            foreach (var ctrl in InputAction.controls) {
                if (slot == BindingSlot.Keyboard && ctrl.device is Keyboard) {
                    control = ctrl;
                    return true;
                }
                if (slot == BindingSlot.Mouse && ctrl.device is Mouse) {
                    control = ctrl;
                    return true;
                }
                if (slot == BindingSlot.Gamepad) {
                    if (ctrl.device is Gamepad) {
                        control = ctrl;
                        return true;
                    } else if (ctrl.device is Mouse m && m.name.StartsWith("V")) {
                        // VMOUSE
                        var gamepadAct = ResolveVMouseActionType(ctrl.name);
                        if (gamepadAct != ActionType.None) {
                            var gamepadBinding = Bindings.All[gamepadAct];
                            if (gamepadBinding.TryGetControlFor(BindingSlot.Gamepad,
                                    out var ia2, out var c)) {
                                ia = gamepadBinding.InputAction;
                                control = c;
                                return true;
                            }
                        }
                    }
                }
            }
            control = null;
            return false;
        }

        public string AllControlGlyphs(out string tooltip, string tooltipJoin = "\n",
                bool showEverything = false) {
            if (!InputAction.enabled) {
                tooltip = T.Inactive;
                return T.Inactive;
            }
            var res = new HashSet<string>(InputAction.bindings.Count);
            var tooltipRes = new HashSet<string>(res.Count);
            if (!VirtualMouseInput.IsCurrent || showEverything) {
                foreach (var ctrl in InputAction.controls) {
                    if (ctrl.device is Keyboard) {
                        res.Add(Bindings.GlyphMap.GetControlGlyph(
                            InputAction, ctrl, out var devName, out var name));
                        tooltipRes.Add($"{devName}: {name}");
                    }
                }
                foreach (var ctrl in InputAction.controls) {
                    if (ctrl.device is Mouse m) {
                        if (!m.name.StartsWith("V")) {
                            // Real mouse
                            res.Add(Bindings.GlyphMap.GetControlGlyph(
                                InputAction, ctrl, out var devName, out var name));
                            tooltipRes.Add($"{devName}: {name}");
                        }
                    }
                }
            }
            foreach (var ctrl in InputAction.controls) {
                if (ctrl.device is Mouse m) {
                    if (m.name.StartsWith("V")) {
                        // Virtual mouse
                        var act = ResolveVMouseActionType(ctrl.name);
                        if (act == ActionType.None) { continue; }
                        var realBinding = Bindings.All[act];
                        res.Add(realBinding.AllControlGlyphs(out var subtip, tooltipJoin));
                        tooltipRes.Add(subtip);
                    }
                } else if (ctrl.device is Gamepad) {
                    res.Add(Bindings.GlyphMap.GetControlGlyph(
                        InputAction, ctrl, out var devName, out var name));
                    tooltipRes.Add($"{devName}: {name}");
                }
            }
            tooltip = string.Join(tooltipJoin, tooltipRes);
            return string.Join("/", res);
        }

        public void Save() {
            var a = InputAction;
            var bc = a.bindings.Count;
            bool hasOverride = false;
            for (int i = 0; i < bc; i++) {
                try {
                    InputBinding b = a.bindings[i];
                    var idx = a.GetBindingIndex(b);
                    if (idx != i) {
                        D.Err("Idx not matching for {0} -> expected {1} got {2}",
                            Action, i, idx);
                    }
                    var k = BindingKey(Id, idx);
                    if (i < defaultBindingCount) {
                        var ovr = b.overridePath;
                        if (ovr != null && ovr.Length == 0) {
                            hasOverride = true;
                            The.Prefs.SetString(k, UnboundPref);
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(ovr)) {
                            hasOverride = true;
                        }
                        The.Prefs.SetString(k, b.overridePath);
                    } else {
                        hasOverride = true;
                        The.Prefs.SetString(k, b.effectivePath);
                    }
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failed saving binding: {0}", Action);
                }
            }
            if (hasOverride) {
                The.Prefs.SetInt(countKey, bc);
            } else {
                The.Prefs.UnsetInt(countKey);
            }
        }
        private string countKey => $"input.{Id}.count";

        // An empty override path is how the Input System disables one binding, but
        // Load skips blank prefs, so an unbound default is saved as this
        private const string UnboundPref = "-";

        // Appended composites are saved as plain bindings, so their parts are not
        // offered: an unbound part would not come back as one
        public bool CanUnbind(int bindingIndex) {
            var bindings = InputAction.bindings;
            if (bindingIndex < 0 || bindingIndex >= bindings.Count) { return false; }
            var b = bindings[bindingIndex];
            if (b.isComposite) { return false; }
            if (bindingIndex < defaultBindingCount) {
                return !string.IsNullOrEmpty(b.effectivePath);
            }
            return !b.isPartOfComposite;
        }

        // A default binding keeps its index (saved overrides are per index) and
        // gets an empty override; Reset brings it back
        public void Unbind(int bindingIndex) {
            if (!CanUnbind(bindingIndex)) {
                D.Err("Cannot unbind {0} binding {1}", Id, bindingIndex);
                return;
            }
            if (bindingIndex < defaultBindingCount) {
                InputAction.ApplyBindingOverride(bindingIndex, string.Empty);
            } else if (InputAction.bindings.Count == 1) {
                // Same stale control state as erasing the last binding in Reset
                RecreateInputAction();
            } else {
                InputAction.ChangeBinding(bindingIndex).Erase();
            }
        }

        // So that binding an unbound slot fills its default again instead of
        // adding a new binding next to the dead one
        public int FindUnboundDefault(BindingSlot slot) {
            var bindings = InputAction.bindings;
            for (int i = 0; i < defaultBindingCount && i < bindings.Count; i++) {
                if (bindings[i].isPartOfComposite) { continue; }
                if (IsUnbound(i, slot)) {
                    return i;
                }
            }
            return -1;
        }

        public bool IsUnbound(int bindingIndex, BindingSlot slot) {
            if (bindingIndex >= defaultBindingCount || !BelongsToSlot(bindingIndex, slot)) {
                return false;
            }
            var ovr = InputAction.bindings[bindingIndex].overridePath;
            return ovr != null && ovr.Length == 0;
        }

        // From the group or the default path, not the controls: an unbound binding has none,
        // and neither has one whose device is not connected
        public bool BelongsToSlot(int bindingIndex, BindingSlot slot) {
            var bindings = InputAction.bindings;
            if (bindingIndex < 0 || bindingIndex >= bindings.Count) { return false; }
            var b = bindings[bindingIndex];
            if (b.isComposite) { return false; }
            var group = string.IsNullOrEmpty(b.groups) ? GroupOf(b.path) : b.groups;
            return group == slot.ToString();
        }

        public bool Load(bool needsReset) {
            ResetInputAction();
            var count = The.Prefs.GetIntAutoHash(countKey, 0);
            var a = InputAction;
            var act = Id;
            bool hasOverride = false;
            for (int i = 0; i < count; i++) {
                if (i < defaultBindingCount) {
                    try {
                        // restore existing overrides
                        InputBinding b = a.bindings[i];
                        var idx = a.GetBindingIndex(b);
                        if (idx != i) {
                            D.Err("Idx not matching for {0} -> expected {1} got {2}",
                                Action, i, idx);
                        }
                        var k = BindingKey(act, idx);
                        var over = The.Prefs.GetStringAutoHash(k, null);
                        if (!string.IsNullOrWhiteSpace(over)) {
                            //D.Err("Restoring override: {0} -> {1}", act, over);
                            hasOverride = true;
                            if (!needsReset) {
                                a.ApplyBindingOverride(idx,
                                    over == UnboundPref ? string.Empty : over);
                            }
                        }
                    } catch (System.Exception ex) {
                        D.LogEx(ex, "Failed loading binding override: {0}", Action);
                    }
                } else {
                    try {
                        // new binding time
                        var k = BindingKey(act, i);
                        var path = The.Prefs.GetStringAutoHash(k, null);
                        // Older builds made a composite when an empty axis slot was clicked,
                        // and saved its name ("axis") and its parts, empty when unbound
                        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("<")) {
                            D.Warn("Dropping leftover binding entry of {0}: {1}", act, path);
                            The.Prefs.SetString(k, null);
                            continue;
                        }
                        hasOverride = true;
                        if (!needsReset) {
                            a.AddBinding(path).WithGroup(GroupOf(path));
                        }
                    } catch (System.Exception ex) {
                        D.LogEx(ex, "Failed loading new binding: {0}", Action);
                    }
                }
            }
            if (hasOverride && !needsReset) {
                The.Prefs.SetInt(countKey, a.bindings.Count);
            } else if (count > 0) {
                // Also drops the stale override paths of a bindings version reset
                The.Prefs.ClearMatching(BindingKeyStart(act));
            }
            if (The.Prefs.GetBoolAutoHash(BindingDisabledKey(act), false)) {
                if (InputAction.enabled) {
                    InputAction.Disable();
                }
                hasOverride = true;
            }
            return hasOverride;
        }

        // The trailing dot keeps a reset of one binding from wiping the saved
        // overrides of another one whose id merely starts the same (Click /
        // ClickCancel, ea.Repair / ea.RepairAll)
        public static string BindingKeyStart(string act) {
            return string.Format("input.{0}.", act);
        }
        public static string BindingKey(string act, int idx) {
            if (idx < 0) {
                D.Err("Index < 0 for action: {0} binding {1}", act);
            }
            return string.Format("input.{0}.{1}", act, idx);
        }
        public static string BindingDisabledKey(string act) {
            return string.Format("input.disabled.{0}", act);
        }

        public ActionType ResolveVMouseActionType(string name) {
            switch (name) {
                case "leftButton": return ActionType.VMouseButtonL;
                case "rightButton": return ActionType.VMouseButtonR;
                case "y": return ActionType.VMouseScroll;
                default: return ActionType.None;
                //case "middleButton": return ActionType.None;
                //case "forwardButton": return ActionType.None;
                //case "backButton": return ActionType.None;
            }
            //D.Err("No vmouse action type for {0}", name);
            //return ActionType.None;
        }

        public MultiBinding Bind4(string up, string down, string left, string right) {
            if (Type == BindingType.Button) {
                D.Err("Trying to bind4 on a wrong type: {0} {1}", Action, Type);
                return this;
            }
            InputAction.AddCompositeBinding("dpad")
                    .With("up", up, GroupOf(up))
                    .With("left", left, GroupOf(left))
                    .With("down", down, GroupOf(down))
                    .With("right", right, GroupOf(right));
            return this;
        }

        public MultiBinding Bind2(string up, string down) {
            if (Type != BindingType.Axis) {
                D.Err("Trying to bind2 on a wrong type: {0} {1}", Action, Type);
                return this;
            }
            InputAction.AddCompositeBinding("axis")
                    .With("positive", up, GroupOf(up))
                    .With("negative", down, GroupOf(down));
            return this;
        }
        public MultiBinding Bind2WithProcessors(string up, string down,
                params string[] processors) {
            if (Type != BindingType.Axis) {
                D.Err("Trying to bind2 on a wrong type: {0} {1}", Action, Type);
                return this;
            }
            var procs = string.Join(',', processors);
            InputAction.AddCompositeBinding("axis")
                    .With("positive", up, GroupOf(up), procs)
                    .With("negative", down, GroupOf(down), procs);
            return this;
        }

        public bool Validate<T>() where T : struct {
            try {
                var value = InputAction.ReadValueAsObject();
                if (value is T val) {
                    return true;
                } else {
                    D.Err("Failed to validate multi binding {0} when trying to get value of type {1}. Actual value type: {2}",
                        ActionName, typeof(T), value?.GetType());
                }
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed to validate multi binding {0} when trying to get value of type {1}",
                    ActionName, typeof(T));
            }
            return false;
        }

        public T GetValue<T>() where T : struct {
            return InputAction.ReadValue<T>();
        }

        public bool IsPressed() {
            var resolver = Bindings.Resolver;
            return resolver.IsPressed(this) && resolver.IsLive(this);
        }

        public bool IsJustPressed() {
            #if DEBUG_BINDINGS
            if (InputAction.WasPressedThisFrame()) {
                D.Warn("JustPressed: {0}, {1}", Id, Time.frameCount);
            }
            #endif
            return Bindings.Resolver.IsJustPressed(this);
        }

        // Holds and releases are only gated by the context, not by shadowing:
        // they belong to whoever polls them (Rotate acts on release, the vent
        // is a hold), and a press already decided the owner of the key.
        public bool IsJustReleased() {
            #if DEBUG_BINDINGS
            if (InputAction.WasReleasedThisFrame()) {
                D.Warn("JustReleased: {0}, {1}", Id, Time.frameCount);
            }
            #endif
            var resolver = Bindings.Resolver;
            return resolver.WasReleasedThisFrame(this) && resolver.IsLive(this);
        }
        public bool IsJustDoubleClicked(bool skipPressedCheck) {
            if (!skipPressedCheck && !InputAction.WasPressedThisFrame()) {
                return false;
            }
            var t = GameTimer.unscaledTime;

            if (t - lastClickTime < 0.4f) {
                lastClickTime = 0f;
                return true;
            }
            var fc = Time.frameCount;
            // To prevent this from updating twice in same frame
            if (lastDoubleClickCheckFrame != fc) {
                lastClickTime = t;
                lastDoubleClickCheckFrame = fc;
            }
            return false;
        }
        public bool IsJustDoubleClicked() {
            var clicked = InputAction.WasPressedThisFrame();
            if (!clicked) { return false; }
            var t = GameTimer.unscaledTime;

            if (t - lastClickTime < 0.4f) {
                lastClickTime = 0f;
                return true;
            }
            var fc = Time.frameCount;
            // To prevent this from updating twice in same frame
            if (lastDoubleClickCheckFrame != fc) {
                lastClickTime = t;
                lastDoubleClickCheckFrame = fc;
            }
            return false;
        }

        public void Cleanup() {
            InputAction.Disable();
            InputAction.Dispose();
        }

        public static bool IsControlOfType(InputControl c, BindingSlot type) {
            switch (type) {
                case BindingSlot.Keyboard:
                    return c.device is Keyboard;
                case BindingSlot.Mouse:
                    return c.device is Mouse;
                case BindingSlot.Gamepad:
                    return c.device is Gamepad || c.device is Mouse m
                        && m.name.StartsWith("V");
                default:
                    return false;
            }
        }
    }
}
