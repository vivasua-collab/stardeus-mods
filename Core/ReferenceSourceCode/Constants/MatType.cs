using System;
using System.Collections.Generic;
using Game.Data;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.UI;
using Game.Systems.UIUX;
using Game.Mods;
using Game.Systems.Space;

namespace Game.Constants {
    /// <summary>
    /// Type of a material. Instance is Mat struct
    /// <seealso cref="Mat"/>
    /// </summary>
    [Serializable]
    public class MatType {
        private static readonly HashSet<string> IgnoredMissing = new();
        public static readonly Dictionary<string, MatType> Known = new();
        public static readonly Dictionary<int, MatType> HKnown = new();
        public static readonly Dictionary<MatType, float> ByRarity = new();
        public static readonly Dictionary<int, List<MatType>> HByGroup = new();
        public static readonly Dictionary<MatType, float> RawByRarity = new();
        // key is rarity (0-4), value is list of mats with that rarity
        public static readonly Dictionary<int, List<MatType>> ByMatRarity = new();
        // key is id hash
        public static readonly Dictionary<int, List<Def>> CompatibleStorages = new();
        private static bool hasLoadedKnownMats;
        public string Id;
        [NonSerialized] public int IdH;
        public string DefId;
        [NonSerialized] public Def Def;
        // Composite means mostly that particle collector will not be able to collect it
        public bool IsComposite;
        public bool IsConsumedOnConstruct;
        public bool IsConsumedOnDestroy;
        // Mat ID of what to convert this mat upon destroying (deconstructing)
        // something that contains this mat
        public string ConvertToMatOnDestroyId;
        public MatType ConvertToMatOnDestroy;
        // Chance to convert the mat to ConvertToMatOnDestroyId
        public float ConvertToMatOnDestroyChance;
        public Color IconTint = Palette.White;
        public bool IgnoreMarket;
        // for research tree balancing
        public float InternalValue;
        public string GroupNameKey;
        private string groupNameT;
        public string GroupNameT => groupNameT ??= GroupNameKey.T();
        /// <see cref="IExplosivenessProvider"/>
        public int ExplosionLevel;
        public string ExplosivenessCurveId;
        private Curve explCurve;
        private bool hasCachedExplosionCurve;
        /// <see cref="IExplosivenessProvider"/>
        public Curve ExplosivenessCurve {
            get {
                if (hasCachedExplosionCurve) {
                    return explCurve;
                }
                if (!CurveDef.TryGet($"Explosiveness/{ExplosivenessCurveId}", out explCurve)) {
                    if (ExplosionLevel > 0) {
                        D.Err("No ExplosivenessCurve for mat with explosion level: {0}: {1} (Curve ID: Explosiveness/{2})",
                            ExplosionLevel, Id, ExplosivenessCurveId);
                    }
                }
                hasCachedExplosionCurve = true;
                return explCurve;
            }
        }

        public DepositConfig Deposit;
        private string lackRejectionT;
        ///<summary>Use this when failing an ad</summary>
        public string LackRejectionT => lackRejectionT ??= $"{T.AdRejLackingMat}: <nobr><b>{Texts.WithColor(Def?.CodexLink ?? T.Unknown, Texts.TMPColorRed)}</b></nobr>";
        private string unreachableRejectionT;
        ///<summary>Use this when failing an ad because the material exists but no
        ///storage holding it can be reached - it must not read as a shortage</summary>
        public string UnreachableRejectionT => unreachableRejectionT ??= $"{T.AdjUnreachable}: <nobr><b>{Texts.WithColor(Def?.CodexLink ?? T.Unknown, Texts.TMPColorRed)}</b></nobr>";
        private string searchNameT;
        public string SearchNameT => searchNameT ??= NameT.ToLowerInvariant()
            .Replace(" ", string.Empty);

        private string nameT;
        public string NameT => nameT ??= Def?.NameT ?? Id;
        private string lackT;
        public string LackT => lackT ??= T.MaterialLack_V1.F(Def?.CodexLink ?? T.Unknown);
        private string favNameT;
        public string IconId;
        // Property determines the physical or other properties, like liquid,
        // solid, nutrient, nuclear, etc
        public string Property;
        /// <summary>
        /// Category or a broad group of the item, i.e. raw material, food,
        /// equipment, weapons. For storage and game logic categorization
        /// </summary>
        public string Group;
        public int GroupH;
        /// <summary>
        /// For organizing player's visual inventory, not the same as Group
        /// </summary>
        public string GroupId;
        public string ProcessableDefId;

