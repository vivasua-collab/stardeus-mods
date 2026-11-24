using System;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Ships {
    public sealed class ShipFlux {
        public NPCShip Ship { get; private set; }
        public ShipFluxState State { get; private set; }
        public int Capacity;
        public float Soft;
        public float Hard;
        public float HardPercent;
        public float TotalPercent;
        public float SoftVentPerTick;
        private float ventRate;
        private bool ventHard;
        public float OverloadVentPerTick;
        public float ManualVentPerTick;
        // The capacity the last Tick checked against, to name a capacity drop in the log
        private int lastTickCapacity;
        private static string hardTxt;
        private static string softTxt;
        private static string fullTxt;
        private BCallback onChanged;
        public BCallback OnChanged => onChanged ??= new BCallback(capacity: 32);
        public string VentRateText {
            get {
                softTxt ??= Texts.WithColor(T.FluxSoftShort, ShipCombatWidgetUtils.FluxSoftColorTxt);
                hardTxt ??= Texts.WithColor(T.FluxHardShort, ShipCombatWidgetUtils.FluxHardColorTxt);
                fullTxt ??= $"{hardTxt}+{softTxt}";
                var showHard = ventHard || (State == ShipFluxState.OK && !Ship.CombatState.IsShieldUp);
                var ventType = showHard ? fullTxt : softTxt;
                ventType = Texts.GreyParentheses(ventType);
                return string.Format("{0}: <b>{1:0}/{2}</b> {3}",
                    T.VentRate,
                    ventRate * Consts.TicksPerHour,
                    T.UnitShortHour,
                    ventType);
            }
        }

        public void LoadState(ShipFluxState state) {
            if (state == ShipFluxState.Undefined) {
                D.Err("Refusing to load uninitialized state on ShipFlux! Setting OK.");
                state = ShipFluxState.OK;
            }
            State = state;
        }

        public void SetState(ShipFluxState state) {
            if (state == ShipFluxState.Undefined) {
                D.Err("Refusing to set uninitialized state on ShipFlux! Setting OK.");
                state = ShipFluxState.OK;
            }
            if (State != state) {
                // D.Warn("Flux state: {0}->{1}", State, state);
                State = state;
            }
            SetRates(true);
        }

        private void SetRates(bool validate) {
            if (validate) {
                ValidateRates();
            }
            switch (State) {
                case ShipFluxState.OK: {
                        ventRate = SoftVentPerTick;
                        ventHard = false;
                        break;
                    }
                case ShipFluxState.Venting: {
                        ventRate = ManualVentPerTick;
                        ventHard = true;
                        break;
                    }
                case ShipFluxState.Overload: {
                        ventRate = OverloadVentPerTick;
                        ventHard = true;
                        break;
                    }
                default: {
                        D.Err("Unhandled flux state: {0}", State);
                        ventRate = SoftVentPerTick;
                        ventHard = false;
                        break;
                    }
            }
        }

        private void ValidateRates() {
            if (SoftVentPerTick == 0f) {
                D.Err("UpdateCapacity was not called before trying to set rates! {0}", Ship);
                UpdateCapacity(Ship.CombatState);
                if (SoftVentPerTick == 0f) {
                    D.Err("SoftVentPerTick still zero! {0}", Ship);
                }
            }
        }

        // FutureHardPercent and FutureTotalPercent preview the ship's own shots (AddShotFlux)
        public float FutureHardPercent(float toAddHard) {
            if (State == ShipFluxState.Overload) {
                toAddHard *= Tunables.ShipFluxHardVentMul;
            }
            return (Hard + toAddHard) / Capacity;
        }
        public float FutureTotalPercent(float toAddHard, float toAddSoft) {
            if (State == ShipFluxState.Overload) {
                toAddHard *= Tunables.ShipFluxHardVentMul;
                toAddSoft *= Tunables.ShipFluxSoftVentMul;
            }
            return (Soft + Hard + toAddHard + toAddSoft) / Capacity;
        }

        public bool IsVenting => State != ShipFluxState.OK;
        public bool IsOverloaded => State == ShipFluxState.Overload;

        public void LoadShip(NPCShip ship) {
            Ship = ship;
        }

        // Called when an NPC ship enters a fight. It enters with no flux: NPC flux is not
        // ticked between fights, and only Tick clamps flux to a lower capacity
        public void SetShip(NPCShip ship) {
            Ship = ship;
            Soft = 0f;
            Hard = 0f;
            HardPercent = 0f;
            TotalPercent = 0f;
            SetState(ShipFluxState.OK);
        }
        public void UpdateCapacity(ShipCombatState combatState) {
            Capacity = Tunables.ShipBaseFlux + Ship.Class.FluxBonus +
                combatState.FluxCapacitorBonus(true);
            var maxCap = Ship.Class.MaxFluxCapacity;
            if (maxCap > 0 && Capacity > maxCap) {
                Capacity = maxCap;
            }
            var ventPerHour = Ship.Class.FluxSoftVentPerHour + combatState.FluxVentBonus(true);
            SoftVentPerTick = ventPerHour / Consts.TicksPerHour;
            var maxVentPerTick = Capacity * Tunables.ShipFluxMaxVentPercentPerHour / Consts.TicksPerHour;
            if (SoftVentPerTick > maxVentPerTick) {
                SoftVentPerTick = maxVentPerTick;
            }
            OverloadVentPerTick = SoftVentPerTick * Tunables.ShipFluxOverloadVentMul;
            ManualVentPerTick = SoftVentPerTick * Tunables.ShipFluxActiveVentMul;
            SetRates(false);
            // No clamping here: flux above a lower capacity is Tick's call. Clamping here
            // deleted flux silently when a capacitor went offline (unpowering capacitors was a
            // free flux dump) and on load, where this runs before the capacitors are restored
            onChanged?.Invoke();
            // UI?.UpdateUIBlock(true);
        }

        // Flux from outside the ship: shield hits, hacks, space hazards. Always counts in full.
        // Shield vs hull is decided when a shot is fired and its flux lands on impact, so a
        // discount here let a ship open its vent while a volley flew and take 5% of its flux
        public void AddFlux(GameState s, float soft, float hard) {
            if (soft == 0f && hard == 0f) { return; }
            Hard += hard;
            Soft += soft;
            CheckFluxState(s);
        }

        // Flux this ship's own weapons generate when they fire. Discounted only while
        // overloaded, so an overload can end while weapons fire (at full cost every shot
        // would refill it). A venting ship holds fire at ships (ShipWeapon.IsHeldByVent), and
        // its shots at space threats cost in full
        public void AddShotFlux(GameState s, float soft, float hard) {
            if (soft == 0f && hard == 0f) { return; }
            if (State == ShipFluxState.Overload) {
                hard *= Tunables.ShipFluxHardVentMul;
                soft *= Tunables.ShipFluxSoftVentMul;
            }
            Hard += hard;
            Soft += soft;
            CheckFluxState(s);
        }

        private void CheckFluxState(GameState s) {
            if (Soft + Hard > Capacity) {
                ClampToCapacity();
                if (State != ShipFluxState.Overload) {
                    EnterOverload(s, null);
                }
            }
            UpdatePercents();
            onChanged?.Invoke();
        }

        private void ClampToCapacity() {
            if (Hard > Capacity) {
                Hard = Capacity;
            }
            if (Soft > Capacity - Hard) {
                Soft = Capacity - Hard;
            }
        }

        private void UpdatePercents() {
            if (Capacity > 0) {
                HardPercent = Hard / Capacity;
                TotalPercent = (Hard + Soft) / Capacity;
            }
        }

        private void EnterOverload(GameState s, string logLine) {
            SetState(ShipFluxState.Overload);
            if (Ship.IsPlayer) {
                s.Sig.ShowFanfare.Send(Texts.Red(T.FluxOverload));
                s.Sys.Log.AddLine(logLine ?? Texts.Red(T.FluxOverload));
            }
        }

        // Flux above capacity in a fight is an overload, also when the capacity dropped under
        // it (a capacitor destroyed, unpowered or moved). Out of a fight the excess is lost,
        // as capacitors switch off when a fight or a space threat ends
        private void OnOverCapacity(GameState s) {
            ClampToCapacity();
            if (Ship.IsInCombat && State != ShipFluxState.Overload) {
                var before = lastTickCapacity > Capacity ? lastTickCapacity : Capacity;
                EnterOverload(s, $"{Texts.Red(T.FluxOverload)}: {T.FluxCapacity} {before} {GlyphMap.ArrowRight} {Capacity}");
            }
        }

        public bool ToggleVent() {
            if (State == ShipFluxState.OK && Soft + Hard <= 0f) {
                return false;
            }
            if (State == ShipFluxState.Overload) {
                return false;
            }
            if (State == ShipFluxState.OK) {
                SetState(ShipFluxState.Venting);
            } else if (State == ShipFluxState.Venting) {
                SetState(ShipFluxState.OK);
            }
            return true;
        }
        public void ToggleVent(bool on) {
            if (State == ShipFluxState.Overload) {
                D.Warn("Cannot toggle hard vent while overloaded");
                return;
            }

            if (on && State != ShipFluxState.Venting) {
                SetState(ShipFluxState.Venting);
            } else if (!on && State == ShipFluxState.Venting) {
                SetState(ShipFluxState.OK);
            }
        }

        public void Tick(GameState s, long ticks) {
            bool wasUpdated = false;
            if (Soft + Hard > Capacity) {
                OnOverCapacity(s);
                wasUpdated = true;
            }
            lastTickCapacity = Capacity;
            if (Soft > 0f) {
                #if !PRODUCTION_BUILD
                if (ventRate <= 0f && !ventHard) {
                    D.Err("Soft Flux vent rate is 0!");
                    SetRates(true);
                }
                #endif
                Soft -= ventRate;
                wasUpdated = true;
                if (Soft < 0f) {
                    Soft = 0f;
                }
            } else if ((ventHard || !Ship.CombatState.IsShieldUp) && Hard > 0f) {
                Hard -= ventRate;
                wasUpdated = true;
                if (Hard < 0f) {
                    Hard = 0f;
                }
            }
            if (State != ShipFluxState.OK && Hard == 0f && Soft == 0f) {
                // done force-venting
                SetState(ShipFluxState.OK);
                wasUpdated = true;
            }
            if (wasUpdated) {
                UpdatePercents();
                onChanged?.Invoke();
            }
        }
    }
}
