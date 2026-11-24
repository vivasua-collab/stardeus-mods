using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Components {
    public sealed class TerraformControlsComp : BaseComponent<TerraformControlsComp>,
            IDefPowerProvider, IOperatorWorkSource {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TerraformControlsComp());
        }

        private const float DefaultEnergyCost = 50f;
        private float energyCost;
        private bool isTerraformingExplained;
        private OperatableDeviceComp operatable;
        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                isTerraformingExplained = true;
            }
            #endif
        }

        public override string ToString() {
            return " * TerraformControls";
        }

        public override void OnLateReady(bool wasLoaded) {
            if (!entity.TryGetComponent(out operatable)) {
                D.Err("TerraformControls requires OperatableDeviceComp! {0}",
                    entity.DefinitionId);
            }
        }

        private IEnumerator ExplainTerraforming() {
            while (UIShowing.Dialog || !Ready.Game) {
                yield return null;
                if (!GameState.IsCurrent(S)) { yield break; }
            }
            The.Dialog.ShowSimple(IconId.CComputing, T.TerraformingAbout);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                    if (elNode.IsConnected && !elNode.HasDeficit) {
                        S.BB.Set(BBKeyH.TerraformControlsPing, G.Ticks);
                    }
                }
            }
        }

        private void AfterTickGrid() {
            if (elNode.IsConnected && !elNode.HasDeficit) {
                S.BB.Set(BBKeyH.TerraformControlsPing, G.Ticks);
                CheckExplainTerraforming();
            }
            if (!IsTerraforming) {
                operatable.DismissOperator(true);
                elNode.SetConsumerWantedInput(0f);
                return;
            }
            if (operatable.Operator == null) {
                operatable.HailOperator(false);
                elNode.SetConsumerWantedInput(0f);
                return;
            }
            elNode.SetConsumerWantedInput(energyCost);
            if (!elNode.IsConsuming) { return; }
            S.BB.Set(BBKeyH.TerraformControlsOperatorPing, G.Ticks);
        }

        private bool IsTerraforming {
            get {
                var target = S.Sys.Terraform.Target;
                return target != null && target.TerraformingProgress < 1f;
            }
        }
        public bool IsOperatorWorking => elNode != null && elNode.IsConsuming && IsTerraforming;

        private void CheckExplainTerraforming() {
            if (isTerraformingExplained) { return; }
            // We have active bridge controls
            if (!S.Prefs.GetBool(Pref.TerraformingExplained,
                    PrefH.TerraformingExplained, false)) {
                if (Ready.Gen && G.ClockSpeed < ClockSpeed.Unlimited) {
                    S.Prefs.SetBool(Pref.TerraformingExplained, true);
                    Coroutines.Start(ExplainTerraforming(), this,
                        "ExplainTerraforming", false, false);
                }
            }
            isTerraformingExplained = true;
        }
    }
}
