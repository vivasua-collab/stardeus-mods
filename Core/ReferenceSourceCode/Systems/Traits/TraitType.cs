using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Traits {
    public sealed class TraitType {
        public string Id;
        public int IdH;
        public string NameT;

        /// <summary>
        /// Used as a fallback if the trait of this type does not have its own icon
        /// </summary>
        public string IconId;
        private static readonly Dictionary<int, TraitType> all = new();

        [SelfInit]
        private static void Init() {
            all.Clear();
        }

        public static void LoadKnown() {
            if (all.Count != 0) { return; }
            var types = The.ModLoader.LoadObjects<TraitType>("Config/Traits/Types");
            foreach (var s in types) {
                s.IdH = Hashes.S(s.Id);
                if (!all.TryAdd(s.IdH, s)) {
                    D.Err("Cannot add body part type: {0}. Already added!", s.Id);
                }
                s.NameT = $"trait.type.{s.Id}".T();
            }
        }

        public static TraitType Get(string type) {
            return Get(type, Hashes.S(type));
        }

        public static TraitType Get(string type, int typeH) {
            if (all.TryGetValue(typeH, out var part)) {
                return part;
            }
            D.Err("TraitType with id {0} (hash {1}) is not defined! See Config/Traits/Types folder",
                    type, typeH);
            return null;
        }
        public static TraitType Get(int type) {
            if (all.TryGetValue(type, out var part)) {
                return part;
            }
            D.Err("TraitType with idHash {0} is not defined! See Config/Traits/Types folder",
                    type);
            return null;
        }

        public override string ToString() {
            return $"TraitType[{Id}]";
        }
    }
}
