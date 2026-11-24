#if UNITY_EDITOR
// #define DEBUG_ABILITIES
#endif
using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    [Serializable]
    public sealed class Ability {
        private static readonly Dictionary<int, Ability> all = new();

        [ExecuteOnReload]
        private static void ClearStatics() {
            all.Clear();
        }

        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Register() {
            LoadKnown();
        }

        public static void LoadKnown() {
            if (all.Count == 0) {
                D.Warn("Loading abilities...");
                var types = The.ModLoader.LoadObjects<Ability>("Config/Abilities");
                foreach (var s in types) {
                    // D.Log("Registering known need type: {0}", s);
                    s.IdH = Hashes.S(s.Id);
                    if (!all.TryAdd(s.IdH, s)) {
                        D.Err("Cannot add ability: {0}. Already added!", s.Id);
                    }
                    if (s.IconId == null) {
                        #if DEBUG_ABILITIES
                        D.Err("No ability icon for {0}", s.Id);
                        #endif
                        s.IconId = Constants.IconId.CWrench2;
                    }
                }
            }
        }
        public static Ability Get(string type) {
            return Get(type, Hashes.S(type));
        }

        public static Ability Get(string type, int typeH) {
            if (all.TryGetValue(typeH, out var ability)) {
                return ability;
            }
            D.Err("Ability with id {0} (hash {1}) is not defined! See Config/Abilities folder",
                    type, typeH);
            return null;
        }
        public static Ability Get(int type) {
            if (all.TryGetValue(type, out var ability)) {
                return ability;
            }
            D.Err("Ability with id {1}, hash {0} is not defined! See Config/Abilities folder",
                    type, AbilityIdH.ResolveName(type));
            return null;
        }
        private string nameT;
        private string descT;
        public string NameT => nameT ??= $"ability.{Id}".T();
        public string DescT => descT ??= $"ability.{Id}.desc".T();
        public string Id;
        public int IdH;
        public string IconId;
        public bool IsInternal;
        public override string ToString() {
            return $"Ability: {Id}";
        }
    }
}
