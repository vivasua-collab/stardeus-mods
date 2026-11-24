using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Story.Events;
using Game.Systems.AI.Combat;
using Game.Systems.Ships;
using Game.Systems.Story;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipCombatAI {
        private readonly Dictionary<ShipDevice, float> weightedTargets = new();
        private readonly Dictionary<NPCShip, float> weightedEnemies = new();
        /// <param name="opponents">ships on the other side of this fight</param>
        /// <param name="isPlayerSide">true for the player and its NPC allies</param>
        public void Tick(GameState s, NPCShip ship, List<NPCShip> opponents,
                long ticks, long combatStartTick, bool isPlayerSide) {
            var cs = ship.CombatState;
            cs.WeaponsShotThisTick = 0;

            var isPlayer = ship.IsPlayer;

            var every15min = ticks % 15 == 0;

            if (every15min) {
                if (isPlayer) {
                    var playerSide = s.Query.GetAllyShips.Ask();
                    for (int i = opponents.Count - 1; i >= 0; i--) {
                        NPCShip e = opponents[i];
                        if (e.CombatState.IsNPCDefeated) {
                            s.Query.EndShipCombatWith.Ask(e, ShipStateFlags.Derelict);
                            break;
                        } else {
                            if (!e.CombatState.IsNegotiating) {
                                if (NPCShipBehavior_Sector_Combat.CanOfferSurrenderToPlayer(e)) {
                                    if (e.CombatState.ConsiderSurrender(playerSide)) {
                                        ShipHailAction_Surrender.HailPlayer(e);
                                    }
                                }
                            }
                        }
                    }
                } else if (!isPlayerSide) {
                    if (CheckPlayerSideDefeated(s, ship, ticks, combatStartTick, cs, opponents)) {
                        return;
                    } else {
                        ConsiderSendingBreachCapsule(s, ship, ticks, combatStartTick);
                    }
                }
            }
            ResolveFocus(cs, opponents, FocusResolveReason.Tick);
            var focusedEnemy = cs.FocusedEnemy;

            // No more auto flux venting for the player
            var autoVentFlux = !isPlayer; // || cs.FluxSettings.AutoVentFlux;
            if (every15min && autoVentFlux) {
                if (!isPlayer && ticks > cs.NextNPCFluxBlunderAt) {
                    D.Warn("Switching blunder strat");
                    ShipCombatFluxStrategyDef.ChooseFor(ship, focusedEnemy,
                        !cs.StrategyFlux.IsBlunder);
                    cs.SetNextBlunder();
                }
                AutoVentFlux(s, ship);
            }
            // battle station salvo firing (player only)
            if (isPlayer) {
                TickBattleStations(s, cs, focusedEnemy);
            }

            // target weapons
            bool hasWeaponThatCanShoot = false;
            bool hasNonFluxBlockedWeaponThatCanShoot = false;
            foreach (var w in cs.Weapons) {
                TickWeapon(s, cs, isPlayer, focusedEnemy, w);
                if (autoVentFlux && w.CanShoot) {
                    hasWeaponThatCanShoot = true;
                    if (!w.SkippedShotDueToFlux) {
                        hasNonFluxBlockedWeaponThatCanShoot = true;
                    }
                }
            }
            if (autoVentFlux && hasWeaponThatCanShoot
                    && !hasNonFluxBlockedWeaponThatCanShoot) {
                // force flux vent because no weapon can shoot
                // D.Warn("Forcing flux to vent due to blocked weapons");
                cs.Flux.ToggleVent(true);
            }
        }

        /// <summary>
        /// The single place where a ship decides which opposing ship it is attacking.
        /// Every join / removal broadcasts a RosterChange to the opposing side, so a
        /// focus never survives the ship it pointed at.
        /// </summary>
        public void ResolveFocus(ShipCombatState cs, List<NPCShip> opponents,
                FocusResolveReason reason) {
            if (Arrays.IsEmpty(opponents)) {
                if (cs.FocusedEnemy == null && cs.NextTargetSwitchTick == 0L) { return; }
                cs.FocusedEnemy = null;
                cs.NextTargetSwitchTick = 0L;
                RefocusWeapons(cs, null);
                cs.Ship.View?.FaceTarget(null);
                return;
            }
            if (reason == FocusResolveReason.Tick && cs.NextTargetSwitchTick != 0L
                    && G.Ticks >= cs.NextTargetSwitchTick) {
                reason = FocusResolveReason.IntervalExpired;
            }
            var current = cs.FocusedEnemy;
            NPCShip next;
            if (reason == FocusResolveReason.Restore) {
                next = RestoreFocus(cs, opponents);
            } else if (!IsFocusValid(current, opponents)
                    || cs.StrategyTarget?.WantsReselectOn(reason) == true) {
                next = SelectFocus(cs, opponents);
                ScheduleNextSwitch(cs);
            } else {
                return;
            }
            if (next == current) { return; }
            cs.FocusedEnemy = next;
            // On restore the weapon targets were just deserialized on purpose, and a
            // ship may legitimately keep firing at something other than its focus.
            if (reason != FocusResolveReason.Restore) {
                RefocusWeapons(cs, next);
            }
            cs.Ship.View?.FaceTarget(next);
        }

        private static bool IsFocusValid(NPCShip focus, List<NPCShip> opponents) {
            return focus != null && focus.IsInCombat && !focus.IsDerelict
                && opponents.Contains(focus);
        }

        /// <summary>
        /// Puts back the saved target, keeping the saved switch schedule with it. A
        /// target that is no longer an opponent falls back to a fresh strategy pick.
        /// </summary>
        private NPCShip RestoreFocus(ShipCombatState cs, List<NPCShip> opponents) {
            var focusedId = cs.FocusedEnemyId;
            cs.FocusedEnemyId = 0;
            if (focusedId != 0) {
                for (int i = opponents.Count - 1; i >= 0; i--) {
                    if (opponents[i].Id != focusedId) { continue; }
                    if (cs.NextTargetSwitchTick == 0L) {
                        // legacy save, or the saved strategy def is gone and a fresh
                        // one was assigned: an interval mode needs a schedule again
                        ScheduleNextSwitch(cs);
                    }
                    return opponents[i];
                }
            }
            var picked = SelectFocus(cs, opponents);
            ScheduleNextSwitch(cs);
            return picked;
        }

        private static void ScheduleNextSwitch(ShipCombatState cs) {
            cs.NextTargetSwitchTick = cs.StrategyTarget?.NextSwitchTick(cs.Rng) ?? 0L;
        }

        private NPCShip SelectFocus(ShipCombatState cs, List<NPCShip> opponents) {
            if (cs.Ship.IsPlayer) {
                // the player picks its own target in ShipCombatTheirWidget, this is
                // only the fallback for when that target is no longer available
                return opponents[0];
            }
            if (cs.StrategyTarget != null) {
                var picked = cs.StrategyTarget.SelectTarget(cs, opponents);
                if (picked != null) { return picked; }
            }
            // no target strategy loaded, any opponent will do
            weightedEnemies.Clear();
            foreach (var e in opponents) {
                if (!e.IsInCombat) { continue; }
                weightedEnemies.Add(e, 1f);
            }
            cs.Rng.TryWeightedFrom(weightedEnemies, out var fallback);
            return fallback;
        }

        private void ConsiderSendingBreachCapsule(GameState s, NPCShip ship,
                long ticks, long combatStartTick) {
            var cs = ship.CombatState;
            if (ship.Class.CombatBreachCapsuleChance <= 0f) { return; }
            if (cs.IsNegotiating) { return; }
            if (s.Sys.Combat.IsHostileCombatInProgress) { return; }
            if (cs.NextBreachCapsuleTick == 0) {
                var delay = s.Rng.Range(Consts.TicksPer2Hours, Consts.TicksPer4Hours);
                if (cs.BreachCapsulesSent > 0) {
                    delay *= 1 + cs.BreachCapsulesSent;
                    delay += Consts.TicksPer4Hours;
                }
                cs.NextBreachCapsuleTick = ticks + delay;
            } else if (ticks > cs.NextBreachCapsuleTick) {
                cs.NextBreachCapsuleTick = 0;
                if (s.Rng.Chance(ship.Class.CombatBreachCapsuleChance)) {
                    if (LaunchBreachCapsuleFromShip(s, ship)) {
                        cs.BreachCapsulesSent++;
                        // 15% chance to follow up with additional breach capsule
                        if (s.Difficulty.Level >= 1.0f && s.Rng.Chance(0.15f * s.Difficulty.Level)) {
                            D.Warn("Extra breach capsule chance rolled!");
                            cs.NextBreachCapsuleTick = ticks + Consts.TicksPer15Minutes;
                        }
                    } else {
                        D.Err("Failed starting breach capsule raid from ship: {0}", ship);
                    }
                } else {
                    D.Warn("Chose not to send breach capsule: {0} ({1})",
                        ship, ship.Class.CombatBreachCapsuleChance);
                }
            }
        }

        public static bool LaunchBreachCapsuleFromShip(GameState s, NPCShip ship) {
            D.Warn("Will send breach capsule: {0} ({1})",
                ship, ship.Class.CombatBreachCapsuleChance);
            string eventId;
            if (s.Rng.Chance(0.5f)) {
                eventId = "Raid/BreachCapsuleRaid";
            } else {
                eventId = "Raid/HullpiercerRaid";
            }
            var evt = StoryEventDef.Get(eventId);
            var data = StoryEventData.CreateFor(evt);
            data.Seed = s.Rng.Int;
            data.Faction = ship.Faction;
            data.GetOrCreateData().SetVector2("Origin", ship.View.Position);
            return s.Query.StartStoryEvent.Ask(data);
        }

        private void TickWeapon(GameState s, ShipCombatState cs,
                bool isPlayer, NPCShip enemy, ShipWeapon w) {
            if (ReloadWeapon(cs, isPlayer, w)) {
                return;
            }
            if (w.IsPointDefense) {
                return;
            }
            if (w.Phase == ShipWeaponPhase.Ready) {
                if (w.TargetShip?.IsInCombat == false) {
                    w.ClearTarget_Ship();
                }

                if (!isPlayer && w.BackingStation == null && w.TargetShip != null) {
                    ConsiderShooting(s, w, cs);
                }
            }
            if (w.Target?.IsDestroyed == true) {
                w.ClearTarget_ShipDevice();
            }
            if (w.Target == null) {
                if (enemy != null && (w.TargetShip == null || w.TargetShip == enemy)) {
                    ChooseTarget(isPlayer, cs, enemy, w);
                }
            }
        }

        private static bool ReloadWeapon(ShipCombatState cs, bool isPlayer, ShipWeapon w) {
            if (isPlayer || w.Phase != ShipWeaponPhase.OutOfAmmo
                    || w.NextReloadTick > G.Ticks) {
                return false;
            }
            // don't try again immediately
            w.NextReloadTick = G.Ticks + Consts.TicksPer2Hours;
            if (cs.AmmoStock.TryGetValue(w.WeaponDef, out var stock)) {
                if (stock == 0) { return false; }
                var toReload = Mathf.Min(stock, w.WeaponDef.AmmoCapacity);
                stock -= toReload;
                cs.AmmoStock[w.WeaponDef] = stock;
                // D.Warn("Reloading enemy weapon ammo: {0} with {1}", w, toReload);
                w.Ammo = toReload;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The player's side is defeated once no ship on it can fight anymore.
        /// </summary>
        private static bool CheckPlayerSideDefeated(GameState s, NPCShip ship,
                long ticks, long combatStartTick, ShipCombatState cs,
                List<NPCShip> playerSide) {
            if (ticks - combatStartTick <= Consts.TicksPer2Hours) { return false; }
            for (int i = playerSide.Count - 1; i >= 0; i--) {
                if (playerSide[i].CombatState.IsCombatCapable) { return false; }
            }
            D.Warn("Player side is defeated by {0}", ship);
            if (cs.Rng.Chance(ship.Faction.VictoryRaidChance)) {
                CombatSys.StartBreachCapsuleRaid(s, ship.Faction);
            }
            // don't attack the ship anymore
            ship.Memory.ChaseTargetId = 0;
            s.Query.EndShipCombatWith.Ask(ship, ShipStateFlags.None);
            return true;
        }

        public static void AutoVentFlux(GameState s, NPCShip ship) {
            var cs = ship.CombatState;
            var isPlayer = ship.IsPlayer;
            if (cs.Flux.State == ShipFluxState.OK) {
                if (isPlayer) {
                    cs.FluxSettings.IsVentingManuallyNow = false;
                }
                var wantVent = cs.FluxSettings.WantsVent(cs.Flux);
                if (wantVent) {
                    // D.Warn("Ship toggling vent on at flux {0}: {1}", cs.Flux, ship);
                    cs.Flux.ToggleVent(true);
                }
            } else if (cs.Flux.State == ShipFluxState.Venting) {
                if (!isPlayer || !cs.FluxSettings.IsVentingManuallyNow) {
                    var wantVentStop = cs.FluxSettings.WantsVentStop(cs.Flux);
                    if (wantVentStop) {
                        // D.Warn("Ship toggling vent off at flux {0}: {1}", cs.Flux, ship);
                        cs.Flux.ToggleVent(false);
                    }
                }
            }
        }

        private void ChooseTarget(bool isPlayer, ShipCombatState cs,
                NPCShip enemy, ShipWeapon w) {
            // enemy may have been destroyed by previous weapon shot
            if (!enemy.IsInCombat) { return; }

            // Per-station preferred target type override (player only; NPC weapons have no BackingStation)
            var pref = w.BackingStation?.PreferredTargetType;
            if (pref != null) {
                if (pref == ShipWeaponTargetTypeDef.ShipHull) {
                    // Hull is not a device — target the ship itself
                    w.SetTarget(enemy);
                    return;
                }
                weightedTargets.Clear();
                var cse2 = enemy.CombatState;
                foreach (var device in cse2.Devices) {
                    if (device.Device.IsDestroyed) { continue; }
                    if (device.Device.TargetType != pref) { continue; }
                    var deviceWeight = device.IsActive ? 1f : 0.2f;
                    weightedTargets.Add(device.Device, deviceWeight);
                }
                if (weightedTargets.Count > 0) {
                    if (cs.Rng.TryWeightedFrom(weightedTargets, out var prefTarget)) {
                        w.SetTarget(prefTarget);
                        return;
                    }
                }
                // No matching targets — fall through to standard auto-targeting
            }

            if (isPlayer || IsFocusedWeapon(cs, w)) {
                ChooseTopPriorityTarget(cs, enemy, w);
            } else {
                ChooseSpreadTarget(cs, enemy, w);
            }
        }

        // Only the targets of the highest priority: the player's weapons, and an NPC ship's
        // focused weapons. The hull is one of them: when its priority is above every device
        // left, the weapon fires at the hull. On a tie a device wins, because a weapon without
        // a device target picks again every tick and would drift onto a device anyway
        private void ChooseTopPriorityTarget(ShipCombatState cs, NPCShip enemy, ShipWeapon w) {
            weightedTargets.Clear();
            var settings = w.EffectiveSettings;
            int topPrio = 1;
            foreach (var device in enemy.CombatState.Devices) {
                if (device.Device.IsDestroyed) { continue; }
                var prio = settings.GetPriorityFor(device.Device.TargetType);
                if (prio < topPrio) { continue; }
                if (prio > topPrio) {
                    weightedTargets.Clear();
                    topPrio = prio;
                }
                weightedTargets.Add(device.Device, device.IsActive ? 1f : 0.2f);
            }
            var hull = ShipWeaponTargetTypeDef.ShipHull;
            var hullPrio = hull != null ? settings.GetPriorityFor(hull) : 0;
            if (weightedTargets.Count > 0 && hullPrio <= topPrio
                    && cs.Rng.TryWeightedFrom(weightedTargets, out var target)) {
                w.SetTarget(target);
            } else if (w.TargetShip != enemy || w.Target != null) {
                w.SetTarget(enemy);
            }
        }

        // An NPC ship's other weapons spread over every device by priority
        private void ChooseSpreadTarget(ShipCombatState cs, NPCShip enemy, ShipWeapon w) {
            weightedTargets.Clear();
            var settings = w.EffectiveSettings;
            foreach (var device in enemy.CombatState.Devices) {
                if (device.Device.IsDestroyed) { continue; }
                var deviceWeight = settings.GetPriorityFor(device.Device.TargetType) * 2f;
                if (!device.IsActive) {
                    deviceWeight *= 0.2f;
                }
                weightedTargets.Add(device.Device, deviceWeight);
            }
            if (cs.Rng.TryWeightedFrom(weightedTargets, out var target)) {
                w.SetTarget(target);
            } else if (w.TargetShip != enemy || w.Target != null) {
                w.SetTarget(enemy);
            }
        }

        // A share of an NPC ship's weapons follow its strategy's top priority, the rest spread.
        // With the spread alone a strategy's focus barely mattered, and many cheap devices
        // soaked NPC fire. Decided per weapon from ids, so it holds across ticks and save/load
        private static bool IsFocusedWeapon(ShipCombatState cs, ShipWeapon w) {
            var share = Tunables.ShipCombatNPCFocusedWeaponShare;
            if (share <= 0f) { return false; }
            if (share >= 1f) { return true; }
            var h = cs.Ship.Id * 2654435761u ^ w.Device.Id * 2246822519u;
            h ^= h >> 15;
            h *= 2654435761u;
            h ^= h >> 13;
            return h % 1000u < share * 1000f;
        }

        public static void ConsiderShooting(GameState s, ShipWeapon w, ShipCombatState cs) {
            var isPlayer = w.Device.Ship.IsPlayer;

            if (w.IsHeldByVent) {
                w.SkippedShotDueToFlux = false;
                return;
            }

            if (!CanShootNow(w, isPlayer)) {
                return;
            }

            if (w.Device.Ship.CombatState.IsNegotiating) {
                return;
            }

            if (w.TargetShip.CombatState.IsNegotiating) {
                return;
            }

            if (isPlayer && w.HasEngageableNonShipTarget) {
                return;
            }

            if (cs.WeaponsShotThisTick >= 1) {
                var chance = 0.5f / cs.WeaponsShotThisTick;
                // chance += 0.15f;
                // D.Warn("Throttle chance: {0} | Shots: {1}", chance, cs.WeaponsShotThisTick);
                if (!cs.Rng.Chance(chance)) {
                    // throttle
                    return;
                }
            }

            if (w.Shoot(s, w.Device.Ship.CombatState.Rng)) {
                cs.WeaponsShotThisTick++;
            }
        }

        private void TickBattleStations(GameState s, ShipCombatState cs, NPCShip enemy) {
            for (int i = cs.BattleStations.Count - 1; i >= 0; i--) {
                var bs = cs.BattleStations[i];
                if (!bs.IsOperatorPresent) { continue; }
                bs.UpdateAccuracyBonus();
                // in a truce nobody shoots, so nobody learns
                if (!cs.IsNegotiating) {
                    bs.TrainOperator();
                }
                if (bs.EffectiveManualFireMode) { continue; }
                bs.ManualFireDone = false;
                if (G.Ticks < bs.NextSalvoTick) { continue; }

                ShipWeapon readyWeapon = null;
                foreach (var w in cs.Weapons) {
                    if (w.BackingStation != bs) { continue; }
                    if (!w.IsReachable) { continue; }
                    if (w.IsHeldByVent) { continue; }
                    if (w.Phase == ShipWeaponPhase.Ready
                            && (w.TargetShip != null || w.HasEngageableNonShipTarget)) {
                        readyWeapon = w;
                        break;
                    }
                }
                if (readyWeapon == null) {
                    continue;
                }

                // negotiation check (skip if targeting a non-ship threat like a missile)
                if (!readyWeapon.HasEngageableNonShipTarget) {
                    if (cs.IsNegotiating
                            || readyWeapon.TargetShip?.CombatState.IsNegotiating == true) {
                        continue;
                    }
                }

                // flux check
                var flux = cs.Flux;
                if (flux.FutureTotalPercent(
                        readyWeapon.WeaponDef.HardFluxPerShot,
                        readyWeapon.WeaponDef.SoftFluxPerShot)
                        >= readyWeapon.EffectiveSettings.ShootFluxThreshold) {
                    readyWeapon.SkippedShotDueToFlux = true;
                    continue;
                }

                readyWeapon.AccuracyBonusFromBattleStation = bs.OperatorAccuracyBonus;
                if (readyWeapon.Shoot(s, cs.Rng)) {
                    cs.WeaponsShotThisTick++;
                    bs.NextSalvoTick = G.Ticks + cs.Rng.Range(
                        Tunables.BattleStationSalvoDelayTicksFrom,
                        Tunables.BattleStationSalvoDelayTicksTo);
                }
            }
        }

        /// <summary>
        /// Called when a ship switches its focused target (the player does it through
        /// ShipCombatTheirWidget). Clears weapon targets locked onto a different ship
        /// so they re-acquire on the next tick.
        /// </summary>
        public static void RefocusWeapons(ShipCombatState cs, NPCShip newEnemy) {
            foreach (var w in cs.Weapons) {
                if (w.TargetShip != null && w.TargetShip != newEnemy) {
                    w.ClearTarget_Ship();
                    w.ClearTarget_ShipDevice();
                }
            }
        }

        public static void RecalculateBattleStationAssignments(ShipCombatState cs) {
            if (!cs.Ship.IsPlayer) {
                D.Err("Trying to recalculate battle stations for NPC! {0}", cs.Ship);
                return;
            }
            foreach (var w in cs.Weapons) {
                if (w.IsPointDefense) { continue; }
                w.BackingStation = null;
                w.AccuracyBonusFromBattleStation = 1.0f;
            }
            foreach (var bs in cs.BattleStations) {
                if (bs.WeaponDef == null) {
                    bs.AssignedWeapons.Set(0);
                    continue;
                }
                int capacity = bs.TotalWeaponCapacity;
                int count = 0;
                foreach (var w in cs.Weapons) {
                    if (count >= capacity) { break; }
                    if (w.WeaponDef != bs.WeaponDef) { continue; }
                    if (w.BackingStation != null) { continue; }
                    w.BackingStation = bs;
                    count++;
                }
                bs.AssignedWeapons.Set(count);
            }
            foreach (var w in cs.Weapons) {
                if (w.IsPointDefense) { continue; }
                if (w.BackingStation != null) {
                    w.Comp?.UpdateStationState();
                    continue;
                }
                if (w.Phase == ShipWeaponPhase.Windup || w.Phase == ShipWeaponPhase.Charging
                        || w.Phase == ShipWeaponPhase.Ready || w.Phase == ShipWeaponPhase.Firing) {
                    w.Phase = ShipWeaponPhase.Inactive;
                    w.PhaseTicksLeft = 0;
                    w.PhaseTicksTotal = 0;
                    // this will update station state
                    w.OnPhaseChange?.Invoke();
                } else {
                    w.Comp?.UpdateStationState();
                }
            }
        }

        public static bool CanShootNow(ShipWeapon w, bool isPlayer) {
            w.SkippedShotDueToFlux = false;
            if (isPlayer) {
                if (w.EffectiveSettings.FireMode != ShipWeaponFireMode.Automatic) {
                    return false;
                }
            }

            var cs = w.Device.Ship.CombatState;
            if (cs.TicksTillShoot > 0) {
                // D.Warn("SHip hesitating to shoot: {0}: {1}", cs.TicksTillShoot, w);
                cs.TicksTillShoot--;
                return false;
            }
            var flux = cs.Flux;

            var willAddHardFlux = w.WeaponDef.HardFluxPerShot;
            var willAddSoftFlux = w.WeaponDef.SoftFluxPerShot;

            var futureTotalFlux = flux.FutureTotalPercent(willAddHardFlux, willAddSoftFlux);

            var threshold = w.EffectiveSettings.ShootFluxThreshold;
            if (futureTotalFlux >= threshold) {
                w.SkippedShotDueToFlux = true;
                return false;
            }

            return true;
        }
    }
}
