#if UNITY_EDITOR
// #define DEBUG_ATTACK
#endif
using Game.Constants;
using Game.Data;
using Game.Systems.Stats;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using KL.Randomness;
using Priority_Queue;
using Unity.Mathematics;
using UnityEngine;
using Game.Systems.Combat;

namespace Game.Systems.AI.Combat {
    public sealed class AttackMethod : FastPriorityQueueNode {
        public StatsAttack Stats;
        public Curve AccuracyCurve;
        public float MaxAccuracyDist;
        // The farthest distance at which the accuracy curve still gives at least half its peak,
        // the threshold the AI treats as an acceptable shot. A drafted shooter ordered to attack
        // walks up to about here, not to MaxAccuracyDist, which is close to melee for some guns.
        public float EffectiveRangeMax;
        // Where a shooter wants to stand: 80% of its reach (CastPosition). Not the accuracy peak: walking to it put pistols and SMGs 2-3 tiles from
        // their targets, where the punch takes over. Getting the most out of a gun's accuracy is
        // the player's call, made with a drafted unit.
        public float PreferredDist => Stats.RangeMax.Value * 0.8f;
        public bool IsMelee;
        public DamageType Type;
        private string typeNameT;
        public string TypeNameT => typeNameT ??= DamageTypes.NameFor(Type);
        public IAttackMethodProvider Provider;
        public string Id;
        // How many ticks do we have to wait until full charge
        public int FullChargeTicks;
        // how many ticks do we wait for full cooldown
        public int CooldownTicks;
        public int EffectiveFullChargeTicks =>
            (int)(FullChargeTicks * Tunables.CombatChargeMultiplier);
        public int EffectiveCooldownTicks =>
            (int)(CooldownTicks * Tunables.CombatCooldownMultiplier);
        public int ChargeTicks;
        // by how many levels can wound severity increase at most when this attack hits a body part
        public byte MaxSeverityIncrease;
        public int CooldownTicksLeft;
        public float Damage => Stats.DPS.Value * (CooldownTicks + FullChargeTicks);
        public AttackAction Action;
        public string ChargeVFX;
        public string AttackVFX;
        public string[] AttackSounds;
        public float FatalHitChanceMul;
        public float InternalHitChanceMul;
        // Sharp attacks deal no internal organ damage by design (see DamageType.cs) - this
        // opts a single Sharp attack method into the same internal-hit formula Blunt uses.
        public bool DeepCut;
        public float RoboticDamageMul = 1f;

        public string ChargeSFX;
        public string AttackSFX;
        public string NameT;
        public Entity Entity => null;
        public float Value => Equations.AttackMethodValue(
            Damage, Stats.AccuracyMax.Value, MaxAccuracyDist);
        /// <summary>
        /// Chance to hit an internal organ. It will be further
        /// Multiplied by SubpartHitChance of the external body part, so
        /// 1f does not mean 100%
        /// </summary>
        /// <param name="hitForce">how strong was the hit? relative to armor, etc</param>
        /// <returns></returns>
        public float SubpartHitChance(float hitForce) {
            hitForce *= InternalHitChanceMul;
            switch (Type) {
                case DamageType.Blunt: return hitForce * 0.5f;
                // Sharp deals no internal organ damage by design, unless the attack method
                // opts in via DeepCut (e.g. a vibrating blade cutting deep enough to reach organs)
                case DamageType.Sharp: return DeepCut ? hitForce * 0.5f : 0f;
                case DamageType.Piercing: return Maths.Clamp(hitForce * 2f, 0f, 1f);
                case DamageType.Energy: return hitForce * 0.75f;
                default: {
                    D.Err("Unhandled attack type: {0}", Type);
                    return 0f;
                }
            }
        }

        /// <returns>false is method is ineffective against target</returns>
        public bool TryPriorityFor(CombatUnit target, float distToTarget,
                out float priority) {
            float hitChance;
            if (AccuracyCurve == null) {
                hitChance = RankingHitChance;
            } else {
                if (!IsWithinAttackRange(distToTarget, out hitChance, out _)) {
                    priority = 0f;
                    return false;
                }
            }
            priority = hitChance * hitChance * Stats.DPS.Value;
            // TODO check attack possibilities, etc
            return true;
        }

        public bool TryPriorityFor(CombatUnit target, out float priority) {
            var hc = RankingHitChance;
            priority = hc * hc * Stats.DPS.Value;
            return true;
        }

