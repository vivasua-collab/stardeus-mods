using Game.Data;
using KL.Utils;
using MessagePack;
namespace Game.Systems.AI.Combat {
    // Squads are gone: each unit has its own objective now. Saves made while they existed still
    // hold them, and this is only read to give their units the objective their squad had.
    [MessagePackObject]
    public struct SquadData {
        [Key("LeaderId")] public uint LeaderId;
        [Key("Units")] public uint[] Units;
        [Key("Name")] public string Name;
        [Key("Flying")] public bool IsFlying;
        [Key("Config")] public CombatUnitConfig Config;
        [Key("ObjectiveId")] public int ObjectiveId;
        [Key("TicksInObjective")] public int TicksInObjective;

        public static void ApplyObjectives(GameState s, SquadData[] data) {
            if (data == null) { return; }
            for (int i = 0; i < data.Length; i++) {
                var sqd = data[i];
                var objective = Registries.CombatObjectives.FastGet(sqd.ObjectiveId);
                if (objective == null || sqd.Units == null) { continue; }
                foreach (var id in sqd.Units) {
                    if (ArmyData.TryFindUnitByBeingDataId(s, id, out var cu)) {
                        cu.Objective = objective;
                        cu.TicksInObjective = sqd.TicksInObjective;
                    }
                }
            }
        }
    }
}
