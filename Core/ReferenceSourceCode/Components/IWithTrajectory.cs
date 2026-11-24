using UnityEngine;

namespace Game.Components {
    public interface IWithTrajectory : IComponent {
        bool IsMidFlight { get; }
        bool WasAligned { get; set; }
        bool CanChangeTrajectory { get; }
        float TrajectoryChangeEnergyCost { get; }
        void OnShipMove(Vector2 dir);
        bool InfluenceTrajectory(Vector2 target, float strength);
        bool TryGetHitPoint(out Vector2 hitPoint, out Vector2 velocity);
    }
}
