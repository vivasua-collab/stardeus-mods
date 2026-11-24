using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    /// <summary>
    /// Wrecked-Empty-Ship counterpart to FullStoragesGenStage: instead of
    /// building a symmetric grid of storages, it drops storage units into
    /// whatever rooms survived destruction (avoiding doors/corridors) and
    /// fills them with a share of the scenario's starting materials.
    /// Used when ModifierDestructionLevel.StorageRatio &gt; 0.
    /// </summary>
    public class RoomStoragesGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        private readonly string scenario;
        private readonly float storageRatio;
        private const int storageCapacity = 2000;
        private const int miniStorageCapacity = 500;

        public RoomStoragesGenStage(string scenario, float storageRatio) {
            this.scenario = scenario;
            this.storageRatio = storageRatio;
        }

        public override void Apply() {
            var mats = new Dictionary<MatType, int>();
            ScenarioStartingMats.Load(scenario, S.Difficulty.StartingResources, mats,
                storageRatio);

            var totalCount = mats.Sum(m => m.Value);
            if (totalCount <= 0) { return; }

            string storageDef;
            int storageCap;
            if (totalCount > storageCapacity) {
                storageDef = DefIds.ObjectsDevicesStorage;
                storageCap = storageCapacity;
            } else {
                storageDef = DefIds.ObjectsDevicesStorageMini;
                storageCap = miniStorageCapacity;
            }
            var numStorages = Mathf.Max(1, Mathf.CeilToInt(totalCount / (float) storageCap));

            var placeStage = new PlaceTileGenStage(storageDef, numStorages, 3, null, Facing.Types)
                .Indoors()
                .AvoidDoors()
                .PreservePassages();
            placeStage.SetRng(rng.Fork());
            placeStage.Initialize(spec, S);
            placeStage.Apply();

            var storages = S.Components.GenFind<MatStorageComp>();
            if (Arrays.IsEmpty(storages)) {
                D.Warn("RoomStoragesGenStage could not place any storages for scenario: {0}", scenario);
                return;
            }

            foreach (var mat in mats.OrderBy(m => m.Key.Id)) {
                var amt = mat.Value;
                bool addedMat = false;
                foreach (var store in storages.OrderBy(r => rng.Value)) {
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
                if (!addedMat && amt > 0) {
                    // Couldn't fit it all into storage; dump the remainder near one.
                    EntityUtils.SpawnRawMaterial(mat.Key, amt,
                        storages[0].OutPort, 0.2f, true, false);
                }
            }
        }
    }
}
