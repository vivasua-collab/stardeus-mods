using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Conditions {
    /// <summary>
    /// Split out of ConditionInjured so blood loss can keep ticking after death
    /// (TickAfterDeath) - a dead being still bleeds out, down to Tunables.BloodDeadBodyPct.
    /// </summary>
    public sealed class ConditionBleeding : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionBleeding());
        }
        public override Priority Importance => Priority.Critical;

        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.TickAfterDeath;
            cond.Flags |= ConditionFlags.PauseWhenBeingHidden;
            // Publish the loss rate right away, without clotting. ConditionsSys iterates its
            // list backwards, so a condition added mid-tick is not ticked until the next one,
            // and stats influenced by BloodLossRate (BloodRegenRate) would lag behind the
            // wound that caused the bleeding.
            var stats = cond.Owner.StatsBeing;
            var bloodCap = stats.BloodCapacity.Value;
            if (bloodCap > 0f) {
                stats.BloodLossRate.UpdateBase(
                    SumBleedLitersPerHour(cond.Owner, 0f) / bloodCap);
            }
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            OnStart(cond, ticks);
        }

        /// <summary>
        /// Totals the bleed rate of every wound. When clotRate (liters per hour taken off each
        /// wound's rate, per tick) is above 0, also applies one tick of clotting to each
        /// bleeding wound, mutating it.
        /// </summary>
        private static float SumBleedLitersPerHour(BeingData owner, float clotRate) {
            float total = 0f;
            foreach (var slot in owner.Body.Slots) {
                if (slot.IsPartRemoved) { continue; }
                var wnds = slot.State?.Wounds;
                if (Arrays.IsEmpty(wnds)) { continue; }
                for (int i = wnds.Count - 1; i >= 0; i--) {
                    var w = wnds[i];
                    var bph = w.BleedLitersPerHour;
                    if (bph <= 0f) { continue; }
                    if (clotRate > 0f) {
                        bph -= clotRate;
                        if (bph < 0f) { bph = 0f; }
                        w.BleedLitersPerHour = bph;
                        wnds[i] = w;
                    }
                    total += bph;
                }
            }
            return total;
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            var owner = cond.Owner;
            var stats = owner.StatsBeing;
            var bloodCap = stats.BloodCapacity.Value;
            var isAlive = owner.Body.IsAlive;

            if (owner.Body.HasFlag(BodyFlags.Invincible) || bloodCap <= 0f) {
                owner.RemoveCondition(s, cond.Condition.IdH);
                return;
            }

            var deadFloor = bloodCap * Tunables.BloodDeadBodyPct;
            if (!isAlive && stats.BloodAmount.Value <= deadFloor) {
                owner.RemoveCondition(s, cond.Condition.IdH);
                return;
            }

            // 1. run through all the wounds, clot them, and accumulate the bleed %.
            // BloodClotRate is a share of the blood capacity, like the bleed rates, so every
            // species gets the same untreated curve; in liters a small animal's wound
            // clotted many times faster, relative to its blood, than a human's.
            var bleedLitersPerH = SumBleedLitersPerHour(owner,
                stats.BloodClotRate.Value * bloodCap);

            // 2. multiply bleed % by blood capacity
            // 3. subtract the blood loss per tick from blood amount stat
            // 4. if blood amount goes to 0, being dies (or, if already dead, floors at
            //    Tunables.BloodDeadBodyPct)
            stats.BloodLossRate.UpdateBase(bleedLitersPerH / bloodCap);
            if (bleedLitersPerH <= 0f) {
                // every wound has clotted
                owner.RemoveCondition(s, cond.Condition.IdH);
                return;
            }

            // BloodLossRate can be suppressed by a modifier while the wounds are still open.
            // Respect that and skip the drain, but keep the condition - ConditionInjured
            // would only recreate it from those same wounds next tick.
            var bloodLossPctPerH = stats.BloodLossRate.Value;
            if (bloodLossPctPerH > 0f) {
                var bloodLossLiters = (bloodLossPctPerH * bloodCap) / Consts.TicksPerHour;
                if (isAlive) {
                    if (stats.BloodAmount.TryDrain(bloodLossLiters)) {
                        var bb = owner.Being?.AIAgent.BB;
                        if (bb != null) {
                            if (bb.BloodLossUntilBleedL <= 0f) {
                                if (owner.Being?.IsActive == true) {
                                    owner.Body.Bleed(0.05f);
                                }
                                bb.BloodLossUntilBleedL = 0.1f;
                            } else {
                                bb.BloodLossUntilBleedL -= bloodLossLiters;
                            }
                        }
                    }
                } else if (stats.BloodAmount.Value - bloodLossLiters <= deadFloor) {
                    // A corpse bleeds down to the floor and no further. Snap to it rather
                    // than draining toward it: float drift can leave blood a hair above the
                    // floor, and TryDrain of an amount too small to move the float still
                    // reports success, so the corpse would keep ticking forever.
                    stats.BloodAmount.UpdateBase(deadFloor);
                    owner.RemoveCondition(s, cond.Condition.IdH);
                    return;
                } else {
                    stats.BloodAmount.TryDrain(bloodLossLiters);
                }
            }

            var bloodPctLeft = stats.BloodAmount.Value / bloodCap;
            Severity sev;
            if (bloodPctLeft < Tunables.BloodLethalPct) {
                sev = Severity.Fatal;
            } else if (bloodPctLeft < 0.25f) {
                sev = Severity.Critical;
            } else if (bloodPctLeft < Tunables.BloodIncapacitatedPct) {
                sev = Severity.Severe;
            } else if (bloodPctLeft < 0.75f) {
                sev = Severity.Moderate;
            } else {
                sev = Severity.Mild;
            }
            cond.SetSeverity(sev);

            if (isAlive) {
                if (bloodPctLeft < Tunables.BloodLethalPct) {
                    // ded
                    BeingUtils.Kill(s, owner, DamageType.Organ,
                        NamedEntity.For(T.EvtOrganFailureTitle), T.DeathBloodLoss, (bp) => {
                        return bp.IsEssential;
                    });
                } else if (bloodPctLeft < Tunables.BloodIncapacitatedPct && !owner.Body.IsIncapacitated) {
                    // collapse if lost more than half of blood
                    owner.Body.CheckCondition(false);
                }
            }
        }

        public override void OnEnd(ConditionState cond, long ticks) {
            var stats = cond.Owner.StatsBeing;
            stats.BloodLossRate.UpdateBase(0f);
            var b = cond.Owner.Being;
            if (b != null) {
                b.AIAgent.BB.BloodLossUntilBleedL = 0f;
            }
            cond.Owner.Body.CheckCondition(false);
        }

        private ConditionBleeding() : base(ConditionId.Bleeding, IconId.CBlood) {}
    }
}
