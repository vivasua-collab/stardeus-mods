using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Mental;
using Game.Systems.Drama;

// FIXME: interfaces, deduplicate with jukebox

namespace Game.Systems.AI {
    public sealed class AIActionFunUseFunProvider : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFunUseFunProvider());
        }

        public const string ActId = "ActFunUseFunProvider";
        private static readonly IFunProvider[] providers = new IFunProvider[64];
        private AIActionFunUseFunProvider() : base(ActId, () => T.ActHavingFun) {
            Preconditions = AIState.With(AIVarsH.HadFun, false)
                .And(AIVarsH.IsNearFunProvider, true);
            Outcomes = AIState.With(AIVarsH.HadFun, true);
            WithRequiredNeed(NeedIdH.Fun);
            WithRequiredAbilities(AbilityIdH.Move);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1NotSaved = false;
            // don't set this to null, previous action will find the jukebox!
            // agent.BB.TargetEntity = null;
            agent.BB.ClearMoveTarget();
            FunSession.Clear(agent);
        }

        private bool IsUsingDevice(AIAgentComp agent) {
            return agent.BB.Bool1NotSaved;
        }

        private void SetUsingDevice(AIAgentComp agent) {
            agent.BB.Bool1NotSaved = true;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            var device = agent.BB.TargetEntity;
            if (device == null) {
                return Error("We managed to UseFunProvider while it was not set by previous action! {0} / {1}",
                    goal, agent);
            }
            if (!device.TryGetComponent<IFunProvider>(out var prov)) {
                return Error("Device does not have FunProviderComp! {0}", device);
            }

            if (!IsUsingDevice(agent)) {
                var pos = prov.WorkSpot;
                // verify if we are near
                if (Pos.SqrDistance(agent.Motion.Position.Idx, pos) > 2f) {
                    return FailedNavToTarget(agent, pos);
                }
                if (prov.TryAddUser(agent.Being)) {
                    prov.CreateSocialOpportunity(agent);
                    agent.BB.SetChatContext(prov.ChatContext);
                    agent.ShowThoughtBubble(prov.ActIcon, prov.ActName);
                    SetUsingDevice(agent);
                    // a session restored from a save keeps its planned end
                    if (!FunSession.IsStarted(agent)) {
                        FunSession.Begin(agent, ticks,
                            FunSession.FullNetGainPerTick(agent.BeingData.StatsBeing, 1f),
                            prov.MinSessionTicks, prov.MaxSessionTicks);
                    }
                    return InProgress;
                } else {
                    agent.BB.SetCooldown(IdH,
                        ticks, Consts.TicksPer2Hours, Consts.TicksPer3Hours);
                    return Failed("Could not use fun provider!");
                }
            }

            // we are using the jukebox
            if (prov.Use(agent.Being)) {
                var data = agent.BeingData;
                var stats = data.StatsBeing;
                var novelty = FunNovelty.Of(data, FunSubjects.ForDevice(device), ticks);
                var fun = stats.Fun;
                fun.AddToBaseWithCompoundLimit(FunSession.GainPerTick(stats, novelty, 1f));
                // stop when a session at full novelty would have stopped, even if the
                // need is not satisfied yet
                if (!FunSession.IsOver(agent, ticks)
                        && prov.ShouldContinueUsing(agent, goal, ticks, fun.Value)) {
                    return InProgress;
                }
            } else {
                return Failed($"{T.RequiredDeviceIsNotAvailable}: {device.Definition.CodexLink}");
            }

            TryWalkAway(agent);

            if (agent.TicksInCurrentAction > Consts.TicksPer10Minutes) {
                prov.Reward(agent.Being, ticks, agent.TicksInCurrentAction);
            }
            FunSession.Finish(agent, ticks);
            goal.WithWorld(AIVarsH.HadFun, true);
            agent.BB.SetCooldown(IdH,
                ticks, Consts.TicksPer8Hours, Consts.TicksPer12Hours);
            return Success;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.Bool1NotSaved) {
                if (agent.BB.TargetEntity?.TryGetComponent<IFunProvider>(out var prov) == true) {
                    // remembered on success and on interruption alike, once it was long
                    // enough to count
                    if (agent.TicksInCurrentAction > Consts.TicksPer10Minutes) {
                        FunNovelty.Record(agent.BeingData,
                            FunSubjects.ForDevice(agent.BB.TargetEntity), ticks);
                    }
                    prov.RemoveUser(agent.Being);
                }
                agent.BB.Bool1NotSaved = false;
            }
            FunSession.Clear(agent);
            agent.BB.ClearChatContext();
            agent.BB.RemoveOpportunity(agent.S);
            agent.BB.TargetEntity = null;
            agent.BB.ClearMoveTarget();
            agent.HideThoughtBubble();
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return CachedFunCostWithCoolDown(agent, ticks);
        }

        protected override float PredictFunNovelty(AIAgentComp agent, long ticks) {
            var count = agent.S.Components.FindAllByInterface(providers);
            var being = agent.Being;
            var data = agent.BeingData;
            float best = 0f;
            bool any = false;
            for (int i = 0; i < count; i++) {
                var p = providers[i];
                if (!p.Entity.IsActive || !p.CanAddUser || !p.CanUse(being)) { continue; }
                any = true;
                var novelty = FunNovelty.Predict(data, FunSubjects.ForDevice(p.FunTile), ticks);
                if (novelty > best) {
                    best = novelty;
                    if (best >= 0.999f) { break; }
                }
            }
            Array.Clear(providers, 0, count);
            return any ? best : 1f;
        }
    }
}
