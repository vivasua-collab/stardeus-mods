#if UNITY_EDITOR
//#define PROFILE_COMPONENT_TICK
#endif
using Game.Constants;
using System.Collections.Generic;
using Game.Data;
using KL.Clock;
using KL.Utils;
using KL.Signals;
using System.Collections;
using KL.Grid;
using UnityEngine;
using MessagePack;
using KL.Collections;
using System;
using Game.UI;
using Game.Systems.UIUX;
using KL.Randomness;
using Game.Data.Space;
using Game.Migrations;
using Game.Utils;
using Game.CodeGen;
using Game.Commands;
using Game.Mods;
using Game.Rendering;

namespace Game.Components {
    public class ComponentState : ITaggedSignalListener {
        [MessagePackObject]
        public class StateData {
            [Key(0)] public List<ComponentData> Floor = new();
            [Key(1)] public List<ComponentData> Walls = new();
            [Key(2)] public List<ComponentData> Objects = new();
            [Key(3)] public List<ComponentData> FreeEntities = new();
            [Key(4)] public Dictionary<int, List<ComponentData>> Extra = new();
        }
        public GameState S;
        // Cannot be readonly, we replace it when loading
        public StateData Data = new();
        public int GameId { get; set; }
        private readonly List<ITickListener> TickListenerComps = new();
        //private bool tickListenersLocked;
        private readonly List<IRareTickListener> RareTickListenerComps = new();
        //private bool rareTickListenersLocked;
        private readonly List<ITickProgressListener> TickProgressListenerComps = new();
        //private bool tickProgressListenersLocked;
        // FIXME why this was a hashset??
        // Because it's much faster to remove things from hashset you idiot
        private readonly HashSet<IComponent> CompsFloor = new();
        private readonly HashSet<IComponent> CompsWalls = new();
        private readonly HashSet<IComponent> CompsObjects = new();
        private readonly HashSet<IComponent> CompsFreeEntities = new();
        private readonly Dictionary<int, HashSet<IComponent>> CompsExtra = new();
        private readonly Dictionary<int, HashSet<IComponent>> CompsByType = new();

        private readonly Dictionary<int, List<int>> typesByInterface = new();
        private readonly HashSet<Type> knownTypes = new();

        public Map Map;

        public object SignalTag => GameId;
        public int CountInLayer(int layer) {
            switch (layer) {
                case WorldLayer.Floor: return Data.Floor.Count;
                case WorldLayer.Walls: return Data.Walls.Count;
                case WorldLayer.Objects: return Data.Objects.Count;
                case WorldLayer.FreeEntities: return Data.FreeEntities.Count;
                default: {
                    var data = ExtraCompData(layer, false);
                    if (data != null) {
                        return data.Count;
                    }
                    D.Err("Trying to get non existent layer components: {0}", layer);
                    return 0;
                }
            }
        }

        private HashSet<IComponent> Comps(int layer) {
            switch (layer) {
                case WorldLayer.Floor: return CompsFloor;
                case WorldLayer.Walls: return CompsWalls;
                case WorldLayer.Objects: return CompsObjects;
                case WorldLayer.FreeEntities: return CompsFreeEntities;
                default: {
                    if (RenderLayers.ById(layer)?.HasPersistentComponents == true) {
                        return ExtraComps(layer);
                    }
                    D.Err("Trying to get non existent layer components: {0}", layer);
                    return null;
                }
            }
        }
        private List<ComponentData> CompData(int layer) {
            switch (layer) {
                case WorldLayer.Floor: return Data.Floor;
                case WorldLayer.Walls: return Data.Walls;
                case WorldLayer.Objects: return Data.Objects;
                case WorldLayer.FreeEntities: return Data.FreeEntities;
                default: {
                    var data = ExtraCompData(layer, true);
                    if (data != null) {
                        return data;
                    }
                    D.Err("Trying to get non existent layer components: {0}", layer);
                    return null;
                }
            }
        }

        private HashSet<IComponent> ExtraComps(int layer) {
            if (!CompsExtra.TryGetValue(layer, out var comps)) {
                comps = new();
                CompsExtra.Add(layer, comps);
            }
            return comps;
        }

        private List<ComponentData> ExtraCompData(int layer, bool create) {
            if (RenderLayers.ById(layer)?.HasPersistentComponents != true) {
                return null;
            }
            Data.Extra ??= new();
            if (!Data.Extra.TryGetValue(layer, out var data) && create) {
                data = new();
                Data.Extra.Add(layer, data);
            }
            return data;
        }

