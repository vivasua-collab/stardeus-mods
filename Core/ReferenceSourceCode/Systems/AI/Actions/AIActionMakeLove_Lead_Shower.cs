using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Systems.Motion;
using Game.Systems.Mental;
using KL.Grid;
using Game.Systems.Drama;
using Game.Systems.Sleep;

namespace Game.Systems.AI {
    public sealed class AIActionMakeLove_Lead_Shower : AIAction, IAIActionMakeLove {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionMakeLove_Lead_Shower());
        }
        public const string ActId = "MakeLoveLead_Shower";
        public readonly static int ActIdH = Hashes.S(ActId);
        public override SocialChatContext ChatContext => SocialChatContext.NoChat;
        private AIActionMakeLove_Lead_Shower() : base(ActId, () => T.VerbMakingLove) {
            Preconditions = AIState.With(AIVarsH.MadeLoveWith_Lead, false);
            Outcomes = AIState.With(AIVarsH.MadeLoveWith_Lead, true);
            MarkAsWork();
            RequireMentalHealth();
            WithoutRequiredAbilities();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // inside the shower flag
            agent.BB.Bool1NotSaved = false;
            // speed modifier
            agent.BB.Object2 = null;
            agent.BB.Long1 = 0;
            // lover
            agent.BB.TargetBeing = null;
            agent.BB.TargetTile = null;
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            D.Warn("Ending make love (shower): {0} -> {1}", agent, goal);
            // inside the shower flag
            if (agent.BB.Bool1NotSaved) {
                agent.BB.Bool1NotSaved = false;
                agent.BB.ReservedSlot.RemoveVisually(agent.Being);
                agent.BB.ReservedSlot.RemoveVisually(agent.BB.TargetBeing);
            }
            agent.BB.ClearParticles();
            // lover
            agent.BB.TargetBeing = null;
            agent.BB.TargetTile = null;
            agent.BB.Long1 = 0;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearReservedSlot();
            if (agent.BB.Object2 is StatModifier mod) {
                agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(mod, false);
            }
            agent.BB.Object2 = null;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                agent.Motion.Progress.Flags |= MotionProgressFlags.IgnoreCongestion;
                return InProgress;
            }

            if (!AIActionMakeLove_Follow.EnsureTargetBeing(S, agent, goal)) {
                return Failed($"{T.Missing}: {T.NounLover}");
            }

            var lover = agent.BB.TargetBeing;

            // face each other when possible
            if (!lover.Motion.IsBusy) {
                if (Vectors.SqrDistance(agent.Position, lover.Position) < 0.25f * 0.25f) {
                    // push the date a little away from us so it wouldn't look creepy
                    lover.Motion.TeleportTo(agent.Position + (lover.Position - agent.Position).normalized * 0.33f);
                }
                FacePosition(agent, lover.Position);
                FacePosition(lover.AIAgent, agent.Position);
            }

            if (agent.TicksInCurrentAction > Consts.TicksPer5Minutes &&
                    lover.AIAgent.CurrentAction?.IdH != AIActionMakeLove_Follow.ActIdH) {
                // our follower is missing
                return Failed(T.DateCutShort_V1.F(lover.Data.Mind.LinkToShortName));
            }

            // where?
            if (agent.BB.TargetTile == null) {
                var slot = S.Sys.Slots.FindForDesignation<ISlots>(agent.BeingData,
                    SlotDesignationH.Shower, S.Sys.Path.Reg.Reach.For(agent.Motion),
                    agent.BB.Blacklist, null, out _);

                if (slot != null) {
                    if (!agent.BB.TryReserveSlot(slot, goal)) {
                        agent.BB.Blacklist.Add(slot.Parent.EntityId);
                        return InProgress;
                    }
                    var pos = slot.Parent.Tile.Transform.AnyPortInFirst;
                    var flags = PathReqFlags.AdjOnTop | PathReqFlags.TempDestination | PathReqFlags.Instant;
                    if (agent.Motion.TryGoTo(pos, flags, out agent.BB.MoveProgress)) {
                        if (lover.Motion.TryGoTo(pos, flags, out _)) {
                            agent.BB.TargetTile = slot.Parent.Tile;
                            return InProgress;
                        } else {
                            agent.Motion.Abort(T.Waiting);
                        }
                    }
                    D.Warn("did not go to slot pos: {0}", Pos.String(slot.SlotPosIdx));
                    agent.BB.UnreserveSlot(true, false);
                } else {
                    D.Err("Couldn't get shower slot");
                    return Failed(T.AdRejTargetUnavailable);
                }
            }

            if (agent.BB.TargetTile == null) {
                D.Warn("Not target tile");
                return InProgress;
            }

            if (agent.BB.TargetTile.TryGetComponent<ShowerComp>(out var shower2)) {
                var resSlot = agent.BB.ReservedSlot;
                if (resSlot == null) {
                    D.Warn("No reserved slot!");
                    agent.BB.TargetTile = null;
                    return InProgress;
                }
                if (lover.Motion.IsBusy) {
                    FacePosition(agent, lover.Position);
                    return InProgress;
                }
                if (lover.PosIdx == agent.PosIdx) {
                    if (!agent.BB.Bool1NotSaved) {
                        agent.BB.Bool1NotSaved = true;
                        if (agent.BB.Long1 == 0) {
                            agent.BB.Long1 = ticks;
                        }
                        // enter the shower
                        resSlot.PutVisually(agent.Being);
                        resSlot.PutVisually(lover);
                        agent.HideAllInfo();
                        lover.HideAllInfo();
                        AddParticles(S, agent);
                    } else {
                        // wait and show vfx
                        if (ticks - agent.BB.Long1 > Consts.TicksPer2Hours) {
                            // D.Warn("Ending after 2 hours: {0} - {1}", ticks, agent.BB.Long1);
                            goal.WithWorld(AIVarsH.MadeLoveWith_Lead, true);
                            return Success;
                        }
                    }
                } else {
                    // something is wrong!
                    return Failed($"{T.Missing}: {T.NounLover}");
                }
            }

            return InProgress;
        }

        private void AddParticles(GameState s, AIAgentComp agent) {
            if (agent.BB.Particles != null) { return; }
            var data = new ParticlesSys.Data() {
                Owner = agent.Being,
                IsEmitOnly = true,
                EmitChance = 1,
                PrefabName = "LoveFX",
                Play = true,
            };
            agent.BB.SetParticles(data);
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (!agent.S.Sys.Slots.HasFreeFor(SlotDesignationH.Shower)) {
                return 0f;
            }
            // less often in the shower, because bed has - 0.1-0.2
            return 1f - Rng.URange(0.0f, 0.15f);
        }
    }
}
