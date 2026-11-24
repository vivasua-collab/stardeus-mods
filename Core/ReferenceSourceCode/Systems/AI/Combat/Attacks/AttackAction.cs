#if !PRODUCTION_BUILD
// #define DEBUG_ATTACK
#endif
using System;
using System.Buffers;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Systems.BodyParts;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Systems.Combat;
using Game.Systems.Creatures;

namespace Game.Systems.AI.Combat {
    public abstract class AttackAction {
        public string Id;
        public int IdH;
        private string nameT;
        private Func<string> nameTFunc;
        public string NameT => nameT ??= nameTFunc == null ? $"act.{Id}".T() : nameTFunc();
        protected static void RegisterAction(AttackAction act) {
            Registries.AttackActions.Add(act.Id, act);
        }

        public AttackAction(string id, Func<string> nameT = null) {
            Id = id;
            IdH = Hashes.S(id);
            this.nameTFunc = nameT;
        }

        public virtual void OnCharge(GameState s, AttackMethod method, CombatUnit unit, IAttackable target) {}
        public virtual bool OnExecute(GameState s, AttackMethod method, CombatUnit unit, IAttackable target,
                bool hit, float chance) {
            return hit;
        }
        public virtual void OnCooldown(GameState s, AttackMethod method, CombatUnit unit) {}

        protected void FaceUnit(CombatUnit unit, CombatUnit target) {
            FacePosition(unit, target.Position);
        }
        protected void FacePosition(CombatUnit unit, Vector2 pos) {
            if (unit.State != CombatUnitState.Incapacitated) {
                var w = unit.Agent.Being;
                w.Graphics.SetFacing(Facing.FromDirection(
                    pos - unit.Position, w.Graphics.CurrentFacing));
            }
        }

        public static bool DealDamageTo(GameState s, AttackMethod method,
                IAttackable attacker, CombatUnit target) {
            target.OnHarmed(attacker.AttackablePosition);
            // - figure out which body part did we hit
            var body = target.Data.Body;

            if (target.Data.HasCondition(ConditionIdH.Sleeping)) {
                // waky waky motherfucker
                AIGoal.Interrupt(target.Agent.Goal, s.Ticks);
            }

            var wbp = Caches.WeightedBodyParts;
            wbp.Clear();

            BeingUtils.FillWeighedBodyParts(body, wbp, true, null);

            // TODO calculate armor, etc
            // Hit force tells how strong was the blow compared to the armor we have

            var partHit = Rng.UWeightedFrom(wbp);

            // hit force will be the % of hit that went through body part resistance + armor
            var damage = method.Damage;
            if (method.RoboticDamageMul != 1f && !partHit.InstalledDef.IsBiological) {
                // fries circuits: robot bodies, and bionic parts on anyone
                damage *= method.RoboticDamageMul;
            }
            byte maxSevIncrease = partHit.GetHitSeverityIncrease(method.Type, damage,
                method.Stats.ArmorPenetration.Value);
            if (maxSevIncrease == 0) {
                return false;
            }

            BodyPartSlot internalHit = null;
            var subHitchance = partHit.Type.SubpartHitChance;

            float hitForce = maxSevIncrease / (float) Severity.Fatal;

            if (subHitchance > 0f) {
                subHitchance *= method.SubpartHitChance(hitForce);
                if (Rng.UChance(subHitchance)) {
                    wbp.Clear();
                    BeingUtils.FillWeighedBodyPartsInternal(body, partHit, wbp, null);
                    internalHit = Rng.UWeightedFrom(wbp);
                }
            }

            #if DEBUG_ATTACK
            D.Warn("Dealing Damage: {0} -> {1} -> {2} -> {3}. Hit {4} / {5}. Force {6}",
                method.Type, method.Stats.DPS.Value, attacker, target, partHit, internalHit,
                    hitForce);
            #endif

            if (maxSevIncrease > 1 && !partHit.InstalledDef.IsBiological && Rng.UChance(0.5f)) {
                // 50% chance to do less severity for robotic parts
                maxSevIncrease -= 1;
            }

            // Reduce hit force on the way further
            hitForce = ApplyDamageToPart(s, attacker, target, method, partHit, hitForce, maxSevIncrease, out var w);
            var sev = w.Severity;
            if (internalHit != null && hitForce > 0f) {
                if (maxSevIncrease > 0) {
                    maxSevIncrease--;
                    ApplyDamageToPart(s, attacker, target, method, internalHit, hitForce, maxSevIncrease, out var w2);
                    sev = Severities.Bigger(sev, w2.Severity);
                }
            }

            if (sev >= Severity.Moderate || !target.Data.Body.IsAlive) {
                body.Bleed(0.25f);
            }

            return true;

            // TODO add pain for biologicals
            // - figure out the resistance to the given attack type
        }

