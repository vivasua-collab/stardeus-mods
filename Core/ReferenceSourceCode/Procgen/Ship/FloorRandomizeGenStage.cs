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
    public class FloorRandomizeGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;

        private new Rng rng;
        public override void Apply() {
            rng = spec.CosmeticRng.Fork();
            var allAreas = S.Sys.Areas.Areas;
            var outline = new HashSet<int>();

            // sort rooms by size, biggest to smallest
            foreach (var area in allAreas) {
                if (!IsWithinGenArea(area.Value.First())) { continue; }
                if (rng.Chance(0.5f)) { continue; }
                S.Sys.Areas.OutlineOfArea(area.Key, outline);
                RandomizeFloors(area.Key, area.Value, outline);
            }
        }

        private string sourceDef;
        private List<Def> targetDefs;
        private Def newDef;

        private void RandomizeFloors(int areaId, HashSet<int> area,
                HashSet<int> outline) {
            if (area.Count == 0) { return; }
            sourceDef = null;
            targetDefs = null;
            foreach (var p in area) {
                RebuildFloor(p);
            }
            foreach (var p in outline) {
                RebuildFloor(p);
            }
        }

        private void RebuildFloor(int p) {
            if (!EntityUtils.IsWithinBounds(p)) { return; }
            var nowTile = EntityUtils.FloorAt(p);

            if (nowTile == null) {
                #if !PRODUCTION_BUILD
                D.ErrSoft("No Floor when it should be, issue with area sys! {0}", Pos.String(p));
                The.ScreenBlock.HideImmediate();
                EntityUtils.CameraFocusOn(p);
                #endif
                return;
            }
            if (nowTile.DefinitionId.EndsWith("Lit")) {
                return;
            }
            if (sourceDef == null) {
                sourceDef = nowTile.DefinitionId;
                targetDefs = The.Defs.Matching(sourceDef);
                newDef = rng.From(targetDefs);
            }
            if (newDef.Id == sourceDef) { return; }
            if (!Definitions.AreMatching(nowTile.DefinitionId, newDef.Id)) {
                return;
            }
            var proto = EntityUtils.CreatePrototype<Tile>(newDef.Id);
            if (nowTile.GenReplaceWith(S, proto)) {
                EntityUtils.TriggerChange(nowTile);
            }
        }
    }
}
