using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using Priority_Queue;
using KL.Collections;
using System.Linq;
using Game.Constants;
using UnityEngine;
using KL.Utils;
using Game.Components;
using KL.Randomness;

namespace Game.Procgen {
    public class FloorWindowGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        public override void Apply() {
            var rng = new Rng(spec.Seed);
            var allAreas = S.Sys.Areas.Areas;
            var outline = new HashSet<int>();

            // sort rooms by size, biggest to smallest
            foreach (var area in allAreas) {
                if (!IsWithinGenArea(area.Value.First())) { continue; }
                if (rng.Chance(0.7f)) { continue; }
                S.Sys.Areas.OutlineOfArea(area.Key, outline);
                RandomizeFloorWindows(area.Key, area.Value, outline, rng);
            }
        }

        private Def newDef;

        private void RandomizeFloorWindows(int areaId, HashSet<int> area,
                HashSet<int> outline, Rng rng) {
            if (area.Count == 0) { return; }
            newDef = null;
            area = PosRect.RemoveOutline(area);
            if (rng.Chance(0.5f)) {
                area = PosRect.RemoveOutline(area);
            }
            if (rng.Chance(0.66f) || area.Count > 180) {
                area = PosRect.KeepOutline(area);
            }
            if (newDef == null) {
                newDef = The.Defs.RandomMatching(DefIds.StructureFloorsFloorGlass01,
                    spec.CosmeticRng);
            }
            foreach (var p in area) {
                RebuildFloor(p, newDef);
            }
            foreach (var p in outline) {
                RebuildFloor(p, newDef);
            }
        }

        protected void RebuildFloor(int p, Def newDef) {
            if (!IsWithinGenArea(p)) { return; }
            if (EntityUtils.IsWallAt(p)) { return; }
            var nowTile = EntityUtils.FloorAt(p);
            if (nowTile.DefinitionId.Contains("Glass") || nowTile.DefinitionId.EndsWith("Lit")) {
                return;
            }
            var proto = EntityUtils.CreatePrototype<Tile>(newDef);
            BuildTile(p, proto);
        }
    }
}
