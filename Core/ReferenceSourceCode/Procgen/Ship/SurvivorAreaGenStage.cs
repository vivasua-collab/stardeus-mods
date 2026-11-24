using KL.Grid;
using Game.Data;
using System.Linq;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using Game.Components;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.InputSystem;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using KL.Randomness;
using Game.Modifiers;
using Game.Data.Factions;
using Game.Scenarios;
using Game.Systems.Creatures;
using System.Threading;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Naming;
using Game.Systems.UIUX;

namespace Game.Procgen {
    public class SurvivorAreaGenStage : BaseShipGenStage, IWithScenarioConfig {
        public override bool RequiresAreas => true;
        private readonly HashSet<int> engines = new();
        private readonly int fuel;
        private readonly int foodPerSurvivor;
        public bool SkipLifeSupportDevices;
        public bool GreetColonists;
        /// <summary>
        /// Faction of the survivors spawned on a derelict (isForDerelict). When null a
        /// random trading faction is picked as fallback. Non-hostile factions yield guests
        /// (Passenger role) instead of Player-faction colonists.
        /// </summary>
        public FactionDef SurvivorFaction;
        private bool isForDerelict;
        private GameSetupData config;

        public SurvivorAreaGenStage(int foodPerSurvivor, int fuel, bool isForDerelict) {
            this.foodPerSurvivor = foodPerSurvivor;
            this.fuel = fuel;
            this.isForDerelict = isForDerelict;
        }

        /// <summary>
        /// Placement rules to drop, strictest first, when no spot satisfies them all. Only
        /// applied on the derelict path - a derelict's factory runs with canStartOver: false
        /// (restarting it would clear the player's own ship too), so it cannot fall back to
        /// StartOver the way the WreckedEmptyShip starting-ship path does.
        /// </summary>
        [System.Flags]
        private enum Relax {
            None = 0,
            SmallAreas = 1,
            Doors = 2,
            EnginesAndDamage = 4,
        }

        private static readonly Relax[] derelictRelaxPasses = {
            Relax.None,
            Relax.SmallAreas,
            Relax.SmallAreas | Relax.Doors,
            Relax.SmallAreas | Relax.Doors | Relax.EnginesAndDamage,
        };

        public override void Apply() {
            engines.Clear();
            var engs = S.Components.GenFind<ShipEngineComp>();
            foreach (var eng in engs) {
                if (IsWithinGenArea(eng.Tile.CenterIdx)) {
                    engines.Add(eng.Entity.PosIdx);
                }
            }

            var ar = S.Sys.Areas;
            ar.NeedsRecalc(WorldLayer.Floor);
            S.Sig.ForceAreas.Send();

            if (isForDerelict) {
                foreach (var relax in derelictRelaxPasses) {
                    if (TryFindAndBuildSurvivorArea(relax)) { return; }
                }
                // Every relaxation still found nothing - keep the event's promise of
                // survivors without the camp structure rather than erroring out.
                BuildFallbackSurvivorPos();
                return;
            }

            if (TryFindAndBuildSurvivorArea(Relax.None)) { return; }
            StartOver = true;
            D.Err("Failed finding a spot for survivor area");
        }

