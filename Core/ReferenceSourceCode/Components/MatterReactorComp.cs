using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.UIUX;
using KL.Utils;
using UnityEngine;
using Game.Systems.Firex;

namespace Game.Components {
    public sealed class MatterReactorComp : BaseComponent<MatterReactorComp>,
            IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new MatterReactorComp());
        }
        private const int DefaultMaxRate = 15;

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var max = config.GetInt(PropertyIdH.MaxRate, DefaultMaxRate);
            power.AddProduction(max, max);
        }

        public float UpgradeEfficiency = 1f;
        private float baseEfficiency;
        private int maxRate;
        private float maxRateAdj => maxRate * Tile.Health.HealthPercent;
        private LightSourceComp Light;
        public RefillableStorageComp Fuel;
        public override void OnLateReady(bool wasLoaded) {
            if (Light == null) {
                if (Entity.TryGetComponent<LightSourceComp>(out Light)) {
                    Light.SwitchLight(false);
                } else {
                    D.Err("LightSource component is required on matter reactors! {0}",
                        Entity.DefinitionId);
                }
            }
            if (Fuel != null) { return; }
            if (!Entity.TryGetComponent(out Fuel)) {
                var msg = "MatterReactor has no RefillableStorageComp on the entity!";
                D.Err(msg);
                WPopup.SystemWarning("GAME BREAKING BUG", msg);
            }
        }

        protected override void OnConfig() {
            maxRate = Config.GetInt(PropertyIdH.MaxRate, DefaultMaxRate);
            baseEfficiency = Config.GetFloat(PropertyIdH.Efficiency, 1f);
        }
        public override string ToString() {
            return " * MatterReactor";
        }
        private bool isConfigured;
        private ElectricNodeComp elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp enode) {
                    elNode = enode;
                    enode.AddAfterTick(this, AfterTickGrid);
                    if (isConfigured && !Fuel.IsEmpty) {
                        enode.SetProducerMaxOutput(maxRateAdj);
                    }
                }
            } else if (message == MsgIdH.HealthWentToZero) {
                var diff = S.Situation.Difficulty;
                if (isConfigured && !Fuel.IsEmpty) {
                    if (Explosions.CanExplode(Mathf.Clamp(0.01f, 0.5f, diff / 2f))) {
                        Tile.Flammable.CreateExplosion(true);
                    } else {
                        Explosion.Create(S, Tile.Flammable.ExplosionLevel,
                            position: Entity.Position,
                            delay: 1,
                            fireChance: 0.95f,
                            chemFuelAmt: 0.05f,
                            flags: ExplosionFlags.FireOnly);
                    }
                }
            }
        }

        private void AfterTickGrid() {
            if (!elNode.IsPowerable) { return; }
            // D.Warn("reactor after tick grid: ");
            isConfigured = Fuel.CheckIfConfigured();
            if (!isConfigured) {
                elNode.SetProducerMaxOutput(0f);
                Light.SwitchUnchecked(false);
                return;
            }
            if (Fuel.IsEmpty) {
                Fuel.NotifyLack();
                Light.SwitchUnchecked(false);
                Fuel.CreateOrUpdateRefillGoal();
                elNode.SetProducerMaxOutput(0f);
                return;
            }

            // we're producing
            Fuel.ClearDeficitFlag();
            var mr = maxRateAdj;
            if (mr < 0.0001f) {
                D.Err("MaxRate for matter reactor is 0! {0}", Tile);
                elNode.SetProducerMaxOutput(0f);
                Fuel.ClearBurnRate();
                return;
            }
            elNode.SetProducerMaxOutput(mr);

            var produced = elNode.Node.Produced;
            if (produced < 0.00001f) {
                Light.SwitchLight(false);
                Fuel.ClearBurnRate();
                return;
            }

            // How efficient is our fuel
            var materialOutput = Fuel.MatType.EnergyOutput
                * UpgradeEfficiency * baseEfficiency;
            var consumedStock = produced / materialOutput;

            consumedStock *= Tunables.MatterReactorConsumptionRate;
            Fuel.TryConsume(consumedStock);
            Light.SwitchUnchecked(true);

            Fuel.CreateOrUpdateRefillGoal();
        }
    }
}
