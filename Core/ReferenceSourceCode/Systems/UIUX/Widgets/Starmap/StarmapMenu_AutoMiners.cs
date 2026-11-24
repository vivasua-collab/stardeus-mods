using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Planets;
using Game.Data.Space;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class StarmapMenu_AutoMiners : StarmapMenuBlock {
        public override int Order => 30;
        private Lifetime lifetime;

        public StarmapMenu_AutoMiners(StarmapMenuWidget parent, StarmapContext ctx, GameState s)
                : base(parent, ctx, s) {
        }

        public override void Load(int siblingIdx) {
            if (!S.BB.Get(BBKeyH.MiningAuto, false)) { return; }

            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = ctx.MenuLifetime.CreateChild("AutoMiners");

            var block = UISpawn.Block(lifetime, parent.WItems, siblingIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.ResearchMiningautomation);

            switch (ctx.Layer) {
                case UniverseLayer.Universe:
                    LoadUniverse(block, ref idx);
                    break;
                case UniverseLayer.Region:
                    LoadRegion(block, ref idx);
                    break;
                case UniverseLayer.Sector:
                    LoadSector(block, ref idx);
                    break;
            }
        }

        private List<AutoMinerInfo> FindMinersInSector(SpaceObject sectorSO) {
            var miners = new List<AutoMinerInfo>();
            AutoMinerList.CollectInSector(S, sectorSO, miners);
            return miners;
        }

        private void LoadUniverse(WContentBlock block, ref int idx) {
            bool found = false;
            var parentSO = ctx.ParentSO;
            if (parentSO == null || Arrays.IsEmpty(parentSO.Children)) {
                ShowEmpty(block, idx);
                return;
            }
            foreach (var regSO in parentSO.Children.OrderBy(c => c.DisplayName)) {
                var regMiners = new List<AutoMinerInfo>();
                AutoMinerList.CollectInRegion(S, regSO, regMiners);
                if (regMiners.Count == 0) { continue; }
                found = true;
                AddExpandableGroup(block, idx++, regSO, regMiners);
            }
            if (!found) {
                ShowEmpty(block, idx);
            }
        }

        private void LoadRegion(WContentBlock block, ref int idx) {
            bool found = false;
            var parentSO = ctx.ParentSO;
            if (Arrays.IsEmpty(parentSO.Children)) {
                ShowEmpty(block, idx);
                return;
            }
            foreach (var secSO in parentSO.Children.OrderBy(c => c.DisplayName)) {
                var secMiners = FindMinersInSector(secSO);
                if (secMiners.Count == 0) { continue; }
                found = true;
                AddExpandableGroup(block, idx++, secSO, secMiners);
            }
            if (!found) {
                ShowEmpty(block, idx);
            }
        }

        private void LoadSector(WContentBlock block, ref int idx) {
            var parentSO = ctx.ParentSO;
            var miners = FindMinersInSector(parentSO);
            if (miners.Count == 0) {
                ShowEmpty(block, idx);
                return;
            }
            foreach (var miner in miners.OrderBy(m => m.Mat.NameT)) {
                AddMinerButton(lifetime, block.Container, idx++, miner);
            }
        }

        private void ShowEmpty(WContentBlock block, int idx) {
            var w = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContent2Text, block.Container, idx);
            w.Load(IconId.CInfo, T.Empty);
        }

        private void AddExpandableGroup(WContentBlock block, int idx,
                SpaceObject groupSO, List<AutoMinerInfo> miners) {
            var w = UISpawn.Widget<WContentListButton>(lifetime,
                UIPrefabId.WContent2ListButton, block.Container, idx);
            var num = miners.Count > 1 ? Units.XNum(miners.Count) : null;
            w.Load(IconId.CDrill, groupSO.DisplayName, num);
            w.LoadOnToggle(lifetime, (life, container, willShow, wasLoaded) => {
                if (!willShow) { return wasLoaded; }
                if (wasLoaded) { return true; }
                int subIdx = 0;
                foreach (var miner in miners.OrderBy(m => m.Mat.NameT)) {
                    AddMinerButton(life, container, subIdx++, miner);
                }
                return true;
            });
        }

        private void AddMinerButton(Lifetime life, RectTransform container,
                int idx, AutoMinerInfo miner) {
            var so = miner.SO;
            var mt = miner.Mat;
            var w = UISpawn.Widget<WButtonIconText2Progress>(life,
                UIPrefabId.WContent2ProgressButton, container, idx);
            w.Load(mt.IconId, mt.NameT, 0f, () => {
                S.UIQuery.StarmapFocusOn.Ask(so, true);
            }, StorageText(so));
            UIUtils.AddTipFunc(life, w, () => AutoMinerList.RigTooltip(S, so));
            UpdateMinerProgress(w, so);

            S.Clock.TickB.Bind(life, (_) => {
                UpdateMinerProgress(w, so);
            }, notifyImmediately: false);
        }

        private void UpdateMinerProgress(WButtonIconText2Progress w, SpaceObject so) {
            if (S.Sys.Planets.TryGetResourceFor(so, out var resource, out var mt)) {
                float pct = resource.UnretrievedMax > 0
                    ? resource.Unretrieved / resource.UnretrievedMax
                    : 0f;
                w.SetPercent(pct, false);
                w.Item.SetText2(AutoMinerList.StorageText(resource));
                if (AutoMinerList.IsMaxDepthReached(resource)) {
                    w.Item.SetText($"{GlyphMap.SpriteWarning} {mt.NameT}");
                } else {
                    w.Item.SetText(mt.NameT);
                }
            }
        }

        private string StorageText(SpaceObject so) {
            if (S.Sys.Planets.TryGetResourceFor(so, out var resource, out _)) {
                return AutoMinerList.StorageText(resource);
            }
            return string.Empty;
        }
    }
}
