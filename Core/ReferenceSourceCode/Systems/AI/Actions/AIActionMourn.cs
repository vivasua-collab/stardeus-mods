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
using Game.Systems.Motion;
using Game.Achievements;
using Game.Systems.Mental;
using System.Collections.Generic;
using Game.Systems.Drama;

namespace Game.Systems.AI {
    public sealed class AIActionMourn : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionMourn());
        }
        public const string ActId = "Mourn";
        public static readonly int ActIdH = Hashes.S(ActId);
        public override SocialChatContext ChatContext => SocialChatContext.NoChat;
        private AIActionMourn() : base(ActId, () => T.VerbMourning) {
            Preconditions = AIState.With(AIVarsH.Mourned, false);
            Outcomes = AIState.With(AIVarsH.Mourned, true);
            MarkAsQuiet();
            RequireMentalHealth();
            WithoutRequiredAbilities();
            WithRequiredJobType(JobTypeIdH.Social);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            // mind
            agent.BB.Object1 = null;
            // target
            agent.BB.TargetEntity = null;
            // time left
            agent.BB.Int1 = 0;
            // gave bonus
            agent.BB.Bool1NotSaved = false;
        }
        public static bool EnsureTargetMind(GameState S, AIAgentComp agent,
                AIGoal goal, out Mind mind) {
            if (agent.BB.Object1 is Mind m) {
                mind = m;
                return true;
            }
            if (goal.TryGetVar(AIGoalVars.MindId_Uint, out uint mindId)) {
                mind = S.Query.FindMind.Ask(mindId);
                if (mind == null) {
                    return false;
                }
                agent.ShowThoughtBubble(IconId.CFuneralRibbon,
                    T.ActMourningSomeone_V1.F(mind.LinkToShortName));
                agent.BB.Object1 = mind;
                return true;
            }
            mind = null;
            D.Err("Mourn goal does not have a var: {0} | {1}", AIGoalVars.MindId_Uint, goal);
            return false;
        }

        private bool FindMournTarget(GameState s, AIAgentComp agent, Mind mind,
                out Entity target) {
            target = agent.BB.TargetEntity;
            if (target != null) { return true; }

            // corpse with brain
            if (mind.Owner != null) {
                var bd = mind.Owner;
                target = FindTargetFor(s, bd);
                // return target != null;
            }

            // corpse with brain removed
            if (mind.FormerOwnerId != 0) {
                var corpse = s.Query.FindBeingData.Ask(mind.FormerOwnerId);
                if (corpse != null) {
                    target = FindTargetFor(s, corpse);
                }
            }

            // ashes or brain body part (stored or not)
            if (mind.ObjId != 0) {
                var remains = s.Query.FindObjData.Ask(mind.ObjId);
                if (remains != null) {
                    if (remains.Obj != null) {
                        target = remains.Obj;
                    } else if (remains.StorageId != 0) {
                        target = s.FindEntity<Tile>(remains.StorageId);
                    }
                }
            }

            return target != null;
        }

        private static Entity FindTargetFor(GameState s, Creatures.BeingData bd) {
            Entity target = null;
            if (bd.Being != null) {
                target = bd.Being;
            } else if (bd.StorageId != 0) {
                target = s.FindEntity<Tile>(bd.StorageId);
            }

            return target;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                if (agent.IsFeelingSafeNow) {
                    return InProgress;
                }
            }
            if (!EnsureTargetMind(S, agent, goal, out Mind mind)) {
                return Failed(T.AdRejTargetUnavailable);
            }

            // find body or ashes or urn, or brain (stored or not)
            if (!FindMournTarget(S, agent, mind, out var target)) {
                return Failed(T.AdRejTargetUnavailable);
            }

            // 2. go near
            if (!EntityUtils.IsNearObj(agent.PosIdx, target)) {
                if (!TryGoToOnce(agent, target, true,
                        extraFlags: PathReqFlags.NonDiagonal, tryOnTop: false)) {
                    // todo penalize
                    return FailedNavToTarget(agent);
                }
                return InProgress;
            } else {
                if (target is not Tile tile || (tile.Transform.Width == 1 && tile.Transform.Height == 1)) {
                    if (Pos.N(target.PosIdx) != agent.PosIdx
                            && Vectors.SqrDistance(agent.Position, target.Position) > 0.7f * 0.7f) {
                        // push the being closer
                        var pos = Vector2.MoveTowards(agent.Position, target.Position, 0.5f);
                        agent.Being.Motion.TeleportTo(pos);
                    }
                }
                FacePosition(agent, target.Position);
            }

            if (agent.BB.Int1 == 0) {
                // how long will this last
                agent.BB.Int1 = Rng.URange(Consts.TicksPer1Hours, Consts.TicksPer2Hours);
            }

            var ticksLeft = --agent.BB.Int1;
            if (ticksLeft < 1) {
                TryWalkAway(agent);
                // did mourn, give mood bonus
                AddMournBonus(agent, ticks, mind, target);
                goal.WithWorld(AIVarsH.Mourned, true);
                return Success;
            }

            // still mourning
            return InProgress;
        }

        private void AddMournBonus(AIAgentComp agent, long ticks, Mind mind, Entity target) {
            agent.BB.Bool1NotSaved = true;
            agent.AddMentalEffect(
                MentalEffect.CreateWithIcon(target.CenterIdx, IconId.CFuneralRibbon,
                    ticks, Consts.TicksPer6Hours,
                        T.MournedSomeone_V1.F(mind.LinkToShortName), 5)
                            .WithTooltip(T.EvMournedOkDesc_V2.F(
                                Texts.LinkToBeing(agent.BeingData),
                                Texts.LinkToMind(mind))));
        }
        private void AddMournFailDebuff(AIAgentComp agent, long ticks, Mind mind, Entity target) {
            var pos = target?.CenterIdx ?? agent.PosIdx;
            agent.AddMentalEffect(
                MentalEffect.CreateWithIcon(pos, IconId.CFuneralRibbon,
                    ticks, Consts.TicksPer8Hours,
                        T.MournedSomeone_V1.F(mind.LinkToShortName), -8)
                            .WithTooltip(T.EvMournedFailDesc_V2.F(
                                Texts.LinkToBeing(agent.BeingData),
                                Texts.LinkToMind(mind))));
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.Bool1NotSaved != true) {
                if (agent.BB.Object1 is Mind mind) {
                    AddMournFailDebuff(agent, ticks, mind, agent.BB.TargetEntity);
                }
                agent.BB.Bool1NotSaved = false;
            }
            agent.BB.Object1 = null;
            agent.BB.ClearMoveTarget();
            // target
            agent.BB.TargetEntity = null;
            // admiration time left
            agent.BB.Int1 = 0;
            agent.HideThoughtBubble();
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
