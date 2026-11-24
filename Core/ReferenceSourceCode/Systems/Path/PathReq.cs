using System;
using System.Runtime.CompilerServices;
using KL.Grid;

namespace Game.Systems.Path {
    public struct PathReq {
        // Reference id
        public uint Id;
        // Who initiated the request
        public int BeingId;
        // Origin position index
        public int PosFrom;
        // Target position index
        public int PosTo;
        public PathReqFlags Flags;
        // Can go through hazards
        // If target is a tile, we will pass the width and height
        // so we'll know when we are adjacent to the tile
        public int GoalWidth;
        public int GoalHeight;
        public NavPermission Permissions;
        // Army bits whose fighters block this walker (Army.HostileBodyBits), 0 outside a fight
        public int HostileBodies;
        // Min tolerable temperature in Kelvin
        public float MinTemp;
        // Max tolerable temperature in Kelvin
        public float MaxTemp;
        // Min required oxygen amount
        public float MinO2;
        // public bool UnsafeOK => (Flags & PathReqFlags.UnsafeOK) == PathReqFlags.UnsafeOK;
        // // If this is set, we want to go on top of the PosTo
        // public bool AdjOnTop => (Flags & PathReqFlags.AdjOnTop) == PathReqFlags.AdjOnTop;
        // // If this is set, we are OK with going near the PosTo (expanded by PosWidth / PosHeight)
        // public bool AdjNearby => (Flags & PathReqFlags.AdjNearby) == PathReqFlags.AdjNearby;

        public override string ToString() {
            return $"PathReq#{Id}[{Pos.String(PosFrom)}->{Pos.String(PosTo)},Flags:{Flags},MinO2:{MinO2},Temp:{MinTemp}-{MaxTemp},BeingId:{BeingId}]";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasFlag(PathReqFlags flag) {
            return (Flags & flag) == flag;
        }
    }
}
