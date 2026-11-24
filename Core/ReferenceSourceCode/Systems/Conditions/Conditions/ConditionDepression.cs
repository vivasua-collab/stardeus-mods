using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Mental;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionDepression : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionDepression());
        }
        public override bool ShowOnHoverFor(Being b) {
            return b.IsAlive;
        }
        public override Priority Importance => Priority.Low;
        public override int MoodSourceH => MoodSourceIdH.Depression;
        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.PauseWhenBeingHidden;
            cond.Flags |= ConditionFlags.RemoveOnResurrect;
        }
        public override void OnLoad(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.PauseWhenBeingHidden;
            cond.Flags |= ConditionFlags.RemoveOnResurrect;
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            if (ticks % Consts.TicksPerHour == 0) {
                if (cond.Owner.Mind.Health?.IsEnlightened == true) {
                    D.Err("Removing depression from enlightened being: {0}", cond.Owner);
                    cond.Owner.RemoveCondition(s, cond.Condition.IdH);
                    return;
                }
                SetMood(s, cond, null, T.CondDepression, -10);
                if (cond.Severity > Severity.Severe) {
                    AIActionUseSuicideBooth.EnsureGoalFor(cond.Owner, ticks);
                }
            }
        }

        private ConditionDepression() : base(ConditionId.Depression, IconId.CMoodBad) {
            PreventsColonyWork = true;
        }
    }
}
