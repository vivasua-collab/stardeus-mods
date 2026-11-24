using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Systems.Combat;
using KL.Grid;

namespace Game.Systems.AI.Combat {
    // When the player hears about a hostile fight, and when a fight ends on its own. A fight
    // starts Quiet: enemies may hunt, the crew works on. The raid alert comes once an enemy
    // that can reach us advances, and is the cue for arming up. Combat in progress comes at the
    // first contact between our side and an enemy. A fight the enemies cannot carry on ends
    // quietly. CombatSys raises the stages here asked for, see CombatSystem.md.
    public sealed class CombatAlerts {
        private readonly CombatSys sys;
        public readonly CombatReach Reach;
        // Saved: where the hostile fight in progress stands for the player
        public HostileAlert Stage;
        // Saved: the last contact between our side and an enemy, or when the fight or its
        // newest army started
        public long LastContactTick;
        private bool contactPending;
        private int contactPos = Pos.Invalid;
        private bool raidCheckPending;
        private long strandedSinceTick;
        // The closest each advancing enemy came to the ship computer in this fight, the farthest
        // each leaving one got from it, and when one last did better. Not saved: a load starts a
        // fresh no-contact window instead.
        private readonly Dictionary<CombatUnit, float> closestToShip = new();
        private readonly Dictionary<CombatUnit, float> farthestFromShip = new();
        private long lastAdvanceTick;

        public CombatAlerts(CombatSys sys) {
            this.sys = sys;
            Reach = new CombatReach(sys);
        }

        public void Begin(long ticks) {
            Stage = HostileAlert.Quiet;
            contactPending = false;
            contactPos = Pos.Invalid;
            closestToShip.Clear();
            farthestFromShip.Clear();
            lastAdvanceTick = 0;
            OnArmyJoined(ticks);
        }

        // A new army may advance, and the no-contact clock counts from its arrival: a raid that
        // joins a long quiet fight must not be ended with it
        public void OnArmyJoined(long ticks) {
            LastContactTick = ticks;
            strandedSinceTick = 0;
            raidCheckPending = true;
        }

        public void End() {
            contactPending = false;
            contactPos = Pos.Invalid;
            raidCheckPending = false;
            strandedSinceTick = 0;
            closestToShip.Clear();
            farthestFromShip.Clear();
            lastAdvanceTick = 0;
        }

        public void Load(CombatSysData data, long ticks) {
            Stage = (HostileAlert) data.Alert;
            LastContactTick = data.LastContactTick > 0 ? data.LastContactTick : ticks;
            End();
            raidCheckPending = Stage == HostileAlert.Quiet;
            lastAdvanceTick = ticks;
        }

        public void Save(CombatSysData data) {
            data.Alert = (byte) Stage;
            data.LastContactTick = LastContactTick;
        }

        private static bool IsInHostileFight(CombatUnit unit) {
            var army = unit?.Army;
            return army != null && (army.Flags & CombatFlags.Hostile) == CombatFlags.Hostile;
        }

        // The attacked unit noticed an attack: every unit attack, hit or miss, a turret shot,
        // a stray shot that came its way. Turrets fight for the crew.
        public void OnAttackNoticed(CombatUnit target, IAttackable attacker) {
            if (!sys.IsHostileCombatInProgress || !IsInHostileFight(target)) { return; }
            var targetIsOurs = target.Army.Faction.IsPlayer;
            if (attacker is CombatUnit unit) {
                if (!IsInHostileFight(unit) || unit.Army == target.Army
                        || unit.Army.Faction.IsPlayer == targetIsOurs) {
                    return;
                }
                NoteContact(targetIsOurs ? unit.PosIdx : target.PosIdx);
            } else if (!targetIsOurs) {
                NoteContact(target.PosIdx);
            }
        }

        // An enemy hit a structure of the ship (a wall, a door, a turret, the ship computer):
        // the fight reached us, whether or not anyone saw it
        public void OnStructureAttacked(CombatUnit attacker, IAttackable target) {
            if (!sys.IsHostileCombatInProgress || !IsInHostileFight(attacker)
                    || attacker.Army.Faction.IsPlayer) {
                return;
            }
            if (!Reach.IsOnShip(target.AttackablePosIdx, G.Ticks)) { return; }
            NoteContact(attacker.PosIdx);
        }

        // One of ours took an enemy as its target: the enemy is in its reach, it knows
        public void OnTargetTaken(CombatUnit unit, CombatUnit target) {
            if (!sys.IsHostileCombatInProgress || !IsInHostileFight(unit)
                    || !IsInHostileFight(target)) {
                return;
            }
            if (!unit.Army.Faction.IsPlayer || target.Army.Faction.IsPlayer) { return; }
            NoteContact(target.PosIdx);
        }

        // One of ours runs: the crew flees only from an enemy in sight (FleePriorityResolver)
        public void OnCrewFled(CombatUnit unit) {
            if (!sys.IsHostileCombatInProgress || !IsInHostileFight(unit)
                    || !unit.Army.Faction.IsPlayer) {
                return;
            }
            NoteContact(unit.PosIdx);
        }

        private void NoteContact(int pos) {
            LastContactTick = G.Ticks;
            if (Stage != HostileAlert.InCombat) {
                contactPending = true;
                contactPos = pos;
            }
        }

        public bool TakeContact(out int pos) {
            pos = contactPos;
            if (!contactPending) { return false; }
            contactPending = false;
            contactPos = Pos.Invalid;
            return true;
        }

        // While nothing is shown: one tick after a fight or an army starts, when whoever
        // started it has set its units up, and every 10 ticks
        public bool TakeRaidCheck(long ticks) {
            if (!raidCheckPending && ticks % 10 != 3) { return false; }
            raidCheckPending = false;
            return true;
        }

