using System.Collections.Generic;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Combat;
using KL.Utils;
using MessagePack;
namespace Game.Systems.AI.Combat {
    [MessagePackObject]
    public struct ArmyData {
        [Key("FactionId")] public string FactionId;
        [Key("Objectives")] public int[] Objectives;
        // only in saves made while squads existed, see SquadData
        [Key("Squads")] public SquadData[] Squads;
        [Key("Units")] public uint[] Units;
        // each unit's objective id and ticks in it, in the order of Units
        [Key("UnitObjectives")] public int[] UnitObjectives;
        [Key("UnitObjectiveTicks")] public int[] UnitObjectiveTicks;
        [Key("IsDefeated")] public bool IsDefeated;
        [Key("IsOrganized")] public bool IsOrganized;
        [Key("FormedTick")] public long FormedTick;
        [Key("InitialUnitCount")] public int InitialUnitCount;
        [Key("Flags")] public CombatFlags Flags;
        // null in saves made before re-joining units stopped counting twice
        [Key("Enrolled")] public uint[] Enrolled;
        // a gathering raid attacks at this tick, 0 when not gathering (CombatObjectiveStage)
        [Key("StageUntil")] public long StageUntilTick;

        public static ArmyData[] Serialize(List<Army> armies) {
            // no combat
            if (Arrays.IsEmpty(armies)) {
                return null;
            }
            var res = new ArmyData[armies.Count];
            for (int i = 0; i < armies.Count; i++) {
                Army a = armies[i];
                var data = Serialize(a);
                res[i] = data;
            }
            return res;
        }
        public static ArmyData Serialize(Army army) {
            var data = new ArmyData();
            data.FactionId = army.Faction.Id;
            data.Units = SerializeUnits(army.Units);
            data.UnitObjectives = SerializeUnitObjectives(army.Units);
            data.UnitObjectiveTicks = SerializeUnitObjectiveTicks(army.Units);
            data.Objectives = SerializeObjectives(army.Objectives);
            data.IsDefeated = army.IsDefeated;
            data.IsOrganized = army.IsOrganized;
            data.FormedTick = army.FormedTick;
            data.InitialUnitCount = army.InitialUnitCount;
            data.Flags = army.Flags;
            data.StageUntilTick = army.StageUntilTick;
            if (army.EnrolledIds.Count > 0) {
                data.Enrolled = new uint[army.EnrolledIds.Count];
                army.EnrolledIds.CopyTo(data.Enrolled);
            }
            return data;
        }

        private static List<CombatObjective> DeserializeObjectives(int[] ids) {
            var res = new List<CombatObjective>();
            if (ids == null) {
                return res;
            }
            for (int i = 0; i < ids.Length; i++) {
                int id = ids[i];
                var obj = Registries.CombatObjectives.FastGet(id);
                if (obj != null) {
                    res.Add(obj);
                } else {
                    D.Err("Failed deserializing objective by id: {0}", id);
                }
            }
            return res;
        }
        private static int[] SerializeObjectives(List<CombatObjective> objectives) {
            if (Arrays.IsEmpty(objectives)) {
                return null;
            }
            var res = new int[objectives.Count];
            for (int i = 0; i < objectives.Count; i++) {
                CombatObjective o = objectives[i];
                res[i] = o.IdH;
            }
            return res;
        }

        public static List<CombatUnit> DeserializeUnits(GameState s, uint[] ids) {
            var res = new List<CombatUnit>();
            if (ids != null) {
                for (int i = 0; i < ids.Length; i++) {
                    if (TryFindUnitByBeingDataId(s, ids[i], out var cu)) {
                        res.Add(cu);
                    } else {
                        D.Err("Could not find serialized unit by being id: {0}", ids[i]);
                    }
                }
            }
            return res;
        }

