using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Migrations;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Components.Upgrades {
    public static class UpgradeRegistry {
        [ExecuteOnReload]
        private static void CleanupStatics() {
            registry.Clear();
            byMatType.Clear();
        }

        private static readonly Dictionary<string, Upgrade> registry = new();
        private static readonly Dictionary<string, Upgrade> byMatType = new();
        public static Dictionary<string, Upgrade> All => registry;

        public static Upgrade Get(string id) {
            var f = registry.Get(id, null);
            return f;
        }

        public static bool IsUpgrade(Def d) {
            if (d.MatType == null) { return false; }
            return byMatType.ContainsKey(d.MatType.Id);
        }

        public static Upgrade GetByMat(string id) {
            var f = byMatType.Get(id, null);
            if (f == null) {
                D.Err("Unknown upgrade by mat type: {0}", id);
                return null;
            }
            return f;
        }
        public static bool TryGetByMat(string id, out Upgrade upgrade) {
            return byMatType.TryGetValue(id, out upgrade);
        }

        public static void Add(string type, Upgrade upgrade) {
            D.Ass(!registry.ContainsKey(type),
                "Registry already contains upgrade {0}!", type);
            registry[type] = upgrade;
            byMatType[upgrade.MatTypeId] = upgrade;
        }
    }
}
