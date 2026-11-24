using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using Game.Constants;
using KL.Utils;
using Game.Commands;
using KL.Randomness;
using Game.Utils;
using Game.CodeGen;
using System;

namespace Game.Procgen {
    public class CaptureShipPlanGenStage : BaseShipGenStage {
        public override bool SkipRNG => true;
        public override void Apply() {
            spec.ForEachTile(AddToPlan, 1);
        }

        private void AddToPlan(int posIdx) {
            var plan = S.Map.Plan.Data;
            var wall = EntityUtils.WallAt(posIdx);
            if (wall == null) { return; }
            if (wall.Door != null) { return; }
            // if (wall.Definition.Id.Contains("Reinforced")) {
            if (IsOuterWall(posIdx)) {
                // Outer wall
                plan[posIdx] = 2;
            } else {
                // Inner wall
                plan[posIdx] = 1;
            }
        }
    }
}
