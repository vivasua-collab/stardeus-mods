using KL.Grid;
using Game.Data;
using System.Linq;
using KL.Utils;
using Game.Constants;
using System.Collections.Generic;
using Game.Components;
using Game.Commands;
using UnityEngine;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Systems.Naming;
using KL.Randomness;
using Game.Scenarios;
using Game.Systems.UIUX;

namespace Game.Procgen {
    // 676781029 - no stasis array
    public class StasisAreaGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        private readonly int numWantedDeadBodies;
        private readonly float minIntegrity;
        private readonly bool doSomeDamageToTheArea;
        private readonly HashSet<int> engines = new();
        private readonly int minDistToCPU;
        private readonly int minDistToEngine;
        protected readonly StasisManifest manifest;
        private int inhabitedCount;

        // Humans and pets are separate ranges on purpose: the headcount of people is a
        // design number per scenario, pets come on top of it and never eat into it.
        public StasisAreaGenStage(
                int minHumans,
                int maxHumans,
                int minPets,
                int maxPets,
                int minDistToCPU,
                int minDistToEngine,
                int addDeadBodies = 0,
                float minIntegrity = 0.85f,
                bool doSomeDamageToTheArea = false)
            : this(new StasisManifest(minHumans, maxHumans, minPets, maxPets),
                minDistToCPU, minDistToEngine, addDeadBodies, minIntegrity,
                doSomeDamageToTheArea) {
        }

        public StasisAreaGenStage(
                StasisManifest manifest,
                int minDistToCPU,
                int minDistToEngine,
                int addDeadBodies = 0,
                float minIntegrity = 0.85f,
                bool doSomeDamageToTheArea = false) {
            this.manifest = manifest;
            this.minDistToCPU = minDistToCPU;
            this.minDistToEngine = minDistToEngine;
            this.doSomeDamageToTheArea = doSomeDamageToTheArea;
            this.numWantedDeadBodies = addDeadBodies;
            this.minIntegrity = minIntegrity;
        }

        // Sleepers chosen before generation. Null = roll them here from the stage rng.
        protected virtual List<StartingBeingData> PresetSleepers() {
            return null;
        }

        public override void Apply() {
            engines.Clear();
            var engs = S.Components.GenFind<ShipEngineComp>();
            foreach (var eng in engs) {
                if (!IsWithinGenArea(eng.Tile.PosIdx)) { continue; }
                engines.Add(eng.Entity.PosIdx);
            }
            StartOver = false;

            var areas = S.Sys.Areas;

            // All islands that are non-cpu ones
            var islands = areas.Islands.Where(i => {
                    if (!IsWithinGenArea(i.Value.First())) { return false; }
                    if (spec.HasCPU && i.Key == areas.IslandAt(spec.CPUPos)) {
                        return false;
                    }
                    return true;
                })
                .OrderByDescending(i => i.Value.Count).ToList();

            // then the CPU's own island. Trying it only when no other island existed let a
            // stray scrap of floor stand for the whole ship, and generation started over.
            var rest = areas.Islands
                .Where(i => !islands.Contains(i))
                .OrderByDescending(i => i.Value.Count)
                .ToList();
            islands.AddRange(rest);

            int startingPoint = Pos.Invalid;
            foreach (var startingIsland in islands) {
                var startingPosRect = Pos.Encapsulate(startingIsland.Value);

                var areasInStartingIsland = areas.Areas.Where(a =>
                    // only areas bigger than 16 tiles
                    a.Value.Count > 16 &&
                    areas.IslandAt(a.Value.First()) == startingIsland.Key)
                        // the rooms RoomGrowthGenStage reserved for arrays, then the biggest
                        .OrderByDescending(a => GenUtils.ContainsAny(a.Value, spec.StasisRoomHints))
                        .ThenByDescending(a => a.Value.Count);

                foreach (var area in areasInStartingIsland) {
                    var potentialStarts = new HashSet<int>(
                        area.Value.Where(i => IsGoodStart(i)));
                    if (TryBuildStasisArrayAt(potentialStarts)) {
                        break;
                    }
                }

                if (inhabitedCount > 0) { break; }

                // There were no surviving areas in the island, or we couldn't build
                var startingIslandOrdered = new List<int>(startingIsland.Value);
                startingIslandOrdered.Sort();

                // Must have .ToList() or OrderBy(p => rng.value).FirstOrDefault(..)
                // will return unpredictable result
                var starts = startingIslandOrdered
                    .Where(p => IsGoodStart(p)).OrderBy(p => rng.Value).ToList();

                if (starts.Count == 0) { continue; }

                foreach (var start in starts) {
                    // Lower left corner
                    if (TryBuildStasisArrayAt(new HashSet<int>() { start })) {
                        break;
                    }
                }
                if (inhabitedCount > 0) { break; }
            }

            if (inhabitedCount == 0 && !spec.CanStartOver) {
                // Nobody would honor StartOver here (a derelict's factory), and the ship
                // would go without its array: a small hull often has no free 5x5 spot
                CarveSpaceForArray();
            }

            if (inhabitedCount == 0) {
                #if !PRODUCTION_BUILD
                The.ScreenBlock.HideImmediate();
                //D.Break();
                #endif
                StartOver = true;
            }
        }

