using UnityEngine;

namespace Game.Components {
    public interface IForceFieldCollider : IComponent {
        Vector2 Position { get; }
        float Size { get; }
        float Altitude { get; }
        // Shields stop what crosses them, not what is already inside the bubble
        bool IgnoresForceField { get; set; }
        /// <param name="softFlux">soft flux to add to player ship</param>
        /// <param name="hardFlux">hard flux to add to player ship</param>
        bool HitForceField(Vector3 hitPos, out float softFlux, out float hardFlux);
    }
}
