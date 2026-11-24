using Game.Constants;
using UnityEngine;
using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using Game.Input;
using Game.Components;
using Game.Systems.AI;
using KL.Utils;
using Game.Systems.Hints;
using Game.UI;

namespace Game.Tools {
    public sealed class ToolTasksManage : ToolTasksBase, IModifyTool, IToolWithCycle {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolTasksManage());
        }

        public bool CanBuild => false;
        // Because it can cancel construction tasks
        public bool CanRemove => true;
        public override string MenuIcon => IconId.WTasks;

        public const string ToolId = "manage_ads";
        public override string NameKey => "tasks";
        public override string Id => ToolId;
        public override bool IsConfigurable => true;
        public override bool SkipParameterSorting => true;
        private readonly Dictionary<string, string> flippedParams = new() {
            {"cancel", "retry"},
            {"retry", "cancel"},
            {"priority.increase", "priority.decrease"},
            {"priority.decrease", "priority.increase"},
        };
        private readonly List<string> availableParams = new() {
            "cancel",
            "retry",
            "priority.increase",
            "priority.decrease",
        };
        public override List<string> AvailableParams => availableParams;
        public override ActionType Shortcut => ActionType.ShortcutAdsPriority;
        public override string[] ShortcutKeys => new[] { "<Keyboard>/p" };
        public override bool ShortcutCyclesParameters => true;
        public override string ShortcutNextParam => currentParam switch {
            "priority.increase" => "priority.decrease",
            "priority.decrease" => "cancel",
            "cancel" => "retry",
            _ => "priority.increase",
        };
        public override int Cursor => CursorFor(currentParam);

        private int CursorFor(string param) {
            switch (param) {
                case "priority.increase": return CursorTypeH.CursorUp;
                case "priority.decrease": return CursorTypeH.CursorDown;
                case "cancel": return CursorTypeH.CursorCancel;
                case "retry": return CursorTypeH.CursorRetry;
                default: return CursorTypeH.CursorUp;
            }
        }

        public override string CurrentParam => currentParam;

        private string currentParam;
        private string currentParamFlipped;
        private bool flipMode;

        public override bool Configure(string param) {
            currentParam = param;
            currentParamFlipped = param;
            return true;
        }
        protected override void OnHover() {
            var newFlipMode = The.Bindings.IsPressed(ActionType.CtrlModifier);
            if (flipMode != newFlipMode) {
                flipMode = newFlipMode;
                if (flipMode && flippedParams.TryGetValue(currentParam, out var flippedParam)) {
                    The.SysSig.SetCursor.Send(CursorFor(flippedParam));
                    currentParamFlipped = flippedParam;
                } else {
                    The.SysSig.SetCursor.Send(Cursor);
                    currentParamFlipped = currentParam;
                }
            }
        }

        protected override string MarkedPriorityText(AIGoal goal) {
            int priority = goal.GoalPriority;
            // Shows the change the click will really make, SetPriority clamps to 0..5
            int delta;
            switch (currentParamFlipped) {
                case "cancel": return CancelMarker(priority);
                case "priority.increase": {
                    delta = Mathf.Min(PriorityStep, MaxPriority - priority);
                    break;
                }
                case "priority.decrease": {
                    delta = -Mathf.Min(PriorityStep, priority);
                    break;
                }
                default: return base.MarkedPriorityText(goal);
            }
            if (delta == 0) {
                return base.MarkedPriorityText(goal);
            }
            return ChangeMarker(priority, delta);
        }

        private static int PriorityStep =>
            The.Bindings.IsPressed(ActionType.ShiftModifier) ? 5 : 1;

        protected override void OnCancel() {
            currentParam = null;
            currentParamFlipped = null;
        }

        protected override void OnClickUp(List<AIGoal> goals) {
            sbyte adj;
            if (currentParamFlipped == "priority.increase") {
                adj = 1;
            } else {
                adj = -1;
            }
            if (The.Bindings.IsPressed(ActionType.ShiftModifier)) {
                adj *= 5;
            }
            var ticks = G.Ticks;
            bool? retrySuccess = null;
            foreach (var goal in goals) {
                switch (currentParamFlipped) {
                    case "cancel": {
                        S.Sys.Hint.Deactivate(HintAdCancel.Id);
                        AIGoal.Cancel(goal, ticks);
                        break;
                    }
                    case "retry": {
                        if (retrySuccess == null) {
                            retrySuccess = false;
                        }
                        if (AIGoal.TryRetry(goal, ticks, true, true)) {
                            retrySuccess = true;
                        }
                        break;
                    }
                    default: {
                        goal.AdjustPriority(ticks, adj);
                        break;
                    }
                }
            }
            if (retrySuccess != null) {
                if (retrySuccess.Value) {
                    UISounds.PlayDigitalConfirm();
                } else {
                    UISounds.PlayActionDenied();
                }
            }
        }

        public override string ToString() {
            return Id;
        }
        public override string MenuNameFor(string param) {
            return $"manage.ads.{param}";
        }

        public override string MenuIconFor(string param) {
            switch (param) {
                case "priority.increase" : return IconId.WPriorityUp;
                case "priority.decrease" : return IconId.WPriorityDown;
                case "cancel" : return IconId.WCancel;
                case "retry" : return IconId.WRetry;
            }
            return IconId.CMissing;
        }

        public string CycleParam() {
            if (currentParam == "priority.increase") {
                return "priority.decrease";
            }
            return null;
        }
    }
}
