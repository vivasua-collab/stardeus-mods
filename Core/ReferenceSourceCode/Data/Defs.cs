// #define DEBUG_DEFS
using System.Collections.Generic;
using Game.Components;
using KL.Utils;
using KL.Collections; // for Put() and Get()
using System.Linq;
using UnityEngine;
using System;
using Game.UI;
using Game.Systems.UIUX;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using Game.Migrations;
using System.Text.RegularExpressions;
using KL.Randomness;
using Game.Mods;
using Game.Research;

namespace Game.Data {
    public class Definitions {
        private struct DefByNameSorter : IComparer<Def> {
            public int Compare(Def x, Def y) {
                return string.Compare(x.NameT, y.NameT);
            }
        }
        public readonly SortedDictionary<string, Def> Defs = new();
        private readonly Dictionary<int, Def> DefsByIdHash = new();
        public readonly Dictionary<string, Def> ToolPaths = new();
        private static readonly Dictionary<int, IComponent> prototypes = new();
        private readonly HashSet<string> missingComponents = new();
        private static readonly DefByNameSorter ByNameSorter = default;

        public static void SortByName(List<Def> defs) {
            defs.Sort(ByNameSorter);
        }

        public void Clear() {
            Defs.Clear();
            DefsByIdHash.Clear();
            ToolPaths.Clear();
        }

        public static void AddComponentPrototype(IComponent proto) {
            proto.CreateComponentClassCache();
            var pn = proto.NameHash;
            if (pn == 0) {
                D.Err("NameHash not caching for component! {0} {1}", pn, proto);
            }
            if (proto.TypeHash == 0) {
                D.Err("TypeHash not caching for component! {0} {1}", pn, proto);
            }
            if (prototypes.ContainsKey(pn)) {
                #if DEBUG_DEFS
                // D.Warn("Overriding component prototype: {0}", pn);
                #endif
                prototypes[pn] = proto;
            } else {
                prototypes.Add(pn, proto);
            }
        }

        public bool HasComponentPrototype(ComponentConfig config, out string error) {
            var keyH = config.ComponentH;
            if (prototypes.TryGetValue(keyH, out var proto)) {
                error = null;
                return true;
            }
            // if (Renames.RemovedComps.Contains(keyH)) {
            //     error = "Component of this type was removed from core game";
            //     return false;
            // }
            // // Check migrations
            // if (Renames.CompName.TryGetValue(keyH, out var migratedComp)) {
            //     error = $"This component was renamed to {migratedComp}";
            // } else {
                error = "Such component does not exist";
            // }
            return false;
        }

        public bool TryGetPrototype<T>(int keyH, out T prototype) {
            if (prototypes.TryGetValue(keyH, out var proto)) {
                if (proto is T pt) {
                    prototype = pt;
                    return true;
                }
            }
            prototype = default;
            return false;
        }

        public IComponent GetPrototype(ComponentConfig config, Entity entity) {
            var keyH = config.ComponentH;
            if (prototypes.TryGetValue(keyH, out var proto)) {
                return proto;
            }
            // if (Renames.RemovedComps.Contains(keyH)) {
            //     // nothing to worry about
            //     return null;
            // }
            // // Check migrations
            // if (Renames.CompName.TryGetValue(keyH, out var migratedComp)) {
            //     // D.Warn("Migrating component: {0} -> {1}", config.Component,
            //     //    migratedComp);
            //     config.Component = migratedComp;
            //     config.EnsureHash();
            //     if (prototypes.TryGetValue(migratedComp, out proto)) {
            //         return proto;
            //     }
            // }
            if (missingComponents.Add(config.Component)) {
                D.Err("Component prototype not registered: {0}. Entity: {1}",
                    config.Component, entity);
            }
            return null;
        }

        public IComponent CreateComponent(ComponentConfig config, Entity entity) {
            var proto = GetPrototype(config, entity);
            IComponent res = null;
            if (proto != null) {
                res = proto.Copy();
            }
            return res;
        }

        public void BuildComponentsFor(Entity entity) {
            var def = entity.Definition;
            if (def == null) {
                D.Err("Trying to build components for entity that has not def! {0} {1}",
                    entity.Id, entity.GetType());
            }
            if (def.Components != null) {
                var cl = def.Components.Length;
                int compIdx = 0;
                int missing = 0;
                for (int i = 0; i < cl; i++) {
                    ComponentConfig cc = def.Components[i];
                    var comp = CreateComponent(cc, entity);
                    if (comp == null) {
                        missing++;
                        continue;
                    }
                    if (comp.Initialize(entity)) {
                        // Can't set i, because missing components will become nulls
                        entity.SetComponent(comp, compIdx++, cl);
                    } else {
                        D.Err("Will not add broken component: {0} to entity {1}",
                            comp, entity);
                    }
                }
                if (missing != 0) {
                    entity.CompactComponents(missing);
                }
            }
        }

