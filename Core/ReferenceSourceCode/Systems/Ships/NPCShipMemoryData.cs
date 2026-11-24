using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Space;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class NPCShipMemoryData {
        [Key("OriginSector")] public int OriginSectorId;
        [Key("TargetSector")] public int TargetSectorId;
        [Key("PreviousSector")] public int PreviousSectorId;
        [Key("PreviousRegion")] public int PreviousRegionId;
        [Key("SectorPath")] public List<int> SectorPathIds;
        [Key("RegionPath")] public List<int> RegionPathIds;
        [Key("FollowTarget")] public int FollowTargetId;
        [Key("LastN2PInteractionTick")] public long LastN2PInteractionTick;
        [Key("ChaseTarget")] public uint ChaseTargetId;
        [Key("IsNearFollowTarget")] public bool IsNearFollowTarget;
        [Key("Custom")] public ComponentData Custom;

        public static NPCShipMemoryData Serialize(NPCShip ship) {
            if (ship.Memory == null) {
                if (ship.IsPlayer) { return null; }
                D.Err("ShipMemory was not set on ship: {0}", ship);
                ship.Memory ??= new NPCShipMemory(ship);
            }
            var memory = ship.Memory;
            return new NPCShipMemoryData {
                OriginSectorId = memory.OriginSector?.SO.Id ?? 0,
                TargetSectorId = memory.TargetSector?.SO.Id ?? 0,
                PreviousSectorId = memory.PreviousSector?.SO.Id ?? 0,
                PreviousRegionId = memory.PreviousRegion?.SO.Id ?? 0,
                SectorPathIds = SerializePath(memory.SectorPath),
                RegionPathIds = SerializePath(memory.RegionPath),
                FollowTargetId = memory.FollowTarget?.Id ?? 0,
                LastN2PInteractionTick = memory.LastN2PInteractionTick,
                ChaseTargetId = memory.ChaseTargetId,
                IsNearFollowTarget = memory.IsNearFollowTarget,
                Custom = memory.Custom
            };
        }

        private static List<int> SerializePath<T>(Queue<T> sectorPath) where T : ISpaceEntity {
            if (Arrays.IsEmpty(sectorPath)) { return null; }
            return sectorPath.Select(p => p.SO.Id).ToList();
        }

        private static Queue<T> DeserializePath<T>(GameState s, List<int> pathIds) {
            if (Arrays.IsEmpty(pathIds)) { return null; }
            var q = new Queue<T>();
            foreach (var id in pathIds) {
                var obj = s.Universe.Find(id);
                if (obj == null) {
                    D.Err("Failed finding path element by id: {0}", id);
                    return null;
                }
                q.Enqueue((T) obj.SpaceEntity);
            }
            return q;
        }

        public static bool TryDeserialize(GameState s, NPCShipMemoryData data, NPCShip ship) {
            if (data == null && ship.IsPlayer) {
                return true;
            }
            ship.Memory = new NPCShipMemory(ship);
            if (data == null) {
                ship.Memory.OriginSector = ship.Sector;
                D.Err("NPCShipMemoryData was null for ship: {0}", ship);
                return true;
            }
            try {
                ship.Memory.OriginSector = FindSpaceEntity<SpaceSector>(s, data.OriginSectorId);
                ship.Memory.TargetSector = FindSpaceEntity<SpaceSector>(s, data.TargetSectorId);
                ship.Memory.PreviousSector = FindSpaceEntity<SpaceSector>(s, data.PreviousSectorId);
                ship.Memory.PreviousRegion = FindSpaceEntity<SpaceRegion>(s, data.PreviousRegionId);
                ship.Memory.SectorPath = DeserializePath<SpaceSector>(s, data.SectorPathIds);
                ship.Memory.RegionPath = DeserializePath<SpaceRegion>(s, data.RegionPathIds);
                ship.Memory.FollowTarget = s.Universe.Find(data.FollowTargetId);
                ship.Memory.ChaseTargetId = data.ChaseTargetId;
                ship.Memory.LastN2PInteractionTick = data.LastN2PInteractionTick;
                ship.Memory.IsNearFollowTarget = data.IsNearFollowTarget;
                ship.Memory.Custom = data.Custom;
                return true;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed deserializing NPCShipMemoryData: {0}. Ship: {1}",
                    D.DumpInstance(data), ship);
                ship.Memory.OriginSector ??= ship.Sector;
                return false;
            }
        }

        private static T FindSpaceEntity<T>(GameState s, int id) where T : ISpaceEntity {
            var obj = s.Universe.Find(id);
            return (T) obj?.SpaceEntity;
        }
    }
}
