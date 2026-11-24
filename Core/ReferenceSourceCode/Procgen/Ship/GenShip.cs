#if UNITY_EDITOR
//#define DEBUG_GEN
#endif
using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Procgen.Space;
using Game.Systems;
using Game.Systems.Ships;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Procgen {
    public static class GenShip {
        public static SpaceObject NewDerelictSO(SpaceObject parent, Vector2 loc,
                GameState s, Rng rng, bool nearby) {
            //var parent = s.Universe.Player.Parent;
            //obj.Location = s.Universe.Player.Location;
            Vector2 locRand;
            if (nearby) {
                locRand = StartingDerelictOffset(loc, rng);
            } else {
                var radius = parent.Type.MinDistanceBetweenSubObjects;
                locRand = rng.InsideUnitCircle() * rng.Range(radius * 3f, radius * 10f);
            }


            var ship = s.Query.SpawnShipAt.Ask(parent);
            ship.TurnIntoDerelict(null);
            ship.TeleportToSectorLocation(loc + locRand);
            var parentExitDist = parent.ExitDist;
            var obj = ship.SO;
            if (parentExitDist > 0f) {
                if (!nearby && obj.Location.magnitude * 1.2f > parent.ExitDist) {
                    // We spawned derelict too far, let's bring it back
                    ship.TeleportToSectorLocation(obj.Location.normalized * parent.ExitDist * 0.95f);
                }
            } else {
                D.Err("Adding derelict ship to parent that has exit distance 0! {0}",
                    parent);
            }
            return obj;
        }

        // Far enough that the starmap icons don't overlap (players missed a derelict hiding
        // under their own ship), close enough to stay on screen in the starfield. The player
        // starts on the exit ring, so aim inwards to keep the derelict inside the sector.
        private static Vector2 StartingDerelictOffset(Vector2 playerLoc, Rng rng) {
            var inwards = playerLoc.sqrMagnitude > 0.0001f
                ? -playerLoc.normalized
                : Vector2.right;
            return Vectors.Rotate(inwards, rng.Range(-75f, 75f)) * Tunables.StartingDerelictDist;
        }

        public static void AddStructureStages(ShipFactory factory, Rng rng,
                int maxEngines, float chanceLocked, int centerAreaSize,
                string[] engineOptions, float shipSize = 1f, int spotRooms = 1) {


            var isStation = factory.ForShip?.Class.IsSpaceStation == true;
            var layout = ShipLayout.Roll(rng, isStation);
            factory.Layout = layout;
            factory.AddStage(layout.CreateHullStage(factory.ShipShape, factory.ForShip));
            AddHullExtras(factory, rng, layout, isStation);

            factory.AddStage(new IslandDetectionAndAttachmentGenStage());

            factory.AddStage(new HoleFillerGenStage());

            if (factory.ForShip != null) {
                factory.AddStage(new ShipEngineGenStage(factory.ForShip));
            } else {
                if (maxEngines > 0 && !Arrays.IsEmpty(engineOptions)) {
                    factory.AddStage(new EngineGenStage(2 * engineOptions.Length,
                        maxEngines, chanceLocked, engineOptions));
                }
            }

            factory.AddStage(new AngleRoundingGenStage(rng.Range(0f, 1f)));
            if (rng.Chance(0.25f / shipSize)) {
                factory.AddStage(new ThickenShipGenStage());
            }
            if (rng.Chance(0.5f)) {
                factory.AddStage(new RoundSharpCornersGenStage());
            }

            Func<ShipSpec, bool> makeDoubleWalls = (spec) =>
                spec.Width > 90 && spec.Height > 70;
            factory.AddStage(new OuterWallGenStage(makeDoubleWalls));

            AddRoomStages(factory, layout, centerAreaSize, shipSize, spotRooms);
            factory.AddStage(new AirlockGenStage());
            factory.AddStage(new RemoveUnreachableAreasGenStage());
            factory.AddStage(new VentGenStage());
            factory.AddStage(new FloorRandomizeGenStage());
            factory.AddStage(new FloorWindowGenStage());
            factory.AddStage(new WallWindowGenStage());
            factory.AddStage(new FloorLightsGenStage());
            factory.AddStage(new EngineDoorGenStage());
            //factory.AddStage(new RoundSharpCornersGenStage());
        }

        // Bollinger's extra wings and cross connections read the mirrored half of the
        // template, and only suit its noise hull: the other hulls are designed shapes
        public static void AddHullExtras(ShipFactory factory, Rng rng, ShipLayout layout,
                bool isStation) {
            if (isStation || layout.Hull != HullVariant.Bollinger || !layout.IsMirrored) {
                return;
            }
            if (rng.Chance(0.25f)) {
                factory.AddStage(new ConnectionGenStage());
            }
            if (rng.Chance(0.5f)) {
                factory.AddStage(new IslandCreationGenStage());
            }
        }

        // The CPU room in the center, the rooms around it, and doors on a spanning tree.
        // spotRooms: 8x8 rooms reserved for the stasis arrays, and for Wrecked's survivor camp
        public static void AddRoomStages(ShipFactory factory, ShipLayout layout,
                int centerAreaSize, float shipSize, int spotRooms = 1) {
            layout.CenterAreaSize = centerAreaSize;
            layout.RoomSizeMul = Math.Max(1f, shipSize);
            layout.StasisRooms = spotRooms;
            layout.ShuttleRooms = 1;
            ShipGenExperiments.ApplyToRooms(layout);
            factory.AddStage(new InnerWallGenStage(centerAreaSize));
            factory.AddStage(new RemoveSmallAreasGenStage());
            factory.AddStage(new RoomGrowthGenStage(layout));
            factory.AddStage(new WallGapFillGenStage());
            factory.AddStage(new RemoveSmallAreasGenStage());
            factory.AddStage(new TreeDoorGenStage());
            factory.AddStage(new MissingDoorGenStage());
        }

        public static void AddDestructionStages(ShipFactory factory, Rng rng,
                int maxIslandSize, float destructionLevel = 0.75f) {
            factory.AddStage(new DestroyShipGenStage(destructionLevel));
            // Below 40% destruction the ship is mostly intact already; skip the
            // extra island-splitting pass so it doesn't get fragmented further.
            var allowIslandSplit = maxIslandSize > 0 && destructionLevel >= 0.40f;
            if (allowIslandSplit) {
                factory.AddStage(new SplitBigIslandsGenStage(maxIslandSize));
            }
            factory.AddStage(new IslandFloatV2GenStage());
            if (allowIslandSplit) {
                factory.AddStage(new SplitBigIslandsGenStage(maxIslandSize));
            }
        }

        public static void AddPostDestructionStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new WallWindowFinalizeGenStage());
        }

        public static void AddLootStages(ShipFactory factory, Rng rng, int numShuttles,
                string[] things, string[] thingsInSpace, string[] objs, bool addDeadBeings) {
            if (numShuttles > 0) {
                factory.AddStage(new ShuttleGenStage(numShuttles, true, true));
                factory.AddStage(new OpenShuttleAreaGenStage(false, true));
            }
            if (addDeadBeings) {
                factory.AddStage(new DeadWorkersGenStage());
                factory.AddStage(new SlaughterSpotGenStage());
            }
            if (things != null) {
                factory.AddStage(new ObjectsGenStage(things));
            }

            if (thingsInSpace != null) {
                AddThingsInSpaceStages(factory, thingsInSpace);
            }

            if (objs != null) {
                factory.AddStage(new ObjGenStage(objs));
            }
        }
        // Solar panels and particle collectors go on the hull, where the ship's designers
        // would have put them; anything else still goes anywhere in space. The collector
        // stage takes no rng fork, so the panels get the one the single stage used to have.
        private static void AddThingsInSpaceStages(ShipFactory factory, string[] thingsInSpace) {
            var collectors = new List<Def>();
            var panels = new List<Def>();
            var others = new List<Def>();
            foreach (var id in thingsInSpace) {
                var def = The.Defs.TryGet(id);
                if (def == null) {
                    D.Err("Def not found by id: {0}", id);
                } else if (def.HasComponent(ComponentIdH.RawMaterialGenerator)) {
                    collectors.Add(def);
                } else if (def.HasComponent(ComponentIdH.SolarPanel)) {
                    panels.Add(def);
                } else {
                    others.Add(def);
                }
            }
            if (collectors.Count > 0) {
                factory.AddStage(new HullParticleCollectorGenStage(collectors));
            }
            if (panels.Count > 0) {
                factory.AddStage(new HullSolarPanelsGenStage(panels));
            }
            if (others.Count > 0) {
                factory.AddStage(new ObjectsInSpaceGenStage(others));
            }
        }

        private static readonly Dictionary<Def, int> defs = new();
        public static void AddRandomLootStages(GameState s, ShipFactory factory,
                Rng rng, int count, int techLevel, Func<Def, bool> isDefUnlocked) {
            defs.Clear();
            GenSpawn.ChooseRandomDefs(s, rng, count, techLevel,
                isDefUnlocked, defs);
            var chessPiece = s.Sys.Get<ChessPitySys>(ChessPitySys.SysId)?
                .TakeChessPiece(s, rng.Fork(), defs);
            if (chessPiece != null) {
                // First pick of the floor: the loot stage drops what finds no good spot
                factory.AddStage(new ObjectsGenStage(new List<Def> { chessPiece }));
            }
            if (defs.Count == 0) { return; }
            var tiles = new List<Def>();
            var tilesInSpace = new List<Def>();
            var objs = new List<Def>();
            foreach (var d in defs) {
                if (d.Value < 1) { continue; }
                var def = d.Key;
                for (int i = 0; i < d.Value; i++) {
                    if (def.LayerId == WorldLayer.FreeEntities) {
                        if (def.IsBeing) {
                            D.Err("Trying to add being to random loot! {0}", def.Id);
                            continue;
                        }
                        objs.Add(def);
                    } else if (def.IsConstructable) {
                        if (def.IsPlacementInSpace) {
                            tilesInSpace.Add(def);
                        } else {
                            tiles.Add(def);
                        }
                    } else {
                        D.Err("Trying to add a non-constructable loot: {0}", def.Id);
                    }
                }
            }
            if (tiles.Count > 0) {
                #if DEBUG_GEN
                D.Warn("Adding random loot tiles: {0}", D.Dump(tiles));
                #endif
                factory.AddStage(new ObjectsGenStage(tiles));
            }
            if (tilesInSpace.Count > 0) {
                #if DEBUG_GEN
                D.Warn("Adding random loot tiles in space: {0}", D.Dump(tilesInSpace));
                #endif
                factory.AddStage(new ObjectsInSpaceGenStage(tilesInSpace));
            }
            if (objs.Count > 0) {
                #if DEBUG_GEN
                D.Warn("Adding random loot obj: {0}", D.Dump(objs));
                #endif
                factory.AddStage(new ObjGenStage(objs));
            }
        }

        public static void AddPostLootStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new ObjectDamageGenStage());
        }
        public static void AddCosmeticStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new DirtGenStage(0.8f));
            factory.AddStage(new DebrisCleanupGenStage());
            factory.AddStage(new DebrisShuffleGenStage());
            factory.AddStage(new DebrisMergeGenStage());
        }
    }
}
