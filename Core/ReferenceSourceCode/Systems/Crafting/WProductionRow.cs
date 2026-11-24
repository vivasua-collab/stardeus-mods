using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Story;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Crafting {
    public class WProductionRow : WBase, ICustomPoolable {
        [SerializeField] private WGroup wContainerMats;
        /// <summary>
        /// Right side of the widget, where devices go
        /// </summary>
        [SerializeField] private WGroup wContainer;
        [SerializeField] private GameObject arrowJoiner;

        private Lifetime life;

        public void Load(Lifetime lifetime, GameState s, Craftable cr, List<CraftingOrder> orders,
                bool expanded, Action<bool> onExpandedChanged) {
            Cleanup();
            life = lifetime.CreateChild("WProdRow");

            var ingrs = cr.Ingredients;
            var len = ingrs.Length;
            arrowJoiner.SetActiveFast(len > 1);
            for (int i = 0; i < len; i++) {
                Mat mat = ingrs[i];
                var src = UISpawn.Widget<WProductionSource>(life, UIPrefabId.WProductionSource, wContainerMats.Container, i);
                src.Load(life, A.S, mat, len > 1);
            }

            var listBtn = UISpawn.Widget<WContentListButton>(life, UIPrefabId.WProductionCraftingGroup, wContainer.Container, 0);
            var def = cr.ProductDef;
            BInt ownedB;
            if (def.MatType != null) {
                ownedB = s.UIQuery.GetBindableMatCountFor.Ask(def.MatType);
            } else if (def.IsBeing) {
                ownedB = s.UIQuery.GetBindableBeingCountFor.Ask(def);
            } else {
                ownedB = s.UIQuery.GetBindableObjCountFor.Ask(def);
            }
            var prodB = s.UIQuery.GetBindableProductionRate.Ask(def);
            var consB = s.UIQuery.GetBindableConsumptionRate.Ask(def);
            var textB = new B3<int, int, int, string>(life, ownedB, prodB, consB,
                (owned, prod, cons) => BuildSubtitle(owned, prod, cons));
            listBtn.Load(def.Preview, def.NameT, textB.Value);
            textB.Bind(life, t => listBtn.SetText2(t));
            UIUtils.AddCodexTooltip(lifetime, listBtn.Button.Text.gameObject, listBtn.Button, def, false, Tooltip.DelaySeconds, false);
            UIUtils.AddCodexTooltip(lifetime, listBtn.Button.Text2.gameObject, listBtn.Button, def, false, Tooltip.DelaySeconds, true);
            listBtn.LoadOnToggle(life, (toggleLife, container, willShow, wasLoaded) => {
                onExpandedChanged(willShow);
                if (!willShow) { return wasLoaded; }
                if (wasLoaded) { return true; }
                int devIdx = 0;
                foreach (var order in orders) {
                    var crafter = order.Crafter;
                    var capturedOrder = order;
                    var row = UISpawn.Widget<WProductionCraftingOrder>(toggleLife,
                        UIPrefabId.WProductionCraftingOrder, container, devIdx++);
                    row.Item.Load(crafter.Entity.Definition.Preview, crafter.Entity.Definition.NameT, null,
                        () => EntityUtils.SelectOrFocus(capturedOrder.Crafter.Tile));
                    row.Amt.Load(capturedOrder.Demand.AmountWanted,
                        a => { capturedOrder.Demand.AmountWanted = a; capturedOrder.Ping(); },
                        w => w < 0 ? 0 : w);
                    AddAmtModTip(toggleLife, row.Amt);
                    row.IconBtn.Load(capturedOrder.Demand.TypeIcon,
                        () => ShowOrderTypeChoice(capturedOrder, row));
                    capturedOrder.Changed.Bind(toggleLife,
                        o => {
                            row.Amt.SetActiveFast(o.Demand.NeedsAmount);
                            row.Item.SetText2(o.StateText(includeBatchSize: true));
                            if (o.Demand.NeedsAmount) {
                                row.Amt.SetValue(o.Demand.AmountWanted);
                            }
                            row.IconBtn.SetImage(o.Demand.TypeIcon);
                        });
                    UIUtils.AddTipFunc(life, row.IconBtn, () => {
                        return capturedOrder.Demand.TypeName;
                    });
                }
                return true;
            }, expanded);
        }

        private static string BuildSubtitle(int owned, int prod, int cons) {
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.Append(T.OwnedCount).Append(": ").Append(Units.NumColoredAbbr(owned));
            bool needsNewLine = true;
            if (cons > 0) {
                needsNewLine = false;
                sb.AppendLine();
                sb.Append("  -").Append(Units.Num(cons)).Append('/').Append(T.Day);
            }
            if (cons > 0 || prod > 0) {
                if (needsNewLine) {
                    sb.AppendLine();
                }
                var nt = Units.Num(prod);
                if (prod < cons) {
                    nt = Texts.Red(nt);
                }
                sb.Append("  +").Append(nt).Append('/').Append(T.Day);
            }
            return sb.ToString();
        }

        private void ShowOrderTypeChoice(CraftingOrder o, WProductionCraftingOrder row) {
            var qc = QuickChoice.Create(o.Demand.CraftableItemNameSimple);
            qc.AddIconButton(
                CraftingDemand.IconFor(CraftingDemandType.One),
                T.DemandTypeOne,
                () => {
                    o.ChangeDemandType(CraftingDemandType.One);
                }, T.DemandTypeOneTip);
            qc.AddIconButton(
                CraftingDemand.IconFor(CraftingDemandType.Custom),
                T.DemandTypeCustom,
                () => {
                    o.ChangeDemandType(CraftingDemandType.Custom);
                }, T.DemandTypeCustomTip);
            qc.AddIconButton(
                CraftingDemand.IconFor(CraftingDemandType.Limit),
                T.DemandTypeLimit,
                () => {
                    o.ChangeDemandType(CraftingDemandType.Limit);
                }, T.DemandTypeLimitTip);
            qc.AddIconButton(
                CraftingDemand.IconFor(CraftingDemandType.Unlimited),
                T.DemandTypeUnlimited,
                () => {
                    o.ChangeDemandType(CraftingDemandType.Unlimited);
                }, T.DemandTypeUnlimitedTip);
            qc.AddIconButton(
                IconId.CCog,
                T.VerbConfigure,
                () => {
                    A.S.UIQuery.OpenCraftingOrderConfig.Ask(o);
                });
            qc.AddIconButton(
                IconId.CStop,
                T.StopProducing,
                () => {
                    var cr = o.Crafter;
                    A.S.Query.DeselectIfSelected.Ask(cr.Tile);
                    cr.UI.StopProducing(o);
                });
            qc.Show();
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
            Cleanup();
        }

        private static void AddAmtModTip(Lifetime l, WInputAmt amt) {
            var key = The.Bindings.GetBinding(ActionType.ShiftModifier).AllControlGlyphs(out var _);
            var ctrlKey = The.Bindings.GetBinding(ActionType.CtrlModifier).AllControlGlyphs(out var _);
            string tip = null;
            if (key != null) {
                tip = T.AmtModifierKeyTip_V2.F(key, Texts.Bold(Units.Num(10)));
            }
            if (ctrlKey != null) {
                var tip2 = T.AmtModifierKeyTip_V2.F(ctrlKey, Texts.Bold(Units.Num(50)));
                tip = tip != null ? $"{tip}\n{tip2}" : tip2;
            }
            UIUtils.AddTip(l, amt, tip);
        }

        private void Cleanup() {
            if (life != null) {
                life.Dispose();
                life = null;
            }
        }
    }
}
