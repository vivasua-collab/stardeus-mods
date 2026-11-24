using Game.Constants;
using Game.Systems.Areas;
using Game.Systems.Path;
using KL.Grid;

namespace Game.Systems.Reg {
    public struct RegPathReq {
        public int FromPos;
        public int ToPos;
        public int GoalWidth;
        public int GoalHeight;
        public RegPathReqFlags Flags;
        public NavPermission Permissions;

        public static int CacheHash(int fromReg, int toReg,
                RegPathReqFlags flags, AreaType toType) {
            // Can't cache nearby requests IF they are for a grounded being in space region,
            // because grounded bots will fail or try invalid construction in space
            if (toType == AreaType.Space || (flags & RegPathReqFlags.GroundedNearby) == RegPathReqFlags.GroundedNearby) {
                return 0;
            }
            return fromReg * Primes.Prime035 + toReg * Primes.Prime056
                + (int) flags * Primes.Prime045;
        }

        public static RegPathReq To(int fromPos, int toPos, bool grounded) {
            return new RegPathReq {
                FromPos = fromPos,
                ToPos = toPos,
                Flags = grounded ? RegPathReqFlags.Grounded : RegPathReqFlags.None,
            };
        }
        public static RegPathReq Near(int fromPos, int toPos, bool grounded,
                int goalW = 0, int goalH = 0) {
            return new RegPathReq {
                FromPos = fromPos,
                ToPos = toPos,
                GoalWidth = goalW,
                GoalHeight = goalH,
                Flags = grounded ? RegPathReqFlags.Nearby | RegPathReqFlags.Grounded
                    : RegPathReqFlags.Nearby,
            };
        }

        public static RegPathReq Create(int from, int to, bool grounded, bool near,
                int goalW = 0, int goalH = 0) {
            RegPathReqFlags f = RegPathReqFlags.None;
            if (grounded) {
                f |= RegPathReqFlags.Grounded;
            }
            if (near) {
                f |= RegPathReqFlags.Nearby;
            }
            return new RegPathReq {
                FromPos = from,
                ToPos = to,
                GoalWidth = goalW,
                GoalHeight = goalH,
                Flags = f,
            };
        }

        public override string ToString() {
            return $"RegPathReq[{Pos.StringFromTo(FromPos, ToPos)}|{Flags}]";
        }
    }
}
