using Game.Data;
using Game.Commands;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Constants;

namespace Game.Commands {
    public class CmdSpawnMatByType : Command {
        private readonly int pos;
        private readonly float radius;
        private readonly MatType type;
        private readonly int amt;
        private readonly bool skipCheck;
        private readonly bool allowMerge;
        private readonly bool isValid;
        private readonly bool orderHaul;
        private readonly int haulPriority;

        public CmdSpawnMatByType(MatType type, int amt, int pos, float radius,
                bool skipCheck = false, bool allowMerge = true, bool orderHaul = false,
                int haulPriority = 3) : base("spawn_mat_type") {
            isValid = EntityUtils.ValidateMatType(type);
            if (isValid) {
                this.pos = pos;
                this.radius = radius;
                this.type = type;
                this.amt = amt;
                this.skipCheck = skipCheck;
                this.allowMerge = allowMerge;
                this.orderHaul = orderHaul;
                this.haulPriority = haulPriority;
                // D.Warn("Setting auto haul for mat: {0}: {1}", type, orderHaul);
            } else {
                D.Err("Trying to spawn invalid mat by type! {0}", type);
            }
        }

        protected override void Execute() {
            if (isValid) {
                var pile = EntityUtils.SpawnRawMaterial(type, amt, pos, radius, skipCheck,
                    allowMerge);
                if (orderHaul && !pile.IsForbidden) {
                    // D.Warn("Executing auto haul for mat: {0}: {1}", type, orderHaul);
                    if (pile.TryOrderStore()) {
                        if (haulPriority != 0 && pile.TryGetGoal(out var storeGoal)) {
                            storeGoal.WithPriority(haulPriority);
                        }
                    }
                }
            }
        }
    }
}
