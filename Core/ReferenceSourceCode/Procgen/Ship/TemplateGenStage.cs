using KL.Grid;
using Game.Data;
using System.Linq;
using Game.Commands;

namespace Game.Procgen {
    public class TemplateGenStage : BaseShipGenStage {
        private Def def;
        public override bool RequiresAreas => true;
        private bool mustBeIndoors;
        private bool mustBeOutdoors;
        public TemplateGenStage(string template) {
            def = The.Defs.Get(template);
        }
        public TemplateGenStage Indoors() {
            mustBeIndoors = true;
            return this;
        }
        public TemplateGenStage Outdoors() {
            mustBeOutdoors = true;
            return this;
        }
        public override void Apply() {
            var subRng = rng.Fork();
            var buildSubRng = subRng.Fork();
            var area = S.Sys.Areas;
            var solidArea = area.SolidArea.OrderBy(_ => subRng.Value).ToList();
            while (solidArea.Count > 0) {
                var posIdx = solidArea[0];
                solidArea.RemoveAt(0);
                if (!IsWithinGenArea(posIdx)) { continue; }
                if (mustBeIndoors && area.IsOutside(posIdx)) {
                    continue;
                }
                if (mustBeOutdoors && !area.IsOutdoors(posIdx)) {
                    continue;
                }
                if (Pos.SqrDistance(posIdx, spec.CPUPos) < 10 * 10) {
                    continue;
                }
                if (TryBuildTemplate(posIdx, def)) {
                    break;
                }
            }
        }

    }
}
