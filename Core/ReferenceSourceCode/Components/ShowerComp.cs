using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using KL.Utils;

using Game.Utils;
namespace Game.Components {
    public sealed class ShowerComp : BaseSlotsComp<ShowerComp>, IRareTickListener {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ShowerComp());
        }
        public override int SlotDesignation => SlotDesignationH.Shower;

        public RefillableStorageComp Water;
        public float waterConsumptionPerTick;
        private CleanableComp cleanable;
        public override bool IsAvailable {
            get {
                if (!base.IsAvailable) { return false; }
                if (Water != null && Water.IsEmpty) { return false; }
                if (cleanable != null && cleanable.IsFull) {
                    if (!cleanable.HasAnyGoal) {
                        cleanable.CreateCleaningGoal(false);
                    }
                    return false;
                }
                return true;
            }
        }

        protected override void OnConfig() {
            var perDay = Config.GetFloat(PropertyIdH.WaterUsePerDay, 10f);
            waterConsumptionPerTick = perDay / Consts.TicksPerDay;
            OnConfigSlots();
        }

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded && Tile.IsConstructed) {
                CreateBeingSlots(IconId.CShower, T.Shower);
            }
            if (!Entity.TryGetComponent(out Water)) {
                D.Err("Shower requires a RefillableStorage comp");
            }
            if (!Entity.TryGetComponent(out cleanable)) {
                D.Err("Shower requires a Cleanable comp");
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                CreateBeingSlots(IconId.CShower, T.Shower);
            }
        }
        // one use is 30 minutes, it will be fully overflowing after X uses
        // it was 10 dirt capacity, now it's 3, thus 0.3f
        private const float dirtAccumulationPerTick = 0.3f / Consts.TicksPer30Minutes;

        public override bool Use(BeingData being, Slot slot) {
            // D.Warn("Trying to consume {0} water", waterConsumptionPerTick);
            if (!Water.TryConsume(waterConsumptionPerTick)) {
                Water.NotifyLack();
                Water.CreateOrUpdateRefillGoal();
                return false;
            }
            var excess = cleanable.AddDirt(dirtAccumulationPerTick);
            return excess == 0f;
        }

        public override void OnRemove() {
            RemoveSlots();
        }

        public override string ToString() {
            return " * Shower";
        }

        public void RareTick(long currentTick) {
            if (Water.IsEmpty && IsUsable) {
                Water.CreateOrUpdateRefillGoal();
            }
        }
    }
}
