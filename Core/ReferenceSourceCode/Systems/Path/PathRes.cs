using KL.Grid;

namespace Game.Systems.Path {
    public struct PathRes {
        // Reference id
        public uint ReqId;
        public int BeingId;
        private PathResFlags flags;
        public bool IsValid {
            get => (flags & PathResFlags.Valid) != 0;
            set => SetFlag(PathResFlags.Valid, value);
        }
        public bool IsSafe {
            get => (flags & PathResFlags.Safe) != 0;
            set => SetFlag(PathResFlags.Safe, value);
        }
        // The path steps on tiles that are unsafe for the walker, though the request did not
        // allow it: the escape from a start with no safe neighbor (FastPathfindJob). IsSafe stays
        // true then, because the preview and reachability checks read it
        public bool CrossesUnsafe {
            get => (flags & PathResFlags.CrossesUnsafe) != 0;
            set => SetFlag(PathResFlags.CrossesUnsafe, value);
        }
        public int Start;
        public int Goal;
        public int Length;
        // How hard was it to calculate
        public int Iterations;
        public override string ToString() {
            return $"Path#{ReqId}[{Pos.String(Start)}->{Pos.String(Goal)},Valid:{IsValid},Safe:{IsSafe},CrossesUnsafe:{CrossesUnsafe},Len:{Length},B:{BeingId},Iter:{Iterations}]";
        }
        private void SetFlag(PathResFlags flag, bool on) {
            if (on) {
                flags |= flag;
            } else {
                flags &= ~flag;
            }
        }
        public static PathRes InvalidResultFor(in PathReq req, int iterations) {
            return new PathRes {
                BeingId = req.BeingId,
                Iterations = iterations,
                Goal = req.PosTo,
                Start = req.PosFrom,
                IsSafe = false,
                IsValid = false,
                Length = 0,
                ReqId = req.Id,
            };
        }
    }

    [System.Flags]
    internal enum PathResFlags : byte {
        Valid = 1 << 0,
        Safe = 1 << 1,
        CrossesUnsafe = 1 << 2,
    }
}
