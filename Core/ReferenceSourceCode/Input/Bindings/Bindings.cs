using System;
using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine.InputSystem;
using Game.Systems.UIUX;
using Game.Platform;
using Game.Tools;

namespace Game.Input {
    public class Bindings {
        public GlyphMap GlyphMap = new();
        public IMouse Mouse { get; private set; }
        public bool IsLocked;
        // Lookup for bindings that have an ActionType. Named bindings
        // (MultiBinding.CreateNamed) are not in here, iterate List to reach everything.
        public readonly Dictionary<ActionType, MultiBinding> All = new();
        public readonly List<MultiBinding> List = new();
        public readonly Dictionary<string, MultiBinding> ById = new();
        public readonly List<MultiBinding> Handled = new();
        public readonly InputLayerResolver Resolver = new();
        public readonly Dictionary<string, List<MultiBinding>> ByGroup = new();
        private readonly HashSet<ActionType> nonLockableActions = new();
        public bool HasGamepad;

        public void DetectMouse() {
            // TODO - detect other input types, change when needed
            if (Mouse == null) {
                Mouse = new RealMouse();
            }

            if (PlatformUtils.UseGamepad()) {
                HasGamepad = Gamepad.all.Count > 0;
            } else {
                HasGamepad = false;
                foreach (var g in Gamepad.all) {
                    InputSystem.DisableDevice(g);
                }
            }
            /*
            foreach (var g in Gamepad.all) {
                D.Err("Gamepad: {0}", g.displayName);
                D.Err("Gamepad devid: {0}", g.deviceId);
                D.Err("Gamepad device: {0}", g.device);
                D.Err("Gamepad device: {0}", g.native);
                D.Err("Gamepad shortname: {0}", g.shortDisplayName);
                D.Err("Gamepad descr: {0}", g.description);
            }
            */
        }

        public void SetMouse(IMouse mouse) {
            Mouse = mouse;
        }

        public bool HasAppliedOverrides;

        public void Clear() {
            if (HasAppliedOverrides) {
                D.Err("Clearing bindings after overrides were applied!");
            }
            foreach (var binding in List) {
                binding.Cleanup();
            }
            All.Clear();
            List.Clear();
            ById.Clear();
            Handled.Clear();
            Resolver.Clear();
            nonLockableActions.Clear();
        }
        public void AddNonLockable(MultiBinding binding) {
            Add(binding);
            nonLockableActions.Add(binding.Action);
        }

        public void Add(MultiBinding binding) {
            if (HasAppliedOverrides) {
                D.Err("Adding a new binding after binding overrides were applied! {0}",
                    binding);
            }
            binding.SaveDefaultState();
            binding.Bindings = this;
            ById.Add(binding.Id, binding);
            if (binding.Action != ActionType.None) {
                All.Add(binding.Action, binding);
            }
            List.Add(binding);
            if (binding.Handler != null) {
                Handled.Add(binding);
            }
            Resolver.Register(binding);
            var groupId = binding.UIGroup;
            if (groupId == null) {
                if (binding.IsHidden) {
                    return;
                }
                D.Err("Binding without group: {0}", binding.Action);
                groupId = "misc";
            }
            if (!ByGroup.TryGetValue(groupId, out var group)) {
                group = new();
                ByGroup.Add(groupId, group);
            }
            group.Add(binding);
        }

        private string GetVMouseControlName(string path, HashSet<string> tmpStr) {
            if (path == "<Mouse>/middleButton") { return null; }
            if (path == "<Mouse>/forwardButton") { return null; }
            if (path == "<Mouse>/backButton") { return null; }
            var act = ResolveVMouseActionType(path);
            return GetControlName(act, tmpStrings: tmpStr);
        }

        private ActionType ResolveVMouseActionType(string path) {
            switch (path) {
                case "<Mouse>/leftButton": return ActionType.VMouseButtonL;
                //case "<Mouse>/middleButton": return ActionType.VMouseButtonM;
                case "<Mouse>/rightButton": return ActionType.VMouseButtonR;
                //case "<Mouse>/forwardButton": return ActionType.VMouseForward;
                //case "<Mouse>/backButton": return ActionType.VMouseBack;
                case "<Mouse>/scroll/y": return ActionType.VMouseScroll;
            }
            D.Err("No vmous action type for {0}", path);
            return ActionType.None;
        }
        private bool HasBindingGlyph(InputBinding x, InputAction ia,
                bool isUsingGamepad) {
            var p = x.effectivePath;
            var isPG = p.StartsWith("<G");
            if (isUsingGamepad) {
                if (isPG) {
                    var str = GlyphMap.GetGamepadGlyph(p,
                        VirtualMouseInput.GamepadFlavor);
                    if (!string.IsNullOrWhiteSpace(str)) {
                        return true;
                    }
                } else if (p.StartsWith("<M")) {
                    var str = GetVMouseControlName(p, null);
                    if (!string.IsNullOrWhiteSpace(str)) {
                        return true;
                    }
                }
            }
            if (isUsingGamepad) { return false; }
            if (!isPG) {
                var str = GlyphMap.GetGlyph(p, out var failGlyph);
                if (!string.IsNullOrWhiteSpace(str) || !string.IsNullOrWhiteSpace(failGlyph)) {
                    return true;
                }
            }
            return false;
        }

