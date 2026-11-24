using System;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Firex;
using Game.Systems.Turrets;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipCapacitor : IShipDevice {
        public ShipCapacitorDef CapacitorDef;
        public ShipDevice Device { get; set; }
        public Def Def => Device.Def;
        // public ShipCapacitorUI UI;
        public Action OnPhaseChange;
        public Action OnPhaseProgressChange;
        public ShipCapacitorPhase Phase;
        public ShipCapacitorComp Comp;
        public int PhaseTicksLeft;
        public int PhaseTicksTotal;

        public string FullTooltip {
            get {
                var sb = Caches.StringBuilder3;
                sb.Clear();
                sb.Append(Texts.SubTitle(CapacitorDef.Def.CodexLink));
                sb.AppendFormat("{0}: {1}\n", T.NounClassPoints, Texts.Accented(CapacitorDef.Device.ClassPoints));
                if (Comp?.Tile?.ENode != null) {
                    sb.AppendFormat("{0}: {1}\n", T.EnergyConsumption,
                        Texts.Accented(Units.ElectricityIO(
                            CapacitorDef.Device.EnergyCostActive,
                            Comp.Tile.ENode.Node.Consumed)));
                }
                sb.AppendFormat("{0}: {1}\n", T.NounFluxBonus, Texts.Accented(CapacitorDef.Capacity));
                return sb.ToString();
            }
        }

        public bool IsActive => Phase != ShipCapacitorPhase.Inactive;
        public bool IsReachable => Comp == null || Comp.Tile.ENode == null
            || Comp.Tile.ENode.IsReachable;

        public void Reset() {
            Phase = ShipCapacitorPhase.Inactive;
            PhaseTicksLeft = 0;
            PhaseTicksTotal = 0;
        }

        public void UpdateHealth() {
            // UI?.UpdateHealthBlock();
            Device.ZoneView?.UpdateHealthBar();
        }

        public override string ToString() {
            return $"ShipCapacitor[{CapacitorDef.Id}]";
        }

        public string PhaseIcon {
            get {
                switch (Phase) {
                    case ShipCapacitorPhase.Inactive: return IconId.COff;
                    case ShipCapacitorPhase.Charging: return IconId.CLoading;
                    case ShipCapacitorPhase.Active: return IconId.CFlux;
                    case ShipCapacitorPhase.Disabled: return IconId.CForbid;
                    default: {
                        D.Err("Unhanded phase: {0}", Phase);
                        return IconId.CWarning;
                    }
                }
            }
        }
        public string PhaseName {
            get {
                switch (Phase) {
                    case ShipCapacitorPhase.Inactive: return T.Inactive;
                    case ShipCapacitorPhase.Charging: return T.Charging;
                    case ShipCapacitorPhase.Active: return T.Active;
                    case ShipCapacitorPhase.Disabled: return T.StateDisabled;
                    default: {
                        D.Err("Unhandled phase: {0}", Phase);
                        return Phase.ToString();
                    }
                }
            }
        }

        public float PhasePercent {
            get {
                if (PhaseTicksTotal == 0) {
                    return 0f;
                }
                switch (Phase) {
                    case ShipCapacitorPhase.Disabled:
                    case ShipCapacitorPhase.Charging: {
                        return 1f - (PhaseTicksLeft / (float) PhaseTicksTotal);
                    }
                    case ShipCapacitorPhase.Active: return 1f;
                    case ShipCapacitorPhase.Inactive: return 0f;
                    default: {
                        D.Err("Unhanded phase: {0}", Phase);
                        return 1f - (PhaseTicksLeft / (float) PhaseTicksTotal);
                    }
                }
            }
        }
        public bool PhaseHasProgress {
            get {
                switch (Phase) {
                    case ShipCapacitorPhase.Disabled:
                    case ShipCapacitorPhase.Inactive: return false;
                    case ShipCapacitorPhase.Active:
                    case ShipCapacitorPhase.Charging: return true;
                    default: {
                        D.Err("Unhanded phase: {0}", Phase);
                        return true;
                    }
                }
            }
        }

        public float CurrentEnergyCost {
            get {
                switch (Phase) {
                    case ShipCapacitorPhase.Inactive:
                    case ShipCapacitorPhase.Disabled: return Device.DeviceDef.EnergyCostPassive;
                    case ShipCapacitorPhase.Charging:
                    case ShipCapacitorPhase.Active: return Device.DeviceDef.EnergyCostActive;
                    default: {
                        D.Err("Unhanded phase: {0}", Phase);
                        return 0f;
                    }
                }
            }
        }

        private bool SetPhase(GameState s, ShipCapacitorPhase phase) {
            if (Phase == phase) { return false; }
            switch (phase) {
                case ShipCapacitorPhase.Inactive: {
                    PhaseTicksTotal = PhaseTicksLeft = 0;
                    break;
                }
                case ShipCapacitorPhase.Charging: {
                    if (PhaseTicksTotal != CapacitorDef.ChargeTimeMinutes) {
                        PhaseTicksTotal = PhaseTicksLeft = CapacitorDef.ChargeTimeMinutes;
                    }
                    break;
                }
                default: {
                    break;
                }
            }
            Phase = phase;
            OnPhaseChange?.Invoke();
            // UI?.UpdateUIBlock(true);
            Device.Ship.CombatState.Flux.UpdateCapacity(Device.Ship.CombatState);
            s.Sig.ShipDevicePhaseChanged.Send(this);
            return true;
        }

        public bool Tick(GameState s, long ticks, bool forceUp = false) {
            if (!Device.Ship.IsInCombat && !forceUp) {
                if (Phase != ShipCapacitorPhase.Inactive) {
                    return SetPhase(s, ShipCapacitorPhase.Inactive);
                }
                return false;
            } else if (!Device.IsOperational || Device.IsMagneticStormAffected(s)) {
                if (Phase != ShipCapacitorPhase.Disabled) {
                    return SetPhase(s, ShipCapacitorPhase.Disabled);
                }
            } else if (Phase == ShipCapacitorPhase.Disabled) {
                return SetPhase(s, ShipCapacitorPhase.Charging);
            }
            switch (Phase) {
                case ShipCapacitorPhase.Inactive: {
                    return SetPhase(s, ShipCapacitorPhase.Charging);
                }
                case ShipCapacitorPhase.Charging: {
                    var ticksLeft = PhaseTicksLeft - 1;
                    if (ticksLeft < 1) {
                        return SetPhase(s, ShipCapacitorPhase.Active);
                    } else {
                        PhaseTicksLeft = ticksLeft;
                        OnPhaseProgressChange?.Invoke();
                        // UI?.UpdateUIBlock(true);
                    }
                    return false;
                }
                case ShipCapacitorPhase.Disabled: {
                    return false;
                }
                case ShipCapacitorPhase.Active: {
                    return false;
                }
                default: {
                    D.Err("Unhandled phase: {0}", Phase);
                    return false;
                }
            }
        }
    }
}
