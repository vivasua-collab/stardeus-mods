using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Components {
    public sealed class PoweredStorageComp : BaseComponent<PoweredStorageComp>,
            IDefPowerProvider {
        private float baseEnergy;
        private float storageCost;
        private float energyPerItemStored;
        private IStorageDevice storage;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new PoweredStorageComp());
        }

        // Empty .. full, read from the storage component's MaxCapacity
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var baseCost = config.GetFloat(PropertyIdH.EnergyCostBase, 0f);
            var perItem = config.GetFloat(PropertyIdH.EnergyCostPerItem, 0f);
            if (perItem <= 0f) {
                power.AddConsumption(baseCost);
                return;
            }
            var capacity = StorageCapacity(def);
            power.AddConsumption(baseCost, capacity > 0
                ? baseCost + capacity * perItem
                : float.PositiveInfinity);
        }

        private static int StorageCapacity(Def def) {
            var storageConfig = def.ComponentConfigFor(ComponentIdH.MatStorage, false)
                ?? def.ComponentConfigFor(ComponentIdH.BeingStorage, false)
                ?? def.ComponentConfigFor(ComponentIdH.ObjStorage, false);
            return storageConfig?.GetInt(PropertyIdH.MaxCapacity, 0) ?? 0;
        }

        public override void OnLateReady(bool wasLoaded) {
            if (!Tile.TryGetComponent(out storage)) {
                D.Err("PoweredStorageComp must have a component with IStorageDevice defined in the same entity! {0}",
                    Entity.DefinitionId);
            }
        }

        protected override void OnConfig() {
            baseEnergy = Config.GetFloat(PropertyIdH.EnergyCostBase, 0f);
            energyPerItemStored = Config.GetFloat(PropertyIdH.EnergyCostPerItem, 0f);
            if (baseEnergy == 0 && energyPerItemStored == 0) {
                D.Err("BaseEnergy and EnergyPerItem cannot both be zero! {0}",
                    Entity.DefinitionId);
            }
        }

        public override string ToString() {
            return " * Powered Storage";
        }
        private ElectricNodeComp elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    this.elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                }
            }
        }

        private void AfterTickGrid() {
            if (energyPerItemStored > 0) {
                storageCost = baseEnergy + storage.UsedCapacity * energyPerItemStored;
                elNode.SetConsumerWantedInput(storageCost);
            } else {
                elNode.SetConsumerWantedInput(baseEnergy);
            }
        }
    }
}
