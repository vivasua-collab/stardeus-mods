using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class WalletWidget : BaseWidget, ICloseableWidget {
        [SerializeField] private WButton wCloseButton;
        [SerializeField] private WTabRow wTabs;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        [SerializeField] private WRawImage wChartImage;
        [SerializeField] private WText wChartTitle;
        [SerializeField] private WText ourWalletText;
        [SerializeField] private WGroup Footer;
        public SubPanelWidget SubPanel;
        public WGroup WItems => wItems;
        private Lifetime lifetime;
        private WalletContext ctx;
        private GameState S;
        private readonly Dictionary<string, WTabButton> tabs = new();
        private WalletTab_Wallet walletTab;
        private WalletTab_StockMarket stockMarketTab;
        private WalletTab_OnlineShop onlineShopTab;

        private RenderTexture chartTex;
        private RTChart chart;
        private bool isChartTooltipActive;
        private int lastChartX = -1;
        private int lastChartY = -1;
        private Color chartBgColor;
        private Color chartGradColor;

        public void Initialize(GameState s, WalletContext ctx) {
            gameObject.SetActiveFast(false);
            Register(UIWidgetId.Wallet, this);
            S = s;
            this.ctx = ctx;
            walletTab = new WalletTab_Wallet(this, s);
            stockMarketTab = new WalletTab_StockMarket(this, s);
            onlineShopTab = new WalletTab_OnlineShop(this, s);
            wCloseButton.Load(Close);
            wTabs.DestroyChildren();
            wItems.DestroyChildren();
            Footer.DestroyChildren();
            SubPanel.Initialize();
            CreateTabs();
            SetColors();
        }

        private void SetColors() {
            var t = The.UITheme;
            chartBgColor = t.GetColor("ChartBackground", this);
            chartGradColor = t.GetColor("ChartGradient", this);
        }

        public void OnThemeReloaded() {
            SetColors();
            if (chart != null) {
                ReleaseChartTexture();
                if (UIShowing.Wallet) {
                    S.UIQuery.RebuildWallet.Ask();
                }
            }
        }

        private void CreateTabs() {
            int idx = 0;
            tabs.Clear();
            CreateTab(idx++, T.Wallet);
            CreateTab(idx++, T.StockMarket);
            CreateTab(idx++, T.ResearchEconomyOnlineshop);
        }

        private void CreateTab(int idx, string name) {
            var prefab = ResourceLibrary.FindPrefab<WTabButton>(UIPrefabId.WTabButton);
            var w = PrefabPool.Spawn(prefab, wTabs.Container);
            w.Load(name, () => ctx.ShowTab(name));
            w.SetSiblingIdx(idx++);
            tabs[name] = w;
        }

        public override void Toggle(bool show) {
            if (show) {
                UIShowing.Set(UIShowingFlags.Wallet);
                Rect.SetAsLastSibling();
            } else {
                UIShowing.Unset(UIShowingFlags.Wallet);
            }
            base.Toggle(show);
            if (show) {
                if (!S.Research.IsUnlocked(DefIdsH.ResearchEconomyWallet)) {
                    WPopup.Create(false, IconId.CWarning, T.OperationFailed,
                        T.SysLockedByResearch_V1.F(
                            The.Defs.FastGet(DefIdsH.ResearchEconomyWallet)
                                .Researchable.NameT)).Spawn();
                    Toggle(false);
                    return;
                }
                Load();
            } else {
                Cleanup();
                ctx.SelectedTab = null;
            }
        }

        public void Load() {
            Cleanup();

            lifetime = ctx.MainLifetime = Lifetime.Create("Wallet");

            S.Wallet.AmountB.Bind(lifetime, amt => {
                ourWalletText.Load($"{T.OurWallet}: {Texts.Green(Wallet.ToValueString(amt))}");
            });

            if (ctx.SelectedTab == null) {
                ctx.SelectedTab = T.Wallet;
            }
            foreach (var tab in tabs) {
                tab.Value.SetSelected(tab.Key == ctx.SelectedTab);
            }

            if (ctx.SelectedTab == T.Wallet) {
                walletTab.Load(lifetime);
                ShowChart(false);
                if (Footer != null) { Footer.SetActiveFast(false); }
            } else if (ctx.SelectedTab == T.StockMarket) {
                var wantsChart = stockMarketTab.Load(lifetime);
                ShowChart(wantsChart);
                Footer.SetActiveFast(stockMarketTab.LoadFooter(Footer));
            } else if (ctx.SelectedTab == T.ResearchEconomyOnlineshop) {
                onlineShopTab.Load(lifetime);
                ShowChart(false);
                Footer.SetActiveFast(onlineShopTab.LoadFooter(Footer));
            }
        }

        private void ShowChart(bool show) {
            if (wChartImage != null) {
                wChartImage.gameObject.SetActiveFast(show);
            }
            if (wChartTitle != null) {
                wChartTitle.gameObject.SetActiveFast(show);
            }
            if (!show) {
                ReleaseChartTexture();
            }
        }

        public void UpdateChartTitle(string title) {
            if (wChartTitle != null) {
                wChartTitle.Load(title);
            }
        }

        public void RenderStockChart(MarketItemModifier hoveredItem) {
            if (wChartImage == null) { return; }

            var rawImage = wChartImage.GetComponentInChildren<RawImage>();
            if (rawImage == null) { return; }

            var rect = rawImage.rectTransform.rect;
            int width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(rect.height));

            if (width <= 1 || height <= 1) { return; }

            if (chartTex == null || chartTex.width != width || chartTex.height != height) {
                ReleaseChartTexture();
                chartTex = RenderTexture.GetTemporary(width, height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rawImage.texture = chartTex;
                chart = new RTChart(wChartImage.gameObject, chartTex,
                    chartBgColor, chartGradColor);
            }

            if (hoveredItem == null) {
                RenderGlobalIndexChart(chart);
            } else {
                RenderItemChart(chart, hoveredItem);
            }
        }

        private void RenderGlobalIndexChart(RTChart target) {
            var market = S.Market;
            if (market.History == null || market.History.Count < 2) { return; }

            var d = new List<RTChartNode>();
            var first = market.History.First();
            var prevX = first.Key;
            var prevY = first.Value;

            foreach (var entry in market.History) {
                var newVal = entry.Value;
                d.Add(new RTChartNode() {
                    Type = RTChartNode.NodeType.Line,
                    From = new Vector2(prevX, prevY),
                    To = new Vector2(entry.Key, newVal),
                    Color = newVal >= prevY ? Color.green : Color.red,
                    Thickness = 3
                });
                prevX = entry.Key;
                prevY = newVal;
            }

            var historyDays = 30 * 6;
            if (d.Count > historyDays) {
                d = d.GetRange(d.Count - historyDays, historyDays);
            }

            target.SetXDays(true);
            target.TooltipFunc = (xy, valXY, minX, minY, maxX, maxY, nodes) => {
                if (valXY.x < minX) { return null; }
                var day = $"{T.Day}: {Mathf.FloorToInt(valXY.x)}";
                var x = Units.TicksAgo(Mathf.FloorToInt(maxX - valXY.x) * Consts.TicksPerDay);
                if (nodes.Count == 1) {
                    return $"{nodes[0].ValueAt(xy.x)}\n{day}\n{x}";
                }
                return null;
            };
            target.Render(d, true, Units.AbbrevNum);
        }

        private void RenderItemChart(RTChart target, MarketItemModifier mod) {
            var hist = mod.History;
            if (hist == null || hist.Count < 2) { return; }

            float cb = 0f;
            float avgP = 0f;
            int owned = S.Portfolio?.OwnedPositions(mod.IdH, out cb, out avgP) ?? 0;

            var basePrice = mod.Item.Value;
            var d = new List<RTChartNode>();
            var first = hist.First();
            var prevX = first.Key;
            var prevY = first.Value * basePrice;
            int firstKey = first.Key;
            var last = hist.Last();

            foreach (var entry in hist) {
                var newVal = entry.Value * basePrice;
                d.Add(new RTChartNode() {
                    Type = RTChartNode.NodeType.Line,
                    From = new Vector2(prevX, prevY),
                    To = new Vector2(entry.Key, newVal),
                    Color = newVal >= prevY ? Color.green : Color.red,
                    Thickness = 3
                });
                prevX = entry.Key;
                prevY = newVal;
            }

            var historyDays = 30 * 6;
            if (d.Count > historyDays) {
                int numDays = d.Count - historyDays;
                d = d.GetRange(numDays, historyDays);
                firstKey = last.Key - historyDays;
            }

            if (owned > 0) {
                d.Add(new RTChartNode() {
                    Type = RTChartNode.NodeType.Line,
                    From = new Vector2(firstKey, avgP),
                    To = new Vector2(last.Key, avgP),
                    Color = Color.yellow,
                    SkipTooltip = true,
                    Thickness = 3
                });
            }

            target.SetXDays(true);
            var capturedOwned = owned;
            var capturedAvgP = avgP;
            var capturedMod = mod;
            target.TooltipFunc = (xy, valXY, minX, minY, maxX, maxY, nodes) => {
                if (nodes.Count != 1) { return null; }
                if (valXY.x < minX) { return null; }
                var day = $"{T.Day}: {Mathf.FloorToInt(valXY.x)}";
                var x = Units.TicksAgo(
                    Mathf.FloorToInt(maxX - valXY.x) * Consts.TicksPerDay);
                var val = nodes[0].ValueAt(xy.x);
                if (val > 1000) { val = Mathf.RoundToInt(val); }
                var y = Wallet.ToValueString(val, false);
                if (capturedOwned > 0) {
                    return $"{capturedMod.Item.Name}\n{y}\n{day}\n{x}\n" +
                        $"{Texts.WithColor(T.MarketAvgPrice, Colors.ToHex3(Texts.TextColorYellow, '#'))}" +
                        $": {Wallet.ToValueString(capturedAvgP, false)}";
                }
                return $"{capturedMod.Item.Name}\n{y}\n{day}\n{x}";
            };
            target.Render(d, true, y => Wallet.ToValueString(y));
        }

        private void Update() {
            if (chart == null || wChartImage == null || !wChartImage.gameObject.activeSelf) {
                return;
            }

            var sp = The.Bindings.Mouse.ScreenPosition;
            var x = Mathf.RoundToInt(sp.x);
            var y = Mathf.RoundToInt(sp.y);
            if (lastChartX == x && lastChartY == y) { return; }
            // only once the mouse moved, not on every frame
            var rawImage = wChartImage.GetComponentInChildren<RawImage>();
            if (rawImage == null) { return; }
            lastChartX = x;
            lastChartY = y;

            var firstUnderCursor = The.Bindings.Mouse.FirstUnderCursor;
            var isHovering = UIUtils.IsChildOf(wChartImage.transform,
                firstUnderCursor != null ? firstUnderCursor.transform : null);

            if (isHovering && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rawImage.rectTransform, sp, null, out var point)) {
                if (point.x < 0 || point.y > 0
                        || point.x > chartTex.width || point.y < -chartTex.height) {
                    if (isChartTooltipActive) { HideChartTooltip(); }
                    return;
                }
                isChartTooltipActive = true;
                point += new Vector2(0f, chartTex.height);
                var tt = chart.TooltipAt(point);
                if (tt != null) {
                    The.SysSig.ShowTooltip.Send(tt, true);
                } else {
                    // the last position stays: HideChartTooltip resets it, which made the
                    // next frame redo all of this and send HideTooltip again, every frame
                    isChartTooltipActive = false;
                    The.SysSig.HideTooltip.Send();
                }
            } else if (isChartTooltipActive) {
                HideChartTooltip();
            }
        }

        private void HideChartTooltip() {
            lastChartX = -1;
            lastChartY = -1;
            isChartTooltipActive = false;
            The.SysSig.HideTooltip.Send();
        }

        private void ReleaseChartTexture() {
            chart?.Unload();
            chart = null;
            if (chartTex != null) {
                RenderTexture.ReleaseTemporary(chartTex);
                chartTex = null;
            }
            if (isChartTooltipActive) {
                HideChartTooltip();
            }
        }

        public void Close() {
            S.UIQuery.ToggleWallet.Ask(false);
        }

        public bool TryCloseWidget() {
            if (SubPanel.IsShowing) {
                SubPanel.Close();
                return true;
            }
            Toggle(false);
            return true;
        }

        public void Cleanup() {
            walletTab?.Cleanup();
            stockMarketTab?.Cleanup();
            onlineShopTab?.Cleanup();
            if (Footer != null) { Footer.SetActiveFast(false); }
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            ctx.MainLifetime = null;
            SubPanel.Close();
            ReleaseChartTexture();
        }

        private void OnDisable() {
            ReleaseChartTexture();
        }
    }
}
