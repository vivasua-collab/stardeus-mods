using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data.Factions;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Diplomacy {
    [Serializable]
    public class FactionAttitudeDef {
        public static Dictionary<int, FactionAttitudeDef> All = new();
        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }
        public string Id;
        public int IdH;
        public FactionAttitudeChance[] AttitudeChances;
        public FactionAttitudeChance DefaultAttitude;
        public float RandomAttitudeTowards(FactionDef faction, Rng rng) {
            foreach (var attChance in AttitudeChances) {
                if (attChance.FactionId == faction.Id) {
                    return rng.Curve(attChance.Curve);
                }
            }
            if (DefaultAttitude == null) {
                D.Err("Default attitude chance not defined for {0}", Id);
                return 0f;
            }
            return rng.Curve(DefaultAttitude.Curve);
        }
        /// <summary>
        /// Deterministic centre of the same distribution RandomAttitudeTowards samples.
        /// Used as the baseline that NPC era drift targets are offset from, so no extra
        /// save state is needed to remember where a pair "belongs".
        /// </summary>
        public float AverageAttitudeTowards(FactionDef faction) {
            foreach (var attChance in AttitudeChances) {
                if (attChance.FactionId == faction.Id) {
                    return attChance.Curve.Average;
                }
            }
            if (DefaultAttitude == null) {
                D.Err("Default attitude chance not defined for {0}", Id);
                return 0f;
            }
            return DefaultAttitude.Curve.Average;
        }
        public static FactionAttitudeDef Get(string id) {
            if (All.TryGetValue(Hashes.S(id), out var tag)) {
                return tag;
            }
            D.Err("FactionAttitude not found by id: {0}", id);
            return All[Hashes.S("Default")];
        }
        public static FactionAttitudeDef FastGet(int idH) {
            if (All.TryGetValue(idH, out var def)) {
                return def;
            }
            D.Err("FactionAttitude not found by id hash: {0}", idH);
            return All[Hashes.S("Default")];
        }
        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<FactionAttitudeDef>("Config/Diplomacy/FactionAttitudes");
            foreach (var type in types) {
                // D.Warn("attitude: {0}->{1}", type.Id, D.DumpCollection(type.AttitudeChances));
                type.IdH = Hashes.S(type.Id);
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("FactionAttitudeDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
        }
    }
}
