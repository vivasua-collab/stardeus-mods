using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class StarmapMenuWidget : BaseWidget {
        [SerializeField] private WButtonText wPanelHeader;
        [SerializeField] private WTabRow wTabs;
        [SerializeField] private WText wTextHeader;
        [SerializeField] private WText wTextSubHeader;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        public SubPanelWidget SubPanel;
        public WGroup WItems => wItems;
        private Lifetime lifetime;
        private GameState S;
        public Action OnRandomize;
        private StarmapContext ctx;
        private readonly Dictionary<string, WTabButton> layerTabs = new();
        private readonly List<StarmapMenuBlock> menuBlocks = new();
        private bool menuBlocksSorted;
        private bool isLoading;
        private bool loadPending;
        public void Initialize(GameState s, StarmapContext ctx) {
            S = s;
            this.ctx = ctx;
            wTextHeader.Load(T.Location);
            wPanelHeader.Load(T.Starmap, Close);
            wTabs.DestroyChildren();
            wItems.DestroyChildren();
            SubPanel.Initialize();
            CreateTabs();
            CreateMenuBlocks(s);
        }

        private void CreateMenuBlocks(GameState s) {
            // Flight capability is added via BindFuel()
            menuBlocks.Add(new StarmapMenu_Hyperjump(this, ctx, s));
            menuBlocks.Add(new StarmapMenu_Scanning(this, ctx, s));
            menuBlocks.Add(new StarmapMenu_AutoMiners(this, ctx, s));
            menuBlocks.Add(new StarmapMenu_Contents(this, ctx, s));
            // mods can hook in here
        }

        private void CreateTabs() {
            int idx = 0;
            layerTabs.Clear();
            CreateTab(idx++, T.SpaceObjectUniverse, UniverseLayer.Universe);
            CreateTab(idx++, T.SpaceObjectRegion, UniverseLayer.Region);
            CreateTab(idx++, T.SpaceObjectSector, UniverseLayer.Sector);
        }

        private void CreateTab(int idx, string name, string layer) {
            // Tabs have no lifetime, they will last for the whole game
            var prefab = ResourceLibrary.FindPrefab<WTabButton>(UIPrefabId.WTabButton);
            var w = PrefabPool.Spawn(prefab, wTabs.Container);

            w.Load(name, () => ShowLayer(layer));
            w.SetSiblingIdx(idx++);
            layerTabs[layer] = w;
        }

        private void ShowLayer(string layer) {
            if (layer == ctx.Layer) {
                // TODO refocus / recenter / reload
                return;
            }
            ctx.SwitchLayer(layer);
        }

        public void Load() {
            if (isLoading) {
                // Something (e.g. a signal fired synchronously by an immediate bind
                // callback while a Load is already in progress) tried to reload the menu
                // while it's already reloading. Re-entering here would dispose lifetimes
                // still in use by the outer call, so coalesce into a single deferred load.
                loadPending = true;
                return;
            }
            isLoading = true;
            try {
                LoadNow();
            } finally {
                isLoading = false;
            }
            if (loadPending) {
                loadPending = false;
                Coroutines.OnNextFrame(Load);
            }
        }

        private void LoadNow() {
            Cleanup();
            ctx.MenuLifetime = lifetime = ctx.MainLifetime.CreateChild("StarmapMenu");

            var ploc = SOUtils.GetPlayerSector(S);
            var text = ploc.SO.ParentPath(false);
            wTextSubHeader.Load(text);
            scroll.verticalNormalizedPosition = 1f;

            foreach (var tab in layerTabs) {
                tab.Value.SetSelected(tab.Key == ctx.Layer);
            }
            RefreshHyperspaceTabs();

            int idx = 0;

            if (!menuBlocksSorted) {
                menuBlocks.Sort((a, b) => a.Order.CompareTo(b.Order));
                menuBlocksSorted = true;
            }

            foreach (var block in menuBlocks) {
                block.Load(idx);
                idx++;
            }
        }

        public void Close() {
            S.UIQuery.ShowStarmap.Ask(false);
        }

        // Nothing fires a signal when a hop actually starts moving (HyperjumpQueue.OnTick's
        // Progress.Start(...) call sends none), so this is polled every frame from
        // StarmapWidget.UpdateWidget() instead of relying solely on HyperjumpQueueChanged -
        // SetActiveFast is a no-op when the state hasn't changed, so this is cheap.
        public void RefreshHyperspaceTabs() {
            if (layerTabs.Count == 0) { return; }
            var inHyperspace = ctx.IsInHyperspace;
            var crossRegion = ctx.IsCrossRegionHyperjump;
            layerTabs[UniverseLayer.Sector].SetActiveFast(!inHyperspace);
            layerTabs[UniverseLayer.Region].SetActiveFast(!crossRegion);
            // If we're still showing a layer whose tab we just hid, the hyperjump is
            // happening somewhere the current view can no longer represent - switch to a
            // layer that's still valid instead of leaving a stale view on screen.
            if (crossRegion && ctx.Layer != UniverseLayer.Universe) {
                ctx.SwitchLayer(UniverseLayer.Universe);
            } else if (inHyperspace && ctx.Layer == UniverseLayer.Sector) {
                ctx.SwitchLayer(UniverseLayer.Region);
            }
        }

        public void BindFuel(MatType fuelSectorType, MatType fuelRegionType,
                BInt fuelSector, BInt fuelRegion) {
            var flightCapability = new StarmapMenu_FlightCapability(this, S, ctx,
                fuelSectorType, fuelRegionType, fuelSector, fuelRegion);
            menuBlocks.Add(flightCapability);
        }

        public void Cleanup() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            ctx.MenuLifetime = null;
            SubPanel.Close();
        }
    }
}