        // Clears the spot that needs the least work for a free 5x5 (the array and a ring
        // around it): walls and objects go, floors are laid or replaced. Inside the hull
        // first, so the room keeps its walls. Never takes the ship computer or an engine.
        private void CarveSpaceForArray() {
            var candidates = new List<int>();
            foreach (var island in S.Sys.Areas.Islands) {
                foreach (var p in island.Value) {
                    if (IsWithinGenArea(p)) {
                        candidates.Add(p);
                    }
                }
            }
            candidates.Sort();
            var best = new List<int>();
            int bestOutside = int.MaxValue;
            int bestWork = int.MaxValue;
            foreach (var p in candidates) {
                if (!TryScoreCarve(p, out var outside, out var work)) { continue; }
                if (outside > bestOutside || (outside == bestOutside && work > bestWork)) {
                    continue;
                }
                if (outside < bestOutside || work < bestWork) {
                    best.Clear();
                    bestOutside = outside;
                    bestWork = work;
                }
                best.Add(p);
            }
            if (best.Count == 0) {
                D.Err("No spot to carve for a stasis array in {0} candidate tiles", candidates.Count);
                return;
            }
            var center = rng.From(best);
            var floor = StasisFloorDef();
            Pos.ForEachAround(center, 2, true, true, (n) => {
                if (EntityUtils.WallAt(n) != null) {
                    new CmdRemoveTile(n, WorldLayer.Walls).Execute(S);
                }
                var obj = EntityUtils.ParentObjAt(n);
                if (obj != null) {
                    new CmdRemoveTile(obj).Execute(S);
                }
                var fl = EntityUtils.FloorAt(n);
                var proto = EntityUtils.CreatePrototype<Tile>(floor.Id);
                if (fl == null) {
                    BuildTile(n, proto);
                } else if (!fl.Integrity.CanHaveFullIntegrity) {
                    ReplaceTile(n, proto);
                }
                return true;
            });
            S.Sig.ForceAreas.Send();
            D.Warn($"Carved space for a stasis array at {Pos.String(center)}: {bestWork} tiles cleared, {bestOutside} of them hull or open space");
            if (!TryBuildStasisArrayAt(new HashSet<int> { center })) {
                D.Err("Could not build a stasis array on the spot carved for it at {0}",
                    Pos.String(center));
            }
        }

        // outside: tiles of open space or hull, work: tiles to clear or floor
        private bool TryScoreCarve(int p, out int outside, out int work) {
            int o = 0;
            int w = 0;
            bool ok = true;
            Pos.ForEachAround(p, 2, true, true, (n) => {
                if (Pos.IsOOB(n) || !IsWithinGenArea(n)) {
                    ok = false;
                    return false;
                }
                var obj = EntityUtils.ParentObjAt(n);
                if (obj != null && (obj.HasComponent<ShipComputerComp>()
                        || obj.HasComponent<ShipEngineComp>())) {
                    ok = false;
                    return false;
                }
                var wall = EntityUtils.WallAt(n);
                if (wall == null ? EntityUtils.FloorAt(n) == null : IsOuterWall(n)) {
                    o++;
                }
                if (!EntityUtils.IsEmptyFloorWithIntegrityAt(n)) {
                    w++;
                }
                return true;
            });
            outside = o;
            work = w;
            return ok;
        }

