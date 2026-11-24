using System.Collections.Generic;
using System.Linq;
using Game.Data.Space;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen.Names {
    public sealed class GenNameData {
        private static readonly HashSet<string> badEndings = new() {
            "the", "a", "of", "in", "at", "for", "mr.", "mrs.", "ms", "dr.", "de",
            "la", "o",
        };

        private static readonly List<string> fallbackNames = new() {
            "Buzz",
            "Fargo",
            "Delta",
            "Max",
            "Zeta",
        };

        private readonly string type;
        public readonly int Parts;
        public readonly int MaxWords;
        private Dictionary<int, string> custom;
        private Dictionary<int, string> unusedCustom;
        private readonly Dictionary<int, bool> partUniqueness = new();
        private readonly Dictionary<int, string> partNames = new();
        private readonly Dictionary<int, List<string>> options = new();
        private readonly Dictionary<int, List<string>> unusedOptions = new();
        private readonly Dictionary<float[], float> weightedChances = new();
        private static readonly float[] defaultChance = new float[] {
            1f
        };

        public GenNameData(string type, int parts, int maxWords = 3) {
            if (maxWords == 0) {
                maxWords = 3;
            }
            this.type = type;
            Parts = parts;
            MaxWords = maxWords;
        }
        public GenNameData WithCustomPath(string path) {
            if (!string.IsNullOrEmpty(path)) {
                var fullpath = $"Config/Generation/Names/{path}";
                custom = new Dictionary<int, string>();
                var customNames = new List<string>(The.ModLoader.LoadTextLines(fullpath));
                if (Arrays.IsEmpty(customNames)) {
                    D.Err("No custom names found in {0}", fullpath);
                } else {
                    foreach (var c in customNames) {
                        custom[Hashes.S(c)] = c;
                    }
                }
            }
            return this;
        }
        public GenNameData WithPartPaths(int idx, NamePartDef partDef) {
            for (int i = 0; i < partDef.Paths.Length; i++) {
                string p = partDef.Paths[i];
                WithPartPath(idx, partDef.Type, p, !partDef.NonUnique);
            }
            return this;
        }

        public GenNameData WithPartPath(int idx, string partName, string path,
                 bool unique) {
            partNames[idx] = partName;
            partUniqueness[idx] = unique;
            if (!options.TryGetValue(idx, out var opts)) {
                opts = new();
                options.Add(idx, opts);
            }
            var data = The.ModLoader.LoadTextLines(
                $"Config/Generation/Names/{path}");
            opts.AddRange(data);
            return this;
        }

        public void Validate() {
            if (options.Count != Parts) {
                D.Err("Expected {0} part paths but got {1} in GenNameData({2})",
                    options.Count, Parts, type);
            }
        }

        public GenNameData WithChances(float weight, params float[] chances) {
            weightedChances.Add(chances, weight);
            return this;
        }

        // The used-name pools belong to one game: what the previous game drew changed what
        // the same seed draws in the next one
        public void ResetUsedNames() {
            unusedCustom?.Clear();
            unusedOptions.Clear();
        }

        private string RandomOption(Rng rng, int i) {
            var allOpts = options.Get(i, null);
            return rng.From(allOpts);
        }
        private string RandomCustomOption(Rng rng,
                bool updateUsedNames, int attemptsLeft = 5) {
            // We don't have anything custom for this type
            if (Arrays.IsEmpty(custom)) { return null; }
            if (unusedCustom == null) { unusedCustom = new(); }

            // Replenish the unused options in their own order: the pick below is random
            // already, and the unseeded shuffle this had gave the same universe seed different
            // names. Shuffling with the caller's rng instead would make how much of it a name
            // uses depend on when the pool runs dry.
            if (unusedCustom.Count == 0) {
                foreach (var c in custom) {
                    unusedCustom[c.Key] = c.Value;
                }
            }
            var optKV = rng.From(unusedCustom);
            // Mark this option as used
            var optK = optKV.Item1;
            var opt = optKV.Item2;
            if (updateUsedNames) {
                unusedCustom.Remove(optK);
            }

            return opt;
        }

        private string RandomOption(Rng rng, int i, bool updateUsedNames,
                int attemptsLeft = 5) {
            if (!unusedOptions.TryGetValue(i, out var opts)) {
                opts = new List<string>();
                unusedOptions.Add(i, opts);
            }
            if (opts.Count == 0) {
                // Replenish the unused options
                var allOpts = options.Get(i, null);
                if (allOpts?.Count > 0) {
                    opts.AddRange(allOpts);
                } else {
                    D.Err("No options for index {0} in GenNameData({1})", i, type);
                    return null;
                }
            }
            var opt = rng.From(opts);
            if (updateUsedNames) {
                // Mark this option as used
                opts.Remove(opt);
            }
            return opt;
        }
        public string Generate(Rng rng) {
            List<string> parts = null;
            if (Parts > 1) {
                parts = new List<string>();
            }

            if (!rng.TryWeightedFrom(weightedChances, out var chances)) {
                chances = defaultChance;
            }

            for (int i = 0; i < chances.Length; i++) {
                if (rng.Chance(chances[i])) {
                    var part = RandomOption(rng, i);
                    if (Parts == 1) {
                        return part;
                    }
                    AddWithLimit(parts, part);
                }
            }

            return JoinWithCheck(parts);
        }

        private string JoinWithCheck(List<string> parts) {
            if (parts.Count == 0) {
                D.ErrSoft("Trying to generate a name from zero parts! Falling back to pure random. Context: {0}", type);
                return Rng.UFrom(fallbackNames);
            }
            while (parts.Count > 1 && badEndings.Contains(
                    parts[^1].ToLowerInvariant())) {
                parts.RemoveAt(parts.Count - 1);
            }
            var res = string.Join(" ", parts);
            if (string.IsNullOrEmpty(res)) {
                D.ErrSoft("Got a blank name in JoinWithCheck! Context: {0}", type);
                return Rng.UFrom(fallbackNames);
            }
            return res;
        }

        private void AddWithLimit(List<string> parts, string part) {
            if (parts.Count >= MaxWords) { return; }
            if (part == null) {
                D.Err("Trying to add null part! {0} {1} {2}",
                    type, part, D.Dump(parts));
                return;
            }
            if (!part.Contains(" ")) {
                parts.AddUnlessBlank(part);
                return;
            }
            var partPieces = part.Split(" ");
            for (int i = 0; i < partPieces.Length; i++) {
                if (parts.Count >= MaxWords) { return; }
                parts.AddUnlessBlank(partPieces[i]);
            }
        }

        public string Generate(Rng rng, bool updateUsedNames) {
            List<string> parts = null;
            if (Parts > 1) {
                parts = new();
            }

            var chances = rng.WeightedFrom(weightedChances);

            for (int i = 0; i < chances.Length; i++) {
                if (rng.Chance(chances[i])) {
                    var part = RandomOption(rng, i, updateUsedNames);
                    if (Parts == 1) {
                        return part;
                    }
                    AddWithLimit(parts, part);
                }
            }

            return JoinWithCheck(parts);
        }

        public string GenerateSO(SpaceObjectType type, Rng rng,
                bool updateUsedNames, float customNameChance) {
            if (rng.Chance(customNameChance)) {
                var cust = RandomCustomOption(rng, updateUsedNames);
                if (!string.IsNullOrEmpty(cust)) {
                    return cust;
                }
            }
            if (Parts == 0) {
                return type?.NameT ?? $"X-{Mathf.Abs(rng.Int)}";
            }

            List<string> parts = null;
            if (Parts > 1) {
                parts = new();
            }

            var chances = rng.WeightedFrom(weightedChances);

            for (int i = 0; i < chances.Length; i++) {
                if (rng.Chance(chances[i])) {
                    var part = RandomOption(rng, i, updateUsedNames);
                    if (Parts == 1) {
                        return part;
                    }
                    AddWithLimit(parts, part);
                }
            }

            return JoinWithCheck(parts);
        }

        public void Generate(Rng rng, BeingName name,
                Species species, bool updateUsedNames) {
            var chances = rng.WeightedFrom(weightedChances);

            List<string> nickParts = null;
            var hasFullName = species.HasFullName;
            if (!hasFullName) {
                nickParts = new List<string>();
            }

            for (int i = 0; i < chances.Length; i++) {
                if (rng.Chance(chances[i])) {
                    var part = RandomOption(rng, i, updateUsedNames);
                    if (hasFullName) {
                        MapPart(name, species, i, part);
                    } else {
                        AddWithLimit(nickParts, part);
                    }
                }
            }

            if (!hasFullName) {
                name.Nickname = JoinWithCheck(nickParts);
            }
        }

        private void MapPart(BeingName name, Species species, int i, string part) {
            var partName = partNames.Get(i, null);
            if (partName == null) {
                D.Err("Unknown part name: {0} for {1} index {2}",
                    part, species.Name, i);
                // p.Nickname = part;
                return;
            }
            switch (partName) {
                case "FirstName": name.FirstName = part; break;
                case "LastName": name.LastName = part; break;
                case "Nickname": name.Nickname = part; break;
            }
        }
    }
}
