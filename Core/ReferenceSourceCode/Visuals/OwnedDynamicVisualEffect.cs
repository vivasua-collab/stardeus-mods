using System;
using UnityEngine;

namespace Game.Visuals {
    /// <summary>
    /// For things like construction particle effect. When the owner
    /// moves away, the particle effect will either be destroyed,
    /// or move to the owner.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OwnedDynamicVisualEffect : MonoBehaviour {
        public ParticlesVFX VFX;
        public Func<Vector2> PositionFunc;
        private void Update() {
            var owner = VFX.Owner;
            if (owner == null || PositionFunc == null || !owner.IsActive) {
                enabled = false;
                // D.Warn("Destroying dynamic VFX! {0} | {1}", owner, PositionFunc);
                VFX.Destroy();
                return;
            }
            // D.Warn("Moving VFX to {0}", PositionFunc());
            VFX.MoveTo(PositionFunc());
        }
    }
}