        // 100 is default, less is rare, more is common
        // common (0) items should have rarity between 75 and 100
        // normal (1) items should have between 50 and 75
        // rare (2) items should have between 25 and 50
        // legendary (3) items should between 1 and 25
        public float Rarity;
        public float EnergyOutput;
        public float CollectorCost;
        /// <summary>
        /// 0 - common, 1 - uncommon, 2 - rare, 3 - legendary, 4 - epic super rare, etc
        /// </summary>
        public int MatRarity;

        [SelfInit]
        private static void Init() {
            // We want them to be fresh in unity
            Known.Clear();
            HKnown.Clear();
            ByRarity.Clear();
            RawByRarity.Clear();
            ByMatRarity.Clear();
            HByGroup.Clear();
            CompatibleStorages.Clear();
            hasLoadedKnownMats = false;
        }

        public int GetCount(Dictionary<MatType, int> available) {
            return available.Get(this, 0);
        }


        public static List<MatType> GetGroup(int groupId) {
            return HByGroup.Get(groupId, null);
        }

        public static void AddRuntime(MatType type) {
            if (type == null) {
                D.Err("Rejecting null mat type");
                return;
            }
            if (Known.ContainsKey(type.Id)) {
                D.Err("Trying to add runtime material that already exists: {0}",
                    type.Id);
                return;
            }
            AddMat(type);
        }

        public static void LoadKnown() {
            if (hasLoadedKnownMats) {
                D.Err("Loading materials more than once!");
                return;
            }
            hasLoadedKnownMats = true;

            var types = The.ModLoader.LoadObjects<MatType>("Config/Materials");
            foreach (var s in types) {
                // D.Log("Registering known material type: {0}", s);
                AddMat(s);
            }
            // second pass for "convert on destroy"
            foreach (var mat in Known.Values) {
                if (mat.ConvertToMatOnDestroyId != null) {
                    if (!Known.TryGetValue(mat.ConvertToMatOnDestroyId, out var mt)) {
                        D.Err("Material definition {0} has invalid ConvertToMatOnDestroyId: {1} - unavailable",
                            mat.Id, mat.ConvertToMatOnDestroyId);
                        continue;
                    }
                    mat.ConvertToMatOnDestroy = mt;
                    if (mat.ConvertToMatOnDestroyChance <= 0f) {
                        D.Err("Material definition {0} has invalid ConvertToMatOnDestroyChance: {1} - must be > 0. Setting to 0.5",
                            mat.Id, mat.ConvertToMatOnDestroyChance);
                        mat.ConvertToMatOnDestroyChance = 0.5f;
                    }
                }
            }

            DepositConfig.LoadKnown();
        }

        public static void ResolveCompatibleStorages() {
            CompatibleStorages.Clear();
            // The public storages, found once instead of going through every def's components
            // again for each of the ~100 materials
            var storages = new List<(Def def, string[] groups)>();
            foreach (var def in The.Defs.Defs.Values) {
                if (def.IsTemplate) { continue; }
                var cfg = def.ComponentConfigFor(CodeGen.ComponentIdH.MatStorage, false);
                if (cfg == null) { continue; }
                if (!cfg.GetBool(PropertyIdH.IsPublic, true)) { continue; }
                if (cfg.GetString(PropertyIdH.StorageType, null) == "TakeOnly") {
                    continue;
                }
                storages.Add((def, cfg.GetStringSet(PropertyIdH.Groups, null)));
            }
            foreach (var mat in Known.Values) {
                var list = new List<Def>();
                CompatibleStorages.Add(mat.IdH, list);
                foreach (var (def, storageGroups) in storages) {
                    if (mat.Group == null) {
                        if (storageGroups == null) {
                            list.Add(def);
                        }
                    } else if (Arrays.ContainsSafe(storageGroups, mat.Group)) {
                        list.Add(def);
                    }
                }
                if (list.Count == 0) {
                    D.Err("Material has no compatible storages! {0}", mat);
                }
            }
        }

