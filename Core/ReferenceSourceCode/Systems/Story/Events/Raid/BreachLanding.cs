using Game.Data;
using KL.Grid;

namespace Game.Systems.Story {
    // Where a breach capsule lands: close to the crew, but never right by an important object
    // (the ship computers, the shuttle, the stasis array). Raiders dropped next to the Ship
    // Computer destroy it before the crew can answer, and its blast downs everyone around it.
    public static class BreachLanding {
        // Each failed try lowers the distance by one, down to minTooCloseDist. Returns false
        // when no try was far enough; the target is then the last one tried.
        public static bool TryFind(int tooCloseDist, int minTooCloseDist, out int target) {
            EntityUtils.LoadImportantTilesCache();
            target = Pos.Invalid;
            for (int attempt = 0; attempt < 10; attempt++) {
                target = EntityUtils.FindLandingTargetForBreach();
                if (!EntityUtils.IsTooCloseToAnyOf(target, tooCloseDist, Caches.TileList)) {
                    return true;
                }
                if (tooCloseDist > minTooCloseDist) {
                    tooCloseDist--;
                }
            }
            return false;
        }
    }
}
