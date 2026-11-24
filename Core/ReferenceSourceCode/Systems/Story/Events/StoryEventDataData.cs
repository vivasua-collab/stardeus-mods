#if UNITY_EDITOR
#define DEBUG_STORY_LOAD
#endif
using System.Collections.Generic;
using Game.Components;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Mental;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Story {
    [MessagePackObject]
    public sealed class StoryEventDataData {
        [Key("Id")] public uint Id;
        [Key("EventDefId")] public string EventDefId;
        [Key("State")] public StoryEventState State;
        [Key("StartTick")] public long StartTick;
        [Key("EndTick")] public long EndTick;
        [Key("Seed")] public int Seed;
        [Key("TileId")] public int TileId;
        [Key("BeingId")] public uint BeingId;
        [Key("ParticipantIds")] public uint[] ParticipantIds;
        [Key("SOId")] public int SOId;
        [Key("FactionId")] public string FactionId;
        [Key("FailReason")] public string FailReason;
        [Key("Flags")] public StoryEventDataFlags Flags;
        [Key("EventFlagsActual")] public StoryEventFlags EventFlagsActual;
        [Key("ObjId")] public uint ObjId;
        [Key("LocationId")] public int LocationId;
        [Key("Data")] public ComponentData Data;
        [Key("LogEntry")] public StoryLogEntry LogEntry;
        [Key("PointsReserved")] public int PointsReserved;

        private static StoryEventDataData Serialize(StoryEventData data)  {
            if (data == null) { return null; }
            var dd = new StoryEventDataData();
            dd.Id = data.Id;
            dd.EventDefId = data.Def?.Id;
            dd.State = data.State;
            dd.StartTick = data.StartTick;
            dd.EndTick = data.EndTick;
            dd.Seed = data.SeedForSave;
            dd.FailReason = data.FailReason;
            dd.FactionId = data.Faction?.Id;
            dd.TileId = data.TargetTile?.Id ?? 0;
            dd.BeingId = data.TargetBeing?.Id ?? 0;
            dd.ObjId = data.TargetObj?.Id ?? 0;
            dd.SOId = data.TargetSO?.Id ?? 0;
            dd.LocationId = data.Location?.Id ?? 0;
            dd.Data = data.Data;
            dd.Flags = data.Flags;
            dd.EventFlagsActual = data.EventFlagsActual;
            dd.LogEntry = data.LogEntry;
            dd.PointsReserved = data.PointsReserved;
            if (!Arrays.IsEmpty(data.Participants)) {
                dd.ParticipantIds = new uint[data.Participants.Count];
                for (int i = 0; i < data.Participants.Count; i++) {
                    var p = data.Participants[i];
                    dd.ParticipantIds[i] = p.Id;
                }
            }
            return dd;
        }

        private static StoryEventData TryDeserialize(GameState s, StoryEventDataData data) {
            if (data == null) { return null; }
            StoryEventDef def = null;
            if (data.EventDefId != null) {
                if (IsRemovedSignalTransaction(data.EventDefId)
                        && !StoryEventDef.All.ContainsKey(Hashes.S(data.EventDefId))) {
                    D.Warn("Skipping removed Signal transaction story event: {0}",
                        data.EventDefId);
                    return null;
                }
                def = StoryEventDef.Get(data.EventDefId);
                if (def == null) {
                    D.Err("Could not load story event def by id: {0}. Skipping load of event data: {1}",
                        data.EventDefId, D.DumpInstance(data, forLog: true));
                    return null;
                }
            }
            var d = new StoryEventData();
            d.Id = data.Id;
            d.Def = def;
            d.LoadState(data.State);
            d.StartTick = data.StartTick;
            d.EndTick = data.EndTick;
            d.Seed = data.Seed;
            d.LogEntry = data.LogEntry;
            if (data.FactionId != null) {
                d.Faction = FactionDef.Get(data.FactionId);
            }
            if (data.TileId > 0) {
                d.TargetTile = s.FindEntity<Tile>(data.TileId);
                #if DEBUG_STORY_LOAD
                if (d.TargetTile == null) {
                    D.Warn("Failed loading target tile by id {0} for story event data {1}",
                        data.TileId, D.DumpInstance(data, forLog: true));
                }
                #endif
            }
            if (data.BeingId > 0) {
                d.TargetBeing = s.Query.FindBeingData.Ask(data.BeingId);
                #if DEBUG_STORY_LOAD
                if (d.TargetBeing == null) {
                    D.Warn("Failed loading target being data by id {0} for story event data {1}",
                        data.BeingId, D.DumpInstance(data, forLog: true));
                }
                #endif
            }
            if (!Arrays.IsEmpty(data.ParticipantIds)) {
                d.Participants = new List<Mind>();
                for (int i = 0; i < data.ParticipantIds.Length; i++) {
                    uint pId = data.ParticipantIds[i];
                    var participant = s.Query.FindMind.Ask(pId);
                    if (participant == null) {
                        D.Err("Failed loading participant mind data by id {0} for story event data {1}",
                            pId, D.DumpInstance(data, forLog: true));
                        continue;
                    }
                    d.Participants.Add(participant);
                }
            }
            if (data.ObjId > 0) {
                d.TargetObj = s.Query.TryFindObjData.Ask(data.ObjId);
                #if DEBUG_STORY_LOAD
                if (d.TargetObj == null) {
                    D.Warn("Failed loading target obj data by id {0} for story event data {1}",
                        data.ObjId, D.DumpInstance(data, forLog: true));
                }
                #endif
            }
            if (data.SOId > 0) {
                if (!s.Universe.TryFind(data.SOId, out d.TargetSO)) {
                    if (data.State != StoryEventState.Ended
                            && data.State != StoryEventState.Failed) {
                        D.Warn("Failed loading target so by id {0} for non-ended story event data {1}",
                            data.SOId, D.DumpInstance(data, forLog: true));
                    }
                }
            }
            if (data.LocationId > 0) {
                var map = s.Query.GetSpaceMap.Ask();
                if (!map.TryFindSector(data.LocationId, out d.Location)) {
                    #if DEBUG_STORY_LOAD
                    D.Warn("Failed loading sector by id {0} for story event data {1}",
                        data.LocationId, D.DumpInstance(data, forLog: true));
                    #endif
                }
            }
            d.FailReason = data.FailReason;
            d.Data = data.Data;
            d.Flags = data.Flags;
            d.EventFlagsActual = data.EventFlagsActual;
            d.PointsReserved = data.PointsReserved;
            return d;
        }

        private static bool IsRemovedSignalTransaction(string eventDefId) {
            return eventDefId == "Signal/AskMatGiveEntity"
                || eventDefId == "Signal/AskMatGiveMoney"
                || eventDefId == "Signal/AskMoneyGiveEntity";
        }

        public static List<StoryEventDataData> Serialize(List<StoryEventData> list) {
            if (Arrays.IsEmpty(list)) { return null; }
            var res = new List<StoryEventDataData>();
            foreach (var d in list) {
                res.Add(Serialize(d));
            }
            return res;
        }

        public static void Deserialize(GameState s, List<StoryEventDataData> data,
                List<StoryEventData> target) {
            target.Clear();
            if (Arrays.IsEmpty(data)) { return; }
            using (D.Timer("DeserializeStoryEvents", 0.0001f)) {
                foreach (var dd in data) {
                    var d = TryDeserialize(s, dd);
                    if (d != null) {
                        target.Add(d);
                    }
                }
            }
        }
    }
}
