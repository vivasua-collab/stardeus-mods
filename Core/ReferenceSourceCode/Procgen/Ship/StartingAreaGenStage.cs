using KL.Grid;
using Game.Data;
using System.Linq;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using Game.Components;
using System.Collections.Generic;
using KL.Randomness;
using System.Collections;
using Game.Modifiers;
using Game.Scenarios;
using Game.Systems.Creatures;
using Game.CodeGen;
using Game.Systems.Naming;
using System;
using Game.Systems.UIUX;
using UnityEngine;

namespace Game.Procgen {

    public class StartingAreaGenStage : BaseShipGenStage, IWithScenarioConfig {
        public override bool RequiresAreas => true;
        public override bool StartOverIfNeedsRepeating => true;
        //public override bool ForceDebug => true;
        public override bool IsAsync => true;
        private int numRobots;
        private int numDrones;
        private int numCarriers;
        private int numHeatSinks;
        private int wantedHeatSinks;
        private GameSetupData config;
        private readonly bool addBattery;
        private readonly bool tryCenter;
        private readonly HashSet<int> engines = new();
        private readonly HashSet<int> reactors = new();
        private readonly HashSet<int> reservedAreas = new();
        private readonly HashSet<int> reservedRooms = new();
        private bool avoidReservedRooms;

        public StartingAreaGenStage(bool addBattery, bool tryCenter, int numHeatSinks = 0) {
            this.addBattery = addBattery;
            this.tryCenter = tryCenter;
            this.numHeatSinks = numHeatSinks;
            this.wantedHeatSinks = numHeatSinks;
        }

        private int stasisPos;

