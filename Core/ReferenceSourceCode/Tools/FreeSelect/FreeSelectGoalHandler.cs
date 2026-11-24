using System;
using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Tools {
    public sealed class FreeSelectGoalHandler : FreeSelectHandler {
        public const string HandlerId = "FreeSelectGoal";

        [SelfInit]
        private static void Register() {
            Register(HandlerId, new FreeSelectGoalHandler());
        }

        public override int Order => 10;

        private readonly GoalHoverBoost hoverBoost = new();

        public override void Initialize(FreeSelectContext ctx) {
            base.Initialize(ctx);
            ctx.S.Query.LoadGoalTooltip.SetResponder(LoadGoalTooltip);
        }

        public override void OnHover(Vector2 pos) {
            ctx.HoveredGoals.Clear();
            ctx.HoveredPosOnFire = false;
            if (Pos.IsOOB(ctx.HoveredPos)) { return; }
            if (S.Sys.Firex.IsAnythingOnFire) {
                var firePos = ctx.HoveredPos;
                if (ctx.HoveredTile != null) {
                    firePos = ctx.HoveredTile.PosIdx;
                }
                if (S.Sys.Firex.TryGetFireAt(firePos, out var fire)) {
                    ctx.HoveredPosOnFire = true;
                    var sb = ctx.BeginTooltipBlock();
                    sb.AppendLine(Texts.Red(T.OnFire));
                    sb.AppendFormat("{0}: {1}\n", T.NounStrength, Units.Percentage(fire.Strength));
                    sb.AppendFormat("{0}: {1}\n", T.Oxygen, Units.Percentage(fire.O2));
                    if (fire.FireFuelAmt > 0) {
                        sb.AppendFormat("{0}: {1}\n", T.Fuel, fire.FireFuelAmt);
                    }
                    sb.AppendFormat("{0}: {1}", T.FireSpreadChance, Units.Percentage(
                        fire.SpreadStr * S.Sys.Firex.SpreadRateFor(fire.FlamIdx)));
                }
            }

            // if (ctx.SelectedBeings.Count > 1 || ctx.TargetBeingIsUnderDirectControl) {
            //     return;
            // }

            S.Query.AIGoalsAtPos.Ask(ctx.HoveredPos, ctx.HoveredGoals);
            hoverBoost.Update(ctx);
            int numGoals = ctx.HoveredGoals.Count;
            if (numGoals > 0) {
                CheckAssignableGoal();
            } else {
                if (ctx.HoveredBeing?.Data.IsFriendly == true) {
                    var hovered = ctx.HoveredBeing?.AIAgent.Goal;
                    if (hovered != null && hovered.Type != AIGoalType.Idle) {
                        AddGoalTooltip(hovered, ctx.HoveredBeing != ctx.TargetBeing);
                    }
                }
            }
        }

        private void CheckAssignableGoal() {
            AIAgentComp agent = null;
            if (ctx.SelectedBeings.Count == 1) {
                agent = ctx.TargetBeing?.AIAgent;
            }
            var numGoals = ctx.HoveredGoals.Count;
            int i = 0;
            if (agent != null) {
                var planner = S.Query.GetAIPlanner.Ask();
                foreach (var goal in ctx.HoveredGoals) {
                    if (AddGoalTooltip(goal, false)) {
                        i++;
                    }
                    if (!CheckAssignableGoal(agent, planner, goal)) {
                        break;
                    }
                    if (i == 3) { break; }
                }
            } else {
                foreach (var goal in ctx.HoveredGoals) {
                    if (AddGoalTooltip(goal, goal.Type == AIGoalType.Colony)) {
                        i++;
                    }
                    if (i == 3) { break; }
                }
            }
            if (i < numGoals) {
                ctx.Tooltip.AppendFormat(Texts.Grey("\n+{0}"), numGoals - i);
            }
        }

        private bool AddGoalTooltip(AIGoal goal, bool includeAgentName) {
            var sb = ctx.BeginTooltipBlock();
            return LoadGoalTooltip(goal, includeAgentName, sb);
        }

        private bool LoadGoalTooltip(AIGoal goal, bool includeAgentName, StringBuilder sb) {
            if (goal == null) { return false; }
            sb.Append(Texts.SubTitle(goal.TaskLine));
            bool needsNewLine = false;
            // if (includeAgentName && goal.Agent != null && goal.Type == AIGoalType.Colony) {
            //     needsNewLine = true;
            //     sb.AppendFormat(" {0} {1}: {2}", GlyphMap.ArrowRight,
            //         T.NounWorker, Texts.LinkToBeing(goal.Agent.BeingData));
            // }

            if (goal.Type == AIGoalType.Colony && goal.Target is IComponent comp) {
                if (needsNewLine) {
                    sb.AppendLine();
                }
                needsNewLine = true;

                if (comp.Entity is Being b) {
                    sb.AppendFormat(" {0} {1}: {2}", GlyphMap.ArrowRight, T.NounTarget, Texts.LinkToBeing(b.Data));
                } else if (comp.Entity is Tile t) {
                    sb.AppendFormat(" {0} {1}: {2}", GlyphMap.ArrowRight, T.NounTarget, t.NameLink);
                } else if (comp.Entity is Obj o) {
                    var link = Texts.LinkToObj(o);
                    if (o.Data.Mind != null) {
                        link = o.Data.Mind.DecorateListName(link);
                    }
                    sb.AppendFormat(" {0} {1}: {2}", GlyphMap.ArrowRight, T.NounTarget, link);
                } else {
                    D.Err("Unhandled entity type for goal tooltip: {0}", goal.Target.Entity);
                }
            }

            if (needsNewLine) {
                sb.AppendLine();
            }
            if (goal.StateStartTick > 0) {
                sb.AppendFormat(" {0} {1} ({2})", GlyphMap.ArrowRight, AIGoal.StateNameSimple(goal.State),
                        Units.TicksToTime(G.Ticks - goal.StateStartTick));
            } else {
                sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight, AIGoal.StateNameSimple(goal.State));
            }
            if (goal.Type == AIGoalType.Colony) {
                if (goal.Agent != null) {
                    sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight,
                        Texts.Green(T.AssignedToWorker_V1.F(Texts.LinkToBeing(goal.Agent.BeingData))));
                    if (goal.IsForceAssigned) {
                        sb.AppendFormat(" ({0})", Texts.Red(T.AdjForced));
                    }
                } else {
                    if (goal.DesiredState.Try(AIVarsH.CanFly, out var canFly)) {
                        if (canFly) {
                            sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight, T.ReqNavFlight);
                        }
                    }
                    if (goal.FailReason != null && AIGoal.IsFailed(goal)) {
                        sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight, Texts.Red(goal.FailReason));
                        var failedBy = goal.FailedAgentId == 0 ? null
                            : S.Query.FindBeingData.Ask(goal.FailedAgentId);
                        if (failedBy != null) {
                            sb.AppendFormat(" ({0})", Texts.LinkToBeing(failedBy));
                        }
                    }
                    // sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, T.TaskPriority, goal.GoalPriority);
                    if (goal.BoostedUntil > G.Ticks) {
                        sb.AppendFormat(" ({0}: {1})", Texts.Green(T.AdjectiveBoosted),
                            Units.TicksToTime(goal.BoostedUntil - G.Ticks));
                    }
                }
                var prio = goal.GoalPriority;
                string prioText;
                if (prio == 0) {
                    prioText = Texts.Red($"0 ({T.AdjForbidden})");
                } else if (prio == 1) {
                    prioText = $"1 {Texts.GreyParentheses(T.AdjPriorityLowest)}";
                } else if (prio == 5) {
                    prioText = $"5 {Texts.GreyParentheses(T.AdjPriorityHighest)}";
                } else {
                    prioText = Units.Num(prio);
                }
                sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, T.TaskPriority, prioText);
            }
            if (S.Research.IsUnlocked(DefIdsH.ResearchRoboticsNanobots)) {
                if (goal.IsNanobotWorkable) {
                    sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight, Texts.Green(T.NanobotReachable));
                } else if (goal.IsNanobotCompatible && S.Query.IsNanobotsJobEnabled.Ask(goal.JobIdH)) {
                    sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight, Texts.Yellow(T.NanobotUnreachable));
                }
            }
            if (goal.Target is PendingReplacementComp pr && pr.IsPending) {
                sb.Append("\n\n");
                pr.AppendReplacementInfo(sb);
            }
            return true;
        }

        public override bool AddSelectedBeingRightClickContext(
                int playerFaction, int canDC, int underDC,
                QuickChoice choice) {
            if (ctx.TargetBeingIsUnderDirectControl) { return false; }
            if (ctx.SelectedBeings.Count != 1) { return false; }
            var selB = ctx.SelectedBeings[0];
            var planner = S.Query.GetAIPlanner.Ask();
            int added = 0;
            if (ctx.HoveredPosOnFire) {
                added++;
                if (selB.AIAgent.Jobs.TryJob(JobTypeIdH.Firefighting, out var ffJob)) {
                    if (!ctx.TargetBeingIsControllable) {
                        choice.AddButton(Texts.Strike(T.FireExtinguish), null, T.NoDirectControlShort);
                    } else if (ffJob.Priority == 0) {
                        choice.AddButton(Texts.Strike(T.FireExtinguish), null, Texts.Red(T.BrainStatJobpriority0));
                    } else if (ctx.TargetBeingCanGoToHoverPos) {
                        choice.AddButton(T.FireExtinguish, () => {
                            var ticks = G.Ticks;
                            var forcedGoal = AIGoal.DirectOrder(JobTypeIdH.Firefighting,
                                    IconId.CFire, T.FireExtinguish, selB.AIAgent, ticks)
                                // A bit of a hack, initial world state can't be empty
                                // this way this works for both idle cleaning and forced cleaning
                                .WithVar(AIGoalVars.TargetPos, ctx.HoveredPos)
                                .WithWorld(AIVarsH.IsFireExtinguished, false)
                                .WithDesired(AIVarsH.IsFireExtinguished, true);
                            if (S.Query.ForceGoal.Ask(forcedGoal, true)) {

                            } else {
                                UISounds.PlayActionDenied();
                            }
                        });
                    } else {
                        choice.AddButton(Texts.Strike(T.FireExtinguish), null, Texts.Red(T.CannotReachPosition));
                    }
                } else {
                    choice.AddButton(Texts.Strike(T.FireExtinguish), null, Texts.Red(T.AdjIncapable));
                }
            }
            Caches.GoalSet.Clear();
            foreach (var g in ctx.HoveredGoals) {
                if (CanAssignGoal(selB.AIAgent, planner, g, out var failReason)) {
                    Caches.GoalSet.Add(g);
                    added++;
                    Being curWorker = null;
                    string actText;
                    if (AIGoal.IsActive(g) && g.Agent != null) {
                        curWorker = g.Agent.Being;
                        actText = T.VerbReassign;
                    } else {
                        actText = T.VerbAssign;
                    }

                    var btn = new QuickChoice.Button() {
                        OnClick = () => {
                            if (!g.IsAllowedFor(selB.AIAgent)) {
                                S.Sig.ShowErrorMessage.Send(T.OperatableWorkerNotAllowed);
                                return;
                            }
                            if (BeingUtils.TryAssignGoal(G.Ticks, g, selB.AIAgent)) {

                            } else {
                                UISounds.PlayActionDenied();
                            }
                        },
                        TipFunc = () => {
                            ctx.Tooltip.Clear();
                            AddGoalTooltip(g, true);
                            return ctx.Tooltip.ToString();
                        },
                        AutoClose = true,
                    };
                    choice.AddButton($"{actText}: {g.Name}", btn);
                        // .WithIconHoverFunction((s) => {
                        //     if (AIGoal.IsActive(g) && g.Agent != null) {
                        //         var curWorker = g.Agent.Being;
                        //         EntityUtils.ShowEntityLink(curWorker, g.Target.Entity, s);
                        //     } else {
                        //         EntityUtils.ShowEntityLink(null, g.Target.Entity, s);
                        //     }
                        // })
                        // .WithClickFunction(() => {
                        //     if (BeingUtils.TryAssignGoal(G.Ticks, g, selB.AIAgent)) {
                        //
                        //     } else {
                        //         UISounds.PlayActionDenied();
                        //     }
                        // })
                        // .WithIconClickFunction(() => {
                        //     //S.Sig.CameraFocusRooted.Send(g.Target.Entity, curWorker);
                        //     var ui = AIGoalUI.For(S, g);
                        //     S.Sig.ShowContextMenu.Send(ui);
                        // });
                    // res.Add(udb);
                }
            }
            if (ctx.HoveredGoals.Count > Caches.GoalSet.Count) {
                foreach (var g in ctx.HoveredGoals) {
                    // already added UI
                    if (Caches.GoalSet.Contains(g)) { continue; }
                    if (!CanAssignGoal(selB.AIAgent, planner, g, out var failReason)) {
                        added++;
                        var btn = new QuickChoice.Button() {
                            OnClick = () => UISounds.PlayActionDenied(),
                            TipFunc = () => {
                                ctx.Tooltip.Clear();
                                ctx.Tooltip.AppendFormat(Texts.Red(failReason));
                                // AddGoalTooltip(g, true);
                                return ctx.Tooltip.ToString();
                            },
                            AutoClose = false,
                        };
                        choice.AddButton($"{Texts.Red(T.CantAssign)}: {Texts.Strike(g.Name)}", btn);

                        // var udb = UDB.Create(prov, UDBT.DText, g.GoalIcon, Texts.Strike(g.Name))
                        //     .WithText(Texts.Red(T.CantAssign))
                        //     .WithIconHoverFunction((s) => {
                        //         if (AIGoal.IsActive(g) && g.Agent != null) {
                        //             var curWorker = g.Agent.Being;
                        //             EntityUtils.ShowEntityLink(curWorker, g.Target.Entity, s);
                        //         } else {
                        //             EntityUtils.ShowEntityLink(null, g.Target.Entity, s);
                        //         }
                        //     })

                            // .WithTooltipFunction((b) => {
                            //     ctx.Tooltip.Clear();
                            //     ctx.Tooltip.AppendFormat(Texts.Red(failReason));
                            //     // AddGoalTooltip(g, true);
                            //     return ctx.Tooltip.ToString();
                            // });
                            // .WithIconClickFunction(() => {
                            //     // S.Sig.CameraFocusRooted.Send(g.Target.Entity, curWorker);
                            //     var ui = AIGoalUI.For(S, g);
                            //     S.Sig.ShowContextMenu.Send(ui);
                            // });
                        // res.Add(udb);
                    }
                }
            }
            return added > 0;
        }

        private bool IsGoalReachable(AIGoal goal) {
            if (ctx.TargetBeingCanGoToHoverPos) { return true; }
            // check if there is something above that position or it is a wall
            if (goal.Target.LayerId != WorldLayer.Walls) {
                if (goal.Target.LayerId == WorldLayer.Floor) {
                    var topmost = EntityUtils.GetTopmostParentTile(ctx.HoveredPos);
                    if (topmost != null && topmost.Definition.LayerId != WorldLayer.Floor && topmost.IsConstructed) {
                        // something is above, let's allow that
                        return true;
                    }
                }
            }
            return false;
        }

        private bool CanAssignGoal(AIAgentComp agent,
                IAIPlanner planner, AIGoal goal, out string because) {
            if (agent == goal.Agent && !AIGoal.IsFailed(goal)) {
                because = Texts.Green(T.AssignedToSelectedWorker);
                return false;
            }
            if (!agent.DirectControl.IsControllable) {
                because = Texts.Red(T.AdjNotControllable);
                return false;
            }
            if (!goal.IsAllowedFor(agent)) {
                because = T.OperatableWorkerNotAllowed;
                return false;
            }
            if (!IsGoalReachable(goal)) {
                because = T.CannotReachPosition;
                return false;
            }
            if (!agent.Jobs.CanDo(goal.JobIdH, out because)) {
                return false;
            }
            // The job can be doable while this goal is not: a Manipulator gives Cleaning through
            // Clean Device, but floor dirt needs Clean Floor. The planner would only say Incapable.
            if (!BeingUtils.HasAbilities(agent.BeingData, goal.RequiredAbilities)) {
                var sb = Caches.StringBuilder;
                sb.Clear();
                BeingUtils.AppendLackingAbilities(sb, agent.BeingData, goal.RequiredAbilities);
                because = sb.ToString();
                return false;
            }

            if (!planner.TryFormulateFor(goal, agent, G.Ticks, out var plan)) {
                because = T.AdjIncapable;
                return false;
            }
            return true;
        }
        /// <returns>true if should try to show remaining goals</returns>
        private bool CheckAssignableGoal(AIAgentComp agent,
                IAIPlanner planner, AIGoal goal) {
            // D.Warn("Checking goal: {0} agent: {1}", goal, agent);
            var sb = ctx.Tooltip;
            if (goal.Agent == agent) {
                if (!AIGoal.IsFailed(goal.State)) {
                    return false;
                }
            }
            // Each line names the selected worker, as these are about them, not the
            // worker the goal is assigned to
            var name = agent.BeingData.Name;
            if (!goal.IsAllowedFor(agent)) {
                sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, name,
                    Texts.Red(T.OperatableWorkerNotAllowed));
                return true;
            }
            if (!agent.Jobs.CanDo(goal.JobIdH, out string because)) {
                sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, name, Texts.Red(because));
                return true;
            }
            if (!BeingUtils.HasAbilities(agent.BeingData, goal.RequiredAbilities)) {
                sb.AppendFormat("\n {0} {1}: ", GlyphMap.ArrowRight, name);
                BeingUtils.AppendLackingAbilities(sb, agent.BeingData, goal.RequiredAbilities);
                return true;
            }

            // we already have calculated an accurate estimate by now
            // no need for inaccurate region check
            // int regDist = AIAuction.CheckReachable(S, agent, goal, true, true);
            // if (regDist == -1) {
            //     return true;
            // }
            if (!IsGoalReachable(goal)) {
                // show remaining goals
                return true;
            }


            if (planner.TryFormulateFor(goal, agent, G.Ticks, out var plan)) {
                // sb.AppendLine(Texts.Green(goal.TaskLine));

                // region dist check worked, so perhaps let's ignore target being can go to hover pos
                // We don't want to loop through all remaining goals
                return false;
            } else {
                sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, name, Texts.Red(T.AdjIncapable));
            }
            return true;
        }

    }
}
