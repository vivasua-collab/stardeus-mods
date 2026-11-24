using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.BodyParts {
    [Serializable]
    public sealed class BodyConfiguration {
        [Serializable]
        public struct Assignment {
            public string Slot;
            public string Part;
            public bool Empty;
        }
        public string Id;
        public bool IsDefault;
        public string SpeciesType;
        public Species Species;
        public Assignment[] Parts;
        public Dictionary<string, BodyPartDef> SlotAssignments;
        public static readonly List<BodyConfiguration> All = new();

        [SelfInit]
        private static void Init() {
            All.Clear();
        }

        public static void LoadKnown() {
            if (All.Count > 0f) { return; }
            var types = The.ModLoader.LoadObjects<BodyConfiguration>("Config/Body/Configurations");
            foreach (var def in types) {
                def.SlotAssignments = new Dictionary<string, BodyPartDef>();
                // too early to get species here
                foreach (var p in def.Parts) {
                    if (p.Empty) {
                        def.SlotAssignments[p.Slot] = null;
                    } else {
                        var part = BodyPartDef.Get(p.Part);
                        if (part == null) {
                            D.Err("Incorrect part id in body configuration {0}: Slot: {1}, Part: {2}",
                                def.Id, p.Slot, p.Part);
                            continue;
                        }
                        def.SlotAssignments[p.Slot] = part;
                    }
                }
                All.Add(def);
            }
            Ready.When(WhenH.CoreLoaded1, LinkSpecies);
        }

        private static void LinkSpecies() {
            for (int i = All.Count - 1; i >= 0; i--) {
                BodyConfiguration cfg = All[i];
                cfg.Species = Species.Get(cfg.SpeciesType);
                if (cfg.Species == null) {
                    D.Err("Could not find species by id {0} for body configuration {1}. See Config/Body/Configurations/{0}.json",
                        cfg.SpeciesType, cfg.Id);
                    All.RemoveAt(i);
                }
            }
        }

        public static BodyConfiguration ForSpecies(Species species) {
            BodyConfiguration fallback = null;
            for (int i = All.Count - 1; i >= 0; i--) {
                BodyConfiguration cfg = All[i];
                if (cfg.Species == species) {
                    if (cfg.IsDefault) {
                        return cfg;
                    }
                    fallback = cfg;
                }
            }
            if (fallback == null) {
                D.Err("Could not find any body configuration for species: {0}. See Config/Body/Configurations",
                    species);
            }
            return fallback;
        }
    }
}