        private void BindingToGlyph(InputBinding x, InputAction ia,
                bool isUsingGamepad, HashSet<string> res, bool anyInputMethod) {
            var p = x.effectivePath;
            var isPG = p.StartsWith("<G");
            if (isUsingGamepad || anyInputMethod) {
                if (isPG) {
                    var str = GlyphMap.GetGamepadGlyph(p,
                        VirtualMouseInput.GamepadFlavor);
                    if (!string.IsNullOrWhiteSpace(str)) {
                        res.Add(str);
                    }
                } else if (p.StartsWith("<M")) {
                    var str = GetVMouseControlName(p, res);
                    if (!string.IsNullOrWhiteSpace(str)) {
                        res.Add(str);
                    }
                }
            }
            if (isUsingGamepad) { return; }
            if (!isPG || anyInputMethod) {
                var str = GlyphMap.GetGlyph(p, out var failGlyph);
                if (str == null && p != "dpad" && p != "axis") { // surrogate dpad and axis
                    var disStr = ia.GetBindingDisplayString(x);
                    if (failGlyph != null) {
                        str = $"{failGlyph} {disStr}".Trim();
                    } else {
                        str = disStr;
                    }
                }
                if (!string.IsNullOrWhiteSpace(str)) {
                    res.Add(str);
                }
            }
        }
        private readonly HashSet<string> tempStrings = new();
        public string GetControlNameWithTip(MultiBinding binding) {
            if (binding == null) {
                return $"[{T.ShortcutNotBound}]";
            }
            var glyph = binding.AllControlGlyphs(out var tt, " / ");
            if (string.IsNullOrEmpty(tt)) {
                return $"[{T.ShortcutNotBound}]";
            }
            tt = Texts.ToBase64Url(tt);
            return $"<nobr><link=\"tip:{tt}\">{glyph}</link></nobr>";
        }

        public string GetControlNameWithTip(ActionType act) {
            if (act == ActionType.None) {
                return GetControlNameWithTip(null);
            }
            if (CodexExportScope.IsActive) {
                return CodexExportScope.ResolveGlyph(act) ?? $"[{T.ShortcutNotBound}]";
            }
            return GetControlNameWithTip(The.Bindings.GetBinding(act));
        }

        public string GetControlName(ActionType action, bool skipComposite = false,
                bool anyInputMethod = false, HashSet<string> tmpStrings = null) {
            var isUsingGamepad = VirtualMouseInput.IsCurrent;
            bool shouldJoin = false;
            if (tmpStrings == null) {
                shouldJoin = true;
                tempStrings.Clear();
                tmpStrings = tempStrings;
            }
            if (All.TryGetValue(action, out var value)) {
                var ia = value.InputAction;
                if (!ia.enabled) { return null; }
                foreach (var x in ia.bindings) {
                    if (x.isPartOfComposite && skipComposite) {
                        continue;
                    }
                    // An unbound slot has an empty override path
                    if (string.IsNullOrEmpty(x.effectivePath)) {
                        continue;
                    }
                    BindingToGlyph(x, ia, isUsingGamepad, tmpStrings, anyInputMethod);
                }
            }
            if (shouldJoin) {
                return string.Join("/", tmpStrings);
            }
            return null;
        }

