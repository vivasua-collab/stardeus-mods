using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Game.Components;
using Game.Constants;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using Game.Input;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using Game.Commands;
using Game.Systems;
using Game.Procgen;
using Unity.Mathematics;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.Electricity;
using Game.Systems.Creatures;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Data.Factions;
using Game.Systems.Naming;
using UnityEditor;
using Game.UI;
using Game.Modifiers;
using Game.Tools;

namespace Game.Data {
    public struct CompDamageComparer : IComparer<IComponent> {
        public int Compare(IComponent x, IComponent y) {
            float d1 = 0f;
            float d2 = 0f;
            if (x.Entity is Tile t) {
                d1 = t.Health.HealthPercent;
            } else if (x.Entity is Obj o) {
                d1 = o.Health.HealthPercent;
            } else {
                D.Err("Trying to compare damage on Being, not supported!");
            }
            if (y.Entity is Tile t2) {
                d2 = t2.Health.HealthPercent;
            } else if (y.Entity is Obj o2) {
                d2 = o2.Health.HealthPercent;
            } else {
                D.Err("Trying to compare damage on Being, not supported!");
            }
            if (d1 > d2) { return 1; }
            if (d2 > d1) { return -1; }
            return 0;
        }
    }
    public struct EntityTypeComparer : IComparer<Entity> {
        public int Compare(Entity x, Entity y) {
            if (x == null || y == null || !x.IsActive || !y.IsActive) {
                return 0;
            }
            // handle child tiles
            if (x.Definition == null) {
                if (x is Tile tx) {
                    x = tx.Transform.Parent;
                } else {
                    D.Err("Entity has no def! {0}", x);
                }
            }
            if (y.Definition == null) {
                if (y is Tile ty) {
                    y = ty.Transform.Parent;
                } else {
                    D.Err("Entity has no def! {0}", y);
                }
            }
            var lX = x.Definition.LayerId;
            var lY = y.Definition.LayerId;
            var diff = lY - lX;
            if (diff != 0) { return diff; }
            if (lX == WorldLayer.FreeEntities) {
                var xIsB = x is Being;
                var yIsB = y is Being;
                if (xIsB && !yIsB) { return -1; }
                if (!xIsB && yIsB) { return 1; }
            }
            if (x.CategoryTitle == y.CategoryTitle) {
                if (x is Being b && y is Being by) {
                    if (b.Species == by.Species) {
                        return string.Compare(b.Data.Name, by.Data.Name);
                    } else {
                        return string.Compare(b.Species.Name, by.Species.Name);
                    }
                }
                return string.Compare(x.Definition.NameT, y.Definition.NameT);
            }
            return string.Compare(x.CategoryTitle, y.CategoryTitle);
        }
    }

    public static class EntityUtils {
        public static GameState S;
        /*
        0 deg   90deg 180deg  270deg
        [0][1] ->[1]->[0][1]->[1]
                 [0]          [0]
                   [2][5]
        [3][4][5]->[1][4]
        [0][1][2]  [0][3]

        [4][5]
        [2][3]  [3][4][5]
        [0][1]->[0][1][2]

        [2][3]->[2][3]
        [0][1]  [0][1]
        */
        public static int SubtilePositions(int pos, Tile tile, int[] result) {

            var t = tile.Transform.IsChild ? tile.Transform.Parent : tile;
            var p = tile.Transform.IsChild ? tile.Transform.ParentPos : pos;
            int i = 0;
            var tr = t.Transform;
            for (int x = 0; x < tr.RotatedWidth; x++) {
                for (int y = 0; y < tr.RotatedHeight; y++) {
                    result[i++] = Pos.NeighborAt(p, x, y);
                }
            }
            return i;
        }

        public static readonly EntityTypeComparer TypeComparer = default;
        public static readonly CompDamageComparer DamageComparer = default;

        public static void ReplaceTile(GameState S, int pos, Tile prototype) {
            var layer = prototype.Definition.LayerId;
            new CmdRemoveTile(pos, layer).Execute(S);
            new CmdPlaceTile(pos, prototype, warn: false, true, true).Execute(S);
        }

        public static T GetCached<T>(GameState s, ref int id, ref T cache) where T : IComponent {
            if (id == 0) { return default; }
            if (cache == null) {
                var entity = s.FindEntity<Entity>(id);
                if (entity?.IsActive != true) {
                    D.Err("Could not find entity by target id (cache): {0}", id);
                    id = 0;
                    cache = default;
                    return default;
                }
                if (entity.TryGetComponent<T>(out var comp)) {
                    cache = comp;
                }
            } else if (!cache.Entity.IsActive) {
                id = 0;
                cache = default;
            }
            return cache;
        }
        public static void SetCached<T>(ref int id, ref T cache, T value) where T : IComponent {
            cache = value;
            if (value == null) {
                id = 0;
            } else {
                if (!value.Entity.IsActive) {
                    D.Err("Trying to set inactive entity as cache: {0}", value);
                    id = 0;
                    cache = default;
                    return;
                }
                id = cache.EntityId;
            }
        }
        public static int FindLandingTargetForBreach() {
            var area = S.Sys.Areas;
            if (S.Difficulty.Level < 0.6f || S.Rng.Chance(0.2f)) {
                if (TryFindLandingTarget(area, 1, out var target)) {
                    return target;
                }
            }
            // Find something more evil
            if (TryFindIndoors(area, out var target2)) {
                return target2;
            }

            return FindLandingTargetForIncoming(area, 0, out _);
        }
        public static bool IsLiveableAtmosphereAt(GameState S, int pos) {
            if (!S.Sys.Areas.IsEnclosed(pos)) { return false; }
            if (S.GetO2(pos) < Consts.MinOxygen) { return false; }
            var temp = S.GetTemp(pos);
            // Same range as SafetyCheckComp defaults, so this agrees with the unsafe area warning
            float tempFrom = Units.CelsiusToKelvin(-20);
            float tempTo = Units.CelsiusToKelvin(40);
            if (tempFrom > temp || tempTo < temp) {
                return false;
            }
            return true;
        }

        [Obsolete("Remove", true)]
        public static void CreateExplosion(Vector2 pos, float maxRadius, float maxStr,
                bool immediate) {
            // var wt = 1f; // TODO
            // var rng = Rng.Unseeded;
            // var rad = Explosions.SuggestRadius(maxRadius, rng, null, wt);
            // var str = Explosions.SuggestStrength(maxStr, rng, null, wt);
            // if (immediate) {
            //     new CmdCreateExplosion(pos, rad, str, delayDamageTicks: 0).Execute(S);
            // } else {
            //     S.CmdQ.Enqueue(new CmdCreateExplosion(pos, rad, str));
            // }
        }
        public static bool TryFindIndoors(AreaSys area, out int target) {
            int count = 0;
            var cacheLength = Caches.PosIdx.Length;
            foreach (var a in area.Areas.Values) {
                foreach (var p in a) {
                    if (count == cacheLength) { break; }
                    // Don't drop on derelict
                    if (!IsWithinMargin(p)) { continue; }
                    if (IsEmptyFloorWithIntegrityAt(p)) {
                        Caches.PosIdx[count++] = p;
                    }
                }
                if (count == cacheLength) { break; }
            }
            if (count > 0) {
                var idx = area.S.Rng.Range(0, count);
                var pos = Caches.PosIdx[idx];
                target = pos;
                return true;
            }
            target = Pos.Invalid;
            return false;
        }

        public static bool TryFindLandingTarget(AreaSys area, float radius,
                out int target, HashSet<int> except = null) {
            if (Arrays.IsEmpty(area.OuterAreas)) {
                target = Pos.Invalid;
                return false;
            }
            int count = 0;
            var cacheLength = Caches.PosIdx.Length;
            var rng = area.S.Rng;
            foreach (var a in area.OuterAreas.Values.OrderBy(v => rng.Value)) {
                if (a.Count < 3) { continue; }
                foreach (var p in a.OrderBy(v => rng.Value)) {
                    // Don't drop on derelict
                    if (except?.Contains(p) == true) { continue; }
                    if (!IsGoodForLanding(p)) { continue; }
                    if (count == cacheLength) { break; }
                    if (radius == 0) {
                        Caches.PosIdx[count++] = p;
                        continue;
                    }
                    // larger radius:
                    bool isOK = true;
                    // hard part - need to see if top / right is empty
                    Pos.ForEachInRadius(p, radius, false, (subP) => {
                        if (!IsGoodForLanding(subP)) {
                            isOK = false;
                            return false;
                        }
                        return true;
                    });
                    if (isOK) {
                        Caches.PosIdx[count++] = p;
                    }
                }
                if (count == cacheLength) { break; }
            }
            if (count > 0) {
                var idx = area.S.Rng.Range(0, count);
                var pos = Caches.PosIdx[idx];
                if (except?.Contains(pos) == true) {
                    target = Pos.Invalid;
                    return false;
                }
                target = pos;
                return true;
            }
            target = Pos.Invalid;
            return false;
        }

        public static int FindLandingTargetForIncoming(AreaSys area,
                float radius, out bool needsExplosion,
                HashSet<int> except = null) {
            if (TryFindLandingTarget(area, radius, out var target, except)) {
                needsExplosion = false;
                return target;
            }

            return FindLandingTargetFallbackForIncoming(area,
                out needsExplosion, except);
        }
        public static int FindLandingTargetFallbackForIncoming(AreaSys area,
                out bool needsExplosion, HashSet<int> except = null) {
            if (area.IslandCount == 0) {
                needsExplosion = false;
                return Pos.Invalid;
            }
            // Nothing to land on, our options are either to miss the ship
            // or break a outer wall
            // We will find an edge of any section and land on the edge,
            // regardless of what is there
            foreach (var isle in area.Islands.Values
                    .OrderByDescending(i => i.Count)) {
                if (IsWithinMargin(isle.First())) {
                    Caches.IntSet.Clear();
                    PosRect.InnerOutlineOf(isle, Caches.IntSet);
                    int attempts = 100;
                    while (attempts-- > 0) {
                        var pos = area.S.Rng.From(Caches.IntSet);
                        if (except?.Contains(pos) == true) {
                            continue;
                        }
                        //D.Warn("Selected pos on island edge: {0}", Pos.String(pos));
                        //CameraFocusOn(pos);
                        needsExplosion = true;
                        return pos;
                    }
                }
            }
            needsExplosion = false;
            return Pos.Invalid;
        }

        public static Tile SelfOrBlockingObjAbove(Tile t) {
            if (t == null) { return null; }
            if (t.Definition.LayerId == WorldLayer.Floor) {
                var obj = ParentObjAt(t.PosIdx);
                if (obj?.Passability.IsPassable == false) {
                    return obj;
                }
            }
            return t;
        }
        public static string DumpPositions(IEnumerable<int> positions) {
            if (positions == null) { return null; }
            var sb = Caches.StringBuilder;
            sb.Clear();
            foreach (var p in positions) {
                sb.Append(Pos.String(p));
                sb.Append(',');
            }
            return sb.ToString();
        }
        public static string DumpPath(List<int> path) {
            if (path == null) { return null; }
            var sb = Caches.StringBuilder;
            sb.Clear();
            for (int i = 0; i < path.Count; i++) {
                int p = path[i];
                sb.Append(Pos.String(p));
                if (i < path.Count - 1) {
                    sb.Append("->");
                }
            }
            return sb.ToString();
        }

