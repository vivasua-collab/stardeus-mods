#if !PRODUCTION_BUILD
// #define DEBUG_SOCIAL_COOLDOWN
#endif
using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Drama {
    public sealed class BeingCluster {
        public readonly int RegId;
        public BeingClusterFlags Flags;

        // WARNING: don't forget to add new collections to Clear()!
        public readonly List<BeingData> All = new();
        /// <summary>
        /// Not incapacitated, not in device, not asleep (able to fight)
        /// </summary>
        public readonly List<BeingData> Active = new();
        /// <summary>
        /// Alive awake non-incapacitated friendlies with Mind.Health != null
        /// </summary>
        public readonly List<BeingData> FriendlySociable = new();
        /// <summary>
        /// Subset of friendly sociable but only with idle beings
        /// </summary>
        public readonly List<BeingData> FriendlySociableIdle = new();
        public readonly List<BeingData> FriendlyAsleep = new();
        public readonly List<BeingData> Dead = new();
        // Beings doing something that is not quiet (HasNoise)
        public readonly List<BeingData> Noisy = new();
        /// <summary>
        /// For expedition clusters
        /// </summary>
        public Expedition Expedition;
        // WARNING: don't forget to add new collections to Clear()!

        public bool IsSociable => (Flags & BeingClusterFlags.HasSociableGroup) == BeingClusterFlags.HasSociableGroup;
        public bool IsSociableIdle => (Flags & BeingClusterFlags.HasSociableIdleGroup) == BeingClusterFlags.HasSociableIdleGroup;
        public bool DisturbsSleeping => (Flags & BeingClusterFlags.DisturbsSleeping) == BeingClusterFlags.DisturbsSleeping;
        public bool WantsCombat => (Flags & BeingClusterFlags.WantsCombat) == BeingClusterFlags.WantsCombat;
        public bool IsExpedition => (Flags & BeingClusterFlags.Expedition) == BeingClusterFlags.Expedition;

        public BeingCluster(int regId) {
            RegId = regId;
        }

        public void Clear() {
            Flags = BeingClusterFlags.None;
            All.Clear();
            Active.Clear();
            FriendlySociable.Clear();
            FriendlySociableIdle.Clear();
            FriendlyAsleep.Clear();
            Dead.Clear();
            Noisy.Clear();
        }

        public void Add(BeingData data,
                long maxLastTick,
                long sociableWindowTickFrom,
                int sociableMaxInteractions) {
            if (data.Being == null) {
                if (!IsExpedition) { return; }
            }
            All.Add(data);
            // ignore beings in devices
            if (data.HasStateFlag(BeingState.InDevice)
                    && data.Being?.AIAgent.ChatContext == null) {
                return;
            }
            // ignore carried beings
            if (data.Being?.Carrier != null) { return; }
            // noise won't be registered, but it's ok
            if (data.Being?.AIAgent.DirectControl.IsUnderDirectControl == true) { return; }
            if (data.HasStateFlag(BeingState.Dead)) {
                var isBio = data.Species.IsBiological;
                var isWild = data.Species.IsWild;
                if (isBio && !isWild) {
                    Flags |= BeingClusterFlags.HasDead;
                    Dead.Add(data);
                }
                return;
            }
            if (data.Body.IsIncapacitated) { return; }
            bool isAsleep = data.HasCondition(ConditionIdH.Sleeping);
            if (!isAsleep) {
                Active.Add(data);
            }

            Flags |= BeingClusterFlags.HasActive;
            if (data.Being?.AIAgent.CurrentAction?.IsQuiet == false) {
                Flags |= BeingClusterFlags.HasNoise;
                Noisy.Add(data);
            }
            if (!data.IsFriendly) {
                Flags |= BeingClusterFlags.HasEnemy;
                return;
            }
            // Guests (non-hostile faction visitors) cluster as friendlies: they take part
            // in colony social life and must not flag the cluster as having an enemy.

            Flags |= BeingClusterFlags.HasFriendly;
            if (data.Mind.Health == null) { return; }

            if (isAsleep) {
                Flags |= BeingClusterFlags.HasSleeping;
                FriendlyAsleep.Add(data);
            } else {
                if (sociableMaxInteractions > 0) {
                    long last = 0;
                    var interactions = data.Mind.Bonds?
                        .SocialInteractionsSince(sociableWindowTickFrom, out last);
                    if (last > 0 && last > maxLastTick) {
                        #if DEBUG_SOCIAL_COOLDOWN
                        D.Warn("Cooling down being with too fresh recent interaction: {0} ago-> {1}",
                            Units.TicksToTime(G.Ticks - last), data);
                        #endif
                        return;
                    }
                    if (interactions > sociableMaxInteractions) {
                        #if DEBUG_SOCIAL_COOLDOWN
                        D.Warn("Cooling down being with too many social interactions: {0} -> {1}",
                            interactions, data);
                        #endif
                        return;
                    }
                }

                if (data.Being?.AIAgent.CombatUnit.Army != null) {
                    return;
                }
                if (data.Being?.AIAgent.Goal?.IsNonInterruptable == true) {
                    return;
                }

                FriendlySociable.Add(data);
                if (data.Being?.AIAgent.IsIdle == true) {
                    FriendlySociableIdle.Add(data);
                }
            }
        }

        public void UpdateFlags() {
            if (FriendlySociable.Count > 1) {
                Flags |= BeingClusterFlags.HasSociableGroup;
            }
            if (FriendlySociableIdle.Count > 1) {
                Flags |= BeingClusterFlags.HasSociableIdleGroup;
            }
        }

        public void ApplyEffects(GameState s, long ticks) {
            if ((Flags & BeingClusterFlags.DisturbsSleeping)
                    == BeingClusterFlags.DisturbsSleeping) {
                DisturbSleeping(s, ticks);
            }
            if ((Flags & BeingClusterFlags.HasSociableGroup)
                    == BeingClusterFlags.HasSociableGroup) {
                UpdateSociableProximity(s);
            }
            if ((Flags & BeingClusterFlags.SeenDead)
                    == BeingClusterFlags.SeenDead) {
                SeeDeadBodies(s, ticks);
            }
            if ((Flags & BeingClusterFlags.WantsCombat)
                    == BeingClusterFlags.WantsCombat &&
                        !s.Sys.Combat.IsHostileCombatInProgress) {
                StartCombat(s, ticks);
            }
        }

        private void UpdateSociableProximity(GameState s) {
            // when crowds get too big, they don't get proximity bonus
            // because in a large group it's harder to create personal bonds.
            // also, performance will be better since this is O(n^2).
            if (FriendlySociable.Count > 5) { return; }

            var opQuery = s.Query.GetOpinionAbout;
            foreach (var bd1 in FriendlySociable) {
                foreach (var bd2 in FriendlySociable) {
                    // do not process same being pair twice and self
                    if (bd1.Id <= bd2.Id) { continue; }
                    if (bd1 == bd2) {
                        D.Err("Id is not less or equal, but bd1 == bd2 {0}", bd1);
                        continue;
                    }
                    var op1 = opQuery.Ask(bd1.Mind, bd2.Mind);
                    var op2 = opQuery.Ask(bd2.Mind, bd1.Mind);
                    if (op1 == op2) {
                        D.Err("Same opinion for {0} and {1}: {2}", bd1, bd2, op1);
                        continue;
                    }
                    bool isClose = IsExpedition;
                    if (!isClose) {
                        var dist = Vectors.SqrDistance(
                            bd1.Being.Position, bd2.Being.Position);
                        isClose = dist < 5f;
                    }
                    if (isClose) {
                        var prox1 = op1.TicksInProximity + DramaSys.ClusterUpdateTickRate;
                        var prox2 = op2.TicksInProximity + DramaSys.ClusterUpdateTickRate;
                        if (prox1 != prox2) {
                            D.Err("Proximity mismatch between beings: {0} and {1}: {2} != {3}",
                                bd1, bd2, prox1, prox2);
                            prox1 = Math.Max(prox1, prox2);
                            prox2 = prox1;
                        }
                        op1.TicksInProximity = prox1;
                        op2.TicksInProximity = prox2;
                        op1.MarkChanged();
                        op2.MarkChanged();
                        s.Sig.BeingProximityDurationChanged.Send(bd1, bd2, prox1);
                        // D.Warn("Proximity between {0} and {1} increased: {2} and {3}",
                        //     bd1.Name, bd2.Name, Units.TicksToTime(op1.TicksInProximity), Units.TicksToTime(op2.TicksInProximity));
                    }
                }
            }
        }

        private void DisturbSleeping(GameState s, long ticks) {
            foreach (var being in FriendlyAsleep) {
                // animals won't get disturbed
                if (being.Species.IsAnimal) { continue; }
                if (being.Mind.Health == null) { continue; }
                if (!HearsNoiseFromOthers(being)) { continue; }
                float disturbChance;
                if (being.Being.Graphics.IsHidden) {
                    // we are sleeping inside a pod, let's increase the chance to not
                    // hear any noises
                    disturbChance = 0.03f;
                } else {
                    disturbChance = 0.5f;
                }
                if (Rng.UChance(disturbChance)) {
                    var effect = MentalEffect.CreateWithIcon(
                        being.Being?.PosIdx ?? -1,
                        IconId.CSoundWarning,
                        ticks, Consts.TicksPer6Hours, T.SleepDisturbedByNoise, -3);
                    s.Sig.AddMentalEffectWithLog.Send(being, effect, 0f, false);
                    being.Mind.Health.Comp?.DisplayMoodBubble(effect);
                }
            }
        }
        private bool HearsNoiseFromOthers(BeingData sleeper) {
            for (int i = Noisy.Count - 1; i >= 0; i--) {
                var other = Noisy[i];
                if (other == sleeper || IsHousehold(sleeper, other)) { continue; }
                return true;
            }
            return false;
        }

        // A partner and the pets sleeping in the same room are the sounds of home: lovers
        // sharing a double bed with their pet kept waking each other up (STA-1908)
        // Public for tests
        public static bool IsHousehold(BeingData sleeper, BeingData other) {
            var bonds = sleeper.Mind?.Bonds;
            if (bonds == null || other.Mind == null) { return false; }
            if (bonds.Opinions.TryGetValue(other.Mind.Id, out var op)
                    && (op.Flags & HouseholdFlags) != BeingOpinionFlags.None) {
                return true;
            }
            // a partner's pet
            var petBonds = other.Mind.Bonds;
            if (petBonds == null || !other.Species.IsPet) { return false; }
            foreach (var petOp in petBonds.Opinions.Values) {
                if ((petOp.Flags & BeingOpinionFlags.Master) != BeingOpinionFlags.Master) {
                    continue;
                }
                if (bonds.Opinions.TryGetValue(petOp.TargetMindId, out var masterOp)
                        && (masterOp.Flags & PartnerFlags) != BeingOpinionFlags.None) {
                    return true;
                }
            }
            return false;
        }

        private const BeingOpinionFlags PartnerFlags =
            BeingOpinionFlags.Lover | BeingOpinionFlags.Spouse;
        // the sleeper's own pets carry Pet in its opinion of them (DramaSys)
        private const BeingOpinionFlags HouseholdFlags = PartnerFlags | BeingOpinionFlags.Pet;

        /// <summary>
        /// Combat interrupts the attacker's current goal (see AIGoal.Interrupt), so a candidate
        /// stuck on a non-interruptable goal (e.g. Insanity) can never be the attacker.
        /// </summary>
        private static bool CanBeCombatAttacker(Being being) {
            var goal = being.AIAgent.Goal;
            return goal == null || !goal.IsNonInterruptable || being.AIAgent.IsDisabled;
        }

        private void StartCombat(GameState s, long ticks) {
            // we have at least one enemy somewhere, let's see if it can be noticed
            // we can start combat
            Being firstPlayer = null;
            Being firstEnemy = null;
            // Counted so the failure below can distinguish two very different situations
            // that both leave firstPlayer null: nobody on our side was in the cluster at
            // all, or they were all mid-task. Only the first one means the cluster is wrong.
            var playersInCluster = 0;
            var playersBusy = 0;
            foreach (var data in Active) {
                if (data.IsPlayer) {
                    playersInCluster++;
                    if (CanBeCombatAttacker(data.Being)) {
                        firstPlayer ??= data.Being;
                    } else {
                        playersBusy++;
                    }
                } else if (!data.IsGuest) {
                    // Guests are neutral: never the enemy combatant, never a player-side fighter.
                    firstEnemy ??= data.Being;
                }

                if (firstPlayer != null && firstEnemy != null) {
                    if (StartCombatBetween(s, firstPlayer, firstEnemy)) {
                        return;
                    }
                }
            }
            // we may have a sleeping friendly, it's not included into the list of Active
            if (firstEnemy != null && firstPlayer == null
                    && ((Flags & BeingClusterFlags.HasSleeping) == BeingClusterFlags.HasSleeping)) {
                foreach (var being in All) {
                    if (being.IsPlayer && CanBeCombatAttacker(being.Being)) {
                        firstPlayer = being.Being;
                        if (StartCombatBetween(s, firstPlayer, firstEnemy)) {
                            return;
                        }
                    }
                }
            }
            #if !PRODUCTION_BUILD
            if (firstPlayer == null && playersInCluster > 0) {
                // Everyone on our side is busy with a non-interruptable goal. UpdateClusters
                // runs again in ClusterUpdateTickRate ticks and starts combat once one of
                // them is free, so this is a postponement rather than a failure.
                // D.Log, not D.Warn: a colonist busy next to an enemy would otherwise emit
                // one of these every ClusterUpdateTickRate ticks for the whole task.
                D.Log("Combat postponed in cluster {0}: all {1} friendly being(s) busy. Enemy: {2}",
                    this, playersBusy, firstEnemy);
            } else {
                D.Err("Failed to start combat in cluster: {0}. Friendly: {1} ({2} in cluster, {3} busy). Enemy: {4}",
                    this, firstPlayer, playersInCluster, playersBusy, firstEnemy);
            }
            #endif
        }

        private bool StartCombatBetween(GameState s, Being firstPlayer, Being firstEnemy) {
            if (s.Query.StartCombatBetween.Ask(
                    firstPlayer.AIAgent.CombatUnit,
                    firstEnemy.AIAgent.CombatUnit)) {
                return true;
            }
            D.ErrSoft("Failed to start combat between friendly / enemy in cluster: {0} vs {1} | {2}",
                firstPlayer, firstEnemy, this);
            return false;
        }

        private void SeeDeadBodies(GameState s, long ticks) {
            var deadCnt = Dead.Count;
            if (deadCnt < 1) {
                D.Err("Seeing dead bodies with 0 dead count! {0}", this);
                return;
            }
            foreach (var being in Active) {
                if (!being.Mind.Species.IsHumanoid) {
                    // animals / robots in human body will ignore
                    continue;
                }
                if (being.Mind.Health == null) {
                    D.Err("Trying to see dead bodies when mental health is null! {0}", being);
                    continue;
                }
                if (being.Mind.Health.IgnoreDeadBodiesUntil > ticks) {
                    continue;
                }
                var g = being.Being?.AIAgent.Goal;
                if (g != null && (g.Flags & AIGoalFlags.IgnoreDeadBodies)
                        == AIGoalFlags.IgnoreDeadBodies) {
                    continue;
                }
                // D.Warn("{0} saw {1} dead bodies", being, deadCnt);
                int effect;
                if (being.Mind.Traits.HasTrait(TraitIdH.Psychopath)) {
                    effect = deadCnt * 5;
                    if (effect > 15) {
                        effect = 15;
                    }
                } else {
                    // Refreshed every cluster update while the bodies lie there, so leaving
                    // them is what hurts, not a single glance
                    effect = deadCnt * -5;
                    if (effect < -15) {
                        effect = -15;
                    }
                }
                being.Mind.Health.AddEffect(s,
                    MentalEffect.CreateWithIcon(being.Being?.PosIdx ?? -1,
                    IconId.CSkull, ticks,
                    Consts.TicksPer2Hours, T.SawDeadBody, effect));
            }
        }

        public override string ToString() {
            return $"BeingCluster[RegId={RegId},Flags={Flags},All={All.Count},Idle:{FriendlySociableIdle.Count}]";
        }

    }
}