        public static void LinkDefs() {
            List<MatType> brokenMats = null;
            foreach (var m in Known) {
                var matType = m.Value;
                matType.Def = The.Defs.Get(matType.DefId);
                if (matType.Def != null) {
                    matType.Def.MatType = matType;
                    if (matType.Def.IgnoreMarket != matType.IgnoreMarket) {
                        D.Err("Mismatch of IgnoreMarket on Def and MatType! {0}", matType);
                    }
                    if (!Images.SpriteExists(matType.IconId)) {
                        D.Err("Material IconId not found: {0} -> {1}", matType.Id,
                            matType.IconId);
                        ModRegistry.WarnAboutBrokenMod(matType.Def, "MatType IconId not found");
                    }
                } else {
                    brokenMats ??= new();
                    brokenMats.Add(matType);
                    D.Err("MatType {0} does not have a corresponding def by id: {1}",
                        matType.Id, matType.DefId);
                    continue;
                }
                if (!string.IsNullOrEmpty(matType.ProcessableDefId)) {
                    var procDef = The.Defs.TryGet(matType.ProcessableDefId);
                    if (procDef == null) {
                        brokenMats ??= new();
                        brokenMats.Add(matType);
                        D.Err("Incorrect configuration for Material: {0}. Missing Def by ProcessableDefId: {1}",
                            matType.Id, matType.ProcessableDefId);
                        ModRegistry.WarnAboutBrokenMod(matType.Def, "MatType does not have ProcessableDefId");
                        continue;
                    }
                }
            }
            if (brokenMats != null) {
                foreach (var bm in brokenMats) {
                    D.Err("Removing broken material: {0}", bm.Id);
                    Known.Remove(bm.Id);
                    HKnown.Remove(bm.IdH);
                    ByRarity.Remove(bm);
                    RawByRarity.Remove(bm);
                }
            }
        }

        private static void AddMat(MatType s) {
            if (s.Rarity == 0) {
                D.Err("Rarity not set for mat {0}. Setting to 100", s.Id);
                s.Rarity = 100;
            }
            if (s.Property == null) {
                s.Property = MatProperty.Solid;
            }
            if (string.IsNullOrEmpty(s.DefId)) {
                s.DefId = $"Obj/Materials/{s.Id}";
            }
            if (s.GroupH == 0 && s.Group != null) {
                s.GroupH = Hashes.S(s.Group);
            }
            if (s.GroupH != 0) {
                if (!HByGroup.TryGetValue(s.GroupH, out var group)) {
                    group = new();
                    HByGroup.Add(s.GroupH, group);
                }
                group.Add(s);
            }
            if (s.GroupId == null) {
                D.Err("MatType has no GroupId: {0}", s.Id);
                s.GroupId = "Default";
            }
            s.GroupNameKey ??= $"group.{s.GroupId}";
            if (s.IdH == 0) {
                s.IdH = Hashes.S(s.Id);
            }
            Known[s.Id] = s;
            HKnown[s.IdH] = s;
            ByRarity[s] = s.Rarity;
            if (!ByMatRarity.TryGetValue(s.MatRarity, out var list)) {
                list = new List<MatType>();
                ByMatRarity.Add(s.MatRarity, list);
            }
            list.Add(s);
            if (!s.IsComposite) {
                RawByRarity[s] = s.Rarity;
            }
        }
        public static MatType FastGet(int type) {
            if (HKnown.TryGetValue(type, out var nType)) {
                return nType;
            }
            return null;
        }

        public static bool TryFindByName(string nameT, out MatType mat) {
            if (nameT == null) {
                mat = null;
                return false;
            }
            nameT = Strings.ToSearchString(nameT);
            foreach (var m in Known.Values) {
                if (m.SearchNameT == nameT) {
                    mat = m;
                    return true;
                }
            }
            mat = null;
            return false;
        }
        public static MatType Get(string type) {
            if (type == null) {
                D.Err("Getting mat type by id null");
                return null;
            }
            if (Known.TryGetValue(type, out var nType)) {
                return nType;
            }
            if (IgnoredMissing.Add(type)) {
                D.Err("Material Type {0} is not defined! See Config/Materials folder", type);
                WPopup.SystemWarning("MISSING MATERIAL",
                    $"Material of type \"{type}\" is no longer available. Perhaps a mod was removed, or this is a very old save. You can try to save a new file now and load it to see if it fixes the issue.");
            }
            return nType;
        }

        public static MatType FindByDefId(string id) {
            foreach (var mat in Known) {
                if (mat.Value.DefId == id) {
                    return mat.Value;
                }
            }
            return null;
        }
        public static MatType FindByDef(Def def) {
            foreach (var mat in Known) {
                if (mat.Value.Def == def) {
                    return mat.Value;
                }
            }
            return null;
        }

        public override string ToString() {
            return $"MatType [{Id}]";
        }

    }
}
