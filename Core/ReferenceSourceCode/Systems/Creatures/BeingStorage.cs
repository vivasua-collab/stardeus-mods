using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Creatures {
    public class BeingStorage {
        private readonly BeingsSys sys;
        private readonly GameState S;
        private readonly HashSet<BeingStorageComp> storages = new();

        public BeingStorage(BeingsSys sys, GameState s) {
            this.sys = sys;
            S = s;
            s.Sig.BeingStorageChanged.AddListener(OnBeingStorageChanged);
            s.Query.StoreBeing.SetResponder(StoreBeing);
            s.Query.UnstoreBeing.SetResponder(UnstoreBeing);
            s.Query.BeingStoragesFor.SetResponder(GetStoragesFor);
            s.Query.AllBeingStorages.SetResponder(GetAllStorages);
        }

        private HashSet<BeingStorageComp> GetAllStorages() {
            return storages;
        }

        private int GetStoragesFor(BeingData data, List<BeingStorageComp> result) {
            int found = 0;
            foreach (var storage in storages) {
                if (storage.HaulTarget.Accepts(data)) {
                    result.Add(storage);
                    found++;
                }
            }
            return found;
        }

        private bool UnstoreBeing(BeingData beingData, BeingStorageComp storage) {
            if (!storages.Contains(storage)) {
                D.Err("Trying to unstore a being in a storage that is not part of storages! {0} => {1}",
                    beingData, storage.Tile);
            }
            if (beingData.StorageId != storage.EntityId) {
                D.Err("Trying to unstore a being that is in the wrong storage! {0} / {1}",
                    beingData, storage);
                return false;
            }
            var being = EjectBeing(beingData, storage);
            return being != null;
        }

        private bool StoreBeing(BeingData beingData, BeingStorageComp storage) {
            if (!storages.Contains(storage)) {
                D.Err("Trying to store a being in a storage that is not part of storages! {0} => {1}",
                    beingData, storage.Tile);
                return false;
            }
            if (storage.IsFull) {
                return false;
            }
            beingData.StorageId = storage.EntityId;
            storage.UsedCapacity++;
            beingData.StateFlags |= BeingState.Stored;
            beingData.StateFlags |= BeingState.Hidden;
            if (beingData.Being != null) {
                S.Beings.Destroy(beingData.Being);
            }
            foreach (var cond in beingData.Conditions) {
                S.Sig.ActivateCondition.Send(cond.Value, false);
            }
            return true;
        }

        private void OnBeingStorageChanged(BeingStorageComp store,
                EntityChange change) {
            switch (change) {
                case EntityChange.Constructed: {
                    AddStorage(store);
                    break;
                }
                case EntityChange.Loaded: {
                    AddStorage(store);
                    break;
                }
                case EntityChange.Destroyed: {
                    if (storages.Remove(store)) {
                    }
                    if (!store.Tile.Reinstallable.IsMidRelocation) {
                        // eject all objects
                        foreach (var being in sys.All.Values) {
                            if (being.StorageId == store.EntityId) {
                                EjectBeing(being, store);
                            }
                        }
                    }
                    break;
                }
            }
        }

        private Being EjectBeing(BeingData data, BeingStorageComp store) {
            if (store.UsedCapacity > 0) {
                store.UsedCapacity--;
            } else {
                D.Err("Storage used capacity is not positive when ejecting being! {0} / {1}",
                    store.UsedCapacity, store);
            }
            var pos = store.OutPort;
            data.StateFlags &= ~BeingState.Stored;
            data.StateFlags &= ~BeingState.Hidden;
            data.StorageId = 0;
            var being = S.Beings.Create(EntityUtils.CenterOf(pos)
                + Rng.UInsideUnitCircle() * 0.25f, data);
            data.RestoreConditions(S);
            return being;
        }

        private void AddStorage(BeingStorageComp store) {
            if (storages.Add(store)) {
            } else {
                D.Err("Trying to add obj storage twice: {0}", store);
            }
        }

        public void ValidateAll(Dictionary<uint, BeingData> allBeings) {
            //storage id -> count
            var storageCounts = new Dictionary<int, int>();
            foreach (var o in allBeings.Values) {
                if (o.StorageId != 0) {
                    if (!storageCounts.TryGetValue(o.StorageId, out var cnt)) {
                        cnt = 0;
                    }
                    storageCounts[o.StorageId] = cnt + 1;
                }
            }
            foreach (var storeCount in storageCounts) {
                // StorageId is the entity id of a BeingStorageComp (see StoreBeing)
                var storage = S.Components.Find<BeingStorageComp>(storeCount.Key);
                if (storage == null) {
                    D.Err("Storage is not available while we have {0} stored beings for it",
                        storeCount.Value);
                    foreach (var being in allBeings.Values) {
                        if (being.StorageId == storeCount.Key) {
                            // Lost with the storage. Stays Hidden: an entity-less dead being
                            // that is not hidden shows up in the medical window.
                            being.StorageId = 0;
                            being.StateFlags &= ~BeingState.Stored;
                            being.StateFlags &= ~BeingState.Alive;
                            being.StateFlags |= BeingState.Hidden;
                            being.StateFlags |= BeingState.Dead;
                            being.Body.Flags |= BodyFlags.Missing;
                            being.DeactivateConditionsOnDeath();
                        }
                    }
                } else {
                    if (storage.UsedCapacity != storeCount.Value) {
                        D.Err("Being Storage capacity does not match the fact! {0} -> {1} != {2}",
                            storage, storage.UsedCapacity, storeCount.Value);
                        storage.UsedCapacity = storeCount.Value;
                    }
                }
            }
        }
    }
}
