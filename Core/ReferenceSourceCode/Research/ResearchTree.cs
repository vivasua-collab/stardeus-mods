#if !PRODUCTION_BUILD
// #define DEBUG_RESEARCH_BALANCE
// #define VERBOSE_VALIDATION
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Migrations;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Modifiers;
using Game.Systems.UIUX;

namespace Game.Research {
    public class ResearchTree {

        [SelfInit]
        public static void Cleanup() {
            _all = null;
            _researchables = null;
            nextTierUnlockPct = 0;
        }

        public GameState S;
        private static float nextTierUnlockPct;
        public readonly Dictionary<MatType, BInt> DatoidCounts = new();
        public MatType[] DatoidTypes;

        private int numUnlocked = -1;
        private int numLocked = -1;

        private int maxTier;
        public int MaxTier {
            get {
                if (maxTier < 0) {
                    UpdateCounts();
                }
                return maxTier;
            }
        }

        public void InitializeDatoids() {
            var tempList = new List<MatType>();
            foreach (var mat in MatType.HByGroup[MatGroupIdH.Datoid]
                    .OrderBy(d => d.Rarity)) {
                tempList.Add(mat);
                DatoidCounts[mat] = new BInt();
            }
            DatoidTypes = tempList.ToArray();
        }

        private int lastCountHash;

        public bool UpdateDatoidCounts() {
            var stores = S.Sys.Inventory.StoragesByGroup(MatGroupIdH.Datoid);
            if (stores == null) {
                if (lastCountHash != 0) {
                    foreach (var cnt in DatoidCounts.Values) {
                        cnt.Set(0);
                    }
                    lastCountHash = 0;
                    return true;
                }
                return false;
            }
            var cache = Caches.MatCounts;
            cache.Clear();
            for (int i = stores.Count - 1; i >= 0; i--) {
                var store = stores[i];
                if (store is UnstoredMatComp) {
                    continue;
                }
                store.AppendMatCounts(cache);
            }
            var newHash = 0;
            foreach (var dc in DatoidTypes) {
                if (cache.TryGetValue(dc, out var cnt)) {
                    newHash += Hashes.Quick(dc.IdH, cnt);
                    DatoidCounts[dc].Set(cnt);
                } else {
                    DatoidCounts[dc].Set(0);
                }
            }
            if (newHash != lastCountHash) {
                lastCountHash = newHash;
                S.Sig.DatoidCountsUpdated.Send(DatoidCounts);
                return true;
            }
            return false;
        }

        private void UpdateCounts() {
            numUnlocked = 0;
            numLocked = 0;
            maxTier = -1;
            var totalCount = All.Count;
            foreach (var item in Progress) {
                ResearchProgress p = item.Value;
                if (p.InternalUnlocked) {
                    var tier = p.Def.Tier;
                    if (tier > maxTier) {
                        maxTier = tier;
                    }
                    numUnlocked++;
                }
            }
            numLocked = totalCount - numUnlocked;
        }

        public string TreePathFor(Def def) {
            if (Researchable.IsEmptyPrereq(def)) {
                return Texts.LinkToTooltip(T.NotAvailable, T.DeviceLootableOnly);
            }
            var path = new List<string>(10);
            if (IsUnlocked(def.IdH)) {
                path.Add(Texts.LinkToResearch(def, Texts.Green(def.NameT)));
            } else {
                path.Add(Texts.LinkToResearch(def, Texts.Yellow(def.NameT)));
            }
            while (!Researchable.IsEmptyPrereq(def)) {
                def = The.Defs.Get(def.Researchable.Prerequisites[0]);
                if (IsUnlocked(def.IdH)) {
                    // don't show unlocked nodes
                    // path.Insert(0, Texts.LinkToResearch(def, Texts.Green(def.NameT)));
                } else {
                    path.Insert(0, Texts.LinkToResearch(def, Texts.Yellow(def.NameT)));
                }
            }
            return string.Join(" > ", path);
        }

        public int NumUnlocked {
            get {
                if (numUnlocked == -1) {
                    UpdateCounts();
                }
                return numUnlocked;
            }
        }

        public int NumLocked {
            get {
                if (numLocked == -1) {
                    UpdateCounts();
                }
                return numLocked;
            }
        }


        public void ResetCache() {
            availableCached = null;
            numUnlocked = -1;
            numLocked = -1;
            maxTier = -1;
            UpdateUsedDisk();
        }

