using System.Buffers;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Randomness;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Conditions {
    public sealed class ConditionDischarged : Condition {
        private ConditionDischarged() : base(ConditionId.Discharged, IconId.CEmergencyRecharge) {
            nameTFunc = () => T.BatteryDischarged;
            descTFunc = () => T.EmergencyRechargeHelp;
            Incapacitates = true;
            PreventsColonyWork = true;
        }
        public override Priority Importance => Priority.High;

        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionDischarged());
        }
        public override bool ShowOnHoverFor(Being b) {
            return b.IsAlive;
        }

        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.PauseWhenBeingHidden;
            var b = cond.Owner.Being;
            if (b == null) {
                D.Err("Trying to start discharged condition for null being! {0}", cond);
            } else {
                StartEmergencyRecharge(b);
                if (cond.Owner.IsFriendly) {
                    b.S.Sys.Log.AddLine(b, Texts.Yellow(T.EmergencyRechargeStart));
                }
            }
        }

        private static void StartEmergencyRecharge(Being b) {
            b.Motion.Immobilize(ImmobilizeReasonIdH.Discharged, true);
            b.Conditions.ShowInfoIcon(IconId.CEmergencyRecharge, T.InfoEmergencyRecharge);
            b.Conditions.HideInfoBar();
        }

        public override void OnEnd(ConditionState cond, long ticks) {
            var b = cond.Owner.Being;
            if (b == null || !b.IsActive) {
                return;
            }
            b.Motion.Immobilize(ImmobilizeReasonIdH.Discharged, false);
            b.Conditions.HideAllInfo();
            if (cond.Owner.IsFriendly) {
                b.S.Sys.Log.AddLine(b, Texts.Green(T.EmergencyRechargeComplete));
            }
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            var b = cond.Owner.Being;
            StartEmergencyRecharge(b);
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            if (cond.Owner.StatsBeing.BatteryCapacity.Value < 1) {
                return;
            }
            var charge = cond.Owner.StatsBeing.BatteryCharge;
            if (charge.Value < 10) {
                if (BodyModSpotComp.IsBatteryOffline(cond.Owner)) {
                    // An Assembly Hub patient has no consumption to derive a rate from,
                    // and the hub charges it
                    return;
                }
                // must be higher than drain, or it won't ever increase!
                var rechargeRate = cond.Owner.StatsBeing.BatteryConsumption.Value * 1.5f;
                if (rechargeRate < 0.001f) {
                    D.ErrSoft("Recharge rate too low! {0} | {1} | Modifiers: {2}",
                        rechargeRate, cond.Owner,
                        D.DumpCollection(cond.Owner.StatsBeing.BatteryConsumption.Modifiers));
                    rechargeRate = 0.1f;
                }
                if (!charge.TryIncrease(rechargeRate)) {
                    D.Err("Failed increasing charge during emergency recharge");
                }
                cond.Owner.Being?.Conditions.ShowInfoBar(Consts.ColorBatteryBar,
                    charge.Value / 10f, T.EmergencyRechargeBar_V1.F(
                        Units.Percentage(charge.Value / 10f)));
            } else {
                cond.Owner.RemoveCondition(s, IdH);
            }
        }
    }
}
