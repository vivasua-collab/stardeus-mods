using KL.Grid;
using UnityEngine;

namespace Game.Visuals {
    [DisallowMultipleComponent]
    public sealed class OwnedStationaryVisualEffect : MonoBehaviour {
        public int OwnerStartPos;
        public ParticlesVFX VFX;
        private void Update() {
            if (VFX.IsDestroyed) { return; }
            var Owner = VFX.Owner;
            if (Owner == null) { return; }
            // string failReason;
            if (!Owner.IsActive) {
                // failReason = "inactive";
            } else if (Pos.SqrDistance(Owner.PosIdx, OwnerStartPos) > 100) {
                // failReason = "too far away";
            } else {
                return;
            }
            // This happens when we run at fast speed and then pause due to particles being destroyed after 5 seconds real time
            // D.Err("Owner is {0} ({4}) but ParticlesVFX is running! Destroying particles: {1}. {2}. {3}. CLock: {4}",
            //     failReason, VFX, Owner, Owner.GetComponent<AIAgentComp>()?.CurrentAction, Pos.String(OwnerStartPos), G.ClockSpeed);
            // EntityUtils.DebugPauseFocusOn(Owner);
            enabled = false;
            VFX.Destroy();
        }
    }
}
