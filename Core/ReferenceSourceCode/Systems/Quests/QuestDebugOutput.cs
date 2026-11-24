using System.Linq;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Quests {
    public sealed class QuestDebugOutput : IDebugOutputProvider {
        private readonly QuestSys questSys;
        private readonly GameState S;

        public QuestDebugOutput(QuestSys sys) {
            questSys = sys;
            S = sys.S;
        }

        public string DebugPanelTitle => "Quest System";

        public bool EveryFrame() {
            return false;
        }

        public bool OnTick(StringBuilder sb) {
            sb.Clear();

            // Active Quests
            sb.AppendFormat("{0}: {1}\n",
                Texts.Accented("Active Quests"),
                Texts.Green(questSys.ActiveQuests.Count));

            foreach (var quest in questSys.ActiveQuests) {
                sb.AppendFormat(" - {0} [{1}]\n",
                    QuestUtils.DecoratedTitle(quest.Data),
                    Texts.Green(quest.Data.State.ToString()));

                if (quest.Data.StartTick > 0) {
                    sb.AppendFormat("   Started: {0} ago\n",
                        Texts.Grey(Units.TicksToTime(G.Ticks - quest.Data.StartTick)));
                }

                if (quest.Data.HasDeadline) {
                    if (quest.Data.GetNextDeadline(out var deadline, out var step)) {
                        sb.AppendFormat("   Next Deadline: {0}\n",
                            Texts.Yellow(Units.TicksToTime(deadline - G.Ticks)));
                    }
                }

                // Steps
                int activeSteps = 0;
                int completedSteps = 0;
                int failedSteps = 0;
                foreach (var step in quest.Steps) {
                    if (step.State == QuestStepState.Active) {
                        activeSteps++;
                    } else if (step.State == QuestStepState.Completed) {
                        completedSteps++;
                    } else if (step.State == QuestStepState.Failed) {
                        failedSteps++;
                    }
                }

                sb.AppendFormat("   Steps: {0} active, {1} completed",
                    Texts.Green(activeSteps),
                    Texts.Green(completedSteps));
                if (failedSteps > 0) {
                    sb.AppendFormat(", {0} failed", Texts.Red(failedSteps));
                }
                sb.AppendFormat(" / {0} total\n", quest.Steps.Length);

                // Active step details
                foreach (var step in quest.Steps) {
                    if (step.IsActive) {
                        sb.AppendFormat("     → {0}: {1}\n",
                            Texts.Blue(step.Def.Id),
                            Texts.Grey(step.State.ToString()));
                        if (step.Deadline > 0) {
                            sb.AppendFormat("       Deadline: {0}\n",
                                Texts.Yellow(Units.TicksToTime(step.Deadline - G.Ticks)));
                        }
                    }
                }
            }

            // Tracked Quest
            if (questSys.TrackedQuest != null) {
                sb.AppendFormat("\n{0}: {1}\n",
                    Texts.Accented("Tracked Quest"),
                    QuestUtils.DecoratedTitle(questSys.TrackedQuest.Data));
            } else {
                sb.Append(Texts.Grey("\nNo Tracked Quest\n"));
            }

            // All Player Quests Summary
            sb.AppendFormat("\n{0}: {1}\n",
                Texts.Accented("All Player Quests"),
                questSys.AllPlayerQuests.Count);

            var stateGroups = questSys.AllPlayerQuests
                .GroupBy(q => q.Data.State)
                .OrderBy(g => g.Key);

            foreach (var group in stateGroups) {
                var stateColor = GetStateColor(group.Key);
                sb.AppendFormat(" - {0}: {1}\n",
                    stateColor(group.Key.ToString()),
                    group.Count());

                foreach (var quest in group.Take(3)) {
                    sb.AppendFormat("   • {0}\n",
                        QuestUtils.DecoratedTitle(quest.Data));
                }
                if (group.Count() > 3) {
                    sb.AppendFormat("   ... and {0} more\n",
                        Texts.Grey((group.Count() - 3).ToString()));
                }
            }

            // Quest Packages
            if (questSys.QuestPackages.Count > 0) {
                sb.AppendFormat("\n{0}: {1}\n",
                    Texts.Accented("Quest Packages"),
                    Texts.Green(questSys.QuestPackages.Count));

                foreach (var pack in questSys.QuestPackages.Values.Take(5)) {
                    sb.AppendFormat(" - Package ID: {0}\n",
                        Texts.Accented2(pack.Id.ToString(), false));
                    if (pack.Step != null) {
                        sb.AppendFormat("   Quest: {0}\n",
                            QuestUtils.DecoratedTitle(pack.Step.Data));
                        sb.AppendFormat("   Step: {0}\n",
                            Texts.Grey(pack.Step.Def.Id));
                    }
                    if (pack.Mats != null) {
                        sb.AppendFormat("   Materials: {0}\n",
                            Texts.Grey(pack.Mats.Count.ToString()));
                    }
                }
                if (questSys.QuestPackages.Count > 5) {
                    sb.AppendFormat(" ... and {0} more\n",
                        Texts.Grey((questSys.QuestPackages.Count - 5).ToString()));
                }
            }

            // Step Targets
            if (questSys.StepTargets.Count > 0) {
                sb.AppendFormat("\n{0}: {1}\n",
                    Texts.Accented("Active Step Targets"),
                    Texts.Green(questSys.StepTargets.Count));

                foreach (var target in questSys.StepTargets.Values.Take(5)) {
                    sb.AppendFormat(" - {0} -> {1}\n",
                        Texts.Blue(target.Step.Def.Id),
                        Texts.Grey(target.Target?.StarmapLink ?? "No SO"));
                    sb.AppendFormat("   Quest: {0}\n",
                        QuestUtils.DecoratedTitle(target.Step.Data));
                }
                if (questSys.StepTargets.Count > 5) {
                    sb.AppendFormat(" ... and {0} more\n",
                        Texts.Grey((questSys.StepTargets.Count - 5).ToString()));
                }
            }

            // Available Quest Defs
            var availableQuests = questSys.AllPlayerQuests
                .Where(q => q.IsAvailable)
                .ToList();

            if (availableQuests.Count > 0) {
                sb.AppendFormat("\n{0}:\n", Texts.Accented("Available Quests Details"));
                foreach (var quest in availableQuests.Take(3)) {
                    sb.AppendFormat(" - {0}\n",
                        QuestUtils.DecoratedTitle(quest.Data));
                    sb.AppendFormat("   Source: {0}\n",
                        Texts.Grey(quest.Data.Provider?.StarmapLink ?? "None"));
                    if (quest.Data.AvailableTick > 0) {
                        sb.AppendFormat("   Available since: {0} ago\n",
                            Texts.Grey(Units.TicksToTime(G.Ticks - quest.Data.AvailableTick)));
                    }
                }
            }

            return true;
        }

        private System.Func<string, string> GetStateColor(QuestState state) {
            switch (state) {
                case QuestState.Active:
                    return Texts.Green;
                case QuestState.Completed:
                    return Texts.Yellow;
                case QuestState.Failed:
                case QuestState.Rejected:
                    return Texts.Red;
                case QuestState.Available:
                    return Texts.Blue;
                default:
                    return Texts.Grey;
            }
        }
    }
}
