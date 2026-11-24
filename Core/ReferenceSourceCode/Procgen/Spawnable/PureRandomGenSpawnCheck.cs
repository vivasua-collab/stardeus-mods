using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Procgen {
    public sealed class PureRandomGenSpawnCheck : IGenSpawnCheck {
        private const string id = "PureRandom";
        [SelfInit]
        private static void Register() {
            GenSpawnChecks.Add(id, new PureRandomGenSpawnCheck());

        }
        public string Id => id;

        private readonly Dictionary<int, float> qc = new();

        public int Quantity(GameState s, Def def, ComponentConfig config, Rng rng) {
            int maxQuantity = config.GetInt(PropertyIdH.MaxQuantity, 2);
            int minQuantity = config.GetInt(PropertyIdH.MinQuantity, 1);
            if (maxQuantity < 2) {
                D.Err("PureRandom[{0}] MaxQuantity cannot be less than 2! Setting to 2!",
                    def.Id);
                maxQuantity = 2;
            }
            if (minQuantity >= maxQuantity) {
                D.Err("PureRandom[{0}] MinQuantity cannot be greater or equal to MaxQuantity in PureRandom check! Setting MinQuantity to 1!",
                    def.Id);
                minQuantity = 1;
            } else if (minQuantity < 1) {
                D.Err("PureRandom[{0}] MinQuantity cannot be less than 1", def.Id);
                minQuantity = 1;
            }
            float minQuantityChance = config.GetFloat(PropertyIdH.MinQuantityChance, 0.4f);
            float maxQuantityChance = config.GetFloat(PropertyIdH.MaxQuantityChance, 0.6f);
            if (minQuantityChance <= 0f && maxQuantityChance <= 0f) {
                D.Err("PureRandom[{0}] MinQuantityChance and MaxQuantityChance cannot both be zero! Setting MaxQuantityChance to 0.5f",
                    def.Id);
                maxQuantityChance = 0.5f;
            }
            qc.Clear();
            for (int i = minQuantity; i <= maxQuantity; i++) {
                qc.Add(i, Mathf.Lerp(minQuantityChance, maxQuantityChance,
                    i / (float) maxQuantity));
            }
            if (rng.TryWeightedFrom(qc, out var q)) {
                return q;
            }
            D.Err("Failed getting PureRandom quantity for {0}", def.Id);
            return 1;
        }
    }
}