        public void SwapIfSame(MultiBinding b1, MultiBinding b2, BindingSlot type) {
            var allCtrls = new Dictionary<string, InputControl>();

            foreach (var c in b1.InputAction.controls) {
                if (MultiBinding.IsControlOfType(c, type)) {
                    allCtrls.Add(c.path, c);
                }
            }
            foreach (var c in b2.InputAction.controls) {
                if (MultiBinding.IsControlOfType(c, type)) {
                    allCtrls.TryAdd(c.path, c);
                }
            }
            foreach (var c in allCtrls.Values) {
                var b1b = b1.InputAction.GetBindingForControl(c).GetValueOrDefault();
                var b2b = b2.InputAction.GetBindingForControl(c).GetValueOrDefault();
                if (b1b == null || b2b == null) { continue; }
                if (b1b.effectivePath == b2b.effectivePath) {
                    if (b1b.hasOverrides) {
                        // set override for b2 to b1b.path (original)
                        b2.ResetToDefaults();
                        b2.InputAction.ApplyBindingOverride(b1b.path, null, b2b.path);
                        The.SysSig.BindingChanged.Send(b2.Action);
                        D.Warn("Swapped bindings 1: {0} -> {1}", b2.Action, b1.Action);
                    } else if (b2b.hasOverrides) {
                        b2.ResetToDefaults();
                        The.SysSig.BindingChanged.Send(b2.Action);
                        D.Warn("Reset swapped action: {0}", b2.Action);
                    } else {
                        D.Err("Cannot swap two conflicting bindings that don't have overrides: {0} / {1}",
                            b1b, b2b);
                    }
                    if (b2.Action == ActionType.Click) {
                        UIPoolRoot.Activator.SetClickAction(b2.InputAction);
                    }
                }
            }
        }

        public bool HasBinding(ActionType action) {
            return All.TryGetValue(action, out var value) && HasBinding(value);
        }

        public bool HasBinding(MultiBinding value) {
            var isUsingGamepad = VirtualMouseInput.IsCurrent;
            var ia = value.InputAction;
            if (!ia.enabled) { return false; }
            foreach (var x in ia.bindings) {
                if (string.IsNullOrEmpty(x.effectivePath)) {
                    continue;
                }
                if (HasBindingGlyph(x, ia, isUsingGamepad)) {
                    return true;
                }
            }
            return false;
        }

        public MultiBinding GetBinding(ActionType action) {
            if (All.TryGetValue(action, out var b)) {
                return b;
            }
            D.Err("No binding found for action: {0}", action);
            return null;
        }

        public TV GetValue<TV>(ActionType action) where TV : struct {
            if (IsLocked && !nonLockableActions.Contains(action)) {
                return default;
            }
            if (All.TryGetValue(action, out var binding)) {
                return binding.GetValue<TV>();
            }
            return default;
        }

        public bool IsEitherPressed(ActionType a1, ActionType a2) {
            if (IsLocked && !nonLockableActions.Contains(a1)) {
                return false;
            }
            return All[a1].IsPressed() || All[a2].IsPressed();
        }

        public bool IsPressed(ActionType action) {
            #if !PRODUCTION_BUILD
            if (HasSimPressed) {
                if (simPressedQueue.Remove(action)) {
                    // D.Warn("Simulated press: {0}", action);
                    HasSimPressed = simPressedQueue.Count > 0;
                    return true;
                }
            }
            #endif
            if (IsLocked && !nonLockableActions.Contains(action)) {
                return false;
            }
            if (All.TryGetValue(action, out var binding)) {
                return binding.IsPressed();
            }
            return false;
        }

        public bool IsDoubleClickedUnchecked(ActionType action) {
            if (IsLocked && !nonLockableActions.Contains(action)) {
                return false;
            }
            if (All.TryGetValue(action, out var binding)) {
                return binding.IsJustDoubleClicked(true);
            }
            return false;
        }
        public bool IsDoubleClicked(ActionType action) {
            if (IsLocked && !nonLockableActions.Contains(action)) {
                return false;
            }
            if (All.TryGetValue(action, out var binding)) {
                return binding.IsJustDoubleClicked();
            }
            return false;
        }

        // Named bindings have no ActionType, so they are queried by reference
        public bool IsJustPressed(MultiBinding binding) {
            if (IsLocked) {
                return false;
            }
            return binding.IsJustPressed();
        }

        public bool IsJustPressedOrReleased(ActionType action) {
            return IsJustPressed(action) || IsJustReleased(action);
        }

        public bool IsJustPressed(ActionType action) {
            #if !PRODUCTION_BUILD
            if (HasSimJustPressed) {
                if (simJustPressedQueue.Remove(action)) {
                    // D.Warn("Simulating just pressed: {0}", action);
                    HasSimJustPressed = simJustPressedQueue.Count > 0;
                    return true;
                }
            }
            #endif
            if (IsLocked && !nonLockableActions.Contains(action)) {
                return false;
            }
            if (All.TryGetValue(action, out var binding)) {
                return binding.IsJustPressed();
            }
            return false;
        }

        /*
        Use AddShortcutGlyph instead!
        public void AddShortcutTooltip(UDB dataBlock, ActionType act) {
            if (HasBinding(ActionType.ShortcutRelocate)) {
                dataBlock.WithTooltip(T.ShortcutTip_V1.F(GetControlName(act)));
            }
        }
        */

