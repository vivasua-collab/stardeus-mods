using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Utils;

namespace Game.Systems.Conditions {
    // Blood toxicity the kidneys failed to clear (BloodFiltration owns the severity). It wounds
    // organs in the order kidney failure does: the gut first, then liver and lungs, then the
    // heart and brain. The wounds can't heal while the toxicity that caused them remains.
    public sealed class ConditionToxemia : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionToxemia());
        }
        public override Priority Importance => Priority.High;
        public override bool ShowOnHoverFor(Being b) {
            return b.IsAlive;
        }

        private INamedEntity source;

        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.PauseWhenBeingHidden;
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.PauseWhenBeingHidden;
        }

        public override void OnEnd(ConditionState cond, long ticks) {
            foreach (var slot in cond.Owner.Body.Slots) {
                if (slot.IsPartRemoved) { continue; }
                Release(slot);
            }
        }

        // Each organ's toxin wound sits exactly at the severity the toxicity calls for, locked
        // with CantHeal. A wound above it (the toxicity fell) is released to heal normally, and
        // locks again once it heals down to that severity.
        public static void SyncWounds(ConditionState cond, long ticks) {
            var self = (ConditionToxemia) cond.Condition;
            self.source ??= NamedEntity.For(self.NameT);
            foreach (var slot in cond.Owner.Body.Slots) {
                if (slot.IsPartRemoved) { continue; }
                var wanted = WantedSeverity(slot.Type.IdH, cond.Severity);
                var hasWound = slot.TryGetWound(DamageType.Toxin, out var w);
                if (wanted == Severity.Irrelevant) {
                    Release(slot);
                } else if (!hasWound || w.Severity < wanted) {
                    // force 0: the wound lands at the wanted severity, no random escalation
                    slot.TryAddOrUpdateWound(self.source, DamageType.Toxin, wanted, ticks, 0f,
                        WoundFlags.CantHeal);
                } else if (w.Severity == wanted) {
                    slot.TrySetWoundFlags(DamageType.Toxin, w.Flags | WoundFlags.CantHeal);
                } else {
                    Release(slot);
                }
            }
        }

        private static void Release(BodyPartSlot slot) {
            if (slot.TryGetWound(DamageType.Toxin, out var w)) {
                slot.TrySetWoundFlags(DamageType.Toxin, w.Flags & ~WoundFlags.CantHeal);
            }
        }

        // Kidneys are left out on purpose: a toxin wound on a weak kidney would weaken it more,
        // and the toxicity would climb by itself instead of holding where the kidneys put it.
        // Up to Severe at most three organs are Moderate (gut and heart), because more than three
        // Moderate parts down a being (Body.CheckCondition): the failing heart at Critical is what
        // downs it. Non-vital organs stop at Severe, only the heart can fail.
        public static Severity WantedSeverity(int partTypeH, Severity toxemia) {
            if (partTypeH == BodyPartIdH.Stomach || partTypeH == BodyPartIdH.Intestines) {
                switch (toxemia) {
                    case Severity.Mild: return Severity.Mild;
                    case Severity.Moderate:
                    case Severity.Severe: return Severity.Moderate;
                    case Severity.Critical:
                    case Severity.Fatal: return Severity.Severe;
                    default: return Severity.Irrelevant;
                }
            }
            if (partTypeH == BodyPartIdH.Liver || partTypeH == BodyPartIdH.Lung) {
                switch (toxemia) {
                    case Severity.Moderate:
                    case Severity.Severe: return Severity.Mild;
                    case Severity.Critical: return Severity.Moderate;
                    case Severity.Fatal: return Severity.Severe;
                    default: return Severity.Irrelevant;
                }
            }
            if (partTypeH == BodyPartIdH.Heart) {
                switch (toxemia) {
                    case Severity.Severe: return Severity.Moderate;
                    case Severity.Critical: return Severity.Critical;
                    case Severity.Fatal: return Severity.Fatal;
                    default: return Severity.Irrelevant;
                }
            }
            if (partTypeH == BodyPartIdH.Brain) {
                switch (toxemia) {
                    case Severity.Severe: return Severity.Mild;
                    case Severity.Critical: return Severity.Moderate;
                    case Severity.Fatal: return Severity.Severe;
                    default: return Severity.Irrelevant;
                }
            }
            return Severity.Irrelevant;
        }

        private ConditionToxemia() : base(ConditionId.Toxemia, IconId.CPoison) {}
    }
}
