using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Modifiers;
using Newtonsoft.Json;

namespace Game.Research {
    [Serializable]
    public class Researchable {
        [NonSerialized] public int NodeDepth = -1;
        [NonSerialized] public int DirectDepCount = -1;
        [NonSerialized] public Def Def;
        [JsonIgnore] public bool IsCostless => Def?.Tier == 0;
        public Mat[] DatoidCosts;
        public bool IsDiscoverable;
        // Means that if parent is discovered, this node will become "discovered" automatically
        public bool IsAutoDiscovered;
        // Means that it's not researchable or usable by player tooling
        public bool IsTreeNode;
        // We will automatically determine this to figure out the datoid cost if needed
        public int UnlockValue;
        // How much the datoids cost for this one, automatically calculated
        public float UnlockCost;
        public bool SkipCostValidation;
        [JsonIgnore] public string NameT => Def.NameT;
        public int Storage;
        //[Obsolete("Make an int[] cache")]
        // TODO make an int[] cache
        public string[] Prerequisites;
        public bool CanBeResearchedNow;
        // Whenever we unlock this def through research, these effects will be applied for the game
        // They have to be re-applied on load save, so they are not supposed to be saveable / loadable
        public string[] UnlockEffects;
        [JsonIgnore] public bool HasUnlockEffects =>!Arrays.IsEmpty(UnlockEffects);
        private Mat[] AdjustedDatoidCostsCache;
        private Difficulty AdjustedDatoidDifficulty;
        private float AdjustedDatoidMul;
        public static bool TreeNode(Def def) {
            return TreeNode(def.Researchable);
        }
        public static bool TreeNode(Researchable r) {
            return r?.IsTreeNode == true;
        }
        public static bool IsEmptyPrereq(Def def) {
            return IsEmptyPrereq(def.Researchable);
        }
        public static bool IsEmptyPrereq(Researchable r) {
            return r == null || Arrays.IsEmpty(r.Prerequisites);
        }
        public Mat[] AdjustedDatoidCosts(Difficulty diff) {
            if (diff.DatoidCostScale == 1f && ModifierResearchCost.Current == 1f) {
                return DatoidCosts;
            }
            // Defs outlive a game, so the cache must also follow the modifier of the current one
            if (AdjustedDatoidDifficulty != diff
                    || AdjustedDatoidMul != ModifierResearchCost.Current) {
                AdjustedDatoidDifficulty = diff;
                AdjustedDatoidMul = ModifierResearchCost.Current;
                AdjustedDatoidCostsCache = null;
            }
            if (AdjustedDatoidCostsCache == null) {
                var res = new Mat[DatoidCosts.Length];
                for (int i = 0; i < DatoidCosts.Length; i++) {
                    var c = DatoidCosts[i];
                    var num = diff.AdjustedDatoidCost(c.StackSize);
                    num = ModifierResearchCost.Apply(num);
                    c.StackSize = num;
                    c.MaxStackSize = num;
                    res[i] = c;
                }
                AdjustedDatoidCostsCache = res;
            }
            return AdjustedDatoidCostsCache;
        }

        public Researchable Copy() {
            var copy = new Researchable {
                IsTreeNode = IsTreeNode,
                UnlockValue = UnlockValue,
                Storage = Storage,
            };
            if (!Arrays.IsEmpty(DatoidCosts)) {
                copy.DatoidCosts = new Mat[DatoidCosts.Length];
                Array.Copy(DatoidCosts, copy.DatoidCosts, DatoidCosts.Length);
            }
            if (!Arrays.IsEmpty(Prerequisites)) {
                copy.Prerequisites = new string[Prerequisites.Length];
                Array.Copy(Prerequisites, copy.Prerequisites,
                    Prerequisites.Length);
            }
            if (!Arrays.IsEmpty(UnlockEffects)) {
                copy.UnlockEffects = new string[UnlockEffects.Length];
                Array.Copy(UnlockEffects, copy.UnlockEffects, UnlockEffects.Length);
            }
            return copy;
        }

        public static Researchable Merge(Researchable baseRes, Researchable res) {
            if (baseRes == null && res == null) {
                return null;
            }
            if (baseRes == null) {
                baseRes = new Researchable();
            }
            if (res == null) {
                res = new Researchable();
            }
            // must be a copy!
            var r = new Researchable();
            r.UnlockValue = Mathf.Max(baseRes.UnlockValue, res.UnlockValue);
            r.Storage = Mathf.Max(baseRes.Storage, res.Storage);
            r.IsTreeNode = baseRes.IsTreeNode || res.IsTreeNode;
            var resHasPr = !Researchable.IsEmptyPrereq(res);
            r.Prerequisites = resHasPr ?
                res.Prerequisites : baseRes.Prerequisites;
            var resHasUE = !Arrays.IsEmpty(res.UnlockEffects);
            r.UnlockEffects = resHasUE ? res.UnlockEffects : baseRes.UnlockEffects;
            var hasCosts = !Arrays.IsEmpty(res.DatoidCosts);
            r.DatoidCosts = hasCosts ? res.DatoidCosts : baseRes.DatoidCosts;
            return r;
        }
    }
}
