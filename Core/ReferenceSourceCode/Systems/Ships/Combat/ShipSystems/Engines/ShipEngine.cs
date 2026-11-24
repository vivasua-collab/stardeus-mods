using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Turrets;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Ships {
    public sealed class ShipEngine : IShipDevice {
        public ShipEngineDef EngineDef;
        public ShipDevice Device { get; set; }
        public Def Def => Device.Def;
        public ShipEnginePhase Phase;
        public ShipCombatEngineComp Comp;
        public int PhaseTicksLeft;
        public int PhaseTicksTotal;
        public Action OnPhaseChange;
        public Action OnPhaseProgressChange;
        public bool IsActive => Phase != ShipEnginePhase.Inactive && Phase != ShipEnginePhase.Disabled;
        public float CurrentEnergyCost {
            get {
                switch (Phase) {
                    case ShipEnginePhase.Inactive:
                    case ShipEnginePhase.Disabled: return Device.DeviceDef.EnergyCostPassive;
                    case ShipEnginePhase.Charging:
                    case ShipEnginePhase.Active: return Device.DeviceDef.EnergyCostActive;
                    default: {
                        D.Err("Unhanded phase: {0}", Phase);
                        return 0f;
                    }
                }
            }
        }
        public float PhasePercent {
            get {
                switch (Phase) {
                    case ShipEnginePhase.Charging: {
                        return 1f - (PhaseTicksLeft / (float) PhaseTicksTotal);
                    }
                    case ShipEnginePhase.Active: return 1f;
                    case ShipEnginePhase.Disabled:
                    case ShipEnginePhase.Inactive: return 0f;
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
                    case ShipEnginePhase.Disabled:
                    case ShipEnginePhase.Inactive: return false;
                    case ShipEnginePhase.Active:
                    case ShipEnginePhase.Charging: return true;
                    default: {
                        D.Err("Unhanded phase: {0}", Phase);
                        return true;
                    }
                }
            }
        }
        public string PhaseName {
            get {
                switch (Phase) {
                    case ShipEnginePhase.Inactive: return T.Inactive;
                    case ShipEnginePhase.Charging: return T.Charging;
                    case ShipEnginePhase.Active: return T.Active;
                    case ShipEnginePhase.Disabled: return T.StateDisabled;
                    default: {
                        D.Err("Unhandled phase: {0}", Phase);
                        return Phase.ToString();
                    }
                }
            }
        }
        public string PhaseIcon {
            get {
                switch (Phase) {
                    case ShipEnginePhase.Inactive: return IconId.COff;
                    case ShipEnginePhase.Charging: return IconId.CLoading;
                    case ShipEnginePhase.Active: return IconId.CPropulsion;
                    case ShipEnginePhase.Disabled: return IconId.CForbid;
                    default: {
                        D.Err("Unhanded phase: {0}", Phase);
                        return IconId.CWarning;
                    }
                }
            }
        }

        public string FullTooltip {
            get {
                var sb = Caches.StringBuilder3;
                sb.Clear();
                sb.Append(Texts.SubTitle(EngineDef.Def.CodexLink));
                sb.AppendFormat("{0}: {1}\n", T.NounClassPoints,
                    Texts.Accented(EngineDef.Device.ClassPoints));
                if (Comp?.Tile?.TryGetComponent<ShipEngineComp>(out var engComp) == true) {
                    sb.AppendFormat("{0}: {1}\n", T.EnergyConsumption,
                        Texts.Accented(Units.ElectricityIO(
                            engComp.EnergyCost,
                            Comp.Tile.ENode.Node.Consumed)));
                }
                // sb.Append(GlyphMap.BRHR);

                var phasePct = PhasePercent;
                if (phasePct > 0f) {
                    sb.AppendFormat("{0}: {1}\n", PhaseName,
                        Texts.Accented(Units.Percentage(PhasePercent)));
                } else {
                    sb.AppendLine(PhaseName);
                }

                return sb.ToString();
            }
        }

        private bool SetPhase(GameState s, ShipEnginePhase state) {
            if (state == Phase) { return false; }
            switch (state) {
                case ShipEnginePhase.Inactive: {
                    PhaseTicksTotal = PhaseTicksLeft = 0;
                    break;
                }
                case ShipEnginePhase.Charging: {
                    if (PhaseTicksTotal != EngineDef.ChargeTimeMinutes) {
                        PhaseTicksTotal = PhaseTicksLeft = EngineDef.ChargeTimeMinutes;
                    }
                    break;
                }
                default: {
                    break;
                }
            }
            Phase = state;
            OnPhaseChange?.Invoke();
            // UI?.UpdateUIBlock(true);
            s.Sig.ShipDevicePhaseChanged.Send(this);
            return true;
        }

        /// <returns>true if ui needs rebuild after this</returns>
        public bool Tick(GameState s, long ticks) {
            if (!Device.Ship.IsInCombat) {
                if (Phase != ShipEnginePhase.Inactive) {
                    return SetPhase(s, ShipEnginePhase.Inactive);
                }
                return false;
            } else if (!Device.IsOperational || Device.IsMagneticStormAffected(s)) {
                if (Phase != ShipEnginePhase.Disabled) {
                    return SetPhase(s, ShipEnginePhase.Disabled);
                }
            } else if (Phase == ShipEnginePhase.Disabled) {
                SetPhase(s, ShipEnginePhase.Charging);
            }
            switch (Phase) {
                case ShipEnginePhase.Inactive: {
                    SetPhase(s, ShipEnginePhase.Charging);
                    break;
                }
                case ShipEnginePhase.Charging: {
                    var ticksLeft = PhaseTicksLeft - 1;
                    if (ticksLeft < 1) {
                        SetPhase(s, ShipEnginePhase.Active);
                    } else {
                        PhaseTicksLeft = ticksLeft;
                        OnPhaseProgressChange?.Invoke();
                    }
                    break;
                }
                case ShipEnginePhase.Disabled: {
                    // discharge
                    if (PhaseTicksLeft < PhaseTicksTotal) {
                        PhaseTicksLeft++;
                        OnPhaseProgressChange?.Invoke();
                    }
                    break;
                }
                case ShipEnginePhase.Active: {
                    // TODO check energy
                    break;
                }
                default: {
                    D.Err("Unhandled phase: {0}", Phase);
                    break;
                }
            }
            return false;
        }

        public override string ToString() {
            return $"ShipEngine[{EngineDef.Id}|{Phase}|{PhaseTicksLeft}s]";
        }

        public void Reset() {
            Phase = ShipEnginePhase.Inactive;
            PhaseTicksLeft = 0;
            PhaseTicksTotal = 0;
        }
        public string GetChargeInfo(out float chargePct) {
            if (PhaseTicksTotal > 0) {
                chargePct = 1f - PhaseTicksLeft / (float) PhaseTicksTotal;
            } else {
                chargePct = 0f;
            }
            switch (Phase) {
                case ShipEnginePhase.Charging: return T.Charging;
                case ShipEnginePhase.Inactive: return T.Inactive;
                case ShipEnginePhase.Disabled: return T.StateDisabled;
                case ShipEnginePhase.Active: return T.Active;
                default: {
                    D.Err("Unhandled weapon phase: {0}", Phase);
                    return Phase.ToString();
                }
            }
        }

        public void UpdateHealth() {
            // UI?.UpdateHealthBlock();
            Device.ZoneView?.UpdateHealthBar();
        }
    }
}
