using System.Collections.Generic;
using System.Linq;
using Game.Achievements;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Diplomacy;
using Game.Systems.Space;
using Game.Systems.Suggestions;
using Game.UI;
using KL.Clock;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    /// <summary>
    /// Handles all NPC ships - their routes, spawning, travel, etc
    /// </summary>
    public sealed class NPCShipSys : GameSystem, ISaveableSpecial {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new NPCShipSys());
        }
        /// <summary>
        /// When turn time passes, ships will move
        /// </summary>
        private long nextTurnTick;
        private NPCShipGen shipGen;
        public const string SysId = "NPCShip";
        public override string Id => SysId;
        private uint LastShipId = 1;
        public readonly List<NPCShip> Ships = new();
        public readonly List<ShipHyperTrail> HyperTrails = new();
        private NPCShip playerShip;
        private List<ShipClassDef> unlockedShipClasses = new();
        public readonly Dictionary<SpaceSector, List<NPCShip>> SectorShips = new();
        public readonly Dictionary<SpaceSector, List<ShipHyperTrail>> SectorHyperTrails = new();
        /// <summary>
        /// Global (cross-ship) N2P reaction cooldowns, keyed by NPCShipReaction.IdH.
        /// Value is the absolute tick before which the reaction may not be selected
        /// again by any ship reacting toward the player.
        /// </summary>
        private readonly Dictionary<int, long> reactionGlobalCooldowns = new();
        private NPCShipSysData data;
        private bool mustRegenNPCShipLoadouts;
        private readonly HashSet<uint> skipLoadoutRegenShipIds = new();

        public override void Unload() {
            Ships.Clear();
            SectorShips.Clear();
            reactionGlobalCooldowns.Clear();
            skipLoadoutRegenShipIds.Clear();
            mustRegenNPCShipLoadouts = false;
        }

        public bool IsReactionGlobalCoolingDown(int reactionIdH) {
            return reactionGlobalCooldowns.TryGetValue(reactionIdH, out var t) && t > G.Ticks;
        }

        public void StampReactionGlobalCooldown(int reactionIdH, int ticks) {
            reactionGlobalCooldowns[reactionIdH] = G.Ticks + ticks;
        }

        protected override void OnInitialize() {
            shipGen = new NPCShipGen(S);
            S.Sig.AfterLoadState.AddListener(OnLoad);
            S.Sig.UniverseCreated.AddListener(PopulateUniverse);
            S.Sig.NewGameInitialized.AddListener(CreatePlayerShip);
        }

        private void OnLoad(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
            state.Clock.OnTickProgress.AddListener(OnTickProgress);
            state.Sig.HyperjumpExecuted.AddListener(OnHyperjump);
            state.Sig.NPCShipEnteredSector.AddListener(OnNPCShipEnterSector);
            state.Sig.HyperspaceLocationChanged.AddListener(OnPlayerHyperjump);
            state.Sig.ShipDefeatedInCombat.AddListener(OnShipDefeated);
            state.Query.NPCShipsAt.SetResponder(GetNPCShipsAt);
            state.Query.NPCShipById.SetResponder(FindNPCShip);
            state.Query.FindHyperTrail.SetResponder(FindHyperTrail);
            state.Query.HasNPCShipsAt.SetResponder(HasNPCShipsAt);
            state.Query.ShipHyperTrailsAt.SetResponder(ShipHyperTrailsAt);
            state.Query.SpawnShipAt.SetResponder(SpawnShipAt);
            state.Query.SpawnShipTplAt.SetResponder(SpawnShipTemplateAt);
            state.Query.SpawnOpponentShipFor.SetResponder(SpawnOpponentFor);
            state.Query.NPCShipJumpTo.SetResponder(NPCShipJumpTo);
            state.Query.ChangeNPCShipBehavior.SetResponder(ChangeNPCShipBehavior);
            state.Query.UpdatePlayerShipEnergy.SetResponder(UpdatePlayerShipEnergy);
            state.Query.GetUnlockedShipClasses.SetResponder(() => unlockedShipClasses);
            state.Sig.AreasRecalculated.AddListener(AfterAreasRecalculated);
            Ready.WhenGame(this, "RestoreNPCShips", RestoreData);
        }

        private void OnShipDefeated(NPCShip attacker, NPCShip victim) {
            if (victim.Faction == attacker.Faction) {
                D.ErrSoft("Attacker and victim have same faction: {0} vs {1}: {2}",
                    attacker, victim, attacker.Faction);
                return;
            }
            if (victim.Faction.IsPlayer) { return; }
            S.Query.ChangeFactionAttitude.Ask(
                victim.Faction, attacker.Faction, -0.01f);

            if (SectorShips.TryGetValue(attacker.Sector, out var ships)) {
                if (Arrays.IsEmpty(ships)) { return; }
                var reactedFactions = new HashSet<FactionDef>();
                foreach (var ship in ships) {
                    if (ship.IsPlayer) { continue; }
                    if (ship.IsInHyperspace) { continue; }
                    if (ship == attacker || ship == victim) { continue; }
                    if (ship.Faction == attacker.Faction) { continue; }
                    if (victim.Faction == ship.Faction) { continue; }

                    float change;
                    if (ship.Faction.IsOutlaw) {
                        // outlaws like those who defeat other ships
                        // attacker will gain respect for defeating a ship
                        if (ship.IsPolice) {
                            change = 0.025f;
                        } else {
                            change = 0.015f;
                        }
                    } else {
                        if (!reactedFactions.Add(ship.Faction)) { continue; }
                        // non-outlaw factions will like when outlaws are
                        // eliminated
                        if (victim.IsOutlaw) {
                            change = 0.025f;
                        } else {
                            // observing another peaceful faction getting
                            // attacked will give negative attitude
                            change = -0.005f;
                        }
                    }
                    // D.Warn("Changing faction attitude after battle: observer: {0} -> (attacker: {1}) (change: {2}) | Victim faction: {3}",
                    //     ship.Faction, attacker.Faction, change, victim.Faction);
                    S.Query.ChangeFactionAttitude.Ask(
                        ship.Faction, attacker.Faction, change);
                }
            }
            ApplyRelationKillReaction(attacker, victim);
        }

        /// <summary>
        /// Diplomatic fallout from a kill, as opposed to the witness reaction above:
        /// factions at war with the victim's faction approve, factions allied with it
        /// take it personally. Global rather than proximity based - nobody needs to have
        /// seen it.
        ///
        /// Deliberately stacks with the witness loop for factions that both watched the
        /// kill and are diplomatically aligned; the two are answering different
        /// questions.
        /// </summary>
        private void ApplyRelationKillReaction(NPCShip attacker, NPCShip victim) {
            // Player kills only. NPC vs NPC battles run constantly in the background,
            // and letting them move relations globally would fight the era drift
            // simulation in DiplomacySys.
            if (!attacker.IsPlayer) { return; }
            var relations = S.Sys.Diplomacy.Relations;
            var player = FactionDef.Player;
            var factions = relations.DiplomaticFactions;
            for (int i = 0; i < factions.Count; i++) {
                var fac = factions[i];
                if (fac.IsPlayer || fac == victim.Faction) { continue; }
                var rel = relations.Get(fac, victim.Faction);
                if (rel == FactionRelation.Enemy) {
                    S.Query.ChangeFactionAttitude.Ask(fac, player,
                        Tunables.KillEnemyOfFactionAttitudeBonus);
                } else if (rel == FactionRelation.Allied) {
                    S.Query.ChangeFactionAttitude.Ask(fac, player,
                        Tunables.KillAllyOfFactionAttitudePenalty);
                }
            }
        }

        private NPCShip SpawnOpponentFor(NPCShip ship, NPCShip template) {
            template ??= new NPCShip();
            // Scripted opponent (story event, mining retaliation, etc.) - must never
            // become a quest giver, however long it survives afterward.
            template.NoQuests = true;
            var diff = S.Rng.Curve(S.Difficulty.NPCShipStrengthCurve);
            var playerCP = ship.CombatState.CurrentClassPoints;

            template.WantedClassPoints = Mathf.RoundToInt(
                playerCP * diff * ShipClassDef.OpponentStrengthCompression(playerCP));
            template.WantedClassPoints = Mathf.Max(template.WantedClassPoints,
                MinViableOpponentClassPoints());

            // Cap weapon tier at player's highest tier + 1 to prevent high-tier weapons
            // from appearing against a low-tier player.
            int playerMaxTier = 0;
            foreach (var w in ship.CombatState.Weapons) {
                if (w.WeaponDef.Device.Tier > playerMaxTier) {
                    playerMaxTier = w.WeaponDef.Device.Tier;
                }
            }
            template.WantedWeaponsTier = playerMaxTier + 1;

            if (template.Class == null) {
                float outlawChance;
                if (template.IsPolice) {
                    outlawChance = 0f;
                } else if (!template.IsClassValid) {
                    outlawChance = 1f;
                } else if (template.Faction?.IsOutlaw == true) {
                    outlawChance = 0.33f;
                } else {
                    outlawChance = 0.05f;
                }
                var isOutlaw = S.Rng.Chance(outlawChance);
                float cp = template.WantedClassPoints;
                if (isOutlaw) {
                    // we need a weaker class ship so it would become outlaw
                    // once we spawn all devices
                    cp /= S.Rng.Range(1.15f, 1.75f);
                } else if (template.IsPolice) {
                    // stronger ship for police
                    cp *= S.Rng.Range(1.15f, 1.75f);
                }
                template.Class = ShipClassDef.StrongestClassForPoints(cp,
                    template.IsPolice);
            }

            return SpawnShipTemplateAt(ship.SO.Parent, template);
        }

        private static int MinViableOpponentClassPoints() {
            var thruster = ShipDeviceDef.FastGet(ShipDeviceIdH.Engine_Thruster);
            var shredder = ShipWeaponDef.FastGet(ShipWeaponIdH.ShredderCannon);
            return 2 * thruster.ClassPoints + shredder.Device.ClassPoints;
        }

        private static void EnsureOpponentHasWeapon(NPCShip ship) {
            var combatState = ship.CombatState;
            if (combatState.Weapons.Count > 0) { return; }

            var shredder = ShipWeaponDef.FastGet(ShipWeaponIdH.ShredderCannon);
            int classPointsToFree = Mathf.Max(0, combatState.CurrentClassPoints
                + shredder.Device.ClassPoints - ship.Class.MaxClassPoints);
            for (int i = combatState.Engines.Count - 1;
                    i >= 0 && classPointsToFree > 0
                        && combatState.Engines.Count > 1; i--) {
                var engine = combatState.Engines[i];
                classPointsToFree -= engine.Device.DeviceDef.ClassPoints;
                combatState.RemoveDevice(engine);
            }
            for (int i = combatState.Devices.Count - 1;
                    i >= 0 && classPointsToFree > 0; i--) {
                var device = combatState.Devices[i];
                if (device is ShipEngine) { continue; }
                classPointsToFree -= device.Device.DeviceDef.ClassPoints;
                combatState.RemoveDevice(device);
            }

            combatState.AddWeapon(shredder);
            combatState.Flux.UpdateCapacity(combatState);
            NPCShipGen.ValidateShipEngineCounts(ship, false);
        }

        private int UpdatePlayerShipEnergy() {
            var sit = S.Map?.Situation;
            if (playerShip != null && sit != null) {
                playerShip.EnergyProduction = (int) sit.GridMaxOutput;
                return playerShip.EnergyProduction;
            }
            return 0;
        }

        private IShipDevice FindShipDevice(NPCShip ship, uint id) {
            if (id == 0) {
                D.Err("Invalid device id! Can't be 0. Ship: {0}", ship);
            }
            var devices = ship.CombatState?.Devices;
            if (devices == null) {
                D.Err("Couldn't find ship device by id: {0}. Devices are null", id);
                return null;
            }
            for (int i = devices.Count - 1; i >= 0; i--) {
                var device = devices[i];
                if (device.Device == null) {
                    D.LogInstance("Got a ShipDevice that has no Device! {0}", device);
                    continue;
                }
                if (device.Device.Id == id) {
                    return device;
                }
            }
            D.Err("Couldn't find ship device by id: {0}. Devices: {1}", id,
                D.DumpCollection(devices));
            return null;
        }

        private void AfterAreasRecalculated() {
            var ship = S.Universe.Player.Ship;
            if (ship != null && !ship.IsInCombat) {
                ship.UpdateSize(S.Sys.Areas.ShipSize);
            }
        }

        private ShipHyperTrail FindHyperTrail(SpaceObject trailSO) {
            var sec = trailSO?.Parent?.Sector;
            if (sec == null) { return null; }
            if (SectorHyperTrails.TryGetValue(sec, out var trails)) {
                foreach (var tr in trails) {
                    if (tr.SO == trailSO) {
                        return tr;
                    }
                }
            }
            return null;
        }

        private List<ShipHyperTrail> ShipHyperTrailsAt(SpaceSector sec) {
            if (SectorHyperTrails.TryGetValue(sec, out var res)) {
                return res;
            }
            return null;
        }

        private NPCShip FindNPCShip(uint id) {
            for (int i = Ships.Count - 1; i >= 0; i--) {
                NPCShip ship = Ships[i];
                if (ship.Id == id) {
                    return ship;
                }
            }
            return null;
        }

        private NPCShipBehavior ChangeNPCShipBehavior(NPCShip ship,
                NPCShipBehaviorScope scope, bool canRepeat) {
            switch (scope) {
                case NPCShipBehaviorScope.Sector: {
                    shipGen.AssignSectorBehavior(ship, canRepeat, Rng.Unseeded);
                    return ship.BehaviorSector;
                }
                case NPCShipBehaviorScope.Region: {
                    shipGen.AssignRegionBehavior(ship, canRepeat, Rng.Unseeded);
                    return ship.BehaviorRegion;
                }
                default: {
                    D.Err("Unknown ship behavior scope! {0}", scope);
                    return null;
                }
            }
        }

        private bool TryRegenerateNPCShipLoadout(NPCShip ship) {
            if (ship.Class == null || ship.Tag == null || ship.Faction == null) {
                D.ErrSoft("Skipping NPC ship loadout regeneration due to missing class, tag, or faction: {0}",
                    ship);
                return false;
            }
            shipGen.RegenerateLoadoutFor(ship, ship.SeededRng(Primes.Prime091));
            return true;
        }

        // A ship kept out of the loadout regeneration because it is in the player's combat
        // keeps its loadout, but not a size from before the NPC size ranges shrank: its hull
        // points and its width in the combat view follow the size.
        private static void ClampToNPCSizeRange(NPCShip ship) {
            var max = ship.Class?.NPCSizeMax ?? 0;
            if (max > 0 && ship.Size > max) {
                ship.UpdateSize(max);
                ship.CombatState?.RecalculateMaxHP(capCurrentHP: true);
            }
        }

        private void RestoreData() {
            int loadoutsRegenerated = 0;
            int loadoutsSkippedPlayer = 0;
            int loadoutsSkippedPlayerCombat = 0;
            int loadoutsSkippedDerelict = 0;
            int loadoutsSkippedInvalid = 0;
            if (!Arrays.IsEmpty(data?.Ships)) {
                foreach (var d in data.Ships) {
                    if (NPCShipData.TryDeserialize(S, d, out var ship)) {
                        if (mustRegenNPCShipLoadouts) {
                            if (ship.IsPlayer) {
                                loadoutsSkippedPlayer++;
                            } else if (ship.IsDerelict) {
                                loadoutsSkippedDerelict++;
                            } else if (skipLoadoutRegenShipIds.Contains(ship.Id)) {
                                loadoutsSkippedPlayerCombat++;
                                ClampToNPCSizeRange(ship);
                            } else if (TryRegenerateNPCShipLoadout(ship)) {
                                loadoutsRegenerated++;
                            } else {
                                loadoutsSkippedInvalid++;
                            }
                        }
                        Ships.Add(ship);
                        UpdateShipSector(ship, null, ship.Sector);
                    }
                }
            }
            if (mustRegenNPCShipLoadouts) {
                D.Log("NPC ship loadout regeneration complete. Regenerated: {0}, skipped player: {1}, skipped player combat: {2}, skipped derelict: {3}, skipped invalid: {4}",
                    loadoutsRegenerated, loadoutsSkippedPlayer, loadoutsSkippedPlayerCombat,
                    loadoutsSkippedDerelict, loadoutsSkippedInvalid);
                mustRegenNPCShipLoadouts = false;
                skipLoadoutRegenShipIds.Clear();
            }
            if (!Arrays.IsEmpty(data?.HyperTrails)) {
                foreach (var tr in data.HyperTrails) {
                    if (ShipHyperTrailData.TryDeserialize(S, tr, out var trail)) {
                        AddHyperTrail(trail);
                    }
                }
            }
            if (A.Player.Ship == null) {
                CreatePlayerShip();
                if (!S.Vars.IsLoadingLegacySave) {
                    D.Warn("Player ship did not exist, creating now: {0}",
                        A.Player.Ship);
                }
            }

            playerShip = A.Player.Ship;

            if (!unlockedShipClasses.Contains(playerShip.Class)) {
                unlockedShipClasses.AddUniqueInefficient(ShipClassDef.Unclassified);
                // unlock all classes that were required to get to this class
                var cl = playerShip.Class;
                int attempts = 1000;
                while (cl != null && attempts-- > 0) {
                    // D.Warn("Unlocking ship class due to player owning it: {0}", cl);
                    unlockedShipClasses.AddUniqueInefficient(cl);
                    cl = cl.RequiredClass;
                }
                if (attempts == 0) {
                    D.Err("Ran out of attempts while loading unlocked ship classes. Cyclic dependency? Last class: {0}", cl);
                }
            }

            foreach (var ship in Ships) {
                ship.BehaviorSector?.OnLoad(ship);
                ship.BehaviorRegion?.OnLoad(ship);
                // every ship exists by now, so kill attribution can be resolved
                ship.CombatState?.ResolveLastAttacker(S);
                NPCShipGen.ValidateShipEngineCounts(ship, true);
            }
            S.Query.FindShipDevice.SetResponder(FindShipDevice);
            // has to be called before battle stations
            Ready.Set(WhenH.ShipDataRestored, true);

            ValidatePlayerShip(true);
            ShipCombatAI.RecalculateBattleStationAssignments(playerShip.CombatState);
            foreach (var bs in playerShip.CombatState.BattleStations) {
                bs.UpdateWarning();
            }
            // Ship combat restore goes last and is invoked rather than gated: it decides
            // which ships are still fighting, and player validation above can change that
            // answer by leaving the player's ship derelict.
            S.Query.RestoreShipCombatData.Ask();
            // CheckPlayerClass(suggestUpgrade: false);
        }

        private void OnPlayerHyperjump(SpaceObject from, SpaceObject to) {
            var ship = S.Universe.Player.Ship;
            UpdateShipSector(ship, from.Sector, to.Sector);
        }

        // Public for console command / tests
        public void OnNPCShipEnterSector(NPCShip ship, SpaceSector sector, bool entered) {
            var isPlayer = ship.IsPlayer;
            if (!entered) {
                ship.BehaviorSector?.OnEnd(ship);
                ship.BehaviorSector = null;
                // ship just jumped
                CreateHyperTrail(ship, sector);
                // Its local position belonged to the sector it left
                ship.IsPlacedInSector = false;
                if (!isPlayer) {
                    // unparent the ship SO
                    var so = ship.SO;
                    if (so.Parent != null) {
                        so.Parent.RemoveChild(so);
                        S.Sig.SpaceObjectRemoved.Send(so);
                    }
                }
                return;
            }

            if (!isPlayer && sector == playerShip.Sector) {
                var so = ship.SO;
                sector.SO.AddChild(so);
                S.Sig.SpaceObjectAdded.Send(so);
            }

            // ship entered
            RemoveHyperTrail(ship, sector);

            // react to other ships
            if (!SectorShips.TryGetValue(sector, out var shipsInSector)) {
                #if !PRODUCTION_BUILD
                if (isPlayer && The.TestsAreRunning && Ships.Count == 1) {
                    // solo ship
                    return;
                }
                #endif
                D.Err("No ship list in sector: {0} -> {1} (Entered: {2})",
                    sector, ship, entered);
                return;
            }
            // existing ships notice the new ship
            float ignoreChanceMul = 1f;
            foreach (var s in shipsInSector.OrderBy(s => Rng.UValue)) {
                if (s.IsInHyperspace) { continue; }
                if (isPlayer && s.SO.Parent == null) {
                    sector.SO.AddChild(s.SO);
                }
                if (s == ship) { continue; }
                if (s.Notice(ship, ignoreChanceMul)) {
                    // increase chance of the next ship ignoring us
                    ignoreChanceMul *= 2.0f;
                    #if !PRODUCTION_BUILD
                    if (!s.IsPlayer && ship.IsPlayer) {
                        D.Warn("Ship reacting to player: {0} => {1}", s, s.BehaviorSector);
                    }
                    #endif
                    if (Rng.UChance(0.7f)) {
                        break;
                    }
                }
            }
            ship.NoticeMany(shipsInSector);

            if (!ship.IsPlayer) {
                if (ship.BehaviorSector == null && sector == playerShip.Sector) {
                    shipGen.AssignSectorBehavior(ship, true, Rng.Unseeded);
                }
            }
        }

        private bool NPCShipJumpTo(NPCShip ship, SpaceSector sector) {
            if (!Ready.StateLock) {
                D.Err("NPC ship jumping while state lock not ready! {0}", ship);
                return false;
            }
            if (!ship.IsInHyperspace) {
                D.Err("Can't jump to a location while not in hyperspace: {0}->{1}",
                    sector, this);
                return false;
            }
            if (sector == ship.Sector) {
                D.Err("Can't jump to same sector: {0}->{1}",
                    sector, this);
                return false;
            }
            var region = ship.Sector.Region;
            if (region != sector.Region) {
                ship.Memory.PreviousRegion = region;
            }
            var prev = ship.Sector;
            ship.Memory.PreviousSector = prev;
            ship.Sector = sector;
            UpdateShipSector(ship, prev, sector);
            if (UIShowing.Starmap &&
                    ((sector.Region.SO == A.StarmapCtx.ParentSO) ||
                    (region.SO == A.StarmapCtx.ParentSO))) {
                D.Warn("Moved a ship in same region as starmap view: {0}->{1}",
                    ship, sector);
                S.UIQuery.RebuildStarmap.Ask(false, false);
            }
            return true;
        }

        public void DepopulateUniverse(bool keepPlayer) {
            NPCShip player = null;
            for (int i = Ships.Count - 1; i >= 0; i--) {
                NPCShip ship = Ships[i];
                if (keepPlayer && ship.IsPlayer) {
                    player = ship;
                    continue;
                }
                RemoveShip(ship);
            }
            Ships.Clear();
            SectorShips.Clear();
            if (keepPlayer) {
                Ships.Add(player);
                UpdateShipSector(player, null, player.Sector);
            }
        }

        public void PopulateUniverse(Universe u) {
            var rng = new Rng(Hashes.Quick(S.SeedSpace, Primes.Prime020));
            int spawned = 0;
            Curve diffDensityCurve = Tunables.NPCShipDensityDifficultyMul;
            Dictionary<ShipClassDef, int> classCounts = new();
            foreach (var region in u.Root.Children) {
                foreach (var sector in region.Children) {
                    if (sector.SpaceEntity.Biome.Template is SectorTemplate secTpl) {
                        var wantedShips = rng.Curve(secTpl.NPCShipDensityCurve);
                        wantedShips *= diffDensityCurve.Evaluate(
                            sector.SpaceEntity.Biome.Difficulty);
                        if (sector.Faction != null) {
                            wantedShips *= sector.Faction.ShipDensityMul;
                        }
                        for (int i = 0; i < wantedShips; i++) {
                            var ship = SpawnShipAt(sector);
                            classCounts.GetIncrement(ship.Class);
                            spawned++;
                            // D.Warn("Spawned ship: {0} at {1}",
                            //     ship, sector.ParentPath(false));
                        }
                    }
                }
            }

            D.Warn("Spawned {0} ships", spawned);
            D.Warn("ClassCounts: {0}", D.DumpCollection(
                classCounts.OrderByDescending(cc => cc.Value).ToArray()));
        }

        public void MigratePlayerShip(NPCShip ship) {
            ship.Sector = (S.Universe.HyperspaceLocation ?? S.Universe.Player.Parent).Sector;
            ship.SO = S.Universe.Player;
            S.Universe.Player.Ship = ship;
            Ships.Insert(0, ship);
            UpdateShipSector(ship, null, ship.Sector);
            playerShip = ship;
        }

        public void CreatePlayerShip() {
            var ship = new NPCShip();
            ship.Faction = FactionDef.Player;
            ship.Name = S.GameName;
            // exploration & survey vessel as default
            ship.Tag = ShipTagDef.Get("ESV");
            ship.Sector = (S.Universe.HyperspaceLocation ?? S.Universe.Player.Parent).Sector;
            ship.Class = ShipClassDef.Unclassified;
            ship.State = ShipStateFlags.Active;
            ship.WillTrade = true;
            ship.Id = 1;
            ship.SO = S.Universe.Player;
            S.Universe.Player.Ship = ship;
            var size = S.Sys.Areas.ShipSize;
            if (size == 0) {
                size = 500;
            }
            ship.EnergyProduction = (int) S.Situation.GridMaxOutput;
            ship.LoadSize(size);
            ShipCombatState.CreateFor(ship);
            ship.CombatState.WeaponSettings = ShipWeaponSettings.Default;
            Ships.Insert(0, ship);
            UpdateShipSector(ship, null, ship.Sector);
            playerShip = ship;
        }

        private bool HasNPCShipsAt(SpaceObject so) {
            if (so.Region != null) {
                foreach (var child in so.Region.Sectors) {
                    if (HasShipIn(child)) {
                        return true;
                    }
                }
            } else if (so.Sector != null) {
                return HasShipIn(so.Sector);
            }
            return false;
        }

        private bool HasShipIn(SpaceSector sector) {
            if (SectorShips.TryGetValue(sector, out var list)) {
                return SOUtils.HasNonPlayerShips(list);
            }
            return false;
        }

        private List<NPCShip> GetNPCShipsAt(SpaceSector sector) {
            if (SectorShips.TryGetValue(sector, out var list)) {
                return list;
            }
            return null;
        }

        private void OnHyperjump(HyperdriveComp drive) {
            // if we move other ships immediately, it will interfere with
            // player's marker animation
            S.Clock.ExecuteAfterFramesForUI(3, AfterPlayerHyperjump);
        }

        private void AfterPlayerHyperjump() {
            // reset turn timer if not much time left
            if (G.Ticks - nextTurnTick < Consts.TicksPerHour) {
                nextTurnTick = G.Ticks +
                    Tunables.ShipTrafficTurnHours * Consts.TicksPerHour;
            }
            DoTurnRegion();
        }

        private void OnTickProgress(float progress) {
            // tick ships in player's local space
            var playerSector = A.PlayerSector;
            if ((playerSector?.HasChildren) != true) {
                return;
            }
            for (int i = playerSector.Children.Count - 1; i >= 0; i--) {
                SpaceObject child = playerSector.Children[i];
                if (child.Ship != null) {
                    child.Ship.OnTickProgress(progress);
                }
            }
        }

        private void OnTick(long ticks) {
            // tick ships in player's local space
            var playerSector = A.PlayerSector;
            if ((playerSector?.HasChildren) != true) {
                return;
            }
            if (ticks % Consts.TicksPer15Minutes == 2) {
                for (int i = Ships.Count - 1; i >= 0; i--) {
                    NPCShip ship = Ships[i];
                    ship.OnTick(ticks);
                }
            } else {
                for (int i = playerSector.Children.Count - 1; i >= 0; i--) {
                    SpaceObject child = playerSector.Children[i];
                    if (child.Ship != null) {
                        child.Ship.OnTick(ticks);
                    }
                }
            }

            TickHyperTrails(ticks, playerSector.Sector);

            if (ticks % Consts.TicksPer30Minutes == 21 && !UIShowing.FullScreenWidget) {
                UpdatePlayerShipEnergy();
                ValidatePlayerShip(false);
            }
            if (ticks % Consts.TicksPerDay == 0) {
                CheckPlayerClass(suggestUpgrade: true);
            }
        }

        private void ValidatePlayerShip(bool isLoad) {
            var isOnePiece = S.Sys.ShipNav.IsInOnePiece;
            var ship = A.Player.Ship;
            if (isLoad) {
                ship.CombatState.ValidatePlayerDevices();
            }
            var isDerelict = !isOnePiece ||
                    (ship.CombatState.Weapons.Count == 0
                        && ship.CombatState.Shields.Count == 0
                        && (ship.CombatState.Engines.Count == 0 || !S.Sys.ShipNav.HasBridgeControls()));

            if (!isDerelict && ship.IsDerelict) {
                ship.State &= ~ShipStateFlags.Derelict;
                A.Player.ResetLooks();
                A.Player.UI?.UpdateLooks();
            } else if (isDerelict && !ship.IsDerelict) {
                ship.State |= ShipStateFlags.Derelict;
                A.Player.ResetLooks();
                A.Player.UI?.UpdateLooks();
            }
        }

        private void CheckPlayerClass(bool suggestUpgrade) {
            if (playerShip.IsDerelict || A.IsInHyperspace) { return; }
            if (!playerShip.Class.IsCompatibleWithPlayer(S, false)) {
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning && !S.Vars.IsShowingCutscene) { return; }
                #endif
                ShowShipClassChoice(true);
            } else if (suggestUpgrade
                    && !S.Scenario.IsSandbox // suggestions skipped in sandbox
                    && !S.Vars.IsShowingCutscene
                    && ShipClassDef.IsBetterClassAvailableForPlayer(S)) {
                S.Query.TryAddSuggestion.Ask(SuggestionChangeShipClass.SuggId, false);
            }
        }

        public void ShowShipClassChoice(bool force) {
            var compatible = ShipClassDef.AllOrdered
                .Where(cl => cl.IsCompatibleWithPlayer(S, false))
                .ToList();
            if (force) {
                D.Warn("Forcing ship class choice!");
                S.UIQuery.ForceShowShipClassChoice.Ask(compatible, OnForceChooseShipClass);
            } else {
                S.UIQuery.ShowShipClassChoice.Ask(compatible, OnForceChooseShipClass);
            }
        }

        private void OnForceChooseShipClass(ShipClassDef shipClass) {
            if (shipClass == null) {
                D.Err("Forced ship class choice returned null!");
                return;
            }
            SetPlayerShipClass(shipClass);
        }

        public void SetPlayerShipClass(ShipClassDef shipClass) {
            var previousClass = playerShip.Class;
            playerShip.Class = shipClass;
            if (previousClass != shipClass) {
                PlayStats.TrackShipClassChange(shipClass);
                ShipAchievements.OnShipClassChosen(shipClass);
                S.Sig.PlayerShipClassChanged.Send();
            }
            A.Player.ResetLooks();
            var ui = A.Player.UI;
            if (ui != null) { ui.UpdateLooks(); }
        }

        private void TickHyperTrails(long ticks, SpaceSector playerSector) {
            int timeout = Mathf.RoundToInt(Tunables.HyperTrailTimeoutHours * Consts.TicksPerHour);
            for (int i = HyperTrails.Count - 1; i >= 0; i--) {
                ShipHyperTrail tr = HyperTrails[i];
                if (ticks - tr.CreatedAt > timeout) {
                    RemoveHyperTrail(tr, i);
                }
            }
            if (playerSector == null) { return; }
            if (!A.IsInHyperspace && SectorHyperTrails.TryGetValue(playerSector, out var trails)) {
                foreach (var trail in trails) {
                    trail.UpdateLooks(ticks, timeout);
                }
            }
        }

        public void DoTurnRegion() {
            for (int i = Ships.Count - 1; i >= 0; i--) {
                NPCShip ship = Ships[i];
                ship.DoTurnRegion();
            }
        }

        private NPCShip SpawnShipAt(SpaceObject origin) {
            return SpawnShipTemplateAt(origin, null);
        }

        private NPCShip SpawnShipTemplateAt(SpaceObject origin, NPCShip template) {
            var ship = shipGen.Generate(origin, S.Rng, template, SectorShips);
            if (ship.WantedWeaponsTier > 0) {
                EnsureOpponentHasWeapon(ship);
            }
            ship.Id = ++LastShipId;
            Ships.Add(ship);
            if (!ship.IsInHyperspace &&
                    ship.Sector.SO.Children != null) {
                ship.Sector.SO.AddChild(ship.SO);
                S.Sig.SpaceObjectAdded.Send(ship.SO);
                ship.PlaceInSector(SOUtils.RandomLocationInside(ship
                    .Sector.SO, S.Rng));
            }
            // D.Warn("Ship spawned: {0}", ship);
            S.Sig.NPCShipSpawned.Send(ship);
            UpdateShipSector(ship, null, ship.Sector);
            return ship;
        }

        public void RemoveShip(NPCShip ship) {
            if (ship == null) {
                return;
            }
            // D.Warn("Removing ship: {0}", ship);
            var isPlayer = ship.IsPlayer;
            if (SectorShips.TryGetValue(ship.Sector, out var list)) {
                if (!list.Remove(ship)) {
                    D.Err("Couldn't remove ship from sector list: {0}", ship);
                }
            } else {
                D.Err("Couldn't find ship sector list: {0}", ship.Sector);
            }
            if (isPlayer) {
                ship.SO.Ship = null;
                ship.SO = null;
                playerShip = null;
            } else {
                SOUtils.RemoveSO(ship.SO);
            }
            if (!Ships.Remove(ship)) {
                D.Err("Couldn't remove ship from list: {0}", ship);
            }
        }

        // Stations and derelicts included - they are ships too
        public void RemoveShipsIn(SpaceSector sector) {
            if (!SectorShips.TryGetValue(sector, out var ships)) { return; }
            for (int i = ships.Count - 1; i >= 0; i--) {
                var ship = ships[i];
                if (ship.IsPlayer) { continue; }
                RemoveShip(ship);
            }
        }

        public void UpdateShipSector(NPCShip ship, SpaceSector prev, SpaceSector next) {
            if (prev != null && prev != next) {
                if (SectorShips.TryGetValue(prev, out var list)) {
                    list.Remove(ship);
                }
            }
            // just remove
            if (next == null) {
                if (prev == null) {
                    D.Err("Cannot update ship sector from null to null! {0}", ship);
                }
                return;
            }
            if (!SectorShips.TryGetValue(next, out var list2)) {
                list2 = new List<NPCShip>();
                SectorShips.Add(next, list2);
            }
            list2.Add(ship);
            if (prev != null && next != null && prev != next) {
                UpdateHyperTrail(ship, prev, next);
                S.Sig.NPCShipMovedInHyperspace.Send(ship, prev, next);
            }
        }
        public ShipHyperTrail FindTrail(NPCShip ship, SpaceSector from) {
            if (from == null) { return null; }
            if (SectorHyperTrails.TryGetValue(from, out var list)) {
                if (!Arrays.IsEmpty(list)) {
                    for (int i = list.Count - 1; i >= 0; i--) {
                        ShipHyperTrail tr = list[i];
                        if (tr.Ship == ship) {
                            return tr;
                        }
                    }
                }
            }
            return null;
        }
        public void UpdateHyperTrail(NPCShip ship, SpaceSector prev, SpaceSector next) {
            var trail = FindTrail(ship, prev);
            if (trail != null) {
                trail.To = next;
            }
        }

        private void RemoveHyperTrail(NPCShip ship, SpaceSector sector) {
            var trail = FindTrail(ship, sector);
            if (trail != null) {
                RemoveHyperTrail(trail);
            }
        }

        public ShipHyperTrail CreateHyperTrail(NPCShip ship, SpaceSector sector) {
            if (sector == null) {
                D.Err("Can't create trail for null sector!");
                sector = ship.SO.Parent?.Sector;
            }
            // if (sector == A.Player.Parent?.Sector) {
            //     D.Warn("Creating hypertrail for ship in player sector: {0} to {1}",
            //         ship, sector);
            // }
            if (sector == null) {
                D.Err("Refusing to create hypertrail for null sector");
                return null;
            }
            var trail = FindTrail(ship, sector);
            if (trail == null) {
                trail = new ShipHyperTrail(ship, sector);
                if (ship.SO.Parent?.Sector != null) {
                    // this ship has an actual position
                    trail.Location = ship.SO.Location;
                }
                AddHyperTrail(trail);
                if (!ship.IsPlayer && A.Player.Parent == sector.SO) {
                    AddTrailSO(sector.SO, trail);
                }
            } else {
                if (ship.SO.Parent?.Sector != null) {
                    // this ship has an actual position
                    trail.Location = ship.LastKnownPosition;
                    if (trail.SO != null) {
                        trail.SO.Location = trail.Location;
                    }
                }
            }
            trail.To = null;
            trail.CreatedAt = G.Ticks;
            return trail;
        }

        private void AddHyperTrail(ShipHyperTrail trail) {
            HyperTrails.Add(trail);
            if (!SectorHyperTrails.TryGetValue(trail.From, out var list)) {
                list = new();
                SectorHyperTrails.Add(trail.From, list);
            }
            list.Add(trail);
        }

        private void RemoveHyperTrail(ShipHyperTrail trail, int listIdx = -1)  {
            if (listIdx == -1) {
                HyperTrails.Remove(trail);
            } else {
                HyperTrails.RemoveAt(listIdx);
            }
            bool removedSectorTrail = false;
            if (SectorHyperTrails.TryGetValue(trail.From, out var list)) {
                removedSectorTrail = list.Remove(trail);
            }
            if (!removedSectorTrail) {
                D.Err("Couldn't remove trail from sector list: {0}",
                    trail.From);
            }
            if (trail.SO != null) {
                SOUtils.RemoveSO(trail.SO);
                trail.SO = null;
            }
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.NPCShip ??= new NPCShipSysData();
            sd.NPCShip.LastShipId = LastShipId;
            sd.NPCShip.GenVersion = NPCShipSysData.CurrentGenVersion;
            var ships = sd.NPCShip.Ships ??= new();
            ships.Clear();
            foreach (var ship in Ships) {
                var shipData = NPCShipData.Serialize(ship);
                if (shipData != null) {
                    ships.Add(shipData);
                }
            }

            sd.NPCShip.UnlockedClasses = new();
            foreach (var cl in unlockedShipClasses) {
                sd.NPCShip.UnlockedClasses.Add(cl.Id);
            }

            var trails = sd.NPCShip.HyperTrails ??= new();
            trails.Clear();
            foreach (var secTr in SectorHyperTrails.Values) {
                foreach (var tr in secTr) {
                    if (ShipHyperTrailData.TrySerialize(tr, out var data)) {
                        trails.Add(data);
                    }
                }
            }

            var reactionCooldowns = sd.NPCShip.ReactionCooldowns ??= new();
            reactionCooldowns.Clear();
            foreach (var (reactionIdH, tick) in reactionGlobalCooldowns) {
                reactionCooldowns[reactionIdH] = tick;
            }
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            data = sd.NPCShip;
            if (data != null) {
                LastShipId = data.LastShipId;
            }
            if (LastShipId == 0) {
                // 1 is reserved for player's ship
                LastShipId = 1;
            }

            mustRegenNPCShipLoadouts = data != null &&
                data.GenVersion < NPCShipSysData.CurrentGenVersion;
            skipLoadoutRegenShipIds.Clear();
            if (mustRegenNPCShipLoadouts) {
                AddNonPlayerLoadoutRegenSkipIds(sd.ShipCombat?.Enemies);
                AddNonPlayerLoadoutRegenSkipIds(sd.ShipCombat?.Allies);
                D.Log("Will regenerate NPC ship loadouts. Gen version: {0}->{1}",
                    data.GenVersion, NPCShipSysData.CurrentGenVersion);
            }

            reactionGlobalCooldowns.Clear();
            if (data?.ReactionCooldowns != null) {
                foreach (var (reactionIdH, tick) in data.ReactionCooldowns) {
                    reactionGlobalCooldowns[reactionIdH] = tick;
                }
            }

            unlockedShipClasses.Clear();
            if (!Arrays.IsEmpty(data?.UnlockedClasses)) {
                foreach (var cl in data.UnlockedClasses) {
                    var def = ShipClassDef.Get(cl);
                    if (def == null) {
                        D.Err("Failed loading unlocked ship class by id: {0}", cl);
                    } else {
                        unlockedShipClasses.Add(def);
                    }
                }
            }
        }

        private void AddNonPlayerLoadoutRegenSkipIds(List<uint> shipIds) {
            if (shipIds == null) { return; }
            foreach (var shipId in shipIds) {
                if (shipId == 1) { continue; }
                skipLoadoutRegenShipIds.Add(shipId);
            }
        }

        public void LoadObjectsFor(SpaceObject obj) {
            var sec = obj.Sector;
            if (sec == null) {
                D.Err("Loading Objects for sector that doesn't have .Sector: {0}", obj);
                return;
            }
            if (SectorShips.TryGetValue(sec, out var ships)) {
                if (!Arrays.IsEmpty(ships)) {
                    foreach (var ship in ships) {
                        if (!ship.IsInHyperspace && !ship.IsPlayer && obj.TryAddChild(ship.SO)) {
                            // Ships generated with the sector (trade zone station ring,
                            // derelict zone) or that exited hyperspace here already have
                            // a position. Randomizing it piled stations on top of each
                            // other (STA-1520)
                            if (!ship.IsPlacedInSector) {
                                ship.PlaceInSector(SOUtils.RandomLocationInside(obj, S.Rng));
                            }
                            S.Sig.SpaceObjectAdded.Send(ship.SO);
                        }
                    }
                }
            }
            if (SectorHyperTrails.TryGetValue(sec, out var trails)) {
                if (!Arrays.IsEmpty(trails)) {
                    foreach (var trail in trails) {
                        AddTrailSO(obj, trail);
                    }
                }
            }
        }

        private void AddTrailSO(SpaceObject obj, ShipHyperTrail trail) {
            var so = trail.GetSOFor(obj);
            if (obj.TryAddChild(so)) {
                S.Sig.SpaceObjectAdded.Send(so);
            }
        }

        public NPCShip Migration_CovertSpaceStation(SpaceObject station) {
            var tpl = new NPCShip();
            tpl.Class = ShipClassDef.FastGet(ShipClassIdH.SpaceStation);
            tpl.SO = station;
            tpl.Name = station.Name;
            tpl.Faction = station.Faction;
            var ship = shipGen.Generate(station.Parent, S.Rng, tpl, SectorShips);
            ship.Id = ++LastShipId;
            Ships.Add(ship);
            // D.Warn("Space station converted to ship: {0}", ship);
            S.Sig.NPCShipSpawned.Send(ship);
            UpdateShipSector(ship, null, ship.Sector);
            return ship;
        }
        public NPCShip Migration_CovertDerelict(SpaceObject derelict) {
            // D.Warn("Converting derelict to ship: {0}", derelict);
            var tpl = new NPCShip();
            tpl.SO = derelict;
            var ship = shipGen.Generate(derelict.Parent, S.Rng, tpl, SectorShips);
            ship.Id = ++LastShipId;
            Ships.Add(ship);
            ship.TurnIntoDerelict(null);
            // D.Warn("Derelict converted to ship: {0}", ship);
            S.Sig.NPCShipSpawned.Send(ship);
            UpdateShipSector(ship, null, ship.Sector);
            return ship;
        }

        public void SetBestPlayerShipClass() {
            ShipClassDef best = null;
            foreach (var cl in ShipClassDef.AllOrdered) {
                if (cl.IsCompatibleWithPlayer(S, true)) {
                    best = cl;
                }
            }
            D.Warn("Best class for player ship: {0}", best);
            S.Universe.Player.Ship.Class = best;
        }
    }
}
