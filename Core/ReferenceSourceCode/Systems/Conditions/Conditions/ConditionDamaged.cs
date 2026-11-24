using System.Buffers;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionDamaged : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionDamaged());
        }
        public override Priority Importance => Priority.Normal;
        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.PauseWhenBeingHidden;
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            int numWounds = 0;
            var maintenanceRate = cond.Owner.StatsBeing.MaintenanceRate.Value;
            var canHeal = maintenanceRate > 0f;
            bool needsCondUIUpdate = false;
            Severity sev = Severity.Irrelevant;
            float pain = 0f;
            float worstHealableWoundHealAmt = 1f;
            // 1. run through all the wounds and accumulate the bleed %
            foreach (var slot in cond.Owner.Body.Slots) {
                if (slot.IsPartRemoved) { continue; }
                bool needsCondUpdate = false;
                var wnds = slot.State.Wounds;
                if (!Arrays.IsEmpty(wnds)) {
                    for (int i = wnds.Count - 1; i >= 0; i--) {
                        var w = wnds[i];
                        if (canHeal) {
                            if (Wound.TryHeal(cond.Owner, maintenanceRate, slot, ref w,
                                    ref needsCondUpdate,
                                    ref needsCondUIUpdate)) {
                                continue;
                            }
                            if (w.Severity < Severity.Irreparable) {
                                worstHealableWoundHealAmt = Mathf.Min(w.HealAmt, worstHealableWoundHealAmt);
                            }
                            wnds[i] = w;
                        }
                        numWounds++;
                        sev = Severities.Bigger(sev, w.Severity);
                        // the robot's hidden pain: what drives its system shock (PainShock).
                        // A destroyed part is gone, not strained, and a robot's never settles
                        // like flesh does: counted, three of them would shut a bot down for good
                        if (w.Severity < Severity.Irreparable) {
                            pain += WoundPainSource.PainOf(w.Severity);
                        }
                    }
                }
                if (needsCondUpdate) {
                    slot.UpdateCondition(false);
                }
            }

            if (canHeal) {
                if (worstHealableWoundHealAmt > 0f && worstHealableWoundHealAmt < 1f) {
                    cond.Owner.Being?.Conditions.ShowInfoBar(Consts.ColorHealthBar,
                        worstHealableWoundHealAmt, T.Repairing);
                } else {
                    cond.Owner.Being?.Conditions.HideInfoBar();
                }
            }

            if (needsCondUIUpdate) {
                cond.Owner.NotifyConditionsListChanged();
            }

            if (numWounds == 0) {
                cond.Owner.RemoveCondition(s, cond.Condition.IdH);
                return;
            }

            PainShock.Update(s, cond.Owner, pain);
            cond.SetSeverity(sev);

            bool canBleedNow = cond.Owner.HasStateFlag(Creatures.BeingState.Alive);
            if (canBleedNow && Rng.UChance(numWounds * 0.01f)) {
                cond.Owner.Body.Bleed(0.25f);
            }
        }

        public override void OnEnd(ConditionState cond, long ticks) {
            // no damage left; a system shock ends on its own next tick
            var stats = cond.Owner.StatsBeing;
            stats.WoundPain.SetTotal(0f, stats.Pain);
        }

        private ConditionDamaged() : base(ConditionId.Damaged, IconId.CBlood) {}
    }
}