        public static Func<int, Vector2> MeteorVector(Facing.Type direction,
                float angleVar, int cnt, Rng rng, out Vector2 meteorV) {
            var angle = rng.Range(-angleVar, angleVar) / 180f;
            var h = Pos.GridH;
            var w = Pos.GridW;
            var divsH = h / (float) cnt;
            var divsW = w / (float) cnt;
            float minFluc = 10f;
            float maxFluc = 30f;
            switch (direction) {
                case Facing.Type.Up: {
                        meteorV = new(angle, -1f);
                        return (i) => {
                            return ToSafe(new Vector2(
                                divsW * i + rng.Range(minFluc, maxFluc) * rng.Sign(),
                                h - rng.Range(1f, 15f)));
                        };
                    }
                case Facing.Type.Down: {
                        meteorV = new(angle, 1f);
                        return (i) => {
                            return ToSafe(new Vector2(
                                divsW * i + rng.Range(minFluc, maxFluc) * rng.Sign(),
                                rng.Range(1f, 15f)));
                        };
                    }
                case Facing.Type.Left: {
                        meteorV = new(1f, angle);
                        return (i) => {
                            return ToSafe(new Vector2(
                                rng.Range(1f, 15f),
                                divsH * i + rng.Range(minFluc, maxFluc) * rng.Sign()));
                        };
                    }
                case Facing.Type.Right: {
                        meteorV = new(-1f, angle);
                        return (i) => {
                            return ToSafe(new Vector2(
                                w - rng.Range(1f, 15f),
                                divsH * i + rng.Range(minFluc, maxFluc) * rng.Sign()));
                        };
                    }
                default: {
                        D.Err("Unknown facing type: {0}", direction);
                        meteorV = Consts.V2One;
                        return i => Consts.V2Zero;
                    }
            }
        }

        // Like MeteorVector but positions are outside the grid by extraDistance tiles.
        // Do NOT use ToSafe — these are intentionally OOB for the pending-spawn corridor.
        public static Func<int, Vector2> MeteorVectorOOB(Facing.Type direction,
                float angleVar, int cnt, Rng rng, float extraDistance, out Vector2 meteorV) {
            var angle = rng.Range(-angleVar, angleVar) / 180f;
            var h = Pos.GridH;
            var w = Pos.GridW;
            var divsH = h / (float) cnt;
            var divsW = w / (float) cnt;
            float minFluc = 10f;
            float maxFluc = 30f;
            float minEdge = extraDistance;
            float maxEdge = extraDistance + 10f;
            switch (direction) {
                case Facing.Type.Up: {
                        meteorV = new(angle, -1f);
                        return (i) => new Vector2(
                            divsW * i + rng.Range(minFluc, maxFluc) * rng.Sign(),
                            h + rng.Range(minEdge, maxEdge));
                    }
                case Facing.Type.Down: {
                        meteorV = new(angle, 1f);
                        return (i) => new Vector2(
                            divsW * i + rng.Range(minFluc, maxFluc) * rng.Sign(),
                            -rng.Range(minEdge, maxEdge));
                    }
                case Facing.Type.Left: {
                        meteorV = new(1f, angle);
                        return (i) => new Vector2(
                            -rng.Range(minEdge, maxEdge),
                            divsH * i + rng.Range(minFluc, maxFluc) * rng.Sign());
                    }
                case Facing.Type.Right: {
                        meteorV = new(-1f, angle);
                        return (i) => new Vector2(
                            w + rng.Range(minEdge, maxEdge),
                            divsH * i + rng.Range(minFluc, maxFluc) * rng.Sign());
                    }
                default: {
                        D.Err("Unknown facing type: {0}", direction);
                        meteorV = Consts.V2One;
                        return i => Consts.V2Zero;
                    }
            }
        }

        public static bool IsEnergyNodeAt(int n) {
            return GetTopmostParentTile(n)?.ENode != null;
        }
        public static bool IsEnergyNodeAboveFloorAt(int n) {
            var tile = GetTopmostParentTile(n);
            if (tile == null) { return false; }
            if (tile.Definition.LayerId == WorldLayer.Floor) { return false; }
            return tile.ENode != null;
        }

        public static int SafeSpawnPosFor(Tile tile, Rng rng) {
            int outPort;
            if (tile.Transform.HasOutPort) {
                outPort = tile.Transform.OutPort;
            } else {
                D.Err("Trying to find safe spawn pos for tile without OutPort: {0}",
                    tile.Definition.Id);
                outPort = tile.Transform.AnyPortInFirst;
            }
            if (IsEmptyFloorAt(outPort)) {
                return outPort;
            }
            var outline = Arrays.Copy(tile.Transform.Outline);
            Arrays.Shuffle(rng, outline);

            var ol = outline.Length;
            for (int i = 0; i < ol; i++) {
                var p = outline[i];
                if (IsWithinBounds(p) && IsEmptyFloorAt(p)) {
                    return p;
                }
            }
            return outPort;
        }

        public static int AdjustObjSpawnablePos(int posIdx, bool spaceIsOk = true) {
            if (Pos.IsOOB(posIdx)) {
                D.Err("Trying to adjust spawnable pos for and OOB pos: {0}",
                    posIdx);
                return Pos.Invalid;
            }
            if (posIdx == 0) {
                D.Err("Adjusting spawnable posidx from 0 to 1");
                posIdx = 1;
            }
            var rng = S.Rng;
            bool needsUnstucking = false;
            bool hasUnstucked = false;
            var obst = ParentObjAt(posIdx);
            if (obst?.IsConstructed == true) {
                // check for surface to not adjust position of food on top of oxy pump
                if (obst.Passability.IsPassable || obst.HasComponent<SurfaceComp>()) {
                    return posIdx;
                }
                needsUnstucking = true;
                // We need to move the object
                if (obst.Transform.HasAnyPort) {
                    posIdx = obst.Transform.AnyPortOutFirst;
                    hasUnstucked = true;
                } else {
                    var cnt = obst.Transform.GetSubtilePos(Caches.PosIdx);
                    for (int i = 0; i < cnt; i++) {
                        var p = Caches.PosIdx[i];
                        bool goOn = true;
                        Pos.ForEachAroundRandom(rng, p, 1, false, false, (p1) => {
                            if (Pos.IsOOB(p1)) { return true; }
                            if (IsEmptyFloorAt(p1, spaceIsOk)) {
                                hasUnstucked = true;
                                posIdx = p1;
                                goOn = false;
                                return false;
                            }
                            return true;
                        });
                        if (!goOn) { break; }
                    }
                }
            }
            if (hasUnstucked) {
                return posIdx;
            }
            var wall = WallAt(posIdx);
            if (wall?.IsConstructed == true) {
                needsUnstucking = true;
                Pos.ForEachAroundRandom(rng, posIdx, 1, false, true, (p1) => {
                    if (IsEmptyFloorAt(p1, spaceIsOk)) {
                        hasUnstucked = true;
                        posIdx = p1;
                        return false;
                    }
                    return true;
                });
            }
            if (needsUnstucking && !hasUnstucked) {
                D.Warn("Difficult case of unstucking, we will move the obj down until it's ok. From: {0}",
                    Pos.String(posIdx));
                var newPosIdx = Pos.S(posIdx);
                while (!hasUnstucked) {
                    newPosIdx = Pos.S(newPosIdx);
                    if (Pos.IsOOB(newPosIdx)) {
                        D.Warn("Failed to unstuck an object, reached OOB position while moving down!");
                        return posIdx;
                    }
                    if (IsEmptyFloorAt(newPosIdx, true)) {
                        D.Warn("Unstucked the stuck obj from {0} to {1}",
                            Pos.String(posIdx), Pos.String(newPosIdx));
                        posIdx = newPosIdx;
                        break;
                    }
                }
            }
            return posIdx;
        }

        public static Bounds ToBounds(int posIdx, Vector3 size) {
            var p = CenterOf(posIdx);
            return new(p, size);
        }

        public static Entity FindBeingOrObject(int id) {
            Entity entity = S.Beings.Find(id);
            if (entity == null) {
                entity = S.Objs.Find(id);
            }
            return entity;
        }

        public static void PositionBetween(Vector2 spot, Vector2 otherSpot,
                Obj obj, float spotBias = 0.5f, float jitter = 0.05f) {
            var pos = Vector2.Lerp(spot, otherSpot, spotBias)
                + Rng.UInsideUnitCircle() * jitter;
            obj.Transform.TeleportTo(pos);
            obj.Graphics.TeleportTo(pos);
        }

        public static bool IsTooCloseToAnyOf(int p, float tooCloseDist,
                List<Tile> tiles) {
            var tooCloseSqr = tooCloseDist * tooCloseDist;
            for (int i = tiles.Count - 1; i >= 0; i--) {
                Tile t = tiles[i];
                if (t == null) { continue; }
                if (t.Transform.IsMulti) {
                    var pv = ToVector2(p);
                    var center = t.Position;
                    var tR = t.Size.magnitude * 0.5f;
                    var dist = Vectors.Distance(pv, center) - tR;
                    if (dist <= tooCloseDist) {
                        return true;
                    }
                } else {
                    if (Pos.SqrDistance(p, t.PosIdx) <= tooCloseSqr) {
                        return true;
                    }
                }
            }
            return false;
        }
        public static bool CanReach(Being being, int pos) {
            if (being.Motion.IsGrounded) {
                return S.Sys.Areas.CanReachGrounded(being.PosIdx, pos);
            }
            return true;
        }
        public static bool CanReach(Being being, Entity entity) {
            return CanReach(being, entity.PosIdx);
        }

        public static float PriorityAdjustedRegDist(int regDist, int priority) {
            if (priority == 0) {
                D.Err("Should not try to adjust priority if it's 0!");
                return 1000;
            }
            return regDist + (5 - priority) * 0.5f;
        }

        // Haul priority is strict: any storage of a higher priority beats every closer one of a
        // lower priority. Distance only ranks storages of the same priority. Pickers start with
        // bestPriority 0 (priority 0 storages are never hauled to) and bestDist float.MaxValue.
        public static bool IsBetterStorage(int priority, float dist,
                int bestPriority, float bestDist) {
            if (priority != bestPriority) { return priority > bestPriority; }
            return dist < bestDist;
        }
        public static T PickClosest<T>(Being worker, List<T> items) where T : IComponent {
            var dist = float.MaxValue;
            var p = worker.Position;
            T closest = default;
            var count = items.Count;
            for (int i = 0; i < count; i++) {
                var e = items[i];
                var d = Vectors.SqrDistance(p, e.Entity.Position);
                if (d < dist) {
                    dist = d;
                    closest = e;
                }
            }
            return closest;
        }
        public static T PickClosest<T>(Being worker, int count, T[] items) where T : IComponent {
            var dist = float.MaxValue;
            var p = worker.Position;
            T closest = default;
            for (int i = 0; i < count; i++) {
                var e = items[i];
                var d = Vectors.SqrDistance(p, e.Entity.Position);
                if (d < dist) {
                    dist = d;
                    closest = e;
                }
            }
            return closest;
        }

