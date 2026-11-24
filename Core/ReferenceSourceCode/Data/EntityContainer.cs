using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data.Space;
using KL.Collections;
using KL.Grid;
using KL.QuadTree;
using KL.Utils;
using UnityEngine;

namespace Game.Data {
    public abstract class EntityContainer<T> where T : Entity, IQuadable, new() {
        public GameState S;
        protected QuadTree<T> quadTree;
        public readonly Dictionary<int, List<T>> ByPos = new();
        public readonly List<T> All = new();
        private readonly Dictionary<int, T> entitiesById = new();

        public void Initialize(Map map) {
            S = map.S;
            quadTree = new QuadTree<T>(map.Width, map.Height);
            AfterInit();
        }

        protected virtual void AfterInit() { }

        protected T Create(Def def, GameState s) {
            if (!Ready.StateLock) {
                D.Err("Trying to create an entity while saving! {0}", def);
                return null;
            }
            D.Ass(def != null,
                "Trying to create an entity when Definition is null!");
            var entity = new T() { Definition = def };
            entity.S = s;
            The.Defs.BuildComponentsFor(entity);
            S.AddEntity(entity);
            All.Add(entity);
            entitiesById.Add(entity.Id, entity);
            return entity;
        }

        protected abstract bool AddToQuadTree(T entity);

        public void Clear() {
            All.Clear();
            entitiesById.Clear();
            quadTree = null;
            Destroy();
        }

        public virtual void Destroy() {
            /*
            foreach (var entity in All) {
                S.RemoveEntity(entity, resolveSubTile: true);
            }
            */
        }

        public int FindAllByType<T2>(T2[] results) {
            var count = 0;
            for (int i = 0; i < All.Count; i++) {
                T entity = All[i];
                if (!entity.IsActive) { continue; }
                for (int i1 = 0; i1 < entity.Components.Length; i1++) {
                    Components.IComponent c = entity.Components[i1];
                    if (c is T2 t) {
                        results[count++] = t;
                        // Do not overflow the results cache
                        if (count == results.Length) { break; }
                    }
                }
            }
            return count;
        }

        public int FindAllByType<T2>(T2[] results, Func<T2, bool> predicate) {
            var count = 0;
            for (int i = 0; i < All.Count; i++) {
                T entity = All[i];
                if (!entity.IsActive) { continue; }
                for (int i1 = 0; i1 < entity.Components.Length; i1++) {
                    Components.IComponent c = entity.Components[i1];
                    if (c is T2 t) {
                        if (predicate(t)) {
                            results[count++] = t;
                            // Do not overflow the results cache
                            if (count == results.Length) { break; }
                        }
                    }
                }
            }
            return count;
        }
        public T FindFirstInPos(int posIdx, Func<T, bool> match = null) {
            //P.StartBenchmark("FindFirstInPos");
            if (quadTree.QueryFirst(EntityUtils.CenterOf(posIdx), Consts.V2One, (t) => {
                    return t.IsActive && t.PosIdx == posIdx && (match == null || match(t));
                }, out T res)) {
                //P.StopBenchmark("FindFirstInPos");
                return res;
            }
            //P.StopBenchmark("FindFirstInPos");
            return default;
        }
        private const int queryCacheSize = 1024;
        private readonly (Vector2 pos, T entity)[] queryCache = new (Vector2, T)[queryCacheSize];
        private T exceptCheck;
        private bool CheckActiveExcept(T t) {
            return t != exceptCheck;
        }
        private HashSet<T> exceptSet;
        private bool CheckActiveExceptSet(T t) {
            return !exceptSet.Contains(t);
        }
        private Func<T, bool> checkActiveExcept;
        private Func<T, bool> checkActiveExceptSet;

        public T FindClosestVisual(Vector2 position, T except = null,
                float boundsSize = 10, bool includeProxy = true) {
            exceptCheck = except;
            checkActiveExcept ??= CheckActiveExcept;
            return FindClosestVisualFiltered(position, except == null ? null : checkActiveExcept,
                boundsSize, includeProxy);
        }

        // For cycling through everything at one spot: none of the given ones comes back
        public T FindClosestVisual(Vector2 position, HashSet<T> except,
                float boundsSize = 10, bool includeProxy = true) {
            exceptSet = except;
            checkActiveExceptSet ??= CheckActiveExceptSet;
            return FindClosestVisualFiltered(position,
                except == null || except.Count == 0 ? null : checkActiveExceptSet,
                boundsSize, includeProxy);
        }