        public readonly Dictionary<int, ResearchProgress> Progress = new();
        public bool IsAllUnlocked {
            get {
                if (A.InstantBuild) { return true; }
                return NumLocked == 0;
            }
        }
        public bool DisableTiers => S.Prefs.GetBool(
            Pref.NoResearchTiers, PrefH.NoResearchTiers, false);
        public readonly Dictionary<int, (int, int)> ResearchTierCounts = new();
        public bool IsTierUnlocked(int tier, out int needToUnlock) {
            needToUnlock = 0;
            if (DisableTiers) {
                return true;
            }
            if (tier < 2) { return true; }
            if (nextTierUnlockPct < 0.001f) { return true; }
            if (ResearchTierCounts.TryGetValue(tier - 1, out var cnts)) {
                var needToHaveUnlocked = Mathf.CeilToInt(cnts.Item2 * nextTierUnlockPct);
                var diff = needToHaveUnlocked - cnts.Item1;
                if (diff > 0) {
                    needToUnlock = diff;
                    return false;
                }
            }
            return true;
        }
        public void EnsureTierCounts() {
            if (ResearchTierCounts.Count == 0) {
                UpdateTierCounts();
            }
        }
        public void UpdateTierCounts() {
            ResearchTierCounts.Clear();
            for (int i = All.Count - 1; i >= 0; i--) {
                Def res = All[i];
                if (!ResearchTierCounts.TryGetValue(res.Tier, out var cnt)) {
                    cnt = (0, 0);
                }
                var prog = S.Research.Get(res.IdH);
                if (prog.IsUnlocked) {
                    cnt.Item1++;
                }
                cnt.Item2++;
                ResearchTierCounts[res.Tier] = cnt;
            }
        }
        private static List<Def> _all;
        public static List<Def> All {
            get {
                if (_all == null) {
                    nextTierUnlockPct = Tunables.ResearchNextTierUnlockAt;
                    _all = new List<Def>();
                    foreach (var def in The.Defs.Defs.Values) {
                        if (!Researchable.TreeNode(def)) { continue; }
                        _all.Add(def);
                    }
                }
                return _all;
            }
        }

        private static Dictionary<int, Researchable> _researchables;
        #if DEBUG_RESEARCH_BALANCE
        private static readonly Dictionary<int, int> energyCostByTier = new();
        private static readonly Dictionary<int, int> memoryCostsByTier = new();
        private static readonly Dictionary<int, int> diskCostsByTier = new();
        private static readonly Dictionary<int, int> newEnergyCostByTier = new();
        private static readonly Dictionary<int, int> newMemoryCostsByTier = new();
        private static readonly Dictionary<int, int> newDiskCostsByTier = new();
        #endif

        public void Balance() {
            int maxTier = 0;
            var difficulty = S.Difficulty.Level;
            foreach (var r in Researchables) {
                var res = r.Value;
                var depth = GetNodeDepth(res);
                res.NodeDepth = depth;
                var tier = res.Def.Tier;
                maxTier = Math.Max(tier, maxTier);
                var depCount = GetNodeDeps(res);

                #if DEBUG_RESEARCH_BALANCE
                energyCostByTier.GetIncrement(tier, res.Energy);
                memoryCostsByTier.GetIncrement(tier, res.Memory);
                diskCostsByTier.GetIncrement(tier, res.Storage);
                #endif

                if (res.IsCostless) { continue; }
                var unlockCost = 0f;
                if (res.DatoidCosts != null) {
                    foreach (var d in res.DatoidCosts) {
                        unlockCost += d.Type.InternalValue * d.StackSize;
                    }
                }
                res.UnlockCost = unlockCost;

                res.UnlockValue = Equations.UnlockValueFor(tier, depth, depCount, difficulty);
                res.Storage = Equations.DiskCostFor(tier, depth, depCount, difficulty);
                res.Storage = Mathf.RoundToInt(res.Storage * Tunables.ResearchStorageCostMultiplier);
                //res.UnlockValue = Mathf.RoundToInt(res.UnlockValue * Tunable.Float(TunableKey.ResearchElectricityCostMultiplier));
                #if DEBUG_RESEARCH_BALANCE
                newEnergyCostByTier.GetIncrement(tier, res.Energy);
                newMemoryCostsByTier.GetIncrement(tier, res.Memory);
                newDiskCostsByTier.GetIncrement(tier, res.Storage);
                #endif
                if (Arrays.IsEmpty(res.DatoidCosts)) {
                    res.DatoidCosts = new[] {
                        new Mat() { Type = MatType.FastGet(MatIdH.DatoidBasic),
                            StackSize = res.UnlockValue,
                            MaxStackSize = res.UnlockValue
                        }
                    };
                }
            }

            #if DEBUG_RESEARCH_BALANCE
            for (int i = 1; i <= maxTier; i++) {
                D.Warn("Energy: {0} -> {1} -> {2}", i, energyCostByTier[i], newEnergyCostByTier[i]);
                D.Warn("Memory: {0} -> {1} -> {2}", i, memoryCostsByTier[i], newMemoryCostsByTier[i]);
                D.Warn("Disk: {0} -> {1} -> {2}", i, diskCostsByTier[i], newDiskCostsByTier[i]);
            }
            D.Warn("Energy Total: {0} -> {1}", energyCostByTier.Values.Sum(), newEnergyCostByTier.Values.Sum());
            D.Warn("Memory Total: {0} -> {1}", memoryCostsByTier.Values.Sum(), newMemoryCostsByTier.Values.Sum());
            D.Warn("Disk Total: {0} -> {1}", diskCostsByTier.Values.Sum(), newDiskCostsByTier.Values.Sum());
            #endif
        }


