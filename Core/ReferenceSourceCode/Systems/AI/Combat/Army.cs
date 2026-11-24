#if !PRODUCTION_BUILD
// #define DEBUG_ARMY
#endif
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using Game.Systems.Path;
using KL.Grid;
using Game.Systems.Combat;
using Game.Systems.Conditions;

namespace Game.Systems.AI.Combat {
    public sealed class Army {
        /// <summary>
        /// What the units of this army can be after; each unit gets one of these.
        /// </summary>
        public List<CombatObjective> Objectives;
        public FactionDef Faction;
        public List<CombatUnit> Units;
        public CombatFlags Flags;
        public bool IsOrganized;
        public bool IsDefeated;
        public bool IsDisbanded => Units.Count == 0;
        public int InitialUnitCount;
        // BeingData ids of every unit that ever fought in this army. A unit that leaves and
        // comes back (recovered from incapacitation) is not a reinforcement,
        // and counting it again made IsRouting fire too early.
        public readonly HashSet<uint> EnrolledIds = new();
        // This army's bit in PathSys.BodyMap and the bits of the armies whose bodies block
        // its walkers (HostileBodies). Both 0 outside a hostile fight and for fistfights.
        // Derived every tick, never saved.
        [SkipDumpInstance] public int BodyBit;
        [SkipDumpInstance] public int HostileBodyBits;

        public bool IsRouting() {
            if (InitialUnitCount < 2) { return false; }
            var lost = InitialUnitCount - Units.Count;
            return lost > InitialUnitCount * Tunables.CombatEnemyRouteThreshold;
        }

        // FIXME use home area
        public PosRect HomeArea = PosRect.Invalid;
        /// <summary>
        /// Tick when combat started for this army
        /// </summary>
        public long FormedTick;

        // A gathering raid (CombatObjectiveStage) attacks together at this tick, or as soon as
        // one of its units is harmed or the crew engages it; 0 when the army is not gathering
        public long StageUntilTick;
        public bool IsStaging => StageUntilTick > 0;

        // A gathering raid: it waits where it arrived, then every unit
        // goes in at once instead of strung out by speed. Set before the raid starts:
        // CombatSys.OnStartRaid keeps a gathering army out of hostile combat until it attacks.
        public Army WithStaging(long untilTick) {
            StageUntilTick = untilTick;
            Objectives.Clear();
            return WithObjective(CombatObjectiveIdH.Stage);
        }

        // Also when the army is not staging: a unit left on the staging objective (joined late,
        // an old save) must not wait forever
        public void EndStaging(GameState s, long ticks) {
            var wasStaging = IsStaging;
            StageUntilTick = 0;
            SwitchObjective(s, CombatObjectiveIdH.Raid, ticks);
            if (wasStaging) {
                // the raid's time windows (ship weapons) count from the attack, not the wait
                FormedTick = ticks;
                s.Sys.Log.AddLine(Texts.Red(T.CombatRaidersAttack));
                s.Sys.Combat.OnStagingEnded(this);
            }
        }

        private void SwitchObjective(GameState s, int objectiveIdH, long ticks) {
            var obj = Registries.CombatObjectives.FastGet(objectiveIdH);
            Objectives.Clear();
            Objectives.Add(obj);
            for (int i = 0; i < Units.Count; i++) {
                SetObjective(s, Units[i], obj, ticks);
            }
        }

        private Army() { }

        public static Army Create(FactionDef faction,
                CombatFlags flags = CombatFlags.Hostile) {
            var army = new Army();
            army.Objectives = new List<CombatObjective>();
            army.Units = new List<CombatUnit>();
            army.Faction = faction;
            army.FormedTick = G.Ticks;
            army.Flags = flags;
            return army;
        }

        public Army WithObjective(CombatObjective obj) {
            if (obj != null) {
                Objectives.Add(obj);
            }
            return this;
        }
        public Army WithObjective(int obj) {
            var o = Registries.CombatObjectives.FastGet(obj);
            if (o != null) {
                Objectives.Add(o);
            }
            return this;
        }