        public bool IsJustReleased(ActionType action) {
            if (IsLocked && !nonLockableActions.Contains(action)) {
                return false;
            }
            #if !PRODUCTION_BUILD
            if (HasSimJustReleased) {
                if (simJustReleasedQueue.Remove(action)) {
                    // D.Warn("Simulating just released: {0}", action);
                    HasSimJustReleased = simJustReleasedQueue.Count > 0;
                    return true;
                }
            }
            #endif
            if (All.TryGetValue(action, out var binding)) {
                return binding.IsJustReleased();
            }
            return false;
        }

        // BUMP THIS WHEN CHANGING
        public const int BindingsVersion = 7;

        public void SetDeadZone(float min, float max) {
            InputSystem.settings.defaultDeadzoneMin = min;
            InputSystem.settings.defaultDeadzoneMax = max;
        }

        public void LogConnectedDevices() {
            var sb = new StringBuilder();
            sb.AppendFormat("Connected input devices:");
            try {
                foreach (var dev in InputSystem.devices) {
                    if (dev.enabled) {
                        sb.AppendFormat("\n - {0} [{1}]", dev.displayName, dev.path);
                        if (dev.noisy) {
                            sb.Append(" [noisy!]");
                        }
                    }
                }
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed loading connected devices");
            }
            D.Warn(sb.ToString());
        }

