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
    public sealed class ShipShield : IShipDevice {
        public ShipShieldDef ShieldDef;
        public ShipDevice Device { get; set; }
        public Def Def => Device.Def;
        // public ShipShieldUI UI;
        public ShipShieldPhase Phase;
        public ShipShieldComp Comp;
        public int PhaseTicksLeft;
        public int PhaseTicksTotal;
        public Action OnPhaseChange;
        public Action OnPhaseProgressChange;
        private static Curve sizeMulCurve;
        public float CurrentEnergyCost {
            get {
                switch (Phase) {
                    case ShipShieldPhase.Inactive:
                    case ShipShieldPhase.Disabled:
                        return Device.DeviceDef.EnergyCostPassive;
                    case ShipShieldPhase.Charging:
                    case ShipShieldPhase.Active:
                        return MaxEnergyCost;
                    default: {
                        D.Err("Unhanded phase: {0}", Phase);
                        return 0f;
                    }
                }
            }
        }

        public float MaxEnergyCost {
            get {
                var cost = Device.DeviceDef.EnergyCostActive;
                sizeMulCurve ??= Tunables.ShipSizeShieldEnergyCostCurve;
                var sizeMultiplier = sizeMulCurve.Evaluate(A.ShipSize);
                return cost * sizeMultiplier;
            }
        }

        public float PhasePercent {
            get {
                switch (Phase) {
                    case ShipShieldPhase.Charging: {
                        return 1f - (PhaseTicksLeft / (float) PhaseTicksTotal);
                    }
                    case ShipShieldPhase.Active: return 1f;
                    case ShipShieldPhase.Disabled:
                    case ShipShieldPhase.Inactive: return 0f;
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
                    case ShipShieldPhase.Disabled:
                    case ShipShieldPhase.Inactive: return false;
                    case ShipShieldPhase.Active:
                    case ShipShieldPhase.Charging: return true;
                    default: {
                        D.Err("Unhanded phase: {0}", Phase);
                        return true;
                    }
                }
            }
        }
        public string PhaseIcon {
            get {
                switch (Phase) {
                    case ShipShieldPhase.Inactive: return IconId.COff;
                    case ShipShieldPhase.Charging: return IconId.CLoading;
                    case ShipShieldPhase.Active: return IconId.CForceField;
                    case ShipShieldPhase.Disabled: return IconId.CForbid;
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
                    case ShipShieldPhase.Inactive: return T.Inactive;
                    case ShipShieldPhase.Charging: return T.Charging;
                    case ShipShieldPhase.Active: return T.Active;
                    case ShipShieldPhase.Disabled: return T.StateDisabled;
                    default: {
                        D.Err("Unhandled phase: {0}", Phase);
                        return Phase.ToString();
                    }
                }
            }
        }
        public string FullTooltip {
            get {
                var sb = Caches.StringBuilder3;
                sb.Clear();
                sb.Append(Texts.SubTitle(ShieldDef.Def.CodexLink));
                sb.AppendFormat("{0}: {1}\n", T.NounClassPoints,
                    Texts.Accented(ShieldDef.Device.ClassPoints));
                if (Comp?.Tile?.ENode != null) {
                    sb.AppendFormat("{0}: {1}\n", T.EnergyConsumption,
                        Texts.Accented(Units.ElectricityIO(
                            MaxEnergyCost,
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
        public bool IsActive => Phase != ShipShieldPhase.Inactive && Phase != ShipShieldPhase.Disabled;

        private bool SetPhase(GameState s, ShipShieldPhase phase) {
            if (Phase == phase) { return false; }
            // D.Warn("Shield phase: {0} -> {1}", Phase, phase);
            switch (phase) {
                case ShipShieldPhase.Inactive: {
                    PhaseTicksTotal = PhaseTicksLeft = 0;
                    break;
                }
                case ShipShieldPhase.Charging: {
                    var speed = Comp != null ? Comp.UpgradeSpeed : 1f;
                    var target = Maths.Max(1, Mathf.RoundToInt(ShieldDef.ChargeTimeMinutes / speed));
                    if (PhaseTicksTotal != target) {
                        PhaseTicksTotal = PhaseTicksLeft = target;
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
            s.Sig.ShipDevicePhaseChanged.Send(this);
            return true;
        }

        /// <returns>true if significant changes happened, will need UI rebuild</returns>
        public bool Tick(GameState s, long ticks, bool forceUp) {
            if (!Device.Ship.IsInCombat && !forceUp) {
                if (Phase != ShipShieldPhase.Inactive) {
                    return SetPhase(s, ShipShieldPhase.Inactive);
                }
                return false;
            } else if (!Device.IsOperational || Device.Ship.CombatState.Flux.IsVenting
                    || Device.IsMagneticStormAffected(s)) {
                if (Phase != ShipShieldPhase.Disabled) {
                    return SetPhase(s, ShipShieldPhase.Disabled);
                }
            } else if (Phase == ShipShieldPhase.Disabled) {
                return SetPhase(s, ShipShieldPhase.Charging);
            }
            // change phase
            switch (Phase) {
                case ShipShieldPhase.Inactive: {
                    return SetPhase(s, ShipShieldPhase.Charging);
                }
                case ShipShieldPhase.Charging: {
                    var ticksLeft = PhaseTicksLeft - 1;
                    if (ticksLeft < 1) {
                        return SetPhase(s, ShipShieldPhase.Active);
                    } else {
                        PhaseTicksLeft = ticksLeft;
                        OnPhaseProgressChange?.Invoke();
                        // UI?.UpdateUIBlock(true);
                    }
                    return false;
                }
                case ShipShieldPhase.Disabled: {
                    // don't discharge because it's too hard to vent then
                    return false;
                }
                case ShipShieldPhase.Active: {
                    // TODO check energy
                    return false;
                }
                default: {
                    D.Err("Unhandled phase: {0}", Phase);
                    return false;
                }
            }
        }

        public override string ToString() {
            return $"ShipShield[{ShieldDef.Id}|{Phase}|{PhaseTicksLeft}s]";
        }

        public void Reset() {
            Phase = ShipShieldPhase.Inactive;
            PhaseTicksLeft = 0;
            PhaseTicksTotal = 0;
        }

        public void RebuildUI() {
            // UI?.Rebuild();
        }
        public void UpdateHealth() {
            // UI?.UpdateHealthBlock();
            Device.ZoneView?.UpdateHealthBar();
        }
    }
}