        public Army WithUnit(CombatUnit unit) {
            if (unit.State == CombatUnitState.Incapacitated) {
                if (unit.Data.Body.IsAbleToWork) {
                    D.Err("Fixing unit that is marked as incapacitated but able to work {0} | {1}",
                        unit, unit.Data);
                    unit.SetState(CombatUnitState.Unaware);
                } else {
                    D.Err("Trying to add an incapacitated combat unit to an army! {0} | {1}",
                        unit, unit.Data);
                    return this;
                }
            }
            if (IsOrganized) {
                D.Err("Trying to add a unit directly to an organized army! TODO");
            }
            AddUnit(unit);
            // A fistfight is no hostility: a guest in one keeps its door access (STA-1729)
            if ((Flags & CombatFlags.Friendly) == CombatFlags.None
                    && IsHostileTowards(FactionDef.Player)) {
                unit.Agent.Motion.SetPermission(NavPermission.Enemy, true);
            }
            return this;
        }
        public bool IsHostileTowards(FactionDef faction) {
            return faction != Faction;
        }

        public void Tick(GameState S, long ticks) {
            if (IsDefeated) {
                foreach (var u in Units) {
                    if (u.Agent.Being.IsAlive && u.State != CombatUnitState.Incapacitated) {
                        D.ErrSoft("Defeated army has a unit that is not defeated! Undefeating the army! {0} -> {1}",
                            this, u);
                        IsDefeated = false;
                        break;
                    }
                }
                if (IsDefeated) {
                    return;
                }
            }
            if (!IsOrganized) {
                // This happens for example when a crawler spawns mid combat
                #if UNITY_EDITOR
                D.Warn("Ticking an unorganized army! Organizing: {0}", this);
                #endif
                Organize();
            }
            // Update unit positions, etc
            for (int i = Units.Count - 1; i >= 0; i--) {
                CombatUnit u = Arrays.SafeGetFast(Units, i);
                if (u == null) { continue; }
                if (u.State == CombatUnitState.Incapacitated || !u.Agent.Being.IsActive) {
                    RemoveUnit(u, u.State == CombatUnitState.Incapacitated
                        ? CombatUnitState.Incapacitated
                        : CombatUnitState.Unaware);
                    if (Units.Count == 0) {
                        // army is gone
                        IsDefeated = true;
                        return;
                    }
                    continue;
                }
                if (!Belongs(u)) {
                    // Changed sides mid-fight (hacked, recruited, obey chip, loaded from an
                    // old save): an army only ever fights for its own faction, so the unit
                    // leaves with its goals and targets. EnsureInCombat puts it into the
                    // army of its new faction.
                    RemoveUnit(u, CombatUnitState.Unaware, keepFaction: true);
                    if (Units.Count == 0) {
                        IsDefeated = true;
                        return;
                    }
                    continue;
                }
                u.Tick(S, ticks);
            }
            // change what units want to do
            for (int i = Units.Count - 1; i >= 0; i--) {
                CombatUnit u = Arrays.SafeGetFast(Units, i);
                if (u == null) { continue; }
                if (u.Objective == null) {
                    if (Arrays.IsEmpty(Objectives)) {
                        D.Err("Objectives not set for army. Creating defaults: {0}", this);
                        CreateDefaultObjectives();
                    }
                    SetObjective(S, u, Rng.UFrom(Objectives), ticks);
                }
                TickObjective(S, u, ticks);
                if (IsDisbanded) {
                    // an objective disbanded the whole army mid-tick (e.g. friendly
                    // fight stalemate breakup) - Units are already cleared
                    return;
                }
            }
        }

        private static void SetObjective(GameState s, CombatUnit u, CombatObjective obj,
                long ticks) {
            if (u.Objective != null) {
                u.Objective.OnDeactivate(s, u, ticks);
                u.ClearGoals();
            }
            u.Objective = obj;
            u.TicksInObjective = 0;
            obj?.OnActivate(s, u, ticks);
        }

