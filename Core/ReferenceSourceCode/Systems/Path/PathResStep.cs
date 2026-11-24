using System.Collections.Generic;

namespace Game.Systems.Path {
    public struct ResStepComparer : IComparer<PathResStep> {
        public int Compare(PathResStep x, PathResStep y) {
            if (x.Index > y.Index) {
                return -1;
            } else if (x.Index < y.Index) {
                return 1;
            } else {
                return 0;
            }
        }
    }
    public struct PathResStep {
        public PathResStep(ushort idx, int pos) {
            Index = idx;
            PosIdx = pos;
        }
        public readonly ushort Index;
        public readonly int PosIdx;
    }
}
