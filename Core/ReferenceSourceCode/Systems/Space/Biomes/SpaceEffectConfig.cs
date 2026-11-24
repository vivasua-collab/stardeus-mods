using System.Collections.Generic;
using Game.Data;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Space {
    public sealed class SpaceEffectConfig : SpaceAssignable {
        [SelfInit]
        private static void Init() {
            All.Clear();
        }
        public static readonly Dictionary<string, SpaceEffectConfig> All = new();
        public static void LoadKnown() {
            var all = The.ModLoader.LoadObjects<SpaceEffectConfig>("Config/Space/Effects");
            if (Arrays.IsEmpty(all)) {
                D.Err("Failed to load any Space/SpaceEffects!");
            }
            foreach (var cfg in all) {
                if (!All.TryAdd(cfg.Id, cfg)) {
                    D.Err("SpaceEffectConfig with id {0} is already registered! {0}", cfg.Id);
                    continue;
                }
                var fx = SpaceEffect.Get(cfg.Id);
                cfg.IdH = Hashes.S(cfg.Id);
                cfg.NeverUnavailable = true;
                if (fx != null) {
                    fx.Config = cfg;
                    cfg.Effect = fx;
                }
            }
        }

        public SpaceEffect Effect;

        public override string ToString() {
            return $"SpaceFX[{Id}]";
        }
        public static SpaceEffectConfig Get(string id) {
            if (All.TryGetValue(id, out var cfg)) {
                return cfg;
            }
            D.Err("SpaceEffect not available for id: {0}", id);
            return null;
        }
    }
}
