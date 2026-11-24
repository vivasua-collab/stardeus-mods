using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.Creatures;

namespace Game.Components {
    public sealed class PetFoodSourceComp : BaseSlotsComp<PetFoodSourceComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new PetFoodSourceComp());
        }
        public override int SlotDesignation => SlotDesignationH.FoodSource;

        private RefillableStorageComp storage;
        // For tests
        public RefillableStorageComp Storage => storage;
        private void UpdateBowlGraphics() {
            Tile.Graphics.SetAnimationFrame(storage.CurrentStock > 0.1f ? 1 : 0);
        }

        private void RestoreFood() {
            S.Sig.AfterLoad.RemoveListener(RestoreFood);
            UpdateBowlGraphics();
        }

        public override void OnLateReady(bool wasLoaded) {
            storage = entity.GetComponent<RefillableStorageComp>();
            storage.OnStockChange = OnStockChange;
            if (wasLoaded && Tile.IsConstructed) {
                CreateBeingSlots(null, null);
            }
            S.Sig.AfterLoad.AddListener(RestoreFood);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                CreateBeingSlots(null, null);
            }
        }

        public bool Consume(float amt) {
            if (storage.TryConsume(amt)) {
                return true;
            } else {
                if (storage.IsConfigured) {
                    storage.CreateOrUpdateRefillGoal();
                }
                UpdateBowlGraphics();
                return false;
            }
        }

        private void OnStockChange(float amt) {
            UpdateBowlGraphics();
        }

        public override void OnRemove() {
            RemoveSlots();
        }
        protected override void OnConfig() {
            OnConfigSlots();
        }

        public override string ToString() {
            return " * PetFoodSource";
        }

        public override bool IsCompatibleWith(BeingData data) {
            return data.Mind.Species.IsAnimal && storage.CurrentStock > 0;
        }
    }
}
