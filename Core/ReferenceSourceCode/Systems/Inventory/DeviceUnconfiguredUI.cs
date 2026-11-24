using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.Notify;
using Game.Systems.UIUX;
using UnityEngine;

namespace Game.Systems.Inventory {
    public sealed class DeviceUnconfiguredUI : BaseDeviceListUI<ICanBeUnconfigured> {

        public DeviceUnconfiguredUI(GameState S) : base(S, S.Sig.UnconfiguredChanged,
                T.Unconfigured, IconId.CNotConfigured, IconId.WCog) {}


        protected override IOrderedEnumerable<ICanBeUnconfigured> OrderedDevices() {
            return devices.OrderBy(d => d.DeviceCategory);
        }

        protected override string Tooltip() {
            return BuildTooltip();
        }

        protected override string GetCategory(ICanBeUnconfigured device) {
            return device.DeviceCategory;
        }

        protected override void SpawnDeviceWidget(Lifetime life,
                ICanBeUnconfigured device, RectTransform container, ref int idx) {
            if (!device.DeviceTile.IsActive) { return; }
            var tile = device.DeviceTile;
            var w = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, container, idx++);
            w.Load(tile.Definition.Preview, tile.NameOrLabel, null, () => {
                S.Sig.SelectEntity.Send(tile);
            });
            UIUtils.AddHover(life, w, on => EntityUtils.ShowEntityLink(null, tile, on));
        }

        protected override void PopulateDeviceList(Lifetime life,
                RectTransform container) {
            var active = FilterActiveDevices();
            var groups = active.GroupBy(d => d.DeviceTile.Definition)
                .OrderBy(g => g.First().DeviceCategory);
            int childIdx = 0;
            foreach (var group in groups) {
                var items = group.ToList();
                if (items.Count == 1) {
                    SpawnDeviceWidget(life, items[0], container, ref childIdx);
                    continue;
                }
                var tileDef = items[0].DeviceTile.Definition;
                var groupWidget = UISpawn.Widget<WContentListButton>(life,
                    UIPrefabId.WContent2ListButton, container, childIdx++);
                groupWidget.Load(tileDef.Preview, tileDef.NameT,
                    Units.XNum(items.Count));
                Lifetime innerLife = null;
                groupWidget.LoadOnToggle(life, (toggleLife, c, willShow, wasLoaded) => {
                    if (innerLife != null) {
                        innerLife.Dispose();
                        innerLife = null;
                    }
                    if (!willShow) { return wasLoaded; }
                    innerLife = toggleLife.CreateChild("UnconfiguredGroup");
                    int innerIdx = 0;
                    foreach (var device in items) {
                        SpawnDeviceWidget(innerLife, device, c, ref innerIdx);
                    }
                    return true;
                });
            }
        }

        protected override string BuildTooltip() {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            Caches.MapStringInt.Clear();
            foreach (var device in devices) {
                var key = device.DeviceTile.NameOrLabel;
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