        public List<IComponent> BuildMissingComponentsFor(Entity entity) {
            var def = entity.Definition;
            List<IComponent> added = null;
            if (def.Components != null) {
                var cl = def.Components.Length;
                for (int i = 0; i < cl; i++) {
                    ComponentConfig cc = def.Components[i];
                    if (entity.HasComponent(cc.ComponentH)) { continue; }
                    // We found a missing component
                    if (added == null) { added = new List<IComponent>(1); }
                    var comp = CreateComponent(cc, entity);
                    if (comp == null) { continue; }
                    if (comp.Initialize(entity)) {
                        // Set component here would kill other comps
                        entity.AddComponent(comp);
                        added.Add(comp);
                    } else {
                        D.Err("Will not add broken missing component: {0} to entity {1}",
                            comp, entity);
                    }
                }
            }
            return added;
        }

        public void Remove(Def def) {
            Defs.Remove(def.Id);
            DefsByIdHash.Remove(Hashes.S(def.Id));
        }

        public List<Def> LayerDefs(int layer) {
            return Defs.Values.Where(v => v.LayerId == layer).ToList();
        }

        public bool Set(Def def) {
            if (string.IsNullOrWhiteSpace(def.NameKey)) {
                def.NameKey = def.Id.Replace("/", ".").ToLowerInvariant();
            }
            def.IdH = Hashes.S(def.Id);
            if (DefsByIdHash.TryGetValue(def.IdH, out var existing)) {
                if (existing != def) {
                    return false;
                }
            } else {
                Defs[def.Id] = def;
                DefsByIdHash[def.IdH] = def;
            }

            if (!string.IsNullOrWhiteSpace(def.ToolPath)) {
                var ids = def.Id.Split('/');
                var idEnd = ids[^1];
                ToolPaths[$"{def.ToolPath.TrimEnd('/')}/{idEnd}"] = def;
            }
            return true;
        }

        private static readonly Regex matchingRegex = new(@"\d+", RegexOptions.None);

        public static bool AreMatching(string d1, string d2) {
            return matchingRegex.Replace(d1, string.Empty) ==
                matchingRegex.Replace(d2, string.Empty);
        }

        public Def RandomMatching(string prefix, Rng rng) {
            var matching = Matching(prefix);
            return rng.From(matching);
        }

        public List<Def> Matching(string prefix) {
            var query = matchingRegex.Replace(prefix, string.Empty);
            var res = new List<Def>();
            foreach (var def in Defs) {
                if (matchingRegex.Replace(def.Value.Id, string.Empty) == query) {
                    res.Add(def.Value);
                }
            }
            return res;
        }

        public bool Contains(string id) {
            return Defs.ContainsKey(id);
        }
        public bool ToolPathsContains(string id) {
            return ToolPaths.ContainsKey(id);
        }
        public bool TryFindByName(string nameT, out Def def) {
            if (nameT == null) {
                def = null;
                return false;
            }
            nameT = Strings.ToSearchString(nameT);
            foreach (var d in Defs.Values) {
                if (d.SearchNameT == nameT) {
                    def = d;
                    return true;
                }
            }
            def = null;
            return false;
        }

        public Def TryGet(string id) {
            if (id == null) {
                D.Err("Trying to get Def with null id. Make sure id variable is initialized before calling The.Defs.Get(id) or The.Defs.TryGet(id)");
                return null;
            }
            if (DefsByIdHash.TryGetValue(Hashes.S(id), out var def)) {
                return def;
            }
            return ToolPaths.Get(id, null);
        }

        public Def FastGet(int idHash) {
            if (DefsByIdHash.TryGetValue(idHash, out var res)) { return res; }
            return null;
        }

        public Def Get(string id) {
            var def = TryGet(id);
            if (def != null) { return def; }
            var idH = Hashes.S(id);
            // if (Renames.DefId.TryGetValue(idH, out var newId)) {
            //     def = TryGet(newId);
            //     if (def != null) {
            //         //// D.Warn("Migrating def: {0} -> {1}", id, newId);
            //         return def;
            //     }
            // }
            // if (Renames.RemovedDefs.Contains(idH)) {
            //     return null;
            // }
            D.Err("Could not load definition by id or tool path: {0}", id);
            ShowMissingObjectsRemoved();
            return def;
        }

