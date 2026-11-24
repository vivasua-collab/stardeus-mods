using Game.Systems.Creatures;

namespace Game.Systems.AI {
    public struct NeedState {
        public AINeed Need;
        public BeingData Data;
        public override string ToString() {
            return $"NeedState[{Need.Id}->{Data}]";
        }
    }
}