        public void SetDefaults() {
            Clear();

            // MAIN

            Add(MultiBinding.CreateFor(ActionType.Click)
                .WithUIGroup("main")
                .WithAutoSwapWith(ActionType.ClickCancel)
                .Bind("<Mouse>/leftButton"));

            Add(MultiBinding.CreateFor(ActionType.ClickCancel)
                .WithUIGroup("main")
                .WithAutoSwapWith(ActionType.Click)
                .Bind("<Mouse>/rightButton"));

            Add(MultiBinding.CreateFor(ActionType.Pause)
                .WithUIGroup("main")
                .Bind("<Gamepad>/start"));

            // CAMERA

            Add(MultiBinding.CreateFor(ActionType.CameraDrag)
                .WithUIGroup("camera")
                .Drag()
                .WithAliasAction(ActionType.ClickCancel)
                .WithAllowedDuplicate(ActionType.Rotate)
                .Bind("<Mouse>/middleButton"));

            // WTF: If gamepad is bound before the keys, it does not work
            Add(MultiBinding.CreateV2For(ActionType.CameraMove)
                .WithUIGroup("camera")
                .Bind4("<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d")
                .BindWithProcessors("<Gamepad>/rightStick", "StickDeadzone")
                .WithAllowedDuplicate(ActionType.QuickSearchNav)
                .WithHiddenSlot(BindingSlot.Mouse));

            Add(MultiBinding.CreateFor(ActionType.CameraZoom,
                    BindingType.Axis, InputActionType.PassThrough)
                .WithUIGroup("camera")
                .Bind2("<Keyboard>/pageUp", "<Keyboard>/pageDown")
                .BindWithProcessors("<Mouse>/scroll/y", "ScrollFix"));

            // TOOLS

            Add(MultiBinding.CreateFor(ActionType.QuickSearch)
                .WithUIGroup("tools")
                .WithNotRebindable(BindingSlot.Gamepad)
                .Bind("<Keyboard>/tab"));

            Add(MultiBinding.CreateFor(ActionType.CycleToolMode)
                .InLayer(InputLayer.Tool)
                .WithUIGroup("tools")
                .Bind("<Keyboard>/f"));

            // Only claims its keys in tools that rotate, elsewhere R falls
            // through to whatever the lower layers bind (repair)
            Add(MultiBinding.CreateFor(ActionType.Rotate)
                .InLayer(InputLayer.Tool)
                .WithAllowedDuplicate(ActionType.CameraDrag)
                .WithUIGroup("tools")
                .WithAvailability(() => The.Toolbox.ActiveTool.HandlesRotate)
                .Bind("<Keyboard>/r")
                .Bind("<Gamepad>/leftStickPress")
                .Bind("<Mouse>/middleButton"));

            Add(MultiBinding.CreateFor(ActionType.ToolNext)
                .InLayer(InputLayer.World)
                .WithUIGroup("tools")
                .Bind("<Keyboard>/rightBracket"));

            Add(MultiBinding.CreateFor(ActionType.ToolPrev)
                .InLayer(InputLayer.World)
                .WithUIGroup("tools")
                .Bind("<Keyboard>/leftBracket"));

            // TIME

            Add(MultiBinding.CreateFor(ActionType.ToggleTime)
                .WithUIGroup("time")
                .Bind("<Keyboard>/space")
                .Bind("<Gamepad>/buttonWest"));

            Add(MultiBinding.CreateFor(ActionType.CycleTimeSpeed)
                .WithUIGroup("time")
                .Bind("<Gamepad>/buttonNorth"));

            Add(MultiBinding.CreateFor(ActionType.TimeSpeedNormal)
                .WithUIGroup("time")
                .Bind("<Keyboard>/1"));

            Add(MultiBinding.CreateFor(ActionType.TimeSpeedFast1)
                .WithUIGroup("time")
                .Bind("<Keyboard>/2"));

            Add(MultiBinding.CreateFor(ActionType.TimeSpeedFast2)
                .WithUIGroup("time")
                .Bind("<Keyboard>/3"));

            // #if !PRODUCTION_BUILD
            // Add(MultiBinding.CreateFor(ActionType.TimeSpeedSlow)
            //     .WithUIGroup("time")
            //     .Bind("<Keyboard>/0"));
            // #endif
            Add(MultiBinding.CreateFor(ActionType.TimeSpeedUnlimited)
                .Hidden()
                .Bind("<Keyboard>/4"));

            // Shortcuts
            // No gamepad default: rightStickPress belongs to CenterView, and two
            // World bindings on one control are a conflict
            Add(MultiBinding.CreateFor(ActionType.ShortcutApps)
                .InLayer(InputLayer.World)
                .WithUIGroup("shortcuts")
                .Bind("<Keyboard>/k"));

            Add(MultiBinding.CreateFor(ActionType.CenterView)
                .InLayer(InputLayer.World)
                .WithUIGroup("shortcuts")
                .Bind("<Keyboard>/home")
                .Bind("<Gamepad>/rightStickPress"));

            Add(MultiBinding.CreateFor(ActionType.OpenResearch)
                .InLayer(InputLayer.World)
                .WithUIGroup("shortcuts")
                .Bind("<Keyboard>/t"));

            Add(MultiBinding.CreateFor(ActionType.OpenStarmap)
                .InLayer(InputLayer.World)
                .WithUIGroup("shortcuts")
                .Bind("<Keyboard>/m"));

            Add(MultiBinding.CreateFor(ActionType.OpenJobs)
                .InLayer(InputLayer.World)
                .WithUIGroup("shortcuts")
                .Bind("<Keyboard>/j"));

            Add(MultiBinding.CreateFor(ActionType.OpenInventory)
                .InLayer(InputLayer.World)
                .WithUIGroup("shortcuts")
                .Bind("<Keyboard>/i"));

            Add(MultiBinding.CreateFor(ActionType.OpenProduction)
                .InLayer(InputLayer.World)
                .WithUIGroup("shortcuts")
                .Bind("<Keyboard>/u"));

            Add(MultiBinding.CreateFor(ActionType.OpenSurgeries)
                .InLayer(InputLayer.World)
                .WithUIGroup("shortcuts")
                .Bind("<Keyboard>/b"));

            Add(MultiBinding.CreateFor(ActionType.OpenQuests)
                .InLayer(InputLayer.World)
                .WithUIGroup("shortcuts")
                .Bind("<Keyboard>/l"));

            // Shadows the copy config shortcut on the same key while in ship combat
            Add(MultiBinding.CreateFor(ActionType.ShipCombatToggleVent)
                .InLayer(InputLayer.ShipCombat)
                .WithUIGroup("shortcuts")
                .Bind("<Keyboard>/v"));

            // Misc
            AddNonLockable(MultiBinding.CreateFor(ActionType.ToggleConsole)
                .WithUIGroup("misc")
                .Bind("<Keyboard>/backquote"));

            Add(MultiBinding.CreateFor(ActionType.ShiftModifier)
                .Hidden()
                .Bind("<Keyboard>/leftShift")
                .Bind("<Keyboard>/rightShift")
                .Bind("<Gamepad>/rightShoulder"));

            Add(MultiBinding.CreateFor(ActionType.CtrlModifier)
                .Hidden()
                .Bind("<Keyboard>/leftCtrl")
                .Bind("<Keyboard>/rightCtrl")
                .Bind("<Gamepad>/leftShoulder"));

            Add(MultiBinding.CreateFor(ActionType.AltModifier)
                .Hidden()
                .Bind("<Keyboard>/leftAlt")
                .Bind("<Keyboard>/rightAlt"));

            Add(MultiBinding.CreateV2For(ActionType.QuickSearchNav)
                .Hidden()
                .Bind4("<Keyboard>/upArrow", "<Keyboard>/downArrow",
                    "<Keyboard>/leftArrow", "<Keyboard>/rightArrow")
                .WithAllowedDuplicate(ActionType.CameraMove));

            // VMOUSE

            AddNonLockable(MultiBinding.CreateV2For(ActionType.VMouseMove)
                .WithUIGroup("vmouse")
                .BindWithProcessors("<Gamepad>/leftStick", "StickDeadzone")
                .WithHiddenSlot(BindingSlot.Keyboard)
                .WithHiddenSlot(BindingSlot.Mouse));

            Add(MultiBinding.CreateV2For(ActionType.VMouseCycleUI)
                .WithUIGroup("vmouse")
                .Bind("<Gamepad>/dpad")
                //.Bind4("<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow")
                .WithHiddenSlot(BindingSlot.Keyboard)
                .WithHiddenSlot(BindingSlot.Mouse));

            AddNonLockable(MultiBinding.CreateFloatFor(ActionType.VMouseScroll)
                .WithUIGroup("vmouse")
                // Float is just for binding form, it's really a Vector2
                .Bind2("<Gamepad>/leftTrigger", "<Gamepad>/rightTrigger")
                // Fuck stream deck's trackpad clicking, it's not going to be used
                //.Bind2("<Gamepad>/leftShoulder", "<Gamepad>/rightShoulder")
                .WithHiddenSlot(BindingSlot.Keyboard)
                .WithHiddenSlot(BindingSlot.Mouse));

            Add(MultiBinding.CreateFor(ActionType.VMouseButtonL)
                .WithUIGroup("vmouse")
                .Bind("<Gamepad>/buttonSouth")
                .WithHiddenSlot(BindingSlot.Keyboard)
                .WithHiddenSlot(BindingSlot.Mouse));

            /*
            Add(MultiBinding.CreateFor(ActionType.VMouseButtonM)
                .WithUIGroup("vmouse")
                .Bind("<Gamepad>/buttonNorth")
                .WithHiddenSlot(BindingSlot.Keyboard)
                .WithHiddenSlot(BindingSlot.Mouse));
            */

            Add(MultiBinding.CreateFor(ActionType.VMouseButtonR)
                .WithUIGroup("vmouse")
                .Bind("<Gamepad>/buttonEast")
                .WithHiddenSlot(BindingSlot.Keyboard)
                .WithHiddenSlot(BindingSlot.Mouse));

            /*
            // Fuck this behavior
            Add(MultiBinding.CreateFor(ActionType.VMouseForward)
                .WithUIGroup("vmouse")
                .WithHiddenSlot(BindingSlot.Keyboard)
                .WithHiddenSlot(BindingSlot.Mouse));

            Add(MultiBinding.CreateFor(ActionType.VMouseBack)
                .WithUIGroup("vmouse")
                .WithHiddenSlot(BindingSlot.Keyboard)
                .WithHiddenSlot(BindingSlot.Mouse));
                */
        }

