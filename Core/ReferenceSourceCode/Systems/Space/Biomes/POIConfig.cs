using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    /// <summary>
    /// Defines how specific points of interest get spawned
    /// </summary>
    public sealed class POIConfig : SpaceAssignable {
        [SelfInit]
        private static void Init() {
            All.Clear();
        }
        public static readonly Dictionary<string, POIConfig> All = new();
        public static void LoadKnown() {
            var all = The.ModLoader.LoadObjects<POIConfig>("Config/Space/POIs");
            foreach (var cfg in all) {
                cfg.IdH = Hashes.S(cfg.Id);
                if (!All.TryAdd(cfg.Id, cfg))  {
                    D.Err("POIConfig with id {0} already exists", cfg.Id);
                    continue;
                }
            }
            if (Arrays.IsEmpty(All)) {
                D.Err("Failed to load any Space/POIs!");
            }
        }
        public float ScanDifficulty;
        public string Type;
        /// <summary>
        /// If POI produces ship, we won't put it into knowledge.
        /// It will be either Space Station or Derelict Ship.
        /// </summary>
        public bool ProducesShip;

        public override string ToString() {
            return $"POIConfig[{Id}]";
        }
        public static POIConfig TryGet(string id) {
            if (All.TryGetValue(id, out var cfg)) {
                return cfg;
            }
            return null;
        }

        public static POIConfig Get(string id) {
            if (All.TryGetValue(id, out var cfg)) {
                return cfg;
            }
            D.Err("POIConfig not available for id: {0}", id);
            return null;
        }
    }
}
