using Game.Constants;
using UnityEngine;
using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using Game.Input;
using Game.Systems.AI;
using Game.UI;

namespace Game.Tools {
    public sealed class ToolTasksRetry : ToolTasksBase {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolTasksRetry());
        }
        public override string MenuIcon => IconId.WRetry;
        public const string ToolId = "ads_retry";
        public override string Id => ToolId;
        public override string ParentId => ToolTasksManage.ToolId;
        public override string ParentParam => "retry";
        public override ActionType Shortcut => ActionType.ShortcutAdsRetry;
        public override string[] ShortcutKeys => new[] { "<Keyboard>/y" };
        public override int Cursor => CursorTypeH.CursorRetry;

        protected override void OnClickUp(List<AIGoal> goals) {
            var ticks = G.Ticks;
            bool success = false;
            for (int i = goals.Count - 1; i >= 0; i--) {
                AIGoal goal = goals[i];
                success |= AIGoal.TryRetry(goal, ticks, true, true);
            }
            if (success) {
                UISounds.PlayDigitalConfirm();
            } else {
                UISounds.PlayActionDenied();
            }
        }
        public override string ToString() {
            return Id;
        }
    }
}
