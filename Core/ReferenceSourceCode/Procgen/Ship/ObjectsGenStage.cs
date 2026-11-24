using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using Game.Constants;
using KL.Utils;
using System.Linq;
using KL.Randomness;
using System.Collections;
using Game.UI;

namespace Game.Procgen {
    public class ObjectsGenStage : BaseShipGenStage {
        public override bool IsAsync => true;
        private readonly List<Def> objects;
        public ObjectsGenStage(List<Def> things) {
            this.objects = things;
        }
        public ObjectsGenStage(string[] things) {
            objects = new();
            foreach (var t in things) {
                var d = The.Defs.TryGet(t);
                if (d != null) {
                    objects.Add(d);
                } else {
                    D.Err("{0} Def not found by id: {1}", LogPrefix, t);
                }
            }
        }
        public override bool RequiresAreas => true;
        public override IEnumerator ApplyAsync() {
            // 1. have a list of stuff we want to build
            // 2. find good locations
            // 3. try to build
            var things = new List<Def>();

            foreach (var def in objects) {
                if (def == null) {
                } else {
                    things.Add(def);
                }
            }

            var subRng = rng.Fork();
            things = things.OrderBy(s => s.IdH)
                .ThenBy(_ => subRng.Value).ToList();

            var buildSubRng = subRng.Fork();
            var solidArea = S.Sys.Areas.SolidArea.OrderBy(_ => subRng.Value).ToList();
            for (int i = 0; i < things.Count; i++) {
                while (solidArea.Count > 0) {
                    var posIdx = solidArea[0];
                    solidArea.RemoveAt(0);
                    if (!IsWithinGenArea(posIdx)) { continue; }
                    if (BuildObject(posIdx, things[i], buildSubRng)) {
                        break;
                    }
                }
                yield return null;
                if (!GameState.IsCurrent(S)) { yield break; }
            }
        }

        private bool BuildObject(int startPos, Def def, Rng rng) {
            if (Pos.SqrDistance(startPos, spec.CPUPos) < 25 * 25) {
                return false;
            }
            if (!IsGoodObjectLocation(startPos)) {
                return false;
            }

            if (def.IsTemplate) {
                return TryBuildTemplate(startPos, def);
            }

            var proto = EntityUtils.CreatePrototype<Tile>(def);
            proto.Transform.Rotation = proto.Transform.RandomValidRotation(rng);
            BuildTile(startPos, proto);
            var obj = EntityUtils.ObjAt(startPos);
            if (obj != null) {
                // This is the wrong object
                if (obj.Transform.IsChild || obj.DefinitionId != def.Id) {
                    return false;
                }
                return true;
            }
            return false;
        }
    }
}