        public override IEnumerator ApplyAsync() {
            numRobots = ModifierBeingsCount.ValueFor(S, SpeciesId.Robot, out _);
            numDrones = ModifierBeingsCount.ValueFor(S, SpeciesId.Drone, out _);
            numCarriers = ModifierBeingsCount.ValueFor(S, SpeciesId.Carrier, out _);
            NeedsRepeating = false;
            engines.Clear();
            var engs = S.Components.GenFind<ShipEngineComp>();
            foreach (var eng in engs) {
                engines.Add(eng.Entity.PosIdx);
            }

            reactors.Clear();
            var reacts = S.Components.GenFind<MatterReactorComp>();

            foreach (var r in reacts) {
                reactors.Add(r.Entity.PosIdx);
                reservedAreas.Add(S.Sys.Areas.At(r.Entity.PosIdx));
            }
            // the rooms RoomGrowthGenStage reserved for the stasis arrays and the shuttle
            reservedRooms.Clear();
            foreach (var hint in spec.StasisRoomHints) {
                reservedRooms.Add(S.Sys.Areas.At(hint));
            }
            foreach (var hint in spec.ShuttleRoomHints) {
                reservedRooms.Add(S.Sys.Areas.At(hint));
            }
            avoidReservedRooms = true;

            StartOver = false;
            // Find surviving CPU first
            var cpus = S.Components.GenFindAllMatching<ShipComputerComp>(
                c => c.Tile.IsConstructed && IsWithinGenArea(c.Tile.PosIdx));


            if (cpus?.Count > 1) {
                // Remove excess cpus
                for (int i = 1; i < cpus.Count; i++) {
                    new CmdRemoveTile(cpus[i].Tile.PosIdx, WorldLayer.Objects).Execute(S);
                }
                var cpu = cpus.FirstOrDefault();
                new CmdRepairTile(cpu.Tile, true).Execute(S);
                var cpuPos = cpu.Entity.PosIdx;
                spec.SetCPUPos(cpuPos);
                Pos.ForEachInRadius(cpuPos, 5, true,
                    (p) => {
                        spec.InvincibleParts.Add(p);
                        return true;
                    });
                BuildWorkers(spec.CPUPos);
                yield break;
            }

            var stasis = S.Components.FindFirstMatching<StasisArrayComp>(
                s => s.Tile.IsActive);
            if (stasis != null) {
                stasisPos = stasis.Tile.PosIdx;
            }

            int startingPoint = Pos.Invalid;
            if (tryCenter) {
                startingPoint = Pos.NeighborAt(S.Grid.CenterPos, -1, -1);
                if (!IsGoodStart(startingPoint)) {
                    startingPoint = Pos.Invalid;
                }
            }

            List<int> startingPoints = new();
            if (startingPoint == Pos.Invalid) {
                var solidArea = S.Sys.Areas.SolidArea.ToList();
                solidArea.Sort();
                foreach (var a in solidArea) {
                    if (IsGoodStart(a)) {
                        startingPoints.Add(a);
                    }
                }
                if (startingPoints.Count == 0 && avoidReservedRooms) {
                    // a hull too tight to spare the reserved rooms still needs its CPU
                    avoidReservedRooms = false;
                    foreach (var a in solidArea) {
                        if (IsGoodStart(a)) {
                            startingPoints.Add(a);
                        }
                    }
                }
            } else {
                startingPoints.Add(startingPoint);
            }
            if (tryCenter) {
                // The rooms were laid out for a CPU at the center (RoomGrowthGenStage keeps
                // the stasis room away from there), so the nearest good spot keeps that true
                var center = S.Grid.CenterPos;
                startingPoints.Sort((a, b) => {
                    var byDist = Pos.SqrDistance(a, center).CompareTo(Pos.SqrDistance(b, center));
                    return byDist != 0 ? byDist : a.CompareTo(b);
                });
            }
            Tile builtCpu = null;
            while (startingPoints.Count > 0) {
                startingPoint = tryCenter ? startingPoints[0] : rng.From(startingPoints);
                //yield return Viz(startingPoint, 0.1f);
                startingPoints.Remove(startingPoint);
                BuildTile(startingPoint, EntityUtils.CreatePrototype<Tile>(
                    DefIds.ObjectsDevicesShipComputer), true);
                builtCpu = EntityUtils.ObjAt(startingPoint);
                if (builtCpu != null) {
                    //yield return Viz(startingPoint, 3f);
                    break;
                }
            }

            if (builtCpu == null) {
                D.Err("Could not build CPU, starting over");
                StartOver = true;
                yield break;
            }

            Pos.ForEachInRadius(startingPoint, 5f, true, (p) => {
                var f = EntityUtils.FloorAt(p);
                if (f?.Health.IsOperational == false) {
                    new CmdRepairTile(f, true).Execute(S);
                }
                return true;
            });
            Pos.ForEachInRadius(startingPoint, 8f, true, (p) => {
                var f = EntityUtils.FloorAt(p);
                if (f?.Health.IsOperational == false && rng.Chance(0.5f)) {
                    new CmdRepairTile(f, true).Execute(S);
                }
                return true;
            });

            BuildHeatSinks(startingPoint);

            spec.SetCPUPos(startingPoint);
            Pos.ForEachInRadius(startingPoint, 5, true,
                (p) => {
                    spec.InvincibleParts.Add(p);
                    return true;
                });

            // Build a battery
            int distance = 3;
            int attempts = 15;
            if (addBattery) {
                bool hasBattery = false;
                while (!hasBattery && attempts-- > 0) {
                    hasBattery = BuildBattery(startingPoint, distance, 15 - attempts);
                    distance++;
                }
                if (!hasBattery) {
                    // Missing battery = broken tutorial
                    D.Err("{0}. Failed building battery, starting over", LogPrefix);
                    StartOver = true;
                    yield break;
                }
            }

            BuildWorkers(startingPoint);

            Pos.ToXY(startingPoint, out var sx, out var sy);
            var defs = The.Defs.Matching(DefIds.StructureFloorsFloorReinforced03);
            var def = spec.CosmeticRng.From(defs);
            var frameDef = spec.CosmeticRng.From(The.Defs.Matching(DefIds.StructureFloorsFrame01));
            Pos.ForEachAround(startingPoint, 2, true, true, (i) => {
                Pos.ToXY(i, out var x, out var y);
                // Trim bottom and left
                if (x == sx - 2 || y == sy - 2) { return true; }
                var targetDef = def;

                if (wantedHeatSinks < 3) {
                    // build frame in corners
                    // bottom left corner
                    if (x == sx - 1 && y == sy - 1) {
                        targetDef = frameDef;
                    // top left
                    } else if (x == sx - 1 && y == sy + 2) {
                        targetDef = frameDef;
                    // top right
                    } else if (x == sx + 2 && y == sy + 2) {
                        targetDef = frameDef;
                    // bottom right
                    } else if (x == sx + 2 && y == sy - 1) {
                        targetDef = frameDef;
                    }
                }

                var fl = EntityUtils.FloorAt(i);
                var reFloor = EntityUtils.CreatePrototype<Tile>(targetDef.Id);
                if (fl == null) {
                    BuildTile(i, reFloor);
                } else {
                    // Don't replace angled tiles
                    if (fl.Integrity.CanHaveFullIntegrity) {
                        ReplaceTile(i, reFloor);
                    }
                }
                return true;
            });
        }

