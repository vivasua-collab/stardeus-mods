using Game.Data;

namespace Game.Components {
    public interface IWorkoutProvider : IComponent, ISlots {
        int WorkSpot { get; }
        void BeginWorkout(Being worker);
        bool DoWorkout(Being worker);
        void EndWorkout(Being worker);
    }
}
