using System;
using System.Collections.Generic;
using System.Linq;
using Game.Systems.Creatures;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Naming {
    [Serializable]
    public sealed class NameInGame {
        private static readonly Dictionary<int, List<NameInGame>> all = new();

        [SelfInit]
        private static void Init() {
            all.Clear();
        }
        public string Id;
        public int IdH;
        public string FirstName;
        public string LastName;
        public string Nickname;
        public string Sex;
        private readonly static List<NameInGame> tempRes = new();

        public static NameInGame RandomFor(NameRequest req,
                HashSet<int> used) {
            if (!all.TryGetValue(req.Species.IdH, out var list)) {
                list = The.ModLoader.LoadObjects<NameInGame>(
                    $"Config/Generation/NameInGame/{req.Species.Id}");
                foreach (var t in list) {
                    t.IdH = Hashes.S(t.Id);
                }
                all.Add(req.Species.IdH, list);
            }
            return RandomFrom(req, list, used);
        }

        private static NameInGame RandomFrom(NameRequest req,
                List<NameInGame> input, HashSet<int> used) {
            tempRes.Clear();
            bool hasMultipleSexes = req.Species.Sexes?.Length > 1;
            for (int i = input.Count - 1; i >= 0; i--) {
                var tpl = input[i];
                if (used.Contains(tpl.IdH)) { continue; }
                if (hasMultipleSexes && !string.IsNullOrEmpty(tpl.Sex)) {
                    if (tpl.Sex[0] != req.Sex) {
                        continue;
                    }
                }
                tempRes.Add(tpl);
            }
            return req.Rng.From(tempRes);
        }
    }
}
