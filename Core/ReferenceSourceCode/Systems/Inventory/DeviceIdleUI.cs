using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.Systems.Notify;
using Game.Systems.UIUX;
using UnityEngine;

namespace Game.Systems.Inventory {
    public sealed class DeviceIdleUI : BaseDeviceListUI<ICanBeIdle> {

        protected override NotificationType NotificationType => NotificationType.Info;
        public DeviceIdleUI(GameState S) : base(
                S, S.Sig.IdleChanged, T.IdleDevices, IconId.CTime, IconId.WConfig) {
        }

        protected override IOrderedEnumerable<ICanBeIdle> OrderedDevices() {
            return devices.OrderBy(d => d.IdleCategory);
        }

        protected override string Tooltip() {
            return BuildTooltip();
        }

        protected override string GetCategory(ICanBeIdle device) {
            return device.IdleCategory;
        }

        protected override void SpawnDeviceWidget(Lifetime life,
                ICanBeIdle idle, RectTransform container, ref int idx) {
            var prod = idle.IdleProducts;
            if (prod == null) { return; }
            var def = idle.ProductDef;
            if (def == null) { return; }
            var tile = idle.DeviceTile;
            var w = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, container, idx++);
            w.Load(def.Preview, prod, null, () => {
                S.Sig.SelectEntity.Send(tile);
            });
            w.SetTint(idle.ProductColor);
            UIUtils.AddTipFunc(life, w, () => idle.IdleTooltip);
            UIUtils.AddHover(life, w, on => {
                EntityUtils.ShowEntityLink(null, tile, on);
            });
        }

        protected override void PopulateDeviceList(Lifetime life,
                RectTransform container) {
            var active = FilterActiveDevices()
                .Where(d => d.IdleProducts != null && d.ProductDef != null);
            var groups = active.GroupBy(d => d.DeviceTile.Definition)
                .OrderBy(g => g.First().IdleCategory);
            int childIdx = 0;
            foreach (var group in groups) {
                var items = group.ToList();
                if (items.Count == 1) {
                    SpawnDeviceWidget(life, items[0], container, ref childIdx);
                    continue;
                }
                var first = items[0];
                var tileDef = first.DeviceTile.Definition;
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
                    innerLife = toggleLife.CreateChild("IdleGroup");
                    int innerIdx = 0;
                    foreach (var idle in items) {
                        SpawnDeviceWidget(innerLife, idle, c, ref innerIdx);
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
