#if UNITY_EDITOR
// #define DEBUG_AI_GOALS
#endif
using System.Collections.Generic;
using Game.Components;
using Game.Data;
using Game.Migrations;
using Game.Systems.AI.Combat;
using Game.Systems.Path;
using KL.Collections;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.AI {
    [MessagePackObject]
    public struct AIGoalData {
        [Key("Id")] public uint Id;
        [Key("TargetId")] public int TargetId;
        [Key("TargetName")] public string TargetName;
        [Key("FailReason")] public string FailReason;
        [Key("WorkerId")] public int AgentId;
        [Key("JobType")] public int JobTypeH;
        [Key("GoalPriority")] public byte GoalPriority;
        [Key("GoalImportance")] public byte GoalImportance;
        [Key("TimesFailed")] public ushort TimesFailed;
        [Key("TimesFailedTotal")] public ushort TimesFailedTotal;
        [Key("State")] public AIGoalState State;
        [Key("StateBefore")] public AIGoalState StateBefore;
        [Key("PriorityResolverId")] public int PriorityResolverId;
        [Key("Type")] public AIGoalType Type;
        [Key("PathFlags")] public PathReqFlags PathFlags;
        [Key("NanobotsTick")] public long NanobotsLastWorkTick;
        [Key("InterruptPrio")] public byte InterruptPriority;
        [Key("MinInterruptTicks")] public int MinTicksBeforeInterrupt;
        [Key("Flags")] public AIGoalFlags Flags;
        [Key("StateChangeTick")] public long StateStartTick;
        [Key("CanRetryAfterTick")] public long CanRetryAfterTick;
        [Key("BoostedUntil")] public long BoostedUntil;
        [Key("FailedAgentId")] public uint FailedAgentId;
        [Key("Name")] public string Name;
        [Key("CreatedAt")] public long CreatedAt;
        [Key("FailedAt")] public long FailedAt;
        [Key("NumBids")] public int NumBids;
        [Key("IconId")] public string IconId;
        [Key("OverwritePrio")] public sbyte OverwritePriority;
        [Key("LastResult")] public AIActionResult LastResult;
        [Key("StateW")] public AIStateData WorldState;
        [Key("StateD")] public AIStateData DesiredState;
        [Key("StateI")] public AIStateData InitialState;
        [Key("Vars")] public ComponentData Vars;
        [Key("AllowedMindIds")] public uint[] AllowedMindIds;
        [Key("ReqAbilityIds")] public int[] RequiredAbilityIds;

        public static bool TrySerialize(AIGoal goal, out AIGoalData data) {
            if (goal == null) {
                data = default;
                return false;
            }
            data = new AIGoalData();
            // if (goal.Target == null) {
            //     D.Err("AIGoal has no Target when serializing: {0}", goal);
            //     return false;
            // }
            data.Id = goal.Id;
            data.Name = goal.Name;
            data.IconId = goal.IconId;
            data.State = goal.State;
            data.StateBefore = goal.StateBefore;
            data.TargetId = goal.Target?.EntityId ?? 0;
            data.JobTypeH = goal.JobIdH;
            data.OverwritePriority = goal.OverwritePriority;
            data.TargetName = goal.Target?.GetName();
            data.DesiredState = AIStateData.Serialize(goal.DesiredState);
            data.WorldState = AIStateData.Serialize(goal.WorldState);
            data.InitialState = AIStateData.Serialize(goal.InitialState);
            data.Vars = goal.Vars;
            data.NumBids = goal.NumBids;
            data.CanRetryAfterTick = goal.CanRetryAfterTick;
            data.TimesFailed = goal.TimesFailed;
            data.Type = goal.Type;
            data.FailedAgentId = goal.FailedAgentId;
            data.BoostedUntil = goal.BoostedUntil;
            data.LastResult = goal.LastResult;
            data.PathFlags = goal.PathFlags;
            data.CreatedAt = goal.CreatedAt;
            data.FailedAt = goal.FailedAt;
            data.PriorityResolverId = goal.PriorityResolver?.IdH ?? 0;
            data.InterruptPriority = goal.InterruptPriority;
            data.MinTicksBeforeInterrupt = goal.MinTicksBeforeInterrupt;
            data.GoalPriority = goal.GoalPriority;
            data.GoalImportance = goal.GoalImportance;
            data.TimesFailedTotal = goal.TimesFailedTotal;
            data.FailReason = goal.FailReason;
            data.Flags = goal.Flags;
            data.StateStartTick = goal.StateStartTick;
            data.AgentId = goal.Agent?.EntityId ?? 0;
            data.RequiredAbilityIds = goal.RequiredAbilities.ToArray();
            data.AllowedMindIds = goal.AllowedMindIds;
            #if DEBUG_AI_GOALS
            D.Warn("Serialized AI Goal: {0}", D.DumpInstance(data));
            #endif
            return true;
        }

        public static AIGoalData[] Serialize(List<AIGoal> goals) {
            var data = new AIGoalData[goals.Count];
            for (int i = goals.Count - 1; i >= 0; i--) {
                AIGoal g = goals[i];
                if (TrySerialize(g, out var gdata)) {
                    data[i] = gdata;
                }
            }
            return data;
        }

        public static void Deserialize(GameState s, AIGoalData[] data, List<AIGoal> res) {
            if (data == null) { return; }
            if (res.Count > 0) {
                D.Err("Trying to deserialize a list of goals into a non-empty result! {0}",
                    D.Dump(res));
                res.Clear();
            }
            for (int i = 0; i < data.Length; i++) {
                AIGoalData d = data[i];
                var g = Deserialize(s, d, false);
                if (g != null) {
                    res.Add(g);
                }
            }
        }

        // Ids of saved goals dropped on load because their resolver was removed
        // (Legacy.RemovedPriorityResolvers). Only read to tell a dropped goal from a lost one.
        public static readonly HashSet<uint> DroppedLegacyGoalIds = new();

        public static AIGoal Deserialize(GameState s, AIGoalData data, bool archived) {
            if (data.Id == 0) {
                return null;
            }
            if (Legacy.RemovedPriorityResolvers.Contains(data.PriorityResolverId)) {
                // an agent whose current goal was one of these finds it missing on load
                DroppedLegacyGoalIds.Add(data.Id);
                return null;
            }
            #if DEBUG_AI_GOALS
            D.Warn("Deserializing AI Goal: {0}", D.DumpInstance(data));
            #endif
            Entity entity = null;
            if (data.TargetId == 0 || !s.Map.TryEntityById(data.TargetId, out entity)) {
                if (data.Type == AIGoalType.Colony && !data.Flags.HasFlag(AIGoalFlags.NoTarget)) {
                    if (!archived) {
                        D.ErrSoft("Trying to deserialize a colony goal that has no target by id: {0}: {1}",
                            data.TargetId, D.DumpInstance(data));
                        return null;
                    }
                }
            }
            IAIGoalProvider target = null;
            if (data.TargetName != null && entity != null) {
                var compIdH = Hashes.S(data.TargetName);
                if (!entity.TryGetComponent(compIdH, out target)) {
                    if (Legacy.RemovedComps.Contains(compIdH)) {
                        return null;
                    }
                    if (!archived) {
                        D.Err("Trying to deserialize a goal that has target entity but no target comp by name: {0}: {1}: {2}",
                            data.TargetName, D.DumpInstance(data), entity);
                        return null;
                    }
                }
            }

            var goal = AIGoal.CreateBlank(data.State);
            goal.Id = data.Id;
            goal.Name = data.Name;
            goal.IconId = data.IconId;
            goal.JobIdH = data.JobTypeH;
            goal.StateBefore = data.StateBefore;
            goal.StateStartTick = data.StateStartTick;
            goal.CanRetryAfterTick = data.CanRetryAfterTick;
            goal.WorldState = AIStateData.Deserialize(data.WorldState, true);
            goal.DesiredState = AIStateData.Deserialize(data.DesiredState, true);
            goal.InitialState = AIStateData.Deserialize(data.InitialState, false);
            goal.GoalPriority = data.GoalPriority;
            goal.GoalImportance = data.GoalImportance;
            goal.OverwritePriority = data.OverwritePriority;
            goal.FailedAgentId = data.FailedAgentId;
            goal.LastResult = data.LastResult;
            goal.CreatedAt = data.CreatedAt;
            goal.BoostedUntil = data.BoostedUntil;
            goal.FailedAt = data.FailedAt;
            goal.NumBids = data.NumBids;
            goal.Vars = data.Vars;
            goal.Flags = data.Flags;
            goal.Type = data.Type;
            goal.PathFlags = data.PathFlags;
            if (data.PriorityResolverId != 0) {
                goal.WithPriorityResolver(data.PriorityResolverId);
            }
            goal.FailReason = data.FailReason;
            goal.TimesFailed = data.TimesFailed;
            goal.WithInterrupt(data.InterruptPriority, data.MinTicksBeforeInterrupt);
            goal.TimesFailedTotal = data.TimesFailedTotal;
            goal.RequiredAbilities = FrugalArray<int>.FromArray(data.RequiredAbilityIds);
            goal.AllowedMindIds = data.AllowedMindIds;
            goal.Target = target;
            if (data.AgentId != 0) {
                var worker = s.Beings.Find(data.AgentId);
                if (worker != null) {
                    goal.SetAgent(worker.AIAgent);
                } else {
                    D.Err("Could not find agent that was saved with AIGoal by agent id: {0}: {1}",
                        data.AgentId, D.DumpInstance(data));
                }
            }
            if (!AIState.IsNotEmpty(goal.DesiredState)) {
                goal.LoadState(AIGoalState.Error);
                goal.FailReason = "Invalid desired state";
            }

            return goal;
        }
    }
}
