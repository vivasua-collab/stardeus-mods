using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;

namespace Game.Components {
    /// <summary>
    /// Feeds a colonist directly from a piped nutrient supply. Skips the whole
    /// cooking / hauling / dining chain at a heavy morale cost. Rogue AI tech.
    /// </summary>
    public sealed class TubeFeederComp : BaseSlotsComp<TubeFeederComp>, IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TubeFeederComp());
        }
        private const float DefaultEnergyCost = 5f;
        public override int SlotDesignation => SlotDesignationH.TubeFeeder;

        private RefillableStorageComp storage;
        public RefillableStorageComp Storage => storage;
        private ElectricNodeComp elNode;
        private float energyCost;

        public override bool IsAvailable {
            get {
                if (!base.IsAvailable) { return false; }
                if (storage == null || storage.IsEmpty) { return false; }
                return elNode?.IsConsuming == true;
            }
        }

        public override bool IsCompatibleWith(BeingData data) {
            // Only living biologicals have a digestive tract to pump into.
            if (!data.Species.IsBiological) { return false; }
            return storage != null && !storage.IsEmpty;
        }

        public bool Consume(float amt) {
            if (storage == null) { return false; }
            if (storage.TryConsume(amt)) { return true; }
            if (storage.IsConfigured) {
                storage.CreateOrUpdateRefillGoal();
            }
            return false;
        }

        protected override void OnConfig() {
            OnConfigSlots();
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override void OnLateReady(bool wasLoaded) {
            storage = entity.GetComponent<RefillableStorageComp>();
            if (wasLoaded && Tile.IsConstructed) {
                CreateBeingSlots(IconId.CFeeding, T.ObjectsDevicesTubefeeder);
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                CreateBeingSlots(IconId.CFeeding, T.ObjectsDevicesTubefeeder);
            } else if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp node) {
                    elNode = node;
                    // The pump runs continuously - a feeder that only drew power
                    // while in use could never report itself as available, since
                    // availability is what makes a colonist walk over in the
                    // first place.
                    elNode.SetConsumerWantedInput(energyCost);
                }
            }
        }

        public override void OnRemove() {
            RemoveSlots();
        }

        public override string ToString() {
            return " * TubeFeeder";
        }
    }
}
