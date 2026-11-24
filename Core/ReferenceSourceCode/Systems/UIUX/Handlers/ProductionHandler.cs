using Game.Data;
using Game.Systems.Crafting;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class ProductionHandler : BaseWidgetHandler<ProductionWidget> {
        private CraftingOrder pendingOrderToShow;

        public ProductionHandler(GameState s, ProductionWidget widget) : base(s, widget) {
            widget.Initialize(s);
            s.UIQuery.ToggleProduction.SetResponder(ToggleProduction);
            s.UIQuery.OpenCraftingOrderConfig.SetResponder(OpenCraftingOrderConfig);
            s.UIQuery.GetPendingCraftingOrderToShow.SetResponder(GetPendingCraftingOrderToShow);
            s.UISig.CraftingOrderSelected.AddListener(OnCraftingOrderSelected);
        }

        private bool ToggleProduction(bool show) {
            widget.Toggle(show);
            return true;
        }

        private bool OpenCraftingOrderConfig(CraftingOrder order) {
            if (order?.Crafter?.Entity == null || !order.Crafter.Entity.IsActive) {
                return false;
            }
            pendingOrderToShow = order;
            S.Sig.SelectEntity.Send(order.Crafter.Entity);
            S.Clock.ExecuteAfterFramesForUI(1, () => {
                if (pendingOrderToShow != order) { return; }
                S.UIQuery.ShowSelectedEntityTab.Ask(ProductionTab.TabId);
                if (pendingOrderToShow == order) {
                    pendingOrderToShow = null;
                }
            });
            return true;
        }

        private CraftingOrder GetPendingCraftingOrderToShow() {
            var p = pendingOrderToShow;
            pendingOrderToShow = null;
            return p;
        }

        private void OnCraftingOrderSelected(CraftingOrder order) {
            if (!UIShowing.Production || order?.Demand?.Craftable == null) { return; }
            widget.ScrollToCraftable(order.Demand.Craftable);
        }
    }
}
