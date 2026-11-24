using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Components {
    public sealed class SurfaceComp : BaseSlotsComp<SurfaceComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new SurfaceComp());
        }
        private int slotDesignation;
        private bool isForBeings;
        public override int SlotDesignation => slotDesignation;

        protected override void OnConfig() {
            var slotDesignationName = Config.GetString(PropertyIdH.Designation, "Surface");
            isForBeings = Config.GetBool(PropertyIdH.IsForBeings, false);
            slotDesignation = Hashes.S(slotDesignationName);
            OnConfigSlots();
        }

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded && Tile.IsConstructed) {
                CreateSlots();
            }
        }

        private void CreateSlots() {
            if (isForBeings) {
                CreateBeingSlots(IconId.CInfo, T.SurfaceSlots);
            } else {
                CreateObjSlots(IconId.CInfo, T.SurfaceSlots);
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                CreateSlots();
            }
        }

        public override void OnRemove() {
            RemoveSlots();
        }

        public override string ToString() {
            return $" * Surface [{Entity.DefinitionId}]";
        }
    }
}