        private Def StasisFloorDef() {
            var defs = The.Defs.Matching(DefIds.StructureFloorsFloorReinforced01);
            return new Rng(spec.Seed).From(defs);
        }

        private void InhabitArray(StasisArrayComp array) {
            int maxIterations = 10;
            int addedBodies = 0;
            var subRng = rng.Fork();
            // Inhabit the arrays
            var humans = subRng.Range(manifest.MinHumans, manifest.MaxHumans + 1);
            if (humans == 0) {
                D.Err("Sleeping beings 0!");
                humans = ManifestSys.DefaultHumans;
            }
            var pets = subRng.Range(manifest.MinPets, manifest.MaxPets + 1);
            array.Stability = subRng.Range(minIntegrity, Mathf.Clamp01(minIntegrity + 0.2f));
            var presets = PresetSleepers();
            if (presets != null && presets.Count > 0) {
                // The people the player already met on the Custom Start screen. The rolls
                // above still happen so the rest of the stage rng stays where it was.
                ManifestSys.For(S).Populate(array, presets, subRng.Fork());
            } else {
                ManifestSys.For(S).Populate(array, humans, pets, subRng.Fork());
            }
            inhabitedCount = array.BeingCount;
            while (addedBodies < numWantedDeadBodies && maxIterations-- > 0) {
                Pos.ForEachAroundRandom(subRng, array.Tile.Transform.OutPort, 3, true, true, (p) => {
                    if (!EntityUtils.IsEmptyFloorAt(p)) { return true; }
                    var vars = new GenBeingVars(Species.Get(SpeciesId.Human), BeingType.Neutral);
                    vars.Body = FactionDef.RandomBodyVars(subRng);
                    var data = S.Query.GenBeingFull.Ask(vars, subRng);
                    var b = S.Beings.Create(p, data);
                    var woundTimeAgo = subRng.Range(Consts.TicksPer3Days, Consts.TicksPerWeek);
                    BeingUtils.KillHistorical(S, data, woundTimeAgo, subRng, new DamageType[] {
                        DamageType.Organ
                    });
                    addedBodies++;
                    return addedBodies < numWantedDeadBodies;
                });
            }
        }

        private bool TryBuildStasisArrayAt(HashSet<int> value) {
            //var areaRect = Pos.Encapsulate(value);
            // Bottom left corner of the area
            //var startPos = Pos.NeighborAt(areaRect.Min, 1, 1);
            var values = value.ToList();
            values.Sort();
            values = values.OrderBy(v => rng.Value).ToList();
            int areaBuiltAt = -1;
            for (int i = 0; i < values.Count; i++) {
                int startPos = Pos.NeighborAt(values[i], -1, -1);
                var array = BuildTile(startPos,
                    EntityUtils.CreatePrototype<Tile>(
                        DefIds.ObjectsDevicesStasisArray), true);
                if (EntityUtils.ObjAt(startPos)?.HasComponent<StasisArrayComp>() != true) {
                    continue;
                }
                areaBuiltAt = startPos;
                //D.Warn("Built stasis at: {0}", Pos.String(startPos));
                // Success
                RepairAround(startPos);
                var def = StasisFloorDef();

                if (doSomeDamageToTheArea) {
                    DoSomeDamage(startPos);
                }

                Pos.ForEachAround(Pos.NeighborAt(startPos, 1, 1), 2, true, true, (i) => {
                    var fl = EntityUtils.FloorAt(i);
                    var reFloor = EntityUtils.CreatePrototype<Tile>(def.Id);
                    if (fl == null) {
                        BuildTile(i, reFloor);
                    } else {
                        // A floor tile without full integrity will be either
                        // angled or hollow
                        if (fl.Integrity.CanHaveFullIntegrity) {
                            ReplaceTile(i, reFloor);
                        }
                    }
                    return true;
                });
                spec.AddStasisPos(areaBuiltAt);
                var chosen = S.Sys.Areas.AreaAt(areaBuiltAt);
                if (Arrays.IsEmpty(chosen) || chosen.Count > 80) {
                    var pr = PosRect.Around(Pos.NeighborAt(startPos, 1, 1), 4);
                    chosen = new HashSet<int>();
                    chosen.AddRange(pr.Contents());
                } else {
                    chosen = ExpandArea(chosen, 2);
                }
                spec.InvincibleParts.AddRangeInefficient(chosen);
                InhabitArray(array.GetComponent<StasisArrayComp>());
                return true;
            }
            return false;
        }

