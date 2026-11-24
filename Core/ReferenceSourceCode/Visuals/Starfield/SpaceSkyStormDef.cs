using System.Collections.Generic;
using KL.Utils;
using UnityEngine;

namespace Game.Visuals {
    // How a space storm colors the sky, from Config/Visuals/SpaceSkyStorms. Id is the storm
    // story event id (PhotonStorm, MagneticStorm, ...) or the sector effect id (DustStorm).
    // A separate folder from SpaceSky because LoadObjects reads folders recursively.
    public sealed class SpaceSkyStormDef {
        public const string ConfigPath = "Config/Visuals/SpaceSkyStorms";

        public string Id;
        public string Color;
        public float Strength = 1f;
        // Soft flashes per second per nebula cloud (each cloud flashes on its own, there are
        // 10-20 of them), at most one per cycle
        public float FlashRate;
        // Aurora-like bands drifting through the clouds, 0 = none
        public float Shimmer;

        private static Dictionary<string, SpaceSkyStormDef> all;

        [ExecuteOnReload]
        public static void Unload() {
            all = null;
        }

        public static bool TryGet(string id, out SpaceSkyStormDef def) {
            if (all == null) {
                all = new Dictionary<string, SpaceSkyStormDef>();
                foreach (var d in The.ModLoader.LoadObjects<SpaceSkyStormDef>(ConfigPath)) {
                    all[d.Id] = d;
                }
            }
            return all.TryGetValue(id, out def);
        }

        public Vector4 ColorAndStrength {
            get {
                if (Colors.TryFromHex(Color, out var c)) {
                    return new Vector4(c.r, c.g, c.b, Strength);
                }
                D.Err("Invalid color in space sky storm {0} (should be standard hex format): {1}",
                    Id, Color);
                return Vector4.zero;
            }
        }

        public override string ToString() {
            return $"SpaceSkyStormDef[{Id}]";
        }
    }
}