        private WPopupData conflictsPopup;

        public void ValidateConflicts() {
            // D.Warn("Validating conflicts");
            var ctrlActs = new Dictionary<InputControl, List<MultiBinding>>();
            foreach (var b in List) {
                foreach (var c in b.InputAction.controls) {
                    if (!ctrlActs.TryGetValue(c, out var list)) {
                        list = new();
                        ctrlActs.Add(c, list);
                    }
                    list.Add(b);
                }
            }
            StringBuilder sb = null;
            var conflicting = new List<MultiBinding>();
            foreach (var b in ctrlActs) {
                var group = b.Value;
                if (group.Count < 2) {
                    continue;
                }
                if (group.Count == 2 && IsAutoSwapPair(group[0], group[1])) {
                    SwapIfSame(group[0], group[1], BindingSlot.Keyboard);
                    SwapIfSame(group[0], group[1], BindingSlot.Mouse);
                    SwapIfSame(group[0], group[1], BindingSlot.Gamepad);
                    continue;
                }
                // Sharing a control is fine across layers, that is what the
                // layers are for. Only list the bindings that really collide.
                conflicting.Clear();
                for (int i = 0; i < group.Count; i++) {
                    for (int j = 0; j < group.Count; j++) {
                        if (i != j && IsConflict(group[i], group[j])) {
                            conflicting.Add(group[i]);
                            break;
                        }
                    }
                }
                if (conflicting.Count == 0) {
                    continue;
                }
                var glyph = GlyphMap.GetControlGlyph(conflicting[0].InputAction, b.Key, out var devName, out var ctrlName);
                (sb ??= new()).Append(glyph);
                sb.AppendFormat(" ({0} {1}):\n", devName, ctrlName);
                foreach (var v in conflicting) {
                    sb.AppendFormat("  - {0}\n", v.ActionName);
                }
                sb.Append("\n");
            }
            if (sb != null) {
                conflictsPopup?.Despawn();
                conflictsPopup = WPopup.Create(true, IconId.CWarning,
                    T.DuplicateBinding, null, sb.ToString())
                        .Spawn(UIPoolRoot.SystemPopupContainer);
            }
        }