        private static void TickObjective(GameState s, CombatUnit u, long ticks) {
            if (u.State != CombatUnitState.Incapacitated && u.LastTick != 0
                    && u.Goals.Count == 0) {
                // a unit loaded without its combat goals, or one whose goals were cleared
                u.GoalsChanged = true;
            }
            if (u.GoalsChanged) {
                u.GoalsChanged = false;
                u.Objective.OnUnitChanged(u, ticks);
            }
            u.Objective.Tick(s, u, ticks);
            u.TicksInObjective++;
        }

        // The unit leaves the fight with nothing of it: goals, targets, the objective.
        // keepFaction is true when the unit leaves because its faction changed - resetting
        // AntiPlayerTemp back to Player would undo that very change. A restrained being goes
        // back to the faction it had (Mind.ReturnFaction), or to Player when none was kept.
        public void RemoveUnit(CombatUnit unit, CombatUnitState newState,
                bool keepFaction = false) {
            if (unit.Army != this) {
                D.Err("Trying to remove a unit from a wrong army! {0} : {1}", unit, this);
                return;
            }
            if (unit.Agent.Goal?.Type == AIGoalType.Combat) {
                // prevent combat goal from remaining active after combat
                unit.Agent.DropGoal();
            }
            unit.ClearGoals();
            unit.SetState(newState);
            // Every way out of a fistfight comes through here (Disband, a device, a faction
            // change), and only Disband used to take it away (STA-1750)
            if (unit.Data?.HasCondition(ConditionIdH.Preoccupied) == true) {
                unit.Data.RemoveCondition(unit.Agent.S, ConditionIdH.Preoccupied);
            }
            unit.SetAttackTarget(null, true);
            // a turret or ship computer picked by the old objective
            unit.Attackable = null;
            unit.Objective = null;
            unit.GoalsChanged = false;
            // Out of an army nothing ticks the unit. Its next fight starts the count afresh,
            // or the first army to tick there takes the gap since this one for a stall.
            unit.LastTick = 0;
            unit.Agent.Motion.CombatSlowdown = 0f;
            unit.SuppressionLevel = 0f;
            unit.ResetStunSuppression();
            unit.ResetNoAttackStall();
            unit.ResetRoutFleeRoll();
            unit.Comp?.HideSuppressionThoughtBubble();
            var mind = unit.Agent.BeingData.Mind;
            if (!keepFaction && mind.Faction.IdH == FactionIdH.AntiPlayerTemp) {
                var returnTo = mind.ReturnFaction ?? FactionDef.Player;
                mind.ReturnFaction = null;
                mind.SetFaction(returnTo);
            }
            // Takes back the Enemy bit WithUnit put on: the being's own faction and role decide
            // its door access again. A guest kept it after a fight and was shut in (STA-1729).
            unit.Agent.Motion.UpdatePermissions();
            if (Units.Remove(unit)) {
                #if DEBUG_ARMY
                D.Warn("Removed unit from army: {0}->{1}", this, unit);
                #endif
            } else {
                D.Err("Failed to remove unit from army: {0}->{1}", this, unit);
            }
            unit.Army = null;
        }

        // An army fights for one faction. The exception is a guest a predator went for:
        // it shelters with the crew without changing faction (CombatSys.ShelterWithCrew),
        // because an army of its own faction would be everyone's enemy - the crew's too.
        private bool Belongs(CombatUnit unit) {
            var data = unit.Agent.BeingData;
            return data.EffectiveFaction == Faction
                || (Faction.IsPlayer && data.IsProtectedGuest);
        }

        public bool HasUnitOfOwnFaction() {
            for (int i = 0; i < Units.Count; i++) {
                var u = Units[i];
                if (u == null || u.State == CombatUnitState.Incapacitated) { continue; }
                if (u.Agent.BeingData.EffectiveFaction == Faction) {
                    return true;
                }
            }
            return false;
        }

