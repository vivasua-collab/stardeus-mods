using System.Collections.Generic;
using Game.Components;
using Game.Components.Upgrades;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class InstallableUpgradeTab : BaseEntityTab {
        public const string TabId = "InstallableUpgrade";
        private const string SubId = "InstallableUpgrade";

        [SelfInit]
        private static void Register() {
            RegisterTab(new InstallableUpgradeTab());
        }

        private ObjData obj;
        private Upgrade upgrade;
        private GameState S;
        private WBase contentBtn;

        public override string Id => TabId;
        public override int Order => 20;
        public override string TabName => T.Installable;

        public override bool ShowFor(GameState s, ObjData obj) {
            if (obj.Obj == null) { return false; }
            if (!obj.Obj.TryGetComponent<InstallableUpgradeComp>(out _)) { return false; }
            var matId = obj.Obj.Definition.MatType?.Id;
            if (matId == null) { return false; }
            var up = UpgradeRegistry.GetByMat(matId);
            if (up == null) { return false; }
            this.obj = obj;
            this.upgrade = up;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var upgradeable = S.Sys.Upg.UpgradableWith(upgrade);
            var count = upgradeable?.Count ?? 0;

            var btn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            contentBtn = btn;
            btn.Load(IconId.CUpgrade, T.CompatibleObjects, Units.Num(count), () => {
                ShowCompatibleDevicesPanel(btn);
            });

            if (SubPanelWidget.IsOpenFor == SubId && contentBtn != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(group.Container);
                ShowCompatibleDevicesPanel(contentBtn);
                var savedBtn = contentBtn;
                Coroutines.OnNextFrame(() => {
                    var sp = S.UIQuery.GetEntitySubPanel.Ask();
                    sp.RepositionWidget(savedBtn, connectorOnly: false);
                });
            }

            return true;
        }

        private void ShowCompatibleDevicesPanel(WBase forWidget) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.Select);

            var block = UISpawn.Block(l, subPanel.Container, 0);
            int idx = 0;

            var header = UISpawn.Widget<WText>(l,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.CompatibleObjects);

            var upgradeable = S.Sys.Upg.UpgradableWith(upgrade);
            var count = upgradeable?.Count ?? 0;

            if (count == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            } else {
                // Group by definition so devices of the same type share one expandable row
                var groups = new Dictionary<string, List<UpgradesComp>>();
                var groupOrder = new List<string>();
                foreach (var up in upgradeable) {
                    var defId = up.Entity.Definition.Id;
                    if (!groups.TryGetValue(defId, out var list)) {
                        list = new List<UpgradesComp>();
                        groups[defId] = list;
                        groupOrder.Add(defId);
                    }
                    list.Add(up);
                }

                foreach (var defId in groupOrder) {
                    var items = groups[defId];
                    var def = items[0].Entity.Definition;
                    var capturedItems = items;

                    var row = UISpawn.Widget<WContentListButton>(l,
                        UIPrefabId.WContent2ListButton, block.Container, idx++);
                    row.Load(def.Preview, def.NameT, Units.Num(items.Count));
                    row.LoadOnToggle(l, (tLife, container, willShow, wasLoaded) => {
                        return LoadGroupItems(tLife, container, willShow, wasLoaded,
                            capturedItems, subPanel);
                    });
                }
            }

            subPanel.Show(forWidget, SubId);
        }

        private bool LoadGroupItems(Lifetime life, RectTransform container,
                bool willShow, bool wasLoaded,
                List<UpgradesComp> items, SubPanelWidget subPanel) {
            if (!willShow) { return wasLoaded; }
            if (wasLoaded) { return true; }

            int idx = 0;
            foreach (var up in items) {
                var ent = up.Entity;
                var capturedUp = up;
                var w = UISpawn.Widget<WContentButton2Text2>(life,
                    UIPrefabId.WContent2Button2Text2, container, idx++);
                w.Item.Load(ent.Definition.Preview, ent.Definition.NameT,
                    Units.XOutOfY(up.UsedSlots, up.NumSlots), () => {
                        EntityUtils.CameraFocusOn(ent);
                    });
                w.Button.Load(T.Install, () => {
                    EntityUtils.HideEntityLink();
                    if (!capturedUp.OrderUpgrade(upgrade)) {
                        UISounds.PlayActionDenied();
                        ReloadTab();
                    } else {
                        subPanel.Close();
                        // SelectEntity(null) would only clear the selection lists and
                        // leave the panel offering this module again
                        S.Query.DeselectAny.Ask();
                    }
                });
            }
            return true;
        }

        protected override void Cleanup() {
            obj = null;
            upgrade = null;
            S = null;
            contentBtn = null;
        }
    }
}
