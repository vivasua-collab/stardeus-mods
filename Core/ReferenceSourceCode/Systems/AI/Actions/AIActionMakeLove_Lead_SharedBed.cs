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
    public sealed class AIActionMakeLove_Lead_SharedBed : AIAction, IAIActionMakeLove {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionMakeLove_Lead_SharedBed());
        }
        public const string ActId = "MakeLoveLead_SharedBed";
        public readonly static int ActIdH = Hashes.S(ActId);
        public override SocialChatContext ChatContext => SocialChatContext.NoChat;
        private AIActionMakeLove_Lead_SharedBed() : base(ActId, () => T.VerbMakingLove) {
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
            // stage
            agent.BB.Int1 = 0;
            agent.BB.Long1 = 0;
            // lover
            agent.BB.TargetBeing = null;
            agent.BB.TargetTile = null;
            // bed slot
            agent.BB.Object1 = null;
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            D.Warn("Ending make love (bed): {0} -> {1}", agent, goal);
            // inside the bed flag
            if (agent.BB.Bool1NotSaved) {
                agent.BB.Bool1NotSaved = false;
                if (agent.BB.TargetTile.TryGetComponent<BedComp>(out var bed)) {
                    var slot = bed.SlotFor(agent.BeingData);
                    slot.RemoveVisually(agent.Being);
                    slot.RemoveVisually(agent.BB.TargetBeing);
                }
            }
            agent.BB.ClearParticles();
            // lover
            agent.BB.TargetBeing = null;
            agent.BB.TargetTile = null;
            // stage
            agent.BB.Int1 = 0;
            agent.BB.Long1 = 0;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearReservedSlot();
            // bed slot
            agent.BB.Object1 = null;
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
            if (!lover.Motion.IsBusy && !agent.BB.Bool1NotSaved) {
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


            if (agent.BB.Object1 is not BedSlot slot) {
                // where?
                if (agent.BB.TargetTile == null) {
                    BedComp tempBed = null;
                    if (Rng.UChance(0.25f)) {
                        // sofa
                        tempBed = S.Query.FindTemporaryBed.Ask(agent.BeingData, true);
                    }
                    if (tempBed == null) {
                        // owned bed
                        tempBed = S.Query.FindBed.Ask(agent.BeingData, true);
                    }
                    if (tempBed == null) {
                        // try any non-temporary bed, even assigned ones
                        tempBed = S.Query.FindTemporaryBed.Ask(agent.BeingData, false);
                    }
                    if (tempBed == null) {
                        return Failed($"{T.AdRejTargetUnavailable}: {T.NounBed}");
                    }
                    agent.BB.TargetTile = tempBed.Tile;
                }
                slot = agent.BB.TargetTile.GetComponent<BedComp>().SlotFor(agent.BeingData);

                if (slot != null) {
                    var flags = PathReqFlags.AdjOnTop | PathReqFlags.TempDestination | PathReqFlags.Instant;
                    var pos = slot.InPort;
                    if (agent.Motion.TryGoTo(pos, flags, out agent.BB.MoveProgress)) {
                        if (lover.Motion.TryGoTo(pos, flags, out _)) {
                            agent.BB.Object1 = slot;
                            return InProgress;
                        } else {
                            agent.Motion.Abort(T.Waiting);
                        }
                    }
                    D.Err("failed to go to bed: {0}", agent.BB.TargetTile);
                    return Failed(T.AdRejTargetUnavailable);
                } else {
                    D.Err("Couldn't get bed slot");
                    return Failed(T.AdRejTargetUnavailable);
                }
            }


            if (slot != null) {
                if (slot == null) {
                    D.Warn("No bed slot!");
                    agent.BB.TargetTile = null;
                    return InProgress;
                }
                if (lover.Motion.IsBusy) {
                    FacePosition(agent, lover.Position);
                    return InProgress;
                }
                if (EntityUtils.IsNearObj(lover.PosIdx, agent.BB.TargetTile)) {
                    if (!agent.BB.Bool1NotSaved) {
                        agent.BB.Bool1NotSaved = true;
                        if (agent.BB.Long1 == 0) {
                            agent.BB.Long1 = ticks;
                        }
                        // enter the shower
                        slot.PutVisually(agent.Being, 0.1f);
                        slot.PutVisually(lover, -0.1f);
                        SetRotation(agent.Being, slot);
                        SetRotation(lover, slot);
                        agent.HideAllInfo();
                        lover.HideAllInfo();
                        AddParticles(S, agent);
                    } else {
                        // wait and show vfx
                        if (ticks - agent.BB.Long1 > Consts.TicksPer2Hours) {
                            // D.Warn("Ending after 2 hours: {0} - {1}", ticks, agent.BB.Long1);
                            goal.WithWorld(AIVarsH.MadeLoveWith_Lead, true);
                            return Success;
                        } else {
                            if (Rng.UChance(0.25f)) {
                                agent.Being.Graphics.SetRotation(Rng.URange(-10f, 10f), true);
                            }
                            if (Rng.UChance(0.25f)) {
                                lover.Graphics.SetRotation(Rng.URange(-10f, 10f), true);
                            }
                        }
                    }
                } else {
                    // something is wrong!
                    return Failed($"{T.Missing}: {T.BedSlotMain}");
                }
            }

            // 1. double bed (if agent has one assigned with lover)
            // 2. shower
            // 3. sleeping pod (if agent or lover has one assigned)
            // 4. any surface (with censorship)

            return InProgress;
        }

        private static void SetRotation(Being worker, BedSlot bedSlot) {
            if (worker.Species.IsAnimal) {
                worker.Graphics.SetFacing(Rng.UFrom(Facing.Types));
                worker.Graphics.SetRotation(Rng.URange(-45f, 45f), true);
            } else {
                worker.Graphics.SetFacing(bedSlot.Rotation);
                worker.Graphics.SetRotation(Rng.URange(-10f, 10f), true);
            }
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
            return 1f - Rng.URange(0.1f, 0.2f);
        }
    }
}
