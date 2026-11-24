using System;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using UnityEngine;
using Game.Input;

namespace Game.Components {
    public sealed class VentComp : BaseComponent<VentComp>, IHullIntegrityProvider,
            IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new VentComp());
        }
        private const float DefaultEnergyCost = 0.1f;
        private bool grabbedSurroundings;

        private int n, e, s, w;

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            grabbedSurroundings = false;
        }

        public bool IsClosed => isClosed;
        // Toggling does nothing without power, so the Vent tab disables its button on this
        public bool IsPowered => Tile.ENode.IsReachable && !Tile.ENode.HasDeficit;
        private bool isClosed;
        private bool isExposedToOpenSpace;
        private bool isAddedToEquilibrium;
        private float energyCost;
        private float minIntegrity;
        public float Integrity => isClosed ? 1f : minIntegrity;

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsClosed", isClosed);
        }

        protected override void OnLoad(ComponentData data) {
            isClosed = data.GetBool("IsClosed", isClosed);
        }

        public override void OnLateReady(bool wasLoaded) {
            var minInsulAirtight = Mathf.Min(Tile.Integrity.MaxInsulation, Tile.Integrity.MaxAirtight);
            if (minInsulAirtight > 0f) {
                minIntegrity = 0.05f / minInsulAirtight;
            }
            if (!isAddedToEquilibrium) {
                isAddedToEquilibrium = true;
                S.Sig.VentChanged.Send(this, true);
                SetClosed(isClosed);
            }
        }

        public override void OnRemove() {
            if (isAddedToEquilibrium) {
                isAddedToEquilibrium = false;
                S.Sig.VentChanged.Send(this, false);
            }
        }

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public bool IsExposedToOpenSpace() {
            if (!Tile.IsConstructed) { return true; }
            if (isClosed) { return false; }
            GrabSurroundings();
            var a = S.Sys.Areas;
            var aN = a.At(n);
            // 0 is space, -1 is a wall
            if (aN == 0 || aN < -1) { return true; }
            var aW = a.At(w);
            if (aW == 0 || aW < -1) { return true; }
            var aE = a.At(e);
            if (aE == 0 || aE < -1) { return true; }
            var aS = a.At(s);
            if (aS == 0 || aS < -1) { return true; }
            return false;
        }

        // Return true if there was some oxygen in at least one direction of
        // the vent. Mostly for triggering a hint
        // FIXME job system + burst this
        public bool Equalize() {
            if (isClosed) { return false; }
            if (!Tile.IsConstructed) { return false; }
            GrabSurroundings();
            var a = S.Sys.Areas;

            var aN = a.At(n);
            var aW = a.At(w);
            var aE = a.At(e);
            var aS = a.At(s);

            // 0 is space, -1 is a wall
            var exposed = aN == 0 || aW == 0 || aE == 0 || aS == 0
                || aN < -1 || aW < -1 || aE < -1 || aS < -1;
            if (exposed != isExposedToOpenSpace) {
                isExposedToOpenSpace = exposed;
                UpdateIcon();
            }

            // up down
            if (aN != -1 && aS != -1 && aW == -1 && aE == -1) {
                var heatN = S.GetTemp(n);
                var heatS = S.GetTemp(s);
                var oxyN = S.GetO2(n);
                var oxyS = S.GetO2(s);
                if (heatN > heatS) {
                    var newH = Math.Max(heatN * 0.9f, (heatN + heatS) / 2f);
                    S.SetTemp(n, newH);
                    S.SetTemp(s, newH);
                } else if (heatS > heatN) {
                    var newH = Math.Max(heatS * 0.9f, (heatN + heatS) / 2f);
                    S.SetTemp(n, newH);
                    S.SetTemp(s, newH);
                }
                if (oxyN > oxyS) {
                    var newO = Math.Max(oxyN * 0.9f, (oxyS + oxyN) / 2f);
                    S.SetO2(s, newO);
                    S.SetO2(n, newO);
                } else if (oxyS > oxyN) {
                    var newO = Math.Max(oxyS * 0.9f, (oxyN + oxyS) / 2f);
                    S.SetO2(s, newO);
                    S.SetO2(n, newO);
                }
                if (oxyN > 0.05f || oxyS > 0.05f) {
                    return aN == 0 || aS == 0;
                } else {
                    return false;
                }
                // left right
            } else if (aN == -1 && aS == -1 && aW != -1 && aE != -1) {
                var heatE = S.GetTemp(e);
                var heatW = S.GetTemp(w);
                var oxyE = S.GetO2(e);
                var oxyW = S.GetO2(w);
                if (heatE > heatW) {
                    var newH = Math.Max(heatE * 0.9f, (heatE + heatW) / 2f);
                    S.SetTemp(e, newH);
                    S.SetTemp(w, newH);
                } else if (heatW > heatE) {
                    var newH = Math.Max(heatW * 0.9f, (heatW + heatE) / 2f);
                    S.SetTemp(e, newH);
                    S.SetTemp(w, newH);
                }
                if (oxyE > oxyW) {
                    var newO = Math.Max(oxyE * 0.9f, (oxyE + oxyW) / 2f);
                    S.SetO2(w, newO);
                    S.SetO2(e, newO);
                } else if (oxyW > oxyE) {
                    var newO = Math.Max(oxyW * 0.9f, (oxyW + oxyE) / 2f);
                    S.SetO2(w, newO);
                    S.SetO2(e, newO);
                }
                if (oxyE > 0.05f || oxyW > 0.05f) {
                    return aW == 0 || aE == 0;
                } else {
                    return false;
                }
            }
            return false;
        }

        private void GrabSurroundings() {
            if (!grabbedSurroundings) {
                grabbedSurroundings = true;
                var p = Tile.PosIdx;
                n = Pos.N(p);
                e = Pos.E(p);
                s = Pos.S(p);
                w = Pos.W(p);
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp enode) {
                    enode.SetConsumerWantedInput(energyCost);
                }
            }
        }

        // Public for tests
        public void ToggleVent() {
            if (Tile.Health.IsHeavilyDamaged) {
                WPopup.Warning(T.OperationFailed, T.OperationFailedDeviceHeavilyDamaged);
                return;
            }
            if (!IsPowered) { return; }
            SetClosed(!isClosed);
        }

        // for procgen
        public void SetClosed(bool closed) {
            isClosed = closed;
            Transmit(this, MsgIdH.HullIntegrityChanged);
            UpdateIcon();
        }

        private void UpdateIcon() {
            if (isClosed) {
                ShowInfoIcon(IconId.CNoFlow, T.VentClosed);
            } else if (isExposedToOpenSpace) {
                ShowInfoIcon(IconId.CWarning, $"{T.VentOpen} {GlyphMap.ArrowRight} {Texts.Red(T.AreaExposedToSpace)}");
            } else {
                HideInfoIcon();
            }
        }
        public override string ToString() {
            return " * Vent";
        }
    }
}
