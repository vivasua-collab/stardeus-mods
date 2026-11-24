using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.Systems.AI;
using Game.Systems.Notify;
using Game.Systems.Trade;
using Game.Systems.UIUX;
using KL.Collections;
using UnityEngine;

namespace Game.Systems.Inventory {
    public sealed class MatDeficitUI : BaseDeviceListUI<MatDeficitEventHolder> {
        protected override bool AllowUnconstructed => true;
        protected override NotificationType NotificationType => NotificationType.Info;
        public MatDeficitUI(GameState S) : base(S, S.Sig.MatDeficitChanged,
                T.AdRejLackingMat, IconId.CLackingMaterials, IconId.WMaterials) {
            S.Sig.TradeExecuted.AddListener(OnTradeExecuted);
            S.Query.GetMaterialDeficits.SetResponder(GetDeficits);
        }

        private int GetDeficits(List<int> mats) {
            mats.Clear();
            int cnt = 0;
            foreach (var dev in devices) {
                if (mats.AddUniqueInefficient(dev.DeficitType.IdH)) {
                    cnt++;
                }
            }
            return cnt;
        }

        private void OnTradeExecuted(TradeUI trade) {
            if (!trade.HasBoughtSomething) { return; }
            foreach (var d in devices) {
                if (d.Owner is IAIGoalProvider goalProv) {
                    if (goalProv.TryGetGoal(out var g)) {
                        if (AIGoal.IsFailed(g)) {
                            AIGoal.TryRetry(g, G.Ticks, true, false);
                        }
                    }
                }
            }
        }

        protected override IOrderedEnumerable<MatDeficitEventHolder> OrderedDevices() {
            return devices.OrderBy(e => e.Category);
        }
        protected override string Tooltip() {
            return BuildTooltip();
        }


        protected override string GetCategory(MatDeficitEventHolder device) {
            return device.Category;
        }

        protected override void SpawnDeviceWidget(Lifetime life,
                MatDeficitEventHolder def, RectTransform container, ref int idx) {
            if (!def.Entity.IsActive) { return; }
            var tile = def.Tile;
            var matDef = def.DeficitType?.Def;
            if (tile == null || matDef == null) { return; }
            var w = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, container, idx++);
            w.Load(matDef.Preview, tile.NameOrLabel, def.DeficitType.NameT, () => {
                S.Sig.SelectEntity.Send(tile);
                // S.Sig.ShowInfoPanelDetails.Send(def.Owner);
            });
            UIUtils.AddHover(life, w, on => EntityUtils.ShowEntityLink(null, tile, on));
        }

        protected override void PopulateDeviceList(Lifetime life,
                RectTransform container) {
            var active = FilterActiveDevices();
            var groups = active.GroupBy(d => d.DeficitType?.IdH ?? 0)
                .OrderBy(g => g.First().DeficitType?.NameT);
            int childIdx = 0;
            foreach (var group in groups) {
                var first = group.First();
                var matType = first.DeficitType;
                if (matType == null) { continue; }
                var groupWidget = UISpawn.Widget<WContentListButton>(life,
                    UIPrefabId.WContent2ListButton, container, childIdx++);
                groupWidget.Load(matType.IconId, matType.NameT,
                    Units.XNum(group.Count()));
                Lifetime innerLife = null;
                groupWidget.LoadOnToggle(life, (toggleLife, c, willShow, wasLoaded) => {
                    if (innerLife != null) {
                        innerLife.Dispose();
                        innerLife = null;
                    }
                    if (!willShow) { return wasLoaded; }
                    innerLife = toggleLife.CreateChild("MatDeficitGroup");
                    int innerIdx = 0;
                    foreach (var def in group) {
                        if (!def.Entity.IsActive) { continue; }
                        var tile = def.Tile;
                        if (tile == null) { continue; }
                        var w = UISpawn.Widget<WButtonIconText2>(innerLife,
                            UIPrefabId.WContent2Button, c, innerIdx++);
                        w.Load(tile.Definition.Preview, tile.NameOrLabel, null, () => {
                            S.Sig.SelectEntity.Send(tile);
                        });
                        UIUtils.AddHover(innerLife, w, on =>
                            EntityUtils.ShowEntityLink(null, tile, on));
                    }
                    return true;
                });
                UIUtils.AddCodexTooltip(life, groupWidget.PopupArea.gameObject,
                    groupWidget, matType.Def);
            }
        }

        protected override string BuildTooltip() {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            Caches.MapStringInt.Clear();
            foreach (var device in OrderedDevices()) {
                var tileName = device.Tile?.NameOrLabel;
                if (tileName == null) { continue; }
                var deficit = device.DeficitType?.Def?.CodexLink;
                if (deficit == null) { continue; }
                var key = string.Format("{0}: {1}", tileName, deficit);
                if (!Caches.MapStringInt.TryGetValue(key, out var cnt)) {
                    cnt = 0;
                }
                Caches.MapStringInt[key] = cnt + 1;
            }
            foreach (var kv in Caches.MapStringInt.OrderBy(kv => kv.Key)) {
                if (kv.Value > 1) {
                    sb.AppendFormat("- {0} (x{1})\n", kv.Key, kv.Value);
                } else {
                    sb.AppendFormat("- {0}\n", kv.Key);
                }
            }
            return sb.ToString();
        }
    }
}
