using System.Collections.Generic;
using Game.Components;
using Game.Data;
using Game.Migrations;
using Game.Systems.AI.Combat;
using KL.Utils;
using MessagePack;

namespace Game.Systems.AI {
    [MessagePackObject]
    public struct AIAgentData {
        [Key("AgentId")] public int AgentId;
        [Key("CurGoalId")] public uint CurrentGoalId;
        [Key("CurAct")] public string CurrentAct;
        [Key("Plan")] public AIPlanData Plan;
        [Key("BB")] public AIAgentBlackboard BB;
        [Key("TicksInCurAct")] public int TicksInCurrentAct;
        [Key("CurActStartTick")] public long CurActStartTick;
        [Key("LastFailedGoal")] public AIGoalData LastFailedGoal;
        [Key("LastFailedGoalTicks")] public long LastFailedGoalTicks;
        [Key("LastFailedGoalReason")] public string LastFailedGoalReason;
        [Key("LastCompletedGoalTicks")] public long LastCompletedGoalTicks;
        [Key("LastCompletedGoal")] public AIGoalData LastCompletedGoal;
        [Key("IdleGoal")] public AIGoalData IdleGoal;
        [Key("ActiveGoal")] public AIGoalData ActiveGoal;
        [Key("Goals")] public AIGoalData[] Goals;
        [Key("ActionMemory")] public ComponentData ActionMemory;
        [Key("CombatUnit")] public CombatUnitData CombatUnit;
        public static bool Serialize(AIAgentComp agent, out AIAgentData data) {
            data = new AIAgentData();
            if (agent == null) {
                D.Err("Trying to serialize null agent!");
                return false;
            }
            if (agent.Being == null) {
                D.Err("Trying to serialize agent with null worker! {0}", (object)agent);
                return false;
            }
            // Hidden goals are not saved (their owner rebuilds them), so an agent working on
            // one is saved without a goal and bids for the rebuilt one after the load
            var isGoalHidden = agent.Goal?.IsHidden == true;
            var activeGoal = isGoalHidden ? null : agent.Goal;
            if (!isGoalHidden) {
                data.CurrentAct = agent.CurrentAction?.Id;
                data.TicksInCurrentAct = agent.TicksInCurrentAction;
            }
            if (AIGoalData.TrySerialize(activeGoal, out var agd)) {
                data.ActiveGoal = agd;
            }
            data.CurActStartTick = agent.CurrentActionStartTick;
            data.CurrentGoalId = activeGoal?.Id ?? 0;
            if (AIGoalData.TrySerialize(agent.LastFailedGoal, out var lfg)) {
                data.LastFailedGoal = lfg;
                data.LastFailedGoalReason = agent.LastFailedGoalReason;
                data.LastFailedGoalTicks = agent.LastFailedGoalTicks;
            }
            if (AIGoalData.TrySerialize(agent.LastCompletedGoal, out var lcg)) {
                data.LastCompletedGoal = lcg;
                data.LastCompletedGoalTicks = agent.LastCompletedGoalTicks;
            }
            agent.BB?.BeforeSave();
            data.BB = agent.BB;
            data.AgentId = agent.Being.Id;
            if (agent.ActionMemory != null) {
                data.ActionMemory = agent.ActionMemory.Save();
            }
            if (AIGoalData.TrySerialize(agent.IdleGoal, out var idleGoal)) {
                data.IdleGoal = idleGoal;
            }
            data.CombatUnit = CombatUnitData.Serialize(agent.CombatUnit);
            if (!isGoalHidden) {
                data.Plan = AIPlanData.Serialize(agent.Plan);
            }
            if (!Arrays.IsEmpty(agent.Goals)) {
                data.Goals = new AIGoalData[agent.Goals.Count];
                for (int i = agent.Goals.Count - 1; i >= 0; i--) {
                    AIGoal g = agent.Goals[i];
                    if (AIGoalData.TrySerialize(g, out var goal)) {
                        data.Goals[i] = goal;
                    }
                }
            }
            return true;
        }

