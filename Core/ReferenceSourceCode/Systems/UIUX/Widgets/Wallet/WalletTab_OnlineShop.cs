using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Systems.Trade;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class WalletTab_OnlineShop {
        private readonly WalletWidget parent;
        private readonly GameState S;
        private Lifetime lifetime;

        public WalletTab_OnlineShop(WalletWidget parent, GameState s) {
            this.parent = parent;
            S = s;
        }

        public void Load(Lifetime parentLifetime) {
            lifetime?.Dispose();
            lifetime = parentLifetime.CreateChild("Wallet.OnlineShop");

            if (!S.Research.IsUnlocked(DefIdsH.ResearchEconomyOnlineShop)) {
                var block = UISpawn.Block(lifetime, parent.WItems, 0);
                var lockedW = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, 0);
                lockedW.Load(IconId.CWarning, T.ResearchRequiredGeneric_V1.F(T.ResearchEconomyOnlineshop));
                return;
            }

            int blockIdx = 0;

            // Pending orders
            var pendingBlock = UISpawn.Block(lifetime, parent.WItems, blockIdx++);
            var idx = 0;

            var pendingHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, pendingBlock.Container, idx++);
            pendingHeader.Load(T.ShopOrdersPendingDelivery);

            var pending = S.Query.GetOnlineOrders.Ask(false);
            if (!Arrays.IsEmpty(pending)) {
                foreach (var order in pending.OrderByDescending(o => o.OrderTick)) {
                    AddOrderWidget(pendingBlock.Container, ref idx, order);
                }
            } else {
                var emptyW = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, pendingBlock.Container, idx++);
                emptyW.Load(IconId.CInfo, T.Empty);
            }

            // Completed orders
            var completed = S.Query.GetOnlineOrders.Ask(true);
            if (!Arrays.IsEmpty(completed)) {
                var completedOrdered = completed.OrderByDescending(o => o.OrderTick);
                bool addedDelivered = false;
                int deliveredIdx = 0;
                int failedIdx = 0;
                WContentBlock deliveredBlock = null;
                WContentBlock failedBlock = null;

                foreach (var order in completedOrdered) {
                    if (order.CapsuleId > 0) {
                        if (!addedDelivered) {
                            deliveredBlock = UISpawn.Block(lifetime, parent.WItems, blockIdx++);
                            var deliveredHeader = UISpawn.Widget<WText>(lifetime,
                                UIPrefabId.WContent2Header, deliveredBlock.Container, deliveredIdx++);
                            deliveredHeader.Load(T.ShopOrdersDelivered);
                            addedDelivered = true;
                        }
                        AddOrderWidget(deliveredBlock.Container, ref deliveredIdx, order);
                    }
                }

                bool addedFailed = false;
                foreach (var order in completedOrdered) {
                    if (order.CapsuleId < 1) {
                        if (!addedFailed) {
                            failedBlock = UISpawn.Block(lifetime, parent.WItems, blockIdx++);
                            var failedHeader = UISpawn.Widget<WText>(lifetime,
                                UIPrefabId.WContent2Header, failedBlock.Container, failedIdx++);
                            failedHeader.Load(T.ShopOrdersDeliveryFailed);
                            addedFailed = true;
                        }
                        AddOrderWidget(failedBlock.Container, ref failedIdx, order);
                    }
                }
            }

            S.Sig.ShopOrderDelivered.AddListener(OnShopOrderDelivered);
            S.Sig.ShopOrderCreated.AddListener(OnShopOrderCreated);
            lifetime.OnDispose(() => {
                S.Sig.ShopOrderDelivered.RemoveListener(OnShopOrderDelivered);
                S.Sig.ShopOrderCreated.RemoveListener(OnShopOrderCreated);
            });
        }

        private void AddOrderWidget(UnityEngine.RectTransform container, ref int idx,
                OnlineOrder order) {
            order.UpdateDeliveryText(S);
            var orderW = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, container, idx++);
            orderW.Load(order.Icon, order.Title, null);
            order.DeliveryText.Bind(lifetime, text => {
                orderW.SetText2(text);
            });
            UIUtils.AddTipFunc(lifetime, orderW, order.Tooltip);
        }

        private void CreateNewOrder() {
            if (A.IsInHyperspace) {
                S.Sig.ShowErrorMessage.Send(T.ExitHyperspaceFirst);
                return;
            }
            S.Query.CreateOnlineTrade.Ask();
        }

        private void OnShopOrderDelivered(OnlineOrder order) {
            if (UIShowing.Wallet) {
                S.UIQuery.RebuildWallet.Ask();
            }
        }

        private void OnShopOrderCreated(OnlineOrder order) {
            if (UIShowing.Wallet) {
                S.UIQuery.RebuildWallet.Ask();
            }
        }

        public bool LoadFooter(WGroup footer) {
            if (!S.Research.IsUnlocked(DefIdsH.ResearchEconomyOnlineShop)) {
                return false;
            }
            var createBtn = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WPanelButton, footer.Container, 0);
            createBtn.Load(T.CreateShopOrder, CreateNewOrder);
            return true;
        }

        public void Cleanup() {
            lifetime?.Dispose();
            lifetime = null;
        }
    }
}
