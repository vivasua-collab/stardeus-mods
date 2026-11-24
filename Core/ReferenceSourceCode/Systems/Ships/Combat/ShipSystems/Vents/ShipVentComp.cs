using Game.Data;
using Game.CodeGen;
using KL.Utils;
using Game.Components;
using Game.Systems.Ships;
using Game.Systems.TileAnim;
using Game.Constants;
using Game.Utils;
using System.Text;
using Game.Systems.Stats;
using Game.Systems.UIUX;

namespace Game.Systems.Ships {
    public sealed class ShipVentComp : BaseComponent<ShipVentComp>,
            IShipDeviceComp, IDefDescriptionProvider, IStatProvider, IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ShipVentComp());
        }

        private ShipVent vent;
        public ShipVent Vent => vent;

        public ShipDeviceDef DeviceDef => ventDef.Device;

        private ShipVentDef ventDef;
        private bool isRegistered;
        private uint shipDeviceId;
        private Lifetime lifetime;

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetUInt("ShipDeviceId", vent?.Device.Id ?? 0);
        }

        protected override void OnLoad(ComponentData data) {
            shipDeviceId = data.GetUInt("ShipDeviceId", 0);
        }

        protected override void OnConfig() {
            var id = Config.GetString(PropertyIdH.ShipVentId);
            ventDef = ShipVentDef.Get(id);
            if (ventDef == null) {
                D.Err("Cannot find ShipVentDef by id {0} for {1}",
                    id, entity.DefinitionId);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            if (Tile.IsConstructed && !isRegistered) {
                if (wasLoaded) {
                    Ready.When(WhenH.ShipDataRestored, RestoreDevice);
                } else {
                    RegisterVent(true);
                }
            }
        }
        public override void UpdatePosIdx(int pos, int dx, int dy) {
            if (Tile.IsConstructed && !isRegistered
                    && EntityUtils.IsWithinMargin(Tile.CenterIdx, false, false)) {
                RegisterVent(true);
            }
        }

        private void RestoreDevice() {
            if (shipDeviceId != 0) {
                var device = S.Query.FindShipDevice.Ask(
                    S.Universe.Player.Ship, shipDeviceId);
                if (device is ShipVent v) {
                    vent = v;
                    if (vent.Device.Tile != Tile) {
                        D.Err("Vent device tile mismatch! {0}. Tile = {1}, Expected = {2}",
                            vent, Tile, vent.Device.Tile);
                    }
                } else {
                    D.Err("Failed to restore ShipDevice for: {0}. Not a ShipVent: {1}",
                        Tile, device);
                }
            }
            RegisterVent(true, wasLoaded: true);
        }

        public override void OnRemove() {
            RegisterVent(false);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                S.Clock.OnNextUpdate(() => {
                    RegisterVent(true);
                });
            } else if (message == MsgIdH.RelocationStateChanged) {
                if (Tile.Reinstallable.IsMidRelocation) {
                    RegisterVent(false);
                } else if (Tile.IsConstructed) {
                    RegisterVent(true);
                }
            }
        }

        private void RegisterVent(bool add, bool wasLoaded = false) {
            if (add && !Tile.IsActive) { return; }
            if (add && Tile.Reinstallable.IsMidRelocation) { return; }
            if (add && !EntityUtils.IsWithinMargin(Tile.CenterIdx, false, false)) {
                D.Warn("Skipping adding vent to tile outside of margin: {0}",
                    Tile);
                return;
            }
            if (isRegistered == add) { return; }
            var ship = S.Universe.Player.Ship;
            if (add) {
                if (vent == null) {
                    vent = ship.CombatState.AddVent(ventDef);
                    if (!vent.Device.SetTile(Tile)) {
                        ship.CombatState.RemoveDevice(vent);
                        isRegistered = false;
                        return;
                    }
                    if (wasLoaded) {
                        D.Err("Loaded vent but it was null, recreated: {0}",
                            vent);
                    }
                } else {
                    if (wasLoaded) {
                        if (!ship.CombatState.Vents.Contains(vent)) {
                            ship.CombatState.AddDevice(vent);
                            D.Err("Loaded vent but it wasn't in combat state, adding: {0}",
                                vent);
                        }
                    } else {
                        if (!ship.CombatState.AddDevice(vent)) {
                            D.Err("Trying to add vent twice: {0}", vent);
                        }
                    }
                }
                vent.Comp = this;
                Tile.ENode.SetConsumerWantedInput(vent.CurrentEnergyCost);
                ship.CombatState.Flux.UpdateCapacity(ship.CombatState);
                lifetime = Lifetime.Create($"ShipVent_{Tile.Id}");
                ship.CombatState.Flux.OnChanged.Bind(lifetime, UpdateVentGraphic);
            } else {
                if (!ship.CombatState.RemoveDevice(vent)) {
                    D.Err("Failed to remove the vent {0}", vent);
                }
                ship.CombatState.Flux.UpdateCapacity(ship.CombatState);
                lifetime?.Dispose();
                lifetime = null;
                HideInfoIcon();
                if (Tile.TryGetComponent<TileAnimationComp>(out var anim)) {
                    anim.RegisterAnim(false);
                }
            }
            isRegistered = add;
        }

        private void UpdateVentGraphic() {
            if (vent == null || Tile == null) { return; }
            ShowInfoIcon(vent.PhaseIcon, vent.PhaseName);
            var shouldAnimate = vent.IsActive && vent.Device.Ship.CombatState.Flux.IsVenting;
            if (Tile.TryGetComponent<TileAnimationComp>(out var anim)) {
                anim.RegisterAnim(shouldAnimate);
            }
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            Def.PrependAdditionalInfo(sb, addedAnything);
            var config = def.ComponentConfigFor(NameHash);
            if (config == null) { return false; }
            var id = config.GetString(PropertyIdH.ShipVentId);
            var device = ShipVentDef.Get(id);
            sb.AppendFormat("{0}: <b>{1}</b>", T.NounFluxVentBonus, device.VentBonus);
            sb.AppendFormat(", {0}: <b>{1}</b>", T.NounClassPoints, device.Device.ClassPoints);
            return true;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            ShipVentDef.Get(config.GetString(PropertyIdH.ShipVentId))
                .Device.AddDefPower(ref power);
        }

        public string StatsCategoryName => T.NounFluxVent;
        public int StatsOrder => 15;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            if (ventDef == null) { return; }
            var cp = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            cp.Load(IconId.CFlux, T.NounClassPoints, Units.Num(ventDef.Device.ClassPoints));

            var bonus = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            bonus.Load(IconId.CFlux, T.NounFluxVentBonus, Units.Num(ventDef.VentBonus));

            var energy = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            energy.Load(IconId.CEnergy, $"{T.EnergyConsumption} ({T.Max})", Units.Electricity(ventDef.Device.EnergyCostActive));
        }
    }
}
