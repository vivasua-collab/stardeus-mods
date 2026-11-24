using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Creatures;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Procgen.Names {
    // [Obsolete("Use Naming System!")]
    public static class GenNamesLegacy {
        private static readonly Dictionary<string, GenNameData> genericData = new();
        private static readonly Dictionary<Species,
            Dictionary<char, GenNameData>> nameData = new();
        [SelfInit]
        private static void Cleanup() {
            genericData.Clear();
            nameData.Clear();
        }

        // after every game, keeping the loaded name lists
        public static void ResetUsedNames() {
            foreach (var data in genericData.Values) {
                data.ResetUsedNames();
            }
            foreach (var bySpecies in nameData.Values) {
                foreach (var data in bySpecies.Values) {
                    data.ResetUsedNames();
                }
            }
        }

        public static GenNameData For(int spaceObjectTypeId) {
            var type = SpaceObjectType.Find(spaceObjectTypeId);
            if (type == null) {
                D.Err("Couldn't find SpaceObjType by idH: {0}",
                    SpaceObjectTypeIdH.ResolveName(spaceObjectTypeId));
            }
            return For(type);
        }
        public static GenNameData For(string spaceObjectTypeId) {
            var type = SpaceObjectType.Find(spaceObjectTypeId);
            if (type == null) {
                D.Err("Couldn't find SpaceObjType by id: {0}", spaceObjectTypeId);
            }
            return For(type);
        }

        public static GenNameData For(SpaceObjectType t) {
            if (genericData.TryGetValue(t.Id, out var gen)) {
                return gen;
            }
            gen = new GenNameData(t.Id, t.NameParts?.Length ?? 0, t.NameMaxWords);
            gen.WithCustomPath(t.CustomNames);

            if (gen.Parts == 0) {
                return gen;
            }

            var chances = new List<float[]>();
            for (int ci = 0; ci < t.NameChanceWeights.Length; ci++) {
                chances.Add(new float[gen.Parts]);
            }

            for (int i = 0; i < t.NameParts.Length; i++) {
                var np = t.NameParts[i];
                gen.WithPartPaths(i, np);
                for (int ci = 0; ci < t.NameChanceWeights.Length; ci++) {
                    chances[ci][i] = np.Chances[ci];
                }
            }

            for (int ci = 0; ci < t.NameChanceWeights.Length; ci++) {
                gen.WithChances(t.NameChanceWeights[ci], chances[ci]);
            }

            gen.Validate();
            genericData.Add(t.Id, gen);
            return gen;
        }

        // FIXME deduplicate with SpaceObjectType
        public static GenNameData For(ColonistFate fate) {
            if (genericData.TryGetValue(fate.Id, out var gen)) {
                return gen;
            }
            gen = new(fate.Id, fate.NameParts?.Length ?? 0,
                fate.NameMaxWords);
            gen.WithCustomPath(fate.CustomNames);

            if (gen.Parts == 0) {
                return gen;
            }

            var chances = new List<float[]>();
            for (int ci = 0; ci < fate.NameChanceWeights.Length; ci++) {
                chances.Add(new float[gen.Parts]);
            }

            for (int i = 0; i < fate.NameParts.Length; i++) {
                var np = fate.NameParts[i];
                gen.WithPartPaths(i, np);
                for (int ci = 0; ci < fate.NameChanceWeights.Length; ci++) {
                    chances[ci][i] = np.Chances[ci];
                }
            }

            for (int ci = 0; ci < fate.NameChanceWeights.Length; ci++) {
                gen.WithChances(fate.NameChanceWeights[ci], chances[ci]);
            }

            gen.Validate();
            genericData.Add(fate.Id, gen);
            return gen;
        }

        public static GenNameData For(Species sp, char sex) {
            if (!nameData.TryGetValue(sp, out var spNames)) {
                spNames = new Dictionary<char, GenNameData>();
                nameData.Add(sp, spNames);
            }
            if (spNames.TryGetValue(sex, out var gen)) {
                return gen;
            }
            if (sp.NameParts == null) {
                // D.Warn("NameParts not set on species: {0}", sp.Type);
                return null;
            }
            gen = new GenNameData($"{sp.Id}/{sex}",
                sp.NameParts.Length, 3);

            var chances = new List<float[]>();
            for (int ci = 0; ci < sp.NameChanceWeights.Length; ci++) {
                chances.Add(new float[gen.Parts]);
            }

            for (int i = 0; i < sp.NameParts.Length; i++) {
                var np = sp.NameParts[i];
                gen.WithPartPaths(i, np);
                var beingSex = sp.GetSex(sex);
                if (beingSex != null && !Arrays.IsEmpty(beingSex.NameParts)) {
                    for (int j = 0; j < beingSex.NameParts.Length; j++) {
                        var gnp = beingSex.NameParts[j];
                        if (gnp.Type == np.Type) {
                            gen.WithPartPaths(i, gnp);
                        }
                    }
                }
                for (int ci = 0; ci < sp.NameChanceWeights.Length; ci++) {
                    chances[ci][i] = np.Chances[ci];
                }
            }

            for (int ci = 0; ci < sp.NameChanceWeights.Length; ci++) {
                gen.WithChances(sp.NameChanceWeights[ci], chances[ci]);
            }

            gen.Validate();
            spNames.Add(sex, gen);
            return gen;
        }
    }
}