        private T FindClosestVisualFiltered(Vector2 position, Func<T, bool> filter,
                float boundsSize, bool includeProxy) {
            int found = 0;
            var bounds = new Bounds(position, new Vector3(boundsSize, boundsSize, 1));
            if (filter == null) {
                found = quadTree.QueryNonAlloc(bounds, queryCache);
            } else {
                found = quadTree.QueryNonAlloc(bounds, queryCache, filter);
            }
            if (includeProxy && found < queryCacheSize) {
                Caches.EntityProxyList.Clear();
                var proxiesFound = A.S.Query.GetEntityProxiesInRect.Ask(
                    bounds.min, bounds.max, Caches.EntityProxyList);
                if (proxiesFound > 0) {
                    for (int i = Caches.EntityProxyList.Count - 1; i >= 0; i--) {
                        Systems.Proxy.EntityProxy pr = Caches.EntityProxyList[i];
                        if (pr.Entity is T ent) {
                            if (filter != null && !filter(ent)) { continue; }
                            queryCache[found++] = (pos: pr.Position, ent);
                            if (found == queryCacheSize) {
                                break;
                            }
                        }
                    }
                }
            }
            if (found == 0) {
                return default;
            }
            T closest = default;
            float closestDist = float.MaxValue;
            for (int i = 0; i < found; i++) {
                var posEntity = queryCache[i];
                var dist = Vectors.Distance(posEntity.pos, position);
                if (closestDist > dist) {
                    closest = posEntity.entity;
                    closestDist = dist;
                }
            }
            // D.Warn("Closest: {0}. Dist: {1}, Sel size: {2}", closest, closestDist, closest?.SelectableSize.magnitude * 0.5f);
            if (closest != null &&
                    closestDist <= closest.SelectableSize.magnitude * 0.5f) {
                return closest;
            }
            return default;
        }
        public T FindFirstMatching(Func<T, bool> check) {
            //P.StartBenchmark("FindFirstMatching");
            for (int i = 0; i < All.Count; i++) {
                T entity = All[i];
                if (!entity.IsActive) { continue; }
                if (!check(entity)) { continue; }
                //P.StopBenchmark("FindFirstMatching");
                return entity;
            }
            //P.StopBenchmark("FindFirstMatching");
            return default;
        }

        public T FindClosestMatching(int posIdx, Func<T, bool> check,
                float maxDist, float minDist) {
            //P.StartBenchmark("FindClosestMatching");
            var found = quadTree.QueryNonAlloc(new Bounds(EntityUtils.CenterOf(posIdx),
                new Vector3(maxDist, maxDist) * 2), queryCache, check);
            if (found == 0) {
                //P.StopBenchmark("FindClosestMatching");
                return null;
            }
            T closest = default;
            // float maxDistSqr = maxDist * maxDist;
            float minDistSqr = minDist * minDist;
            float closestDist = float.MaxValue;
            for (int i = 0; i < found; i++) {
                var entity = queryCache[i].entity;
                if (!entity.IsActive) { continue; }
                var dist = Pos.SqrDistance(posIdx, entity.PosIdx);
                if (dist <= minDistSqr) {
                    //P.StopBenchmark("FindClosestMatching");
                    return entity;
                }
                if (dist < closestDist) {
                    closest = entity;
                    closestDist = dist;
                }
            }
            //P.StopBenchmark("FindClosestMatching");
            return closest;
        }

        public int FindInRadiusMatching(Vector2 position, float radiusFloat,
                T[] res, Func<T, bool> check, int max = 0) {
            //P.StartBenchmark("FindInRadiusMatching");
            var minSqrDistance = radiusFloat * radiusFloat;
            int found = 0;
            var resLen = res.Length;
            if (max == 0 || max > resLen) {
                max = resLen;
            }
            for (int i = All.Count - 1; i >= 0; i--) {
                T entity = All[i];
                if (!entity.IsActive) {
                    continue;
                }
                if (Vectors.SqrDistance(entity.Position, position) < minSqrDistance) {
                    if (check(entity)) {
                        res[found++] = entity;
                        if (found == max) { break; }
                    }
                }
            }
            //P.StopBenchmark("FindInRadiusMatching");
            return found;
        }

        // TODO use Caches.Being instead of list
        //[Obsolete("Produces garbage")]
        public int FindInRadius(List<T> result, Vector2 position, float radiusFloat) {
            //P.StartBenchmark("FindInRadius");
            var minSqrDistance = radiusFloat * radiusFloat;
            //var result = new List<T>();
            result.Clear();
            int found = 0;
            for (int i = All.Count - 1; i >= 0; i--) {
                T entity = All[i];
                if (!entity.IsActive) { continue; }
                if (Vectors.SqrDistance(entity.Position , position) < minSqrDistance) {
                    result.Add(entity);
                    found++;
                }
            }
            //P.StopBenchmark("FindInRadius");
            return found;
        }

        // Through the quad tree, so the cost follows what is in the rect, not everything on the
        // map. Adds to results without clearing them
        public int QueryInRect(Vector2 min, Vector2 max, List<T> results,
                Func<T, bool> check) {
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return quadTree.QueryNonAlloc(bounds, results, check);
        }