        private void BuildHeatSinks(int startingPoint) {
            if (numHeatSinks == 0) { return; }
            if (numHeatSinks > 6) {
                numHeatSinks = 6;
            }
            var outSorted = new List<int>();
            outSorted.Add(Pos.NeighborAt(startingPoint, -1, 0));
            outSorted.Add(Pos.NeighborAt(startingPoint, -1, 1));
            outSorted.Add(Pos.NeighborAt(startingPoint, 0, 2));
            outSorted.Add(Pos.NeighborAt(startingPoint, 1, 2));
            outSorted.Add(Pos.NeighborAt(startingPoint, -1, 2));
            outSorted.Add(Pos.NeighborAt(startingPoint, 2, 2));
            while (numHeatSinks > 0 && outSorted.Count > 0) {
                var p = outSorted[0];
                outSorted.RemoveAt(0);
                if (EntityUtils.IsEmptyFloorWithIntegrityAt(p)) {
                    var hsProto = EntityUtils.CreatePrototype<Tile>(
                        DefIdsH.ObjectsDevicesHeatSink);
                    var hs = BuildTile(p, hsProto);
                    if (hs != null) {
                        numHeatSinks--;
                    }
                }
            }
        }

        private void BuildWorkers(int startingPoint) {
            // Build a construction robot
            int robotStartDist = 5;
            //493849900

            int builtRobots = 0;
            // Each search covers the whole square around the CPU, so a search that found no
            // spot would find none again: start over instead (below)
            while (builtRobots < numRobots
                    && BuildConstructionRobot(startingPoint, robotStartDist)) {
                builtRobots++;
            }

            if (builtRobots < numRobots) {
                D.Err("Could not build {0} robots, starting over", numRobots);
                StartOver = true;
                return;
            }

            var pos = EntityUtils.CenterOf(startingPoint);

            // Add a construction drone
            for (int i = 0; i < numDrones; i++) {
                var data = GetBeingData(S, Species.Get(SpeciesIdH.Drone), BeingType.StartingCrew, config);
                var cmd = new CmdSpawnBeing(
                    data, FreeSpotNear(startingPoint, pos));
                cmd.Execute(S);
            }

            // Add carriers
            for (int i = 0; i < numCarriers; i++) {
                var data = GetBeingData(S, Species.Get(SpeciesIdH.Carrier), BeingType.StartingCrew, config);
                var cmd = new CmdSpawnBeing(
                    data, FreeSpotNear(startingPoint, pos));
                cmd.Execute(S);
            }

            if (config == null) { return; }
            foreach (var startingBeings in config.StartingBeings) {
                var species = startingBeings.Key;
                if (species.IsBiological || species.IdH == SpeciesIdH.Robot
                        || species.IdH == SpeciesIdH.Drone
                        || species.IdH == SpeciesIdH.Carrier) {
                    continue;
                }
                while (startingBeings.Value.Count > 0) {
                    var data = GetBeingData(S, species, BeingType.StartingCrew, config);
                    var cmd = new CmdSpawnBeing(
                        data, FreeSpotNear(startingPoint, pos));
                    cmd.Execute(S);
                }
            }
        }


        // A random spot around the CPU could land on a device (or the CPU itself), and a
        // being spawned inside one is shut in. The first roll is kept when it is free, so
        // seeds whose crew already stood clear keep the same rng sequence.
        private Vector2 FreeSpotNear(int startingPoint, Vector2 center) {
            for (int i = 0; i < 10; i++) {
                var spot = center + rng.InsideUnitCircle() * 2;
                if (EntityUtils.IsEmptyFloorAt(EntityUtils.ToPos(spot))) {
                    return spot;
                }
            }
            var found = Pos.Invalid;
            for (int r = 1; r <= freeSpotSearchRadius && found == Pos.Invalid; r++) {
                Pos.ForEachAround(startingPoint, r, false, true, p => {
                    if (!Pos.IsOOB(p) && EntityUtils.IsEmptyFloorAt(p)) {
                        found = p;
                        return false;
                    }
                    return true;
                });
            }
            return found == Pos.Invalid ? center : EntityUtils.CenterOf(found);
        }

