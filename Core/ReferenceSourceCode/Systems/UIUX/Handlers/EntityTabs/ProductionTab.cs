using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Crafting;
using Game.UI;
using Game.Utils;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class ProductionTab : BaseEntityTab {
        public const string TabId = "Production";
        [SelfInit]
        private static void Register() {
            RegisterTab(new ProductionTab());
        }

        private MultiCrafterComp crafter;
        private GameState S;
        private bool ingredientsExpanded = true;
        private CraftingOrder pendingConfigOrder;
        public override string TabName => T.MenuProduction;

        public override int Order => 10;

        public override bool ShowFor(GameState s, Tile tile) {
            if (tile.TryGetComponent(out crafter)) {
                S = s;
                return true;
            }
            return false;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;
            var w = UISpawn.Widget<WButtonIconText>(lifetime, UIPrefabId.WContent3IconButtonSimple,
                block.Container, idx++);
            w.Load(IconId.WPlusThin, T.CraftOrderNew, () => OnCreateOrder(w));
            w.SetInteractable(!BlockActions);
            w.SetTint(The.UITheme.GetColor("White", this));

            var orders = crafter.Orders;
            if (Arrays.IsEmpty(orders)) {
                return true;
            }

            var pending = S.UIQuery.GetPendingCraftingOrderToShow.Ask() ?? pendingConfigOrder;
            pendingConfigOrder = null;
            WContent3Button pendingOw = null;
            CraftingOrder pendingOrder = null;

            CraftingOrderReorder reorder = null;
            bool needsReorder = !Arrays.IsEmpty(orders);
            if (needsReorder && !BlockActions) {
                reorder = new CraftingOrderReorder(crafter, block.Container);
            }

            foreach (var order in orders) {
                var ow = UISpawn.Widget<WContent3Button>(lifetime,
                    UIPrefabId.WContent3Button_Sortable, block.Container, idx++);
                ow.Button.Load(order.Demand.Product.Preview, null, null,
                    () => {
                        S.UISig.CraftingOrderSelected.Send(order);
                        OnConfigureOrder(ow, order);
                    });
                ow.Button.SetInteractable(!BlockActions);

                order.Changed.Bind(lifetime, (o) => {
                    UpdateOrderButton(ow, o);
                });

                UIUtils.AddCodexTooltip(lifetime, ow.gameObject, ow, order.Demand.Product);

                if (reorder != null && ow.TryGetComponent<UIDragListener>(out var drag)) {
                    reorder.Register(ow, drag);
                }

                if (pending != null && order == pending && pending.Crafter == crafter) {
                    pendingOw = ow;
                    pendingOrder = order;
                }
            }

            if (pendingOw != null) {
                var capturedOw = pendingOw;
                var capturedOrder = pendingOrder;
                var capturedLife = lifetime;
                Coroutines.OnFrame(3, () => {
                    // The tab can close or reload within those frames: lifetime is then
                    // null (CreateLifetime threw) or new, and the row may be another's
                    if (capturedLife == null || capturedLife.IsDisposed
                            || capturedLife != lifetime) {
                        return;
                    }
                    OpenOrderSubPanel(capturedOw, capturedOrder);
                });
            }

            return true;
        }

        private void UpdateOrderButton(WContent3Button ow, CraftingOrder order) {
            string title = order.Demand.CraftableItemName;
            if (order == crafter.CurrentOrder) {
                title = $"{title} {Texts.Accented(GlyphMap.TriangleLeft)}";
            }
            ow.TextTop.SetText(title);
            string text = order.StateText();
            ow.Button.SetText2(text);
            var demand = order.Demand;

            if (demand.Type == CraftingDemandType.Limit) {
                ow.Button.SetText($"{demand.TypeName}: {Units.XOutOfY(demand.GlobalInventoryCount, demand.AmountWanted)}");
            } else if (demand.Type != CraftingDemandType.Unlimited && demand.Type != CraftingDemandType.One) {
                ow.Button.SetText($"{demand.TypeName}: {Units.XOutOfY(demand.AmountProduced, demand.AmountWanted)}");
            } else {
                ow.Button.SetText(demand.TypeName);
            }
        }


        public override bool LoadFooter(WGroup group) {
            var idx = 0;
            var prio = UISpawn.Widget<WPriorityUpDown>(lifetime, UIPrefabId.WContent4Priority, group.Container, idx++);
            prio.Up.Load(IconId.WArrowUp, () => crafter.UI.MovePriority(1));
            prio.Down.Load(IconId.WArrowDown, () => crafter.UI.MovePriority(-1));
            prio.Up.SetInteractable(!BlockActions);
            prio.Down.SetInteractable(!BlockActions);
            prio.Texts.Load(T.TaskPriority, null);
            UIUtils.AddTip(lifetime, prio, T.JobPriorityHint);
            crafter.Settings.TaskPriority.Bind(lifetime, (p) => {
                prio.Texts.SetText2(Units.XOutOfY(p, 5));
            });

            var toggle = UISpawn.Widget<WToggleText>(lifetime, UIPrefabId.WContent4Toggle, group.Container, idx++);
            if (Arrays.IsEmpty(crafter.Orders)) {
                toggle.Load(T.DeviceIgnoreUnconfigured, crafter.Settings.IsIgnoreUnconfiguredOn, (y) => {
                    crafter.Settings.IsIgnoreUnconfiguredOn = y;
                    if (y) {
                        crafter.UI.MarkConfigured();
                    }
                });
            } else {
                toggle.Load(T.DeviceWarnIfIdle, crafter.Settings.IsIdleWarnOn, (y) => {
                    crafter.Settings.IsIdleWarnOn = y;
                    if (!y) {
                        crafter.UI.HideIdleNotification();
                    }
                });
            }
            toggle.SetInteractable(!BlockActions);

            if (!Arrays.IsEmpty(crafter.Orders)) {
                var clear = UISpawn.Widget<WButtonIconText>(lifetime, UIPrefabId.WPanelButtonIcon, group.Container, idx++);
                clear.Load(IconId.CStop, T.ClearAll, () => {
                    crafter.UI.StopAll();
                    ReloadTab();
                });
                clear.SetInteractable(!BlockActions);
            }

            var prod = UISpawn.Widget<WButtonText>(lifetime, UIPrefabId.WPanelButton, group.Container, idx++);
            prod.Load(T.NounProductionOverview, () => {
                S.UIQuery.ToggleProduction.Ask(!UIShowing.Production);
            });
            return true;
        }

        private void OnConfigureOrder(WBase ow, CraftingOrder order) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            if (subPanel.IsShowingFor(ow)) {
                subPanel.Close();
                return;
            }
            OpenOrderSubPanel(ow, order);
        }

        private void OpenOrderSubPanel(WBase ow, CraftingOrder order) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            subPanel.SetTitle(T.CraftOrderSettings);
            var l = subPanel.CreateLifetime(lifetime);
            var blockIdx = 0;

            AddMainBlock(order, l, subPanel, blockIdx++);
            var target = AddConfigBlock(order, l, subPanel, blockIdx++);
            LoadOrderFooter(order, subPanel, l);

            order.Changed.Bind(l, o => {
                if (target.Dropdown.Value != (int) o.Demand.Type) {
                    target.Dropdown.SetValue((int) o.Demand.Type, notify: true);
                }
            }, notifyImmediately: false);

            subPanel.Show(ow);
            A.S.UIQuery.HideCodexTooltip.Ask();
        }

        private static bool IsOrderFulfilled(CraftingOrder order) {
            return order.State == CraftingOrderState.ProducedEnough
                && (order.Demand.Type == CraftingDemandType.One
                    || order.Demand.Type == CraftingDemandType.Custom);
        }

        private WDropdownWithIcon AddConfigBlock(CraftingOrder order, Lifetime l,
                SubPanelWidget subPanel, int blockIdx) {

            var pCont = subPanel.Container.Container;
            var block = UISpawn.Block(l, subPanel.Container, blockIdx);
            var container = block.Container;
            int idx = 0;

            var header = UISpawn.Widget<WText>(l,
                UIPrefabId.WContent2Header, container, idx++);
            header.SetText(T.CraftOrderSettings);

            var target = UISpawn.Widget<WDropdownWithIcon>(l,
                UIPrefabId.WContent2Dropdown, container, idx++);
            target.Icon.SetImage(IconId.CTarget);

            var amtBlock = UISpawn.Widget<WInputAmtIconText2>(l,
                UIPrefabId.WContent2Text2Amt, container, idx++);

            amtBlock.Item.Load(IconId.CCount, T.WantedAmount, null);
            amtBlock.Amt.Load(order.Demand.AmountWanted, (a) => {
                order.Demand.AmountWanted = a;
                if (order.State == CraftingOrderState.ProducedEnough) {
                    order.State = CraftingOrderState.InQueue;
                }
                order.Ping();
            }, (wantA) => {
                if (wantA < 0) {
                    wantA = 0;
                }
                return wantA;
            });
            order.Changed.Bind(l, o => amtBlock.Amt.SetValue(o.Demand.AmountWanted),
                notifyImmediately: false);

            AddInputModTip(l, amtBlock);

            var dt = order.Demand.Type;
            amtBlock.SetActiveFast(dt == CraftingDemandType.Limit
                || dt == CraftingDemandType.Custom);

            // we're loading target dropdown after amtBLock is available
            // so we can toggle it
            target.Dropdown.Load(MultiCrafterUI.DemandTypeOpts,
                (int)order.Demand.Type, (i) => {
                    var dt = (CraftingDemandType)i;
                    order.ChangeDemandType(dt);
                    amtBlock.SetActiveFast(
                        dt == CraftingDemandType.Limit || dt == CraftingDemandType.Custom);
                    block.RebuildLayout();
                    var parentGroup = block.GetComponentInParent<WGroup>();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parentGroup.Rect);
                    order.Ping();
                });


            var batch = UISpawn.Widget<WSliderWithIconText2>(l,
                UIPrefabId.WContent2Text2Slider, container, idx++);

            batch.Item.Load(IconId.CCount, T.CrafterBatchSize, Units.Num(order.Demand.BatchSize));
            batch.Slider.Load(order.Demand.BatchSize, (v) => {
                var intV = Mathf.RoundToInt(v);
                if (intV != order.Demand.BatchSize) {
                    order.UpdateBatchSize(intV);
                    order.Ping();
                }
            }, 1, crafter.Settings.MaxBatchSize);
            batch.Slider.SetValueTextFunc((v) => {
                var intV = Mathf.RoundToInt(v);
                return Units.Num(intV);
            });
            UIUtils.AddTip(l, batch, T.CrafterBatchSizeTip);

            BuildColorBlock(order, l, container, ref idx);

            var suppress = UISpawn.Widget<WToggleText>(l,
                UIPrefabId.WContent2Toggle, container, idx++);
            suppress.Load(T.CraftOrderSuppressDeficit, order.SuppressDeficitWarning, (on) => {
                order.SuppressDeficitWarning = on;
            });
            UIUtils.AddTip(l, suppress, T.CraftOrderSuppressDeficitTip);

            return target;
        }

        private static void AddInputModTip(Lifetime l, WInputAmtIconText2 amtBlock) {
            var key = The.Bindings.GetBinding(ActionType.ShiftModifier)
                .AllControlGlyphs(out var _);
            var ctrlKey = The.Bindings.GetBinding(ActionType.CtrlModifier)
                .AllControlGlyphs(out var _);
            string tip = null;
            if (key != null) {
                tip = T.AmtModifierKeyTip_V2.F(key, Texts.Bold(Units.Num(10)));
            }
            if (ctrlKey != null) {
                var tip2 = T.AmtModifierKeyTip_V2.F(ctrlKey, Texts.Bold(Units.Num(50)));
                if (tip != null) {
                    tip = $"{tip}\n{tip2}";
                } else {
                    tip = tip2;
                }
            }
            UIUtils.AddTip(l, amtBlock.Amt, tip);
        }

        private void AddMainBlock(CraftingOrder order, Lifetime l,
                SubPanelWidget subPanel, int blockIdx) {
            var block = UISpawn.Block(l, subPanel.Container, blockIdx);
            var container = block.Container;
            var dem = order.Demand;
            int idx = 0;

            // The header is the only state dependent widget here, so it gets its own child
            // lifetime and is swapped on its own. Rebuilding the whole subpanel instead
            // would destroy the Wanted Amount input while it is being typed into.
            var headerIdx = idx++;
            Lifetime headerLife = null;
            WProgressWithLabel progHeader = null;
            var showingDone = order.State != CraftingOrderState.ProducedEnough;
            order.Changed.Bind(l, (o) => {
                var done = o.State == CraftingOrderState.ProducedEnough;
                if (showingDone != done) {
                    showingDone = done;
                    var isSwap = headerLife != null;
                    headerLife?.Dispose();
                    headerLife = l.CreateChild("OrderHeader");
                    if (done) {
                        progHeader = null;
                        var doneHeader = UISpawn.Widget<WText>(headerLife,
                            UIPrefabId.WContentHeader, container, headerIdx);
                        doneHeader.Load(T.GoalStateCompleted);
                    } else {
                        progHeader = UISpawn.Widget<WProgressWithLabel>(headerLife,
                            UIPrefabId.WContentHeaderProgress, container, headerIdx);
                        progHeader.Label.Load(T.ProducingNow_V1.F(string.Empty));
                    }
                    if (isSwap) {
                        block.RebuildLayout();
                    }
                }
                if (progHeader == null) { return; }
                if (dem.Type == CraftingDemandType.Limit) {
                    var gc = dem.GlobalInventoryCount;
                    progHeader.Progress.SetPercent((float) gc / dem.AmountWanted, false);
                    progHeader.Progress.SetText(Units.XOutOfY(gc, dem.AmountWanted));
                } else if (dem.Type != CraftingDemandType.Unlimited
                        && dem.Type != CraftingDemandType.One) {
                    progHeader.Progress.SetPercent((float) dem.AmountProduced / dem.AmountWanted, false);
                    progHeader.Progress.SetText(Units.XOutOfY(dem.AmountProduced, dem.AmountWanted));
                } else {
                    progHeader.Progress.SetPercent(o.Progress);
                }
            });

            var w = UISpawn.Widget<WIconText2>(l,
                UIPrefabId.WContentText2, container, idx++);
            w.Load(dem.Product.Preview, null);

            order.Changed.Bind(l, (o) => {
                w.SetText(dem.CraftableItemName);
                var owned = S.Sys.Inventory.CountOfProduct(dem.Product);
                w.SetText2($"{T.OwnedCount}: {owned}");
                if (dem.ColorId != null) {
                    w.SetTint(Palette.ItemColor(dem.ColorId));
                } else {
                    w.SetTint(Consts.ColorWhite);
                }
            });

            UIUtils.AddCodexTooltip(l, w.gameObject, w, dem.Product);
            UIUtils.AddTipFunc(l, w, () => {
                var owned = S.Sys.Inventory.CountOfProduct(dem.Product);
                return $"{T.OwnedCount}: {owned}";
            });

            var w0 = UISpawn.Widget<WButtonIconText2>(l,
                UIPrefabId.WContentListButton, container, idx++);
            // I was an idiot to place "Ingredients:" into translations long ago...
            w0.Load(IconId.CCog, T.Ingredients.Replace(":", string.Empty));
            w0.SetText2(null);
            var listButton = w0.GetComponent<WContentListButton>();
            listButton.LoadOnToggle(l, (life, c, w, wl) => ToggleIngredients(order, l, c, w, wl),
                ingredientsExpanded);
        }

        private void LoadOrderFooter(CraftingOrder order,
                SubPanelWidget subPanel, Lifetime l) {
            var fIdx = 0;
            var stopButton = UISpawn.Widget<WButtonIconText>(l,
                UIPrefabId.WPanelButtonIcon, subPanel.Footer.Container, fIdx++);
            stopButton.Load(IconId.CStop, T.StopProducing, () => {
                crafter.UI.StopProducing(order);
                subPanel.Close();
                ReloadTab();
            });

            var dem = order.Demand;
            // Repeat and Pause share a prefab, so the button is re-loaded in place when the
            // order becomes fulfilled instead of the subpanel being torn down and rebuilt.
            var stateButton = UISpawn.Widget<WButtonIconText>(l,
                UIPrefabId.WPanelButtonIcon, subPanel.Footer.Container, fIdx++);

            var wasFulfilled = !IsOrderFulfilled(order);
            var wasPaused = order.State != CraftingOrderState.Paused;
            order.Changed.Bind(l, (o) => {
                var fulfilled = IsOrderFulfilled(o);
                var paused = o.State == CraftingOrderState.Paused;
                if (wasFulfilled != fulfilled) {
                    wasFulfilled = fulfilled;
                    // Force UpdatePauseButton below to load the icon, tint and label
                    wasPaused = !paused;
                    if (fulfilled) {
                        stateButton.Load(IconId.WRetry, T.Repeat, () => {
                            dem.AmountProduced = 0;
                            dem.AmountProducedThis = 0;
                            order.State = CraftingOrderState.InQueue;
                            crafter.UpdateExtraInfo();
                            order.Ping();
                        });
                    } else {
                        stateButton.SetInteractable(true);
                        stateButton.SetOnClick(() => {
                            if (order.State == CraftingOrderState.Paused) {
                                order.State = CraftingOrderState.InQueue;
                            } else {
                                order.State = CraftingOrderState.Paused;
                            }
                            crafter.UpdateExtraInfo();
                            order.Ping();
                        });
                    }
                }
                if (fulfilled) { return; }
                if (wasPaused != paused) {
                    wasPaused = paused;
                    UpdatePauseButton(o, stateButton);
                }
            });

            subPanel.ShowFooter(true);
        }

        private void BuildColorBlock(CraftingOrder order, Lifetime l,
                RectTransform container, ref int idx) {
            if (order.Demand.Craftable.ColorGroupId == null) { return; }

            var colChoice = ColorChoice.ForGroup(S, order.Demand.Craftable.ColorGroupId)
                .WithAction(def => {
                    order.Demand.ColorId = def?.Id;
                    order.Ping();
                })
                .IncludeRandom(true);

            var colorWidget = UISpawn.Widget<WButtonIconText2>(l,
                UIPrefabId.WContent2Button, container, idx++);
            colorWidget.Load(IconId.CColorWheel, T.ChangeColor, null, () => colChoice.Show(colorWidget.Rect));
            UIUtils.AddTip(l, colorWidget, T.ChangeColor);

            order.Changed.Bind(l, _ => RefreshColorWidget(colorWidget, order.Demand.ColorId));
        }

        private static void RefreshColorWidget(WButtonIconText2 w, string colorId) {
            if (colorId != null) {
                var def = Palette.Get(colorId);
                w.SetImage(IconId.CColorPreview);
                w.SetTint(def.Color);
                w.SetText(def.NameT);
            } else {
                w.SetImage(IconId.CColorWheel);
                w.SetTint(Consts.ColorWhite);
                w.SetText(T.RandomColor);
            }
        }

        private void UpdatePauseButton(CraftingOrder order, WButtonIconText btn) {
            string icon;
            Color iconTint;
            if (order.State == CraftingOrderState.Paused) {
                icon = IconId.WPlay;
                iconTint = The.UITheme.GetColor("Green", this);
                btn.SetText(T.Resume);
            } else {
                icon = IconId.WPause;
                iconTint = The.UITheme.GetColor("Accent", this);
                btn.SetText(T.InputPause);
            }
            btn.SetImage(icon);
            btn.SetTint(iconTint);
        }

        private bool ToggleIngredients(CraftingOrder order,
                Lifetime lifetime, RectTransform container,
                bool willShow, bool wasLoaded) {
            ingredientsExpanded = willShow;
            if (!willShow) { return wasLoaded; }
            if (wasLoaded) { return true; }

            int idx = 0;

            var ings = order.Ingredients;
            for (int i = 0; i < ings.Length; i++) {
                Mat ing = ings[i];
                var w0 = UISpawn.Widget<WIconText2Progress>(lifetime,
                    UIPrefabId.WContentProgress, container, idx++);
                w0.Load(ing.Type.IconId, ing.Type.NameT, 0f, T.NotAvailable);

                var ingI = i;
                order.Changed.Bind(lifetime, (o) => {
                    var ing = ings[ingI];
                    w0.SetProgress((float)ing.StackSize / ing.MaxStackSize,
                        Units.XOutOfY(ing.StackSize, ing.MaxStackSize));
                });

                AddMatTip(lifetime, ing, w0);
            }

            return true;
        }

        private void AddMatTip(Lifetime lifetime, Mat ing, WIconText2Progress w0) {
            UIUtils.AddTipFunc(lifetime, w0, () => {
                var owned = S.Sys.Inventory.CountOfProduct(ing.Type.Def);
                var ownedText = Units.Num(owned);
                if (owned < ing.Diff) {
                    ownedText = Texts.Red(ownedText);
                }
                return $"{T.OwnedCount}: {ownedText}";
            });

            UIUtils.AddCodexTooltip(lifetime, w0.gameObject, w0, ing.Type.Def);
        }

        private void OnCreateOrder(WBase w) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            if (subPanel.IsShowingFor(w)) {
                subPanel.Close();
                return;
            }

            subPanel.SetTitle(T.SelectItem);
            var l = subPanel.CreateLifetime(lifetime);

            var availableMaterials = Caches.MatCounts;
            availableMaterials.Clear();
            S.Sys.Inventory.LoadRemainingMaterials(availableMaterials);
            var citems = crafter.UI.GetCraftableItems();

            var pCont = subPanel.Container.Container;

            var block = UISpawn.Block(l, subPanel.Container, 0);

            var cont = block.Container;

            var count = 0;
            var r = S.Research;
            var locked = Caches.DefList;
            locked.Clear();
            int idx = 0;
            foreach (var c in citems.OrderBy(i => i.ProductDef.NameT)) {
                var item = c.ProductDef;
                if (!r.IsDefUnlocked(item)) {
                    locked.Add(item);
                    continue;
                }
                count++;
                var crName = MultiCrafterUI.CraftableName(S, c, item, out var src, out var mat);
                var wgt = UISpawn.Widget<WContent3Button>(l,
                    UIPrefabId.WContent3Button, cont, idx++);
                var invcount = S.Sys.Inventory.CountColored(item, 10, prependX: true);
                src = $"{Texts.Grey(GlyphMap.ArrowUp)} {src}";
                wgt.TextTop.SetText(crName);
                wgt.Button.Load(item.Preview, src, invcount, () => StartCrafting(c, subPanel));
                UIUtils.AddTipFunc(l, wgt, () => {
                    return crafter.UI.ShowProductionChoiceTooltipFor(c, item);
                });
                UIUtils.AddCodexTooltip(l, wgt.gameObject, wgt, item);
            }
            if (count == 0) {
                var wgt = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, cont, idx++);
                wgt.Load(IconId.CInfo, T.NoBlueprints);
                UIUtils.AddTip(l, wgt, T.NoBlueprintsHelp);
            }
            if (locked.Count > 0) {
                ResearchLockedRow.Spawn(l, cont, idx++, locked);
            }

            subPanel.Show(w);
        }

        private void StartCrafting(Craftable c, SubPanelWidget subPanel) {
            var order = crafter.UI.StartProducing(c);
            if (order != null) {
                crafter.UI.SetAutoCraftingAmountFor(order.Demand);
                pendingConfigOrder = order;
            }
            crafter.UI.CheckCrawlerAlerts(c);
            subPanel.Close();
            ReloadTab();
        }

        protected override void Cleanup() {
            S = null;
            crafter = null;
        }

        public override string Id => TabId;
    }
}
