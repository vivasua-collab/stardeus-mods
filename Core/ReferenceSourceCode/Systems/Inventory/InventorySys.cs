#if UNITY_EDITOR
// #define DEBUG_INVENTORY
#endif
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using System.Buffers;
using Game.Systems.Reg;
using Game.Systems.AI;
using Game.Systems.Trade;
using Game.Systems.Crafting;
using Game.Systems.UIUX;

namespace Game.Systems.Inventory {
    // TODO use StorageType when calculating remaining amounts, etc
    public sealed class InventorySys : GameSystem, ISaveable {
        public const string SysId = "InventorySys";
        public override string Id => SysId;
        // FIXME: Why is this a list?
        private readonly List<IMatStorage> materialStorages = new();
        private readonly Dictionary<int, List<IMatStorage>> groupStorages = new();
        private readonly List<IMatStorage> commonStorages = new();
        // Storages split from the piles, so per tick queries do not walk every pile
        private readonly List<IMatStorage> nonPileStorages = new();
        public readonly PileIndex Piles = new();
        private readonly List<IHaulTarget> haulTargets = new();
        public readonly MatClaims Claims = new();
        public List<IMatStorage> Storages => materialStorages;
        private float autoHaulFreq;
        private long matCountCacheTick;
        private readonly Dictionary<MatType, int> matCountCache = new();
        private readonly Dictionary<MatType, BInt> matCountsB = new();
        private AreaSys areas;
        private FullStorageUI fullUI;
        private const int bindableCap = 64;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new InventorySys());
        }
        public bool TryToRemove(MatType matType, int amt, int groupIdH) {
            if (!groupStorages.TryGetValue(groupIdH, out var storages)) {
                return false;
            }
            var needToTake = amt;
            for (int i = storages.Count - 1; i >= 0; i--) {
                var s = storages[i];
                if (!s.Entity.IsActive) { continue; }
                var avail = s.Available(matType);
                if (avail > 0) {
                    var took = s.Take(matType, null,
                        Mathf.Min(needToTake, avail), null);
                    needToTake -= took;
                    D.Warn("Removed {0}x{1} from {2}", matType.NameT, took, s.Entity);
                }
                if (needToTake < 1) {
                    return true;
                }
            }
            return false;
        }

        // All or nothing: the full amount is counted first, and only then taken. Taking
        // as it went once left a short sale half done - the items were gone and the
        // trade or quest delivery failed anyway (STA-1693).
        public bool TrySell(MatType matType, int amt, bool remove) {
            var storages = Storages;
            var sellable = 0;
            for (int i = storages.Count - 1; i >= 0 && sellable < amt; i--) {
                var s = storages[i];
                if (!CanSellFrom(s)) { continue; }
                sellable += s.AvailableWithoutReservation(matType);
            }
            if (sellable < amt) { return false; }
            if (!remove) { return true; }

            var needToTake = amt;
            for (int i = storages.Count - 1; i >= 0 && needToTake > 0; i--) {
                var s = storages[i];
                if (!CanSellFrom(s)) { continue; }
                var avail = s.AvailableWithoutReservation(matType);
                if (avail < 1) { continue; }
                var took = s.Take(matType, null, Mathf.Min(needToTake, avail), null);
                needToTake -= took;
                D.Warn("Sold {0}x{1} from {2}", matType.NameT, took, s.Entity);
            }
            if (needToTake > 0) {
                D.Err("Sold {0} short by {1} after counting {2} sellable", matType, needToTake,
                    sellable);
                return false;
            }
            return true;
        }

        private static bool CanSellFrom(IMatStorage s) {
            if (!s.Entity.IsActive) { return false; }
            return s is not UnstoredMatComp pile || pile.IsTakeableWithoutWorker;
        }
        protected override void OnInitialize() {
            autoHaulFreq = Tunables.AutoHaulFrequency;
            if (autoHaulFreq < 0.1f) {
                autoHaulFreq = 0.1f;
            }

            fullUI = new FullStorageUI(S);

            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.TradeExecuted.AddListener(OnExecuteTrade);

            S.UIQuery.GetBindableMatCountFor.SetResponder(GetBindableMatCount);
        }

        private BInt GetBindableMatCount(MatType mt) {
            if (!matCountsB.TryGetValue(mt, out var bInt)) {
                var cnt = matCountCache.TryGetValue(mt, out var count) ? count : 0;
                // bindable capacity cannot be too low, or production overview widget will fail to load
                bInt = new BInt(cnt, capacity: bindableCap);
                matCountsB[mt] = bInt;
            }
            return bInt;
        }

        private void OnExecuteTrade(TradeUI parameter) {
            ExpireCountCache();
        }

        private void OnLoadSave(GameState state) {
            areas = state.Sys.Areas;
            curHaulIdx = -1;
            state.Clock.OnTick.AddListener(OnTick);
            RefreshCountCache();
            ValidateEnergyOutput();
        }

        private void ValidateEnergyOutput() {
            foreach (var mat in MatType.Known.Values) {
                var output = mat.EnergyOutput;
                var craftables = Craftable.FindForDef(mat.Def);
                if (!Arrays.IsEmpty(craftables)) {
                    foreach (var cr in craftables) {
                        if (cr.IgnoreEnergyOutput) { continue; }
                        // disposable
                        if (Arrays.IsEmpty(cr.Ingredients)) { continue; }
                        var ingrOutput = 0f;
                        foreach (var ingr in cr.Ingredients) {
                            ingrOutput += ingr.Type.EnergyOutput * ingr.StackSize;
                        }
                        var ingrReduced = ingrOutput;
                        if (cr.OutputMultiplier > 1) {
                            ingrReduced /= cr.OutputMultiplier;
                        }
                        if (ingrReduced < output * 0.7f) {
                            if (cr.OutputMultiplier > 0) {
                                D.Err("{0} has output of {1}, but craftable ingredients: {2} have output of {3}/{4}={5}",
                                    mat.Id, output, cr.Id, ingrOutput, cr.OutputMultiplier, ingrReduced);
                            } else {
                                D.Err("{0} has output of {1}, but craftable ingredients: {2} have output of {3}",
                                    mat.Id, output, cr.Id, ingrOutput);
                            }
                        }
                    }
                }
            }
        }

        private long nextHaulOrderTick;
        private bool isHauling;
        private void OnTick(long ticks) {
            if (!Ready.Gen) { return; }
            RefreshCountCache();
            ProcessHaulTargets(ticks);
            if (ticks % Consts.TicksPer15Minutes == 11) {
                fullUI.Check(commonStorages);
            }
            CheckRefillables(ticks);
            if (ticks % Consts.TicksPerMinute == 37) {
                Claims.Sweep(ticks);
            }
        }

        private void CheckRefillables(long ticks) {
            if (ticks % Consts.TicksPer2Hours != 74) { return; }
            var refillables = S.Components.AllByType<RefillableStorageComp>();
            if (Arrays.IsEmpty(refillables)) { return; }
            foreach (RefillableStorageComp refillable in refillables) {
                if (refillable.IsConfigured && !refillable.HasAnyGoal) {
                    refillable.CreateOrUpdateRefillGoal();
                }
            }
        }

        private int curHaulIdx;
        private void ProcessHaulTargets(long ticks) {
            if (isHauling) {
                ProcessHaulTargetsSync(ticks);
                return;
            }
            if (ticks < nextHaulOrderTick) { return; }
            ProcessHaulTargetsSync(ticks);
        }
        private void ProcessHaulTargetsSync(long ticks) {
            var maxIdx = haulTargets.Count - 1;
            if (maxIdx < 0) {
                isHauling = false;
                curHaulIdx = -1;
                nextHaulOrderTick = ticks + Consts.TicksPer15Minutes;
                return;
            }
            // D.Log("ProcessHaulTargets: {0}/{1}", curHaulIdx, maxIdx);
            isHauling = true;
            if (curHaulIdx <= 0 || curHaulIdx > maxIdx) {
                curHaulIdx = maxIdx;
            }
            int processed = 0;
            var startTime = Time.realtimeSinceStartupAsDouble;
            for (int i = curHaulIdx; i >= 0; i--) {
                IHaulTarget ht = haulTargets[i];
                // we are doing the second loop
                var curTarget = ht;
                if (!curTarget.Entity.IsActive) {
                    D.Err("Inactive object in haul targets! {0}", curTarget.Entity);
                }
                curTarget.TryOrderHauling();
                curHaulIdx = i;
                processed++;
                if (processed % 5 == 0) {
                    var time = Time.realtimeSinceStartupAsDouble;
                    if (time - startTime > 0.001d) {
                        // max 0.001ms for this
                        // D.Log("processed: {0} in {1:0.00000}s", processed, time - startTime);
                        return;
                    }
                }
            }
            var w = S.Situation.Workers;
            if (w < 1) { w = 1; }
            var ticksTillNext = (Consts.TicksPerHour / w) / autoHaulFreq;
            nextHaulOrderTick = ticks + Mathf.RoundToInt(ticksTillNext);
            isHauling = false;
        }


        public bool HasAnyOf(List<MatType> matTypes) {
            for (int i = 0; i < matTypes.Count; i++) {
                MatType m = matTypes[i];
                if (HasAny(m)) {
                    return true;
                }
            }
            return false;
        }

        // FIXME shard by defId and / or type
        private readonly List<Mat> matsCache = new();

        public List<IMatStorage> StoragesByGroup(int groupId) {
            if (groupStorages.TryGetValue(groupId, out var res)) {
                return res;
            }
            return null;
        }

        public string CountColored(Def def, int greenThreshold, bool prependX = false) {
            var cnt = CountOf(def);
            var cntStr = prependX ? Units.XNum(cnt) : Units.Num(cnt);
            if (cnt < 1) {
                return Texts.Red(cntStr);
            }
            if (cnt > greenThreshold) {
                return Texts.Green(cntStr);
            }
            return Texts.Yellow(cntStr);
        }

        public int CountOf(Def def) {
            if (def == null) { return 0; }
            if (def.MatType != null) {
                return AvailableCount(def.MatType);
            } else if (def.IsBeing) {
                return S.Query.CountFriendlyBeingsBySpecies.Ask(Species.ForDef(def.IdH).IdH, true);
            } else if (def.LayerId < WorldLayer.FreeEntities) {
                return CountTiles(def);
            } else {
                return CountObjs(def);
            }
        }
        public int CountOfProduct(Def def) {
            if (def == null) { return 0; }
            if (def.IsBeing) {
                return S.Query.CountFriendlyBeingsBySpecies.Ask(Species.ForDef(def.IdH).IdH, false);
            }
            return CountOf(def);
        }

        private int CountObjs(Def def) {
            return S.Query.ObjCountByDef.Ask(def);
        }

        private int CountTiles(Def def) {
            var map = S.Map;
            if (map?.TryAllByDef(def, out var all) == true) {
                var cnt = 0;
                foreach (var obj in all.Values) {
                    if (obj is Tile t && t.IsConstructed) {
                        cnt++;
                    }
                }
                return cnt;
            }

            return 0;
        }

        public int CountByGroup(int group) {
            if (!groupStorages.TryGetValue(group, out var stores)) {
                return 0;
            }
            var count = 0;
            foreach (var s in stores) {
                if (s.AvailableForGroup(group, matsCache)) {
                    count += matsCache.Count;
                }
            }
            return count;
        }
        public readonly HashSet<MatType> KnownMaterials = new();

        public void RegisterKnownMaterial(MatType mat) {
            if (mat == null) {
                D.Err("Trying to add null material");
                return;
            }
            KnownMaterials.Add(mat);
        }

        public bool HasAny(MatType mat) {
            if (mat == null) {
                D.Err("Mat type is null in HasAny!");
                return false;
            }
            RefreshCountCache();
            if (matCountCache.TryGetValue(mat, out var count)) {
                return count > 0;
            }
            return false;
        }

        #if !PRODUCTION_BUILD
        public void TestClearCountCache() {
            matCountCacheTick = 0;
        }
        #endif

        // FIXME refactor to keep count updated all the time instead of caching it
        private void ExpireCountCache() {
            matCountCacheTick = 0L;
        }
        private int matCountChecksum = 0;
        private void RefreshCountCache() {
            var tick = G.Ticks;
            if (matCountCacheTick == tick) { return; }
            matCountCacheTick = tick;
            matCountCache.Clear();
            var mc = nonPileStorages.Count;
            for (int i = 0; i < mc; i++) {
                IMatStorage storage = nonPileStorages[i];
                if (storage.IsPublic) {
                    // used to also check for not store only, datoid loader needs this
                    // && storage.StorageType != StorageType.StoreOnly
                    storage.AppendMatCounts(matCountCache);
                }
            }
            // Piles are public and count their whole stack, kept as a running total
            foreach (var kv in Piles.Totals) {
                if (kv.Value > 0) {
                    AppendMatCount(matCountCache, kv.Key, kv.Value);
                }
            }
            int newChecksum = MatCountChecksum();
            if (newChecksum != matCountChecksum) {
                matCountChecksum = newChecksum;
                S.Sig.MatCountChanged.Send(matCountCache);
                foreach (var mBind in matCountsB) {
                    if (matCountCache.TryGetValue(mBind.Key, out var count)) {
                        mBind.Value.Set(count);
                    } else {
                        mBind.Value.Set(0);
                    }
                }
            }
        }

        private int MatCountChecksum() {
            int sum = 0;
            foreach (var kv in matCountCache) {
                int keyId = kv.Key.IdH;
                var amt = kv.Value;
                sum = unchecked(sum * 31 + keyId + amt);
            }
            return sum;
        }

        public int AvailableCount(MatType mat) {
            if (mat == null) {
                D.Err("Mat type is null in AvailableCount!");
                return 0;
            }
            RefreshCountCache();
            if (matCountCache.TryGetValue(mat, out var result)) {
                return result;
            }
            return 0;
        }

        // AvailableCount without forbidden piles: what the crew may actually use. The UI
        // shows AvailableCount, forbidden stock is still owned
        public int AllowedCount(MatType mat) {
            var count = AvailableCount(mat) - Piles.ForbiddenOf(mat);
            return count > 0 ? count : 0;
        }

        // A forbidden pile is no source for our own crew (see Forbidding.md). Every
        // automatic search for something to take checks this
        public static bool IsForbiddenFor(IMatStorage s, Being worker) {
            return s is UnstoredMatComp pile && pile.IsForbidden
                && (worker == null || worker.Data.CaresAboutForbidden);
        }

        // AllowedCount minus whatever a higher priority demand has earmarked, so a
        // task that can be fully served never drains the stock another task is waiting
        // to accumulate. See MatClaims.
        public int AvailableFor(MatType mat, IMatRequester asker, int askerPriority) {
            var count = AllowedCount(mat);
            if (count < 1) { return 0; }
            var reserved = Claims.Reserved(mat, asker, askerPriority, G.Ticks);
            return reserved >= count ? 0 : count - reserved;
        }

        // maxRegDist: storages more region hops away are skipped, -1 = no limit. Ignored
        // when AI precision is off
        public IMatStorage FindClosestReachableStorage(Being worker,
                HashSet<IMatStorage> ignore, Func<IMatStorage, bool> check,
                    bool forTaking,
                    bool adjustForPriority = false, int maxRegDist = -1) {
            // var start = Time.realtimeSinceStartupAsDouble;
            Caches.StorageList.Clear();
            for (int i = Storages.Count - 1; i >= 0; i--) {
                IMatStorage s = Storages[i];
                if (ignore?.Contains(s) == true) { continue; }
                if (!s.IsPublic) { continue; }
                if (s.Entity.IsCarriedByAny) { continue; }
                if (forTaking && s.StorageType == StorageType.StoreOnly) {
                    continue;
                }
                if (adjustForPriority && s.Priority == 0) {
                    continue;
                }
                if (IsForbiddenFor(s, worker)) { continue; }
                var sp = forTaking ? s.OutPort : s.InPort;
                if (sp > 0 && worker.Motion.HadNavFailureAt(sp)) {
                    continue;
                }
                if (!check(s)) { continue; }
                Caches.StorageList.Add(s);
            }
            // var cnt = Caches.StorageList.Count;
            var res = ResolveClosestStorage(Caches.StorageList, worker, adjustForPriority,
                forTaking, maxRegDist);
            // var end = Time.realtimeSinceStartupAsDouble;
            // D.Warn("Resolving closest storage from: {0}->{1}. Took: {2:0.0000}s. Tick: {3}",
            //     Storages.Count, cnt, end - start, G.Ticks);
            return res;
        }
        public IMatStorage FindClosestReachableStorageByGroup(int group, Being worker,
                HashSet<int> ignore, Func<IMatStorage, bool> filter,
                bool safeOnly, bool forTaking, bool adjustForPriority = false) {
            // var start = Time.realtimeSinceStartupAsDouble;
            Caches.StorageList.Clear();

            var safety = worker.Data.Safety;
            // food and drink for the worker itself: only storages it can walk to by a route
            // it may take (BeingReach), any route when it risks its life
            var reach = S.Sys.Path.Reg.Reach.For(worker.Motion, !safeOnly);
            for (int i = Storages.Count - 1; i >= 0; i--) {
                IMatStorage s = Storages[i];
                if (ignore?.Contains(s.EntityId) == true) { continue; }
                if (!s.IsPublic) { continue; }
                if (adjustForPriority && s.Priority == 0) { continue; }
                if (s.Entity.IsCarriedByAny) { continue; }
                if (IsForbiddenFor(s, worker)) { continue; }
                var port = forTaking ? s.OutPort : s.InPort;
                if (safeOnly && !safety.IsActuallySafeAt(port)) { continue; }
                if (!reach.CanReach(port)) { continue; }
                if (!s.AvailableForGroup(group)) { continue; }
                if (filter?.Invoke(s) == false) { continue; }
                Caches.StorageList.Add(s);
            }
            // var cnt = Caches.StorageList.Count;
            var res = ResolveClosestStorage(Caches.StorageList, worker, adjustForPriority, forTaking);
            // var end = Time.realtimeSinceStartupAsDouble;
            // D.Warn("Resolving closest storage from (group): {0}->{1}. Took: {2:0.0000}s. Tick: {3}",
            //     Storages.Count, cnt, end - start, G.Ticks);
            return res;
        }

        private readonly List<Obj> nearObjs = new();
        private static readonly Func<Obj, bool> isLoosePile = o => o.Pile != null
            && !o.IsCarriedByAny;

        // The closest pile for taking within radius tiles of origin that passes check. Piles
        // nearby come from the obj quad tree: scanning every storage cost a hauler about
        // 0.7 ms per extra pile it looked for once a broken up derelict left thousands of
        // piles. precise false ranks by straight distance, without a region search
        public UnstoredMatComp FindClosestPileNear(Being worker, int origin, int radius,
                HashSet<IMatStorage> ignore, Func<UnstoredMatComp, bool> check,
                int maxRegDist, bool precise) {
            nearObjs.Clear();
            var center = EntityUtils.CenterOf(origin);
            var half = new Vector2(radius + 1f, radius + 1f);
            S.Objs.QueryInRect(center - half, center + half, nearObjs, isLoosePile);
            var list = Caches.StorageList;
            list.Clear();
            for (int i = 0; i < nearObjs.Count; i++) {
                var pile = nearObjs[i].Pile;
                if (!pile.IsAddedToInventory) { continue; }
                if (ignore?.Contains(pile) == true) { continue; }
                var sp = pile.OutPort;
                if (sp > 0 && worker.Motion.HadNavFailureAt(sp)) { continue; }
                if (!check(pile)) { continue; }
                list.Add(pile);
            }
            nearObjs.Clear();
            return (UnstoredMatComp) ResolveClosestStorage(list, worker, false, true,
                maxRegDist, precise);
        }

        private IMatStorage ResolveClosestStorage(List<IMatStorage> list, Being worker,
                bool adjustForPriority, bool forTaking, int maxRegDist = -1,
                bool precise = true) {
            var cnt = list.Count;
            if (cnt == 0) {
                return null;
            }
            var isG = worker.Motion.IsGrounded;
            var wp = worker.PosIdx;
            IMatStorage closest = null;
            float closestDist = float.MaxValue;

            if (!precise || !S.Prefs.GetBool(Pref.AIPrecision, PrefH.AIPrecision, true)) {
                for (int i = 0; i < cnt; i++) {
                    var store = list[i];
                    var d = Pos.SqrDistance(wp, forTaking ? store.OutPort : store.InPort);
                    if (d < closestDist) {
                        if (d < 5) {
                            return store;
                        }
                        closest = store;
                        closestDist = d;
                    }
                }
                return closest;
            }


            // One region search from the worker covers every candidate; a path search per
            // candidate got slow with thousands of loose piles on the map
            var intPool = ArrayPool<int>.Shared;
            var targets = intPool.Rent(cnt);
            var resPathLengths = intPool.Rent(cnt);
            var penaltyPool = ArrayPool<float>.Shared;
            var penalties = penaltyPool.Rent(cnt);
            for (int i = 0; i < cnt; i++) {
                var s = list[i];
                targets[i] = forTaking ? s.OutPort : s.InPort;
                penalties[i] = adjustForPriority
                    ? EntityUtils.PriorityAdjustedRegDist(0, s.Priority) : 0f;
            }
            var req = new RegDistsReq {
                FromPos = wp,
                Grounded = isG,
                Permissions = worker.Motion.Permissions,
                Targets = targets,
                Count = cnt,
                Penalties = penalties,
                // Rng.UValue below
                Jitter = 1f,
                MaxDist = maxRegDist,
            };

            if (S.Query.RegionDists.Ask(req, resPathLengths)) {
                for (int i = 0; i < cnt; i++) {
                    var dist = resPathLengths[i];
                    if (dist < 0) { continue; }
                    var s = list[i];
                    float adjDist;
                    if (adjustForPriority) {
                        adjDist = EntityUtils.PriorityAdjustedRegDist(
                            dist, s.Priority);
                    } else {
                        adjDist = dist;
                    }
                    // to break the monotony of always going for the same item
                    // in same region
                    adjDist += Rng.UValue;

                    if (adjDist < closestDist) {
                        closest = s;
                        closestDist = adjDist;
                        if (dist < 0.5f) {
                            break;
                        }
                    }
                }
            }
            intPool.Return(targets);
            intPool.Return(resPathLengths);
            penaltyPool.Return(penalties);
            return closest;
        }

        // A flyer found no reachable storage holding mat at all. Every storage that
        // holds it is out of its reach, so each gets the same mark a walk failure
        // would have given it - otherwise an enclosed storage is silently filtered
        // out by the region path query and never flagged to the player.
        public void MarkUnreachableByFlying(MatType mat, Being worker) {
            if (mat == null || worker == null || worker.Motion.IsGrounded) { return; }
            for (int i = nonPileStorages.Count - 1; i >= 0; i--) {
                Mark(nonPileStorages[i]);
            }
            var piles = Piles.Of(mat);
            if (piles != null) {
                for (int i = piles.Count - 1; i >= 0; i--) {
                    Mark(piles[i]);
                }
            }

            void Mark(IMatStorage s) {
                if (!s.IsPublic) { return; }
                if (s.StorageType == StorageType.StoreOnly) { return; }
                if (!s.Entity.IsActive) { return; }
                // Carried piles stay in the inventory but are no storage to fetch from
                if (s.Entity.IsCarriedByAny) { return; }
                // Not a source at all, so out of reach says nothing about it
                if (IsForbiddenFor(s, worker)) { return; }
                if (s.AvailableWithoutReservation(mat) < 1) { return; }
                s.MarkAsUnreachableByFlying(worker);
            }
        }

        private int findIdx;
        private int findCnt;
        private MatType findMat;
        private readonly IMatStorage[] findStorages = new IMatStorage[256];
        public int FindStoragesContaining(MatType mat, IMatStorage[] res, bool includeStoreOnly) {
            int c = 0;
            var rl = res.Length;
            foreach (var s in Storages) {
                if (!s.IsPublic) { continue; }
                if (!includeStoreOnly && s.StorageType == StorageType.StoreOnly) { continue; }
                if (!s.Entity.IsActive) { continue; }
                if (s.AvailableWithoutReservation(mat) > 0) {
                    res[c++] = s;
                    if (c >= rl) { return c;}
                }
            }
            return c;
        }
        public IMatStorage FindNextMat(MatType mat, bool includeStoreOnly) {
            if (findMat != mat) {
                findMat = mat;
                findIdx = 0;
            }
            findCnt = FindStoragesContaining(mat, findStorages, includeStoreOnly);
            if (findCnt == 0) { return null; }
            if (findIdx >= findCnt) { findIdx = 0; }
            return findStorages[findIdx++];
        }

        /// <summary>
        /// Ignores TreatAsObj mats
        /// </summary>
        /// <param name="mat"></param>
        /// <param name="worker"></param>
        /// <param name="ignore"></param>
        /// <param name="forTaking">If this is false, then it's for storing. Changes the InPort / OutPort in search</param>
        /// <param name="adjustForPriority"></param>
        /// <returns></returns>
        public IMatStorage FindClosestReachableStorage(MatType mat, Being worker,
                HashSet<IMatStorage> ignore, bool forTaking, bool adjustForPriority = false) {
            var isG = worker.Motion.IsGrounded;
            int wIsland = 0;
            var wp = worker.PosIdx;
            if (isG) {
                wIsland = areas.IslandAt(wp);
            }
            // var ticks = G.Ticks;
            var list = Caches.StorageList;
            list.Clear();
            IMatStorage near = null;
            for (int i = nonPileStorages.Count - 1; i >= 0; i--) {
                if (Consider(nonPileStorages[i])) { return near; }
            }
            var piles = Piles.Of(mat);
            if (piles != null) {
                for (int i = piles.Count - 1; i >= 0; i--) {
                    if (Consider(piles[i])) { return near; }
                }
            }

            return ResolveClosestStorage(list, worker, adjustForPriority, forTaking);

            // true when s is right next to the worker, which ends the search
            bool Consider(IMatStorage s) {
                if (forTaking && s.StorageType == StorageType.StoreOnly) {
                    return false;
                }
                if (ignore?.Contains(s) == true) { return false; }
                if (!s.IsPublic || !s.Entity.IsActive) { return false; }
                if (s.Entity.IsCarriedByAny) { return false; }
                if (IsForbiddenFor(s, worker)) { return false; }
                if (adjustForPriority && s.Priority == 0) { return false; }
                var av = s.Available(mat);
                // over-reserved storages report a negative amount
                if (av < 1) { return false; }
                if (av == 1 && s is UnstoredMatComp pile) {
                    if (pile.TreatAsObj) {
                        return false;
                    }
                }
                var sp = forTaking ? s.OutPort : s.InPort;
                if (Pos.IsNear(sp, wp, 1, 1)) {
                    near = s;
                    return true;
                }
                if (isG && !areas.CanReachGroundedIslandToPos(wIsland, sp)) {
                    // check if its a pile and it is adjacent
                    return false;
                }
                if (worker.Motion.HadNavFailureAt(sp)) { return false; }
                if (!worker.Data.Safety.IsActuallySafeAt(sp)) { return false; }
                list.Add(s);
                return false;
            }
        }
        // Used by story event, will return reserved, carried, etc
        public int FindUnstoredMaterialsByGroup(UnstoredMatComp[] cache, int group) {
            int found = 0;
            var lim = cache.Length;
            var mc = materialStorages.Count;
            //var tick = G.Ticks;
            for (int i = 0; i < mc; i++) {
                IMatStorage storage = materialStorages[i];
                var ent = storage.Entity;
                if (!ent.IsActive) { continue; }
                //if (ent.Carrier != null) { continue; }
                if (!storage.AcceptsGroup(group)) { continue; }
                if (storage is UnstoredMatComp unstored) {
                    if (unstored.StackSize > 0) {
                        cache[found++] = unstored;
                        if (found == lim) { return found; }
                    }
                }
            }
            return found;
        }

        // reachFrom: the asking storage's position, so only piles it may auto-haul are
        // found (AutoHaulReach) and the ones out of reach never fill the cache. -1 (Pos.Invalid)
        // finds piles anywhere
        public int FindHaulableUnstoredMaterials(UnstoredMatComp[] cache,
                Func<MatType, bool> filter, int reachFrom = -1) {
            int found = 0;
            var lim = cache.Length;
            var checkReach = reachFrom != Pos.Invalid;
            var reachIsland = checkReach ? areas.IslandAt(reachFrom) : 0;
            CollectHaulablePiles();
            var rangeCount = haulableRanges.Count;
            if (rangeCount == 0) { return 0; }
            // The cache fills type by type, so each call starts at the next type: otherwise
            // the first type would always be the one a storage orders hauled
            haulTypeCursor = (haulTypeCursor + 1) % rangeCount;
            for (int r = 0; r < rangeCount; r++) {
                var range = haulableRanges[(haulTypeCursor + r) % rangeCount];
                if (!filter(range.Type)) { continue; }
                var end = range.Start + range.Count;
                for (int i = range.Start; i < end; i++) {
                    var unstored = haulablePiles[i];
                    // an earlier asker this tick may have ordered it hauled already
                    if (!IsHaulable(unstored)) { continue; }
                    if (!checkReach || AutoHaulReach.CanAutoHaul(areas, reachIsland,
                            unstored.Entity.PosIdx)) {
                        cache[found++] = unstored;
                        if (found == lim) { return found; }
                    }
                }
            }
            return found;
        }

        private int haulTypeCursor;
        // Every storage with auto-haul asks for haulable piles in turn, and with many workers
        // that is nearly every tick. Most piles cannot be ordered (already ordered, forbidden
        // derelict loot), so the ones that can are collected once per tick, like haulable objs
        private long haulableTick = -1;
        private readonly List<UnstoredMatComp> haulablePiles = new();
        private readonly List<(MatType Type, int Start, int Count)> haulableRanges = new();

        private void CollectHaulablePiles() {
            var ticks = G.Ticks;
            if (haulableTick == ticks) { return; }
            haulableTick = ticks;
            haulablePiles.Clear();
            haulableRanges.Clear();
            var types = Piles.Types;
            for (int t = 0; t < types.Count; t++) {
                var type = types[t];
                var piles = Piles.Of(type);
                var start = haulablePiles.Count;
                for (int i = 0; i < piles.Count; i++) {
                    if (IsHaulable(piles[i])) {
                        haulablePiles.Add(piles[i]);
                    }
                }
                if (haulablePiles.Count > start) {
                    haulableRanges.Add((type, start, haulablePiles.Count - start));
                }
            }
        }

        private static bool IsHaulable(UnstoredMatComp pile) {
            var ent = pile.Entity;
            if (!ent.IsActive || ent.IsCarriedByAny) { return false; }
            if (pile.TreatAsObj || pile.IsForbidden) { return false; }
            // Two flyers could not get there (UnstoredMatComp.MarkAsUnreachableByFlying)
            if (pile.IsMarkedUnreachable) { return false; }
            // already has a storing goal - a haul target could not order another
            if (ent.Goal != null) { return false; }
            return pile.UnreservedCount > 0;
        }

        public bool HasEnough(MatType type, int amount, int islandId = -1,
                Being being = null) {
            int accumulator = 0;
            bool ignoreDanger = true;
            BeingSafety safety = null;
            if (being != null) {
                safety = being.Data.Safety;
                ignoreDanger = safety.IgnoreDanger;
            }
            var mc = nonPileStorages.Count;
            for (int i = 0; i < mc; i++) {
                if (AddsUp(nonPileStorages[i])) { return true; }
            }
            var piles = Piles.Of(type);
            if (piles != null) {
                for (int i = 0; i < piles.Count; i++) {
                    if (AddsUp(piles[i])) { return true; }
                }
            }
            return false;

            bool AddsUp(IMatStorage st) {
                if (st.StorageType == StorageType.StoreOnly) {
                    return false;
                }
                if (IsForbiddenFor(st, being)) { return false; }
                // Cheapest first: most storages hold none of it
                var available = st.Available(type);
                if (available < 1) { return false; }
                var sp = st.OutPort;
                if (islandId != -1) {
                    if (!areas.CanReachGroundedIslandToPos(islandId, sp)) {
                        return false;
                    }
                }
                if (ignoreDanger || safety.IsActuallySafeAt(sp)) {
                    accumulator += available;
                    return accumulator >= amount;
                }
                return false;
            }
        }

        // HasEnough that also has to cover what a higher priority demand earmarked, so
        // the asker only sees stock nobody stronger is waiting on. See MatClaims.
        public bool HasEnoughFor(MatType type, int amount, IMatRequester asker,
                int askerPriority) {
            return HasEnough(type,
                amount + Claims.Reserved(type, asker, askerPriority, G.Ticks));
        }

        public override void Unload() {
            #if DEBUG_INVENTORY
            // D.Log("Inventory clearing material storages");
            #endif
            materialStorages.Clear();
            nonPileStorages.Clear();
            Piles.Clear();
            haulablePiles.Clear();
            haulableRanges.Clear();
            haulableTick = -1;
            haulTargets.Clear();
            Claims.Clear();
            nextHaulOrderTick = 0;
            this.commonStorages.Clear();
            fullUI?.Reset();
        }

        public void Add(IHaulTarget haulTarget) {
            haulTargets.Add(haulTarget);
        }

        public void Remove(IHaulTarget haulTarget) {
            haulTargets.RemoveRef(haulTarget);
        }

        public void Add(IMatStorage storage) {
            if (!storage.Entity.IsActive) {
                D.Err("Trying to add inactive storage! {0}", storage);
                return;
            }
            if (storage.Entity is Tile t && !t.IsConstructed) {
                D.Err("Trying to add unconstructed storage to inventory! {0}", storage);
                return;
            }
            if (storage.IsAddedToInventory) {
                D.Err("Trying to add duplicate storage to inventory! {0}", storage);
                return;
            }
            #if !PRODUCTION_BUILD
            if (materialStorages.ContainsRef(storage)) {
                D.Err("skipping storage, already added to inventory! {0}",
                   storage);
                return;
            }
            #endif
            materialStorages.Add(storage);
            if (storage.HaulTarget != null) {
                haulTargets.Add(storage.HaulTarget);
            }
            storage.IsAddedToInventory = true;
            if (storage is UnstoredMatComp pile && pile.Type != null) {
                Piles.Add(pile);
            } else {
                nonPileStorages.Add(storage);
            }
            #if DEBUG_INVENTORY
            D.Log("Added to inventory: {0}", storage);
            #endif

            var g = storage.Groups;
            if (g != null) {
                foreach (var gr in g) {
                    if (!groupStorages.TryGetValue(gr, out var groupS)) {
                        groupS = new();
                        groupStorages.Add(gr, groupS);
                    }
                    groupS.Add(storage);
                }
            } else {
                commonStorages.Add(storage);
            }

            if (storage.Contents != null) {
                foreach (var item in storage.Contents) {
                    RegisterKnownMaterial(item.Key);
                }
            }
        }

        public void Remove(IMatStorage storage) {
            if (!storage.IsAddedToInventory) {
                D.Err("Trying to remove storage tht is not in inventory: {0}",
                    storage);
            }
            #if DEBUG_INVENTORY
            // D.Log("Removing from inventory: {0}", storage);
            #endif
            if (!materialStorages.RemoveRef(storage)) {
                D.Err("Could not remove mat storage (not there) {0}", storage);
            } else if (storage is UnstoredMatComp pile && pile.Type != null) {
                Piles.Remove(pile);
            } else {
                nonPileStorages.RemoveRef(storage);
            }
            if (storage.HaulTarget != null) {
                Remove(storage.HaulTarget);
            }
            storage.IsAddedToInventory = false;
            var g = storage.Groups;
            if (g != null) {
                foreach (var gr in g) {
                    if (!groupStorages.TryGetValue(gr, out var groupS)) {
                        D.Err("Trying to remove a storage that didn't have group list: {0} {1}",
                            gr, storage);
                    } else {
                        groupS.RemoveRef(storage);
                    }
                }
            } else {
                commonStorages.RemoveRef(storage);
            }
        }

        public void OnPileStackChanged(UnstoredMatComp pile, int delta) {
            // Piles without a type are not indexed (a broken mod)
            if (pile.IndexInType < 0) { return; }
            Piles.OnStackChanged(pile, delta);
        }

        public void OnPileForbidChanged(UnstoredMatComp pile) {
            if (pile.IndexInType < 0) { return; }
            Piles.OnForbidChanged(pile);
        }

        public bool HasCompatibleStorages(MatType mat) {
            // Piles are take only, so they never count here
            var mc = nonPileStorages.Count;
            for (int i = 0; i < mc; i++) {
                IMatStorage storage = nonPileStorages[i];
                if (storage.StorageType == StorageType.TakeOnly ||
                        !storage.IsUsable || storage.IsFull) { continue; }
                // Haulers skip these too (LoadCompatibleStorages). Counting them made a
                // storing order wait forever for a storage no worker would deliver to,
                // instead of failing with "No Storage Unit"
                if (storage.Priority == 0) { continue; }
                if (storage.Entity is Tile t && t.Health.IsDamaged) { continue; }
                if (storage.WillAccept(mat, 1, out _)) {
                    return true;
                }
            }
            return false;
        }

        public List<IMatStorage> FindCompatibleStorages(Being worker, MatType mat,
                int amount, bool forTaking, bool adjustForPriority = true) {
            var result = new List<IMatStorage>();
            var g = mat.GroupH;
            if (g == 0) {// || result.Count == 0) {
                // Fall back to common storages
                LoadCompatibleStorages(commonStorages, result, worker, mat, amount, forTaking, adjustForPriority);
            } else if (groupStorages.TryGetValue(g, out List<IMatStorage> storages)) {
                // Evaluate group storages
                LoadCompatibleStorages(storages, result, worker, mat, amount, forTaking, adjustForPriority);
            }
            return result;
        }

        private void LoadCompatibleStorages(List<IMatStorage> materialStorages,
                List<IMatStorage> result, Being worker, MatType mat, int amount,
                    bool forTaking, bool adjustForPriority) {
            var mc = materialStorages.Count;
            var grounded = worker.Motion.IsGrounded;
            var workerIslandId = areas.IslandAt(worker.PosIdx);
            // var brain = worker.Brain;
            for (int i = 0; i < mc; i++) {
                IMatStorage storage = materialStorages[i];
                if (adjustForPriority && storage.Priority == 0) { continue; }
                if (storage.Entity is Tile t) {
                    if (t.Health.IsDamaged) {
                        continue;
                    }
                } else {
                    D.Err("Storage on non-tile obj! {0}", storage.Entity);
                }
                if (storage.StorageType == StorageType.TakeOnly ||
                        !storage.IsUsable || storage.IsFull) { continue; }
                var targetAmt = amount;
                bool willAccept = storage.WillAccept(mat, targetAmt, out bool wouldAcceptSome);
                if (!willAccept && !wouldAcceptSome) {
                    continue;
                }
                var ws = forTaking ? storage.OutPort : storage.InPort;
                if (grounded && !areas.CanReachGroundedIslandToPos(workerIslandId, ws)) {
                    continue;
                }
                if (!worker.Data.Safety.IsActuallySafeAt(ws)) {
                    continue;
                }
                if (worker.Motion.HadNavFailureAt(ws)) {
                    continue;
                }
                // A storage that takes only part of the load is offered too: the hauler
                // delivers what fits, then continues to the next storage (strict priority)
                if (willAccept || (wouldAcceptSome && amount > 1)) {
                    result.Add(storage);
                }
            }
        }

        public Dictionary<MatType, int> GetRemainingMatsForUI(bool refresh) {
            if (refresh) {
                LoadRemainingMaterials(availableCache);
            }
            return availableCache;
        }

        public void LoadRemainingMaterials(Dictionary<MatType, int> remainingMaterials) {
            remainingMaterials.Clear();
            var mc = materialStorages.Count;
            foreach (var m in KnownMaterials) {
                remainingMaterials.Add(m, 0);
            }
            for (int i = 0; i < mc; i++) {
                IMatStorage storage = materialStorages[i];
                if (storage is UnstoredMatComp pile) {
                    var type = pile.Type;
                    if (type == null) { continue; }
                    AppendMatCount(remainingMaterials, type, pile.Available(type));
                } else {
                    if (storage.Contents == null) { continue; }
                    foreach (var item in storage.Contents) {
                        int matCnt = item.Value;
                        var type = item.Key;
                        if (type == null) {
                            D.Err("Invalid material: {0} in {1}", matCnt, storage.Entity);
                            continue;
                        }
                        AppendMatCount(remainingMaterials, type, matCnt);
                    }
                }
            }
        }
        /// <summary>
        /// Same walk as LoadRemainingMaterials, but keeps materials sitting in storage devices
        /// apart from UnstoredMatComp piles (floor piles and piles a being is carrying), so
        /// wealth accounting can show where the colony's materials actually are.
        /// </summary>
        public void LoadRemainingMaterials(Dictionary<MatType, int> stored,
                Dictionary<MatType, int> unstored) {
            stored.Clear();
            unstored.Clear();
            var mc = materialStorages.Count;
            for (int i = 0; i < mc; i++) {
                IMatStorage storage = materialStorages[i];
                if (storage is UnstoredMatComp pile) {
                    var type = pile.Type;
                    if (type == null) { continue; }
                    AppendMatCount(unstored, type, pile.Available(type));
                } else {
                    if (storage.Contents == null) { continue; }
                    foreach (var item in storage.Contents) {
                        var type = item.Key;
                        if (type == null) { continue; }
                        AppendMatCount(stored, type, item.Value);
                    }
                }
            }
        }

        public static void AppendMatCount(Dictionary<MatType, int> mats,
                MatType type, int count) {
            if (mats.TryGetValue(type, out var cnt)) {
                mats[type] = cnt + count;
            } else {
                mats.Add(type, count);
            }
        }

        public void LoadRemainingMaterials(
                Dictionary<MatType, int> remainingMaterials,
                    string[] propertyFilter,
                    string[] groupFilter,
                    bool includeZero) {
            if (propertyFilter == null && groupFilter == null) {
                LoadRemainingMaterials(remainingMaterials);
                return;
            }

            remainingMaterials.Clear();
            if (includeZero) {
                foreach (var knownMat in KnownMaterials) {
                    if (propertyFilter != null) {
                        if (!Arrays.Contains(propertyFilter, knownMat.Property)) {
                            continue;
                        }
                    }
                    if (groupFilter != null) {
                        if (!Arrays.Contains(groupFilter, knownMat.Group)) {
                            continue;
                        }
                    }
                    remainingMaterials.Add(knownMat, 0);
                }
            }
            var mc = materialStorages.Count;
            for (int i = 0; i < mc; i++) {
                IMatStorage storage = materialStorages[i];
                if (storage is UnstoredMatComp pile) {
                    if (propertyFilter != null) {
                        if (!Arrays.Contains(propertyFilter, pile.Type.Property)) {
                            continue;
                        }
                    }
                    if (groupFilter != null) {
                        if (!Arrays.Contains(groupFilter, pile.Type.Group)) {
                            continue;
                        }
                    }
                    MatType type = pile.Type;
                    if (remainingMaterials.TryGetValue(type, out var cnt)) {
                        remainingMaterials[type] = cnt + pile.Available(type);
                    } else {
                        remainingMaterials.Add(type, pile.Available(type));
                    }
                } else {
                    var con = storage.Contents;
                    if (con == null) { continue; }
                    foreach (var item in con) {
                        var type = item.Key;
                        if (propertyFilter != null) {
                            if (!Arrays.Contains(propertyFilter, type.Property)) {
                                continue;
                            }
                        }
                        if (groupFilter != null) {
                            if (!Arrays.Contains(groupFilter, type.Group)) {
                                continue;
                            }
                        }
                        int mat = item.Value;
                        if (remainingMaterials.TryGetValue(type, out var cnt)) {
                            remainingMaterials[type] = cnt + mat;
                        } else {
                            remainingMaterials.Add(type, mat);
                        }
                    }
                }
            }
        }

        private readonly Dictionary<MatType, int> availableCache = new();

        public string GetName() {
            return Id;
        }

        ///<returns>true if there is a deficit</returns>
        public bool CheckAndNotifyDeficit(MatType type, int amt, AIGoal goal,
                IMatRequester req) {
            if (!HasEnough(type, amt)) {
                var c = goal.Target;
                req.NotifyMatDeficit(type);
                return true;
            }
            return false;
        }

        private ComponentData data;

        private ComponentData Data => data ??= new(0, GetName());

        public ComponentData Save() {
            var mats = new string[KnownMaterials.Count];
            int i = 0;
            foreach (var mat in KnownMaterials) {
                if (mat == null) {
                    D.Err("Null material in known mats!");
                    continue;
                }
                mats[i++] = mat.Id;
            }
            Data.SetStringSet("KnownMats", mats);
            return data;
        }

        public void Load(ComponentData data) {
            var knownMats = data.GetStringSet("KnownMats", null);
            if (Arrays.IsEmpty(knownMats)) { return; }
            for (int i = 0; i < knownMats.Length; i++) {
                string mat = knownMats[i];
                var matType = MatType.Get(mat);
                if (matType != null) {
                    RegisterKnownMaterial(matType);
                }
            }
            // don't start hauling right after load
            nextHaulOrderTick = S.Ticks + Consts.TicksPer30Minutes;
        }
    }
}