        public void ValidateTree() {
            var validated = new HashSet<Researchable>();
            var tierMaxUnlockCost = new Dictionary<int, float>();
            var tierAvgUnlockCost = new Dictionary<int, float>();
            var tierSumUnlockCost = new Dictionary<int, float>();
            var tierCount = new Dictionary<int, int>();
            var tierMaxUnlockCostItems = new Dictionary<int, string>();
            int maxTier = 0;
            foreach (var r in Researchables)  {
                var tier = r.Value.Def.Tier;
                var sum = tierSumUnlockCost.Get(tier, 0);
                var cnt = tierCount.Get(tier, 0);
                tierSumUnlockCost[tier] = sum + r.Value.UnlockCost;
                tierCount[tier] = cnt + 1;
                var curMax = tierMaxUnlockCost.Get(tier, 0);
                var newMax = Math.Max(r.Value.UnlockCost, curMax);
                if (maxTier < tier) {
                    maxTier = tier;
                }
                if (curMax < newMax) {
                    tierMaxUnlockCostItems[tier] = r.Value.Def.Id;
                    tierMaxUnlockCost[tier] = newMax;
                }
            }
            foreach (var t in tierCount) {
                var tier = t.Key;
                var cnt = t.Value;
                tierAvgUnlockCost[tier] = tierSumUnlockCost[tier] / cnt;
            }

            // validate overall tiers data
            // 1. Tier N avg cost cannot be > 2x tier N-1 avg cost
            for (int i = 2; i <= maxTier; i++) {
                var avgPrev = tierAvgUnlockCost[i - 1];
                var avg = tierAvgUnlockCost[i];

                bool logError = false;
                var costDiff = Equations.MaxTierAvgCostDiff(i);
                var actDiff = avg / avgPrev;
                logError = actDiff > costDiff || actDiff < 1f;

                var msg = string.Format("Research Validation: Tier {0} avg cost ({1}) is x{5:0.0} compared to Tier {2} ({3}). Avg should be < x{4:0.0}",
                        i, avg, i - 1, avgPrev, costDiff, actDiff);
                if (logError) {
                    D.Err(msg);
                }
                #if VERBOSE_VALIDATION
                if (!The.TestsAreRunning) {
                    D.Warn(msg);
                }
                #endif
            }

            // validate individual items
            foreach (var r in Researchables.Values.OrderBy(r => r.Def.Tier)) {
                ValidateResearchable(r, validated, tierMaxUnlockCost,
                    tierAvgUnlockCost, tierMaxUnlockCostItems);
            }

            // validate all defs
            foreach (var d in The.Defs.Defs.Values)  {
                if (d.Tier == 0) { continue; }
                if (Researchable.TreeNode(d)) { continue; }
                if (d.IsAbstract) { continue; }
                if (d.IsTemplate) { continue; }
                if (!d.HasComponent(ComponentIdH.Constructable)) { continue; }
                if (d.ConstructedMats == null) {
                    D.Err("Constructable has no constructed mats! {0}", d.Id);
                    continue;
                }
                foreach (var mat in d.ConstructedMats) {
                    var matDef = mat.Type?.Def;
                    if (matDef == null) {
                        D.Err("MatDef not set for mat type: {0}", mat);
                        continue;
                    }
                    if (matDef.Tier > 2 && matDef.Tier > d.Tier) {
                        D.Err("Building a {0} (T:{1}) requires material of higher tier: {2} (T:{3})",
                            d.Id, d.Tier, matDef.Id, matDef.Tier);
                    }
                }
            }
        }


