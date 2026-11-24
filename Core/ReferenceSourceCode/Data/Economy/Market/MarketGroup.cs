using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Mods;
using KL.Utils;
using Game.Utils;
using UnityEngine;

namespace Game.Data.Economy {
    [Serializable]
    public class MarketGroup {
        [SelfInit]
        private static void Register() {
            All.Clear();
        }
        public static readonly Dictionary<int, MarketGroup> All = new();
        public string Id;
        public string NameT;
        public int IdH;
        public float Rarity;
        /// <summary>
        /// This will be multiplied by TraderQuantityCurve
        /// </summary>
        public float TraderQuantity;
        /// <summary>
        /// A ceiling for trader quantity, if > 0, we will never see a trader offer
        /// more than this amount of an item
        /// </summary>
        public int MaxTraderQuantity;
        public float Volatility;
        public float RecessionEffect;
        public static void LoadMarketGroups() {
            var groups = The.ModLoader.LoadObjects<MarketGroup>(
                "Config/Economy/MarketGroups");
            foreach (var g in groups) {
                g.IdH = Hashes.S(g.Id);
                if (g.TraderQuantity > 0f) {
                    if (g.Rarity == 0f) {
                        D.Err("Market group has no rarity! {0}", g.Id);
                        g.Rarity = 0.5f;
                    }
                    if (g.Volatility > 0.0019f) {
                        D.Err("Volatility too high for traded group: {0}: {1}",
                            g.Id, g.Volatility);
                        g.Volatility = 0.0019f;
                    }
                } else {
                    if (g.Volatility > 0.019f) {
                        D.Err("Volatility too high for non traded group: {0}: {1}",
                            g.Id, g.Volatility);
                        g.Volatility = 0.019f;
                    }
                }
                g.NameT = $"market.group.name.{g.Id}".T();
                if (g.RecessionEffect == 0f) {
                    g.RecessionEffect = 1f;
                }
                if (!All.TryAdd(g.IdH, g)) {
                    D.Err("Trying to load same market group more than once: {0}", g);
                }
            }
        }
        public static MarketGroup Find(int id) {
            return All[id];
        }
        public static MarketGroup Find(string id) {
            if (!All.TryGetValue(Hashes.S(id), out var g)) {
                D.Err("MarketGroup not available by id: {0}", id);
                // CreateMarketGroupTemplate(id);
            }
            return g;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void CreateMarketGroupTemplate(string id) {
            var file = $"{id}.json";
            var dir = "Config/Economy/MarketGroups";
            dir = ModLoader.CoreModFilePath(dir);
            var filePath = $"{dir}/{file}";
            if (File.Exists(filePath)) { return; }
            var sb = Caches.StringBuilder2;
            sb.Clear();
            sb.AppendLine(" FIX THIS! ");
            sb.AppendLine("{");
            sb.AppendFormat("    \"Id\" : \"{0}\",\n", id);
            sb.AppendLine(  "    \"RecessionEffect\" : 1.0,");
            sb.AppendLine(  "    \"Volatility\" : 0.002");
            sb.AppendLine("}");
            Files.EnsureDir(dir);
            File.WriteAllText(filePath, sb.ToString());
            D.Err("Created market group file! {0}", filePath);
        }
    }
}
