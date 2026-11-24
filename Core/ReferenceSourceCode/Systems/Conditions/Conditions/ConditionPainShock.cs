using System;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Conditions {
    // Down from pain (PainShock decides when it starts and ends). Body.CheckCondition sees
    // Incapacitates and puts the being down like any other incapacitation, so rescue, capture,
    // carrying to a bed and the downed raider rules all apply as they are.
    public abstract class ConditionShock : Condition {
        protected ConditionShock(string id, string icon, Func<string> name, Func<string> desc)
                : base(id, icon) {
            nameTFunc = name;
            descTFunc = desc;
            Incapacitates = true;
            PreventsColonyWork = true;
        }
        public override Priority Importance => Priority.High;
        public override bool ShowOnHoverFor(Being b) {
            return b.IsAlive;
        }

        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.PauseWhenBeingHidden;
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.PauseWhenBeingHidden;
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            if (!PainShock.HasWoundCondition(cond.Owner)) {
                cond.Owner.RemoveCondition(s, IdH);
            }
        }
    }

    public sealed class ConditionPainShock : ConditionShock {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionPainShock());
        }
        private ConditionPainShock() : base(ConditionId.PainShock, IconId.CStress,
            () => T.CondPainshock, () => T.CondPainshockDesc) { }
    }

    public sealed class ConditionSystemShock : ConditionShock {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionSystemShock());
        }
        private ConditionSystemShock() : base(ConditionId.SystemShock, IconId.CEnergy,
            () => T.CondSystemshock, () => T.CondSystemshockDesc) { }
    }
}
