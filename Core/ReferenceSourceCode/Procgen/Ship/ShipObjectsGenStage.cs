using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;

namespace Game.Procgen {
    public class ShipObjectsGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        public override void Apply() {
            // 1. have a list of stuff we want to build
            // 2. find good locations
            // 3. try to build
            var things = new List<string>();

            foreach (var def in The.Defs.Defs) {
                if (!def.Value.Id.StartsWith("Templates/")) { continue; }
                var config = def.Value.ComponentConfigFor(ComponentIdH.Template);
                var chance = config.GetFloat(PropertyIdH.Chance, 0.5f);
                if (!rng.Chance(chance)) { continue; }
                var minInstances = config.GetInt(PropertyIdH.MinInstances, 1);
                var maxInstances = config.GetInt(PropertyIdH.MaxInstances, 2);
                var instances = rng.Range(minInstances, maxInstances + 1);
                while (instances-- > 0) {
                    things.Add(def.Value.Id);
                }
            }

            foreach (var thing in things) {
                var posIdx = S.Sys.Areas.GenRandomSolidPos(rng);
                //if (IsGoodLocation(posIdx)) {
                    BuildObject(posIdx, 3, thing);
                //}
            }
        }

        private bool BuildObject(int startPos, int distance, string objType) {
            int objectPoint = 0;
            Pos.ForEachAroundRandom(rng, startPos,
                    distance, false, true, (n) => {
                if (IsGoodObjectLocation(n)) {
                    objectPoint = n;
                    return false;
                }
                return true;
            });

            var proto = EntityUtils.CreatePrototype<Tile>(objType);
            // TODO gen rotation
            BuildTile(objectPoint, proto);
            var obj = EntityUtils.ObjAt(objectPoint);
            return obj != null;
        }
    }
}
