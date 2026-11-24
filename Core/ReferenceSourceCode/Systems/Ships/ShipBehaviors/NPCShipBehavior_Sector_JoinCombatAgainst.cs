using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Diplomacy;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    /// <summary>
    /// Flies to the player and joins the fight against them on arrival. Mirrors
    /// NPCShipBehavior_Sector_JoinCombat, on the opposing side: ships of the attacked
    /// ship's own faction, allies of that faction, and police defending a lawful ship
    /// from an outlaw player, may reinforce the enemy roster. See ShipJoinAgainstMotive
    /// and the "Enemy reinforcement" section of ShipCombatSys.cs for the shared
    /// enemy-slot budget both this scan and the hail-insult retaliation path draw from.
    /// This scan itself is only ever invoked (from ShipCombatSys.OnTick) while
    /// PlayerInitiatedCombat is true - an NPC's own unprovoked attack on the player must
    /// never draw in reinforcements.
    /// </summary>
    public sealed class NPCShipBehavior_Sector_JoinCombatAgainst : NPCShipBehavior {
        public const string BehaviorId = "Sector_JoinCombatAgainst";
        public static int BehaviorIdH = Hashes.S(BehaviorId);
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Sector_JoinCombatAgainst());
        }
        public override string Id => BehaviorId;
        public override NPCShipBehaviorScope Scope => NPCShipBehaviorScope.Sector;
        public override bool CanHyperjump => false;
        public override bool CanInterruptForNotice(NPCShip observer, NPCShip target) {
            return false;
        }

        public static bool AssignTo(NPCShip ship) {
            if (!CheckCanReach(ship, A.Player)) { return false; }
            ship.SetBehavior(Registries.NPCShipBehaviors.FastGet(BehaviorIdH),
                NPCShipBehaviorScope.Sector);
            ship.Memory.SetFollowTarget(A.Player);
            SetOnFollowTarget(ship);
            return true;
        }

        public override void OnLoad(NPCShip ship) {
            // the reach callbacks are delegates, they are not serialized
            if (!ship.Memory.IsNearFollowTarget) {
                SetOnFollowTarget(ship);
            }
        }

        private static void SetOnFollowTarget(NPCShip ship) {
            ship.Memory.OnReachFollowTarget = (ship, target) => {
                if (GetMotive(target.S, ship) == ShipJoinAgainstMotive.None) {
                    ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                    return;
                }
                if (!target.S.Query.JoinShipCombatAgainstPlayer.Ask(ship)) {
                    // the enemy cap could have filled up while this one was flying over
                    ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                }
            };
            ship.Memory.OnReachFollowTargetFail = (ship, target) => {
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
            };
        }

        public override void DoTurn(NPCShip ship) {
            if (GetMotive(A.S, ship) == ShipJoinAgainstMotive.None) {
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                return;
            }
            TryFollow(ship);
        }

        /// <summary>
        /// True when ship must never fight against the player - allied with the
        /// player's own faction directly, or with the faction of a current NPC ally.
        /// Shared by GetMotive and by ShipHailAction_Insult.DoRetaliate /
        /// ShipCombatSys.JoinCombatAgainstPlayer, which have their own dispatch and
        /// arrival paths outside the motive system but must respect the same
        /// invariant: a ship on the player's side must never end up opposite a ship of
        /// its own or an allied faction, since target strategies pick freely from the
        /// opposing roster and would have it shoot a friendly ship.
        /// </summary>
        public static bool IsAlliedWithPlayerSide(GameState s, NPCShip ship) {
            var relations = s.Sys.Diplomacy.Relations;
            if (relations.Get(ship.Faction, FactionDef.Player) == FactionRelation.Allied) {
                return true;
            }
            var allies = s.Query.GetAllyShips.Ask();
            for (int i = allies.Count - 1; i >= 0; i--) {
                var ally = allies[i];
                if (ally.IsPlayer) { continue; }
                if (ally.Faction == ship.Faction) { return true; }
                if (relations.Get(ship.Faction, ally.Faction) == FactionRelation.Allied) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Why, if at all, this ship would join the fight against the player. Checked
        /// against GetShipsAgainstPlayer, not just the live Enemies roster, so a ship
        /// only reinforces something it is actually motivated to reinforce, and never
        /// treats another inbound joiner as its own motive.
        ///
        /// Re-checks IsShipCombatPlayerInitiated on every call, not just at scan
        /// dispatch time: this is what makes DoTurn and the arrival callback turn an
        /// already-flying ship back if the fight it was sent for ends and a different,
        /// NPC-initiated one is what the player is in by the time it gets there.
        /// </summary>
        public static ShipJoinAgainstMotive GetMotive(GameState s, NPCShip ship) {
            if (!s.Query.IsShipCombatPlayerInitiated.Ask()) { return ShipJoinAgainstMotive.None; }
            var against = s.Query.GetShipsAgainstPlayer.Ask();
            if (against.Count == 0) { return ShipJoinAgainstMotive.None; }
            if (IsAlliedWithPlayerSide(s, ship)) { return ShipJoinAgainstMotive.None; }
            var relations = s.Sys.Diplomacy.Relations;
            var best = ShipJoinAgainstMotive.None;
            var playerIsOutlaw = A.Player.Ship.IsOutlaw;
            for (int i = against.Count - 1; i >= 0; i--) {
                var v = against[i];
                if (v == ship) { continue; }
                // another opportunist is not a reason to fly out: only live enemies and
                // independently committed attackers count, or two inbound joiners would
                // keep each other airborne after the fight they were sent for ends
                if (v.BehaviorSector is NPCShipBehavior_Sector_JoinCombatAgainst) { continue; }
                var toV = relations.Get(ship.Faction, v.Faction);
                // will not fight for someone they consider an enemy
                if (toV == FactionRelation.Enemy) { continue; }
                // a faction disowns its own criminals; Pirate is IsOutlaw:true, so this
                // is also what stops police defending pirates
                if (v.IsOutlaw && !ship.IsOutlaw) { continue; }
                if (ship.IsPolice && v.IsOutlaw) { continue; }
                if (ship.IsPolice && playerIsOutlaw) { return ShipJoinAgainstMotive.Police; }
                if (v.Faction == ship.Faction) {
                    best = ShipJoinAgainstMotive.SameFaction;
                } else if (toV == FactionRelation.Allied && best != ShipJoinAgainstMotive.SameFaction) {
                    best = ShipJoinAgainstMotive.Ally;
                }
            }
            return best;
        }

        /// <summary>
        /// Difficulty-scaled odds a ship reinforces the player's opponents.
        /// </summary>
        public static float JoinChanceFor(GameState s) {
            return Mathf.Clamp01(Tunables.ShipCombatEnemyJoinChance * s.Difficulty.SoftLevel);
        }

        /// <summary>
        /// Sends eligible ships in the player's sector after the player while a fight is
        /// on, tiered Police, then SameFaction, then Ally. Each tier re-asks the enemy
        /// slot budget before every candidate, so a dispatch mid-tier is reflected
        /// immediately and no separate slot count needs to be threaded through.
        /// </summary>
        public static void ScanForAttackers(GameState s, long combatStartTick) {
            if (!s.Sys.NPCShip.SectorShips.TryGetValue(A.PlayerSector.Sector,
                    out var ships)) {
                return;
            }
            if (Arrays.IsEmpty(ships)) { return; }
            AssignTier(s, ships, ShipJoinAgainstMotive.Police, combatStartTick);
            AssignTier(s, ships, ShipJoinAgainstMotive.SameFaction, combatStartTick);
            AssignTier(s, ships, ShipJoinAgainstMotive.Ally, combatStartTick);
        }

        private static void AssignTier(GameState s, List<NPCShip> ships,
                ShipJoinAgainstMotive tier, long combatStartTick) {
            for (int i = ships.Count - 1; i >= 0; i--) {
                if (s.Query.GetShipCombatEnemySlots.Ask() <= 0) { return; }
                var ship = ships[i];
                if (GetMotive(s, ship) != tier) { continue; }
                if (!CanJoin(s, ship, combatStartTick)) { continue; }
                AssignTo(ship);
            }
        }

        private static bool CanJoin(GameState s, NPCShip ship, long combatStartTick) {
            if (!ship.IsActive || ship.IsDerelict) { return false; }
            if (ship.IsInCombat || ship.IsInHyperspace) { return false; }
            if (ship.BehaviorSector is NPCShipBehavior_Sector_JoinCombatAgainst) { return false; }
            if (ship.BehaviorSector?.IsBusy(ship, out _) == true) { return false; }
            if (s.Query.IsActiveQuestProvider.Ask(ship)) { return false; }
            if (!RollsToJoin(s, ship, combatStartTick)) { return false; }
            return true;
        }

        /// <summary>
        /// Deterministic per ship per combat. ScanForAttackers runs every 15 ticks, so a
        /// fresh roll each time would make joining a certainty in a long fight; seeding
        /// from the ship and the combat makes a refusal stick, and survive a save/load,
        /// without persisting anything.
        /// </summary>
        private static bool RollsToJoin(GameState s, NPCShip ship, long combatStartTick) {
            var seed = Hashes.Quick(ship.Seed, (int)combatStartTick);
            return new Rng(seed).Chance(JoinChanceFor(s));
        }
    }
}