        public void Validate() {
            foreach (var d in Defs) {
                var def = d.Value;
                ValidatePrerequisites(def);
            }
        }

        private void ValidatePrerequisites(Def def) {
            if (Researchable.IsEmptyPrereq(def)) { return; }
            bool needsPrereqRebuild = false;
            for (int i = def.Researchable.Prerequisites.Length - 1; i >= 0; i--) {
                string prereq = def.Researchable.Prerequisites[i];
                if (!Defs.ContainsKey(prereq)) {
                    Ready.WhenCore(() => {
                        ModRegistry.WarnAboutBrokenMod(def, $"Misconfigured research for <b>{def.Id}</b>: Research prerequisite <b>{prereq}</b> is missing");
                        D.Err("Removing missing research prerequisite {0} -> {1}",
                            def.Id, prereq);
                    });
                    def.Researchable.Prerequisites[i] = null;
                    needsPrereqRebuild = true;
                }
            }
            if (needsPrereqRebuild) {
                def.Researchable.Prerequisites = Arrays.RemoveNulls(
                    def.Researchable.Prerequisites);
            }
        }

        private bool isShowMissingObjectsRemovePending;

        private void ShowMissingObjectsRemoved() {
            if (isShowMissingObjectsRemovePending) { return; }
            isShowMissingObjectsRemovePending = true;
            Ready.WhenGame(this, "ShowRemovalWarning", () => {
                isShowMissingObjectsRemovePending = false;
                WPopup.SystemWarning("Object Removed",
                "Definition of some objects was not found. Object of this type will be removed. Either a mod was disabled, or something changed in core game. See logs (console ~) for more details.");
            });
        }

        public void AfterLoad() {
            foreach (var d in Defs.Values) {
                The.ModLoader.ValidateResearchablePostLoad(d);
                d.AfterLoad();
            }
            MatType.LinkDefs();
            DeserializeDatoids();

            ResolveUnlocks();
        }
        public void ResolveUnlocks() {
            foreach (var d in Defs.Values) {
                if (!Researchable.TreeNode(d)) { continue; }
                var unlocks = new List<Def>();
                foreach (var def in Defs.Values) {
                    if (def.IsAbstract) { continue; }
                    if (def.SkipToolListing) { continue; }
                    if (Researchable.IsEmptyPrereq(def)) { continue; }
                    if (!Arrays.ContainsSafe(def.Researchable.Prerequisites, d.Id)) {
                        continue;
                    }
                    unlocks.Add(def);
                }
                d.Unlockables = unlocks;
            }
            foreach (var d in Defs.Values) {
                if (Researchable.TreeNode(d)) {
                    foreach (var unlockable in d.Unlockables) {
                        if (unlockable.Tier < d.Tier) {
                            unlockable.Tier = d.Tier;
                        }
                    }
                }
            }
        }

        private void DeserializeDatoids(Def def) {
            if (!Researchable.TreeNode(def)) { return; }
            var dc = def.Researchable.DatoidCosts;
            if (Arrays.IsEmpty(dc)) { return; }
            var tier = def.Tier;
            var tierMuls = Tunables.ResearchTierDatoidCostMultipliers;
            // Default
            float tierMul;
            if (tier < tierMuls.Length) {
                tierMul = tierMuls[tier];
            } else {
                tierMul = tierMuls[^1];
            }
            for (int i = dc.Length - 1; i >= 0; i--) {
                var datoid = dc[i];
                if (datoid.TryDeserialize(out var dmat)) {
                    if (tierMul != 1.0f) {
                        dmat.StackSize = Mathf.RoundToInt(dmat.StackSize * tierMul);
                        if (dmat.StackSize == 0) {
                            dmat.StackSize = 1;
                        }
                        dmat.MaxStackSize = dmat.StackSize;
                    }
                    dc[i] = dmat;
                } else {
                    D.Err("Could not deserialize datoid cost for: {0} -> {1}",
                        def.Id, datoid.TypeId);
                    ModRegistry.WarnAboutBrokenMod(def,
                        "DatoidCosts are incorrectly configured for this research node");
                }
            }
            Array.Sort(def.Researchable.DatoidCosts, new Mat.RarityComparer());
        }

        private void DeserializeDatoids() {
            foreach (var d in Defs) {
                DeserializeDatoids(d.Value);
            }
        }
    }
}
