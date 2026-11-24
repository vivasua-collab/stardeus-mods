using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.UIUX;
using Game.UI;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using System;
using UnityEngine;
using KL.Grid;

namespace Game.Tools {
    // TODO this must be put under runtime, not in The
    public sealed class Toolbox {
        [ExecuteOnReload]
        public static void CleanupStatics() {
            ToolShortcuts.Clear();
            toolFactories.Clear();
            Shortcuts.Clear();
        }
        private static readonly Dictionary<string, Func<ITool>> toolFactories = new();
        private readonly Dictionary<string, ITool> tools = new();
        public readonly List<ITool> PassiveTools = new();
        public ITool ActiveTool;
        // For rare actions / debugging
        public ICollection<string> AvailableToolIds => tools.Keys;
        // For rare actions / debugging
        public ICollection<ITool> AvailableTools => tools.Values;
        public ToolFreeSelect FreeSelect;
        public ToolPlaceTiles BuildTool;
        public ToolConnect ConnectTool;
        public GameState S;
        public bool IsFreeSelect => ActiveTool == FreeSelect;
        public static void BindShortcuts(Bindings bindings) {
            EnsureShortcuts();
            // Tool switching is the World layer: a tool (Rotate), a selection
            // (entity action hotkeys) or ship combat may claim the same key, and
            // whenever they do not, the key falls through to here
            foreach (var t in ToolShortcuts) {
                var toolId = t.Value;
                var bind = MultiBinding.CreateFor(t.Key).InLayer(InputLayer.World);
                var tool = toolFactories[toolId].Invoke();
                foreach (var key in tool.ShortcutKeys) {
                    bind.Bind(key).WithUIGroup("shortcuts");
                }
                bindings.Add(bind);
            }

            // Not an entity action: cancelling has no bottom bar button of its
            // own, it lives inside the task button's quick choice
            bindings.Add(MultiBinding.CreateNamed(CancelSelectedTasksBindingId, () => T.CancelTask)
                .InLayer(InputLayer.Selection)
                .WithUIGroup(EntityActionHotkeys.UIGroup)
                .WithAvailability(() => A.S.Query.HasSelectedTasks.Ask())
                .WithHandler(() => {
                    if (A.S.Query.TryCancelSelectedTasks.Ask()) {
                        UISounds.PlayDigitalConfirm();
                    }
                })
                .Bind("<Keyboard>/c"));
        }
        public const string CancelSelectedTasksBindingId = "sel.CancelTasks";
        public ITool FindByShortcut(ActionType shortcut) {
            var toolId = ToolShortcuts.Get(shortcut, null);
            if (toolId == null) {
                D.Err("No tool with shortcut {0}", shortcut);
                return null;
            }
            return tools.Get(toolId, null);
        }

        public ITool FindChildTool(ITool parent, string param) {
            foreach (var t in tools.Values) {
                if (t.ParentId == parent.Id && t.ParentParam == param) {
                    return t;
                }
            }
            return null;
        }

        public void Initialize(GameState s) {
            S = s;
            Shortcuts.Clear();
            foreach (var tf in toolFactories) {
                Add(tf.Value.Invoke());
            }
            foreach (var tool in tools) {
                if (tool.Value.NameT == null) {
                    if (tool.Value.IsDevTool) {
                        tool.Value.NameT = tool.Value.NameKey.TryT();
                    } else {
                        tool.Value.NameT = tool.Value.NameKey.T();
                    }
                }
                tool.Value.Initialize(s);
            }
            foreach (var tool in PassiveTools) {
                tool.Initialize(s);
            }
            Clear();
        }
        public static void DoubleClick(int p1, ITool tool = null) {
            var v1 = Pos.ToVector(p1);
            Click(v1, tool);
            if (tool == null) {
                tool = The.Toolbox.FreeSelect;
            }
            if (tool is IsDoubleClickableTool dbl) {
                dbl.OnDoubleClick(v1);
            } else {
                D.Err("Not a double-click tool! {0}", tool);
            }
        }
        public static void Click(int p1, ITool tool = null) {
            var v1 = Pos.ToVector(p1);
            Click(v1, tool);
        }

        public static void Click(Vector2 v1, ITool tool = null) {
            tool ??= The.Toolbox.ActiveTool;
            tool.OnHover(v1);
            tool.OnClickDown(v1);
            tool.OnClickUp(v1, v1, 0f);
        }
        public static void ClickDragRelease(int p1, int p2, ITool tool = null) {
            var v1 = Pos.ToVector(p1);
            var v2 = Pos.ToVector(p2);
            ClickDragRelease(v1, v2, tool);
        }

        public static void ClickDragRelease(Vector2 v1, Vector2 v2, ITool tool = null) {
            tool ??= The.Toolbox.ActiveTool;
            tool.OnHover(v1);
            tool.OnClickDown(v1);
            tool.OnHover(v2);
            var dragDist = Vectors.Distance(v1, v2);
            if (dragDist > 0f) {
                tool.OnDrag(v1, v2, dragDist);
            }
            // tool.OnHover(v2);
            tool.OnClickUp(v1, v2, dragDist);
        }

        public void Clear() {
            if (ActiveTool != FreeSelect) {
                ActiveTool?.Deactivate();
                ActiveTool = FreeSelect;
                FreeSelect?.Activate();
            }
            The.SysSig?.SetCursor.Send(CursorTypeH.Cursor);
            S?.Sig?.ToolChanged.Send(ActiveTool);
        }