        private int GetNodeDeps(Researchable r) {
            if (r.DirectDepCount != -1) { return r.DirectDepCount; }
            var depCount = 0;
            foreach (var node in All) {
                if (Arrays.ContainsSafe(node.Researchable.Prerequisites, r.Def.Id)) {
                    depCount++;
                }
            }
            r.DirectDepCount = depCount;
            return depCount;
        }
        private int GetNodeDepth(Researchable r, int depth = 0) {
            if (r.NodeDepth != -1) { return r.NodeDepth; }
            //var r = S.Research.Get(id);
            if (Researchable.IsEmptyPrereq(r)) {
                return 0;
            }
            var minDepth = int.MaxValue;
            try {
                foreach (var p in r.Prerequisites) {
                    if (p == r.Def.Id) {
                        D.Err("Dependency cycle! {0} has itself in prerequisites!", p);
                        continue;
                    }
                    var pR = The.Defs.Get(p).Researchable;
                    if (pR.IsCostless) {
                        return 0;
                    }
                    var pDepth = GetNodeDepth(pR, depth);
                    minDepth = Mathf.Min(pDepth, minDepth);
                }
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed getting prerequisites for researchable: {0}. Likely a dependency cycle!",
                    r.Def.Id);
            }
            return minDepth + 1;
        }

        private void ValidateResearchable(Researchable r,
                HashSet<Researchable> validated,
                Dictionary<int, float> tierMaxUnlockCost,
                Dictionary<int, float> tierAvgUnlockCost,
                Dictionary<int, string> tierMaxUnlockCostItems) {
            if (validated.Contains(r)) { return; }
            validated.Add(r);
            var tier = r.Def.Tier;
            if (tier == 0) { return; }
            if (tier > 1) {
                var maxUnlockCostOfTierBelow = tierMaxUnlockCost.Get(tier - 1, 0);
                var minSuggestedUnlockCostForThisTier = maxUnlockCostOfTierBelow * 1.05f;
                var maxSuggestedUnlockCostForThisTier = maxUnlockCostOfTierBelow * 3;
                if (r.UnlockCost < minSuggestedUnlockCostForThisTier) {
                    D.Err("Research Validation: T{0} {1} has max unlock cost {2}, T{3} {4} has cost of {5} (too low). Min suggested cost: {6}",
                        tier-1, tierMaxUnlockCostItems.Get(tier - 1, null), maxUnlockCostOfTierBelow,
                            tier, r.Def.Id, r.UnlockCost, minSuggestedUnlockCostForThisTier);
                }
                if (r.UnlockCost > maxSuggestedUnlockCostForThisTier) {
                    D.Err("Research Validation: T{0} {1} has max unlock cost {2}, T{3} {4} has cost of {5} (too high). Max suggested cost: {6}",
                        tier-1, tierMaxUnlockCostItems.Get(tier - 1, null), maxUnlockCostOfTierBelow,
                            tier, r.Def.Id, r.UnlockCost, maxSuggestedUnlockCostForThisTier);
                }
            }
            if (Researchable.IsEmptyPrereq(r)) { return; }
            for (int i = r.Prerequisites.Length - 1; i >= 0; i--) {
                string pr = r.Prerequisites[i];
                if (pr == r.Def.Id) {
                    D.Err("Researchable depends on itself! {0}", pr);
                    r.Prerequisites[i] = null;
                }
                var parent = Researchables.Get(Hashes.S(pr), null);
                if (parent == null || parent.Def.Tier == 0) { continue; }
                if (parent.Def.Tier > r.Def.Tier) {
                    D.Err("Research Validation: Parent tier higher than child! {0}({1}) -> {2}({3})",
                        parent.Def.Id, parent.Def.Tier, r.Def.Id, r.Def.Tier);
                }
                if (r.Def.Tier - parent.Def.Tier > 2) {
                    D.Err("Research Validation: Tier gap too big! {0}({1}) -> {2}({3})",
                        parent.Def.Id, parent.Def.Tier, r.Def.Id, r.Def.Tier);
                }
                if (parent.Storage > r.Storage * 1.5f) {
                    D.Err("Research Validation: Parent storage 2x higher than child! {0}({1}) -> {2}({3})",
                        parent.Def.Id, parent.Storage, r.Def.Id, r.Storage);
                }
                if (parent.UnlockValue > r.UnlockValue * 1.5f) {
                    D.Err("Research Validation: Parent unlock value much higher than child! {0}({1}) -> {2}({3})",
                        parent.Def.Id, parent.UnlockValue, r.Def.Id, r.UnlockValue);
                }
                if (parent.Def.Tier == r.Def.Tier) {
                    // child should not be more than 2x expensive than parent
                    if (parent.UnlockCost * 2f < r.UnlockCost) {
                        D.Err("Research Validation: Parent (Tier {5}) unlock cost much lower than child! {0}({1}) -> {2}({3}). Max recommended child cost is {4}",
                            parent.Def.Id, parent.UnlockCost, r.Def.Id, r.UnlockCost, parent.UnlockCost * 2f, parent.Def.Tier);
                    }
                } else {
                    var tierDiff = r.Def.Tier - parent.Def.Tier;
                    // child should not be more than 4x expensive than parent if child is +1 tier
                    var childCostLimit = parent.UnlockCost * (2f + tierDiff * 2);
                    if (childCostLimit < r.UnlockCost) {
                        D.Err("Research Validation: Parent (Tier {5}) unlock cost much lower than child (+{6} tier)! {0}({1}) -> {2}({3}). Max recommended child cost is {4}",
                            parent.Def.Id, parent.UnlockCost, r.Def.Id, r.UnlockCost, childCostLimit, parent.Def.Tier, tierDiff);
                    }
                }
                // child should always be at least a little more expensive than parent
                if (!r.SkipCostValidation && parent.UnlockCost > r.UnlockCost) {
                    D.Err("Research Validation: Parent (Tier {5}) unlock costs more than child! {0}({1}) -> {2}({3}). Min recommended child cost is {4}",
                        parent.Def.Id, parent.UnlockCost, r.Def.Id, r.UnlockCost, parent.UnlockCost + 1f, parent.Def.Tier);
                }
                ValidateResearchable(parent, validated, tierMaxUnlockCost,
                    tierAvgUnlockCost, tierMaxUnlockCostItems);
            }
        }