        private static bool IsEnemyArmy(Army army) {
            return !army.Faction.IsPlayer && !army.IsStaging && !army.IsDefeated
                && (army.Flags & CombatFlags.Friendly) != CombatFlags.Friendly;
        }

        // An enemy on the hunt that can get to us. Unaware ones are animals that only fight
        // what comes to them; fleeing and leaving ones are on their way out. Asks for a
        // Reach.Refresh in the same tick first.
        private bool IsAdvancing(CombatUnit unit) {
            return unit.State == CombatUnitState.Aggressive && !unit.IsLeaving
                && !Reach.IsStranded(unit.Agent.Being);
        }

        public CombatUnit FirstAdvancingEnemy(List<Army> armies, long ticks) {
            Reach.Refresh(ticks);
            for (int i = 0; i < armies.Count; i++) {
                var army = armies[i];
                if (!IsEnemyArmy(army)) { continue; }
                for (int j = 0; j < army.Units.Count; j++) {
                    var unit = army.Units[j];
                    if (IsAdvancing(unit)) {
                        return unit;
                    }
                }
            }
            return null;
        }

        // Every 10 ticks while enemy armies stand. A fight whose every enemy is stranded ends
        // CombatStrandedEndMinutes after the last contact. Any fight ends after
        // CombatNoContactEndHours without contact while no enemy comes any closer, for what
        // the stranded test misses (an immobilized raider, a path that never works). Real
        // fights make contact far more often (shots, bashing doors and walls, the ship
        // computer), and a raid crossing a big map takes hours to make its first. A raider
        // still getting away keeps it going too: the end of its fight took its way out, a slow
        // hurt one stopped halfway to the edge and stayed on the map for good (STA-1753).
        public bool ShouldEndQuietly(List<Army> armies, long ticks, out string why) {
            why = null;
            Reach.Refresh(ticks);
            // | and not ||: both keep their records up to date
            if (AnyEnemyCameCloser(armies) | AnyLeaverGotAway(armies)) {
                lastAdvanceTick = ticks;
            }
            var noContactTicks = (long) (Tunables.CombatNoContactEndHours * Consts.TicksPerHour);
            if (ticks - Math.Max(LastContactTick, lastAdvanceTick) >= noContactTicks) {
                why = "no contact, and no enemy coming closer";
                return true;
            }
            if (!AreAllEnemiesStranded(armies)) {
                strandedSinceTick = 0;
                return false;
            }
            if (strandedSinceTick == 0) {
                strandedSinceTick = ticks;
            }
            var strandedTicks = (long) (Tunables.CombatStrandedEndMinutes * Consts.TicksPerMinute);
            if (ticks - Math.Max(strandedSinceTick, LastContactTick) < strandedTicks) {
                return false;
            }
            why = "every enemy is stranded";
            return true;
        }

        // An advancing enemy came at least a tile closer to the ship computer than it had been
        // in this fight. Each unit has its own record, so one stuck near the ship does not hide
        // a new raid's approach, and one wandering about runs out of new records.
        private bool AnyEnemyCameCloser(List<Army> armies) {
            var cameCloser = false;
            for (int i = 0; i < armies.Count; i++) {
                var army = armies[i];
                if (!IsEnemyArmy(army)) { continue; }
                for (int j = 0; j < army.Units.Count; j++) {
                    var unit = army.Units[j];
                    if (!IsAdvancing(unit)) { continue; }
                    var dist = Reach.DistanceToShip(unit.Agent.Being);
                    if (dist < 0f) { continue; }
                    if (!closestToShip.TryGetValue(unit, out var closest)) {
                        closestToShip[unit] = dist;
                    } else if (dist <= closest - 1f) {
                        closestToShip[unit] = dist;
                        cameCloser = true;
                    }
                }
            }
            return cameCloser;
        }

        // A leaving enemy got at least a tile farther from the ship computer than it had been.
        // Its own record again, so one that stops getting away runs out of new ones.
        private bool AnyLeaverGotAway(List<Army> armies) {
            var gotAway = false;
            for (int i = 0; i < armies.Count; i++) {
                var army = armies[i];
                if (!IsEnemyArmy(army)) { continue; }
                for (int j = 0; j < army.Units.Count; j++) {
                    var unit = army.Units[j];
                    if (!IsOnItsWayOut(unit)) { continue; }
                    var dist = Reach.DistanceToShip(unit.Agent.Being);
                    if (dist < 0f) { continue; }
                    if (!farthestFromShip.TryGetValue(unit, out var farthest)) {
                        farthestFromShip[unit] = dist;
                    } else if (dist >= farthest + 1f) {
                        farthestFromShip[unit] = dist;
                        gotAway = true;
                    }
                }
            }
            return gotAway;
        }

        // On its way off the map: one that came to after a raid, or one that flees with a way out
        private static bool IsOnItsWayOut(CombatUnit unit) {
            if (unit.State == CombatUnitState.Incapacitated) { return false; }
            return unit.IsLeaving || (unit.IsFleeing && EscapeRoute.Exists(unit));
        }

        private bool AreAllEnemiesStranded(List<Army> armies) {
            var standing = 0;
            for (int i = 0; i < armies.Count; i++) {
                var army = armies[i];
                if (!IsEnemyArmy(army)) { continue; }
                for (int j = 0; j < army.Units.Count; j++) {
                    var unit = army.Units[j];
                    if (unit.State == CombatUnitState.Incapacitated) { continue; }
                    standing++;
                    if (!Reach.IsStranded(unit.Agent.Being)) { return false; }
                }
            }
            return standing > 0;
        }
    }
}