        public static bool TryFindUnitByBeingDataId(GameState s, uint id, out CombatUnit cu) {
            var beingData = s.Query.FindBeingData.Ask(id);
            if (beingData == null) {
                D.Err("Could not find combat unit by being data id: {0} (no being data)", id);
                cu = null;
                return false;
            }
            if (beingData.Being == null) {
                D.Err("Found unit being data, but no Being on it! {0}", beingData);
                cu = null;
                return false;
            }
            if (beingData.Being.AIAgent.CombatUnit == null) {
                D.Err("Found unit being, but no CombatUnit on it! {0}",
                    beingData.Being);
                cu = null;
                return false;
            }
            cu = beingData.Being.AIAgent.CombatUnit;
            return true;
        }

        private static int[] SerializeUnitObjectives(List<CombatUnit> units) {
            if (Arrays.IsEmpty(units)) {
                return null;
            }
            var res = new int[units.Count];
            for (int i = 0; i < units.Count; i++) {
                res[i] = units[i].Objective?.IdH ?? 0;
            }
            return res;
        }

        private static int[] SerializeUnitObjectiveTicks(List<CombatUnit> units) {
            if (Arrays.IsEmpty(units)) {
                return null;
            }
            var res = new int[units.Count];
            for (int i = 0; i < units.Count; i++) {
                res[i] = units[i].TicksInObjective;
            }
            return res;
        }

        public static uint[] SerializeUnits(List<CombatUnit> units) {
            if (Arrays.IsEmpty(units)) {
                return null;
            }
            var res = new uint[units.Count];
            for (int i = 0; i < units.Count; i++) {
                CombatUnit u = units[i];
                res[i] = u.Data.Id;
            }
            return res;
        }

        public static void Deserialize(GameState S, ArmyData[] data, List<Army> armies) {
            if (armies.Count > 0) {
                D.Err("Trying to deserialize while armies count is > 0! PANIK!");
                if (!Arrays.IsEmpty(data)) {
                    armies.Clear();
                }
            }
            // no combat
            if (Arrays.IsEmpty(data)) { return; }

            foreach (var ad in data) {
                var army = Deserialize(S, ad);
                if (army != null) {
                    armies.Add(army);
                }
            }
        }

        public static Army Deserialize(GameState S, ArmyData data) {
            var faction = FactionDef.Get(data.FactionId);
            var army = Army.Create(faction);
            army.IsDefeated = data.IsDefeated;
            army.IsOrganized = data.IsOrganized;
            army.Objectives = DeserializeObjectives(data.Objectives);
            army.FormedTick = data.FormedTick;
            army.StageUntilTick = data.StageUntilTick;
            army.InitialUnitCount = data.InitialUnitCount;
            if (army.FormedTick == 0L) {
                // catch up for old saves
                army.FormedTick = S.Ticks;
            }
            army.Units = DeserializeUnits(S, data.Units);
            foreach (var u in army.Units) {
                u.Army = army;
            }
            SquadData.ApplyObjectives(S, data.Squads);
            if (data.UnitObjectives != null && data.Units != null
                    && data.UnitObjectives.Length == data.Units.Length) {
                for (int i = 0; i < data.Units.Length; i++) {
                    if (data.UnitObjectives[i] == 0) { continue; }
                    if (TryFindUnitByBeingDataId(S, data.Units[i], out var cu) && cu.Army == army) {
                        cu.Objective = Registries.CombatObjectives.FastGet(data.UnitObjectives[i]);
                        if (data.UnitObjectiveTicks != null
                                && data.UnitObjectiveTicks.Length == data.Units.Length) {
                            cu.TicksInObjective = data.UnitObjectiveTicks[i];
                        }
                    }
                }
            }
            if (data.Enrolled != null) {
                army.EnrolledIds.UnionWith(data.Enrolled);
            } else {
                // older save: the units still here are the ones known to have enlisted
                foreach (var u in army.Units) {
                    army.EnrolledIds.Add(u.Data.Id);
                }
            }
            army.Flags = data.Flags;
            if (army.Flags == CombatFlags.None) {
                D.Warn("Loading army without flags! setting to hostile: {0}", army);
                army.Flags |= CombatFlags.Hostile;
            }
            return army;
        }
    }
}
