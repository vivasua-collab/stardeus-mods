using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Diplomacy;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Ships {
    /// <summary>
    /// Flies to the player and joins their fight as an ally on arrival. Assigned from
    /// code rather than an assignment def, because the reason to join depends on the
    /// current combat rather than on the ship's own routine. See ShipJoinMotive.
    /// </summary>
    public sealed class NPCShipBehavior_Sector_JoinCombat : NPCShipBehavior {
        public const string BehaviorId = "Sector_JoinCombat";
        public static int BehaviorIdH = Hashes.S(BehaviorId);
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Sector_JoinCombat());
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
                if (!IsWorthJoiningFor(target.S, ship)) {
                    ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                    return;
                }
                if (!target.S.Query.JoinShipCombatAsAlly.Ask(ship)) {
                    // the ally cap could have filled up while this one was flying over
                    ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                }
            };
            ship.Memory.OnReachFollowTargetFail = (ship, target) => {
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
            };
        }

        public override void DoTurn(NPCShip ship) {
            if (!IsWorthJoiningFor(A.S, ship)) {
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                return;
            }
            TryFollow(ship);
        }

        /// <summary>
        /// Re-checked on arrival and every turn while flying over, so a ship whose
        /// reason evaporated mid-flight turns back. The CommonEnemy chance roll is
        /// deliberately not repeated here - once committed, a ship follows through.
        /// </summary>
        public static bool IsWorthJoiningFor(GameState s, NPCShip ship) {
            return GetMotive(s, ship) != ShipJoinMotive.None;
        }

        /// <summary>
        /// Why, if at all, this ship would join the player's current fight.
        /// </summary>
        public static ShipJoinMotive GetMotive(GameState s, NPCShip ship) {
            var enemies = s.Query.GetEnemyShips.Ask();
            if (enemies.Count == 0) { return ShipJoinMotive.None; }
            var relations = s.Sys.Diplomacy.Relations;
            var player = FactionDef.Player;
            var toPlayer = relations.Get(ship.Faction, player);
            // will not fight for someone they consider an enemy
            if (toPlayer == FactionRelation.Enemy) { return ShipJoinMotive.None; }
            var isPolice = false;
            var hasCommonEnemy = false;
            for (int i = enemies.Count - 1; i >= 0; i--) {
                var enemyFac = enemies[i].Faction;
                // Never turn up to shoot at ourselves or at our own allies. Vetoing the
                // whole join rather than one target matters: target strategies pick
                // freely from the enemy roster, so a joiner that tolerated an allied
                // ship on that roster could end up shooting it.
                if (enemyFac == ship.Faction) { return ShipJoinMotive.None; }
                var toEnemy = relations.Get(ship.Faction, enemyFac);
                if (toEnemy == FactionRelation.Allied) { return ShipJoinMotive.None; }
                if (toEnemy == FactionRelation.Enemy) { hasCommonEnemy = true; }
                if (ship.IsPolice && enemyFac.IdH == FactionIdH.Pirate) { isPolice = true; }
            }
            // police first, so the existing pirate response is unchanged by alliances
            if (isPolice) { return ShipJoinMotive.Police; }
            if (toPlayer == FactionRelation.Allied) { return ShipJoinMotive.Ally; }
            if (hasCommonEnemy) { return ShipJoinMotive.CommonEnemy; }
            return ShipJoinMotive.None;
        }

        /// <summary>
        /// Sends eligible ships in the player's sector after the player while a fight is
        /// on. Pending joiners count against the ally cap too, so one scan can never
        /// send more ships than there is room for.
        ///
        /// Slots are filled in motive tiers rather than sector-list order: with only a
        /// couple of slots, an alliance the player negotiated must not lose its place to
        /// an opportunist that merely happened to sit earlier in the list.
        /// </summary>
        /// <param name="allySlotsLeft">cap minus the NPC allies already fighting</param>
        public static void ScanForJoiners(GameState s, int allySlotsLeft,
                long combatStartTick) {
            if (allySlotsLeft <= 0) { return; }
            if (!s.Sys.NPCShip.SectorShips.TryGetValue(A.PlayerSector.Sector,
                    out var ships)) {
                return;
            }
            if (Arrays.IsEmpty(ships)) { return; }
            allySlotsLeft -= CountPendingJoiners(ships);
            if (allySlotsLeft <= 0) { return; }
            allySlotsLeft = AssignTier(s, ships, ShipJoinMotive.Police,
                allySlotsLeft, combatStartTick);
            allySlotsLeft = AssignTier(s, ships, ShipJoinMotive.Ally,
                allySlotsLeft, combatStartTick);
            AssignTier(s, ships, ShipJoinMotive.CommonEnemy,
                allySlotsLeft, combatStartTick);
        }

        /// <summary>
        /// Motive is recomputed per tier instead of bucketing ships into lists - this
        /// runs on a tick path, and a few cheap recomputations beat allocating per scan.
        /// </summary>
        /// <returns>slots still free after this tier</returns>
        private static int AssignTier(GameState s, List<NPCShip> ships,
                ShipJoinMotive tier, int allySlotsLeft, long combatStartTick) {
            for (int i = ships.Count - 1; i >= 0 && allySlotsLeft > 0; i--) {
                var ship = ships[i];
                if (GetMotive(s, ship) != tier) { continue; }
                if (!CanJoin(s, ship, tier, combatStartTick)) { continue; }
                if (AssignTo(ship)) {
                    allySlotsLeft--;
                }
            }
            return allySlotsLeft;
        }

        private static int CountPendingJoiners(List<NPCShip> ships) {
            var count = 0;
            for (int i = ships.Count - 1; i >= 0; i--) {
                if (ships[i].BehaviorSector is NPCShipBehavior_Sector_JoinCombat) {
                    count++;
                }
            }
            return count;
        }

        private static bool CanJoin(GameState s, NPCShip ship, ShipJoinMotive motive,
                long combatStartTick) {
            if (!ship.IsActive || ship.IsDerelict) { return false; }
            // covers ships that are already allies, they are all flagged InCombat
            if (ship.IsInCombat || ship.IsInHyperspace) { return false; }
            if (ship.BehaviorSector is NPCShipBehavior_Sector_JoinCombat) { return false; }
            if (ship.BehaviorSector?.IsBusy(ship, out _) == true) { return false; }
            if (s.Query.IsActiveQuestProvider.Ask(ship)) { return false; }
            if (motive == ShipJoinMotive.CommonEnemy && !RollsToJoin(ship, combatStartTick)) {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Deterministic per ship per combat. ScanForJoiners runs every 15 ticks, so a
        /// fresh roll each time would make joining a certainty in a long fight; seeding
        /// from the ship and the combat makes a refusal stick, and survive a save/load,
        /// without persisting anything.
        /// </summary>
        private static bool RollsToJoin(NPCShip ship, long combatStartTick) {
            var seed = Hashes.Quick(ship.Seed, (int)combatStartTick);
            return new Rng(seed).Chance(Tunables.ShipCombatCommonEnemyJoinChance);
        }
    }
}
