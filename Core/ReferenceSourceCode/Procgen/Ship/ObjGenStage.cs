using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using Game.Constants;
using KL.Utils;
using Game.Commands;
using System.Linq;
using Game.Components;

namespace Game.Procgen {
    public class ObjGenStage : BaseShipGenStage {
        private readonly List<Def> objects;
        public ObjGenStage(List<Def> things) {
            this.objects = things;
        }
        public ObjGenStage(string[] things) {
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
        public override void Apply() {
            var things = new List<Def>();
            var subRng = rng.Fork();

            foreach (var def in objects) {
                things.Add(def);
            }

            things = things.OrderBy(s => s.IdH)
                .ThenBy(_ => subRng.Value).ToList();

            foreach (var thing in things) {
                int attempts = 10;
                while (attempts-- > 0) {
                    var posIdx = spec.HasGenArea ?
                        S.Sys.Areas.GenRandomSolidPosWithin(subRng, spec.GenArea) :
                        S.Sys.Areas.GenRandomSolidPos(subRng);
                    if (EntityUtils.FloorAt(posIdx)?.Integrity.CanHaveFullIntegrity == true) {
                        if (PlaceObject(posIdx, 3, thing)) {
                            break;
                        }
                    }
                }
            }
        }

        private bool PlaceObject(int startPos, int distance, Def def) {
            int objectPoint = 0;
            Pos.ForEachAroundRandom(rng, startPos,
                    distance, false, true, (n) => {
                if (EntityUtils.IsClearFloorAt(n)
                        && (!spec.HasCPU || Pos.SqrDistance(n, spec.CPUPos) > 8 * 8)) {
                    objectPoint = n;
                    return false;
                }
                return true;
            });
            if (objectPoint == 0) {
                return false;
            }

            var cmd = new CmdSpawnObj(EntityUtils.CenterOf(objectPoint), def, true);
            cmd.Execute(S);
            // D.Warn("Spawned: {0}", cmd.Obj);
            return cmd.Obj != null;
        }
    }
}
