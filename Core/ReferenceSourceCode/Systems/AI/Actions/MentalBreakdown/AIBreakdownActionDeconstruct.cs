using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using KL.Grid;
using Game.AI;
using Game.Systems.Mental;
using Game.Systems.Stats;

namespace Game.Systems.AI {
    public sealed class AIBreakdownActionDeconstruct : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionDeconstruct());
        }
        public const string ActId = "BreakdownDeconstruct";
        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownBreakdowndeconstructDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }
        protected override int AiVar => AIVarsH.MentalDeconstructed;
        private AIBreakdownActionDeconstruct() : base(ActId, () => T.BreakdownBreakdowndeconstruct) {
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.Build);
            WithForbiddenFlags(StatFlags.Animal);
            WithRequiredJobType(JobTypeIdH.Demolition);
            // WithRequiredJobType adds work flag, but breakdowns are all rest
            Flags &= ~Actions.AIActionFlags.Work;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }

            if (agent.BB.TargetTile != null && agent.BB.TargetTile.IsConstructed) {
                if (EntityUtils.IsNearObj(agent.PosIdx, agent.BB.TargetTile)) {
                    // we are near the tile!
                    if (agent.Jobs.TryJob(JobTypeIdH.Demolition, out var slot)) {
                        float workAmount = slot.UseSkillAtWorkSpeed(SkillXp.Construction / 15f);
                        return PerformDeconstruction(
                            S, agent, workAmount, ticks);
                    } else {
                        agent.BB.ClearMoveTarget();
                        agent.BB.TargetTile = null;
                    }

                } else if (!TryGoToOnce(agent, agent.BB.TargetTile)) {
                    agent.BB.ClearMoveTarget();
                    agent.BB.TargetTile = null;
                }

                return InProgress;
            }

            // find target
            if (agent.BB.Long1NotSaved == 0) {
                agent.BB.Long1NotSaved = ticks + Rng.URange(
                    Consts.TicksPer10Minutes, Consts.TicksPer20Minutes);
                return InProgress;
            } else if (agent.BB.Long1NotSaved < ticks) {
                // subsequent deconstructions
                agent.BB.Long1NotSaved = ticks + Rng.URange(
                    Consts.TicksPerHour, Consts.TicksPer3Hours);
                if (agent.BB.Byte1 > 0) {
                    agent.BB.Byte1--;
                } else {
                    // significantly reduce the chance if we
                    // deconstructed quite enough
                    if (Rng.UChance(0.5f)) {
                        agent.BB.TargetTile = null;
                        return InProgress;
                    }
                }

                Tile target = null;
                if (S.Difficulty.Level > 0.9f && Rng.UChance(0.05f)) {
                    // Try to target a wall
                    var areaId = S.Sys.Areas.At(agent.PosIdx);
                    if (areaId > 0) {
                        var outlineCache = Caches.IntSet;
                        outlineCache.Clear();
                        S.Sys.Areas.OutlineOfArea(areaId, outlineCache);
                        var randomWall = Rng.UFrom(outlineCache);
                        target = EntityUtils.WallAt(randomWall);
                        if (target != null) {
                            if (!target.IsConstructed) {
                                target = null;
                            }
                        }
                    }
                }

                target ??= S.Query.FindTileNearby.Ask(
                    agent.PosIdx, agent.Motion.IsGrounded, 4,
                        WorldLayer.Objects, (t) => {
                    var c = t.Constructable;
                    if (!c.IsConstructed) { return false; }
                    if (t.Definition.IsImportant) { return false; }
                    if (!agent.IsFeelingSafeAt(t.Transform.WorkSpotOrPos)) {
                        return false;
                    }
                    return true;
                });

                if (target != null) {
                    agent.BB.ClearMoveTarget();
                    agent.BB.TargetTile = target;
                }
                return InProgress;
            }

            if (agent.TicksInCurrentAction < Consts.TicksPer16Hours
                    && agent.BeingData.StatsBeing.Sanity.Value < agent.BB.Float1) {
                agent.BB.ClearMoveTarget();
                if (Rng.UChance(0.8f)) {
                    return InProgress;
                }
                if (AIActionIdleRoam.TryRoam(agent, false, Rng.URange(1, 4), true)) {
                    return InProgress;
                } else {
                    var pos = AIActionFindSafety.FindSafePos(S, agent, agent.PosIdx);
                    if (pos != Pos.Invalid) {
                        if (agent.Motion.TryGoTo(pos, PathReqFlags.AdjOnTop | PathReqFlags.UnsafeOK)) {
                            return InProgress;
                        }
                    }
                }
            }
            return EndBreakdown(agent, goal);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            // deconstruction target
            agent.BB.TargetTile = null;
            // when we will do next deconstruction
            agent.BB.Long1NotSaved = 0;
            // sanity target
            agent.BB.Float1 = Rng.URange(0.5f, 0.6f);
            // how many tiles we can deconstruct
            var maxTiles = Mathf.CeilToInt(agent.S.Difficulty.Level * 3) + 1;
            var minTiles = maxTiles / 2;
            if (minTiles < 1) {
                minTiles = 1;
            }
            agent.BB.Byte1 = (byte) Rng.URange(minTiles, maxTiles);
            agent.BeingData.StatsBeing.MoveSpeed.AddModifier(
                AIActionIdleRoam.StatModRoam, false);
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            // deconstruction target
            agent.BB.TargetTile = null;
            // when we will do next deconstruction
            agent.BB.Long1NotSaved = 0;
            agent.BB.Byte1 = 0;
            agent.BB.Float1 = 0f;
            agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(
                AIActionIdleRoam.StatModRoam, false);
            agent.BB.ClearFX();
            base.OnDeactivate(agent, goal, ticks);
        }

        private AIActionResult PerformDeconstruction(
            GameState S, AIAgentComp agent,
                float workAmount, long ticks) {
            var tile = agent.BB.TargetTile;
            FacePosition(agent, tile.Position);
            var worker = agent.Being;
            var c = tile.Constructable;
            if (!c.IsConstructed) {
                return InProgress;
            }
            if (c.AddDeconstruction(workAmount)) {
                c.BreakDown(2f,
                    moveMaterialsAside: false,
                    orderHaul: false,
                    autoRebuild: true);
                var msg = T.BreakdownDeconstructLog_V2.F(
                        c.Tile.Definition.CodexLink,
                            Pos.String(c.Tile.CenterIdx));
                S.Sys.Log.AddLine(worker, Texts.Beautify(msg), true);
                S.Sig.ShowFanfare.Send(Texts.Yellow(
                    $"{Texts.LinkToBeing(worker.Data)}: {msg}"));
                agent.BB.ClearFX();
                agent.BB.TargetTile = null;
                agent.AddMentalEffect(MentalEffect.Create(agent.PosIdx,
                    ticks, Consts.TicksPer4Hours, NameT, 100));
                // S.Situation.AddTension(1f, "Breakdown: Deconstruction");
            } else {
                AIActionDeconstruct.ShowFX(S, agent, worker, tile);
            }
            return InProgress;
        }
    }
}
