using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class ShipConfigTab_Devices {
        private readonly ShipConfigWidget parent;
        private readonly GameState S;
        private readonly List<IShipDeviceComp> pendingDevices = new();
        private Lifetime lifetime;
        private Lifetime parentLifetime;
        private bool pendingDirty;

        public ShipConfigTab_Devices(ShipConfigWidget parent, GameState s) {
            this.parent = parent;
            S = s;
        }

        public void Load(Lifetime parentLifetime) {
            lifetime?.Dispose();
            lifetime = parentLifetime.CreateChild("ShipConfig.Devices");
            this.parentLifetime = parentLifetime;
            var parentLife = parentLifetime;
            A.Player.Ship.CombatState.DevicesChangedB.Bind(lifetime, () => {
                Load(parentLife);
            }, false);
            ListenForPendingChanges();

            var block = UISpawn.Block(lifetime, parent.WItems, 0);
            int idx = 0;

            // var header = UISpawn.Widget<WText>(lifetime,
            //     UIPrefabId.WContent2Header, block.Container, idx++);
            // header.Load(T.NounClassPoints);

            var combatState = A.Player.Ship.CombatState;
            var devices = combatState.Devices;
            foreach (var device in combatState.Devices.OrderByDescending(
                    d => d.Device.DeviceDef.ClassPoints).ThenBy(d => d.Device.Def.NameT)) {
                var tile = device.Device.Tile;
                var cp = device.Device.DeviceDef.ClassPoints;
                var name = device.Device.NameWithSeq;
                var cpText = T.NounClassPointsShort_V1.F(cp);

                if (tile != null) {
                    var capturedTile = tile;
                    var btn = UISpawn.Widget<WButtonIconText2>(lifetime,
                        UIPrefabId.WContent2Button, block.Container, idx++);
                    btn.Load(tile.Definition.Preview, name, cpText,
                        () => EntityUtils.SelectOrFocus(capturedTile));
                    UIUtils.AddHover(lifetime, btn,
                        (h) => EntityUtils.ShowEntityLink(null, capturedTile, h));
                } else {
                    var row = UISpawn.Widget<WIconText2>(lifetime,
                        UIPrefabId.WContent2Text2, block.Container, idx++);
                    row.Load(IconId.CInfo, name, cpText);
                }
            }

            if (devices.Count == 0) {
                var empty = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2TextBlock, block.Container, idx++);
                empty.Load(T.Empty);
            }

            var total = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            var totalCP = combatState.CurrentClassPoints;
            var maxCP = A.Player.Ship.Class.MaxClassPoints;
            var isOver = totalCP > maxCP;
            var totalCPText = isOver ? Texts.Red(Units.Num(totalCP)) : Texts.Green(Units.Num(totalCP));
            var icon = totalCP > maxCP ? IconId.CWarning : IconId.CCheck;
            var text = $"{totalCPText}/{maxCP}";
            total.Load(icon, T.NounTotalSum, T.NounClassPointsShort_V1.F(text));

            LoadPendingBlock(totalCP, maxCP);
        }

        private void LoadPendingBlock(int builtCP, int maxCP) {
            pendingDevices.Clear();
            var cache = Caches.ShipDeviceComps;
            var found = S.Components.FindAllByInterface(cache);
            for (int i = 0; i < found; i++) {
                if (ShipCombatSys.IsPendingPlayerDevice(cache[i])) {
                    pendingDevices.Add(cache[i]);
                }
            }
            if (pendingDevices.Count == 0) { return; }
            pendingDevices.Sort(ComparePending);

            var block = UISpawn.Block(lifetime, parent.WItems, 1);
            int idx = 0;
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.ConstructionPending);

            var pendingCP = 0;
            for (int i = 0; i < pendingDevices.Count; i++) {
                var comp = pendingDevices[i];
                var cp = comp.DeviceDef.ClassPoints;
                pendingCP += cp;
                var entity = comp.Entity;
                var btn = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                btn.Load(entity.Definition.Preview, entity.Definition.NameT,
                    T.NounClassPointsShort_V1.F(cp),
                    () => EntityUtils.SelectOrFocus(entity));
                UIUtils.AddHover(lifetime, btn,
                    (h) => EntityUtils.ShowEntityLink(null, entity, h));
            }
            pendingDevices.Clear();

            var total = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            var isOver = builtCP + pendingCP > maxCP;
            var pendingCPText = isOver ? Texts.Red(Units.Num(pendingCP)) : Texts.Green(Units.Num(pendingCP));
            var icon = isOver ? IconId.CWarning : IconId.CCheck;
            total.Load(icon, T.NounTotalSum, T.NounClassPointsShort_V1.F(pendingCPText));
        }

        private static int ComparePending(IShipDeviceComp a, IShipDeviceComp b) {
            var byCP = b.DeviceDef.ClassPoints.CompareTo(a.DeviceDef.ClassPoints);
            if (byCP != 0) { return byCP; }
            return string.CompareOrdinal(a.Entity.Definition.NameT, b.Entity.Definition.NameT);
        }

        // DevicesChangedB only covers built devices, so blueprint placement and
        // cancellation need entity signals. Rebuilds are coalesced to once per frame
        // because blueprints are often placed in bulk.
        private void ListenForPendingChanges() {
            pendingDirty = false;
            S.Sig.EntityAdded.AddListener(OnEntityChanged);
            S.Sig.EntityRemoved.AddListener(OnEntityChanged);
            S.Sig.ConstructionCompleted.AddListener(OnConstructionCompleted);
            S.Sig.EveryFrame.AddListener(OnEveryFrame);
            lifetime.OnDispose(() => {
                S.Sig.EntityAdded.RemoveListener(OnEntityChanged);
                S.Sig.EntityRemoved.RemoveListener(OnEntityChanged);
                S.Sig.ConstructionCompleted.RemoveListener(OnConstructionCompleted);
                S.Sig.EveryFrame.RemoveListener(OnEveryFrame);
            });
        }

        private void OnEntityChanged(Entity entity) {
            if (entity.HasComponent<IShipDeviceComp>()) {
                pendingDirty = true;
            }
        }

        private void OnConstructionCompleted(Tile tile) {
            OnEntityChanged(tile);
        }

        private void OnEveryFrame() {
            if (!pendingDirty || lifetime == null) { return; }
            pendingDirty = false;
            Load(parentLifetime);
        }

        public void Cleanup() {
            lifetime?.Dispose();
            lifetime = null;
        }
    }
}
