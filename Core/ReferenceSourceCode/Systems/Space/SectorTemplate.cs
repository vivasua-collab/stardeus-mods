using System;
using System.Collections.Generic;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Space {
    public sealed class SectorTemplate : SpaceEntityTemplate {
        public static readonly Dictionary<string, SectorTemplate> All = new();
        [SelfInit]
        private static void Init() {
            All.Clear();
        }
        public static void LoadKnown() {
            var all = The.ModLoader.LoadObjects<SectorTemplate>("Config/Space/SectorTemplates");
            if (Arrays.IsEmpty(all)) {
                D.Err("Failed to load any Space/SectorTemplates!");
            }
            foreach (var rt in all) {
                rt.IdH = Hashes.S(rt.Id);
                if (!All.TryAdd(rt.Id, rt)) {
                    D.Err("Duplicate SectorTemplate.Id: {0}", rt.Id);
                }
            }
        }

        public string Type;
        public bool HasOrbit;
        /// <summary>
        /// If this is set to true, we will check DifficultyChance against Region difficulty
        /// before trying to assign this template to any of the sectors in the region.
        /// And if the first attempt to roll fails, we will remember that this region cannot
        /// have this template.
        /// </summary>
        public bool CheckRegionChance;
        /// <summary>
        /// If true, this sector cannot be maked as inbound when some ship jumps
        /// from an adjacent region into current region. For hyperjump relays
        /// and dangerous sectors like stars.
        /// </summary>
        public bool CantBeInbound;
        /// <summary>
        /// Ships are what this sector is (trade zone stations, derelict zone wrecks).
        /// Removing them would leave an empty shell that still looks like one, so such a
        /// sector can never be a secluded start.
        /// </summary>
        public bool ContentIsShips;
        public float[] NumPOIsChance;
        public Curve NumPOIsCurve => NumPOIsChance;
        public float[] NumDepositsChance;
        public Curve NumDepositsCurve => NumDepositsChance;
        /// <summary>
        /// A random distribution of wanted number of ships in this sector
        /// </summary>
        public float[] NPCShipDensity;
        private Curve npcShipDensityCurve;
        public Curve NPCShipDensityCurve => npcShipDensityCurve ??= NPCShipDensity;
        /// <summary>
        /// This template will not be revealed in starmap until you visit the object
        /// </summary>
        public bool Surprise;
        /// <summary>
        /// This template can only be assigned to a space object that can have POIs (planet, asteroid zone)
        /// </summary>
        public bool MustHavePOIs;
        public float TemplateWeight;

        public override string ToString() {
            return $"SectorTemplate[{Id}]";
        }

        public static SectorTemplate Get(string id) {
            if (id == null) {
                D.Err("Trying to get null sector template, fallback to EmptySpace");
                id = "EmptySpace";
            }
            if (All.TryGetValue(id, out var tpl)) {
                return tpl;
            }
            D.Err("Failed to find SectorTemplate by id: {0}", id);
            return null;
        }
        public override bool IsAllowedFor(ISpaceEntity ntt, Rng rng) {
            if (!CheckRegionChance) {
                return true;
            }
            if (ntt is not SpaceRegion reg) {
                if (ntt is SpaceSector sec) {
                    reg = sec.Region;
                } else {
                    D.Err("Unknown space entity type! {0}", ntt);
                    return true;
                }
            }
            if (reg.Biome.IsTemplateAllowed(this, out var hasMemory)) {
                if (!hasMemory) {
                    var chance = DifficultyCurve.Evaluate(reg.Biome.Difficulty);
                    var isAllowed = rng.Chance(chance);
                    reg.Biome.AllowTemplate(this, isAllowed);
                    return isAllowed;
                }
                return true;
            }
            return false;
        }

    }
}
