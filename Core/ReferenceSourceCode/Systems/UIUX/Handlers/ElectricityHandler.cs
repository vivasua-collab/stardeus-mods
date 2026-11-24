using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Rendering;
using Game.Systems.Electricity;
using Game.UI;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class ElectricityHandler : BaseWidgetHandler<ElectricityWidget> {
        private readonly ElectricityContext ctx;
        private Lifetime tabsLifetime;
        private readonly List<BaseElectricityTab> tabs = new();
        private BaseElectricityTab currentTab;
        private bool isChartVisible = true;

        public ElectricityHandler(GameState s, ElectricityWidget widget) : base(s, widget) {
            ctx = new ElectricityContext();
            widget.Initialize(s, ctx);
            s.Clock.OnTick.AddListener((ticks) => {
                // grid ticks on mod 0, so we'll update the chart and stats 1 tick after
                if (ticks % 10 == 1) {
                    ctx.TickB.Set(ticks);
                }
            });
            s.UIQuery.ToggleElectricity.SetResponder(ToggleElectricity);
            s.UIQuery.RebuildElectricity.SetResponder(RebuildElectricity);
            The.SysSig.UIThemeReloaded.AddListener(OnThemeReloaded);
            widget.OnShow = OnShow;
            widget.OnTabChanged = OnTabChanged;
            RegisterTabs();
        }

        public void Cleanup() {
            The.SysSig.UIThemeReloaded.RemoveListener(OnThemeReloaded);
        }

        private void OnThemeReloaded() {
            widget.OnThemeReloaded();
        }

        private void RegisterTabs() {
            tabs.Add(new ElectricityTab_Overview());
            tabs.Add(new ElectricityTab_SwitchedOff());
            tabs.Add(new ElectricityTab_Producers());
            tabs.Add(new ElectricityTab_Consumers());
            tabs.Add(new ElectricityTab_Batteries());
            tabs.Add(new ElectricityTab_Connectors());
            tabs.Add(new ElectricityTab_Disconnected());
        }

        // The grid overlay is a separate map view (F1), opening or closing
        // this panel never touches it
        private bool ToggleElectricity(bool show) {
            if (show != widget.gameObject.activeSelf) {
                widget.Toggle(show);
            }
            return true;
        }

        private bool RebuildElectricity() {
            if (widget.gameObject.activeSelf) {
                widget.Rebuild();
                return true;
            }
            return false;
        }

        private void OnShow(Lifetime lifetime) {
            currentTab ??= tabs.Count > 0 ? tabs[0] : null;
            LoadGridsAndTabs(lifetime);
        }

        private void OnTabChanged(Lifetime lifetime) {
            LoadGridsAndTabs(lifetime);
        }

        private void LoadGridsAndTabs(Lifetime lifetime) {
            if (tabsLifetime != null) {
                tabsLifetime.Dispose();
                tabsLifetime = null;
            }
            tabsLifetime = lifetime.CreateChild("ElecTabs");

            var sys = S.Sys.Electricity;
            LoadGridsList(tabsLifetime, sys);
            LoadTabButtons(tabsLifetime);
            LoadTabContent(tabsLifetime);
            LoadFooter(tabsLifetime, sys);
            if (isChartVisible) {
                widget.ShowChart(true);
                RenderChart(sys);
                ctx.TickB.Bind(tabsLifetime, (_) => {
                    RenderChart(sys);
                }, notifyImmediately: false);
            }
        }

        private void LoadGridsList(Lifetime lifetime, ElectricitySys sys) {
            int idx = 0;

            var managedGrids = new List<int>();
            var unmanagedGrids = new List<int>();
            for (int i = 0; i < sys.GridsCount; i++) {
                if (sys.TryGetGrid(i, out var grid)) {
                    if (grid.Size < 2) { continue; }
                    if (grid.HasShipComputer) {
                        managedGrids.Add(i);
                    } else {
                        unmanagedGrids.Add(i);
                    }
                }
            }

            // Managed grids - expandable, pre-expanded
            if (managedGrids.Count > 0) {
                var managedList = UISpawn.Widget<WContentListButton>(lifetime,
                    UIPrefabId.WContent2ListButton, widget.Grids.Container, idx++);
                managedList.Load(IconId.CComputing, T.GridsManaged,
                    Units.XNum(managedGrids.Count));
                managedList.LoadOnToggle(lifetime, (life, container, willShow, wasLoaded) => {
                    if (!willShow) { return wasLoaded; }
                    if (wasLoaded) { return true; }
                    int subIdx = 0;
                    foreach (var gridIdx in managedGrids) {
                        var capturedIdx = gridIdx;
                        sys.TryGetGrid(capturedIdx, out var g);
                        string displayName = GetGridDisplayName(sys, capturedIdx, g);
                        var btn = UISpawn.Widget<WButtonIconText2Progress>(life,
                            UIPrefabId.WContent2ProgressButton, container, subIdx++);
                        float prog;
                        if (g.MaxOutput > 0) {
                            prog = g.Consumption / g.MaxOutput;
                        } else {
                            prog = 0f;
                        }
                        btn.Load(IconId.CComputing, displayName, prog,
                            () => {
                                OnManagedGridClicked(capturedIdx);
                            }, Units.XNum(g.Size));
                        var tip = UIUtils.AddTipFunc(life, btn,
                            () => $"{T.SituationGridsize}: {Texts.Accented(g.Size)}");
                        tip.HoverFun = (h) => {
                            if (h) {
                                HighlightGrid(capturedIdx, sys);
                            } else {
                                EntityUtils.HideEntityLink();
                            }
                        };
                        ctx.TickB.Bind(life, (_) => {
                            if (sys.TryGetGrid(capturedIdx, out var freshGrid)) {
                                string displayName = GetGridDisplayName(sys, capturedIdx, freshGrid);
                                btn.Item.SetText(displayName);
                                btn.Item.SetText2(Units.XNum(freshGrid.Size));
                                float p = freshGrid.MaxOutput > 0
                                    ? freshGrid.Consumption / freshGrid.MaxOutput
                                    : 0f;
                                btn.SetPercent(p, false);
                            }
                        }, notifyImmediately: false);
                    }
                    return true;
                }, expanded: true);

                // Pre-select first grid if none selected
                if (ctx.SelectedGridIdx < 0 && managedGrids.Count > 0) {
                    ctx.SelectedGridIdx = managedGrids[0];
                    sys.UI.SetSelectedGrid(ctx.SelectedGridIdx, false);
                }
            }

            // Unmanaged grids - expandable, NOT pre-expanded
            if (unmanagedGrids.Count > 0) {
                var unmanagedList = UISpawn.Widget<WContentListButton>(lifetime,
                    UIPrefabId.WContent2ListButton, widget.Grids.Container, idx++);
                unmanagedList.Load(IconId.CNoConnection, T.GridsUnmanaged,
                    Units.XNum(unmanagedGrids.Count));
                unmanagedList.LoadOnToggle(lifetime, (life, container, willShow, wasLoaded) => {
                    if (!willShow) { return wasLoaded; }
                    if (wasLoaded) { return true; }
                    int subIdx = 0;
                    foreach (var gridIdx in unmanagedGrids) {
                        var capturedIdx = gridIdx;
                        sys.TryGetGrid(capturedIdx, out var g);
                        var name = T.EnergyGridNr_V2.F(capturedIdx, T.Unknown);
                        var btn = UISpawn.Widget<WButtonIconText2>(life,
                            UIPrefabId.WContent2Button, container, subIdx++);
                        btn.Load(IconId.CNoConnection, name,
                            Units.XNum(g.Size), () => {
                                OnUnmanagedGridClicked(capturedIdx, sys);
                            });
                        UIUtils.AddHover(lifetime, btn, (h) => {
                            if (h) {
                                HighlightGrid(capturedIdx, sys);
                            } else {
                                EntityUtils.HideEntityLink();
                            }
                        });
                    }
                    return true;
                }, expanded: false);
            }
        }

        private string GetGridDisplayName(ElectricitySys sys, int capturedIdx, ElectricGrid g) {
            var gridState = ElectricityUtils.GridState(g);
            var name = T.EnergyGridNr_V2.F(capturedIdx, gridState);
            var isSelected = ctx.SelectedGridIdx == capturedIdx;
            var displayName = isSelected
                ? Texts.Accented(name, false)
                : name;
            return displayName;
        }

        private void OnManagedGridClicked(int gridIdx) {
            var sys = S.Sys.Electricity;
            ctx.SelectedGridIdx = gridIdx;
            sys.UI.SetSelectedGrid(gridIdx, false);
            ShowOverlay();
            widget.Rebuild();
        }

        private void OnUnmanagedGridClicked(int gridIdx, ElectricitySys sys) {
            FocusOnGrid(gridIdx, sys);
            ShowOverlay();
        }

        // Picking a grid or a view mode is about what the map shows, so the
        // map has to show it. Opening the panel alone never does this.
        private void ShowOverlay() {
            if (UIShowing.Overlay && UIShowing.OverlayType == OverlayId.Electricity) {
                return;
            }
            S.Sig.ShowOverlay.Send(OverlayId.Electricity);
        }

        private void FocusOnGrid(int gridIdx, ElectricitySys sys) {
            var b = GridBounds(gridIdx, sys);
            S.Sig.CameraFocus.Send(b.Center(), b.Extents, 0);
        }

        private void HighlightGrid(int gridIdx, ElectricitySys sys) {
            var b = GridBounds(gridIdx, sys);
            S.Sig.CameraShowPosLink.Send(null,
                b.Center(), b.Extents,
                CameraFocus.FlashColor());
        }

        private PosRect GridBounds(int gridIdx, ElectricitySys sys) {
            Bounds bounds = default;
            bool hasBounds = false;
            var arrNodes = sys.ArrNodes;
            for (int i = 0; i < arrNodes.Count; i++) {
                if (arrNodes.TryGet(i, out var node)) {
                    if (node.GridId == gridIdx && node.IsInitialized) {
                        if (!hasBounds) {
                            bounds = new Bounds(node.Position, Consts.V3Zero);
                            hasBounds = true;
                        } else {
                            bounds.Encapsulate(node.Position);
                        }
                    }
                }
            }
            if (bounds.size.sqrMagnitude < 2) {
                bounds.Expand(1f);
            }
            if (hasBounds) {
                return PosRect.From(bounds);
            }
            return PosRect.Invalid;
        }

        private void LoadTabButtons(Lifetime lifetime) {
            int idx = 0;
            foreach (var tab in tabs) {
                var capturedTab = tab;
                var prefab = ResourceLibrary.FindPrefab<WTabButton>(UIPrefabId.WTabButton);
                var btn = UISpawn.Widget<WTabButton>(lifetime, prefab,
                    widget.Tabs.Container, idx++);
                btn.Load(tab.Name, () => {
                    if (currentTab == capturedTab) { return; }
                    currentTab = capturedTab;
                    widget.Rebuild();
                });
                btn.SetSelected(currentTab == tab);
            }
            widget.Tabs.Show();
            LayoutRebuilder.ForceRebuildLayoutImmediate(widget.Tabs.Container);
        }

        private void LoadTabContent(Lifetime lifetime) {
            if (currentTab == null || ctx.SelectedGridIdx < 0) {
                return;
            }
            currentTab.LoadContent(lifetime, S, widget.WItems, ctx.SelectedGridIdx, widget);
        }

        private readonly List<string> viewModeOptions = new();

        private void LoadFooter(Lifetime lifetime, ElectricitySys sys) {
            if (widget.FooterGroup == null) { return; }
            widget.FooterGroup.SetActiveFast(true);
            int idx = 0;

            // View mode dropdown (always visible)
            if (viewModeOptions.Count == 0) {
                viewModeOptions.Add(T.LinkDisplayAuto);
                viewModeOptions.Add(T.LinkDisplayAll);
                viewModeOptions.Add(T.LinkDisplayConnectors);
                viewModeOptions.Add(T.LinkDisplayTeleporters);
            }
            var viewDropdown = UISpawn.Widget<WDropdown>(lifetime,
                UIPrefabId.WDropdownWithFrame, widget.FooterGroup.Container, idx++);
            viewDropdown.Load(viewModeOptions, (int)ctx.ViewMode, v => {
                ctx.ViewMode = (ElectricityContext.GridDisplayMode)v;
                sys.UI.SetGridDisplayMode(v);
                ShowOverlay();
            });
            UIUtils.AddTip(lifetime, viewDropdown, T.LinkDisplay);

            // Chart button (QuickChoice)
            var chartBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent4Button, widget.FooterGroup.Container, idx++);
            chartBtn.Load(IconId.CCog, T.Chart, null, () => {
                ShowChartQuickChoice(sys, chartBtn);
            });

            // if (currentTab is ElectricityTab_Overview) {
                var hasGrid = sys.TryGetGrid(ctx.SelectedGridIdx, out var grid);
                // Try Auto Connect button
                var autoBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent4Button, widget.FooterGroup.Container, idx++);
                autoBtn.Load(IconId.CEnergy, T.TryAutoConnect, null, () => {
                    var qc = QuickChoice.Create();
                    qc.Widget = autoBtn.Rect;
                    qc.AddButton(T.Confirm, () => {
                        sys.TryAutoConnectAll();
                        ctx.SelectedTab = T.EnergyOverview;
                        widget.Rebuild();
                    });
                    qc.Show();
                });
                UIUtils.AddTip(lifetime, autoBtn, T.TryAutoConnectTip);
                // Disconnect All button
                if (hasGrid && grid.HasShipComputer) {
                    var disconnectBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                        UIPrefabId.WContent4Button, widget.FooterGroup.Container, idx++);
                    disconnectBtn.Load(IconId.CNoEnergy, T.DisconnectAll, null, () => {
                        var qc = QuickChoice.Create();
                        qc.Widget = disconnectBtn.Rect;
                        qc.AddButton(T.Confirm, () => {
                            sys.DisconnectGridConnections(ctx.SelectedGridIdx);
                            ctx.SelectedGridIdx = 0;
                            A.S.UIQuery.RebuildElectricity.Ask();
                            widget.Rebuild();
                        });
                        qc.Show();
                    });
                }
            // }
        }

        private void ShowChartQuickChoice(ElectricitySys sys, WBase btn) {
            var qc = QuickChoice.Create(T.Chart);
            qc.Widget = btn.Rect;
            qc.AddToggle(T.Show, new QuickChoice.Toggle {
                    Value = isChartVisible,
                    OnToggle = v => {
                        isChartVisible = v;
                        widget.ShowChart(v);
                        if (v) {
                            RenderChart(sys);
                        }
                        ShowChartQuickChoice(sys, btn);
                    }
                });
            if (isChartVisible) {
                qc.AddToggle(T.CompressValues, new QuickChoice.Toggle {
                        Value = RTChart.CompressChart,
                        OnToggle = v => {
                            widget.ToggleChartView();
                            if (isChartVisible) {
                                RenderChart(sys);
                            }
                        }
                    });
                qc.AddButton(T.ClearHistory, () => {
                    ClearHistory(sys);
                }, autoClose: false);
            }
            qc.Show();
        }

        private readonly StringBuilder chartTooltipSB = new();
        private readonly List<RTChartNode> chartData = new();

        private void RenderChart(ElectricitySys sys) {
            if (!isChartVisible) { return; }
            BuildChartData(sys);
            widget.RenderElectricityChart(chartData, ChartTooltipFunc, ChartLabelY);
        }

        private void ClearHistory(ElectricitySys sys) {
            if (sys.History.Count > 2) {
                sys.History.RemoveRange(0, sys.History.Count - 2);
            }
            if (widget.ChartImage != null && widget.ChartImage.gameObject.activeSelf) {
                RenderChart(sys);
            }
        }

        private static string ChartLabelY(float y) {
            return Units.Electricity(y);
        }

        private string ChartTooltipFunc(Vector2 xy, Vector2 xyVal,
                float minX, float minY, float maxX, float maxY,
                List<RTChartNode> nodes) {
            var val = xy.y;
            if (val < 0) { return null; }
            chartTooltipSB.Clear();
            var x = Units.TicksAgo(Mathf.FloorToInt(maxX - xyVal.x));
            chartTooltipSB.Append(x);
            foreach (var n in nodes) {
                chartTooltipSB.Append("\n");
                chartTooltipSB.Append(Texts.WithColor(n.Name,
                    Colors.ToHex3(n.Color, '#')));
                chartTooltipSB.Append(": ");
                chartTooltipSB.Append(Units.Electricity(n.ValueAt(xy.x)));
            }
            return chartTooltipSB.ToString();
        }

        private void BuildChartData(ElectricitySys sys) {
            chartData.Clear();
            var hist = sys.History;
            var hc = hist.Count;
            if (hc < 2) { return; }
            var first = hist[0];
            var prevX = first.Tick;
            var prevYActOut = first.Consumption;
            var prevYMaxOut = first.MaxOutput;
            var prevYRenewOut = first.RenewableOutput;
            var prevYMaxOutWithBat = prevYMaxOut + first.BatteryOutput;
            var prevYBatCharge = prevYMaxOut + first.BatteryCharge * first.BatteryOutput;
            var prevYDeficit = first.Deficit;
            var lineColorMaxOutWithBat = Consts.ColorChartLightPurple;
            var lineColorBatCharge = Consts.ColorChartPurple;
            var lineColorMaxOut = Consts.ColorChartYellow;
            var lineColorRenewOut = Consts.ColorChartGreen;
            var lineColorActOut = Consts.ColorChartBlueGreen;
            var lineColorDeficit = Consts.ColorChartRed;
            for (int i = 0; i < hc; i++) {
                var val = hist[i];
                var tick = val.Tick;
                var newValMaxOut = hist[i].MaxOutput;
                var newValRenewOut = hist[i].RenewableOutput;
                var batOutput = hist[i].BatteryOutput;
                if (batOutput > 0) {
                    var newValMaxOutWithBat = newValMaxOut + batOutput;
                    chartData.Add(new RTChartNode() {
                        Type = RTChartNode.NodeType.Line,
                        Name = T.EnergyMaxBatOutput,
                        From = new Vector2(prevX, prevYMaxOutWithBat),
                        To = new Vector2(tick, newValMaxOutWithBat),
                        Color = lineColorMaxOutWithBat,
                        Thickness = 3
                    });
                    prevYMaxOutWithBat = newValMaxOutWithBat;
                } else {
                    prevYMaxOutWithBat = newValMaxOut;
                }
                var batCharge = hist[i].BatteryCharge * hist[i].BatteryOutput;
                if (batOutput > 0) {
                    var newBatCharge = newValMaxOut + batCharge;
                    chartData.Add(new RTChartNode() {
                        Type = RTChartNode.NodeType.Line,
                        Name = T.EnergyBatCharge,
                        From = new Vector2(prevX, prevYBatCharge),
                        To = new Vector2(tick, newBatCharge),
                        Color = lineColorBatCharge,
                        Thickness = 1.5f
                    });
                    prevYBatCharge = newBatCharge;
                } else {
                    prevYBatCharge = newValMaxOut;
                }
                chartData.Add(new RTChartNode() {
                    Type = RTChartNode.NodeType.Line,
                    Name = T.EnergyMaxOutput,
                    From = new Vector2(prevX, prevYMaxOut),
                    To = new Vector2(tick, newValMaxOut),
                    Color = lineColorMaxOut,
                    Thickness = 3
                });
                prevYMaxOut = newValMaxOut;
                if (newValRenewOut > 0f) {
                    chartData.Add(new RTChartNode() {
                        Type = RTChartNode.NodeType.Line,
                        DashSize = 10f,
                        Name = T.EnergyRenewable,
                        From = new Vector2(prevX, prevYRenewOut),
                        To = new Vector2(tick, newValRenewOut),
                        Color = lineColorRenewOut,
                        Thickness = 1.5f
                    });
                }
                prevYRenewOut = newValRenewOut;
                var newValActOut = val.Consumption;
                chartData.Add(new RTChartNode() {
                    Type = RTChartNode.NodeType.Line,
                    Name = T.EnergyCurOutput,
                    From = new Vector2(prevX, prevYActOut),
                    To = new Vector2(tick, newValActOut),
                    Color = lineColorActOut,
                    Thickness = 3
                });
                prevYActOut = newValActOut;
                var newValDeficit = val.Deficit;
                if (newValDeficit > 0 || prevYDeficit > 0) {
                    chartData.Add(new RTChartNode() {
                        Type = RTChartNode.NodeType.Line,
                        Name = T.EnergyDeficit,
                        From = new Vector2(prevX, prevYDeficit),
                        To = new Vector2(tick, newValDeficit),
                        Color = lineColorDeficit,
                        Thickness = 3
                    });
                    prevYDeficit = newValDeficit;
                }
                prevX = tick;
            }
        }
    }
}