        private void DoSomeDamage(int startPos) {
            var subrng = rng.Fork();
            var a = S.Sys.Areas;
            var areaId = a.At(startPos);
            if (areaId > 0) {
                // crack some floors near the stasis array
                var allTiles = new List<int>(a.AreaById(areaId));
                allTiles.Sort();
                int attempts = 30;
                int damaged = subrng.Range(1, 4);
                while (allTiles.Count > 0 && damaged > 0 && attempts-- > 0) {
                    var idx = subrng.Range(0, allTiles.Count);
                    var pos = allTiles[idx];
                    allTiles.RemoveAt(idx);
                    if (EntityUtils.IsObjAt(pos)) { continue; }
                    var floor = EntityUtils.FloorAt(pos);
                    if (floor == null) { continue; }
                    spec.InvincibleParts.Remove(pos);
                    damaged--;
                    floor.Health.TakeDamage(
                        floor.Health.MaxHealth
                            * rng.Range(0.5f, 0.8f),
                        DamageType.Explosive);
                }

                // crack some walls
                var outSet = new HashSet<int>();
                a.OutlineOfArea(areaId, outSet);
                var outline = new List<int>(outSet);
                outline.Sort();
                attempts = 30;
                damaged = subrng.Range(2, 5);
                while (outline.Count > 0 && damaged > 0 && attempts-- > 0) {
                    var idx = subrng.Range(0, outline.Count);
                    var pos = outline[idx];
                    outline.RemoveAt(idx);
                    var wall = EntityUtils.WallAt(pos);
                    if (wall == null) { continue; }
                    spec.InvincibleParts.Remove(pos);
                    damaged--;
                    wall.Health.TakeDamage(
                        wall.Health.MaxHealth
                            * rng.Range(0.5f, 0.9f),
                        DamageType.Explosive);
                }
            }
        }

        private void RepairAround(int startingPoint) {
            Pos.ForEachInRadius(Pos.NeighborAt(startingPoint, 1, 1), 5f, true, (p) => {
                var f = EntityUtils.FloorAt(p);
                if (f?.Health.IsOperational == false) {
                    new CmdRepairTile(f, true).Execute(S);
                }
                return true;
            });
            var fixRng = rng.Fork();
            Pos.ForEachInRadius(startingPoint, 10f, true, (p) => {
                var f = EntityUtils.FloorAt(p);
                if (f?.Health.IsOperational == false && fixRng.Chance(0.5f)) {
                    new CmdRepairTile(f, true).Execute(S);
                }
                return true;
            });
        }

        private bool IsGoodStart(int p) {
            if (spec.HasGenArea && !IsWithinGenArea(p)) {
                return false;
            }
            // 1661396845
            // 146554989
            if (spec.HasCPU && Pos.Distance(spec.CPUPos, p) < minDistToCPU) {
                return false;
            }
            if (spec.HasCPU) {
                if (S.Sys.Areas.IsSameAreaIndoors(spec.CPUPos, p)) {
                    return false;
                }
            }
            foreach (var e in engines) {
                // Too close to an engine
                if (Pos.SqrDistance(p, e) < minDistToEngine * minDistToEngine) {
                    return false;
                }
            }
            bool goodStart = true;
            Pos.ForEachAround(p, 2, true, true, (n) => {
                if (Pos.IsOOB(n)) { return true; }
                if (!EntityUtils.IsEmptyFloorAt(n, false)) {
                    goodStart = false;
                    return false;
                }
                return true;
            });
            return goodStart;
        }
    }
}