        public void AddUnit(CombatUnit unit) {
            #if DEBUG_ARMY
            D.Warn("Adding unit to army: {0}->{1}", this, unit);
            #endif
            if (IsDefeated && unit.State != CombatUnitState.Incapacitated) {
                D.Err("Adding a non-defeated unit to a defeated army! {0} -> {1}", this, unit);
                IsDefeated = false;
            }
            if (Units.Contains(unit)) {
                D.Err("Trying to add duplicate unit to the army! {0} / {1}", this, unit);
                return;
            }
            if (!Belongs(unit)) {
                D.Err("Trying to add a unit to the army of a wrong effective faction! {0} => ({2}) {1} | {3}",
                    this, unit, unit.Agent.BeingData.EffectiveFaction,
                    unit.Data.StatsBeing.AIAlignment);
                return;
            }
            Units.Add(unit);
            if (unit.State == CombatUnitState.Aware) {
                // left its last army Aware (restrained, a defecting guest); nothing moves a unit
                // out of Aware, so it starts over and ValidateConfig takes it from there
                unit.SetState(CombatUnitState.Unaware);
            }
            // Organize counts the units it starts with; after that only new recruits count
            if (EnrolledIds.Add(unit.Data.Id) && IsOrganized) {
                InitialUnitCount++;
            }
            unit.OnAddToArmy(this);
        }

        public void Absorb(Army other) {
            #if DEBUG_ARMY
            D.Warn("Army absorbing another {0} <= {1}", this, other);
            #endif
            if (Flags != other.Flags) {
                D.Err("Absorbing army with different flags! {0} and {1}", Flags, other.Flags);
            }
            IsDefeated = false;
            // AddUnit raises InitialUnitCount for genuine reinforcements, so IsRouting() keeps
            // the right fraction of losses after an absorbed follow-up wave. Units keep the
            // objective they had; those without one get one of ours on the next tick.
            for (int i = other.Units.Count - 1; i >= 0; i--) {
                CombatUnit u = other.Units[i];
                u.Army = null;
                AddUnit(u);
            }
            other.Units.Clear();
        }

        public void Organize() {
            if (IsOrganized) {
                D.Err("Trying to organize an already organized army! {0}", this);
                return;
            }
            #if DEBUG_ARMY
            D.Warn("Organizing army: {0}", this);
            #endif
            if (Objectives.Count == 0) {
                CreateDefaultObjectives();
            }
            IsOrganized = true;
            // the units this army starts with are the baseline for IsRouting
            EnrolledIds.Clear();
            foreach (var u in Units) {
                EnrolledIds.Add(u.Data.Id);
            }
            InitialUnitCount = EnrolledIds.Count;

            foreach (var u in Units) {
                PrepareForFight(u);
            }

            if ((Flags & CombatFlags.Friendly) == CombatFlags.Friendly) {
                // friendly fight will make units preoccupied so player won't
                // be able to control and separate them
                foreach (var u in Units) {
                    if (!u.Data.HasCondition(ConditionIdH.Preoccupied)) {
                        ConditionState.CreateFor(
                            u.Agent.S, ConditionIdH.Preoccupied,
                            u.Data, Severity.Irrelevant, true);
                    }
                }
            }
        }

        // The crew keeps its work: a fight may start far away and unseen. Free Will sets every
        // unarmed colonist to flee, and that used to drop its task at every fight's start; the
        // flee goal takes a unit off its task once an enemy is near (FleePriorityResolver).
        private static void PrepareForFight(CombatUnit u) {
            u.SetState(CombatUnitState.Unaware);
            u.ValidateConfig();
        }

        private void CreateDefaultObjectives() {
            if ((Flags & CombatFlags.Friendly) == CombatFlags.Friendly) {
                WithObjective(CombatObjectiveIdH.FistFight);
            } else {
                WithObjective(Faction.ArmyObjective);
            }
        }

        public override string ToString() {
            return $"Army[{GetHashCode()}|{Flags}|{Faction}|Units:{Units?.Count}|Defeated:{IsDefeated}]";
        }


        public void Disband() {
            IsOrganized = false;
            for (int i = Units.Count - 1; i >= 0; i--) {
                CombatUnit u = Units[i];
                u.SetAttackTarget(null, false);
                RemoveUnit(u, CombatUnitState.Unaware);
            }
            Units.Clear();
        }
    }
}