        public static Dictionary<int, Researchable> Researchables {
            get {
                if (_researchables == null) {
                    _researchables = new();
                    foreach (var r in All) {
                        _researchables[r.IdH] = r.Researchable;
                    }
                }
                return _researchables;
            }
        }
        public bool IsUnlocked(int researchId) {
            var res = Get(researchId);
            if (res == null) {
                D.Err("Trying to check if a missing research is unlocked: {0}",
                    researchId);
                return false;
            }
            return res.IsUnlocked;
        }

        [Obsolete("Use int version")]
        public bool IsUnlocked(string researchId) {
            return Get(researchId).IsUnlocked;
        }

        [Obsolete("Make int version")]
        public bool IsDiscovered(string id) {
            //D.Warn("Discovered: {0}", id);
            var prog = Get(id);
            if (prog == null) {
                D.Err("Trying to check if missing research is discovered: {0}", id);
                return false;
            }
            var r = prog.Researchable;
            if (!Researchable.IsEmptyPrereq(r)) {
                foreach (var rp in r.Prerequisites) {
                    if (!IsDiscovered(rp)) { return false; }
                }
            }
            return prog.IsDiscovered;
        }
        // Sandbox fakes "everything is researched" on the read side only
        // (ResearchProgress.IsUnlocked), never marking anything discovered, so the research
        // tree UI has to treat every node as discovered too - otherwise nodes paint as "???"
        // while being unlockable. Deliberately not folded into IsDiscovered, which the codex,
        // quick search and story events read to hide spoilers.
        public bool IsDiscoveredOrSandbox(int id) {
            return A.InstantBuild || IsDiscovered(id);
        }

        public bool IsDiscovered(int id) {
            var prog = Get(id);
            if (prog == null) {
                D.Err("Trying to check if non-existent research is discovered: {0}", id);
                return false;
            }
            var r = prog.Researchable;
            if (!Researchable.IsEmptyPrereq(r)) {
                foreach (var rp in r.Prerequisites) {
                    if (!IsDiscovered(Hashes.S(rp))) { return false; }
                }
            }
            return prog.IsDiscovered;
        }

        public readonly BInt UsedDisk = new();
        public void UpdateUsedDisk() {
            int usedDisk = 0;
            foreach (var res in All) {
                var existing = Get(res.IdH);
                if (existing == null) { continue; }
                if (!existing.InternalUnlocked) { continue; }
                usedDisk += existing.Storage;
            }
            D.Log("Seting used disk to: {0}", usedDisk);
            UsedDisk.Set(usedDisk);
        }
        public void LoadProgress(List<ResearchProgress> saved) {
            if (saved == null) { return; }
            RemoveDeadProgress(saved);
            foreach (var p in saved) {
                try {
                    p.IdH = Hashes.S(p.Id);
                    Progress[p.IdH] = p;
                    p.S = S;
                    if (p.Def != null) {
                        p.SetResearchable(p.Def.Researchable);
                    }
                    if (p.InternalUnlocked) {
                        ApplyUnlockEffectsFor(p.Researchable, false);
                    }
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failed loading saved research item, mod issue or removed: {0}", p);
                }
            }
            UpdateTierCounts();
            UpdateUsedDisk();
        }

