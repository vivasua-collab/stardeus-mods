using System;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Space;
using KL.Randomness;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class NPCShipData {
        [Key("Id")] public uint Id;
        [Key("Name")] public string Name;
        [Key("Class")] public string ClassDefId;
        [Key("Tag")] public string TagDefId;
        [Key("Faction")] public string FactionId;
        [Key("Memory")] public NPCShipMemoryData Memory;
        [Key("Sector")] public int SectorId;
        [Key("State")] public ShipStateFlags State;
        [Key("BehaviorSector")] public string BehaviorSectorId;
        [Key("BehaviorRegion")] public string BehaviorRegionId;
        [Key("SO")] public int SOId;
        [Key("NextRegionTurnTick")] public long NextRegionTurnTick;
        [Key("NextNoticeTick")] public long NextNoticeNearbyTick;
        [Key("NextLocalPosition")] public Vector2 NextLocalPosition;
        [Key("CurLocalPosition")] public Vector2 CurLocalPosition;
        // Missing in older saves: false, and the ship gets a random spot on sector entry
        [Key("PlacedInSector")] public bool IsPlacedInSector;
        [Key("LocalTarget")] public Vector2 LocalTarget;
        [Key("WillTrade")] public bool WillTrade;
        [Key("IsQuestGiver")] public bool IsQuestGiver;
        [Key("NoQuests")] public bool NoQuests;
        [Key("QuestId")] public uint QuestId;
        [Key("HasLocalTarget")] public bool HasLocalTarget;
        [Key("LocalVelocity")] public float LocalVelocity;
        [Key("WantedVelocity")] public float WantedVelocity;
        [Key("LooksTypeId")] public string LooksTypeId;
        [Key("WantsHyperspace")] public bool WantsHyperspace;
        [Key("IsInHyperspace")] public bool IsInHyperspace;
        [Key("Size")] public int Size;
        [Key("NumShortRangeDrives")] public int NumShortRangeDrives;
        [Key("NumShortRangeDrivesCharged")] public int NumShortRangeDrivesCharged;
        [Key("NumLongRangeDrives")] public int NumLongRangeDrives;
        [Key("NumLongRangeDrivesCharged")] public int NumLongRangeDrivesCharged;
        [Key("NumThrusters")] public int NumThrusters;
        [Key("NumFusionCells")] public int NumFusionCells;
        [Key("NumHeavyFusionCells")] public int NumHeavyFusionCells;
        [Key("CombatState")] public ShipCombatStateData CombatState;
        [Key("IsPolice")] public bool IsPolice;
        [Key("EnergyProduction")] public int EnergyProduction;

        public static NPCShipData Serialize(NPCShip ship) {

            try {
                if (ship == null) {
                    return null;
                }
                if (ship.Sector?.SO == null) {
                    D.Err("Trying to serialize ship that has no sector or sector has no space object! {0}", ship);
                    return null;
                }
                if (!ship.IsDerelict && ship.Faction == null) {
                    D.Err("Trying to serialize ship without faction! {0}",
                        D.DumpInstance(ship));
                    return null;
                }
                var data = new NPCShipData();
                data.Id = ship.Id;
                data.Name = ship.Name;
                data.ClassDefId = ship.Class?.Id;
                data.TagDefId = ship.Tag?.Id;
                data.FactionId = ship.Faction?.Id;
                data.Memory = NPCShipMemoryData.Serialize(ship);
                data.SectorId = ship.Sector.SO.Id;
                data.BehaviorSectorId = ship.BehaviorSector?.Id;
                data.BehaviorRegionId = ship.BehaviorRegion?.Id;
                data.SOId = ship.SO?.Id ?? 0;
                data.State = ship.State;
                data.NextLocalPosition = ship.NextLocalPosition;
                data.CurLocalPosition = ship.CurLocalPosition;
                data.IsPlacedInSector = ship.IsPlacedInSector;
                data.LocalTarget = ship.LocalTarget;
                data.WillTrade = ship.WillTrade;
                data.IsQuestGiver = ship.IsQuestGiver;
                data.NoQuests = ship.NoQuests;
                data.QuestId = ship.QuestId;
                data.HasLocalTarget = ship.HasLocalTarget;
                data.LocalVelocity = ship.LocalVelocity;
                data.WantedVelocity = ship.WantedVelocity;
                data.LooksTypeId = ship.LooksTypeId;
                data.WantsHyperspace = ship.WantsHyperspace;
                data.IsInHyperspace = ship.IsInHyperspace;
                data.NextRegionTurnTick = ship.NextRegionTurnTick;
                data.NextNoticeNearbyTick = ship.NextNoticeNearbyTick;
                data.Size = ship.Size;
                data.NumShortRangeDrivesCharged = ship.NumShortRangeDrivesCharged;
                data.NumShortRangeDrives = ship.NumShortRangeDrives;
                data.NumLongRangeDrivesCharged = ship.NumLongRangeDrivesCharged;
                data.NumLongRangeDrives = ship.NumLongRangeDrives;
                data.NumThrusters = ship.NumThrusters;
                data.NumFusionCells = ship.NumFusionCells;
                data.NumHeavyFusionCells = ship.NumHeavyFusionCells;
                data.CombatState = ShipCombatStateData.Serialize(ship);
                data.IsPolice = ship.IsPolice;
                data.EnergyProduction = ship.EnergyProduction;
                return data;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed serializing ship: {0}", D.DumpInstance(ship));
                return null;
            }
        }

        public static bool TryDeserialize(GameState s, NPCShipData data, out NPCShip ship) {
            ship = new NPCShip();
            try {
                ship.Id = data.Id;
                ship.Name = data.Name;
                ship.Class = ShipClassDef.Get(data.ClassDefId);
                if (ship.Class == null) {
                    ship.Class = ShipClassDef.Unclassified;
                    D.Err("Deserializing ship without class! {0} | {1}",
                        data.Id, data.Name);
                }
                ship.Tag = ShipTagDef.Get(data.TagDefId);
                if (ship.Tag == null) {
                    ship.Tag = ShipTagDef.Get("USV");
                    D.Err("Deserializing ship without tag! {0} | {1}",
                        data.Id, data.Name);
                }
                if (data.FactionId != null) {
                    // derelicts may not have one
                    ship.Faction = FactionDef.Get(data.FactionId);
                }
                if (s.Universe.Find(data.SectorId)?.SpaceEntity is SpaceSector sec) {
                    ship.Sector = sec;
                } else {
                    D.Err("Could not find space sector for ship: {0}", data);
                    return false;
                }
                ship.SO = s.Universe.Find(data.SOId);
                if (ship.SO == null) {
                    D.Err("Failed restoring ship SOId {0}: {1}", data.SOId,
                        D.DumpInstance(data, forLog: true));
                    return false;
                }
                ship.State = data.State;
                ship.SO.Ship = ship;
                ship.NextRegionTurnTick = data.NextRegionTurnTick;
                ship.NextNoticeNearbyTick = data.NextNoticeNearbyTick;
                ship.NextLocalPosition = data.NextLocalPosition;
                ship.CurLocalPosition = data.CurLocalPosition;
                ship.IsPlacedInSector = data.IsPlacedInSector;
                ship.LocalTarget = data.LocalTarget;
                ship.WillTrade = data.WillTrade;
                ship.IsQuestGiver = data.IsQuestGiver;
                ship.NoQuests = data.NoQuests;
                ship.QuestId = data.QuestId;
                ship.HasLocalTarget = data.HasLocalTarget;
                ship.LocalVelocity = data.LocalVelocity;
                ship.WantedVelocity = data.WantedVelocity;
                ship.LoadWantsHyperspace(data.WantsHyperspace);
                ship.IsInHyperspace = data.IsInHyperspace;
                ship.LoadSize(data.Size);
                ship.NumShortRangeDrivesCharged = data.NumShortRangeDrivesCharged;
                ship.NumShortRangeDrives = data.NumShortRangeDrives;
                ship.NumLongRangeDrivesCharged = data.NumLongRangeDrivesCharged;
                ship.NumLongRangeDrives = data.NumLongRangeDrives;
                ship.NumThrusters = data.NumThrusters;
                ship.NumFusionCells = data.NumFusionCells;
                ship.NumHeavyFusionCells = data.NumHeavyFusionCells;
                ship.IsPolice = data.IsPolice;
                ship.EnergyProduction = data.EnergyProduction;
                if (ship.EnergyProduction == 0 && ship.Id != 0) {
                    // Generate energy production for existing NPC ships
                    ship.EnergyProduction = (int) (ship.Class.MinEnergy * Rng.URange(1.05f, 1.5f));
                }
                if (data.BehaviorSectorId != null) {
                    if (ship.IsDerelict || !ship.IsActive) {
                        D.ErrSoft("Skipping load of sector behavior for derelict: {0} -> {1}",
                            data.BehaviorSectorId, ship);
                    } else {
                        ship.BehaviorSector = Registries.NPCShipBehaviors.Get(data.BehaviorSectorId);
                    }
                }
                if (data.BehaviorRegionId != null) {
                    if (ship.IsDerelict || !ship.IsActive) {
                        D.ErrSoft("Skipping load of region behavior for derelict: {0} -> {1}",
                            data.BehaviorRegionId, ship);
                    } else {
                        ship.BehaviorRegion = Registries.NPCShipBehaviors.Get(data.BehaviorRegionId);
                    }
                }
                NPCShipMemoryData.TryDeserialize(s, data.Memory, ship);
                ShipCombatStateData.TryDeserialize(s, data.CombatState, ship);
                return true;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed to deserialize NPCShip: {0}", D.DumpInstance(data));
                return false;
            }
        }
    }
}
