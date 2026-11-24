using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Components.Upgrades;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class UninstallUpgradeAction : BaseEntityAction {
        public const string ActionId = "UninstallUpgrade";

        [SelfInit]
        private static void Register() {
            RegisterAction(new UninstallUpgradeAction());
        }

        public override string Id => ActionId;

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            int cnt = 0;
            foreach (var t in tiles) {
                if (HasInstalled(t)) {
                    cnt++;
                }
            }
            if (cnt == 0) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, s, new List<Tile>(tiles), cnt),
                Priority.Normal);
            return true;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                GameState s, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CUpgrade, T.Remove,
                () => ShowUninstallChoice(s, tiles));
            return widget;
        }

        private static void ShowUninstallChoice(GameState s, List<Tile> tiles) {
            var upgradeTargets = new Dictionary<Upgrade, List<UpgradesComp>>();

            foreach (var tile in tiles) {
                if (!tile.IsActive) { continue; }
                if (tile.IsImmutable) { continue; }
                if (!tile.TryGetComponent<UpgradesComp>(out var comp)) {
                    continue;
                }
                if (Arrays.IsEmpty(comp.Upgrades)) {
                    continue;
                }
                for (int i = comp.Upgrades.Length - 1; i >= 0; i--) {
                    var upgId = comp.Upgrades[i];
                    if (upgId == null) {
                        continue;
                    }
                    var upg = UpgradeRegistry.Get(upgId);
                    if (upg == null) {
                        continue;
                    }
                    if (!upgradeTargets.TryGetValue(upg, out var list)) {
                        list = new List<UpgradesComp>();
                        upgradeTargets[upg] = list;
                    }
                    if (!list.Contains(comp)) {
                        list.Add(comp);
                    }
                }
            }

            if (upgradeTargets.Count == 0) {
                return;
            }

            if (upgradeTargets.Count == 1) {
                foreach (var kv in upgradeTargets) {
                    OrderUninstallAll(kv.Key, kv.Value);
                    A.S.UIQuery.RebuildEntityActions.Ask();
                    return;
                }
            }

            var qc = QuickChoice.Create(T.Remove);
            foreach (var kv in upgradeTargets) {
                var upg = kv.Key;
                var targets = kv.Value;
                qc.AddButton($"{upg.Title} ({Units.XNum(targets.Count)})", () => {
                    OrderUninstallAll(upg, targets);
                    A.S.UIQuery.RebuildEntityActions.Ask();
                });
            }
            qc.Show();
        }

        private static void OrderUninstallAll(Upgrade upg, List<UpgradesComp> targets) {
            foreach (var comp in targets) {
                if (!comp.Entity.IsActive) { continue; }
                if (comp.Entity.IsImmutable) { continue; }
                if (Arrays.IsEmpty(comp.Upgrades)) {
                    continue;
                }
                for (int i = 0; i < comp.Upgrades.Length; i++) {
                    if (comp.Upgrades[i] == upg.UpgradeId) {
                        comp.EjectSlot(i, upg);
                        break;
                    }
                }
            }
        }

        private static bool HasInstalled(Tile tile) {
            if (!tile.IsActive) { return false; }
            if (tile.IsImmutable) { return false; }
            if (!tile.TryGetComponent<UpgradesComp>(out var comp)) {
                return false;
            }
            if (Arrays.IsEmpty(comp.Upgrades)) {
                return false;
            }
            for (int i = 0; i < comp.Upgrades.Length; i++) {
                if (comp.Upgrades[i] != null) {
                    return true;
                }
            }
            return false;
        }
    }
}
