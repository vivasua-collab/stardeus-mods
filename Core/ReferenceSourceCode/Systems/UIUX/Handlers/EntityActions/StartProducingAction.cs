using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Crafting;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class StartProducingAction : BaseEntityAction {
        public const string ActionId = "StartProducing";

        [SelfInit]
        private static void Register() {
            RegisterAction(new StartProducingAction());
        }

        public override string Id => ActionId;
        public override string HotkeyLabel => T.VerbConfigure;

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!CountStartable(s, tiles, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, s, new List<Tile>(tiles), cnt),
                Priority.Normal);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i,
                GameState s, Tile tile) {
            var crafter = tile.GetComponent<MultiCrafterComp>();
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CCog, crafter.Settings.CraftActionNameT,
                () => ShowCraftableChoice(s, new List<Tile> { tile }));
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                GameState s, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CCog, T.VerbConfigure,
                () => ShowCraftableChoice(s, tiles));
            return widget;
        }

        private static void ShowCraftableChoice(GameState s, List<Tile> tiles) {
            var crafters = new List<MultiCrafterComp>();
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanStart(s, p, out var crafter)) {
                    crafters.Add(crafter);
                }
            }
            if (crafters.Count == 0) {
                return;
            }

            var firstCrafter = crafters[0];
            var items = firstCrafter.UI.GetCraftableItems();
            if (items == null || items.Count == 0) {
                return;
            }

            var qc = QuickChoice.Create(firstCrafter.Settings.CraftActionNameT);
            foreach (var craftable in items) {
                var item = craftable.ProductDef;
                if (!s.Research.IsDefUnlocked(item)) {
                    continue;
                }
                var c = craftable;
                qc.AddButton(item.NameT, () => {
                    StartAll(crafters, c);
                    A.S.UIQuery.RebuildEntityActions.Ask();
                    var tab = Registries.EntityTabs.Get(ProductionTab.TabId, false);
                    if (tab != null) {
                        A.S.UIQuery.ReloadEntityTab.Ask(tab);
                    }
                });
            }
            qc.Show();
        }

        private static void StartAll(List<MultiCrafterComp> crafters, Craftable craftable) {
            foreach (var crafter in crafters) {
                if (crafter.Entity.IsActive) {
                    var order = crafter.StartProducing(craftable);
                    crafter.UI.SetAutoCraftingAmountFor(order.Demand);
                }
            }
        }

        private static bool CountStartable(GameState s, List<Tile> tiles, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanStart(s, p, out _)) {
                    c++;
                }
            }
            cnt = c;
            return cnt > 0;
        }

        private static bool CanStart(GameState s, Tile tile, out MultiCrafterComp crafter) {
            crafter = null;
            if (!tile.IsActive) {
                return false;
            }
            if (!tile.TryGetComponent(out crafter)) {
                return false;
            }
            return Arrays.IsEmpty(crafter.Orders);
        }
    }
}