        /// <summary>
        /// Sandbox reports every node as unlocked (ResearchProgress.IsUnlocked) without ever
        /// running Unlock(), so the blackboard flags that unlock effects write - and that
        /// features like Hack, paint or auto mining gate on - would otherwise stay unset.
        /// Every unlock effect is an idempotent blackboard write, so replaying the whole set
        /// is safe and repeatable.
        /// </summary>
        public void ApplyAllUnlockEffects() {
            foreach (var def in All) {
                ApplyUnlockEffectsFor(def.Researchable, false);
            }
        }

        public void ApplyUnlockEffectsFor(Researchable r, bool notify) {
            if (Arrays.IsEmpty(r.UnlockEffects)) { return; }
            foreach (var id in r.UnlockEffects) {
                var fx = Registries.UnlockEffects.Get(id);
                if (fx != null) {
                    fx.OnUnlock(S, notify);
                }
            }
        }

        public List<ResearchProgress> SaveProgress() {
            return new List<ResearchProgress>(Progress.Values);
        }

        private void RemoveDeadProgress(List<ResearchProgress> saved) {
            for (int i = saved.Count - 1; i >= 0; i--) {
                ResearchProgress p = saved[i];
                if (p.Def == null) {
                    p.IdH = Hashes.S(p.Id);
                    if (!Legacy.RemovedDefs.Contains(p.IdH)) {
                        #if PRODUCTION_BUILD
                        D.Err("Removing invalid research progress: {0}.", p);
                        #else
                        if (!The.TestsAreRunning) {
                        }
                        #endif
                    }
                    saved.RemoveAt(i);
                }
            }
        }

        public bool Discover(int id) {
            var prog = Get(id);
            if (prog?.IsDiscovered != false) {
                return false;
            }
            prog.Discover(S);
            return true;
        }

        /// <summary>
        /// Marks the given research and all of its prerequisites (recursively) as discovered.
        /// Unlike <see cref="Discover(int)"/>, this avoids leaving a granted node stuck showing
        /// as "Not Discovered" in the tree when its prerequisites haven't been discovered yet -
        /// ResearchTree.IsDiscovered requires every prerequisite to also be discovered.
        /// </summary>
        public bool DiscoverWithPrerequisites(int idH) {
            var prog = Get(idH);
            if (prog == null) {
                D.Err("Trying to discover non-existent research: {0}", idH);
                return false;
            }
            var changed = false;
            var r = prog.Researchable;
            if (!Researchable.IsEmptyPrereq(r)) {
                foreach (var rp in r.Prerequisites) {
                    changed |= DiscoverWithPrerequisites(Hashes.S(rp));
                }
            }
            if (!prog.IsDiscovered) {
                prog.Discover(S);
                changed = true;
            }
            return changed;
        }

        public List<(Def def, IResearchUnlockEffect unlockEff)> GetUnlockableIconsFor(Def forDef) {
            var res = new List<(Def, IResearchUnlockEffect)>();
            if (!Arrays.IsEmpty(forDef.Researchable.UnlockEffects)) {
                foreach (var effId in forDef.Researchable.UnlockEffects) {
                    var eff = Registries.UnlockEffects.Get(effId);
                    if (eff == null) { continue; }
                    res.Add((null, eff));
                }
            }
            foreach (var d in The.Defs.Defs) {
                var def = d.Value;
                if (def.IsAbstract) { continue; }
                if (def.SkipToolListing) { continue; }
                if (Researchable.IsEmptyPrereq(def)) { continue; }
                if (!Arrays.Contains(def.Researchable.Prerequisites, forDef.Id)) {
                    continue;
                }
                res.Add((def, null));
            }
            return res;
        }

        public void Reload() {
            foreach (var r in Progress) {
                var def = r.Value.Def;
                if (def != null) {
                    r.Value.SetResearchable(def.Researchable);
                } else {
                    D.Err("Research progress contains a missing item: {0}", r.Key);
                }
            }
            UpdateTierCounts();
            ResetCache();
        }