        /// <summary>
        /// Creates or advances a condition for the damaged body part
        /// </summary>
        /// <param name="s"></param>
        /// <param name="target"></param>
        /// <param name="partHit"></param>
        /// <param name="hitForce"></param>
        /// <returns>reduced hitForce</returns>
        /// <exception cref="NotImplementedException"></exception>
        private static float ApplyDamageToPart(GameState s, IAttackable attacker,
                CombatUnit target,
                AttackMethod method, BodyPartSlot partHit, float hitForce,
                    byte maxSevIncrease, out Wound wound) {

            #if DEBUG_ATTACK
            D.Warn("Applying damage to body part: {0}", partHit);
            #endif


            // float includeFatalChance;
            // if (target.Data.EffectiveFaction.IsPlayer) {
            //     includeFatalChance = s.Difficulty.Level * 0.5f;
            // } else {
            //     includeFatalChance = 0.5f / s.Difficulty.Level;
            // }
            // var sev = Severities.For(hitForce, Rng.UChance(includeFatalChance));

            Severity hitSev;
            if (partHit.TryGetWound(method.Type, out var w)) {
                hitSev = Severities.IncreaseBy(w.Severity, maxSevIncrease);
            } else {
                hitSev = (Severity) maxSevIncrease;
            }
            var fatalHitChanceMul = method.FatalHitChanceMul;
            // A part the being cannot live without (body, head, heart) is harder to destroy
            // outright, so most fights end with someone down and rescuable rather than dead.
            // An eye or a limb keeps the weapon's full chance: a knife can still put out an eye.
            var destroyChanceMul = partHit.IsRequiredForBeingAlive
                ? Tunables.CombatVitalPartDestroyMul : 1f;
            // The worst this hit may leave the wound at. A capped hit passes its cap on, so the
            // wound's own escalation (Severities.Accumulate) cannot destroy the part anyway.
            var maxSev = Severity.Fatal;
            if (hitSev >= Severity.Irreparable) {
                if (fatalHitChanceMul <= 0f
                        || !Rng.UChance(math.min(1f, fatalHitChanceMul) * destroyChanceMul)) {
                    hitSev = Severity.Critical;
                    maxSev = Severity.Critical;
                }
            } else if (hitSev == Severity.Critical && fatalHitChanceMul > 1f) {
                // above 1 the multiplier is an extra chance to destroy: 1.2 = +20%
                if (Rng.UChance((fatalHitChanceMul - 1f) * destroyChanceMul)) {
                    hitSev++;
                }
            }

            if (target.Army != null && (target.Army.Flags & CombatFlags.Friendly) == CombatFlags.Friendly) {
                if (attacker is CombatUnit cu && cu.Army != null) {
                    if ((cu.Army.Flags & CombatFlags.Friendly) == CombatFlags.Friendly) {
                        // abort, already wounded enough
                        if (w.Severity >= Severity.Moderate) {
                            wound = w;
                            return 0f;
                        }
                        if (hitSev > Severity.Moderate) {
                            hitSev = Severity.Moderate;
                        }
                        maxSev = Severity.Moderate;
                    }
                }
            }

            if (maxSevIncrease == 0) {
                wound = w;
                return 0f;
            }

            bool wasAlive = target.Data.Body.IsAlive;

            if (partHit.TryAddWound(attacker, method.Type, hitSev, G.Ticks, hitForce, out wound,
                    maxSeverity: maxSev)) {
                var isAlive = target.Data.Body.IsAlive;
                // the wound as it ended up (escalated, and named Irreparable or Fatal for this part)
                var msg = T.InflictWoundLog_V5.F(
                    attacker.EntityName,
                    Severities.NameFor(wound.Severity, false),
                    method.TypeNameT,
                    target.Data.Mind.CombatLogName,
                    partHit.SlotName
                );
                if (wasAlive && !isAlive) {
                    target.Data.Mind.OnDeath(msg, G.Ticks);
                }
                s.Sys.Log.AddLine(Texts.ReplaceBoldWithAccented(msg));
                if (method.Type == DamageType.Sharp || method.Type == DamageType.Piercing) {
                    // Internal organs are smaller so they will get more damage if
                    // the hit goes through
                    return hitForce * Rng.URange(0.5f, 2.0f);
                } else {
                    // non-sharp / piercing damage will get reduced for the internal
                    // organs
                    return hitForce * Rng.URange(0.5f, 0.95f);
                }
            }
            return 0f;
        }

        // The shot left the gun: ranged actions show it flying to where it ended
        public virtual void OnShot(GameState s, AttackMethod method, CombatUnit unit,
                Vector2 impact) {}

        // Returns whether the target was hit: a shot on target can still be caught by someone
        // in the line of fire (StrayShot.TryCatchOnTheWay)
        public bool Execute(GameState s, AttackMethod method, CombatUnit unit, IAttackable target,
                bool hit, float chance, in ShotOdds odds) {
            if (target is CombatUnit cu) {
                cu.RefreshCover();
            } else {
                cu = null;
            }
            if (!method.IsMelee) {
                // the shot flies on to wherever it ends, and may hit someone or something else
                // (StrayShot)
                Vector2 impact;
                if (!hit) {
                    impact = StrayShot.Resolve(s, method, unit, unit.Data.EffectiveFaction,
                        unit.SightPosition, target, odds.Aim, odds.Cover, chance);
                } else if (StrayShot.TryCatchOnTheWay(s, method, unit,
                        unit.Data.EffectiveFaction, unit.SightPosition, target, out impact)) {
                    hit = false;
                } else {
                    impact = target.AttackablePosition;
                }
                OnShot(s, method, unit, impact);
            }
            if (!OnExecute(s, method, unit, target, hit, chance)) {
                return hit;
            }
            if (cu != null) {
                DealDamageTo(s, method, unit, cu);
            } else if (target is Tile t) {
                // a structure takes a shot's full damage too
                t.Health.TakeDamage(method.Damage, method.Type);
            }
            return hit;
        }
    }

}
