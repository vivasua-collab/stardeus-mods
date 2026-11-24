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
    public sealed class InstallUpgradeAction : BaseEntityAction {
        public const string ActionId = "InstallUpgrade";

        [SelfInit]
        private static void Register() {
            RegisterAction(new InstallUpgradeAction());
        }

        public override string Id => ActionId;

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            int cnt = 0;
            foreach (var t in tiles) {
                if (CanInstall(s, t)) {
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
            widget.LoadMulti(cnt, IconId.CUpgrade, T.InstallUpgrade,
                () => ShowInstallChoice(s, tiles));
            return widget;
        }

        private static void ShowInstallChoice(GameState s, List<Tile> tiles) {
            var allUpgrades = s.Sys.Upg.AvailableFor(null);
            if (Arrays.IsEmpty(allUpgrades)) {
                return;
            }

            var upgradeTargets = new Dictionary<Upgrade, List<UpgradesComp>>();
            foreach (var tile in tiles) {
                if (!tile.IsActive) { continue; }
                if (tile.IsImmutable) { continue; }
                if (!tile.TryGetComponent<UpgradesComp>(out var comp)) {
                    continue;
                }
                if (!comp.CanOrderUpgrade) {
                    continue;
                }
                foreach (var upg in allUpgrades) {
                    if (upg.IsCompatibleWith(tile)) {
                        if (!upgradeTargets.TryGetValue(upg, out var list)) {
                            list = new List<UpgradesComp>();
                            upgradeTargets[upg] = list;
                        }
                        list.Add(comp);
                    }
                }
            }

            if (upgradeTargets.Count == 0) {
                return;
            }

            var qc = QuickChoice.Create(T.InstallUpgrade);
            foreach (var kv in upgradeTargets) {
                var upg = kv.Key;
                var targets = kv.Value;
                qc.AddButton($"{upg.Title} ({Units.XNum(targets.Count)})", () => {
                    OrderInstallAll(upg, targets);
                    A.S.UIQuery.RebuildEntityActions.Ask();
                });
            }
            qc.Show();
        }

        private static void OrderInstallAll(Upgrade upg, List<UpgradesComp> targets) {
            foreach (var comp in targets) {
                if (!comp.Entity.IsActive) { continue; }
                if (comp.Entity.IsImmutable) { continue; }
                if (!comp.CanOrderUpgrade) {
                    continue;
                }
                var slot = comp.FirstEmptySlot;
                if (slot < 0) {
                    continue;
                }
                comp.OrderUpgrade(slot, upg);
            }
        }

        private static bool CanInstall(GameState s, Tile tile) {
            if (!tile.IsActive) { return false; }
            if (tile.IsImmutable) { return false; }
            if (!tile.TryGetComponent<UpgradesComp>(out var comp)) {
                return false;
            }
            if (!comp.CanOrderUpgrade) {
                return false;
            }
            var allUpgrades = s.Sys.Upg.AvailableFor(null);
            if (Arrays.IsEmpty(allUpgrades)) {
                return false;
            }
            foreach (var upg in allUpgrades) {
                if (upg.IsCompatibleWith(tile)) {
                    return true;
                }
            }
            return false;
        }
    }
}
