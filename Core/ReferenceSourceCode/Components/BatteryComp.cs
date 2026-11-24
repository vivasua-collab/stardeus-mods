#if !PRODUCTION_BUILD
// #define DEBUG_BATTERY
#endif
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class BatteryComp : BaseComponent<BatteryComp>, IRelocatable,
            IDefPowerProvider {
        private const int DefaultEnergyOutput = 50;

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var capacity = def.ComponentConfigFor(ComponentIdH.ElectricNode, false)?
                .GetInt(PropertyIdH.BatteryMaxCharge, 0) ?? 0;
            power.SetBattery(capacity,
                config.GetInt(PropertyIdH.EnergyOutput, DefaultEnergyOutput));
        }

        public enum Status {
            Idle,
            Charging,
            Discharging
        }
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BatteryComp());
        }
        private string stateText {
            get {
                switch (status) {
                    case Status.Charging: return T.BatteryStateCharging;
                    case Status.Discharging: return T.BatteryStateDischarging;
                    default: return T.BatteryStateIdle;
                }
            }
        }
        public string StateText => stateText;
        public float BatteryCharge => elNode?.Node.BatteryCharge ?? 0f;
        public float BatteryMaxCharge => elNode?.Node.BatteryMaxCharge ?? 0f;
        public float CurDischargeRate => status == Status.Discharging ? elNode?.Node.Produced ?? 0f : 0f;
        public float CurChargeRate => status == Status.Charging ? elNode?.Node.Consumed ?? 0f : 0f;
        public float EnergyOutput => energyOutput;
        public string TtdText => EstimateRemainingTime();
        public string TtdTooltip => T.BatteryTtdTip_V1.F(Units.Electricity(energyOutput));
        public bool SkipDischargeWarning {
            get => skipDischargeWarning;
            set => skipDischargeWarning = value;
        }

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        public ElectricNodeComp ENode => elNode;

        public float ChargePercent => elNode.Node.BatteryCharge / elNode.Node.BatteryMaxCharge;
        private bool wasRelocated;

        private Status status;
        private float energyOutput;
        private bool skipDischargeWarning;
        private bool hadFullDischarge;
        private bool hasNotifiedDischarge;
        protected override void OnConfig() {
            energyOutput = Config.GetInt(PropertyIdH.EnergyOutput, DefaultEnergyOutput);
            if (energyOutput < 1) {
                D.Err("Battery has zero or negative energy output in config! {0}",
                    Entity.DefinitionId);
            }
        }

        public override string ToString() {
            return " * Battery";
        }

        protected override void OnLoad(ComponentData data) {
            skipDischargeWarning = data.GetBool("SkipDischargeWarn", false);
            hadFullDischarge = data.GetBool("HadFullDischarge", false);
        }

        private ElectricNodeComp elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elNode) {
                    this.elNode = elNode;
                    elNode.SetProducerMaxOutput(energyOutput);
                    elNode.AddAfterTick(this, AfterTickGrid);
                    UpdateBatteryBar();
                }
            } else if (message == MsgIdH.ConstructionFinished && !wasRelocated) {
                elNode?.SetBatteryCharge(elNode.Node.BatteryMaxCharge * 0.5f);
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("SkipDischargeWarn", skipDischargeWarning);
            data.SetBool("HadFullDischarge", hadFullDischarge);
        }

        private void NotifyDischarge() {
            if (skipDischargeWarning) { return; }
            if (!Ready.Gen) { return; }
            if (!hasNotifiedDischarge) {
                S.Sys.Log.AddLine(Tile, T.BatteryDischarged);
                hasNotifiedDischarge = true;
            }
        }

        private string EstimateRemainingTime() {
            // * 10 because we simulate electricity every 10 ticks
            var canLastTicks = Mathf.RoundToInt(elNode.Node.BatteryCharge / energyOutput) * 10;
            var canLastTime = Units.TicksToTime(canLastTicks, true);
            return canLastTime;
        }

        public void SetFullCharge() {
            elNode.Node.BatteryCharge = elNode.Node.BatteryMaxCharge;
            A.Grid.Update(elNode, elNode.Node);
            UpdateBatteryBar();
            stateChangedB?.Invoke();
        }

        private float lastCharge = -1f;
        private int lastChargeInt = -1;

        private void AfterTickGrid() {
            var isP = elNode.IsPowerable;
            if (status != Status.Idle) {
                if (!isP) {
                    status = Status.Idle;
                }
            }
            var charge = elNode.Node.BatteryCharge;
            if (Maths.Approximately(charge, lastCharge)) {
                return;
            }
            lastCharge = charge;
            if (isP) {
                if (charge < Maths.Epsilon) {
                    if (!hadFullDischarge) {
                        hadFullDischarge = true;
                        NotifyDischarge();
                    }
                } else if (hadFullDischarge && charge > elNode.Node.BatteryMaxCharge * 0.5f) {
                    hadFullDischarge = false;
                    if (hasNotifiedDischarge) {
                        hasNotifiedDischarge = false;
                    }
                }
                if (elNode.IsConsuming) {
                    status = Status.Charging;
                } else if (elNode.IsProducing) {
                    status = Status.Discharging;
                } else {
                    status = Status.Idle;
                }
            } else {
                status = Status.Idle;
            }
            UpdateBatteryBar();
            stateChangedB?.Invoke();
        }

        private void UpdateBatteryBar() {
            var chargeInt = Mathf.RoundToInt(lastCharge);
            if (chargeInt == lastChargeInt) { return; }
            lastChargeInt = chargeInt;
            var pct = ChargePercent;
            var bar = ShowInfoBar(Consts.ColorBatteryBar, pct, 0, 0,
                    T.BatteryChargeBar_V1.F(Units.Percentage(pct)));
            bar.IsPretranslated = true;
        }

        public void RelocateTo(Entity target) {
            var bat = target.GetComponent<BatteryComp>();
            bat.wasRelocated = true;
            var chargeBefore = elNode.Node.BatteryCharge;
            if (target.TryGetComponent<ElectricNodeComp>(out var otherElNode)) {
                otherElNode.Node.BatteryCharge = chargeBefore;
                // not doing this via set charge on purpose
                // to prevent updating node while it's not yet registered
                // otherElNode.SetBatteryCharge(chargeBefore);
            } else {
                D.Err("Battery charge lost after relocating the battery! {0} -> {1}",
                    chargeBefore, target);
            }
            bat.skipDischargeWarning = skipDischargeWarning;
        }
    }
}
