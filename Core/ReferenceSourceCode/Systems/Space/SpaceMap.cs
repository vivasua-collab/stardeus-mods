using System.Collections.Generic;
using Game.Constants;
using Game.Data.Space;
using Game.Utils;

namespace Game.Systems.Space {
    public sealed class SpaceMap {
        public List<SpaceRegion> Regions;
        public SpaceObject SO;
        public GenSpaceParams GenParams;
        public string StatsText {
            get {
                int totalSectors = 0;
                foreach (var reg in Regions) {
                    totalSectors += reg.Sectors.Count;
                }
                return T.SpaceStatsLine_V2.F(totalSectors, Regions.Count);
            }
        }

        public void LoadDeposits(Dictionary<MatType, int> mats) {
            mats.Clear();
            foreach (var reg in Regions) {
                foreach (var sec in reg.Sectors) {
                    foreach (var dep in sec.Biome.Deposits) {
                        if (!mats.TryGetValue(dep.Key.Mat, out var cnt)) {
                            cnt = 0;
                        }
                        mats[dep.Key.Mat] = cnt + dep.Value;
                    }
                }
            }
        }

        public bool TryFindSector(int sectorId, out SpaceSector sector) {
            for (int i1 = Regions.Count - 1; i1 >= 0; i1--) {
                var reg = Regions[i1];
                for (int i = reg.Sectors.Count - 1; i >= 0; i--) {
                    var sec = reg.Sectors[i];
                    if (sec.Id == sectorId) {
                        sector = sec;
                        return true;
                    }
                }
            }
            sector = null;
            return false;
        }
    }
}