        public int FindVisualInRect(Vector2 startPos, Vector2 position,
                List<Entity> results, Func<Entity, bool> filter) {
            //P.StartBenchmark("FindInVisualRect");
            for (int i = 0; i < All.Count; i++) {
                T entity = All[i];
                if (!entity.IsActive) { continue; }
                if (Geometry.RectContains(
                        startPos, position, entity.SelectPosition)) {
                    if (filter == null || filter(entity)) {
                        results.Add(entity);
                    }
                }
            }
            //P.StopBenchmark("FindInVisualRect");
            return results.Count;
        }
        public int FindVisualInRect(Vector2 startPos, Vector2 position,
                List<T> results, Func<T, bool> filter) {
            //P.StartBenchmark("FindInVisualRect2");
            for (int i = All.Count - 1; i >= 0; i--) {
                T entity = All[i];
                if (!entity.IsActive) { continue; }
                if (Geometry.RectContains(
                        startPos, position, entity.SelectPosition)) {
                    if (filter == null || filter(entity)) {
                        results.Add(entity);
                    }
                }
            }
            //P.StopBenchmark("FindInVisualRect2");
            return results.Count;
        }

        public void Destroy(T entity, bool immediately = true) {
            bool needBruteForceRemoval = false;
            if (entity.Carrier != null) {
                // D.Warn("Destroying carried object, dropping: {0}", entity);
                entity.Carrier.Graphics.Drop(entity, true);
            }
            if (!Pos.IsOOB(entity.PosIdx)) {
                if (ByPos.TryGetValue(entity.PosIdx, out var list)) {
                    if (!list.Remove(entity)) {
                        needBruteForceRemoval = true;
                        D.Err("Failed to remove entity by pos: {0} at {1}", entity, Pos.String(entity.PosIdx));
                    }
                } else {
                    needBruteForceRemoval = true;
                    D.Err("Failed to remove entity by pos (list not found): {0} at {1}",
                        entity, Pos.String(entity.PosIdx));
                }
                if (needBruteForceRemoval) {
                    foreach (var posList in ByPos) {
                        if (needBruteForceRemoval) {
                            foreach (var e in posList.Value) {
                                if (e == entity) {
                                    D.Err("Found fallback by pos for {0} at {1}", e, Pos.String(posList.Key));
                                    posList.Value.Remove(entity);
                                    needBruteForceRemoval = false;
                                    break;
                                }
                            }
                        }
                    }
                    if (needBruteForceRemoval) {
                        D.Err("Failed to find fallback for entity {0} (never registered?)", entity);
                    }
                }
                quadTree.Remove(entity);
            }
            All.RemoveRef(entity);
            entitiesById.Remove(entity.Id);
            S.RemoveEntity(entity, immediately);
            OnDestroy(entity, immediately);
        }

        protected abstract void OnDestroy(T entity, bool immediately);

        public void SetData(List<T> data) {
            Initialize(S.Map);
            All.Clear();
            PreprocessEntities(data);
            if (data == null || data.Count == 0) { return; }
            All.AddRange(data);
            AfterInit();
        }

        private void PreprocessEntities(List<T> data) {
            for (int i = data.Count - 1; i >= 0; i--) {
                // Remove dead
                var d = data[i];
                if (d.Id == 0) {
                    d.IsActive = false;
                    data.RemoveAt(i);
                } else if (!d.HasDefinition) {
                    D.Err("Removing entity without def");
                    d.IsActive = false;
                    data.RemoveAt(i);
                } else {
                    AddToQuadTree(d);
                    entitiesById.Add(d.Id, d);
                }
            }
        }

        public List<T> GetData() {
            return All;
        }

        public T Find(int id) {
            if (id == 0) {
                D.Err("Trying to find item with 0 id!");
                return default;
            }
            if (entitiesById.TryGetValue(id, out var res)) {
                return res;
            }
            return null;
        }
        public List<T> FindByDef(int idH, Func<T, bool> check = null) {
            var res = new List<T>();
            for (int i = All.Count - 1; i >= 0; i--) {
                T entity = All[i];
                if (!entity.IsActive) { continue; }
                if (check != null && !check(entity)) { continue; }
                if (entity.Definition.IdH == idH) {
                    res.Add(entity);
                }
            }
            return res;
        }
        private readonly Dictionary<int, int> defCountCache = new();
        private long defCountTick;
        public int CountByDef(Def def) {
            var tick = G.Ticks;
            if (tick != defCountTick) {
                defCountCache.Clear();
                defCountTick = tick;
                // build the cache for this tick
                for (int i = All.Count - 1; i >= 0; i--) {
                    T entity = All[i];
                    if (entity.IsActive) {
                        var idH = entity.Definition.IdH;
                        if (defCountCache.TryGetValue(idH, out var c)) {
                            defCountCache[idH] = c + 1;
                        } else {
                            defCountCache.Add(idH, 1);
                        }
                    }
                }
            }
            if (defCountCache.TryGetValue(def.IdH, out var cnt)) {
                return cnt;
            }
            return 0;
        }
        public int CountByDef(Def def, Func<T, bool> check) {
            int count = 0;
            int idH = def.IdH;
            for (int i = All.Count - 1; i >= 0; i--) {
                T entity = All[i];
                if (!entity.IsActive) { continue; }
                if (entity.Definition.IdH == idH) {
                    if (!check(entity)) { continue; }
                    count++;
                }
            }
            return count;
        }
    }
}