        private static bool IsAutoSwapPair(MultiBinding a, MultiBinding b) {
            return (a.AutoSwapWithAction != ActionType.None && a.AutoSwapWithAction == b.Action)
                || (b.AutoSwapWithAction != ActionType.None && b.AutoSwapWithAction == a.Action);
        }

        // Two bindings on one control collide when nothing decides between them:
        // same layer, or one of them is in Always, which is outside of the
        // layer order. Different layers shadow each other instead.
        public static bool IsConflict(MultiBinding a, MultiBinding b) {
            if (a == b) {
                return false;
            }
            if (a.Layer != b.Layer && a.Layer != InputLayer.Always
                    && b.Layer != InputLayer.Always) {
                return false;
            }
            if (IsAutoSwapPair(a, b)) {
                return false;
            }
            if (HasDisjointContexts(a, b)) {
                return false;
            }
            // Allow certain duplicates, like camera drag and rotate
            return !(a.AllowsDuplicateWith(b.Action) && b.AllowsDuplicateWith(a.Action));
        }

        // Same layer, but never live together: entity action hotkeys made for
        // different entity types, the bottom bar only ever shows one type.
        // Always is outside of the layer order, so it never qualifies.
        private static bool HasDisjointContexts(MultiBinding a, MultiBinding b) {
            return a.Layer == b.Layer && a.Layer != InputLayer.Always
                && a.ContextMask != 0 && b.ContextMask != 0
                && (a.ContextMask & b.ContextMask) == 0;
        }