        public static bool FindAllObjReachableBy<T>(Being worker,
                T[] reachable, out int count) where T : IComponent  {
            if (worker.Motion.IsGrounded) {
                var a = S.Sys.Areas;
                var islandId = a.IslandAt(worker.PosIdx);
                count = S.Objs.FindAllByType<T>(reachable, (o) => {
                    return islandId == a.IslandAt(o.Entity.PosIdx);
                });
            } else {
                count = S.Objs.FindAllByType<T>(reachable);
            }
            return count > 0;
        }

        public static bool IsNearObj(int startPos, Entity obj, int maxDist = 1) {
            if (obj is Tile tile) {
                return IsNearObj(startPos, tile, maxDist);
            }
            if (obj == null) {
                D.Err("Checking if near null obj! {0}", Pos.String(startPos));
                return false;
            }
            var op = obj.PosIdx;
            return Pos.IsNear(startPos, op, 1, 1, maxDist);
        }
        public static bool IsNearObj(int startPos, Tile obj, int maxDist = 1) {
            if (obj == null) {
                D.Err("Checking if near null tile! {0}", Pos.String(startPos));
                return false;
            }
            if (obj.Definition.LayerId == WorldLayer.Floor) {
                var objAbove = ParentObjAt(obj.PosIdx);
                if (objAbove != null && objAbove.IsConstructed) {
                    obj = objAbove;
                }
            }
            var op = obj.PosIdx;
            var w = obj.Transform.RotatedWidth;
            var h = obj.Transform.RotatedHeight;
            return Pos.IsNear(startPos, op, w, h, maxDist);
        }

