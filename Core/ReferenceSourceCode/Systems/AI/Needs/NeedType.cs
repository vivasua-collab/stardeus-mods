#if UNITY_EDITOR
// #define DEBUG_NEEDS
#endif
using System;
using System.Collections.Generic;
using Game.Data;
using Game.Systems.AI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Constants {
    [Serializable]
    public class NeedType {
        private static readonly Dictionary<int, NeedType> knownNeeds = new();

        [SelfInit]
        private static void Init() {
            // We want them to be fresh in unity
            knownNeeds.Clear();
        }
        public static NeedType Get(string type) {
            return Get(Hashes.S(type));
        }
        public static NeedType Get(int type) {
            if (knownNeeds.TryGetValue(type, out var need)) {
                return need;
            }
            D.Err("Need Type {0} ({1}) is not defined! See Config/Needs folder",
                    type, NeedIdH.ResolveName(type));
            return null;
        }

        public int IdHash;
        public AINeed Need;
        public int Priority;
        private string nameT;
        private string infoT;
        public string NameKey;
        public string NameT => nameT ??= (NameKey ?? $"need.{Id}").T();
        public string InfoT => infoT ??= $"need.{Id}.info".T();
        public string Id;
        public string IconId;
        #if DEBUG_NEEDS
        private const bool warnIfMissing = true;
        #else
        private const bool warnIfMissing = false;
        #endif
        public static void LoadKnown() {
            if (knownNeeds.Count == 0) {
                var types = The.ModLoader.LoadObjects<NeedType>("Config/Needs");
                foreach (var s in types) {
                    // D.Log("Registering known need type: {0}", s);
                    s.IdHash = Hashes.S(s.Id);
                    knownNeeds[s.IdHash] = s;
                    var need = Registries.AINeeds.Get(s.Id, s.IdHash, warnIfMissing);
                    if (need != null) {
                        need.Type = s;
                        s.Need = need;
                    }
                }
            }
            // validate all needs if we have a type for each
            foreach (var need in Registries.AINeeds.All) {
                if (need.Value.Type == null) {
                    D.Err("NeedType is not available: {0}", need.Value.Id);
                }
            }
        }
        public override string ToString() {
            return $"NeedType: {Id}";
        }
    }
}
