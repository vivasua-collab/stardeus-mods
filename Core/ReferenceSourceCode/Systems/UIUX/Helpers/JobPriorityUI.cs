using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    // How a worker's job priority is shown and changed, the same in the Job Matrix and the
    // being's Jobs subtab: left click raises, right click lowers, both wrap through off
    public static class JobPriorityUI {
        private static readonly string[] shown = new string[JobSlot.MaxPriority + 1];

        public static string Text(JobSlot slot) {
            if (slot.Incapable) { return string.Empty; }
            return Text(slot.Priority);
        }

        public static string Text(byte priority) {
            if (priority > JobSlot.MaxPriority) { priority = JobSlot.MaxPriority; }
            return shown[priority] ??= Format(priority);
        }

        private static string Format(byte priority) {
            if (priority == 0) { return Texts.Grey("-"); }
            var num = priority.ToString();
            if (priority >= 4) { return Texts.Accented(num); }
            if (priority <= 2) { return Texts.Grey(num); }
            return num;
        }

        public static void Cycle(BeingData being, JobSlot slot, int dir) {
            if (being != null && being.HasCondition(ConditionIdH.MentalBreakdown)) {
                UISounds.PlayActionDenied();
                return;
            }
            slot.CyclePriority(dir);
        }

        // the priority line and the click hint, for the end of a job tooltip
        public static string TooltipLines(JobSlot slot) {
            if (slot.Incapable) { return null; }
            var value = slot.Priority == 0 ? T.Off : Text(slot.Priority);
            var hint = T.JobPriorityClickHint_V2.F(The.Bindings.GetControlName(ActionType.Click),
                The.Bindings.GetControlName(ActionType.ClickCancel));
            return $"{T.JobPriority_V1.F(value)}\n{Texts.Grey(hint)}";
        }
    }
}
