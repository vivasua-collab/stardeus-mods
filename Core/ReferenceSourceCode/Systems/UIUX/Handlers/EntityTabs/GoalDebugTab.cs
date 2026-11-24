using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class GoalDebugTab : BaseEntityTab {
        public const string TabId = "GoalDebug";

        /// <summary>
        /// Toggled by <c>dev debug_tasks</c> console command. Also set to true when any
        /// being with logging enabled is loaded from a save.
        /// </summary>
        public static bool Enabled;

        private const int MaxEntries = 10000;
        private const int PageSize = 100;

        [SelfInit]
        private static void Register() {
            RegisterTab(new GoalDebugTab());
        }

        // ── static logging API (called from AIAgentComp) ─────────────────────────────

        /// <summary>Called from AIAgentComp.SetGoal after OnSetGoal.Send.</summary>
        public static void LogStart(AIAgentComp agent, AIGoal goal, AIPlan plan) =>
            Append(agent, goal, plan, true, default);

        /// <summary>Called from AIAgentComp.ClearGoal before Goal.SetAgent(null).</summary>
        public static void LogEnd(AIAgentComp agent, AIGoal goal, AIGoalState state) =>
            Append(agent, goal, null, false, state);

        private static void Append(AIAgentComp agent, AIGoal goal, AIPlan plan, bool isStart, AIGoalState state) {
            // Gate first — no string work until we know logging is on (perf).
            if (goal == null || agent?.BeingData == null) { return; }
            var b = agent.BeingData;
            if (!b.LogGoalChanges) { return; }
            if (goal.Type == AIGoalType.Idle && !b.LogIdleGoals) { return; }
            // Only after the gate: resolve status string and format the log line.
            var status = isStart
                ? "start"
                : $"{StatusFor(goal)} in {Units.TicksToTime(G.Ticks - agent.GoalStartTick, true)}";
            var lastAction = isStart ?  plan?.ToString() : agent.LastAction?.Id;
            (b.GoalChanges ??= new()).Add(FormatEntry(agent, goal, status, lastAction));
            if (b.GoalChanges.Count > MaxEntries) {
                b.GoalChanges.RemoveRange(0, b.GoalChanges.Count - MaxEntries);
            }
            agent.RaiseGoalDebugChanged();
        }

        /// <summary>
        /// Change this method to alter the entry format.
        /// Format: [day|HH:mm][beingPos][goalPos][status][goalId][jobName][goalName][target]
        /// </summary>
        public static string FormatEntry(AIAgentComp agent, AIGoal goal, string status,
                string plan = null) {
            var date = agent.S.Clock.ToDateTime(Consts.TickDurationInGameTime);
            var day = Texts.DayFrom(date);
            var hhmm = date.ToString(Consts.TimeFormat);
            if (!agent.BeingData.Species.IsBiological) {
                var batCharge = agent.BeingData.StatsBeing.BatteryCharge.Value;
                var batCap = agent.BeingData.StatsBeing.BatteryCapacity.Value;
                if (batCap > 0) {
                    var batPct = batCharge / batCap;
                    hhmm = $"{hhmm} Bat:{Units.Percentage(batPct)}";
                }
            }
            var beingPos = Texts.LinkToPos(agent.PosIdx);
            var goalPos = goal.Target?.Entity != null
                ? goal.Target.Entity.PosIdx : agent.PosIdx;
            if (goalPos != agent.PosIdx) {
                beingPos = $"{beingPos}->{Texts.LinkToPos(goalPos)}";
            }
            string target = string.Empty;
            if (goal.Target != null) {
                target = $"@ {goal.Target?.Entity.Definition.NameT} ";
            }
            if (plan != null) {
                target = $"{target}[{plan}] ";
            }
            return $"[{day}|{hhmm}][{status}]"
                 + $" {goal.JobNameT} > {goal.Name} {target}{beingPos}";
        }

        /// <summary>Maps an AIGoalState to a short human-readable status token.</summary>
        public static string StatusFor(AIGoal goal) {
            if (goal == null) {
                return "error!";
            }
            var state = goal.State;
            if (AIGoal.IsCompleted(state)) { return "complete"; }
            if (AIGoal.IsCancelled(state)) { return "cancel"; }
            if (AIGoal.IsInterrupted(state)) { return "interrupt"; }
            if (AIGoal.IsFailed(state)) {
                if (goal.FailReason != null) {
                    return $"fail({goal.FailReason})";
                }
                return "fail";
            }
            return state.ToString().ToLowerInvariant();
        }

        // ── instance members ──────────────────────────────────────────────────────────

        private GameState S;
        private BeingData being;

        public override string Id => TabId;
        public override int Order => 95;
        public override string TabName => "Debug Tasks";

        public override bool ShowFor(GameState s, BeingData being) {
            if (!Enabled || being?.Being?.AIAgent == null) { return false; }
            S = s;
            this.being = being;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            // Wire live refresh first so new entries trigger a re-render even when the
            // list is currently empty (binding is scoped to this tab's lifetime).
            being.Being.AIAgent.GoalDebugChangedB.Bind(lifetime, () => ReloadTab(), false);

            var entries = being.GoalChanges;
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;
            if (entries == null || entries.Count == 0) {
                var txt = UISpawn.Widget<WText>(lifetime,
                        UIPrefabId.WContentTextBlock,block.Container, idx++);
                if (being.LogGoalChanges) {
                    txt.Load("No debug entries yet, wait for the being to take some tasks");
                } else {
                    txt.Load("Enable task debugging in the footer to start collecting data.");
                }
                return true;
            }

            // Newest first: iterate from the end of the list.
            int loadCount = Math.Min(PageSize, entries.Count);
            for (int i = entries.Count - 1; i >= entries.Count - loadCount; i--) {
                var row = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2TextBlock, block.Container, idx++);
                row.Load(entries[i]);
            }

            if (entries.Count > PageSize) {
                var scroll = block.Container.GetComponentInParent<ScrollRect>();
                var btn = UISpawn.Widget<WButtonText>(lifetime,
                    UIPrefabId.WContent2ButtonSimple, block.Container, idx);
                LoadMoreAction(btn, block.Container, entries, entries.Count - loadCount, scroll);
            }

            return true;
        }

        private void LoadMoreAction(WButtonText btn, RectTransform container,
                List<string> entries, int remainingStart, ScrollRect scroll) {
            int remaining = remainingStart;
            btn.Load(Texts.Italic($"+{remaining}"), () => {
                int sibIdx = btn.transform.GetSiblingIndex();
                int batch = Math.Min(PageSize, remaining);
                int fromIdx = remaining - batch;
                for (int i = 0; i < batch; i++) {
                    var row = UISpawn.Widget<WText>(lifetime,
                        UIPrefabId.WContent2TextBlock, container, sibIdx + i);
                    row.Load(entries[fromIdx + i]);
                }
                int newRemaining = remaining - batch;
                if (newRemaining > 0) {
                    btn.transform.SetSiblingIndex(sibIdx + batch);
                    LoadMoreAction(btn, container, entries, newRemaining, scroll);
                } else {
                    btn.SetActiveFast(false);
                }
                if (scroll != null) {
                    UIUtils.RebuildScroll(scroll, false);
                }
            });
        }

        public override bool LoadFooter(WGroup group) {
            int idx = 0;

            var loggingToggle = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WFooterItem_Toggle, group.Container, idx++);
            loggingToggle.Load("Enable Logging", being.LogGoalChanges, isOn => {
                being.LogGoalChanges = isOn;
                ReloadTab();   // show/hide the "Log Idle Goals" checkbox
            });

            if (being.LogGoalChanges) {
                var idleToggle = UISpawn.Widget<WToggleText>(lifetime,
                    UIPrefabId.WFooterItem_Toggle, group.Container, idx++);
                idleToggle.Load("Log Idle", being.LogIdleGoals, isOn => {
                    being.LogIdleGoals = isOn;
                });

                if (being.GoalChanges?.Count > 0) {
                    var copyBtn = UISpawn.Widget<WButtonIconText>(lifetime,
                        UIPrefabId.WPanelButtonIcon, group.Container, idx++);
                    copyBtn.Load(IconId.CCopySettings, $"Copy ({being.GoalChanges.Count})", () => {
                        if (being.GoalChanges == null || being.GoalChanges.Count == 0) { return; }
                        GUIUtility.systemCopyBuffer = string.Join("\n", being.GoalChanges);
                    });
                    UIUtils.AddTip(lifetime, copyBtn, T.CopyToClipboard);

                    var clearBtn = UISpawn.Widget<WButtonIconText>(lifetime,
                        UIPrefabId.WPanelButtonIcon, group.Container, idx++);
                    clearBtn.Load(IconId.CStop, "Clear All", () => {
                        being.GoalChanges?.Clear();
                        ReloadTab();
                    });
                }

            }

            return true;
        }

        protected override void Cleanup() {
            S = null;
            being = null;
        }
    }
}