        public void CleanupAfterGame() {
            /*
            // We don't give a fuck about any of this, new runtime will have new toolbox
            ActiveTool?.Deactivate();
            ActiveTool = FreeSelect;
            FreeSelect?.Activate();
            */
            The.SysSig?.SetCursor.Send(CursorTypeH.Cursor);
        }

        public void SetActive(ITool tool, string param = null) {
            if (tool == BuildTool && param == null) {
                S.UIQuery.ShowBuildMenu.Ask(!UIShowing.BuildMenu);
                return;
            }

            The.SysSig.SetCursor.Send(CursorTypeH.Cursor);
            tool.Activate();
            bool confSuccess;
            if (param != null) {
                confSuccess = tool.Configure(param);
            } else {
                if (tool.ShortcutCyclesParameters) {
                    confSuccess = tool.Configure(tool.ShortcutNextParam);
                } else {
                    confSuccess = tool.Configure(null);
                }
            }
            if (!confSuccess) {
                tool.Deactivate();
                //D.Err("Failed switching into tool {0} with param {1}", tool.Id, param);
                UISounds.PlayButtonClick();
                S.Sig.ToolChanged.Send(FreeSelect);
                // S.Sig.ShowToolMenuItem.Send(tool.MenuItem);
                return;
            }

            if (ActiveTool != tool) {
                ActiveTool?.Deactivate();
                ActiveTool = tool;
            }
            S.Sig.ToolChanged.Send(tool);
            The.SysSig.SetCursor.Send(tool.Cursor);
        }

        public static readonly List<ActionType> Shortcuts = new();
        public readonly static Dictionary<ActionType, string> ToolShortcuts = new();

        public static void Add(string id, Func<ITool> toolFactory) {
            toolFactories[id] = toolFactory;
        }

        public static void EnsureShortcuts() {
            if (ToolShortcuts.Count != 0) { return; }
            foreach (var t in toolFactories) {
                var tool = t.Value();
                if (tool.Shortcut != ActionType.None) {
                    if (Arrays.IsEmpty(tool.ShortcutKeys)) {
                        D.Err("Tool {0} has shortcut {1} but no keys defined!",
                            tool.Id, tool.Shortcut);
                    } else {
                        ToolShortcuts[tool.Shortcut] = tool.Id;
                    }
                }
            }

        }

        private void Add(ITool tool) {
            if (tool is ToolFreeSelect f) { FreeSelect = f; }
            if (tool is ToolPlaceTiles t) { BuildTool = t; }
            if (tool is ToolConnect c) { ConnectTool = c; }
            if (tool.IsPassive) {
                PassiveTools.Add(tool);
                return;
            }
            if (tool.Shortcut != ActionType.None) {
                if (!Shortcuts.AddUniqueInefficient(tool.Shortcut)) {
                    // This will happen every time we load a game from a game
                    #if !PRODUCTION_BUILD
                    D.ErrSoft("Already registered shortcut for tool {0} -> {1}",
                        tool.NameKey, tool.Shortcut);
                    #endif
                }
            }
            tools[tool.Id] = tool;
        }
        public ITool Get(string id) {
            if (tools.TryGetValue(id, out var tool)) {
                return tool;
            }
            D.Err("Tool not available: {0}", id);
            return null;
        }
        public T Get<T>(string id) {
            if (tools.TryGetValue(id, out var tool)) {
                if (tool is T tt) {
                    return tt;
                }
                D.Err("Tried to get a too by id {0} with type {1}, got {2}",
                    id, typeof(T), tool);
                return default;
            }
            D.Err("Tool not available by id: {0}", id);
            return default;
        }

        public void ActivateShortcut(ActionType shortcutType) {
            if (shortcutType == ActionType.None) {
                D.Err("Shortcut pressed with ActionType.NONE");
                return;
            }
            // Keys shared with a selection (dispose, build a copy, relocate) or
            // with a tool (rotate) never get here: those bindings sit in higher
            // input layers and shadow the tool shortcut while they are live
            var tool = FindByShortcut(shortcutType);
            ActiveToolShortcut(tool);
        }

        public void ActiveToolShortcut(ITool tool) {
            if (tool == null) {
                UISounds.PlayActionDenied();
                return;
            }
            if (tool == ActiveTool || tool.ParentId == ActiveTool.Id) {
                // if we are activating a tool via shortcut, let's see
                // if we can cycle the parameter instead of switching back to
                // free select
                if (tool is IToolWithCycle cycle) {
                    var next = cycle.CycleParam();
                    if (next != null) {
                        SetActive(tool, next);
                        UISounds.PlayButtonClick();
                        return;
                    }
                }
                if (tool == BuildTool && tool.CurrentParam != null) {
                    if (UIShowing.BuildMenu) {
                        A.S.UIQuery.ShowBuildMenu.Ask(false);
                    } else {
                        SetActive(FreeSelect);
                        SetActive(tool);
                        UISounds.PlayButtonClick();
                    }
                    return;
                }
                Clear();
                UISounds.PlayCancel();
            } else {
                SetActive(tool);
                UISounds.PlayButtonClick();
            }
        }
    }
}
