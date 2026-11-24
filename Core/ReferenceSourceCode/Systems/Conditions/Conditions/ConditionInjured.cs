using System.Buffers;
using Game.Components;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionInjured : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionInjured());
        }
        public override Priority Importance => Priority.High;
        public override int MoodSourceH => MoodSourceIdH.Pain;
        // private readonly StatModifier InjuredSetTo0Mod = StatModifier.Sets(0f, T.CondInjured);
        // sleep suffers more the worse the pain
        // Lazy: Register runs before the language loads, so a label made then stays English
        private StatModifier injuredSleepModMild;
        private StatModifier InjuredSleepModMild => injuredSleepModMild ??= StatModifier.AddPercent(-0.05f, T.CondInjured);
        private StatModifier injuredSleepModModerate;
        private StatModifier InjuredSleepModModerate => injuredSleepModModerate ??= StatModifier.AddPercent(-0.1f, T.CondInjured);
        private StatModifier injuredSleepMod;
        private StatModifier InjuredSleepMod => injuredSleepMod ??= StatModifier.AddPercent(-0.15f, T.CondInjured);
        // only while a part the being walks with is hurt (WoundPain.IsLocomotion)
        private StatModifier injuredMovementMod;
        private StatModifier InjuredMovementMod => injuredMovementMod ??= StatModifier.AddPercent(-0.25f, T.CondInjured);
        public override void Tick(GameState s, ConditionState cond, long ticks) {
            int numWounds = 0;
            bool hasBleedingWounds = false;
            bool slowsDown = false;
            var stats = cond.Owner.StatsBeing;
            // the worst severity the wounds hurt and hinder like (WoundPain.EffectiveSeverity)
            Severity sev = Severity.Irrelevant;
            // what all the wounds hurt together (PainShock)
            float pain = 0f;
            var whr = stats.WoundHealRate.Value;
            bool needsCondUIUpdate = false;
            // 1. run through all the wounds, heal what we can, track severity/bleeding
            foreach (var slot in cond.Owner.Body.Slots) {
                if (slot.IsPartRemoved) { continue; }
                bool needsCondUpdate = false;
                var wnds = slot.State?.Wounds;
                if (!Arrays.IsEmpty(wnds)) {
                    for (int i = wnds.Count - 1; i >= 0; i--) {
                        var w = wnds[i];
                        if (Wound.TryHeal(cond.Owner, whr, slot, ref w,
                                ref needsCondUpdate,
                                ref needsCondUIUpdate)) {
                            continue;
                        }
                        // a destroyed part never heals, but its pain settles
                        if (WoundPain.Settle(ref w, whr)) {
                            needsCondUIUpdate = true;
                        }

                        // Update the wound's HealAmt
                        wnds[i] = w;

                        if (w.BleedLitersPerHour > 0f) {
                            hasBleedingWounds = true;
                        }
                        if (w.IsSettled) { continue; }
                        var effSev = WoundPain.EffectiveSeverity(w, slot);
                        sev = Severities.Bigger(sev, effSev);
                        pain += WoundPainSource.PainOf(effSev);
                        if (!slowsDown && effSev >= Severity.Moderate
                                && WoundPain.IsLocomotion(slot)) {
                            slowsDown = true;
                        }
                        numWounds++;
                    }
                }
                if (needsCondUpdate) {
                    slot.UpdateCondition(false);
                }
            }

            if (needsCondUIUpdate) {
                cond.Owner.NotifyConditionsListChanged();
            }

            if (numWounds == 0 || cond.Owner.Body.HasFlag(BodyFlags.Invincible)) {
                cond.Owner.RemoveCondition(s, cond.Condition.IdH);
                return;
            }

            PainShock.Update(s, cond.Owner, pain);
            cond.SetSeverity(sev);
            SetModifier(stats.SleepRate, InjuredSleepModMild, sev == Severity.Mild);
            SetModifier(stats.SleepRate, InjuredSleepModModerate, sev == Severity.Moderate);
            SetModifier(stats.SleepRate, InjuredSleepMod, sev >= Severity.Severe);
            SetModifier(stats.MoveSpeed, InjuredMovementMod, slowsDown);
            if (ticks % Consts.TicksPer15Minutes == 4 && cond.Owner.Mind.Health != null) {
                string painTitle = null;
                short painEffect = 0;
                switch (sev) {
                    case Severity.Mild: {
                        painTitle = T.NounPainMild;
                        painEffect = -3;
                        break;
                    }
                    case Severity.Moderate: {
                        painTitle = T.NounPainModerate;
                        painEffect = -5;
                        break;
                    }
                    case Severity.Severe:
                    case Severity.Critical: {
                        painTitle = T.NounPainSevere;
                        painEffect = -7;
                        break;
                    }
                    case Severity.Irreparable:
                    case Severity.Fatal: {
                        painTitle = T.NounPainExcruciating;
                        painEffect = -10;
                        break;
                    }
                }
                // One source for every severity, so a changed severity updates the effect
                // instead of adding a second one, and no painful wound left removes it
                SetMood(s, cond, IconId.CStress, painTitle, painEffect);
            }

            // Blood loss is handled by ConditionBleeding, which (unlike this condition)
            // keeps ticking after death. Start it here since ConditionInjured only ticks
            // while alive, so bleeding always begins on a living being.
            if (hasBleedingWounds && !cond.Owner.HasCondition(ConditionIdH.Bleeding)) {
                ConditionState.CreateFor(s, ConditionIdH.Bleeding, cond.Owner, Severity.Mild, true);
            }

            // if (b?.IsActive == true) {
            //     var bloodAmt = stats.BloodAmount.Value;
            //     if (bloodAmt < bloodCap) {
            //         b.Persona.ShowInfoBar(Consts.ColorHealthBar,
            //             bloodAmt / bloodCap, bloodAmt * 1000f, (int) (bloodCap * 1000f),
            //                 "blood.amount.bar");
            //     }
            // }
        }

        private static void SetModifier(Stat stat, StatModifier mod, bool on) {
            if (on) {
                stat.TryAddModifier(mod);
            } else {
                stat.RemoveModifier(mod, false);
            }
        }

        public override void OnStart(ConditionState cond, long ticks) {
            // crawler can attach to a colonist and then shit happens
            cond.Flags |= ConditionFlags.PauseWhenBeingHidden;
            // cond.Owner.StatsBeing.BloodRegenRate.TryAddModifier(InjuredSetTo0Mod);
            // the sleep and movement modifiers depend on the wounds, so Tick sets them
            // D.Warn("Starting injured state");
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            OnStart(cond, ticks);
            // D.Warn("Loading injured state");
        }

        public override void OnEnd(ConditionState cond, long ticks) {
            var stats = cond.Owner.StatsBeing;
            var b = cond.Owner.Being;
            if (b != null) {
                b.Persona.HideInfoBar();
            }
            stats.SleepRate.RemoveModifier(InjuredSleepModMild, false);
            stats.SleepRate.RemoveModifier(InjuredSleepModModerate, false);
            stats.SleepRate.RemoveModifier(InjuredSleepMod, false);
            stats.MoveSpeed.RemoveModifier(InjuredMovementMod, false);
            // no wounds left to hurt; a pain shock ends on its own next tick
            stats.WoundPain.SetTotal(0f, stats.Pain);
            cond.Owner.Body.CheckCondition(false);
        }

        private ConditionInjured() : base(ConditionId.Injured, IconId.CBlood) {}
    }
}