        private bool TryFindAndBuildSurvivorArea(Relax relax) {
            var ar = S.Sys.Areas;
            int minAreaSize = relax.HasFlag(Relax.SmallAreas) ? 16 : 40;

            var goodAreas = new List<HashSet<int>>();
            var allAreas = ar.Areas.OrderBy(a => a.Value.Count).ToList();
            // Maybe we still have enclosed areas?
            foreach (var area in allAreas) {
                if (area.Value.Count < minAreaSize) { continue; }
                if (!relax.HasFlag(Relax.EnginesAndDamage) && !IsNotDamaged(area.Value)) {
                    continue;
                }
                if (IsWithinGenArea(area.Value.First())
                        && !spec.InvincibleParts.Contains(area.Value.First())) {
                    goodAreas.Add(area.Value);
                }
            }

            // the rooms RoomGrowthGenStage reserved first; the stasis array has taken one
            goodAreas = goodAreas
                .OrderByDescending(a => GenUtils.ContainsAny(a, spec.StasisRoomHints))
                .ThenBy(_ => rng.Value)
                .ToList();

            // Find a 4x4 spot to build the template in
            foreach (var area in goodAreas) {
                var aSorted = area.ToList();
                aSorted.Sort();
                aSorted = aSorted.OrderBy(r => rng.Value).ToList();
                foreach (var pos in aSorted) {
                    if (TryBuildSurvivorAreaAt(pos, aSorted, relax)) {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Last resort for a derelict where no spot survives even the most relaxed pass:
        /// drop the camp structure (beds, reactor, planter, power) entirely and just place
        /// survivors and their resources on any empty floor, so the story event still
        /// delivers the survivors it promised instead of an empty derelict.
        /// </summary>
        private void BuildFallbackSurvivorPos() {
            var pos = FindBeingSpawnPos();
            if (pos <= 0) {
                D.Err("Failed finding a spot for survivor area");
                return;
            }
            var areaId = S.Sys.Areas.At(pos);
            IEnumerable<int> pumpArea = S.Sys.Areas.Areas.TryGetValue(areaId, out var areaSet)
                ? areaSet
                : new[] { pos };
            PumpAtmosphereAt(pumpArea);

            int survivors = SpawnSurvivors(pos);
            spec.SetSurvivorPos(pos);
            SpawnResources(pos, survivors);
            D.Warn("{0} No legal survivor camp spot found (seed {1}) - spawning survivors " +
                "without a camp at {2}", LogPrefix, spec.Seed, Pos.String(pos));
        }

        private bool TryBuildSurvivorAreaAt(int pos, List<int> room, Relax relax) {
            for (int x = 0; x < 4; x++) {
                for (int y = 0; y < 4; y++) {
                    var p = Pos.NeighborAt(pos, x, y);
                    if (!IsGoodPos(p, relax)) {
                        return false;
                    }
                }
            }

            if (!SkipLifeSupportDevices) {
                // D.Log("Trying to build survivor area at: {0}", pos);
                var areas = The.Defs.Matching(DefIds.TemplatesSurvivorArea01);
                var area = rng.From(areas);
                // If we're still here, bottom left is good start
                for (int x = 0; x < 4; x++) {
                    for (int y = 0; y < 4; y++) {
                        RemoveFloorLights(Pos.NeighborAt(pos, x, y), 1);
                    }
                }
                if (!TryBuildTemplate(pos, area)) {
                    return false;
                }
            }
            var expanded = ExpandArea(new HashSet<int>(room));

            MakeAreaInvincible(expanded, 2);

            CloseAllDoorsAndVents(expanded);

            BuildLightsAroundArea(pos);
            var reactor = RefillEmergencyReactor(pos);
            int reactorPos = pos;
            if (reactor == null) {
                D.Err("Failed finding the emergency reactor!");
            } else {
                reactorPos = reactor.Tile.Transform.InPort;
            }
            ConfigurePlanter(pos);

            ConnectDevicesIn(expanded);

            int attempts = 10;
            if (attempts < 1) {
                D.Err("Failed waiting for Atmo02 / AtmoTemp to be ready!");
            }
            PumpAtmosphereAt(room);

            int survivors = SpawnSurvivors(reactorPos);
            spec.SetSurvivorPos(reactorPos);

            SpawnResources(reactorPos, survivors);
            return true;
        }

        private void ConfigurePlanter(int pos) {
            var planter = S.Components.FindClosestMatching<PlanterComp>(
                EntityUtils.CenterOf(pos), true,
                    (p) => !p.Tile.Health.IsDamaged);
            if (planter == null) {
                D.Err("{0} Planter not found during gen stage!", LogPrefix);
            } else {
                planter.StartGrowing(DefIds.ObjPlantsWheat);
            }
        }

        private void SpawnResources(int pos, int survivors) {
            var targetFood = Mathf.RoundToInt(
                survivors * foodPerSurvivor / S.Difficulty.Level);
            var targetFuel = Mathf.RoundToInt(fuel / S.Difficulty.Level);

            if (targetFood > 0) {
                // D.Log("Spawning food at {0}", Pos.String(foodSpot));
                var pile = EntityUtils.SpawnRawMaterial(MatId.SurvivalMeal, targetFood,
                    pos, 0.25f, true, true);
                pile.IsForbidden = true;
            }

            if (targetFuel > 0) {
                int fuelPos = pos;
                Pos.ForEachAround(pos, 3, false, true, (p) => {
                    if (EntityUtils.IsEmptyFloorAt(p)) {
                        fuelPos = p;
                        return false;
                    }
                    return true;
                });
                // The emergency reactor's reserve, so it has to be what that reactor burns
                // (see RefillEmergencyReactor and the scenario's RefuelDevicesGenStage)
                var pile = EntityUtils.SpawnRawMaterial(MatId.ScrapMetal, targetFuel,
                    fuelPos, 0.25f, false, false);
                pile.IsForbidden = true;
            }
        }

        private int FindPosFor(BeingData data) {
            int attempts = 100;
            int pos = startingPos;
            var areas = S.Sys.Areas;
            int areaId = areas.At(pos);
            while (attempts-- > 0) {
                int spos = Pos.Invalid;
                Pos.ForEachAroundRandom(subRng, pos, 5, false, true, (p) => {
                    if (areas.At(p) != areaId) { return true; }
                    if (EntityUtils.IsEmptyFloorAt(p)) {
                        spos = p;
                        return false;
                    }
                    return true;
                });
                if (spos != Pos.Invalid) {
                    return spos;
                }
            }
            return startingPos;
        }

        private int startingPos;
        private Rng subRng;

        private int SpawnSurvivors(int pos) {
            startingPos = pos;
            subRng = rng.Fork();
            int spawned = 0;
            var bd = Caches.BeingDataList;
            bd.Clear();

            if (isForDerelict) {
                // A stocked survivor camp is rescue content, so an ownerless derelict falls
                // back to a trade (friendly) faction rather than a raid one - the raid pick
                // used to be here only because the faction was silently dropped and every
                // survivor ended up Player-faction regardless.
                var fac = SurvivorFaction
                    ?? S.Sys.Diplomacy.RandomFactionForTrade()
                    ?? FactionDef.FastGet(FactionIdH.Adventurer);
                // With combat disabled a hostile faction cannot act on its hostility, so
                // treat the camp as guests rather than leaving inert strangers aboard.
                bool isHostile = A.IsCombatOn
                    && S.Sys.Diplomacy.IsHostileTowardsPlayer(fac);
                var role = isHostile ? ColonyRole.Unassigned : ColonyRole.Passenger;
                // Biased towards people rather than robots for factions that have any -
                // see SurvivorSpecies. A stocked, life-supported camp reads oddly staffed
                // by robots either way, so this applies on both the guest and hostile branch.
                var weightedSpecies = SurvivorSpecies.WeightedFor(S, fac);
                for (int i = 0; i < 2; i++) {
                    if (!rng.TryWeightedFrom(weightedSpecies, out var sp)) { continue; }
                    var data = BeingUtils.GenBeingWithInventory(
                        sp.IdH, BeingType.Friendly, fac,
                            GenItemFlags.Casual | GenItemFlags.SpaceReady, role);
                    var b = S.Beings.Create(pos, data);
                    BeingUtils.UpdateFlagsFor(S, data);
                    if (!isHostile) {
                        // Being rescued is a relief only if they're not about to shoot us;
                        // likewise only guests socialise into the crew's relationships.
                        PassengerUtils.AddRescueBoostFor(data);
                        bd.Add(data);
                    }
                    spawned++;
                }
            } else {
                // We pre-generated clothing already in the
                var beings = ColonistGenStage.SpawnColonists(S, config, GenItemFlags.None, FindPosFor);
                if (!Arrays.IsEmpty(beings)) {
                    foreach (var b in beings) {
                        if (!b.Species.IsAnimal) {
                            b.Data.StatsBeing.AIAlignment.UpdateBase(subRng.Range(0.4f, 0.8f));
                            BeingUtils.UpdateFlagsFor(S, b.Data);
                        }
                        BeingUtils.AddInitialMentalBoostFor(b.Data, 7);
                        bd.Add(b.Data);
                        spawned++;
                    }
                } else {
                    return spawned;
                }
            }

            S.Query.GenerateRelationshipsFor.Ask(bd);
            return spawned;
        }

        private void ConnectDevicesIn(HashSet<int> area) {
            ElectricNodeComp connector = null;
            foreach (var p in area) {
                var node = EntityUtils.GetTopmostParentTile(p)?.ENode;
                // We don't want to connect to some randomly spawned connector
                if (node != null && node.Node.IsConnector
                        && node.Tile.DefinitionId == DefIds.ObjectsDevicesFloorSocket) {
                    connector = node;
                }
            }
            if (connector == null) {
                D.Err("Failed to find the connector in survivor area!");
                foreach (var p in area) {
                    var node = EntityUtils.GetTopmostParentTile(p)?.ENode;
                    if (node != null && !node.Node.IsConnector
                            && !node.Tile.Definition.IsImportant) {
                        A.Grid.TryAutoConnect(node, false);
                    }
                }
                return;
            }
            foreach (var p in area) {
                var node = EntityUtils.GetTopmostParentTile(p)?.ENode;
                if (node != null && !node.Node.IsConnector
                        && !node.Tile.Definition.IsImportant) {
                    A.Grid.TryConnect(connector, node, out _);
                }
            }
            // start producing electricity
            A.Grid.ExecuteTick(G.Ticks);
            // lights will shine after this
            A.Grid.ExecuteTick(G.Ticks);
        }

        private MatterReactorComp RefillEmergencyReactor(int pos) {
            var subrng = rng.Fork();
            for (int x = 0; x < 4; x++) {
                for (int y = 0; y < 4; y++) {
                    var p = Pos.NeighborAt(pos, x, y);
                    var obj = EntityUtils.ParentObjAt(p);
                    if (obj != null && obj.TryGetComponent<MatterReactorComp>(
                            out var reactor)) {
                        var scrap = MatType.Get(MatId.ScrapMetal);
                        var fuel = reactor.Entity
                            .GetComponent<RefillableStorageComp>();
                        fuel.SetTargetMaterial(scrap);
                        fuel.Fill(subrng.Range(30, 40));
                        return reactor;
                    }
                }
            }
            return null;
        }

        private void BuildLightsAroundArea(int pos) {
            int builtLights = 0;
            var subRng = rng.Fork();
            var areas = S.Sys.Areas;
            int areaId = areas.At(pos);
            Pos.ForEachAroundRandom(subRng, pos, 5, false, true, (p) => {
                if (areas.At(p) != areaId) { return true; }
                if (EntityUtils.IsEmptyFloorAt(p)) {
                    var floor = EntityUtils.FloorAt(p);
                    var lightDefId = $"{floor.DefinitionId}Lit";
                    var lightDef = The.Defs.TryGet(lightDefId);
                    if (lightDef != null) {
                        var light = EntityUtils.CreatePrototype<Tile>(
                            lightDefId);
                        BuildTile(p, light);
                        return builtLights++ <= 2;
                    }
                }
                return true;
            });
        }

        private void CloseAllDoorsAndVents(HashSet<int> area) {
            foreach (var pos in area) {
                var w = EntityUtils.WallAt(pos);
                if (w == null) { continue; }
                w.Door?.SetClosed(true);
                if (w.TryGetComponent<VentComp>(out var vent)) {
                    vent.SetClosed(true);
                }
            }
        }

        private bool IsNotDamaged(HashSet<int> area) {
            foreach (var pos in area) {
                var floor = EntityUtils.FloorAt(pos);
                if (floor?.Health.IsDamaged == true) {
                    return false;
                }
            }
            return true;
        }

        private bool IsGoodPos(int p, Relax relax) {
            if (S.Sys.Areas.IslandAt(p) == 0) { return false; }
            if (!IsWithinGenArea(p)) { return false ;}
            if (!relax.HasFlag(Relax.EnginesAndDamage)) {
                foreach (var e in engines) {
                    // Too close to an engine
                    if (Pos.SqrDistance(p, e) < 25) {
                        return false;
                    }
                }
            }
            if (spec.HasCPU) {
                if (S.Sys.Areas.IsSameAreaIndoors(spec.CPUPos, p)) {
                    return false;
                }
            }
            if (!EntityUtils.IsEmptyFloorAt(p, false)) {
                return false;
            }
            // not a derelict
            if (EntityUtils.IsWithinMargin(p, false, false)) {
                // too close to CPU would auto-connect it to the grid
                // and mess up the quests
                if (spec.HasCPU && Pos.SqrDistance(spec.CPUPos, p) < 6 * 6) {
                    return false;
                }
            }
            if (!relax.HasFlag(Relax.Doors)) {
                var isOK = true;
                Pos.ForEachAround(p, 1, true, true, (n) => {
                    if (GenUtils.IsDoor(n)) {
                        isOK = false;
                        return false;
                    }
                    return true;
                });
                if (!isOK) { return false; }
            }
            return true;
        }

        public void SetConfig(GameSetupData config) {
            this.config = config;
        }
    }
}