        // This has to be called after goals are deserialized
        public static bool Deserialize(GameState s, AIAgentComp agent, AIAgentData data,
                Dictionary<uint, AIGoal> goals) {
            if (agent == null || data.AgentId == 0) {
                D.Err("Deserializing null agent or uninitialized data! {0} / {1}",
                    agent, D.DumpInstance(data));
                return false;
            }
            if (data.CurrentAct != null) {
                // it could be a removed action (i.e. find jukebox)
                var act = Registries.AIActions.Get(data.CurrentAct, errorIfMissing: false);
                if (act == null && Legacy.AIActions.TryGetValue(data.CurrentAct, out var repId)) {
                    act = Registries.AIActions.Get(repId, errorIfMissing: true);
                }
                if (act != null) {
                    agent.SetAction(act);
                }
            }
            // must be done after setting action
            agent.CurrentActionStartTick = data.CurActStartTick;
            agent.TicksInCurrentAction = data.TicksInCurrentAct;
            agent.ActionMemoryData = data.ActionMemory;
            agent.Plan = AIPlanData.Deserialize(data.Plan);
            agent.LastCompletedGoal = AIGoalData.Deserialize(s, data.LastCompletedGoal, true);
            agent.LastFailedGoal = AIGoalData.Deserialize(s, data.LastFailedGoal, true);
            agent.LastCompletedGoalTicks = data.LastCompletedGoalTicks;
            agent.LastFailedGoalTicks = data.LastFailedGoalTicks;
            agent.LastFailedGoalReason = data.LastFailedGoalReason;
            agent.IdleGoal = AIGoalData.Deserialize(s, data.IdleGoal, false);
            CombatUnitData.Deserialize(s, agent, data.CombatUnit);
            if (!Arrays.IsEmpty(data.Goals)) {
                agent.Goals.Clear();
                for (int i = data.Goals.Length - 1; i >= 0; i--) {
                    AIGoalData d = data.Goals[i];
                    var g = AIGoalData.Deserialize(s, data.Goals[i], false);
                    if (g != null) {
                        agent.Goals.Add(g);
                        if (g.Id == data.CurrentGoalId) {
                            // this was the current private goal of the agent!
                            agent.Goal = g;
                        }
                    }
                }
            }
            if (agent.Goal == null && data.CurrentGoalId != 0) {
                if (goals.TryGetValue(data.CurrentGoalId, out var g)) {
                    agent.Goal = g;
                } else {
                    bool foundPersonalGoal = false;
                    if (agent.IdleGoal?.Id == data.CurrentGoalId) {
                        agent.Goal = agent.IdleGoal;
                        foundPersonalGoal = true;
                    }
                    if (!foundPersonalGoal) {
                        foreach (var ag in agent.Goals) {
                            if (ag.Id == data.CurrentGoalId) {
                                agent.Goal = ag;
                                foundPersonalGoal = true;
                                break;
                            }
                        }
                    }
                    if (!foundPersonalGoal) {
                        foreach (var cg in agent.CombatUnit.Goals) {
                            if (cg.Id == data.CurrentGoalId) {
                                agent.Goal = cg;
                                foundPersonalGoal = true;
                                break;
                            }
                        }
                    }
                    if (!foundPersonalGoal) {
                        if (data.ActiveGoal.Id == data.CurrentGoalId) {
                            agent.Goal = AIGoalData.Deserialize(s, data.ActiveGoal, false);
                        } else if (!AIGoalData.DroppedLegacyGoalIds.Contains(data.CurrentGoalId)) {
                            D.Err("Could not deserialize agent's cur goal by id: {0}. {1}",
                                data.CurrentGoalId, agent);
                        }
                    }
                }
            }
            if (data.ActiveGoal.Id != 0 && agent.Goal?.Id != data.ActiveGoal.Id
                    && !Legacy.RemovedPriorityResolvers.Contains(data.ActiveGoal.PriorityResolverId)) {
                D.Err("Sanity check: Saved active goal does not match actual goal after agent was loaded: {0} != {1} | {2}",
                    D.DumpInstance(data.ActiveGoal), agent.Goal, agent);
            }
            agent.BB = data.BB;
            if (agent.BB == null) {
                if (agent.Being.IsAlive) {
                    D.Err("Blackboard was not saved for a living agent: {0}", agent);
                    agent.BB = new AIAgentBlackboard();
                }
            }
            if (agent.BB != null) {
                agent.BB.SetAgent(agent);
                agent.BB.AfterLoad(s, agent.Goals);
            }
            agent.CurrentAction?.AfterLoad(agent, agent.Goal);
            return true;
        }
    }
}