        public static bool IsSurroundedBySolidFloor(int idx) {
            var gridSize = Pos.GridSize;
            for (int x = -1; x <= 1; x++) {
                for (int y = -1; y <= 1; y++) {
                    if (x == 0 && y == 0) { continue; }
                    var nPos = Pos.NeighborAt(idx, x, y);
                    if (nPos < 0 || nPos >= gridSize) {
                        // oob = not surrounded by floor
                        return false;
                    }
                    var n = S.Map.Grids[WorldLayer.Floor].Data[nPos];
                    if (n?.Integrity.CanHaveFullIntegrity != true) {
                        return false;
                    }
                }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBlankAt(int pos) {
            return !IsFloorAt(pos);
        }

        // FIXME: this does not belong here
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ToVector2(int pos) {
            return Pos.ToVector(pos, 0, 0);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ToVector2(int x, int y) {
            return S.Grid.ToWorldPos(x, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 CenterOf(int idx) {
            return Pos.ToVector(idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 PosToV2WithRandOffset(int pos, float dist = 0.33f) {
            return Pos.ToVector(pos) + Rng.UInsideUnitCircle() * dist;
        }

        public static void ShowMouseLink(Entity to, Color color, bool on) {
            if (on && to?.IsActive == true) {
                S.Sig.CameraShowLink.Send(null, to, color);
            } else {
                S.Query.CameraHideLink.Ask();
            }
        }
        public static void ShowPosLink(int to) {
            if (!Pos.IsOOBNonZero(to)) {
                S.Sig.CameraShowPosLink.Send(null,
                    CenterOf(to), Consts.V2One, CameraFocus.FlashColor());
            } else {
                S.Query.CameraHideLink.Ask();
            }
        }
        public static void ShowEntityLink(Entity from, Entity to, bool on) {
            if (UIShowing.FullScreenWidget) { return; }
            // if (from != null) {
            //     D.Throw("from not null is not supported in ShowEntityLink! Migrate!");
            // }
            if (on && to?.IsActive == true) {
                S.Sig.CameraShowLink.Send(from, to, CameraFocus.FlashColor());
            } else {
                S.Query.CameraHideLink.Ask();
            }
        }
        public static string PosOrLabelOf(Tile tile) {
            var label = tile.Label?.Label;
            if (string.IsNullOrEmpty(label)) {
                label = Pos.String(tile.PosIdx);
            }
            return label;
        }

        public static bool HideEntityLink() {
            return S.Query.CameraHideLink.Ask();
        }
        public static bool IsWithinMargin(int pos) {
            return IsWithinMargin(pos, 1, 1, !Ready.Gen, true);
        }

        public static bool IsWithinMargin(int pos, int rw, int rh) {
            return IsWithinMargin(pos, rw, rh, !Ready.Gen, true);
        }

        public static bool IsWithinMargin(int pos,
                bool ignoreMargin, bool ignoreCornerAreas) {
            return IsWithinMargin(pos, 1, 1, ignoreMargin, ignoreCornerAreas);
        }
        public static bool IsWithinMargin(int pos,
                int rotatedWidth, int rotatedHeight,
                bool ignoreMargin, bool ignoreCornerAreas) {

            if (Pos.IsOOB(pos)) { return false; }
            Pos.ToXY(pos, out var x, out var y);
            return IsWithinMargin(x, y, rotatedWidth, rotatedHeight,
                ignoreMargin, ignoreCornerAreas);
        }

        public static void LoadImportantTilesCache() {
            Caches.TileList.Clear();
            foreach (Tile t in S.DataGrids[WorldLayer.Objects].Data) {
                if (t != null && t.Definition?.IsImportant == true) {
                    Caches.TileList.Add(t);
                }
            }
            if (Caches.TileList.Count == 0) {
                D.ErrSoft("No important objects found!");
            }
        }

        public static bool TryFindSafeLandingSpot(
                out int landingSpot, float radius, out bool needsExplosion) {
            var hasChosenLandingSpot = true;
            // TODO extract duplicate code from this and AsteroidStoryEvent
            Caches.TileList.Clear();
            foreach (Tile t in S.DataGrids[WorldLayer.Objects].Data) {
                if (t != null && t.Definition?.IsImportant == true) {
                    Caches.TileList.Add(t);
                }
            }

            var target = FindLandingTargetForIncoming(S.Sys.Areas,
                radius, out needsExplosion);

            if (IsTooCloseToAnyOf(target, radius + 1, Caches.TileList)) {
                target = FindLandingTargetForIncoming(S.Sys.Areas,
                    radius, out needsExplosion);
                if (IsTooCloseToAnyOf(target, radius, Caches.TileList)) {
                    target = FindLandingTargetFallbackForIncoming(
                        S.Sys.Areas, out needsExplosion);
                }
            }
            landingSpot = target;
            return hasChosenLandingSpot;
        }

        public static Vector2 RandomMapEdgePoint(Rng rng, float offset) {
            var facing = rng.From(Facing.Types);
            return RandomMapEdgePoint(rng, facing, offset);
        }
        public static Vector2 RandomMapEdgePoint(Rng rng, Facing.Type facing, float offset) {
            float x;
            float y;
            var gw = Pos.GridW;
            var gh = Pos.GridH;
            switch (facing) {
                case Facing.Type.Down: {
                    x = rng.Range(offset, gw - 1f - offset);
                    y = offset;
                    break;
                }
                case Facing.Type.Up: {
                    x = rng.Range(offset, gw - 1f - offset);
                    y = gh - 1f - offset;
                    break;
                }
                case Facing.Type.Left: {
                    x = offset;
                    y = S.Rng.Range(offset, gh - 1f - offset);
                    break;
                }
                case Facing.Type.Right: {
                    x = gw - 1f - offset;
                    y = S.Rng.Range(offset, gh - 1f - offset);
                    break;
                }
                default: {
                    x = y = 0;
                    break;
                }
            }
            return ToSafe(new Vector2(x, y));
        }

        public static bool IsWithinMargin(int x, int y,
                int rotatedWidth, int rotatedHeight,
                bool ignoreMargin, bool ignoreCornerAreas) {
            // 1 is the minimum to have less OOB checks
            var margin = ignoreMargin ? 1 : Consts.MapMargin;
            #if UNITY_EDITOR
            if (margin > Pos.GridW * 0.15f) {
                margin = (int) (Pos.GridW * 0.15f);
            }
            #endif
            if (x < margin) { return false; }
            if (y < margin) { return false; }
            var gh = Pos.GridH;
            var gw = Pos.GridW;
            if (x > gw - margin - rotatedWidth) {
                return false;
            }
            if (y > gh - margin - rotatedHeight) {
                return false;
            }

            // Corner no-build zones are allowed during gen
            if (ignoreCornerAreas) { return true; }
            #if UNITY_EDITOR
            var cornerMargin = Consts.CornerMargin;
            if (cornerMargin > Pos.GridW * 0.25f) {
                cornerMargin = (int) (Pos.GridW * 0.25f);
            }
            if (x < cornerMargin - rotatedWidth + 1
                    && y < cornerMargin - rotatedHeight + 1) {
                return false;
            }
            if (x < cornerMargin - rotatedWidth + 1
                    && y > gh - cornerMargin - rotatedHeight) {
                return false;
            }
            if (x > gw - cornerMargin - rotatedWidth
                    && y > gh - cornerMargin - rotatedHeight) {
                return false;
            }
            if (x > gw - cornerMargin - rotatedWidth
                    && y < cornerMargin - rotatedHeight + 1) {
                return false;
            }
            #else
            if (x < Consts.CornerMargin - rotatedWidth + 1
                    && y < Consts.CornerMargin - rotatedHeight + 1) {
                return false;
            }
            if (x < Consts.CornerMargin - rotatedWidth + 1
                    && y > gh - Consts.CornerMargin - rotatedHeight) {
                return false;
            }
            if (x > gw - Consts.CornerMargin - rotatedWidth
                    && y > gh - Consts.CornerMargin - rotatedHeight) {
                return false;
            }
            if (x > gw - Consts.CornerMargin - rotatedWidth
                    && y < Consts.CornerMargin - rotatedHeight + 1) {
                return false;
            }
            #endif
            return true;
        }

        public static bool CanPlaceObjAt(Vector2 vec, bool spaceIsOk) {
            var p = ToPos(vec);
            if (!IsWithinBounds(vec) || !EntityUtils.IsEmptyFloorAt(p, spaceIsOk)) {
                return false;
            }
            var cleanUp = EntityUtils.IsEmptyFloorAt(ToPos(vec + new Vector2(0, 0.3f)), spaceIsOk);
            if (!cleanUp) { return false; }
            var cleanDown = EntityUtils.IsEmptyFloorAt(ToPos(vec - new Vector2(0, 0.3f)), spaceIsOk);
            if (!cleanDown) { return false; }
            var cleanLeft = EntityUtils.IsEmptyFloorAt(ToPos(vec - new Vector2(0.3f, 0)), spaceIsOk);
            if (!cleanLeft) { return false; }
            var cleanRight = EntityUtils.IsEmptyFloorAt(ToPos(vec + new Vector2(0.3f, 0)), spaceIsOk);
            if (!cleanRight) { return false; }
            // Check for angled floor
            var floor = EntityUtils.FloorAt(p);
            if (floor != null) { return floor.Passability.IsWalkable; }
            return true;
        }

        // The seed that produces ship computer with glass stuck in the head
        //gen_ship 1515496595 158 99
        public static Vector2 SpawnableAround(int pos, float radius, bool spaceIsOk) {
            var center = CenterOf(pos);
            var r = S.Rng;
            if (!Ready.Gen) {
                int attempts = 10;
                while (attempts-- > 0) {
                    var rng = r.InsideUnitCircle().normalized *
                        S.Rng.Range(radius * 0.5f, radius);
                    var vec = center + rng;
                    if (CanPlaceObjAt(vec, spaceIsOk)) {
                        return vec;
                    } else {
                        vec = center - rng;
                        if (CanPlaceObjAt(vec, spaceIsOk)) {
                            return vec;
                        }
                    }
                }
                #if UNITY_EDITOR
                // D.Warn("Could not find empty spot around {0}", Pos.String(pos));
                #endif
            }
            var p  = CenterOf(pos) + (r.InsideUnitCircle() * 0.25f);
            return p;
        }
        public static bool IsConstructedFloorAt(int pos) {
            var floor = FloorAt(pos);
            return floor?.IsConstructed == true;
        }
        public static bool IsConstructedWallAt(int pos) {
            var wall = WallAt(pos);
            return wall?.IsConstructed == true;
        }

        public static Entity GetWallOrObjAt(int pos, bool skipImmutable = false) {
            var obj = ParentObjAt(pos);
            if (obj?.IsConstructed == true) {
                if (!skipImmutable || !obj.IsImmutable) {
                    return obj;
                }
            }
            var wall = WallAt(pos);
            if (wall?.IsConstructed == true) {
                if (!skipImmutable || !wall.IsImmutable) {
                    return wall;
                }
            }
            return null;
        }
        public static bool IsConstructedTileAt(int pos, bool skipImmutable = false) {
            var floor = FloorAt(pos);
            if (floor?.IsConstructed == true) {
                return !skipImmutable || !floor.IsImmutable;
            }
            var obj = ParentObjAt(pos);
            if (obj?.IsConstructed == true) {
                return !skipImmutable || !obj.IsImmutable;
            }
            return false;
        }

        // A constructed floor counts as a valid construction-wave anchor only if it belongs
        // to a section — matching the same condition AreaSys.ResolveAreaTypeAt uses to admit
        // a tile into a section (CanHaveFullIntegrity || IsWalkable). Angled floors
        // (Floor01Angle: MaxIntegrity 0.5, IsWalkable false) resolve to AreaType.Space in
        // AreaSys and are unreachable by grounded workers, so they must NOT anchor a wave.
        public static bool IsSectionFloorAt(int pos) {
            var floor = FloorAt(pos);
            return floor?.IsConstructed == true
                && floor.Passability?.IsUnbuiltOverrideActive != true
                && (floor.Integrity.CanHaveFullIntegrity || floor.Passability.IsWalkable);
        }

        // Blueprint-safe version of the IsSectionFloorAt predicate: judges whether an
        // unbuilt floor tile *will* be a section floor once constructed. MaxIntegrity and
        // IsWalkableDefault are both set from the definition at OnConfig, so they are valid
        // to read before construction (unlike the live IsWalkable, which is temporarily
        // forced false while unbuilt). Used so construction-wave seed selection can avoid
        // anchoring on a tile that will never satisfy HasCardinalConstructedFloorInWave once
        // built (e.g. Floor01Angle: MaxIntegrity 0.5, not walkable), while still recognizing
        // fragile-but-walkable floors (e.g. Frame01: MaxIntegrity 0.25, walkable) as valid.
        public static bool WillBeSectionFloor(Tile floor) {
            return floor?.Integrity != null && floor.Passability != null
                && (floor.Integrity.CanHaveFullIntegrity || floor.Passability.IsWalkableDefault);
        }

        public static bool HasCardinalConstructedFloor(int pos) {
            if (IsSectionFloorAt(Pos.N(pos))) { return true; }
            if (IsSectionFloorAt(Pos.E(pos))) { return true; }
            if (IsSectionFloorAt(Pos.S(pos))) { return true; }
            if (IsSectionFloorAt(Pos.W(pos))) { return true; }
            return false;
        }

        // Is there anywhere next to pos a grounded worker could actually stand? Unlike
        // HasCardinalConstructedFloor this asks PathSys instead of just the floor layer's own
        // construction state: PathSys.IsWalkable is a composite of floor + wall + object
        // passability, so a neighbor blocked by a solid (non-door) wall correctly returns false
        // here even though the floor underneath it is fully constructed.
        // All 8 neighbors count, because work adjacency is Chebyshev, not cardinal: IsNearObj
        // (Pos.IsNear) is a box test and PathReqFlags.AdjNearby accepts a diagonal arrival, so a
        // worker standing diagonally builds a tile just as well as one standing cardinally. A
        // cardinal-only check stranded every wall-ring corner - once both of its cardinal
        // neighbors carried a constructed wall, the diagonal room interior was the only standing
        // spot left, and the corner's task waited for a worker forever.
        public static bool HasWalkableNeighbor(int pos) {
            var p = S.Sys.Path;
            if (p.IsWalkable(Pos.N(pos))) { return true; }
            if (p.IsWalkable(Pos.E(pos))) { return true; }
            if (p.IsWalkable(Pos.S(pos))) { return true; }
            if (p.IsWalkable(Pos.W(pos))) { return true; }
            if (p.IsWalkable(Pos.NE(pos))) { return true; }
            if (p.IsWalkable(Pos.NW(pos))) { return true; }
            if (p.IsWalkable(Pos.SE(pos))) { return true; }
            if (p.IsWalkable(Pos.SW(pos))) { return true; }
            return false;
        }

        // Like HasCardinalConstructedFloor, but counts any constructed floor neighbor, not
        // just a section-eligible one. Used by the non-island floor availability gate: a
        // non-island tile (e.g. angled) may build next to another already-built non-island
        // tile too, not only a section floor. Using the section-only check here would strand
        // a connected non-island group after its first tile, since every other tile in the
        // group only ever touches non-section neighbors (each other).
        public static bool HasCardinalConstructedFloorAny(int pos) {
            if (IsConstructedFloorAt(Pos.N(pos))) { return true; }
            if (IsConstructedFloorAt(Pos.E(pos))) { return true; }
            if (IsConstructedFloorAt(Pos.S(pos))) { return true; }
            if (IsConstructedFloorAt(Pos.W(pos))) { return true; }
            return false;
        }

        public static bool HasCardinalConstructedFloorInWave(int pos, int waveId) {
            if (waveId == 0) { return false; }
            static bool InWave(int p, int waveId) {
                var floor = FloorAt(p);
                return floor?.IsConstructed == true
                    && floor.Constructable.FloorWaveId == waveId
                    && IsSectionFloorAt(p);
            }
            if (InWave(Pos.N(pos), waveId)) { return true; }
            if (InWave(Pos.E(pos), waveId)) { return true; }
            if (InWave(Pos.S(pos), waveId)) { return true; }
            if (InWave(Pos.W(pos), waveId)) { return true; }
            return false;
        }

        // Mirror of HasCardinalConstructedFloorInWave: is a cardinal neighbor still an
        // unfinished blueprint of the same wave? Lets the floor wave gate tell "the wave has
        // not reached this tile yet, keep waiting" apart from "the wave is done around this
        // tile, nothing will ever open it". Only section-eligible neighbors count: a non-section
        // one (e.g. an angled floor) is never walkable once built, so waiting for it could never
        // produce a standing spot and would strand the tile forever.
        public static bool HasCardinalUnfinishedFloorInWave(int pos, int waveId) {
            if (waveId == 0) { return false; }
            static bool InWave(int p, int waveId) {
                var floor = FloorAt(p);
                return floor?.IsActive == true && !floor.IsConstructed
                    && floor.Constructable?.FloorWaveId == waveId
                    && WillBeSectionFloor(floor);
            }
            if (InWave(Pos.N(pos), waveId)) { return true; }
            if (InWave(Pos.E(pos), waveId)) { return true; }
            if (InWave(Pos.S(pos), waveId)) { return true; }
            if (InWave(Pos.W(pos), waveId)) { return true; }
            return false;
        }

        public static bool HasCardinalOpenSpace(int pos) {
            if (FloorAt(Pos.N(pos))?.IsConstructed != true) { return true; }
            if (FloorAt(Pos.E(pos))?.IsConstructed != true) { return true; }
            if (FloorAt(Pos.S(pos))?.IsConstructed != true) { return true; }
            if (FloorAt(Pos.W(pos))?.IsConstructed != true) { return true; }
            return false;
        }

        public static bool HasAdjacentSamePlacementGroup(Tile tile, int objRootPos,
                bool requireConstructed = false) {
            var outline = tile.Transform.OutlineAtWithGarbage(objRootPos, false);
            var tilePG = tile.GetComponent<ConstructableComp>().PlacementGroup;
            for (int i = outline.Length - 1; i >= 0; i--) {
                int pos = outline[i];
                var objAt = ParentObjAt(pos);
                if (objAt != null && objAt.TryGetComponent<ConstructableComp>(out var con)) {
                    if (con.PlacementGroup == tilePG) {
                        if (!requireConstructed || con.IsFinished) {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        public static bool HasAdjacentSameType(Tile tile, int objRootPos) {
            var outline = tile.Transform.OutlineAtWithGarbage(objRootPos, false);
            for (int i = 0; i < outline.Length; i++) {
                int pos = outline[i];
                var objAt = ParentObjAt(pos);
                if (objAt != null && objAt.Definition.Id == tile.Definition.Id) {
                    return true;
                }
            }
            return false;
        }

        // FIXME refactor this abomination
        public static bool HasAdjacentFloor(Tile obj, int objRootPos,
                bool requireConstructed = true) {
            if (obj == null || obj.Transform == null) {
                D.Err("Checking has adjacent floor for tile that is null or without transform: {0}", obj);
                return false;
            }
            if (obj.Transform.IsMulti) {
                if (obj.Transform.IsChild) {
                    D.Err("Reassigning child to parent!");
                    obj = obj.Transform.Parent;
                }
            }

            if (obj.Transform.HasWorkSpot) {
                var workSpot = obj.Transform.RotatedWorkSpot(objRootPos);
                var hasFloor = IsFloorAt(workSpot);
                //// D.Warn("HasFloor at WS: {0} {1} {2}", objRootPos, workSpot, hasFloor);
                return hasFloor;
            }

            D.Ass(objRootPos != Pos.Invalid,
                "Trying to do adjacent floor check for invalid tile pos: {0}", obj);
            Pos.ToXY(objRootPos, out var rX, out var rY);
            // Sorry
            // YOU ARE NOT FORGIVEN!
            // TODO refactor
            for (var x = -1; x <= obj.Transform.RotatedWidth; x++) {
                for (var y = -1; y <= obj.Transform.RotatedHeight; y++) {
                    if (x == -1 || x == obj.Transform.RotatedWidth) {
                        // we have to check every x y except where y is -1 || max
                        if (y == -1 || y == obj.Transform.RotatedHeight) {
                            // diagonal touch doesn't count.
                            continue;
                        }
                        var floor = FloorAt(Pos.FromXY(rX + x, rY + y));
                        if (floor != null) {
                            if (!requireConstructed || floor.IsConstructed) {
                                return true;
                            }
                        }
                    }
                    if (y == -1 || y == obj.Transform.RotatedHeight) {
                        // we have to check every x y except where x is -1 || max
                        if (x == -1 || x == obj.Transform.RotatedWidth) {
                            // diagonal touch doesn't count.
                            continue;
                        }
                        var floor = FloorAt(Pos.FromXY(rX + x, rY + y));
                        if (floor != null) {
                            if (!requireConstructed || floor.IsConstructed) {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDeviceUsable(Tile tile) {
            return tile != null && tile.IsActive && tile.IsConstructed &&
                tile.Health?.IsHeavilyDamaged != true
                    && (tile.ENode?.IsPowerable != false);
        }
        private const float lineOfSightStep = 0.15f;
        private const float lineOfSightStepFast = 0.75f;
        private const float lineOfSightStepSqr = lineOfSightStep * lineOfSightStep;
        private const float lineOfSightStepFastSqr = lineOfSightStepFast * lineOfSightStepFast;
        // Only full cover (walls, closed doors, full height objects) blocks sight. Partial cover
        // such as sandbags is seen and shot over: the fast raycast stopping at 50 blinded turrets
        // ringed by sandbags and turned down firing spots the precise raycast accepted.
        public const byte SightBlockingCover = 99;
        public static bool HasLineOfSightCoverFast(Vector2 start, Vector2 end,
                float minDist, float maxDist) {
            float dist;
            float maxDistSqr;
            if (maxDist == 0f) {
                maxDistSqr = 1000f;
            } else {
                maxDistSqr = maxDist * maxDist;
            }
            var direction = (end - start).normalized;
            var delta = direction * lineOfSightStepFast;
            if (minDist > 0f) {
                var d = Vectors.SqrDistance(start, end);
                if (d <= minDist * minDist) {
                    return false;
                }
                start += delta * minDist;
            }
            var startPos = ToPos(start);
            var endPos = ToPos(end);
            if (endPos == startPos) {
                // same pos, we have line of sight
                return true;
            }
            int lastPos = -1;
            int attempts = 10000;
            var coverMap = S.Query.GetCoverMap.Ask();
            do {
                start += delta;
                dist = Vectors.SqrDistance(start, end);
                if (dist <= lineOfSightStepFastSqr) {
                    return true;
                }
                if (dist >= maxDistSqr) {
                    return false;
                }
                var pos = ToPos(start);
                if (pos == lastPos || pos == startPos) { continue; }
                if (coverMap[pos] > SightBlockingCover) {
                    return false;
                }
                lastPos = pos;
            } while (dist > lineOfSightStepSqr && attempts-- > 0);
            if (attempts < 1) {
                D.Err("Took too many attempts to check for line of sight! {0} - {1}, w off {2}",
                    start, end, minDist );
                return false;
            }
            return true;

        }

        public static bool HasLineOfSightCover(Vector2 start, Vector2 end,
                float minDist, float maxDist, out byte maxCover) {
            maxCover = 0;
            float dx = end.x - start.x;
            float dy = end.y - start.y;
            float distSqr = dx * dx + dy * dy;
            if (minDist > 0f && distSqr <= minDist * minDist) {
                return false;
            }
            float maxDistSqr = maxDist > 0f ? maxDist * maxDist : 1000f;
            if (distSqr >= maxDistSqr) {
                return false;
            }
            int x0 = (int) math.floor(start.x);
            int y0 = (int) math.floor(start.y);
            int x1 = (int) math.floor(end.x);
            int y1 = (int) math.floor(end.y);
            if (x0 == x1 && y0 == y1) {
                return true;
            }
            var coverMap = S.Query.GetCoverMap.Ask();
            float dist = math.sqrt(distSqr);
            var dirNorm = new Vector2(dx / dist, dy / dist);
            int stepX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int stepY = dy > 0 ? 1 : (dy < 0 ? -1 : 0);
            float tMaxX = stepX != 0 ? ((stepX > 0 ? x0 + 1f : x0) - start.x) / dx : float.MaxValue;
            float tMaxY = stepY != 0 ? ((stepY > 0 ? y0 + 1f : y0) - start.y) / dy : float.MaxValue;
            float tDeltaX = stepX != 0 ? math.abs(1f / dx) : float.MaxValue;
            float tDeltaY = stepY != 0 ? math.abs(1f / dy) : float.MaxValue;
            int cx = x0, cy = y0;
            int gridH = Pos.GridH;
            bool hasCutCorner = false;
            for (int safety = 0; safety < 10000; safety++) {
                if (tMaxX < tMaxY) {
                    cx += stepX;
                    tMaxX += tDeltaX;
                } else {
                    cy += stepY;
                    tMaxY += tDeltaY;
                }
                if (cx == x1 && cy == y1) {
                    return true;
                }
                int pos = cx * gridH + cy;
                if (pos < 0 || pos >= coverMap.Length) {
                    return false;
                }
                var curCov = coverMap[pos];
                if (curCov > SightBlockingCover) {
                    // we want to ignore this cover if it is a corner
                    var center = new Vector2(cx + 0.5f, cy + 0.5f);
                    float distToCenter = Vectors.DistToRay(dirNorm, center,
                        start, out var closestPoint);
                    byte curCovN;
                    if (hasCutCorner || distToCenter < 0.33f) {
                        maxCover = curCov;
                        return false;
                    } else {
                        var d = closestPoint - center;
                        var nx = (int) math.sign(d.x);
                        var ny = (int) math.sign(d.y);
                        var n1 = Pos.NeighborAt(pos, 0, ny);
                        var n2 = Pos.NeighborAt(pos, nx, 0);
                        var c1 = coverMap[n1];
                        var c2 = coverMap[n2];
                        curCovN = Maths.Max(c1, c2);
                    }
                    if (curCovN >= curCov) {
                        maxCover = curCovN;
                        return false;
                    }
                    curCov = curCovN;
                    hasCutCorner = true;
                }
                if (curCov > maxCover) {
                    maxCover = curCov;
                }
            }
            D.Err("Took too many attempts to check for line of sight! {0} - {1}, w off {2}",
                start, end, minDist);
            return false;
        }

        public static Tile FirstObstacleAlongTrajectory(Vector2 start,
                Vector2 target, float step = 0.25f) {
            float dist = 0;
            var end = target;
            bool isStartOOB = Pos.IsOOB(start);
            bool wasNotOOB = false;
            do {
                start = Vector2.MoveTowards(start, end, step);
                dist = Vectors.SqrDistance(start, end);
                if (Pos.IsOOB(start)) {
                    if (!isStartOOB) {
                        // we started inside and left the bounds, it's ok
                        // D.Warn("Nothing along trajectory: {0}->{1}", start, target);
                        return null;
                    }
                    if (wasNotOOB) {
                        // we already entered the bounds and exited again,
                        // in this case it is a complete miss, no obstacles
                        // D.Warn("Nothing along trajectory that started OOB: {0}->{1}",
                        //     start, target);
                        return null;
                    }
                    continue;
                } else {
                    wasNotOOB = true;
                }
                int pos = ToPos(start);
                int endPos = ToPos(target);
                if (pos == endPos) {
                    var tile = GetTopmostParentTile(endPos);
                    if (tile?.IsConstructed == false) {
                        tile = null;
                    }
                    // D.Warn("no obstacles, hitting topmost tile at pos: {0}->{1}: {2}",
                    //     start, target, tile);
                    return tile;
                }
                var obj = ParentObjAt(pos);
                if (obj?.IsConstructed == true) {
                    if (!obj.Passability.IsPassable) {
                        return obj;
                    }
                } else {
                    var wall = WallAt(pos);
                    if (wall?.IsConstructed == true && wall.Door?.IsClosed != false) {
                        return wall;
                    }
                }
            } while (dist > 1);
            // happens some times
            // D.Err("Unexpected out of loop: {0}->{1}", start, target);
            return null;
        }

        public static bool HasLineOfSightNonCover(Vector2 start, Vector2 target,
                float offset = 0, bool ignoreObjects = false) {
            float dist = 0;
            var end = target;
            if (offset > 0) {
                if (Vectors.SqrDistance(start, end) <= offset) {
                    return false;
                }
                start = Vector2.MoveTowards(start, end, offset);
            }
            do {
                start = Vector2.MoveTowards(start, end, 1);
                dist = Vectors.SqrDistance(start, end);
                int pos = ToPos(start);
                int endPos = ToPos(target);
                if (pos == endPos) { return true; }
                var obj = ParentObjAt(pos);
                // if (obj == target) { return true; }
                if (obj?.IsConstructed == true) {
                    if (!obj.Passability.IsPassable && !ignoreObjects) {
                        return false;
                    }
                } else {
                    var wall = WallAt(pos);
                    if (wall?.IsActive == true && wall.Door?.IsClosed != false) {
                        return false;
                    }
                }
            } while (dist > 1);
            return true;
        }
        public static bool HasLineOfSightNonCover(Vector2 start, Entity target,
                float offset = 0, bool ignoreObjects = false) {
            float dist = 0;
            var end = target.Position;
            if (offset > 0) {
                if (Vectors.SqrDistance(start, end) <= offset) {
                    return false;
                }
                start = Vector2.MoveTowards(start, end, offset);
            }
            do {
                start = Vector2.MoveTowards(start, end, 1);
                dist = Vectors.SqrDistance(start, end);
                int pos = ToPos(start);
                if (pos == target.PosIdx) { return true; }
                var obj = ParentObjAt(pos);
                if (obj == target) { return true; }
                if (obj?.IsConstructed == true) {
                    if (!obj.Passability.IsPassable && !ignoreObjects) {
                        return false;
                    }
                } else {
                    var wall = WallAt(pos);
                    if (wall?.IsActive == true && wall.Door?.IsClosed != false) {
                        return false;
                    }
                }
            } while (dist > 1);
            return true;
        }

        public static int ToPosCorner(Vector2 pos) {
            return Pos.FromXY(pos.x + 0.5f, pos.y + 0.5f);
        }
        public static Vector2 ToSafe(Vector2 pos) {
            var gw = Pos.GridW - 1;
            var gh = Pos.GridH - 1;
            if (pos.x < 0 || pos.x > gw || pos.y < 0 || pos.y > gh) {
                pos = new(
                    Math.Min(Math.Max(0, pos.x), gw),
                    Math.Min(Math.Max(0, pos.y), gh)
                );
            }
            return pos;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToPosSafe(Vector2 pos) {
            pos = ToSafe(pos);
            return Pos.FromXY(pos.x, pos.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToPos(float x, float y) {
            return Pos.FromXY(x, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToPos(Vector2 pos) {
            return Pos.FromXY(pos.x, pos.y);
        }

        public static T Closest<T>(int to, ICollection<T> entries) where T : IComponent {
            if (Arrays.IsEmpty(entries)) { return default; }
            T closest = default;
            int closestDist = int.MaxValue;
            foreach (var e in entries) {
                if (!e.Entity.IsActive) { continue; }
                var dist = Pos.SqrDistance(e.Entity.PosIdx, to);
                if (dist < closestDist) {
                    closest = e;
                    closestDist = dist;
                }
            }
            return closest;
        }
        public static T ClosestMatching<T>(int to, ICollection<T> entries, Func<T, bool> check) where T : IComponent {
            if (Arrays.IsEmpty(entries)) { return default; }
            T closest = default;
            int closestDist = int.MaxValue;
            foreach (var e in entries) {
                if (!e.Entity.IsActive) { continue; }
                if (!check(e)) { continue; }
                var dist = Pos.SqrDistance(e.Entity.PosIdx, to);
                if (dist < closestDist) {
                    closest = e;
                    closestDist = dist;
                }
            }
            return closest;
        }

        public static void TileSizeFor(Def def, bool visual, out int width, out int height) {
            var tr = def.ComponentConfigFor(ComponentIdH.TileTransform);
            width = tr.GetInt(PropertyIdH.Width, 1);
            height = tr.GetInt(PropertyIdH.Height, 1);
            if (visual) {
                var tg = def.ComponentConfigFor(ComponentIdH.TileGraphics);
                if (tg.GetBool(PropertyIdH.HasHeight)) {
                    height++;
                }
            }
        }

        public static T CreatePrototype<T>(Def tileDef) where T : Entity, new() {
            if (tileDef == null) {
                D.Err("Cannot build prototype from null def!");
                return default;
            }
            if (tileDef?.IsAbstract != false) {
                D.Err("Cannot build prototype for def: {0}", tileDef);
                return default;
            }
            var t = new T { Definition = tileDef };
            t.S = S;
            The.Defs.BuildComponentsFor(t);
            return t;
        }

        public static T CreatePrototype<T>(int defIdH) where T : Entity, new() {
            var tileDef = The.Defs.FastGet(defIdH);
            return CreatePrototype<T>(tileDef);
        }

        public static T CreatePrototype<T>(string def) where T : Entity, new() {
            if (def == null) {
                D.Err("Trying to Create prototype with null def");
                return default;
            }
            var tileDef = The.Defs.Get(def);
            return CreatePrototype<T>(tileDef);
        }

        public static void CameraFocusOn(Entity entity, bool isStoryEvent = false) {
            // D.Warn("Cam focus on {0}", entity);
            if (entity == null) {
                D.Err("Trying to focus on null entity!");
                return;
            }
            if (isStoryEvent && !A.FocusOnEvents) { return; }
            if (entity is Tile) {
                S.Sig.CameraFocusEntity.Send(entity);
            } else {
                S.Sig.FollowEntity.Send(entity);
            }
        }
        public static void CameraFocusOn(int pos, bool isStoryEvent = false) {
            if (pos < 1) {
                D.Err("Trying to focus camera on invalid pos: {0}", Pos.StringD(pos));
                return;
            }
            if (isStoryEvent && !A.FocusOnEvents) { return; }
            S.Sig.CameraFocus.Send(CenterOf(pos), Consts.V2One,
                Consts.CameraFocusZoomLevel);
        }
        public static void CameraFocusOn(Vector2 pos, bool isStoryEvent = false) {
            if (isStoryEvent && !A.FocusOnEvents) { return; }
            S.Sig.CameraFocus.Send(pos, Consts.V2One,
                Consts.CameraFocusZoomLevel);
        }

        public static void CameraFocusOn(Entity root, Entity entity, bool isStoryEvent = false) {
            if (isStoryEvent && !A.FocusOnEvents) { return; }
            S.Sig.CameraFocusRooted.Send(root, entity);
        }
        public static HashSet<T> FindComponentsAround<T>(int posIdx, int range,
                bool includeSelf, int layer) {
            var res = new HashSet<T>();
            Pos.ForEachInRadius(posIdx, range, includeSelf, (n) => {
                if (TryGetComponent(n, layer, out T cF)) { res.Add(cF); }
                return true;
            });
            return res;
        }

        public static bool TryGetComponent<T>(int posIdx, int layerId, out T c) {
            if (posIdx < 0 || posIdx >= S.GridSize) {
                c = default;
                return false;
            }
            var o = S.Map.Grids[layerId].Data[posIdx];
            if (o == null) {
                c = default;
                return false;
            }
            if (!o.IsActive) {
                c = default;
                return false;
            }
            // Check if it's a child of a multitile
            if (o.Transform.IsChild) {
                o = o.Transform.Parent;
            }
            if (o.IsConstructed && o.TryGetComponent(out T comp)) {
                c = comp;
                return true;
            }
            c = default;
            return false;
        }
        // FIXME refactor to use int version
        public static UnstoredMatComp SpawnRawMaterial(int matTypeId, int amt,
                int pos, float radius, bool skipCheck = false, bool allowMerge = true) {
            return SpawnRawMaterial(
                new Mat() {
                    Type = MatType.FastGet(matTypeId),
                    StackSize = amt,
                    MaxStackSize = amt
                },
                pos, radius, skipCheck, allowMerge);
        }
        public static UnstoredMatComp SpawnRawMaterial(string matTypeId, int amt,
                int pos, float radius, bool skipCheck = false, bool allowMerge = true) {
            return SpawnRawMaterial(
                new Mat() {
                    Type = MatType.Get(matTypeId),
                    StackSize = amt,
                    MaxStackSize = amt
                },
                pos, radius, skipCheck, allowMerge);
        }
        public static UnstoredMatComp SpawnRawMaterial(MatType type, int amt,
                int pos, float radius, bool skipCheck = false, bool allowMerge = true) {
            return SpawnRawMaterial(
                new Mat() {
                    Type = type,
                    StackSize = amt,
                    MaxStackSize = amt
                },
                pos, radius, skipCheck, allowMerge);
        }

        public static bool TryMerge(UnstoredMatComp mat) {
            if (mat.TreatAsObj) {
                return false;
            }
            // Before creating, maybe some similar material already nearby?
            var existing = S.Objs.FindClosestMatching(mat.WorkSpot, (ob) => {
                if (!ob.IsCarriedByAny && ob.Pile != null) {
                    var um = ob.Pile;
                    // don't merge half eaten food
                    if (um.TreatAsObj) { return false; }
                    // Don't merge piles with different haul forbidden setting
                    if (um.IsForbidden != mat.IsForbidden) {
                        return false;
                    }
                    if (um.IsAddedToInventory && um.Type == mat.Type
                            && um.EntityId != mat.EntityId) {
                        return true;
                    }
                }
                return false;
            }, 0.6f, 0f);
            var um = existing?.Pile;
            if (um != null) {
                // D.Log("Merging piles: {0} and {1}", existing.Id, mat.EntityId);
                //bingo!
                um.StackSize += mat.Take(mat.Type, null, mat.StackSize, null);
                um.MaxStackSize = Math.Max(um.MaxStackSize, um.StackSize);
                um.UpdateMat();
                return true;
            }
            return false;
        }

        public static bool ValidateMatType(MatType type) {
            if (type == null) {
                D.Err("Trying to spawn a mat with null type");
                return false;
            }
            if (type.Def == null) {
                D.Err("Cannot spawn raw material, no def: {0}", type.Id);
                return false;
            }
            return true;
        }

        public static bool ValidateMat(Mat mat) {
            if (mat.StackSize < 1) {
                #if !PRODUCTION_BUILD
                D.Err("Spawning material with 0 stack size! {0}", mat);
                #endif
                // Empty stack
                return false;
            }
            if (!ValidateMatType(mat.Type)) {
                D.Err("Invalid mat type for matTypeId: {0}", mat.TypeId);
                return false;
            }
            return true;
        }

        // mergeIntoBlock: join a pile of the type in the same block (DropMerge), for tiles
        // broken down into their materials
        public static UnstoredMatComp SpawnRawMaterial(Mat mat, int pos, float radius,
                bool skipCheck = false, bool allowMerge = true, bool mergeIntoBlock = false) {
            if (!EntityUtils.IsWithinBounds(pos)) { return null; }
            if (!Ready.StateLock) {
                D.Err("Trying to spawn raw material while state lock is not ready! {0}",
                    mat);
                return null;
            }
            if (!ValidateMat(mat)) {
                return null;
            }
            if (allowMerge && mergeIntoBlock) {
                var into = DropMerge.FindTarget(S, mat.Type, mat.StackSize, pos);
                if (into != null) {
                    into.StackSize += mat.StackSize;
                    into.MaxStackSize = Math.Max(into.MaxStackSize, into.StackSize);
                    into.UpdateMat();
                    return into;
                }
            }
            var point = EntityUtils.SpawnableAround(pos, radius, true);

            if (allowMerge) {
                // Before creating, maybe some similar material already nearby?
                var existing = S.Objs.FindClosestMatching(pos, (ob) => {
                    if (!ob.IsCarriedByAny && ob.Pile != null) {
                        var um = ob.Pile;
                        // A fresh drop is never forbidden: merged into forbidden derelict
                        // loot it would quietly become forbidden too (TryMerge checks it)
                        if (um.IsAddedToInventory && um.Type == mat.Type
                                && !um.IsForbidden) {
                            return true;
                        }
                    }
                    return false;
                }, 0.6f, 0f);
                var um = existing?.Pile;
                if (um != null) {
                    //bingo!
                    um.StackSize += mat.StackSize;
                    um.MaxStackSize = Math.Max(um.MaxStackSize, um.StackSize);
                    um.UpdateMat();
                    return um;
                }
            }

            var obj = S.Objs.Create(point, mat.Type.Def);
            if (obj != null) {
                var unstored = obj.Pile;
                if (unstored == null) {
                    D.Err("Created pile obj, but pile component is null! {0}",
                        unstored);
                    return null;
                }
                unstored.MaxStackSize = unstored.StackSize = mat.StackSize;
                unstored.UpdateMat();
                return unstored;
            }
            return null;
        }

        public static bool IsBlankBeforeDeserialize(Tile tile) {
            return tile == null || tile.Id == 0;
        }

        public static bool HasMultiTileGraphics(Tile tile) {
            return tile?.Transform.IsChild == false
                && (tile.Transform.IsMulti || tile.Graphics.HasHeight);
        }

        public static void PlaceTile(Tile tile, int pos, bool instant) {
            if (tile.TryGetComponent<TemplateComp>(out var template)) {
                template.Spawn(pos);
                return;
            }

            if (tile.Constructable != null) {
                tile.Constructable.DoInstantBuild = instant;
            }
            var old = S.Map.Grids[tile.Definition.LayerId].Set(pos, tile);
            if (old != null) {
                S.RemoveEntity(old, resolveSubTile: true);
            }
            // This was recently placed above old removal, not sure why, should comment when
            // figure this out
            S.AddEntity(tile);
        }

        // DO NOT CHECK FOR CHILDREN HERE!
        public static Tile GetTileInLayer(int pos, int layer) {
            if (!IsWithinBounds(pos)) { return null; }
            var tile = S.Map.Grids[layer].Get(pos);
            // DO NOT CHECK FOR CHILDREN HERE!
            if (tile != null && !tile.IsActive) {
                tile = null;
            }
            return tile;
        }
        public static bool IsEmptySpaceAt(int pos) {
            if (!IsWithinBounds(pos)) { return false; }
            var grid = S.Map.Grids;
            return grid[WorldLayer.Floor].Data[pos] == null
                // solar panels
                && grid[WorldLayer.Objects].Data[pos] == null;
        }


        // DO NOT CHECK FOR CHILDREN HERE!
        public static Tile GetTopmostTile(int pos) {
            if (!IsWithinBounds(pos)) { return null; }
            Tile tile = null;
            var grid = S.Map.Grids;
            var dataLayers = RenderLayers.DataLayerIds;
            for (int li = dataLayers.Length - 1; li >= 0; li--) {
                var layer = dataLayers[li];
                tile = grid[layer].Get(pos);
                // DO NOT CHECK FOR CHILDREN HERE!
                if (tile != null && !tile.IsActive) {
                    tile = null;
                }
                if (tile != null) { break; }
            }
            return tile;
        }
        public static Tile GetTopmostParentTile(int pos) {
            var tile = GetTopmostTile(pos);
            if (tile?.Transform.IsChild == true) {
                tile = tile.Transform.Parent;
            }
            return tile;
        }

        public static bool HasComponent(Def item, string v) {
            for (int i = 0; i < item.Components.Length; i++) {
                ComponentConfig c = item.Components[i];
                if (c.Component == v) {
                    return true;
                }
            }
            return false;
        }

        public static void RotatedSize(Tile tile, out int rotWidth, out int rotHeight) {
            rotWidth = 1;
            rotHeight = 1;
            if (tile == null) { return; }
            var tr = tile.Transform;
            rotWidth = tr.RotatedWidth;
            rotHeight = tr.RotatedHeight;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Tile ParentObjAt(int pos, int layer) {
            var obj = S.Map.Grids[layer].Data[pos];
            if (obj == null) { return null; }
            if (obj.Transform.IsChild) {
                return obj.Transform.Parent;
            }
            return obj;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Tile ParentObjAt(int pos) {
            var obj = ObjAt(pos);
            if (obj == null) { return null; }
            if (obj.Transform.IsChild) {
                return obj.Transform.Parent;
            }
            return obj;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Tile ParentObj(Tile obj) {
            if (obj == null) { return null; }
            if (obj.Transform.IsChild) { return obj.Transform.Parent; }
            return obj;
        }

        public static void RotatedGraphicSize(Tile tile, out int rotW, out int rotH) {
            rotW = 1;
            rotH = 1;
            if (tile == null) { return; }
            var def = tile.Graphics;

            rotW = def.GraphicWidth;
            rotH = def.GraphicHeight;

            if (tile.Transform.IsRotated) {
                rotW = def.GraphicHeight;
                rotH = def.GraphicWidth;
            }

            if (def.HasHeight) {
                rotH++;
            }
        }

        public static bool IsRotated(Tile tile) {
            var tr = tile.Transform;
            return tr.IsRotatable && tr.IsRotated;
        }

        public static void TriggerChange(Tile tile,
                bool updatePassability = true) {
            // D.Warn("Tile changed: {0} {1}", tile.PosIdx, tile);
            var pos = tile.PosIdx;
            S.Map.Grids[tile.Definition.LayerId]
                .TriggerChanged(pos, tile, tile);
            if (updatePassability) {
                S.Sig.RecalculatePassability.Send(pos);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Tile TileAt(int pos, int layer) {
            return S.Map.Grids[layer].Data[pos];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Tile FloorAt(int posIdx) {
            return S.Map.Grids[WorldLayer.Floor].Data[posIdx];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Tile WallAt(int posIdx) {
            return S.Map.Grids[WorldLayer.Walls].Data[posIdx];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Tile ObjAt(int posIdx) {
            return S.Map.Grids[WorldLayer.Objects].Data[posIdx];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFloorAt(int idx) {
            //if (idx < 0 || idx >= S.Grid.CellCount) { return false; }
            return S.Map.Grids[WorldLayer.Floor].Data[idx] != null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDoorAt(int idx) {
            if (Pos.IsOOB(idx)) {
                D.Err("Querying if out of bounds pos is door!: {0}", idx);
                return false;
            }
            return S.Sys.Path.IsDoor(idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsConstructedTileAt(int pos, int layer) {
            if (Pos.IsOOB(pos)) { return false; }
            var tile = S.Map.Grids[layer].Data[pos];
            if (tile == null) { return false; }
            if (tile.Transform.IsChild) {
                tile = tile.Transform.Parent;
            }
            if (tile.Constructable == null) { return true; }
            return tile.IsConstructed;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWallAt(int idx) {
            return S.Map.Grids[WorldLayer.Walls].Data[idx] != null;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAirtightWallAt(int idx) {
            var wall = S.Map.Grids[WorldLayer.Walls].Data[idx];
            return wall != null && wall.Integrity.IsAirtight;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsObjAt(int idx) {
            return S.Map.Grids[WorldLayer.Objects].Data[idx] != null;
        }
        public static bool IsNothingAt(int idx) {
            return !IsFloorAt(idx) && !IsObjAt(idx);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEmptyFloorAt(int idx, bool spaceIsOk = false) {
            if (S.Map.Grids[WorldLayer.Walls].Data[idx] != null) {
                return false;
            }
            if (S.Map.Grids[WorldLayer.Objects].Data[idx] != null) {
                return false;
            }
            if (spaceIsOk) { return true; }
            return S.Map.Grids[WorldLayer.Floor].Data[idx] != null;
        }
        public static bool IsGoodForLanding(int idx) {
            var wall = S.Map.Grids[WorldLayer.Walls].Data[idx];
            if (wall != null) {
                return false;
            }
            if (S.Map.Grids[WorldLayer.Objects].Data[idx] != null) {
                return false;
            }
            var floor = S.Map.Grids[WorldLayer.Floor].Data[idx];
            if (floor == null) { return false; }
            return floor.Integrity.Integrity >= Consts.MinLandingZoneIntegrity;
        }
        public static bool IsEmptyFloorWithIntegrityAt(int idx) {
            if (S.Map.Grids[WorldLayer.Walls].Data[idx] != null) {
                return false;
            }
            if (S.Map.Grids[WorldLayer.Objects].Data[idx] != null) {
                return false;
            }
            var floor = S.Map.Grids[WorldLayer.Floor].Data[idx];
            if (floor == null) { return false; }
            return floor.Integrity.CanHaveFullIntegrity;
        }

        // Like IsEmptyFloorWithIntegrityAt, with no loose item lying there either: generation
        // puts one loose thing per tile, so none hides another (STA-1787)
        public static bool IsClearFloorAt(int idx) {
            return IsEmptyFloorWithIntegrityAt(idx) && S.Objs.FindFirstInPos(idx) == null;
        }

        public static bool IsHealthyFloorAt(int idx) {
            if (S.Map.Grids[WorldLayer.Walls].Data[idx] != null) {
                return false;
            }
            if (S.Map.Grids[WorldLayer.Objects].Data[idx] != null) {
                return false;
            }
            var f = S.Map.Grids[WorldLayer.Floor].Data[idx];
            if (f == null) {
                return false;
            }
            return !f.Health.IsDamaged;
        }

        public static int LoadMultitile(Tile tile, Tile[] tiles) {
            var position = tile.Transform.PosIdx;
            var layer = tile.Transform.LayerId;
            var posCount = SubtilePositions(position, tile, Caches.PosIdx);
            int numTiles = 0;
            for (int i = 0; i < posCount; i++) {
                var subTilePos = Caches.PosIdx[i];
                if (subTilePos == position) {
                    tiles[numTiles++] = tile;
                } else {
                    // place child tile
                    var child = new Tile();
                    child.S = tile.S;
                    child.IsActive = true;
                    var tr = new TileTransformComp() {
                        Tile = child,
                        LayerId = layer,
                        IsChild = true,
                        PosIdx = subTilePos,
                        ParentPos = position,
                        Parent = tile,
                    };
                    child.AddComponent(tr);
                    D.Ass(tile.Passability != null,
                        "Parent tile has not got Passability yet");
                    child.Passability = tile.Passability;
                    tiles[numTiles++] = child;
                }
            }
            return numTiles;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWithinBounds(int x, int y) {
            return x >= 0 && x < Pos.GridW
                && y >= 0 && y < Pos.GridH;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWithinBounds(Vector2 vec) {
            return vec.x >= 0 && vec.x < Pos.GridW
                && vec.y >= 0 && vec.y < Pos.GridH;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWithinBounds(int idx) {
            return idx >= 0 && idx < Pos.GridSize;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ClampToBounds(Vector2 v2) {
            if (!Vectors.IsSane(v2)) {
                return Consts.V2Zero;
            }
            var x = Mathf.Clamp(v2.x, 0f, S.GridWidth);
            var y = Mathf.Clamp(v2.y, 0f, S.GridHeight);
            return new Vector2(x, y);
        }
        ///<summary>Not blank and constructed if constructable</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPresent(Tile tile) {
            return tile?.IsConstructed == true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanHaveFullIntegrity(Tile tile) {
            return tile.IsConstructed && tile.Integrity.CanHaveFullIntegrity;
        }

        public static int GetReasonableSpawnAmountFor(MatType matType, Rng rng) {
            double maxValue = Math.Min(Math.Max(S.Wallet.Amount * 0.05f, 1000f), 10000f);
            maxValue *= rng.Range(0.9f, 1.1f);
            var mi = S.Market.Find(matType.Def.IdH);
            int amt = Mathf.Clamp(Mathf.CeilToInt(rng.Range(10, 50)
                    * Mathf.Sqrt(matType.Rarity)), 5, 100);
            if (mi != null) {
                var price = mi.Price;
                if (price > maxValue) {
                    amt = 1;
                } else {
                    amt = (int) math.round(maxValue / mi.Price);
                    D.Warn("Adjusting amount of spawned mi {0} for max value of {1} -> {2}",
                        mi, maxValue, amt);
                }
            } else {
                D.Err("Spawning mat type without def? {0}", matType);
            }
            if (amt < 1) {
                amt = 1;
            } else if (amt > 50) {
                amt = 50;
            }

            return amt;
        }

        public static int OtherSideOfWall(int wallPos, int curAreaId) {
            var areaS = S.Sys.Areas;
            var p = Pos.N(wallPos);
            var areaAt = areaS.At(p);
            if (areaAt != curAreaId && areaAt != -1) { return p; }
            p = Pos.S(wallPos);
            areaAt = areaS.At(p);
            if (areaAt != curAreaId && areaAt != -1) { return p; }
            p = Pos.W(wallPos);
            areaAt = areaS.At(p);
            if (areaAt != curAreaId && areaAt != -1) { return p; }
            p = Pos.E(wallPos);
            areaAt = areaS.At(p);
            if (areaAt != curAreaId && areaAt != -1) { return p; }
            D.Err("Failed to find other side of a wall, is it a single tile? {0}",
                Pos.String(wallPos));
            return p;
        }

        public static Entity SpawnDef(Def def, int posIdx,
                Rng rng = null, int quantity = 1) {
            if (def == null) { return null; }
            if (rng == null) {
                rng = S.Rng;
            }
            var rewardMat = MatType.FindByDef(def);
            if (rewardMat != null) {
                var matAmt = quantity;
                var mat = SpawnRawMaterial(rewardMat, matAmt,
                    posIdx, 0.1f, true, false);
                if (mat != null) {
                    return mat.Entity;
                }
            } else if (def.IsBeing) {
                Being being = null;
                for (int i = 0; i < quantity; i++) {
                    var vars = new GenBeingVars(Species.ForDef(def.IdH), BeingType.Neutral);
                    vars.Body = FactionDef.RandomBodyVars(S.Rng);
                    var cmd = new CmdSpawnBeing(vars , posIdx);
                    cmd.Execute(S);
                    being ??= cmd.SpawnedBeing;
                }
                if (being != null) {
                    return being;
                }
            } else {
                Obj lastObj = null;
                for (int i = 0; i < quantity; i++) {
                    lastObj = S.Objs.Create(CenterOf(posIdx),
                        def, false);
                }
                return lastObj;
            }
            return null;
        }
        public static string NameNonCodex(Entity entity) {
            if (entity is Being b) {
                return b.Data.Name;
            } else if (entity is Tile t) {
                return t.NameOrLabel;
            }
            return entity.Definition.NameT;
        }
        public static string KnownName(Entity entity) {
            if (entity is Being b) {
                return b.Data.Name;
            } else if (entity is Tile t) {
                return t.NameOrLabelCodex;
            }
            return entity.Definition.CodexLink;
        }

        public static string UnknownName(Entity entity) {
            if (entity is Being b) {
                return b.Species.Name;
            }
            return entity.Definition.CodexLink;
        }

        public static bool CheckIfConstructionIsPossible(GameState S, Being worker,
                Tile tile, bool autoAssignFloor, out string fail) {
            if (tile.Definition.LayerId == WorldLayer.Floor) {
                fail = null;
                return true;
            }

            if (tile.Constructable.IsBuiltInSpace) {
                var placement = tile.Constructable != null ?
                    tile.Constructable.Placement : PlacementConstraint.OnFloor;
                if (placement == PlacementConstraint.InSpaceAttached) {
                    if (!HasAdjacentFloor(tile, tile.PosIdx, true)) {
                        // we don't have an adjacent constructed floor next to this
                        fail = T.AdRejFloorNotConstructed;
                        return false;
                    }
                }
                if (placement == PlacementConstraint.InSpaceAttachedChained) {
                    if (!HasAdjacentFloor(tile, tile.PosIdx, true)
                            && !HasAdjacentSamePlacementGroup(
                                tile, tile.PosIdx, false)) {
                        fail = T.PlacementTipInspaceattachedchained;
                        return false;
                    }
                }
                fail = null;
                return true;
            }
            // verify if floor is there
            if (tile.Transform.IsMulti) {
                int count = tile.Transform.GetSubtilePos(Caches.PosIdx);
                bool foundFloorEverywhere = true;
                bool isAnyFloorUnbuilt = false;
                Tile unbuiltFloor = null;
                for (int i = 0; i < count; i++) {
                    var p = Caches.PosIdx[i];
                    var f = FloorAt(p);
                    if (f == null) {
                        foundFloorEverywhere = false;
                        break;
                    } else if (!f.IsConstructed) {
                        unbuiltFloor = f;
                        isAnyFloorUnbuilt = true;
                    }
                }
                if (!foundFloorEverywhere) {
                    fail = T.AdRejNoFloor;
                    if (autoAssignFloor) {
                        AssignUnbuiltFloorTask(S, unbuiltFloor, worker);
                    }
                    return false;
                } else if (isAnyFloorUnbuilt) {
                    fail = T.AdRejFloorNotConstructed;
                    return false;
                }
            } else {
                var f = FloorAt(tile.PosIdx);
                if (f == null) {
                    fail = T.AdRejNoFloor;
                    return false;
                } else if (!f.IsConstructed) {
                    if (autoAssignFloor) {
                        AssignUnbuiltFloorTask(S, f, worker);
                    }
                    fail = T.AdRejFloorNotConstructed;
                    return false;
                }
            }
            fail = null;
            return true;
        }

        public static void AssignUnbuiltFloorTask(GameState S, Tile t, Being worker) {
            if (t == null) { return; }
            var goal = t.Goal;
            if (goal == null) {
                if (!t.IsConstructed) {
                    D.Err("Unconstructed tile has no goal, creating for: {0}", t);
                    t.Constructable.CreateConstructGoal(null);
                    goal = t.Goal;
                }
                if (goal == null) {
                    D.Err("Failed creating goal for unconstructed tile! Finishing construction: {0}", t);
                    t.Constructable.FinishConstruction(false);
                    return;
                }
            }
            if (goal.GoalPriority == 0) {
                goal.SetPriority(G.Ticks, 3);
            }
            if (AIGoal.IsAvailable(goal) && goal.IsAvailabilityGateOpen(S)
                    && worker.AIAgent.Jobs.CanDo(goal.Job.IdHash)) {
                if (goal.TrySetAgent(worker.AIAgent)) {
                    goal.ChangeStateTo(AIGoalState.PreAssigned, G.Ticks, true);
                }
            }
        }

        #if !PRODUCTION_BUILD
        public static void DebugPauseFocusOn(int posIdx, string why = null) {
            if (why != null) {
                D.ErrSoft(why, Pos.String(posIdx));
            }
            if (The.TestsAreRunning) {
                if (CachedPlayerPrefs.GetInt("SlowTests", 0) == 0) {
                    // The.R.State.Clock.ForceStop();
                } else {
                    The.R.State.Clock.SetSpeed(ClockSpeed.Stopped);
                }
            } else {
                The.R.State.Clock.SetSpeed(ClockSpeed.Stopped);
            }
            The.R.Sig.CameraGoto.Send(Pos.ToVector(posIdx), 100f);
            #if UNITY_EDITOR
            EditorApplication.isPaused = true;
            #endif
        }
        public static void DebugPauseFocusOn(Entity entity, string why = null) {
            if (why != null) {
                D.ErrSoft(why, entity);
            }
            if (The.TestsAreRunning) {
                if (CachedPlayerPrefs.GetInt("SlowTests", 0) == 0) {
                    // The.R.State.Clock.ForceStop();
                } else {
                    The.R.State.Clock.SetSpeed(ClockSpeed.Stopped);
                }
            } else {
                The.R.State.Clock.SetSpeed(ClockSpeed.Stopped);
            }
            The.R.Sig.SelectEntity.Send(entity);
        }
        #endif

        public static void CameraFocusOnMap() {
            S.Sig.CameraFocus.Send(S.Grid.Center, S.Grid.Size, 0);
        }
        public static void CameraFocusOnShip() {
            var b = S.Sys.Areas.ShipBounds;
            S.Sig.CameraFocus.Send(b.Center(), b.Extents, 0);
        }
        public static void CameraFocusOn(Vector2 pos, Vector2 size) {
            S.Sig.CameraFocus.Send(pos, size, 0);
        }
        public static void CameraJumpToShip() {
            var b = S.Sys.Areas.ShipBounds;
            S.Sig.CameraFocus.Send(b.Center(), b.Extents, 0);
        }

        public static void SpawnExplosive(FactionDef faction, int pos, Rng rng = null, int count = 1,
                int timerFrom = Consts.TicksPer30Minutes,
                int timerTo = Consts.TicksPer2Hours) {
            rng ??= Rng.Unseeded;
            for (int i = 0; i < count; i++) {
                var cmd = new CmdSpawnObj(
                    CenterOf(pos) + rng.InsideUnitCircle(),
                    The.Defs.FastGet(DefIdsH.ObjMiscC42), true);
                cmd.Execute(S);
                if (cmd.Obj != null && cmd.Obj.TryGetComponent<BombComp>(out var bomb)) {
                    bomb.Faction = faction;
                    bomb.SetTimer(rng.Range(timerFrom, timerTo));
                }
            }
        }

        public static bool IsSameLayer(Tile t1, Tile t2) {
            if (t1?.IsActive != true || t2?.IsActive != true) {
                return false;
            }
            return ParentObj(t1).Definition.LayerId == ParentObj(t2).Definition.LayerId;
        }
        public static void CopyConfig(Tile source, Tile target) {
            if (source.Definition != target.Definition) {
                D.Err("Cannot copy config, source and target def doesn't match! {0} != {1}",
                    source, target);
                return;
            }
            // source / target can be inactive if it's auto rebuild
            // if (!source.IsActive || !target.IsActive) {
            //     D.Err("Cannot copy config, either source or target tile is inactive! {0} != {1}",
            //         source, target);
            //     return;
            // }
            for (int i = 0; i < source.Components.Length; i++) {
                IComponent c = source.Components[i];
                if (c is ICopyableComp cop) {
                    try {
                        ICopyableComp targetComp = (ICopyableComp) target.GetComponent(cop.NameHash);
                        cop.CopyConfigTo(targetComp);
                    } catch (Exception ex) {
                        D.LogEx(ex, "Failed copying config from/to comp: {0}",
                            ComponentState.DumpComponentInfo(c));
                    }
                }
            }
        }

        public static void CopyConnections(Tile source, Tile target) {
            if (source.ENode == null || target.ENode == null) {
                return;
            }
            if (source.ENode.Node.NumConnections == 0) {
                D.Err("No connections on source tile!");
            }
            D.Warn("Copying {0} connections from source tile", source.ENode.Node.NumConnections);
        }

        public static void SelectOrFocus(Entity entity) {
            UIUtils.CloseFullScreenUI();
            if (S.Query.IsExclusivelySelected.Ask(entity)) {
                EntityUtils.CameraFocusOn(entity);
            } else {
                S.Sig.SelectEntityUnfocused.Send(entity);
            }
        }
        public static void SelectAndFocus(Entity entity) {
            UIUtils.CloseFullScreenUI();
            S.Sig.SelectEntity.Send(entity);
        }

        public static Obj SpawnNonMatObj(Vector2 pos, Def def, float quality, Color? color) {
            var objData = S.Query.GenObjFull.Ask(def, Rng.Unseeded);
            objData.Quality = quality;
            if (color.HasValue) {
                objData.Color = color.Value;
            }
            if (objData.BodyPart != null) {
                objData.BodyPartState = BodyPartState.BrandNew();
                objData.BodyPartState.SetQuality(quality);
            }
            return S.Objs.Create(pos, objData, false);
        }

        public static Vector2 MapSizeAdjustedVelocity(Vector2 vel) {
            var mag = vel.magnitude;
            return vel.normalized * mag * MapSizeRatio;
        }

        public static float MapSizeRatio {
            get {
                var nominalSize = ModifierMapSize.Default;
                // map is 160, nom size is 320, we want speed to be 0.5
                return Pos.GridW / (float) nominalSize;
            }
        }

        public static void TryAutoConnect(Tile tile) {
            var node = tile.ENode;
            if (node == null || !node.Entity.IsActive || !node.IsRegistered) {
                S.Sig.ShowErrorMessage.Send(T.AdRejTargetUnavailable);
                return;
            }

            if (S.Sys.Electricity.TryAutoConnect(node, true)) {
                Coroutines.OnNextFrame(() => {
                    if (S.Query.IsExclusivelySelected.Ask(tile)) {
                        S.UIQuery.ReloadCurrentEntity.Ask();
                    }
                });
                return;
            }

            S.Sig.ShowErrorMessage.Send(S.Sys.Electricity.GetAutoConnectFailReason(node));
        }

    }
}