        public string DebugInfo() {
            var list = new List<string>();
            foreach (var def in The.Defs.Defs.Values) {
                if (!Researchable.TreeNode(def)) { continue; }

                var existing = Get(def.IdH);
                if (existing == null) { continue; }
                if (existing.IsUnlocked) {
                    list.Add(existing.Id.Replace("Research/", ""));
                }
            }
            return string.Join(",", list);
        }

        private List<Def> availableCached;

        public void ClearAvailableCache() {
            availableCached = null;
        }
        private readonly List<Def> availableTemp = new();

        public void UpdateAvailable() {
            if (availableCached != null) { return; }
            UpdateTierCounts();
            var instant = A.InstantBuild;
            availableTemp.Clear();
            if (instant) {
                availableCached = availableTemp;
                return;
            }
            foreach (var def in The.Defs.Defs.Values) {
                if (!Researchable.TreeNode(def)) { continue; }
                var existing = Get(def.IdH);
                if (existing == null) {
                    continue;
                }
                if (existing.InternalUnlocked || !IsTierUnlocked(def.Tier, out _)) {
                    def.Researchable.CanBeResearchedNow = false;
                    continue;
                }

                // Check dependencies
                if (Researchable.IsEmptyPrereq(def)) {
                    availableTemp.Add(def);
                    def.Researchable.CanBeResearchedNow = true;
                } else {
                    bool complete = true;
                    foreach (var dep in def.Researchable.Prerequisites) {
                        var depProg = GetAutoHash(dep);
                        if (depProg == null) {
                            D.Err("FIXME No prerequisite research: {0} in {1}", dep, def.Id);
                            complete = false;
                            break;
                        }
                        if (!depProg.InternalUnlocked) {
                            complete = false;
                            break;
                        }
                    }
                    if (complete) {
                        availableTemp.Add(def);
                    }
                    def.Researchable.CanBeResearchedNow = complete;
                }
            }
            availableCached = availableTemp;
        }

        public List<Def> Available {
            get {
                UpdateAvailable();
                return availableCached;
            }
        }

        public Entity Entity => null;

        public ResearchProgress Get(int id) {
            var pr = Progress.Get(id, null);
            if (pr != null) {
                return pr;
            }
            var def = The.Defs.FastGet(id);
            if (def == null) {
                return null;
            }
            pr = new ResearchProgress() { Id = def.Id, IdH = def.IdH };
            pr.S = S;
            pr.SetResearchable(def.Researchable);
            Progress[pr.IdH] = pr;
            return pr;
        }

        [Obsolete("Use int id version where possible")]
        public ResearchProgress Get(string id) {
            return GetAutoHash(id);
        }

        public ResearchProgress GetAutoHash(string id) {
            var pr = Get(Hashes.S(id));
            if (pr == null) {
                D.ErrSoft("Cannot get researchable by id: {0}", id);
            }
            return pr;
        }

        public ResearchTree(GameState s) {
            S = s;
        }

        public void UpdateSit() {
            var completed = 0;
            foreach (var item in Progress) {
                if (item.Value.InternalUnlocked) {
                    completed++;
                }
            }
            S.Situation.ResearchedTech = completed;
        }

        public void Clear() {
            Progress.Clear();
        }
        public bool IsDefUnlocked(Def def, bool ignoreSandbox) {
            if (def == null) {
                D.Err("Trying to check if null def is unlocked");
                return false;
            }
            if (def.SkipToolListing) { return false; }
            if (Researchable.IsEmptyPrereq(def)) {
                return true;
            }
            if (!ignoreSandbox && A.InstantBuild) { return true; }
            foreach (var pre in def.Researchable.Prerequisites) {
                var prereq = GetAutoHash(pre);
                if (prereq == null) { continue; }
                if (!prereq.InternalUnlocked) { return false; }
            }
            return true;
        }
        public bool IsDefActuallyUnlocked(Def def) {
            return IsDefUnlocked(def, true);
        }

        public bool IsDefUnlocked(Def def) {
            return IsDefUnlocked(def, false);
        }

        [Obsolete("Use the version with Def instead of string id")]
        public bool IsDefUnlocked(string id) {
            var def = The.Defs.TryGet(id);
            return IsDefUnlocked(def);
        }

        public string GetName() {
            return "ResearchTree";
        }

