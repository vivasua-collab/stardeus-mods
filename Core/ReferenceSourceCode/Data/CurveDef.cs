using System;
using System.Collections.Generic;
using System.Linq;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Data {
    public sealed class CurveDef {
        public static Dictionary<int, Curve> All = new();
        // loads before core defs, but in CoreDefs stage
        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public string Id;
        public float[] FloatSet;
        public static bool TryGet(string id, out Curve curve) {
            return All.TryGetValue(Hashes.S(id), out curve);
        }
        public static Curve Get(string id, bool throwIfMissing = true) {
            if (All.TryGetValue(Hashes.S(id), out var curve)) {
                return curve;
            }
            if (throwIfMissing) {
                throw new Exception($"CurveDef not found by id: {id}");
            }
            D.Err("CurveDef not found by id: {0}", id);
            return null;
        }
        public static Curve FastGet(int idH) {
            if (All.TryGetValue(idH, out var def)) {
                return def;
            }
            D.Err("Curve not found by id hash: {0}", idH);
            return null;
        }
        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<CurveDef>("Config/Curves");
            foreach (var type in types) {
                if (Arrays.IsEmpty(type.FloatSet)) {
                    D.Err("CurveDef does not have FloatSet: {0}", type.Id);
                    continue;
                }
                var curve = type.FloatSet;
                if (!All.TryAdd(Hashes.S(type.Id), curve)) {
                    D.Err("CurveDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
        }

        public override string ToString() {
            return $"CurveDef[{Id}]";
        }
    }
}
