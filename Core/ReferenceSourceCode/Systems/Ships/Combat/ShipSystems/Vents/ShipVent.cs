using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Ships {
    public sealed class ShipVent : IShipDevice {
        public ShipVentDef VentDef;
        public ShipDevice Device { get; set; }
        public Def Def => Device.Def;
        public ShipVentComp Comp;

        public bool IsReachable => Comp == null || Comp.Tile.ENode == null
            || Comp.Tile.ENode.IsReachable;
        // A vent needs exposure to space to vent flux - disabled while enclosed indoors.
        public bool IsOutdoors => Comp == null || Comp.Tile.Outdoors == null
            || Comp.Tile.Outdoors.IsOutdoors;
        // No charge phase: a vent contributes its bonus whenever it's operational,
        // reachable and outdoors - in or out of combat.
        public bool IsActive => Device.IsOperational && IsReachable && IsOutdoors;

        public string PhaseIcon => IsActive ? IconId.CFlux : IconId.CForbid;
        public string PhaseName => IsActive ? T.Active : T.StateDisabled;
        public float PhasePercent => IsActive ? 1f : 0f;
        public bool PhaseHasProgress => false;
        public float CurrentEnergyCost => Device.DeviceDef.EnergyCostActive;

        public string FullTooltip {
            get {
                var sb = Caches.StringBuilder3;
                sb.Clear();
                sb.Append(Texts.SubTitle(VentDef.Def.CodexLink));
                sb.AppendFormat("{0}: {1}\n", T.NounClassPoints, Texts.Accented(VentDef.Device.ClassPoints));
                if (Comp?.Tile?.ENode != null) {
                    sb.AppendFormat("{0}: {1}\n", T.EnergyConsumption,
                        Texts.Accented(Units.ElectricityIO(
                            CurrentEnergyCost,
                            Comp.Tile.ENode.Node.Consumed)));
                }
                sb.AppendFormat("{0}: {1}\n", T.NounFluxVentBonus, Texts.Accented(VentDef.VentBonus));
                return sb.ToString();
            }
        }

        public void UpdateHealth() {
            Device.ZoneView?.UpdateHealthBar();
        }

        public override string ToString() {
            return $"ShipVent[{VentDef.Id}]";
        }
    }
}
