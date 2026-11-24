using System.Collections;
using Game.Commands;
using KL.Grid;

namespace Game.Procgen {
    public class ShipRotateGenStage : BaseShipGenStage {
        public override bool IsAsync => true;
        public override bool RequiresAreas => true;
        private readonly sbyte dir;
        private readonly bool wholeMap;

        public ShipRotateGenStage(bool wholeMap, sbyte dir) {
            this.wholeMap = wholeMap;
            this.dir = dir;
        }

        public override IEnumerator ApplyAsync() {
            int sectionId = wholeMap ? 0 : S.Sys.Areas.IslandAt(spec.Bounds.CenterPos);
            // Turned around the middle of its area, not of its hull: the area fits its no-build
            // corner whichever way it faces, but a hull off-center in it turned out of the
            // corner and into the player's build space (STA-1924)
            var center = wholeMap ? 0
                : spec.HasGenArea ? spec.GenArea.CenterPos
                : spec.Bounds.CenterPos;
            var cmd = new CmdRotateSection(sectionId, center, dir, true);
            cmd.IsGenStage = true;
            cmd.ShipSpec = spec;
            cmd.WithState(S);
            yield return cmd.ExecuteAsync();
            if (wholeMap) { yield break; }
            // The gen area and the bounds turn with the ship. A 90 degree turn of a non-square
            // derelict put it outside its own area, and every later stage looked there: the
            // stasis split missed the array and logged an error, and loot and damage hit
            // empty space. Both turn around the same center as the ship, so a second step
            // keeps them on it.
            if (TryRotate(spec.Bounds, center, out var bounds)) {
                spec.SetRotatedBounds(bounds);
            }
            if (spec.HasGenArea && TryRotate(spec.GenArea, center, out var area)) {
                spec.SetGenArea(area);
            }
        }

        private bool TryRotate(PosRect rect, int center, out PosRect rotated) {
            rotated = rect;
            if (!Pos.TryRotateAround(center, rect.Min, true, dir, out var a, out _, out _)) {
                return false;
            }
            if (!Pos.TryRotateAround(center, rect.Max, true, dir, out var b, out _, out _)) {
                return false;
            }
            rotated = new PosRect(a, b);
            return true;
        }
    }
}