        // A melee hit lands by the attacker's MeleeAccuracy alone (HitChance), so every melee
        // attack ranks at the same reference chance and quality counts once, through DPS. Its
        // AccuracyMax still follows the quality curve, and squared here it made a well made
        // fist outrank a poorly made weapon: a colonist held a Stun Baton and punched.
        private float RankingHitChance => IsMelee
            ? Tunables.CombatMeleeReferenceHitChance
            : Stats.AccuracyMax.Value;

        private AttackMethod(IAttackMethodProvider prov) {
            Provider = prov;
            Stats = new StatsAttack();
            Stats.FinalizeStats();
        }

        public static AttackMethod CreateFor(
            IAttackMethodProvider provider, AttackMethodData data) {
            var meth = new AttackMethod(provider);
            data.ApplyTo(meth, provider.Quality);
            return meth;
        }

        public override string ToString() {
            return $"AttackMethod[{Id}->{Provider}]";
        }

        public string GetName() {
            return Id;
        }

        /// <param name="unit"></param>
        /// <param name="accuracyPercent"></param>
        /// <param name="suggestedDistChange">suggested distance change to improve accuracy, positive number if we need to get further away, negative if we need to get closer</param>
        /// <returns></returns>
        public bool IsWithinAttackRange(float distToTarget,
                out float accuracyPercent,
                out float suggestedDistChange) {
            var min = Stats.RangeMin.Value;
            var max = Stats.RangeMax.Value;
            suggestedDistChange = MaxAccuracyDist - distToTarget;
            if (distToTarget > max || distToTarget < min) {
                accuracyPercent = 0f;
                return false;
            }
            if (AccuracyCurve != null) {
                var accDist = Maths.InverseLerpUnsafe(min, max, distToTarget);
                accuracyPercent = AccuracyCurve.Evaluate(accDist);
            } else {
                // this will be multiplied by accuracy max where appropriate!
                accuracyPercent = 1f;
            }
            // D.Warn("Accuracy: {0} at dist {1} for {2}",
            //     accuracyPercent * Stats.AccuracyMax.Value, distToTarget, Id);
            return true;
        }

        // A method is used at most once per tick. A unit attacking its target while one of its
        // actions (bashing a door, attacking a turret or the ship computer) attacks with the
        // same method used to charge, cool down and train twice as fast.
        private long lastTryTick = -1;

        // The cooldown part of TryAttack, for a tick in which the unit may not attack
        public void TickCooldown(long ticks) {
            if (ticks == lastTryTick) { return; }
            lastTryTick = ticks;
            if (CooldownTicksLeft > 0) {
                CooldownTicksLeft--;
            }
        }

