#define DEBUG_GEN_ROOMS
using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using Priority_Queue;
using KL.Collections;
using KL.Utils;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using System;
using UnityEngine;
using Game.Modifiers;
using Game.CodeGen;

namespace Game.Procgen {
    public class FullStoragesGenStage : BaseShipGenStage {
        private string scenario;

        private const int storageCapacity = 2000;
        private const int miniStorageCapacity = 500;
        private string storageDef;

        public FullStoragesGenStage(string scenario) {
            this.scenario = scenario;
        }

        public override void Apply() {
            var potentialStorages = new List<XY>();
            if (spec.IsAsymmetric) {
                for (int fy = 1; fy < spec.FullHeight - 1; fy++) {
                    for (int x = 1; x < spec.TemplateWidth - 1; x++) {
                        if (!spec.IsEmptyFull(x, fy, false)) {
                            potentialStorages.Add(new(x, fy));
                        }
                    }
                }
            } else {
                for (int y = 1; y < spec.TemplateHeight - 1; y++) {
                    for (int x = 1; x < spec.TemplateWidth - 1; x++) {
                        if (!spec.IsEmpty(x, y, false, false)) {
                            potentialStorages.Add(new(x, y));
                        }
                    }
                }
            }
            int built = 0;
            var mats = new Dictionary<MatType, int>();
            ScenarioStartingMats.Load(scenario, S.Difficulty.StartingResources, mats);

            var totalCount = mats.Sum(m => m.Value);
            int storageCap;
            if (totalCount > 2000) {
                storageDef = DefIds.ObjectsDevicesStorage;
                storageCap = storageCapacity;
            } else {
                storageDef = DefIds.ObjectsDevicesStorageMini;
                storageCap = miniStorageCapacity;
            }
            var numOfStoragesRequired = Mathf.CeilToInt(totalCount / (float) storageCap);
            if (numOfStoragesRequired % 2 == 1) {
                numOfStoragesRequired++;
            }

            int attempts = 100;

            while (spec.IsAsymmetric && built < numOfStoragesRequired && attempts-- > 0) {
                // no mirrored pairs on an asymmetric hull: one storage per template cell
                var proto = EntityUtils.CreatePrototype<Tile>(storageDef);
                proto.Transform.Rotation = Facing.Type.Down;
                var cell = rng.From(potentialStorages);
                var pos = Pos.NeighborAt(spec.FullCellToWorld(cell.X, cell.Y), 1, 1);
                if (CanPlaceStorageAt(pos, proto)) {
                    BuildTile(pos, proto, true);
                    built++;
                }
            }

            while (built < numOfStoragesRequired && attempts-- > 0) {
                var protoTop = EntityUtils.CreatePrototype<Tile>(storageDef);
                protoTop.Transform.Rotation = Facing.Type.Down;
                var protoBot = EntityUtils.CreatePrototype<Tile>(storageDef);
                protoBot.Transform.Rotation = Facing.Type.Up;
                var storeCell = rng.From(potentialStorages);
                var storePosTop = Pos.NeighborAt(
                    spec.ToWorldPosMirrored(storeCell, true), 1, 1);
                var storePosBot = Pos.NeighborAt(
                    spec.ToWorldPosMirrored(storeCell, false),
                        1, spec.CellHeight - 1 - protoBot.Transform.RotatedHeight);
                if (CanPlaceStorageAt(storePosTop, protoTop)) {
                    BuildTile(storePosTop, protoTop, true);
                    built++;
                }
                if (CanPlaceStorageAt(storePosBot, protoBot)) {
                    BuildTile(storePosBot, protoBot, true);
                    built++;
                }
            }

            // fallback
            if (built < numOfStoragesRequired) {
                var stage = new PlaceTileGenStage(storageDef,
                    numOfStoragesRequired - built, 3, null, Facing.Types)
                    .PreservePassages();
                stage.SetRng(rng.Fork());
                stage.Initialize(spec, S);
                stage.Apply();
            }

            var storages = S.Components.GenFind<MatStorageComp>();

            foreach (var mat in mats.OrderBy(m => m.Key.Id)) {
                var amt = mat.Value;
                bool addedMat = false;
                foreach (var store in storages.OrderBy(r => rng.Value)) {
                    // D.Warn("Trying to put {0} in {1}", mat.Key, store.Tile);
                    if (!store.IsPublic) { continue; }
                    if (!store.WillAccept(mat.Key, 1, out _)) {
                        // incompatible!
                        continue;
                    }
                    amt = store.Store(mat.Key, null, amt, null);
                    if (amt == 0) {
                        addedMat = true;
                        break;
                    }
                }
                if (!addedMat) {
                    // Shuttle
                    EntityUtils.SpawnRawMaterial(mat.Key, amt,
                        storages[0].OutPort, 0.2f, true, false);
                }
            }
        }

        private bool CanPlaceStorageAt(int pos, Tile proto) {
            if (!CmdPlaceTile.CanPlace(pos, proto, WorldLayer.Objects, warn: false)) {
                return false;
            }
            foreach (var p in proto.Transform.OutlineAtWithGarbage(pos)) {
                if (!EntityUtils.IsEmptyFloorAt(p)) {
                    if (EntityUtils.GetTopmostParentTile(p)?.Definition.Id
                            != storageDef) {
                        return false;
                    }
                }
                // if (EntityUtils.IsDoorAt(p)) {
                //     // We would block the door!
                //     return false;
                // }
            }
            return !GenUtils.CoversBeing(S, proto, pos);
        }
    }
}
