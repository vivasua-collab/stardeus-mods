using System;
using System.Collections;
using System.Collections.Generic;
using Game.Components;
using Game.Components.Upgrades;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using Priority_Queue;
using UnityEngine;

namespace Game.Systems {
    public sealed class UpgradesSys : GameSystem {
        public const string SysId = "UpgradesSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new UpgradesSys());
        }

        private readonly List<Upgrade> availableUpgrades = new();
        private readonly List<UpgradesComp> upgraded = new();
        public List<UpgradesComp> Upgraded => upgraded;

        protected override void OnInitialize() {
            availableUpgrades.Clear();
        }

        public void AddToUpgraded(UpgradesComp upgrades) {
            if (!upgraded.AddUniqueInefficient(upgrades)) {
                D.Err("Upgrades already added to inventory! {0}", upgrades.Entity);
            }
        }

        public List<UpgradesComp> UpgradableWith(Upgrade upgrade) {
            if (upgrade == null) {
                D.Err("Trying to get upgrades for null upgrade");
                return null;
            }
            return S.Components.GenFindAllMatching<UpgradesComp>(
                u => u.IsUsable && u.CanOrderUpgrade && upgrade.IsCompatibleWith(u.Entity));
        }

        public void RemoveFromUpgraded(UpgradesComp upgrades) {
            upgraded.Remove(upgrades);
        }

        public override void Unload() {
            availableUpgrades.Clear();
        }

        public string GetName() {
            return "UpgradeSys";
        }
        ///<summary>When entity is null, it will return all available upgrades</summary>
        public List<Upgrade> AvailableFor(Entity entity) {
            var storages = S.Sys.Inventory.StoragesByGroup(MatGroupIdH.Upgrade);
            if (Arrays.IsEmpty(storages)) { return null; }
            var res = new List<Upgrade>();
            for (int i = storages.Count - 1; i >= 0; i--) {
                IMatStorage storage = storages[i];
                if (storage is UnstoredMatComp unstored) {
                    var upgrade = UpgradeRegistry.GetByMat(unstored.Type.Id);
                    if (upgrade == null) { continue; }
                    if (entity == null || upgrade.IsCompatibleWith(entity)) {
                        res.AddUniqueInefficient(upgrade);
                    }
                } else {
                    if (Arrays.IsEmpty(storage.Contents)) { continue; }
                    foreach (var up in storage.Contents) {
                        var type = up.Key;
                        var upgrade = UpgradeRegistry.GetByMat(type.Id);
                        if (upgrade == null) { continue; }
                        if (entity == null || upgrade.IsCompatibleWith(entity)) {
                            res.AddUniqueInefficient(upgrade);
                        }
                    }
                }
            }
            return res;
        }

        public List<Entity> WithUpgrade(string upgradeId) {
            var res = new List<Entity>();
            for (int i = 0; i < upgraded.Count; i++) {
                UpgradesComp up = upgraded[i];
                for (int i1 = 0; i1 < up.Upgrades.Length; i1++) {
                    string u = up.Upgrades[i1];
                    if (u == upgradeId) {
                        res.Add(up.Entity);
                    }
                }
            }
            return res;
        }
    }
}