        public bool TryAttack(GameState s, CombatUnit attacker, IAttackable target,
                float distToTarget, long ticks, float accuracyBonus,
                out bool mustFaceTarget,
                out bool attackPerformed) {
            attackPerformed = false;
            if (ticks == lastTryTick) {
                mustFaceTarget = false;
                return false;
            }
            lastTryTick = ticks;
            if (CooldownTicksLeft > 0) {
                CooldownTicksLeft--;
                Action.OnCooldown(s, this, attacker);
                mustFaceTarget = false;
                return false;
            }
            if (target == null) {
                ChargeTicks = 0;
                mustFaceTarget = false;
                return false;
            }
            if (IsWithinAttackRange(distToTarget, out float accPercent, out var distDiff)) {
                if (!attacker.Data.Mind.Jobs.TryJob(IsMelee ?
                        JobTypeIdH.Melee : JobTypeIdH.Shooting, out var skill)) {
                    D.Err("No job skill for attack method! {0} | {1}", this, attacker);
                    mustFaceTarget = true;
                    return false;
                }

                if (ChargeTicks < EffectiveFullChargeTicks) {
                    Action.OnCharge(s, this, attacker, target);
                    var chargeMul = Stats.MoveSpeedMul.Value;
                    // 0 means "no multiplier" (see AttackMethodData.MoveSpeedMul), so it must not
                    // win a min() against a real penalty.
                    if (chargeMul > 0f) {
                        var current = attacker.Agent.Motion.CombatSlowdown;
                        // Combine with any suppression-driven slowdown already set this tick
                        // (CombatUnit.AttackIfPossible) - the more severe penalty wins.
                        attacker.Agent.Motion.CombatSlowdown = current > 0f
                            ? math.min(current, chargeMul)
                            : chargeMul;
                    }
                    ChargeTicks++;
                    mustFaceTarget = true;
                    return false;
                }
                ChargeTicks = 0;
                // attack!
                attackPerformed = true;
                // TODO armor checks, target stat checks, etc
                // even a hopeless shot is noticed: the miss bonus lets a poor shooter land hits
                // in the end, and its target used to sit through them unaware
                if (target is CombatUnit noticed) {
                    noticed.NoticeAttack(attacker);
                } else {
                    // a wall, a door, a turret or the ship computer
                    s.Sys.Combat.Alerts.OnStructureAttacked(attacker, target);
                }
                ShotOdds odds;
                if (target is CombatUnit cu) {
                    odds = HitChance(attacker, cu.Position, cu.PosIdx,
                        cu.Agent.Motion.VelocityPercent, accPercent, cu.Data);
                    // hit or miss: XP for the whole cycle of the attack, so a
                    // fast and a slow weapon train alike per hour of fighting. Only a target
                    // that can fight back teaches anything; doors, turrets, the ship computer
                    // and downed beings do not
                    if (CanDodge(cu.Data)) {
                        var xpPerTick = IsMelee
                            ? Tunables.CombatMeleeXpPerCycleTick
                            : Tunables.CombatShootingXpPerCycleTick;
                        skill.TrainSkill(xpPerTick * (EffectiveFullChargeTicks
                            + EffectiveCooldownTicks), false);
                    }
                } else {
                    // a structure neither moves nor takes cover, so no cover shields it
                    odds = HitChance(attacker, target.AttackablePosition, -1, 0f,
                        accPercent, null);
                }
                var acc = odds.Total;

                // the miss bonus scales the chance rather than adding to it, so cover and range
                // still matter after a few misses
                acc *= 1f + accuracyBonus;

                var hit = attacker.Rng.Chance(acc);
                #if DEBUG_ATTACK
                D.Warn("TryAttack: (acc: {4}), Hit: {1}, Attacker: {2}. Target: {3} | {0}",
                    this, hit, attacker, target, acc);
                #endif
                // a hit caught by someone in the line of fire counts as a near miss below
                hit = Action.Execute(s, this, attacker, target, hit, acc, odds);
                if (target is CombatUnit suppTarget) {
                    float suppAmt;
                    if (IsMelee) {
                        // Melee only suppresses on a landed hit, by a smaller
                        // amount than ranged fire. A whiffed melee swing doesn't
                        // pin anyone, but a dogpile of connecting hits does.
                        suppAmt = hit ? Tunables.CombatSuppressionPerMeleeHit : 0f;
                    } else {
                        suppAmt = hit
                            ? Tunables.CombatSuppressionPerHit
                            : Tunables.CombatSuppressionPerNearMiss;
                    }
                    if (suppAmt > 0f) {
                        suppTarget.AddSuppression(suppAmt);
                    }
                    if (hit) {
                        TryStun(suppTarget, attacker.Rng);
                    }
                }
                CooldownTicksLeft = math.max(1, EffectiveCooldownTicks + attacker.Rng.Range(-1, 2));
                #if DEBUG_ATTACK
                D.Err("Hit:{3} (acc: {2}): {0} -> {1}", attacker, target, acc, hit);
                #endif
                mustFaceTarget = true;
                return hit;
            } else {
                // #if DEBUG_ATTACK
                // D.Err("Out of range: {0} -> {1} (diff: {2})", attacker, target, distDiff);
                // #endif
                ChargeTicks = 0;
                mustFaceTarget = false;
            }
            return false;
        }

        // Rolled on a landed hit, by unit attacks and turret shots alike.
        // CombatStunSuppressionAmount <= 0 is the kill switch - skips the roll entirely rather
        // than rolling for a zero-strength effect.
        public void TryStun(CombatUnit target, Rng rng) {
            if (Tunables.CombatStunSuppressionAmount <= 0f) { return; }
            var stunChance = target.Data.Species.IsBiological
                ? Stats.StunChanceBio.Value
                : Stats.StunChanceRobo.Value;
            if (stunChance > 0f && rng.Chance(stunChance)) {
                target.AddStunSuppression(Tunables.CombatStunSuppressionAmount,
                    Tunables.CombatStunSuppressionExtraMinutes);
            }
        }

