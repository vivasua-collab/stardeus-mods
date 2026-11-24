using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Items {
    public class ObjStorage {
        private readonly ObjsSys sys;
        private readonly GameState S;
        private readonly HashSet<ObjStorageComp> storages = new();
        private readonly Dictionary<int, List<ObjStorageComp>> storagesByGroup = new();
        private Dictionary<int, List<Def>> groupStorageDefs;
        private Dictionary<int, StorableTraits> groupTraits;

        public ObjStorage(ObjsSys sys, GameState s) {
            this.sys = sys;
            S = s;
            s.Sig.ObjStorageChanged.AddListener(OnObjStorageChanged);
            s.Query.ObjStoragesForGroup.SetResponder(GetObjStoragesForGroup);
            s.Query.StoreObj.SetResponder(StoreObj);
            s.Query.DestroyStoredObj.SetResponder(DestroyStoredObj);
            s.Query.UnstoreObj.SetResponder(UnstoreObj);
            BuildGroupStorageDefs();
            s.Query.ObjStorageDefsForGroup.SetResponder(GetObjStorageDefs);
            BuildGroupTraits();
            s.Query.StorableTraitsForGroup.SetResponder(GetStorableTraits);
        }

        private List<Def> GetObjStorageDefs(int groupH) {
            if (groupStorageDefs.TryGetValue(groupH, out var res)) {
                return res;
            }
            return null;
        }

        private StorableTraits GetStorableTraits(int groupH) {
            return groupTraits.TryGetValue(groupH, out var traits) ? traits : StorableTraits.None;
        }

        // Read from every def that can be stored, not from what this game has seen so
        // far, so a wardrobe offers its ownership filters before any clothing exists.
        private void BuildGroupTraits() {
            groupTraits = new Dictionary<int, StorableTraits>();
            foreach (var def in The.Defs.Defs.Values) {
                if (def.IsAbstract) { continue; }
                var storable = def.ComponentConfigFor(ComponentIdH.StorableObj, false);
                if (storable == null) { continue; }
                var group = storable.GetString(PropertyIdH.Group);
                if (string.IsNullOrEmpty(group)) { continue; }
                var traits = StorableTraits.None;
                if (def.HasComponent(ComponentIdH.ObjQuality)) {
                    traits |= StorableTraits.Quality;
                }
                if (ItemOwnership.TracksOwnership(def)) {
                    traits |= StorableTraits.Ownership;
                }
                if (def.HasComponent(ComponentIdH.BodyPart)) {
                    traits |= StorableTraits.BodyPart;
                }
                var groupH = Hashes.S(group);
                groupTraits.TryGetValue(groupH, out var existing);
                groupTraits[groupH] = existing | traits;
            }
        }

        private void BuildGroupStorageDefs() {
            groupStorageDefs = new Dictionary<int, List<Def>>();
            foreach (var def in The.Defs.Defs.Values) {
                var store = def.ComponentConfigFor(ComponentIdH.ObjStorage, false);
                if (store != null) {
                    var groupStrings = store.GetStringSet(PropertyIdH.Groups, null);
                    if (!Arrays.IsEmpty(groupStrings)) {
                        foreach (var group in groupStrings) {
                            var groupId = Hashes.S(group);
                            if (!groupStorageDefs.TryGetValue(groupId, out var list)) {
                                list = new();
                                groupStorageDefs[groupId] = list;
                            }
                            list.Add(def);
                        }
                    } else {
                        D.Err("Groups not defined for ObjStorageComp! {0}", def.Id);
                    }
                }
            }
        }
        private bool UnstoreObj(ObjData objData, ObjStorageComp storage) {
            if (objData.StorageId == 0) {
                D.Err("Trying to unstore an unstored object! {0} | storage: {1}",
                    objData, storage);
                return false;
            }
            if (storage == null) {
                storage = S.Components.Find<ObjStorageComp>(objData.StorageId);
                if (storage == null) {
                    D.Err("Can't unstore object, storage not found by id: {0} | {1}",
                        objData.StorageId, objData);
                    return false;
                }
            }
            if (!storages.Contains(storage)) {
                D.Err("Trying to unstore an obj in a storage that is not part of storages! {0} => {1}",
                    objData, storage.Tile);
            }
            if (objData.StorageId != storage.EntityId) {
                D.Err("Trying to unstore an object that is in the wrong storage! {0} / {1}",
                    objData, storage);
                return false;
            }
            var obj = EjectObj(objData, storage);
            return obj != null;
        }

        private bool StoreObj(ObjData objData, ObjStorageComp storage) {
            if (!storages.Contains(storage)) {
                D.Err("Trying to store an obj in a storage that is not part of storages! {0} => {1}",
                    objData, storage.Tile);
                return false;
            }
            if (storage.IsFull) {
                return false;
            }
            objData.StorageId = storage.EntityId;
            storage.UsedCapacity++;
            objData.State = ObjState.Stored;
            if (objData.Obj != null) {
                S.Objs.Destroy(objData.Obj);
            }
            return true;
        }

        private List<ObjStorageComp> GetObjStoragesForGroup(int groupIdH) {
            if (storagesByGroup.TryGetValue(groupIdH, out var res)) {
                return res;
            }
            return null;
        }

        private void OnObjStorageChanged(ObjStorageComp store,
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
                        if (store.HaulTarget != null) {
                            S.Sys.Inventory.Remove(store.HaulTarget);
                        }
                        foreach (var gid in store.Groups) {
                            if (storagesByGroup.TryGetValue(gid, out var list)) {
                                list.Remove(store);
                            }
                        }
                    }
                    if (!store.Tile.Reinstallable.IsMidRelocation) {
                        // eject all objects
                        foreach (var obj in sys.All.Values) {
                            if (obj.StorageId == store.EntityId) {
                                EjectObj(obj, store);
                            }
                        }
                    }
                    break;
                }
            }
        }

        private bool DestroyStoredObj(ObjData data) {
            if (data.State != ObjState.Stored || data.StorageId == 0) {
                D.Err("Trying to destroy a stored obj that has wrong state or storage id: {0}", data);
                return false;
            }
            var storage = S.Components.Find<ObjStorageComp>(data.StorageId);
            if (storage != null) {
                storage.UsedCapacity--;
                if (storage.UsedCapacity < 0) {
                    D.Err("Storage used capacity dropped below 0 after removing : {0} -> {1}", data, storage);
                    storage.UsedCapacity = 0;
                }
            }
            data.State = ObjState.Destroyed;
            data.StorageId = 0;
            return S.Query.DeactivateObjData.Ask(data, ObjState.Destroyed);
        }

        private Obj EjectObj(ObjData data, ObjStorageComp store) {
            if (store.UsedCapacity > 0) {
                store.UsedCapacity--;
            } else {
                D.Err("Storage used capacity is not positive when ejecting obj! {0} / {1}",
                    store.UsedCapacity, store);
            }
            var pos = store.InPort;
            data.StorageId = 0;
            data.State = ObjState.Active;
            var obj = S.Objs.Create(EntityUtils.CenterOf(pos)
                + Rng.UInsideUnitCircle() * 0.25f, data);
            return obj;
        }

        private void AddStorage(ObjStorageComp store) {
            // D.Warn("Adding storage: {0}. Haul target: {1}", store, store.HaulTarget);
            if (storages.Add(store)) {
                if (store.HaulTarget != null) {
                    S.Sys.Inventory.Add(store.HaulTarget);
                }
                foreach (var gid in store.Groups) {
                    if (!storagesByGroup.TryGetValue(gid, out var list)) {
                        list = new List<ObjStorageComp>();
                        storagesByGroup.Add(gid, list);
                    }
                    list.Add(store);
                }
            } else {
                D.Err("Trying to add obj storage twice: {0}", store);
            }
        }

        public void ValidateAll(Dictionary<uint, ObjData> allObjs) {
            //storage id -> count
            var storageCounts = new Dictionary<int, int>();
            foreach (var o in allObjs.Values) {
                if (o.StorageId != 0) {
                    if (!storageCounts.TryGetValue(o.StorageId, out var cnt)) {
                        cnt = 0;
                    }
                    storageCounts[o.StorageId] = cnt + 1;
                }
            }
            foreach (var storeCount in storageCounts) {
                var storage = S.Components.Find<ObjStorageComp>(storeCount.Key);
                if (storage == null) {
                    D.Err("Storage is not available while we have {0} stored items for it",
                        storeCount.Value);
                    foreach (var obj in allObjs.Values) {
                        if (obj.StorageId == storeCount.Key) {
                            obj.StorageId = 0;
                            obj.State = ObjState.Destroyed;
                        }
                    }
                } else {
                    if (storage.UsedCapacity != storeCount.Value) {
                        D.Err("Storage capacity does not match the fact! {0} -> {1} != {2}",
                            storage, storage.UsedCapacity, storeCount.Value);
                        storage.UsedCapacity = storeCount.Value;
                    }
                }
            }
        }
    }
}
