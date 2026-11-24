using Game.Constants;
using Game.Data;
using UnityEngine;
using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using Game.Input;
using Game.Components;
using Game.Systems.Hints;
using Game.UI;
using KL.Utils;
using Game.Systems.AI;

namespace Game.Tools {
    public sealed class ToolTasksCancel : ToolTasksBase, IModifyTool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolTasksCancel());
        }
        public override string MenuIcon => IconId.WCancel;
        public const string ToolId = "ads_cancel";
        public bool CanBuild => false;
        public bool CanRemove => true;

        public override string Id => ToolId;
        public override string ParentId => ToolTasksManage.ToolId;
        public override string ParentParam => "cancel";
        protected override Color dragRectColor => Consts.RedRectColor;
        public override ActionType Shortcut => ActionType.ShortcutAdsCancel;
        public override string[] ShortcutKeys => new[] { "<Keyboard>/c" };
        public override int Cursor => CursorTypeH.CursorCancel;

        protected override string MarkedPriorityText(AIGoal goal) {
            return CancelMarker(goal.GoalPriority);
        }

        protected override void OnClickUp(List<AIGoal> goals) {
            CancelGoals(S, goals);
        }

        public static bool CancelGoals(GameState s, List<AIGoal> goals) {
            var ticks = G.Ticks;
            bool cancelled = false;
            for (int i = goals.Count - 1; i >= 0; i--) {
                cancelled |= TryCancelGoal(s, goals[i], ticks);
            }
            return cancelled;
        }

        public static bool TryCancelGoal(GameState s, AIGoal goal, long ticks) {
            if (goal == null) { return false; }
            s.Sys.Hint.Deactivate(HintAdCancel.Id);
            AIGoal.Cancel(goal, ticks);
            return true;
        }

        public override string ToString() {
            return Id;
        }
    }
}