        private const int freeSpotSearchRadius = 6;

        // 1818754558 - robot floats
        private bool BuildConstructionRobot(int startingPoint, int distance) {
            int robotPoint = 0;
            Pos.ForEachAroundRandom(rng, startingPoint, distance, false, true, (n) => {
                if (EntityUtils.IsEmptyFloorWithIntegrityAt(n)) {
                    // Make sure robot is not trapped on some side edge
                    if (EntityUtils.IsEmptyFloorAt(Pos.N(n)) &&
                            EntityUtils.IsEmptyFloorAt(Pos.S(n)) &&
                            EntityUtils.IsEmptyFloorAt(Pos.W(n)) &&
                            EntityUtils.IsEmptyFloorAt(Pos.E(n))) {
                        if (EntityUtils.HasLineOfSightNonCover(EntityUtils.CenterOf(n),
                                EntityUtils.ParentObjAt(spec.CPUPos), 0f, true)) {
                            robotPoint = n;
                            return false;
                        }
                    }
                }
                return true;
            });

            if (robotPoint == 0) { return false; }
            var data = GetBeingData(S, Species.Get(SpeciesIdH.Robot), BeingType.StartingCrew, config);
            var cmd = new CmdSpawnBeing(data, robotPoint);
            cmd.Execute(S);
            return cmd.SpawnedBeing != null;
        }

        private bool BuildBattery(int startingPoint, int distance, int attempt) {
            /*
            D.Err("Building a battery at {0} (dist: {1}, att: {2}",
                Pos.String(startingPoint), distance, attempt);
                */
            int batteryPoint = Pos.Invalid;
            Pos.ForEachAroundRandom(rng, startingPoint,
                    distance, false, true, (n) => {
                if (IsGoodStart(n, 1)) {
                    batteryPoint = n;
                    return false;
                }
                return true;
            });

            if (batteryPoint == Pos.Invalid) { return false; }

            var batProto = EntityUtils.CreatePrototype<Tile>(
                DefIds.ObjectsDevicesBattery);
            batProto.Transform.Rotation = rng.Chance(0.5f) ?
                Facing.Type.Down : Facing.Type.Right;
            BuildTile(batteryPoint, batProto, true);
            var bat = EntityUtils.ObjAt(batteryPoint);
            if (bat == null) { return false; }
            // We do have a battery!
            var batC = bat.GetComponent<BatteryComp>();
            batC.Tile.ENode.SetBatteryCharge(
                rng.Range(0.6f, 0.9f) * batC.Tile.ENode.Node.BatteryMaxCharge);
            Pos.ForEachInRadius(batteryPoint, distance + 1, true, (p) => {
                spec.InvincibleParts.Add(p);
                return true;
            });
            RemoveFloorLights(batteryPoint, 2);
            return true;
        }

        private bool IsGoodStart(int p, int around = 2) {
            if (S.Sys.Areas.IslandAt(p) == 0) { return false; }
            var goodStart = true;
            if (reservedAreas != null) {
                var area = S.Sys.Areas.At(p);
                if (reservedAreas.Contains(area)) {
                    return false;
                }
                if (avoidReservedRooms && reservedRooms.Contains(area)) {
                    return false;
                }
            }
            if (Pos.SqrDistance(p, stasisPos) < 25 * 25) {
                return false;
            }
            foreach (var e in engines) {
                // Too close to an engine
                if (Pos.SqrDistance(p, e) < 5 * 5) {
                    return false;
                }
            }
            foreach (var r in reactors) {
                // Too close to a reactor
                if (Pos.SqrDistance(p, r) < 25 * 25) {
                    return false;
                }
            }
            Pos.ForEachAround(p, around, true, true, (n) => {
                if (GenUtils.IsDoor(n)) {
                    goodStart = false;
                    return false;
                }
                if (!EntityUtils.IsEmptyFloorAt(n, false)) {
                    goodStart = false;
                    return false;
                }
                return true;
            });
            return goodStart;
        }

        public void SetConfig(GameSetupData config) {
            this.config = config;
        }
    }
}
