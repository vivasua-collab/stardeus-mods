using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;
using KL.Utils;

namespace Game.Components {
    public sealed class HyperdriveComp : BaseComponent<HyperdriveComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new HyperdriveComp());
        }
        public ElectricNodeComp ENode => Tile.ENode;
        public RefillableStorageComp Fuel;
        public ShipEngineComp Engine;
        private float chargePerGridTick;
        public float Charge;
        public bool IsWarp;
        public bool IsCharged => Charge >= 1f;
        private float lastPercentage = -1f;
        private const float MinFuelToRun = 0.5f;
        public bool HasFuel => Fuel != null && Fuel.CurrentStock >= MinFuelToRun;

        public bool TryJump() {
            if (Charge >= 1f && Fuel.TryConsume(1f)) {
                Charge = 0f;
                ShowChargeBar();
                S.Sig.HyperjumpExecuted.Send(this);
                return true;
            }
            return false;
        }

        protected override void OnConfig() {
            var chargeTimeHours = Config.GetFloat(PropertyIdH.ChargeTimeHours);
            var chargePerHour = 1f / chargeTimeHours;
            chargePerGridTick = chargePerHour / Consts.TicksPerHour * 10f;
            IsWarp = Config.GetBool(PropertyIdH.IsWarp, false);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("Charge", Charge);
        }

        protected override void OnLoad(ComponentData data) {
            Charge = data.GetFloat("Charge", 0f);
        }


        public override void OnLateReady(bool wasLoaded) {
            if (!Entity.TryGetComponent(out Fuel)) {
                D.Err("HyperdriveComp requires RefillableStorageComp! {0}",
                    Entity.DefinitionId);
            }
            if (!Entity.TryGetComponent(out Engine)) {
                D.Err("HyperdriveComp requires EngineComp! {0}",
                    Entity.DefinitionId);
            }
            if (Tile.IsConstructed) {
                ShowChargeBar();
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (IsWarp && message == MsgIdH.ElectricNodeConnectionChanged) {
                if (Tile.ENode.IsConnected && EntityUtils.IsWithinMargin(Tile.PosIdx, false, false)) {
                    S.UIQuery.ShowTutorialPopup.Ask(WarpPage.PageId, 1f);
                }
            }
        }

        /// <returns>true if energy consumption is wanted</returns>
        public bool TryRunHyperdrive(ElectricNodeComp elNode) {
            if (!elNode.IsPowerable || elNode.HasDeficit) {
                return false;
            }

            if (!HasFuel) {
                Fuel.NotifyLack();
                Fuel.CreateOrUpdateRefillGoal();
                Charge = 0f;
                ShowChargeBar();
                return false;
            }

            bool wantsEnergy = false;
            if (Charge < 1f && !A.IsInHyperspace) {
                if (elNode.IsConsuming) {
                    Charge += chargePerGridTick;
                }
                if (Charge >= 1f) {
                    Charge = 1f;
                } else {
                    wantsEnergy = true;
                }
                ShowChargeBar();
            }
            return wantsEnergy;
        }

        private void ShowChargeBar() {
            if (lastPercentage == Charge) { return; }
            lastPercentage = Charge;
            ShowInfoBar(Consts.ColorBatteryBar, Charge,
                $"{T.NounHyperdrive}: {T.Charging} ({Units.Percentage(Charge)})");
        }
    }
}
