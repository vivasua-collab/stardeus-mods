using System;
using System.Collections.Generic;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Space {
    public sealed class RegionTemplate : SpaceEntityTemplate {
        public static readonly Dictionary<string, RegionTemplate> All = new();
        [SelfInit]
        private static void Init() {
            All.Clear();
        }
        public static void LoadKnown() {
            var all = The.ModLoader.LoadObjects<RegionTemplate>("Config/Space/RegionTemplates");
            if (Arrays.IsEmpty(all)) {
                D.Err("Failed to load any Space/RegionTemplates!");
            }
            foreach (var rt in all) {
                rt.IdH = Hashes.S(rt.Id);
                if (!All.TryAdd(rt.Id, rt)) {
                    D.Err("Duplicate RegionTemplate.Id: {0}", rt.Id);
                }
            }
        }


        /// <summary>
        /// Ids of SectorTemplate that will be forced to exist in this region.
        /// Can be non-unique, if you want more than one instance of a sector in
        /// that region.
        /// </summary>
        public string[] ForceSectors;
        /// <summary>
        /// SectorTemplate Ids that cannot appear in this region
        /// </summary>
        public string[] ExcludeSectors;

        public override string ToString() {
            return $"RegionTemplate[{Id}]";
        }
        public static RegionTemplate Get(string id) {
            if (id == null) {
                D.Err("Trying to get null sector template, fallback to UnclaimedStarSystem");
                id = "UnclaimedStarSystem";
            }
            if (All.TryGetValue(id, out var tpl)) {
                return tpl;
            }
            D.Err("RegionTemplate not available by id: {0}", id);
            return null;
        }

        public override bool IsAllowedFor(ISpaceEntity ntt, Rng rng) {
            // A faction the player has wiped out must never get a new home region, not even
            // through a full universe regeneration via a dark portal.
            if (RogueAIUtils.IsFactionEliminated(ForceFaction)) {
                return false;
            }
            // we have more checks elsewhere for regions, this is mostly for sectors
            return true;
        }
    }
}
