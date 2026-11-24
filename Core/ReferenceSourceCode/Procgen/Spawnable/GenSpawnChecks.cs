using System.Collections.Generic;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Procgen {
    public static class GenSpawnChecks {
        private static readonly Dictionary<string, IGenSpawnCheck> registry = new();
        private static readonly IGenSpawnCheck fallback = new PureRandomGenSpawnCheck();

        [ExecuteOnReload]
        public static void CleanupStatics() {
            registry.Clear();
        }


        public static IGenSpawnCheck Get(string id) {
            if (id == null) { return null; }
            if (registry.TryGetValue(id, out var check)) {
                return check;
            }
            D.Err("Failed getting IGenSpawnCheck by id: {0}. Fallback to: {1}",
                id, fallback.Id);
            return fallback;
        }

        public static void Add(string id, IGenSpawnCheck check) {
            registry[id] = check;
        }
    }
}