        public string Search(string query) {
            var all = new Dictionary<string, string>();
            for (int i = 0; i < All.Count; i++) {
                Def r = All[i];
                if (!IsDiscovered(r.IdH)) { continue; }
                all[r.NameT] = r.Id;
            }
            foreach (var def in The.Defs.Defs) {
                var d = def.Value;
                if (d.SkipToolListing) { continue; }
                if (d.IsAbstract) { continue; }
                if (d.Researchable?.IsTreeNode != false) { continue; }
                if (Researchable.IsEmptyPrereq(d)) { continue; }
                foreach (var p in d.Researchable.Prerequisites) {
                    var existing = all.Get(d.NameT, null);
                    if (existing != null) { continue; }
                    var rDef = The.Defs.Get(p);
                    all[d.NameT] = rDef.Id;
                }
            }
            var key = Strings.Search(query, all.Keys);
            if (key == null) {
                return null;
            }
            return all.Get(key, null);
        }

        public bool Exists(int itemH) {
            return The.Defs.FastGet(itemH) != null;
        }

        public bool TryUnlock(ResearchProgress progress) {
            if (A.Grid.TotalDisk - UsedDisk < progress.Researchable.Storage) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.ResearchLacksDisk}");
                return false;
            }
            UpdateDatoidCounts();
            Caches.MatList.Clear();
            foreach (var cost in progress.Researchable.AdjustedDatoidCosts(S.Difficulty)) {
                if (DatoidCounts.TryGetValue(cost.Type, out var amt)) {
                    if (amt < cost.StackSize) {
                        Caches.MatList.Add(new Mat() {
                            Type = cost.Type,
                            StackSize = cost.StackSize - amt,
                            MaxStackSize = cost.StackSize,
                        });
                    }
                }
            }
            if (Caches.MatList.Count > 0) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.ResearchLacksResources}");
                return false;
            }
            foreach (var cost in progress.Researchable.AdjustedDatoidCosts(S.Difficulty)) {
                if (!S.Sys.Inventory.TryToRemove(cost.Type, cost.StackSize,
                        MatGroupIdH.Datoid)) {
                    D.Err("Failed removing datoids for research {0}-> {1}",
                        progress.Id, cost);
                } else {
                    S.Sig.MatConsumed.Send(cost.Type, cost.StackSize);
                }
            }
            UnlockAndUnbookmark(progress);
            UpdateDatoidCounts();
            return true;
        }

        // In-game unlocks (tree, bonuses, quest rewards) - a bookmark of unlocked research
        // would otherwise stay in the list for good. Scenario setup uses Unlock() directly,
        // before systems are wired.
        public void UnlockAndUnbookmark(ResearchProgress prog) {
            if (prog.IsBookmarked) {
                S.Sys.Research.TryRemoveBookmark(prog, true);
            }
            prog.Unlock(true);
            S.Sys.Research.QueueResearchEventRefresh();
        }

        public bool TryUnlockBonus(int idH) {
            var prog = Get(idH);
            if (prog == null) {
                return false;
            }
            if (prog.IsUnlocked) {
                return false;
            }

            UnlockAndUnbookmark(prog);
            S.Sys.Research.NotifyCompleted(prog);

            return true;
        }

        public void ResetUpToLevel(int maxTier) {
            Progress.Clear();
            if (maxTier < 1) { return; }
            foreach (var res in Researchables) {
                if (res.Value.IsCostless) { continue; }
                if (res.Value.Def.Tier <= maxTier) {
                    Get(res.Key).Unlock();
                }
            }
        }

        public void UnlockForNewScenario(string[] items) {
            foreach (var r in Tunables.AlwaysUnlockedResearchNodes) {
                var item = S.Research.GetAutoHash(r);
                if (item == null) {
                    D.Err("Invalid researchable in AlwaysUnlockedResearchNodes tunable: {0}", r);
                } else {
                    item.Unlock();
                }
            }

            var strat = ModifierBasicTech.Current;
            if (strat != ModifierBasicTech.Strategy.Locked) {
                var chance = strat == ModifierBasicTech.Strategy.Unlocked ? 1 : 0.5f;
                foreach (var r in Tunables.BasicResearchNodes) {
                    var item = S.Research.GetAutoHash(r);
                    if (item == null) {
                        D.Err("Invalid researchable in AlwaysUnlockedResearchNodes tunable: {0}", r);
                    } else {
                        if (chance >= 1f || S.Rng.Chance(chance)) {
                            item.Unlock();
                        }
                    }
                }
            }

            // extra items defined in scenario
            foreach (var r in items) {
                var item = S.Research.GetAutoHash(r);
                if (item == null) {
                    D.Err("Invalid researchable in {0}_UnlockedResearch tunable: {1}", S.Scenario?.Id, r);
                } else {
                    item.Unlock();
                }
            }
            UpdateUsedDisk();
        }
    }
}