        // The chance one attack hits, before the miss streak bonus. TryAttack rolls against it and
        // the shot tooltip shows it, so what the player reads is what the dice use. Pure: it
        // trains nothing, so the tooltip may ask any time.
        // targetPosIdx: where the target takes cover, -1 for a target that takes none
        public ShotOdds HitChance(CombatUnit attacker, Vector2 targetPos, int targetPosIdx,
                float targetVelocity, float accPercent, BeingData target) {
            // melee weapons have no accuracy: the attacker's MeleeAccuracy is the whole hit
            // chance (their AccuracyMax is only a reference for weapon choice)
            var weapon = IsMelee ? 1f : accPercent * Stats.AccuracyMax.Value;
            // D.Warn("Base accuracy {0} + {1} (lvl: {2}, loves? {3})", acc, accBonus, skill.Level, skill.Loves);
            var acc = weapon;
            // the attacker's skill, eyes and hands (ShootingAccuracy / MeleeAccuracy: the skill
            // as a stat source, Sight, Dexterity, light)
            var stats = attacker.Data.StatsBeing;
            float beingMul = IsMelee ? stats.MeleeAccuracy.Value
                : ShootingMulAt(stats.ShootingAccuracy.Value,
                    Vectors.Distance(attacker.Position, targetPos));
            // a hostile fighting several of the crew in the first raids aims worse
            beingMul *= OutnumberedOdds.AttackerMul(attacker, target);
            acc = math.min(1f, acc * beingMul);
            // the shooter's whole aim at this distance, before the situation (cover, movement)
            float aim = acc;
            float dodgeMul = 1f;
            if (IsMelee && CanDodge(target)) {
                dodgeMul = 1f - math.clamp(target.StatsBeing.MeleeDodge.Value, 0f,
                    Tunables.CombatMaxMeleeDodge);
                acc *= dodgeMul;
            }
            float coverMul = 1f;
            float movingMul = 1f;
            // melee attacks happen from too close, we won't apply cover for them
            if (acc > 0.05f) {
                if (!IsMelee) {
                    coverMul = CoverMultiplier(attacker, targetPosIdx);
                    acc *= coverMul;
                }
                if (targetVelocity > 0.15f) {
                    movingMul = math.lerp(1f, 0.25f, targetVelocity);
                    acc *= movingMul;
                    #if DEBUG_ATTACK
                    D.Warn("Accuracy after target velocity {0} => {1}", targetVelocity, acc);
                    #endif
                }
            }
            float leaderMul = 1f;
            if (attacker.IsCommanded) {
                leaderMul = Tunables.CombatCommandAccuracyMul;
                acc = math.min(1f, acc * leaderMul);
            }
            return new ShotOdds(weapon, beingMul, aim, dodgeMul, coverMul,
                movingMul, leaderMul, acc);
        }

        // A shooter's accuracy works per tile of range, so skill, Sight and hands matter little
        // point blank and a lot far away. ShootingAccuracy is that factor at the reference
        // distance (12 tiles), so a shot raises it to distance / 12: a skill-0 shooter hits 62%
        // as often as a skill-5 one at 12 tiles, 79% at 6 and 49% at 18.
        public static float ShootingMulAt(float shootingAccuracy, float distance) {
            if (shootingAccuracy <= 0f) { return 0f; }
            return math.pow(shootingAccuracy,
                distance / Tunables.CombatSkillReferenceDistance);
        }

        // Only a being that is free to move can step out of the way (or teach the attacker
        // anything, see TryAttack)
        private static bool CanDodge(BeingData target) {
            if (target == null || !target.HasStateFlag(BeingState.Alive)
                    || target.Body.IsIncapacitated) {
                return false;
            }
            if (target.HasStateFlag(BeingState.InDevice)) { return false; }
            var b = target.Being;
            return b != null && !b.Motion.IsImmobilized;
        }

        // The cover around the target against this shooter, by angle and distance (CoverUtils).
        // Capped: combined cover from several tiles must never make a unit that can still
        // shoot back impossible to hit
        private static float CoverMultiplier(CombatUnit attacker, int targetPosIdx) {
            if (targetPosIdx < 0) { return 1f; }
            var block = CoverUtils.BlockChance(attacker.Agent.S, targetPosIdx, attacker.PosIdx);
            if (block <= 0f) { return 1f; }
            #if DEBUG_ATTACK
                D.Warn("Cover blocks {0}. Attacker pos: {1}. Target pos: {2}",
                    block, attacker.PosIdx, targetPosIdx);
            #endif
            return 1f - math.min(block, Tunables.CombatMaxCoverReduction);
        }

    }
}
