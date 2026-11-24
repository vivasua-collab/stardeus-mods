using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using System;

namespace Game.Procgen {
    public class ReduceEnginesGenStage : BaseShipGenStage {
        public override void Apply() {
            var engines = S.Components.GenFindAllMatching<ShipEngineComp>(
                    e => e.Tile.IsConstructed)?.ToList();
            if (engines == null || engines.Count == 0) {
                // at this points all engines may have been destroyed by other
                // destruction stages, so it's ok
                D.Warn("No engines in reduce engines gen stage! {0}", spec);
                return;
            }
            while (engines.Count > 1) {
                var engine = rng.From(engines);
                engines.Remove(engine);
                engine.Tile.Constructable.BreakDown(0.5f, false, true, true);
            }
        }
    }
}
