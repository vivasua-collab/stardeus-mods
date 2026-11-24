using Game.Data;
using Game.Commands;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Commands {
    public class CmdSpawnMats : Command {
        private readonly int pos;
        private readonly bool orderHaul;
        private readonly bool skipConsumedOnConstruct;
        private readonly float radius;
        private readonly Mat[] mats;
        private readonly bool mergeIntoBlock;

        public CmdSpawnMats(Mat[] mats, int pos, float radius,
                bool skipConsumedOnConstruct = false, bool orderHaul = false,
                bool mergeIntoBlock = false) : base("spawn_mats") {
            if (mats == null) {
                D.Err("Trying to spawn null Mat[] array!");
            }
            this.pos = pos;
            this.radius = radius;
            this.mats = mats;
            this.orderHaul = orderHaul;
            this.skipConsumedOnConstruct = skipConsumedOnConstruct;
            this.mergeIntoBlock = mergeIntoBlock;
        }

        protected override void Execute() {
            if (mats == null) { return; }
            for (int i = mats.Length - 1; i >= 0; i--) {
                var mat = mats[i];
                if (mat.StackSize > 0) {
                    if (skipConsumedOnConstruct && mat.Type.IsConsumedOnConstruct) {
                        continue;
                    }
                    var spawned = EntityUtils.SpawnRawMaterial(mat, pos, radius, true, true,
                        mergeIntoBlock);
                    if (orderHaul && spawned != null) {
                        var goal = spawned.CreateStoringGoal(false, false);
                        if (goal != null) {
                            goal.WithPriority(1);
                            spawned.Entity.TrySetGoal(goal);
                        }
                    }
                }
            }
        }
    }
}