        public void RestoreAfterLoad(int gameId) {
            GameId = gameId;
            Data.Extra ??= new();
            foreach (var layer in RenderLayers.PersistentComponentLayerIds) {
                Comps(layer).Clear();
            }
            CompsExtra.Clear();
            CompsByType.Clear();
            typesByInterface.Clear();
            knownTypes.Clear();
            TickListenerComps.Clear();
            RareTickListenerComps.Clear();
            TickProgressListenerComps.Clear();
        }

        public void SerializeBeforeSave() {
            IComponent lastComponent = null;
            try {
                Data.Extra ??= new();
                foreach (var layer in RenderLayers.PersistentComponentLayerIds) {
                    // readonly not null
                    var comps = Comps(layer);
                    var data = CompData(layer);
                    if (data == null) {
                        throw new Exception(string.Format(
                            "CompData was null in ComponentState.SerializeBeforeSave in layer {0}",
                                WorldLayer.Name(layer)));
                    }
                    var countBefore = comps.Count;
                    var countAfter = comps.Count;
                    /*
                    // D.Log("Serialize components. Before = {0}, After = {1}",
                        countBefore, countAfter);
                    */
                    data.Clear();
                    foreach (IComponent comp in comps) {
                        lastComponent = comp;
                        try {
                            if (comp == null) {
                                D.Err("Comp was null in SerializeBeforeSave!");
                                continue;
                            }
                            // Cannot check if entity is active here! it will break the hell out of saves
                            if (comp.EntityId == 0) {
                                D.Err("Removing unlinked component: {0}", comp);
                                continue;
                            }
                            if (!comp.Entity.IsActive) {
                                continue;
                            }
                            comp.OnSave();
                        } catch (Exception compEx) {
                            Ready.WhenMainThread(this, "LogEx", () => {
                                D.LogEx(compEx, "Caught component exception while saving: {0}",
                                    DumpComponentInfo(comp));
                            });
                            if (ModRegistry.IsModType(comp.GetType(), out var mod)) {
                                ModRegistry.ReportBrokenModSave(comp.GetType(), mod);
                            #if !PRODUCTION_BUILD
                            } else {
                                throw compEx;
                            #endif
                            }
                        }
                        var d = comp.Data;
                        if (d != null) {
                            d.UnsetMode();
                            if (!d.IsEmpty) {
                                data.Add(d);
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                S.IsInconsistent = true;
                Ready.WhenMainThread(this, "LogEx", () => {
                    D.LogEx(ex,
                        "Failed to save the game state, error in serialize before save! Last component: {0}",
                            DumpComponentInfo(lastComponent));
                });
                // We cannot spawn a widget, we're outside main thread! We still do this in cmdsavegame
                throw new Exception(string.Format(
                    "Failed saving the game due to: {0}", ex.Message), ex);
            }
        }

        public static string DumpComponentInfo(IComponent comp) {
            if (comp == null) { return "[Component == null]"; }
            var entity = comp.Entity;
            if (entity != null) {
                var isActive = entity.IsActive;
                var def = entity.DefinitionId;
                return $"[Component[{comp.GetName()}][{comp.GetType()}][Entity:{def}][EntityActive:{isActive}]";
            }
            return $"[Component[{comp.GetName()}][{comp.GetType()}][NullEntity]";
        }

        public void Clear() {
            Data.Extra ??= new();
            foreach (var layer in RenderLayers.PersistentComponentLayerIds) {
                Comps(layer).Clear();
                CompData(layer).Clear();
            }
            CompsExtra.Clear();
            CompsByType.Clear();
            TickListenerComps.Clear();
            RareTickListenerComps.Clear();
            TickProgressListenerComps.Clear();
        }

        public void CleanupAfterLoad() {
            Data.Extra ??= new();
            foreach (var layer in RenderLayers.PersistentComponentLayerIds) {
                CompData(layer).Clear();
            }
        }

        public void RegisterFor(Entity entity) {
            var cl = entity.Components.Length;
            for (int i = 0; i < cl; i++) {
                IComponent comp = entity.Components[i];
                RegisterNewComp(entity, comp);
            }
            // Late ready
            for (int i = 0; i < cl; i++) {
                IComponent comp = entity.Components[i];
                comp.OnLateReady(false);
            }
            // Post Late ready
            for (int i = 0; i < cl; i++) {
                IComponent comp = entity.Components[i];
                comp.PostLateReady(false);
                // TODO skip this if developer mode is off
                comp.Validate();
            }
        }

        private void RegisterNewComp(Entity entity, IComponent comp) {
            comp.EntityId = entity.Id;
            Add(comp);
            comp.OnReady(false);
        }

        public void RemoveFor(Entity entity, bool final) {
            // D.Log("Removing components for entity with id: {0}. Final: {1}",
            //     entity.Id, final);
            if (entity.Components == null) {
                D.Err("Entity did not have components during removal: {0}", entity);
                return;
            }
            if (!Ready.StateLock) {
                D.Err("Removing components for entity while saving! Entity: {0}, final: {1}",
                    entity, final);
                S.CmdQ.Enqueue(new CmdLambda(() =>
                    RemoveFor(entity, final),
                        "RemoveFor was attempted to be removed during save", entity));
                return;
            }
            for (int i = entity.Components.Length - 1; i >= 0; i--) {
                IComponent comp = entity.Components[i];
                if (comp == null) { continue; }
                if (!final) {
                    DeregisterTickListener(comp, false);
                } else {
                    Remove(comp);
                }
            }
        }

        public void Add(IComponent comp) {
            if (!Ready.StateLock) {
                D.Err("Adding comp for while saving! Comp: {0}", DumpComponentInfo(comp));
                S.CmdQ.Enqueue(new CmdLambda(() =>
                    Add(comp),
                        "Add(comp) was attempted during save", comp));
                return;
            }
            var comps = Comps(comp.LayerId);
            Add(comps, comp);
        }

        private void Add(HashSet<IComponent> comps, IComponent comp) {
            if (!Ready.StateLock) {
                D.Err("Adding comp while saving! {0}", DumpComponentInfo(comp));
            }
            comps.Add(comp);
            AddByType(comp);
            RegisterTickListener(comp);
        }

        private void AddByType(IComponent comp) {
            if (comp.SkipTypeLookupCache) { return; }
            var th = comp.TypeHash;
            if (!CompsByType.TryGetValue(th, out var typeComps)) {
                typeComps = new HashSet<IComponent>();
                CompsByType.Add(th, typeComps);
                knownTypes.Add(comp.GetType());
                // We have to clear the cache for interface hashes, because we
                // don't know what interfaces this new type has
                typesByInterface.Clear();
            }
            typeComps.Add(comp);
        }

        public void Remove(IComponent comp) {
            if (!Ready.StateLock) {
                D.Err("Removing a comp while saving! {0}",
                    DumpComponentInfo(comp));
            }
            var layerId = comp.LayerId;
            Comps(layerId).Remove(comp);
            RemoveByType(comp);
            comp.OnRemove();
            DeregisterTickListener(comp, true);
            // Make this component not loadable anymore
            comp.EntityId = 0;
        }

        private void RemoveByType(IComponent comp) {
            if (comp.SkipTypeLookupCache) { return; }
            if (CompsByType.TryGetValue(comp.TypeHash, out var comps)) {
                comps.Remove(comp);
            //} else {
                //D.Err("Could not find cache by comp hash: {0}", comp);
            }
        }
        public T Find<T>(int entityId, int nameHash) where T : IComponent{
            if (entityId == 0) {
                D.Err("Trying to find component {0} with 0 id!",
                    typeof(T));
                return default;
            }

            if (Map.TryEntityById(entityId, out var e)) {
                return e.GetComponent<T>(nameHash);
            }

            return default;
        }

        public T Find<T>(int entityId) where T : IComponent{
            if (entityId == 0) {
                D.Err("Trying to find component {0} with 0 id!",
                    typeof(T));
                return default;
            }

            if (Map.TryEntityById(entityId, out var e)) {
                return e.GetComponent<T>();
            }
            return default;
        }

        public IComponent Find(int entityId, int layer, string name) {
            if (entityId == 0) {
                D.Err("Trying to find component {0} with 0 id! (layer: {1})",
                    name, layer);
                return default;
            }
            if (Map.TryEntityById(entityId, out var e)) {
                foreach (IComponent comp in e.Components) {
                    if (comp.GetName() == name) {
                        return comp;
                    }
                }
            }
            return null;
        }

        public T FindFirstMatching<T>(Func<T, bool> check) where T : IComponent {
            if (!CompsByType.TryGetValue(Hashes.TypeHash<T>(), out var list)) {
                if (typeof(T).IsInterface) {
                    D.Err("FindAllMatching<T> only works with types, trying interface: {0}", typeof(T));
                }
                return default;
            }
            foreach (T c in list) {
                if (c.Entity.IsActive) {
                    if (check(c)) { return c; }
                }
            }
            return default;
        }
        public int FindMatchingByInterface<T>(List<T> res, Func<T, bool> check) where T : IComponent {
            int count = 0;
            foreach (var h in TypeHashesFor<T>()) {
                if (CompsByType.TryGetValue(h, out var comps)) {
                    foreach (T c in comps) {
                        if (check(c)) {
                            res.Add(c);
                        }
                    }
                }
            }
            return count;
        }
        public T FindFirstMatchingByInterface<T>(Func<T, bool> check) {
            foreach (var h in TypeHashesFor<T>()) {
                if (CompsByType.TryGetValue(h, out var comps)) {
                    foreach (T c in comps) {
                        if (check(c)) {
                            return c;
                        }
                    }
                }
            }
            return default;
        }

        public int FindAllByInterface<T>(T[] res) {
            int count = 0;
            foreach (var h in TypeHashesFor<T>()) {
                if (CompsByType.TryGetValue(h, out var comps)) {
                    foreach (T c in comps) {
                        res[count++] = c;
                        if (res.Length == count) { return count; }
                    }
                }
            }
            return count;
        }
        public int FindAllMatchingByInterface<T>(T[] res, Func<T, bool> check) {
            int count = 0;
            foreach (var h in TypeHashesFor<T>()) {
                if (CompsByType.TryGetValue(h, out var comps)) {
                    foreach (T c in comps) {
                        if (check(c)) {
                            res[count++] = c;
                            if (res.Length == count) { return count; }
                        }
                    }
                }
            }
            return count;
        }
        public List<T> GenFindAllMatching<T>(Func<T, bool> check) where T : IComponent {
            List<T> res = null;
            if (!CompsByType.TryGetValue(Hashes.TypeHash<T>(), out var list)) {
                if (typeof(T).IsInterface) {
                    D.Err("FindAllMatching<T> only works with types, trying interface: {0}", typeof(T));
                }
                return res;
            }
            foreach (T c in list) {
                if (c.Entity.IsActive) {
                    if (check(c)) {
                        res ??= new();
                        res.Add(c);
                    }
                }
            }
            return res;
        }
        public int FindAllMatching<T>(List<T> res, Func<T, bool> check, int max = 0) where T : IComponent {
            if (!CompsByType.TryGetValue(Hashes.TypeHash<T>(), out var list)) {
                if (typeof(T).IsInterface) {
                    D.Err("FindAllMatching<T> only works with types, trying interface: {0}", typeof(T));
                }
                return 0;
            }
            var count = 0;
            foreach (T c in list) {
                if (c.Entity.IsActive) {
                    if (check(c)) {
                        res.Add(c);
                        count++;
                        if (max != 0 && count == max) {
                            return count;
                        }
                    }
                }
            }
            return count;
        }
        public int FindAllMatching<T>(T[] res, Func<T, bool> check) where T : IComponent {
            if (!CompsByType.TryGetValue(Hashes.TypeHash<T>(), out var list)) {
                if (typeof(T).IsInterface) {
                    D.Err("FindAllMatching<T> only works with types, trying interface: {0}", typeof(T));
                }
                return 0;
            }
            var count = 0;
            foreach (T c in list) {
                if (c.Entity.IsActive) {
                    if (check(c)) {
                        res[count++] = c;
                        if (count == res.Length) { break; }
                    }
                }
            }
            return count;
        }
        public T FindClosestMatchingInterface<T>(Vector2 pos, bool requireSameIsland,
                Func<T, bool> predicate) where T : IComponent {

            if (!typeof(T).IsInterface) {
                D.Err("FindClosestMatchingInterface<T> only works with interfaces, trying type: {0}", typeof(T));
                return default;
            }
            var closestDist = float.MaxValue;
            T closest = default;
            var closeEnoughSqr = Units.CloseEnoughSqr;
            var closeEnoughChance = Units.CloseEnoughChance;
            int reqIslandId = -1;
            var ar = S.Sys.Areas;
            if (requireSameIsland) {
                reqIslandId = ar.IslandAt(EntityUtils.ToPos(pos));
            }

            foreach (var h in TypeHashesFor<T>()) {
                if (CompsByType.TryGetValue(h, out var comps)) {
                    foreach (T c in comps) {
                        if (!c.Entity.IsActive) { continue; }
                        if (predicate != null && !predicate(c)) { continue; }
                        if (requireSameIsland) {
                            var islandId = ar.IslandAt(c.Entity.PosIdx);
                            if (!ar.CanReachGroundedIslandToIsland(reqIslandId, islandId)) {
                                continue;
                            }
                        }
                        var dist = Vectors.SqrDistance(c.Entity.Position, pos);
                        if (dist < closestDist) {
                            closest = c;
                            closestDist = dist;
                            if (dist <= closeEnoughSqr && S.Rng.Chance(closeEnoughChance)) {
                                return closest;
                            }
                        }
                    }
                }
            }
            return closest;
        }

        public T FindClosestMatching<T>(Vector2 pos, bool requireSameIsland,
                Func<T, bool> predicate) where T : IComponent {
            if (!CompsByType.TryGetValue(Hashes.TypeHash<T>(), out var list)) {
                if (typeof(T).IsInterface) {
                    D.Err("FindClosestMatching<T> only works with types, trying interface: {0}", typeof(T));
                }
                return default;
            }
            var closestDist = float.MaxValue;
            T closest = default;
            var closeEnoughSqr = Units.CloseEnoughSqr;
            var closeEnoughChance = Units.CloseEnoughChance;
            int reqIslandId = -1;
            var ar = S.Sys.Areas;
            if (requireSameIsland) {
                reqIslandId = ar.IslandAt(EntityUtils.ToPos(pos));
            }
            foreach (IComponent c in list) {
                if (c is T t) {
                    if (!c.Entity.IsActive) { continue; }
                    if (predicate != null && !predicate(t)) { continue; }
                    if (requireSameIsland) {
                        var islandId = ar.IslandAt(c.Entity.PosIdx);
                        if (!ar.CanReachGroundedIslandToIsland(reqIslandId, islandId)) {
                            continue;
                        }
                    }
                    var dist = Vectors.SqrDistance(c.Entity.Position, pos);
                    if (dist < closestDist) {
                        closest = t;
                        closestDist = dist;
                        if (dist <= closeEnoughSqr && S.Rng.Chance(closeEnoughChance)) {
                            return closest;
                        }
                    }
                }
            }
            return closest;
        }

        public void FindAll<T>(List<T> list) where T : IComponent {
            if (CompsByType.TryGetValue(Hashes.TypeHash<T>(), out var comps)) {
                foreach (T c in comps) {
                    if (c.Entity.IsActive) {
                        list.Add(c);
                    }
                }
            } else if (typeof(T).IsInterface) {
                D.Err("FindAll<T> only works with types, trying interface: {0}", typeof(T));
            }
        }

        public List<T> GenFind<T>() where T : IComponent {
            var res = new List<T>();
            if (typeof(T).IsInterface) {
                FindByInterface<T>(res);
            } else {
                FindAll<T>(res);
            }
            return res;
        }
        public int CountByInterface<T>(int max = -1) {
            int res = 0;
            foreach (var h in TypeHashesFor<T>()) {
                if (CompsByType.TryGetValue(h, out var comps)) {
                    res += comps.Count;
                    if (res >= max) { break; }
                }
            }
            return res;
        }
        public int CountByInterfaceMatching<T>(Func<T, bool> check, int max = -1) {
            int res = 0;
            foreach (var h in TypeHashesFor<T>()) {
                if (CompsByType.TryGetValue(h, out var comps)) {
                    foreach (T comp in comps) {
                        if (check(comp)) {
                            res += comps.Count;
                            if (res >= max) { break; }
                        }
                    }
                }
            }
            return res;
        }
        /// <summary>WARNING: This will include count of inactive components while they are not destroyed fully</summary>
        public int CountByType<T>() {
            if (CompsByType.TryGetValue(Hashes.TypeHash<T>(), out var comps)) {
                return comps.Count;
            } else if (typeof(T).IsInterface) {
                D.Err("CountByType<T> only works with types, trying interface: {0}", typeof(T));
            }
            return 0;
        }

        public int CountMatching<T>(Func<T, bool> predicate, int max = 0) {
            int count = 0;
            if (CompsByType.TryGetValue(Hashes.TypeHash<T>(), out var comps)) {
                foreach (IComponent c in comps) {
                    if (c.Entity.IsActive && c is T t && predicate(t)) {
                        count++;
                        if (max != 0 && count == max) {
                            return count;
                        }
                    }
                }
            } else if (typeof(T).IsInterface) {
                D.Err("CountMatching<T> only works with types, trying interface: {0}", typeof(T));
            }
            return count;
        }

        public int CountOfConstructed<T>(int max = 0)
                where T : IComponent {
            int count = 0;
            if (CompsByType.TryGetValue(Hashes.TypeHash<T>(), out var comps)) {
                foreach (IComponent c in comps) {
                    if (c.Entity.IsActive && c is T) {
                        if (c.Entity is Tile tt && tt.IsConstructed) {
                            count++;
                            if (max != 0 && count == max) {
                                return count;
                            }
                        }
                    }
                }
            } else if (typeof(T).IsInterface) {
                D.Err("CountMatching<T> only works with types, trying interface: {0}", typeof(T));
            }
            return count;
        }

        private List<int> TypeHashesFor<T>() {
            // As we add new components to the game, we have to append them to
            // hashes by interface
            // Try to find the list of type hashes by interface hash
            var th = Hashes.TypeHash<T>();
            if (!typesByInterface.TryGetValue(th, out var typeKeys)) {
                typeKeys = new();
                typesByInterface.Add(th, typeKeys);
                BuildTypeHashes<T>(typeKeys);
            }
            return typeKeys;
        }

        private void BuildTypeHashes<T>(List<int> hashes) {
            var tt = typeof(T);
            foreach (var type in knownTypes) {
                // tell if type implements T
                if (tt.IsAssignableFrom(type)) {
                    hashes.Add(Hashes.TypeHash(type));
                }
            }
        }

        public void FindByInterface<T>(List<T> res) where T : IComponent {
            // 1. get all type hashes for the interface
            var hashes = TypeHashesFor<T>();
            for (int i = hashes.Count - 1; i >= 0; i--) {
                int h = hashes[i];
                // 2. foreach type hash, find
                FindByHash<T>(h, res);
            }
        }
        public List<T> GenFindAllMatchingByInterface<T>(Func<T, bool> check) where T : IComponent {
            List<T> res = new();
            FindByInterface(res);
            for (int i = res.Count - 1; i >= 0; i--) {
                if (check != null && !check(res[i])) {
                    res.RemoveAt(i);
                }
            }
            return res;
        }

        private void FindByHash<T>(int hash, List<T> res) where T : IComponent {
            if (CompsByType.TryGetValue(hash, out var comps)) {
                foreach (T c in comps) {
                    #if !PRODUCTION_BUILD
                    D.Ass(c.Entity.IsActive,
                        "Entity is inactive in CompsByType: {0}", c.Entity);
                    #endif
                    if (c.Entity.IsActive) {
                        res.Add(c);
                    }
                }
            } else if (typeof(T).IsInterface) {
                D.Err("FindByHash<T> only works with types, trying interface: {0}", typeof(T));
            }
        }
        /// <summary>
        /// WARNING do a null check!
        /// Don't attempt to create AllByInterface<T>, that won't be easy.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public HashSet<IComponent> AllByType<T>() {
            if (!CompsByType.TryGetValue(Hashes.TypeHash<T>(), out var comps)) {
                if (typeof(T).IsInterface) {
                    D.Err("AllByType<T> only works with types, trying interface: {0}", typeof(T));
                }
                return null;
            }
            return comps;
        }

        public int FindAll<T>(T[] res) {
            if (!CompsByType.TryGetValue(Hashes.TypeHash<T>(), out var comps)) {
                if (typeof(T).IsInterface) {
                    D.Err("FindAll<T> only works with types, trying interface: {0}", typeof(T));
                }
                return 0;
            }
            int found = 0;
            foreach (IComponent c in comps) {
                if (c.Entity.IsActive) {
                    res[found++] = (T) c;
                    if (found == res.Length) { break; }
                }
            }
            return found;
        }
        public T FindFirstByInterface<T>(Func<T, bool> check) where T : IComponent {
            foreach (var h in TypeHashesFor<T>()) {
                if (CompsByType.TryGetValue(h, out var comps)) {
                    foreach (T c in comps) {
                        if (c.Entity.IsActive && check(c)) {
                            return c;
                        }
                    }
                }
            }
            return default;
        }

        public T FindFirstByInterface<T>() where T : IComponent {
            foreach (var h in TypeHashesFor<T>()) {
                if (CompsByType.TryGetValue(h, out var comps)) {
                    foreach (T c in comps) {
                        var e = c.Entity;
                        #if !PRODUCTION_BUILD
                        D.Ass(e.IsActive, "Entity is active");
                        #endif
                        return c;
                    }
                }
            }
            return default;
        }

        public T FindFirstByType<T>() where T : IComponent {
            #if !PRODUCTION_BUILD
            if (typeof(T).IsInterface) {
                D.Err("Trying to use FirstByType with an interface! {0}", typeof(T).Name);
                return default;
            }
            #endif

            if (!CompsByType.TryGetValue(Hashes.TypeHash<T>(), out var comps)) {
                return default;
            }
            foreach (T c in comps) {
                var e = c.Entity;
                if (e.IsActive) {
                    return c;
                }
            }
            return default;
        }

        public void RemoveLayer(int layer) {
            if (RenderLayers.ById(layer)?.HasPersistentComponents == true) {
                var comps = Comps(layer);
                comps?.Clear();
            } else {
                // D.Warn("Skipping removal of component layer: {0}", WorldLayer.Name(layer));
            }
        }

        // Need to support both T[] and List<T>
        public void Reinitialize<T>(ICollection<T> entities, int layer) where T : Entity {
            if (entities.Count == 0) { return; }
            var cache = new Dictionary<int, Entity>(entities.Count);
            foreach (var entity in entities) {
                // Child tiles have 0 entity id
                if (entity == null || entity.Id == 0) { continue; }
                if (entity.Definition == null) {
                    // D.Warn("Skipping entity without def: {0}", entity);
                    continue;
                }
                if (!cache.TryAdd(entity.Id, entity)) {
                    D.Err("Trying to reinitialize duplicate entity! {0}. Already have {1}",
                        entity, cache[entity.Id]);
                }
            }
            if (cache.Count == 0) { return; }
            var compData = CompData(layer);
            var comps = Comps(layer);

            var brokenComps = new List<IComponent>();

            // Loop through all persisted components and add them on to the entity
            for (int i = 0; i < compData.Count; i++) {
                var data = compData[i];
                if (data == null) { continue; }
                if (!cache.TryGetValue(data.EntityId, out Entity entity)) {
                    continue;
                }
                try {
                    // var compH = Hashes.S(data.Component);
                    // var compType = Renames.MigratedComponent(data.Component, compH,
                    //     out var removed);
                    // if (removed) {
                    //     continue;
                    // }
                    var compType = Hashes.S(data.Component);
                    var compConf = entity.Definition.ComponentConfigFor(compType, false);
                    var isConfigMissing = compConf == null;
                    if (isConfigMissing) {
                        #if !PRODUCTION_BUILD
                        // // Migrate legacy position for a being
                        // if (entity is Being b && compType == "Transform") {
                        //     b.PosIdx = TransformComp.ResolveLegacyBeingPos(data);
                        // }
                        // if (!The.TestsAreRunning) {
                        //     D.Err("Removing a component that existed in save but does not exist in entity def: {0} {1}",
                        //         compType, entity.DefinitionId);
                        // }
                        // #else
                        D.ErrSoft("Removing a component that existed in save but does not exist in entity def: {0} {1}",
                            data.Component, entity.DefinitionId);
                        #endif
                        compData[i] = null;
                        continue;
                        // Blank component config fallback
                        // TODO - use some global defaults for certain components
                        //compConf = new() { Component = compType };
                    }

                    var comp = The.Defs.CreateComponent(compConf, entity);
                    if (comp == null) { continue; }
                    if (isConfigMissing) {
                        #if !PRODUCTION_BUILD
                        if (!The.TestsAreRunning) {
                            D.Warn("Component config on entity {0} is null for comp: {1}",
                                entity.DefinitionId, compType);
                        }
                        #else
                        D.Warn("Component config on entity {0} is null for comp: {1}",
                            entity.DefinitionId, compType);
                        #endif
                    }
                    // Repeating what Add(comp) does, but we don't grab the comps
                    // for performance
                    if (entity.AddComponent(comp)) {
                        Add(comps, comp);

                        if (!comp.Load(entity, data)) {
                            brokenComps.Add(comp);
                        }
                    }
                } catch (Exception ex) {
                    entity.IsActive = false;
                    entity.DeactivationTime = -1;
                    D.LogEx(ex, "Failed reinitializing entity (phase 1): {0} -> {1}",
                        entity.Definition.Id, entity.Id);
                }
            }

            //ProfilerUtils.StartProfile("PostRestoreAll");
            foreach (var entity in cache.Values) {
                try {
                    // Restore components that were added to the json definitions
                    // after older versions were saved without them
                    var missing = The.Defs.BuildMissingComponentsFor(entity);
                    if (missing != null) {
                        for (int i = 0; i < missing.Count; i++) {
                            IComponent comp = missing[i];
                            RegisterNewComp(entity, comp);
                            if (!comp.Load(entity, null)) {
                                D.Err("Failed to load null data for missing comp: {0}", comp);
                                brokenComps.Add(comp);
                            }
                        }
                    }

                    var ec = entity.Components;
                    var ecl = ec.Length;

                    for (int i = 0; i < ecl; i++) {
                        IComponent comp = ec[i];
                        if (comp == null) {
                            D.Err("Entity has a null component! Ignoring. {0}",
                                ((Entity) entity).InfoString());
                            continue;
                        }
                        comp.OnReady(true);
                    }

                    for (int i = 0; i < ecl; i++) {
                        IComponent comp = ec[i];
                        comp.OnLateReady(true);
                    }

                    for (int i = 0; i < ecl; i++) {
                        IComponent comp = ec[i];
                        comp.PostLateReady(true);
                    }
                } catch (Exception ex) {
                    D.LogEx(ex, "Failed reinitializing entity (phase 2): {0} -> {1}",
                        entity.DefinitionId, entity.Id);
                    entity.IsActive = false;
                    entity.Id = 0;
                    entity.DeactivationTime = -1;
                    RemoveFor(entity, true);
                    Ready.WhenGame(this, "Load Error", () => {
                        WPopup.SystemWarning("PARTIAL LOAD FAILURE",
                            $"Failed loading entity: {entity.DefinitionId}");
                    });
                }
            }

            if (brokenComps.Count > 0) {
                // D.Warn("Removing broken components: {0}", brokenComps.Count);
                for (int i = 0; i < brokenComps.Count; i++) {
                    IComponent c = (IComponent) brokenComps[i];
                    comps.Remove(c);
                }
            }

            //ProfilerUtils.EndProfile();
        }

        private void RegisterTickListener(IComponent comp) {
            if (comp is ITickListener tl) {
                TickListenerComps.Add(tl);
            }
            if (comp is IRareTickListener rtl) {
                RareTickListenerComps.Add(rtl);
            }
            if (comp is ITickProgressListener tpl) {
                TickProgressListenerComps.Add(tpl);
            }
        }
        private void DeregisterTickListener(IComponent comp, bool final) {
            if (comp is ITickListener tl) {
                TickListenerComps.Remove(tl);
            }
            if (comp is IRareTickListener rtl) {
                RareTickListenerComps.Remove(rtl);
            }
            if (comp is ITickProgressListener tpl) {
                if (tickProgressListenersLocked) {
                    if (final) {
                        D.ErrSoft("FINAL Removing a ITickProgressListener while tick progress is running! This could have side effects! {0}->{1}",
                            comp, comp.Entity);
                        TickProgressListenerComps.Remove(tpl);
                    } else {
                        D.Warn("Trying to remove a ITickProgressListener while tick progress is running! Skipping. This could have side effects! {0}->{1}",
                            comp, comp.Entity);
                    }
                } else {
                    TickProgressListenerComps.Remove(tpl);
                }
            }
        }

        public void Subscribe() {
            S.Clock.OnTickProgress.AddListener(OnTickProgress);
            S.Clock.OnTick.AddListener(OnTick);
        }

        public void Unsubscribe() {
            S.Clock.OnTickProgress.RemoveListener(OnTickProgress);
            S.Clock.OnTick.RemoveListener(OnTick);
        }

        private bool tickProgressListenersLocked;
        // Clock.OnTick isolates whole systems, not the components inside this loop: one
        // component that threw on every tick stopped every component after it from ticking
        // (devices, production), and in OnTickProgress from animating
        private readonly ErrorThrottle tickErrors = new();

        private void OnTickProgress(float progress) {
            if (!Ready.Game) { return; }
            tickProgressListenersLocked = true;
            try {
                for (int i = TickProgressListenerComps.Count - 1; i >= 0; i--) {
                    if (i >= TickProgressListenerComps.Count) { continue; }
                    var comp = TickProgressListenerComps[i];
                    try {
                        comp.TickProgress(progress);
                    } catch (Exception ex) {
                        if (tickErrors.ShouldLog(comp, out var count)) {
                            D.LogEx(ex, "Failed TickProgress of {0} ({1} times)", comp, count);
                        }
                    }
                }
            } finally {
                tickProgressListenersLocked = false;
            }
        }

        private void OnTick(long tick) {
            if (!Ready.Game) { return; }
            //tickListenersLocked = true;
            var tlc = TickListenerComps.Count;

            #if PROFILE_COMPONENT_TICK
            #endif

            for (int i = tlc - 1; i >= 0; i--) {
                var comp = TickListenerComps.SafeGetFast(i);
                if (comp == null) { continue; }
                try {
                    comp.Tick(tick);
                } catch (Exception ex) {
                    if (tickErrors.ShouldLog(comp, out var count)) {
                        D.LogEx(ex, "Failed Tick of {0} ({1} times)", comp, count);
                    }
                }
            }
            if (tick % Consts.TicksPer15Minutes == 0) {
                var rtlc = RareTickListenerComps.Count;
                for (int i = rtlc - 1; i >= 0; i--) {
                    var comp = RareTickListenerComps.SafeGetFast(i);
                    if (comp == null) { continue; }
                    try {
                        comp.RareTick(tick);
                    } catch (Exception ex) {
                        if (tickErrors.ShouldLog(comp, out var count)) {
                            D.LogEx(ex, "Failed RareTick of {0} ({1} times)", comp, count);
                        }
                    }
                }
            }
            // the throttle holds its keys, so removed components are let go daily
            if (tick % Consts.TicksPerDay == 0) {
                tickErrors.Clear();
            }
        }

        public List<T> FindInArea<T>(Vector2 start, Vector2 end,
                int layerId = WorldLayer.Objects) {
            var minX = Math.Min(start.x, end.x);
            var maxX = Math.Max(start.x, end.x);
            var minY = Math.Min(start.y, end.y);
            var maxY = Math.Max(start.y, end.y);
            List<T> res = null;
            var comps = Comps(layerId);
            foreach (IComponent comp in comps) {
                if (comp is T compT && comp.Entity.IsActive) {
                    var p = comp.Entity.Position;
                    if (p.x > minX && p.x < maxX && p.y > minY && p.y < maxY) {
                        res ??= new();
                        res.Add(compT);
                    }
                }
            }
            return res;
        }
    }
}
