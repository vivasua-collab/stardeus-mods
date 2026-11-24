using Game.Data;
using Game.Commands;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Constants;
using Game.Components;

namespace Game.Commands {
    public class CmdSpawnMat : Command {
        private readonly int pos;
        private readonly float radius;
        private readonly Mat mat;
        private readonly bool skipCheck;
        private readonly bool allowMerge;
        private readonly bool isValid;

        public CmdSpawnMat(Mat mat, int pos, float radius,
                bool skipCheck = false, bool allowMerge = true) : base("spawn_mat") {
            if (pos == 0 || Pos.IsOOB(pos)) {
                D.Err("Refusing to spawn material in invalid pos! {0} -> {1}", pos, mat);
                isValid = false;
                return;
            }
            isValid = EntityUtils.ValidateMat(mat);
            if (!isValid) {
                D.Err("Trying to spawn an invalid mat: {0}", mat);
            } else {
                this.pos = pos;
                this.radius = radius;
                this.mat = mat;
                this.skipCheck = skipCheck;
                this.allowMerge = allowMerge;
            }
        }
        public CmdSpawnMat(MatType matType, int stackSize, int pos, float radius,
                bool skipCheck = false, bool allowMerge = true) : this(
                    new Mat {
                        Type = matType,
                        StackSize = stackSize,
                        MaxStackSize = stackSize
                    }, pos, radius, skipCheck, allowMerge) {
        }

        protected override void Execute() {
            if (isValid) {
                EntityUtils.SpawnRawMaterial(mat, pos, radius, skipCheck, allowMerge);
            }
        }
    }
}
