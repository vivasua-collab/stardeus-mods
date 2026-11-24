using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Game.Components;
using Game.Components.Upgrades;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Drama;
using Game.Systems.Mental;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class UpgradesTab : BaseEntityTab {
        public const string TabId = "Upgrades";
        [SelfInit]
        private static void Register() {
            RegisterTab(new UpgradesTab());
        }

        private Tile tile;
        private UpgradesComp upgrades;
        private GameState S;
        private bool compatibleExpanded;
        public override string TabName => T.Upgrades;
        public override int Order => 50;
        private Upgrade[] allUpgrades;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.IsImmutable) { return false; }
            bool hasAny = false;
            var ups = AllUpgrades();
            for (int i = ups.Length - 1; i >= 0; i--) {
                var up = ups[i];
                if (up != null && up.IsCompatibleWith(tile)) {
                    hasAny = true;
                    break;
                }
            }
            if (!hasAny) { return false; }
            if (tile.Upgrades != null) {
                upgrades = tile.Upgrades;
            } else {
                return false;
            }
            S = s;
            this.tile = tile;
            return true;
        }

        protected override void OnSpawnTab(WTabButton widget, Lifetime parentLifetime) {
            upgrades.UpgradesChanged.Bind(parentLifetime, () => {
                var upCount = Units.XOutOfY(upgrades.UsedSlots, upgrades.NumSlots);
                widget.SetText($"{TabName} {upCount}");
            });
        }

        public override bool LoadContent(WGroup group) {
            LoadUpgrades(group);
            return true;
        }

        private Upgrade[] AllUpgrades() {
            return allUpgrades ??= UpgradeRegistry.All.Values.Where(u => u != null)
                .OrderBy(u => u.Title).ToArray();
        }

        private int NumCompatibleUpgrades() {
            int count = 0;
            var array = AllUpgrades();
            for (int i = array.Length - 1; i >= 0; i--) {
                Upgrade up = array[i];
                if (up.IsCompatibleWith(tile)) {
                    count++;
                }
            }
            return count;
        }

        private void LoadUpgrades(WGroup group) {
            int gidx = 0;
            var block = UISpawn.Block(lifetime, group, gidx++);
            int idx = 0;

            upgrades.UpgradesChanged.Bind(lifetime, () => {
                ReloadTab();
            }, false);

            var hasUpgrades = upgrades.HasUpgrades();

            if (hasUpgrades) {
                for (int i = 0; i < upgrades.NumSlots; i++) {
                    var upId = upgrades.Upgrades.SafeGet(i);
                    if (upId == null) { continue; }
                    var upgrade = UpgradeRegistry.Get(upId);
                    if (upgrade == null) {
                        D.Err("Unknown upgrade: {0} in {1}. Removing", upId, tile);
                        upgrades.Upgrades[i] = null;
                        continue;
                    }
                    var mat = MatType.Get(upgrade.MatTypeId);
                    if (mat == null) {
                        D.Err("Unknown material type: {0} for upgrade {1} in {2}. Removing",
                            upgrade.MatTypeId, upId, tile);
                        upgrades.Upgrades[i] = null;
                        continue;
                    }
                    var w = UISpawn.Widget<WButtonIconText2>(lifetime, UIPrefabId.WUpgradeItem, block.Container, idx++);
                    var slot = i;
                    w.Load(upgrade.Icon, upgrade.Title, mat.Def.DescT, () => {
                        if (upgrade.CanRemove(out var reason)) {
                            upgrades.EjectSlot(slot, upgrade);
                            ReloadTab();
                        } else {
                            S.Sig.ShowErrorMessage.Send(reason);
                        }
                    });
                    UIUtils.AddCodexTooltip(lifetime, w.gameObject, w, mat.Def);
                }
            }


            if (upgrades.PendingUpgrades != null) {
                for (int pi = 0; pi < upgrades.PendingUpgrades.Length; pi++) {
                    var pendingId = upgrades.PendingUpgrades[pi];
                    if (pendingId == null) { continue; }
                    var up = UpgradeRegistry.Get(pendingId);
                    if (up == null) { continue; }
                    var pendingSlot = pi;
                    var w = UISpawn.Widget<WButtonIconText2>(lifetime,
                        UIPrefabId.WUpgradeItem, block.Container, idx++);
                    w.Load(up.Icon, Texts.Grey(up.Title),
                        Texts.Italic(T.UpgradeInProgres), () => {
                        upgrades.CancelPendingUpgrade(pendingSlot);
                        ReloadTab();
                    });
                    var mat = MatType.Get(up.MatTypeId);
                    if (mat?.Def != null) {
                        UIUtils.AddCodexTooltip(lifetime, w.gameObject, w, mat.Def);
                    }
                }
            }
            if (upgrades.CanOrderUpgrade) {
                var w = UISpawn.Widget<WButtonIconText>(lifetime,
                    UIPrefabId.WUpgradeInstallButton, block.Container, idx++);
                w.Load(IconId.CUpgrade, T.InstallUpgrade, () => {
                    ShowInstallUpgradePanel(w);
                });
                if (Arrays.IsEmpty(S.Sys.Upg.AvailableFor(tile))) {
                    w.SetInteractable(false);
                    UIUtils.AddTip(lifetime, w, T.NoCompatibleUpgrades);
                }
            }

            AddCompatibleUpgradesList(group, gidx++);
        }

        private void AddCompatibleUpgradesList(WGroup group, int gidx) {
            var block = UISpawn.Block(lifetime, group, gidx);
            var idx = 0;

            var header = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContentHeader, block.Container, idx++);
            header.Load(T.CompatibleUpgrades);

            var listToggle = UISpawn.Widget<WContentListButton>(lifetime,
                UIPrefabId.WContentListButton, block.Container, idx++);
            listToggle.Load(IconId.CInfo, T.CompatibleUpgradeTypes,
                Units.XNum(NumCompatibleUpgrades()));
            listToggle.LoadOnToggle(lifetime, ToggleCompatibleUpgrades, compatibleExpanded);
        }

        private bool ToggleCompatibleUpgrades(Lifetime life, RectTransform container,
                bool willShow, bool wasLoaded) {
            compatibleExpanded = willShow;
            if (!willShow) {
                return wasLoaded;
            }

            if (wasLoaded) { return true; }

            int idx = 0;
            var all = AllUpgrades();
            for (int i = 0; i < all.Length; i++) {
                Upgrade up = all[i];
                if (!up.IsCompatibleWith(tile)) {
                    continue;
                }
                var mat = MatType.Get(up.MatTypeId);
                var isLocked = mat?.Def != null && !S.Research.IsDefUnlocked(mat.Def);
                var text2 = isLocked
                    ? Texts.Red(T.ResearchLocked)
                    : (mat?.Def != null ? Units.XNum(S.Sys.Inventory.CountOf(mat.Def)) : T.Empty);

                var row = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, container, idx++);
                row.Load(up.Icon, up.Title, text2);
                if (mat?.Def != null) {
                    UIUtils.AddCodexTooltip(life, row.gameObject, row, mat.Def);
                }
            }
            return true;
        }

        private void ShowInstallUpgradePanel(WBase w) {
            // The button was enabled when the tab was built; the last upgrade may have been
            // used up or its storage torn down since. AvailableFor returns null for none.
            var availableUpgrades = S.Sys.Upg.AvailableFor(tile);
            if (Arrays.IsEmpty(availableUpgrades)) {
                S.Sig.ShowErrorMessage.Send(T.NoCompatibleUpgrades);
                ReloadTab();
                return;
            }
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.SelectUpgrade);
            var block = UISpawn.Block(l, subPanel.Container, 0);
            int idx = 0;
            var header = UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header,
                block.Container, idx++);
            header.Load(T.AvailableUpgrades);
            foreach (var up in availableUpgrades) {
                var mat = MatType.Get(up.MatTypeId);
                if (mat == null) { continue; }
                var wUpgrade = UISpawn.Widget<WButtonIconText2>(l,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                var owned = S.Sys.Inventory.CountOf(mat.Def);
                wUpgrade.Load(up.Icon, up.Title, Units.XNum(owned), () => {
                    var slot = upgrades.FirstEmptySlot;
                    if (slot >= 0 && upgrades.OrderUpgrade(slot, up)) {
                        if (!upgrades.CanOrderUpgrade) {
                            subPanel.Close();
                        }
                        ReloadTab();
                    }
                });
                UIUtils.AddCodexTooltip(lifetime, wUpgrade.gameObject, wUpgrade, mat.Def);
            }
            subPanel.Show(w);
        }

        protected override void Cleanup() {
            tile = null;
            upgrades = null;
            S = null;
        }

        public override string Id => TabId;
    }
}
