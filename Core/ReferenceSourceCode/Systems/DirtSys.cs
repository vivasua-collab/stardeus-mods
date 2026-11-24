using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Firex;
using Game.Systems.Hints;
using Game.Utils;
using Game.Visuals;
using Game.CodeGen;
using KL.Clock;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Priority_Queue;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Systems {
    public sealed class DirtSys : GameSystem, ISaveableSpecial {
        public const string SysId = "DirtSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new DirtSys());
        }

        private int[] dirtMap;
        // Tiles under firefoam. Every ignition reads it (FirexSys.IsFoamProtected), so it is
        // kept here, next to every path that adds or removes dirt
        private bool[] foamMap;
        private int foamTiles;
        // The foam's look, created with the first foam (Firefoam.shader)
        private FirefoamBuffer foamBuffer;
        private readonly HashSet<int> dirtReservations = new();
        private readonly List<DirtData> dirtData = new();
        private readonly DirtCleaningTasks cleaningTasks;
        public DirtCleaningTasks CleaningTasks => cleaningTasks;
        public int DirtCount => dirtData.Count;
        public int FoamTiles => foamTiles;

        public DirtSys() {
            cleaningTasks = new DirtCleaningTasks(this, dirtData);
        }

        public int CountDirtNearby(int posIdx, int max = 3) {
            if (!EntityUtils.IsEmptyFloorAt(posIdx)) {
                return 0;
            }
            var reg = S.Query.RegionAt.Ask(posIdx);
            int dirtCount = 0;
            for (int i = dirtData.Count - 1; i >= 0; i--) {
                DirtData dirt = dirtData[i];
                var dreg = S.Query.RegionAt.Ask(dirt.PosIdx);
                if (dreg == reg) {
                    dirtCount++;
                    if (dirtCount >= max) {
                        return dirtCount;
                    }
                }
            }
            return dirtCount;
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            // S.Sig.BeforeInitState.AddListener(Unload);
            S.Sig.RebuildMap.AddListener(RebuildMap);
            // area ids change on every recalculation, so the dirt per area is counted again
            S.Sig.AreasRecalculated.AddListener(cleaningTasks.MarkRecount);
            S.Sig.EveryFrame.AddListener(FlushFoam);
        }

        private void FlushFoam() {
            foamBuffer?.Flush();
        }

        public bool IsReserved(int pos) {
            return dirtReservations.Contains(pos);
        }

        public void Reserve(int pos) {
            if (pos == -1) {
                D.Err("Trying to reserve dirt at -1");
                return;
            }
            dirtReservations.Add(pos);
        }

        public void Unreserve(int pos) {
            if (pos == -1) {
                D.Err("Trying to unreserve dirt at -1");
                return;
            }
            dirtReservations.Remove(pos);
        }

        private bool CanBeDirty(int posIdx) {
            if (Pos.IsOOB(posIdx)) { return false; }
            var fl = EntityUtils.FloorAt(posIdx);
            if (fl?.Integrity.CanHaveFullIntegrity != true) {
                return false;
            }
            var multitile = EntityUtils.GetTopmostParentTile(posIdx);
            if (multitile?.Passability.IsPassable == false) {
                return false;
            }
            return true;
        }

        public void Add(DirtData dirt, bool validate = true) {
            var dpi = dirt.PosIdx;
            if (validate && !CanBeDirty(dpi)) {
                return;
            }
            if (dirtMap == null) {
                D.Err("Trying to add dirt while dirt map is null! {0}", dirt);
                return;
            }
            if (dirt.IsFoam) {
                // foam a section move carries along; new foam comes through AddFoam
                if (foamMap[dpi]) { return; }
                ShowFoam(dpi, FirefoamBuffer.LongAgo);
            }
            var count = dirtMap[dpi];
            if (count < 3 || !validate || dirt.IsFoam) {
                dirtMap[dpi] = count + 1;
                dirtData.Add(dirt);
                dirt.CreateGraphic(S);
                if (count == 0) {
                    cleaningTasks.OnTileDirtied(dpi);
                }
            }
        }

        public bool HasFoam(int pos) {
            return foamMap != null && (uint) pos < (uint) foamMap.Length && foamMap[pos];
        }

        // One foam item per tile, past the cap of 3 dirt items: foam that did not land would
        // leave the tile unprotected. More foam on a foamed tile renews its timer.
        // revealDelay staggers the spray in the shader, in seconds.
        public void AddFoam(int pos, long expiresAt, float revealDelay = 0f) {
            if (dirtMap == null || !CanBeDirty(pos)) { return; }
            if (foamMap[pos]) {
                for (int i = dirtData.Count - 1; i >= 0; i--) {
                    var d = dirtData[i];
                    if (d.IsFoam && d.PosIdx == pos) {
                        d.ExpiresAt = expiresAt;
                        return;
                    }
                }
                D.Err("Foam map had foam at {0} without its dirt", Pos.String(pos));
                HideFoam(pos);
            }
            var count = dirtMap[pos];
            dirtMap[pos] = count + 1;
            dirtData.Add(DirtData.CreateFoam(pos, expiresAt));
            ShowFoam(pos, AmbientMotion.Time + revealDelay);
            if (count == 0) {
                cleaningTasks.OnTileDirtied(pos);
            }
        }

        private void ShowFoam(int pos, float revealAt) {
            if (foamMap[pos]) { return; }
            foamMap[pos] = true;
            foamTiles++;
            if (foamBuffer == null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) {
                foamBuffer = new FirefoamBuffer(S.GridSize);
            }
            foamBuffer?.Show(pos, revealAt);
        }

        private void HideFoam(int pos) {
            if (foamMap == null || !foamMap[pos]) { return; }
            foamMap[pos] = false;
            foamTiles--;
            foamBuffer?.Hide(pos);
        }

        private void ClearFoam() {
            if (foamMap != null) {
                Array.Clear(foamMap, 0, foamMap.Length);
            }
            foamTiles = 0;
            foamBuffer?.HideAll();
        }

        // Dirt with a timer wears off by itself, tile by tile (only firefoam has one)
        private void ExpireDirt(long ticks) {
            for (int i = dirtData.Count - 1; i >= 0; i--) {
                var d = dirtData[i];
                if (d.ExpiresAt == 0 || d.ExpiresAt > ticks) { continue; }
                var p = d.PosIdx;
                dirtData.RemoveAt(i);
                d.RemoveGraphic(S);
                if (d.IsFoam) {
                    HideFoam(p);
                }
                dirtMap[p]--;
                if (dirtMap[p] < 1) {
                    dirtMap[p] = 0;
                    dirtReservations.Remove(p);
                    cleaningTasks.OnTileCleaned(p);
                }
            }
        }

        public bool IsDirty(int pos) {
            #if !PRODUCTION_BUILD
            if (dirtMap == null) {
                D.Err("Asking if pos is dirty before dirtMap was initialized! {0}",
                    pos);
                return false;
            }
            if (Pos.IsOOB(pos)) {
                D.Err("Asking if an OOB pos is dirty: {0}/{1}. Map size; {2}",
                    pos, Pos.String(pos), dirtMap.Length);
                return false;
            }
            #endif
            return dirtMap[pos] > 0;
        }

        public bool TryFindClosestDirtInRadius(Being worker, int radius,
                bool sameRoom, out int dirtPos) {
            dirtPos = Pos.Invalid;
            var roomId = S.Sys.Areas.AreaAt(worker.PosIdx);
            if (dirtData.Count == 0) { return false; }
            bool found = false;
            var wp = worker.PosIdx;
            float closestDist = float.MaxValue;
            var radiusSqr = radius * radius;
            var closeEnoughSqr = Units.CloseEnoughSqr;
            var closeEnoughChance = Units.CloseEnoughChance;

            for (int i = dirtMap.Length - 1; i >= 0; i--) {
                bool isDirty = dirtMap[i] > 0;
                if (!isDirty) { continue; }
                var dist = Pos.SqrDistance(wp, i);
                if (dist > radiusSqr) { continue; }
                if (dirtReservations.Contains(i)) { continue; }
                if (dist < closestDist) {
                    if (sameRoom) {
                        if (roomId != S.Sys.Areas.AreaAt(i))  {
                            continue;
                        }
                    }
                    closestDist = dist;
                    dirtPos = i;
                    if (dist <= closeEnoughSqr && Rng.UChance(closeEnoughChance)) {
                        return true;
                    }
                    found = true;
                }
            }
            return found;
        }

        // Searched outwards through regions, not by straight line distance, so dirt right
        // behind a wall doesn't lure the worker out of the room it is cleaning. A non-zero
        // areaId keeps the search to one cleaning area (DirtCleaningTasks.AreaOf)
        public bool FindClosestDirtReachableBy(Being worker, HashSet<int> unreachable,
                out int nextDirtyPos, int maxDist, int areaId = 0) {
            nextDirtyPos = Pos.Invalid;
            if (dirtData.Count == 0) { return false; }
            searchWorker = worker;
            searchUnreachable = unreachable;
            searchAreaId = areaId;
            isCleanableBySearchWorker ??= IsCleanableBySearchWorker;
            nextDirtyPos = S.Query.FindClosestNearby.Ask(worker.PosIdx, worker.Motion,
                maxDist, isCleanableBySearchWorker);
            searchWorker = null;
            searchUnreachable = null;
            searchAreaId = 0;
            return nextDirtyPos != Pos.Invalid;
        }

        // Search context for the cached check delegate, which keeps the search allocation free
        private Being searchWorker;
        private HashSet<int> searchUnreachable;
        private int searchAreaId;
        private Func<int, bool> isCleanableBySearchWorker;

        private bool IsCleanableBySearchWorker(int pos) {
            if (dirtMap[pos] == 0) { return false; }
            if (dirtReservations.Contains(pos)) { return false; }
            if (searchUnreachable?.Contains(pos) == true) { return false; }
            if (!EntityUtils.IsWithinMargin(pos, false, false)) {
                // don't try to clean derelicts
                return false;
            }
            if (searchAreaId != 0 && cleaningTasks.AreaOf(pos) != searchAreaId) {
                return false;
            }
            var motion = searchWorker.Motion;
            if (motion.IsGrounded) {
                if (!searchWorker.Data.Safety.IsActuallySafeAt(pos)) { return false; }
                if (motion.HadNavFailureAt(pos)) { return false; }
            }
            return true;
        }

        public string Stats => $"DirtSys: Dirt count: {dirtData.Count}";

        public List<DirtData> At(int posIdx) {
            var res = new List<DirtData>();
            for (int i = 0; i < dirtData.Count; i++) {
                DirtData dirt = dirtData[i];
                if (dirt.PosIdx == posIdx) {
                    res.Add(dirt);
                }
            }
            return res;
        }

        /// <returns>true if done cleaning</returns>
        public bool CleanOneAt(int idx, out bool hadDirt, float chance = 1f) {
            hadDirt = dirtMap[idx] > 0;
            if (!hadDirt) { return true; }
            if (!Rng.UChance(chance)) { return false; }
            for (int i = dirtData.Count - 1; i >= 0; i--) {
                var item = dirtData[i];
                if (item.PosIdx == idx) {
                    dirtMap[idx]--;
                    dirtData.RemoveAt(i);
                    item.RemoveGraphic(S);
                    if (item.IsFoam) {
                        HideFoam(idx);
                    }
                    if (dirtMap[idx] < 1) {
                        dirtReservations.Remove(idx);
                        cleaningTasks.OnTileCleaned(idx);
                        return true;
                    } else {
                        return false;
                    }
                }
            }
            D.Err("Dirt map contained {0} but no dirt data was found!", idx);
            dirtMap[idx] = 0;
            HideFoam(idx);
            cleaningTasks.OnTileCleaned(idx);
            return true;
        }

        public void CleanAllAt(int idx) {
            if (dirtMap[idx] > 0) {
                dirtMap[idx] = 0;
                for (int i = dirtData.Count - 1; i >= 0; i--) {
                    var item = dirtData[i];
                    if (item.PosIdx == idx) {
                        dirtData.RemoveAt(i);
                        item.RemoveGraphic(S);
                    }
                }
                HideFoam(idx);
                cleaningTasks.OnTileCleaned(idx);
            }
        }

        public bool HasDirtReachableBy(Being being) {
            if (dirtData.Count == 0) { return false; }
            if (!being.Motion.IsGrounded) { return true; }
            var a = S.Sys.Areas;
            var beingIsland = a.IslandAt(being.PosIdx);
            for (int i = dirtMap.Length - 1; i >= 0; i--) {
                bool d = dirtMap[i] > 0;
                if (!d) { continue; }
                if (dirtReservations.Contains(i)) { continue; }
                if (a.IslandAt(i) == beingIsland) { return true; }
            }
            return false;
        }

        private void OnLoadSave(GameState state) {
            RestoreData();
            S.Sig.EntityRemoved.AddListener(OnEntityRemoved);
            state.Clock.OnTick.AddListener(OnTick);
            // D.Warn("Dirt: {0}", DirtCount);
        }

        private void OnTick(long ticks) {
            cleaningTasks.Tick(ticks);
            if (foamTiles > 0 && ticks % Consts.TicksPerHour == 0) {
                ExpireDirt(ticks);
            }
        }

        private void OnEntityRemoved(Entity entity) {
            if (entity.Definition?.LayerId == WorldLayer.Floor) {
                CleanAllAt(entity.PosIdx);
            }
        }

        public override void Unload() {
            dirtMap = null;
            foamMap = null;
            foamTiles = 0;
            foamBuffer?.Dispose();
            foamBuffer = null;
            dirtData.Clear();
            dirtReservations.Clear();
            cleaningTasks.Clear();
        }

        public string GetName() {
            return Id;
        }
        private ComponentData data;

        private ComponentData Data => data ??= new(0, GetName());

        public void SaveSpecial(SystemsDataSpecial sd) {
            if (Arrays.IsEmpty(dirtData)) { return; }
            var data = new DirtSysData {
                Dirt = new DirtData[dirtData.Count]
            };
            for (int i = 0; i < dirtData.Count; i++) {
                var d = dirtData[i];
                data.Dirt[i] = d;
            }
            sd.Dirt = data;
        }

        private DirtSysData sysData;

        public void LoadSpecial(SystemsDataSpecial sd) {
            if (sd == null) { return; }
            sysData = sd.Dirt;
        }

        private void RestoreData() {
            cleaningTasks.MarkRecount();
            dirtData.Clear();
            dirtMap ??= new int[S.GridSize];
            foamMap ??= new bool[S.GridSize];
            ClearFoam();
            if (sysData == null) { return; }
            var dirt = sysData;
            for (int i = dirt.Dirt.Length - 1; i >= 0; i--) {
                var d = dirt.Dirt[i];
                if (RestoreDirtAfterLoad(d)) {
                    var p = d.PosIdx;
                    // one foam item per tile
                    if (d.IsFoam && foamMap[p]) { continue; }
                    dirtData.Add(d);
                    dirtMap[p]++;
                    if (d.IsFoam) {
                        ShowFoam(p, FirefoamBuffer.LongAgo);
                    }
                }
            }
            sysData = null;
        }

        private bool RestoreDirtAfterLoad(DirtData d) {
            var pos = d.PosIdx;
            if (CanBeDirty(pos)) {
                d.CreateGraphic(S);
                return true;
            } else {
                RemoveAt(pos, false);
                return false;
            }
        }

        // For generation (island float apart)
        public List<DirtData> RemoveAt(int pos, bool returnRemovedDirt = true) {
            // section moves call it per tile and put the dirt back elsewhere
            cleaningTasks.MarkRecount();
            List<DirtData> res = null;
            for (int i = dirtData.Count - 1; i >= 0; i--) {
                DirtData d = dirtData[i];
                if (d.PosIdx == pos) {
                    dirtData.RemoveAt(i);
                    if (returnRemovedDirt) {
                        res ??= new();
                        res.Add(d);
                    }
                }
            }
            dirtMap[pos] = 0;
            // foam put back elsewhere shows again through Add
            HideFoam(pos);
            return res;
        }

        public void RebuildMap() {
            cleaningTasks.MarkRecount();
            Array.Clear(dirtMap, 0, dirtMap.Length);
            ClearFoam();
            for (int i = dirtData.Count - 1; i >= 0; i--) {
                DirtData d = dirtData[i];
                var p = d.PosIdx;
                if (EntityUtils.IsFloorAt(p)) {
                    dirtMap[p]++;
                    if (d.IsFoam) {
                        ShowFoam(p, FirefoamBuffer.LongAgo);
                    }
                } else {
                    d.RemoveGraphic(S);
                    dirtData.RemoveAt(i);
                }
            }
        }
    }
}
