using System.Collections.Generic;
using Game.Systems.AI;
using MessagePack;

namespace Game.Systems.AI {
    [MessagePackObject]
    public class AIPlannerSysData {
        [Key("LastGoalId")] public uint LastGoalId;
        [Key("GoalsWithError")] public List<AIGoalData> GoalsWithError;
        [Key("Goals")] public List<AIGoalData> Goals;
        // Dictionary because we register agents before we load this
        // and then we query for agent data by agent id
        [Key("Agents")] public Dictionary<int, AIAgentData> Agents;
    }
}