        public static bool SharesControl(MultiBinding a, MultiBinding b) {
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

        public bool HasConflicts(MultiBinding binding) {
            foreach (var other in List) {
                if (IsConflict(binding, other) && SharesControl(binding, other)) {
                    return true;
                }
            }
            return false;
        }

        public void GetConflicts(MultiBinding binding, List<MultiBinding> into) {
            foreach (var other in List) {
                if (IsConflict(binding, other) && SharesControl(binding, other)) {
                    into.Add(other);
                }
            }
        }

        // Bindings on the same control in a lower layer, which do not fire while
        // this one is live. Pass higher = true for the ones that win over this one.
        public void GetShadowed(MultiBinding binding, List<MultiBinding> into, bool higher = false) {
            if (binding.Layer == InputLayer.Always) {
                return;
            }
            foreach (var other in List) {
                if (other == binding || other.Layer == InputLayer.Always
                        || other.Layer == binding.Layer) {
                    continue;
                }
                if ((other.Layer > binding.Layer) == higher && SharesControl(binding, other)) {
                    into.Add(other);
                }
            }
        }

        // Bindings of the same layer on the same control that are not a conflict,
        // because their contexts never overlap
        public void GetCoexisting(MultiBinding binding, List<MultiBinding> into) {
            foreach (var other in List) {
                if (other != binding && HasDisjointContexts(binding, other)
                        && SharesControl(binding, other)) {
                    into.Add(other);
                }
            }
        }

        private readonly List<MultiBinding> relationsTmp = new();

        // Settings row tooltip: the context of the binding and what it collides
        // with or takes the key from
        public string DescribeRelations(MultiBinding binding, out bool hasConflicts) {
            var sb = new StringBuilder();
            sb.Append(T.InputLayerTip_V1.F(LayerName(binding.Layer)));
            relationsTmp.Clear();
            GetConflicts(binding, relationsTmp);
            hasConflicts = relationsTmp.Count > 0;
            AppendRelations(sb, T.InputConflictWith_V1, true);
            relationsTmp.Clear();
            GetCoexisting(binding, relationsTmp);
            AppendRelations(sb, T.InputSharesKeyWith_V1, false);
            relationsTmp.Clear();
            GetShadowed(binding, relationsTmp);
            AppendRelations(sb, T.InputOverrides_V1, false);
            relationsTmp.Clear();
            GetShadowed(binding, relationsTmp, true);
            AppendRelations(sb, T.InputOverriddenBy_V1, false);
            relationsTmp.Clear();
            return sb.ToString();
        }

        private static string LayerName(InputLayer layer) {
            switch (layer) {
                case InputLayer.World: return T.InputLayerWorld;
                case InputLayer.Selection: return T.InputLayerSelection;
                case InputLayer.Tool: return T.InputLayerTool;
                case InputLayer.ShipCombat: return T.NounShipCombat;
                default: return T.InputLayerAlways;
            }
        }

        private void AppendRelations(StringBuilder sb, string format, bool isWarning) {
            if (relationsTmp.Count == 0) {
                return;
            }
            var names = new StringBuilder();
            for (int i = 0; i < relationsTmp.Count; i++) {
                if (i > 0) { names.Append(", "); }
                names.Append(relationsTmp[i].ActionName);
            }
            var line = format.F(names.ToString());
            sb.Append('\n');
            sb.Append(isWarning ? Texts.Red(line) : line);
        }

        public static string GamepadFlavor(Gamepad g) {
            var gName = g.name;
            if (The.Platform.IsSteamDeck()) {
                return "Steam";
            }
            if (gName.StartsWith("DualShock")) {
                if (gName.Contains("4")) {
                    return "PS4";
                } else {
                    return "PS5";
                }
            }
            if (gName.Contains("Switch")) {
                return "Switch";
            }
            //D.Log("Xbox or unknown controller name: {0}", gName);
            return "Xbox";
        }

        // Set while the scripted capture tooling (Assets/Scripts/Capture) drives the game
        // through its own input devices, which happens with the window unfocused. Not dev-only:
        // captures run in non-development builds, which have no watermark.
        public bool IgnoreAppFocus;

        #if !PRODUCTION_BUILD
        public bool HasSimJustPressed;
        public bool HasSimJustReleased;
        public bool HasSimPressed;
        private readonly List<ActionType> simJustPressedQueue = new();
        private readonly List<ActionType> simJustReleasedQueue = new();
        private readonly List<ActionType> simPressedQueue = new();

        public void SimulateJustReleased(ActionType action, int times = 1) {
            for (int i = 0; i < times; i++) {
                simJustReleasedQueue.Add(action);
            }
            HasSimJustReleased = true;
        }
        public void SimulateJustPressed(ActionType action, int times = 1) {
            for (int i = 0; i < times; i++) {
                simJustPressedQueue.Add(action);
            }
            HasSimJustPressed = true;
        }
        public void SimulatePressed(ActionType action, int times = 1) {
            for (int i = 0; i < times; i++) {
                simPressedQueue.Add(action);
            }
            HasSimPressed = true;
        }
        public void ClearSimPressed() {
            simJustReleasedQueue.Clear();
            simJustPressedQueue.Clear();
            simPressedQueue.Clear();
            HasSimJustReleased = false;
            HasSimJustPressed = false;
            HasSimPressed = false;
            Resolver.ClearSimControls();
        }

        // The Simulate* methods above answer "this action fired" and skip the
        // layer resolution. These fake the device instead, so which binding
        // wins the key is decided by the real code.
        public bool HasSimControls => Resolver.HasSimInput;

        public void SimulateControlPress(string controlPath) {
            Resolver.SimulateControlPress(controlPath);
        }

        public void SimulateControlRelease(string controlPath) {
            Resolver.SimulateControlRelease(controlPath);
        }
        #endif

        public string GetGlyphFor(ActionType act) {
            if (!HasBinding(act)) { return null; }
            var glyph = GetBinding(act)
                    .AllControlGlyphs(out _);
            return glyph;
        }

        public string GetGlyphFor(MultiBinding binding) {
            if (!HasBinding(binding)) { return null; }
            return binding.AllControlGlyphs(out _);
        }

        // For named bindings, which have no ActionType
        public string AddShortcutGlyphTo(string text, string bindingId) {
            // The website exporter only has canonical glyphs for ActionType bindings
            if (CodexExportScope.IsActive || !ById.TryGetValue(bindingId, out var binding)) {
                return text;
            }
            var glyph = GetGlyphFor(binding);
            if (string.IsNullOrWhiteSpace(glyph)) {
                return text;
            }
            return $"{glyph} {text}";
        }

        public string AddShortcutGlyphTo(string text, ActionType act) {
            if (!HasBinding(act)) {
                return text;
            }
            if (CodexExportScope.IsActive) {
                var canonical = CodexExportScope.ResolveGlyph(act);
                if (!string.IsNullOrWhiteSpace(canonical)) {
                    return $"{canonical} {text}";
                }
                return text;
            }
            var glyph = GetBinding(act)
                    .AllControlGlyphs(out var tooltip);
            if (!string.IsNullOrWhiteSpace(glyph)) {
                return $"{glyph} {text}";
            }
            // if (tooltip != null) {
            //     udb.WithTooltip(T.ShortcutTip_V1.F(tooltip));
            // }
            return text;
        }
    }
}
