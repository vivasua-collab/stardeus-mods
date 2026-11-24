using System;
using System.Collections.Generic;
using Game.Components;
using Game.Data.Space;
using Game.Systems.Space;
using KL.Utils;

namespace Game.Systems.Ships {
    public sealed class NPCShipMemory {
        /// <summary>
        /// The sector where this ship has originally spawned
        /// </summary>
        public SpaceSector OriginSector;
        public SpaceSector TargetSector;
        public SpaceSector PreviousSector;
        public SpaceRegion PreviousRegion;
        /// <summary>
        /// List of sectors we are going through. AKA Route.
        /// </summary>
        public Queue<SpaceSector> SectorPath;
        public Queue<SpaceRegion> RegionPath;
        public SpaceObject FollowTarget;
        public Action<NPCShip, SpaceObject> OnReachFollowTarget;
        public Action<NPCShip, SpaceObject> OnReachFollowTargetFail;
        public bool IsNearFollowTarget;
        public long LastN2PInteractionTick;
        private ComponentData custom;
        public uint ChaseTargetId;
        public bool HasCustom;
        private readonly NPCShip ship;

        public NPCShipMemory(NPCShip ship) {
            this.ship = ship;
        }

        public ComponentData Custom {
            get {
                custom ??= new();
                HasCustom = true;
                return custom;
            }
            set {
                HasCustom = true;
                custom = value;
            }
        }
        public bool TryCustomEnum<T>(string key, out T val) where T : Enum {
            if (TryCustom(key, out int valInt)) {
                val = Enums.From<T>(valInt);
                return true;
            }
            val = default;
            return false;
        }
        public bool TryCustom(string key, out long val) {
            if (!HasCustom) {
                val = 0L;
                return false;
            }
            return Custom.TryGetLong(key, out val);
        }
        public bool TryCustom(string key, out int val) {
            if (!HasCustom) {
                val = 0;
                return false;
            }
            return Custom.TryGetInt(key, out val);
        }
        public bool TryCustom(string key, out bool val) {
            if (!HasCustom) {
                val = false;
                return false;
            }
            return Custom.TryGetBool(key, out val);
        }
        public bool SetFollowTarget(SpaceObject target) {
            if (FollowTarget != target) {
                // D.Warn("Setting follow target: {0} -> {1}", ship, target);
                FollowTarget = target;
                IsNearFollowTarget = false;
                return true;
            } else {
                if (IsNearFollowTarget) {
                    // to retrigger "on reach"
                    IsNearFollowTarget = false;
                    return true;
                }
            }
            return false;
        }

        public override string ToString() {
            return $"NPCShipMemory[Origin:{OriginSector},Chase:{ChaseTargetId},Target:{TargetSector},SPath:{D.DumpCollection(SectorPath)},RPath:{D.DumpCollection(RegionPath)}]";
        }
    }
}
