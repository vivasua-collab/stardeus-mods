using KL.Console;
using KL.Utils;
using Game.CodeGen;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;
using System.Collections.Generic;
using System;
using KL.Collections;
using System.Reflection;
using System.Text;
using Game.Data.Economy;
using KL.Randomness;
using System.Linq;
using Game.Systems;
using Game.Systems.Quests;
using Game.UI;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandQuest : BaseConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandQuest());
        }

        protected override Dictionary<string, string> subCommands => new() {
            { "activate", "QuestActivate" },
            { "add", "QuestAdd" },
            { "offer_ship_quest", "OfferShipQuest" },
            // { "expire", "QuestExpire" },
            { "list", "ListQuests" },
            { "debug", "ShowDebug" },
            { "complete_next_step", "CompleteNextStep" },
            { "set_quest_bool", "SetQuestBool" },
            { "set_global_int", "SetGlobalInt" },
            { "recover", "RecoverChain" },
            { "skip_tutorials", "SkipTutorials" },
            { "chain", "ShowChain" },
            { "dedup", "DedupQuests" },
            { "toggle_widget", "ToggleWidget" },
            { "force_signal", "ForceSignalQuest" },
        };

        protected override HashSet<string> commandsThatDontBlockAchievements => new() {
            "list",
            "debug",
            "chain",
            "toggle_widget",
            // Same as abandoning every tutorial quest by hand, which blocks nothing either
            "skip_tutorials",
        };

        private ConsoleCommandResult ToggleWidget(ConsoleCommandArguments args) {
            var registry = A.S.UIQuery.GetWidgetRegistry.Ask();
            if (registry == null || registry.Quest == null) {
                return Error("QuestWidget not found!");
            }
            var widget = registry.Quest;
            if (widget.IsShowing) {
                widget.Hide();
                return OK("QuestWidget hidden");
            }
            widget.Show();
            return OK("QuestWidget shown");
        }

        private ConsoleCommandResult SetQuestBool(ConsoleCommandArguments args) {
            var flag = args.GetString(2);
            bool yes = true;
            if (args.HasArgument(3)) {
                var b = args.GetBool(3, out var isValidBool);
                if (isValidBool) {
                    yes = b;
                } else {
                    return Error("Invalid bool: {0}", args.GetString(3));
                }
            }
            A.S.Query.SetQuestBool.Ask(flag, yes);
            return OK("Set quest bool: {0} -> {1}", flag, yes);
        }

        private ConsoleCommandResult CompleteNextStep(ConsoleCommandArguments args) {
            var quest = A.S.Query.GetTrackedQuest.Ask();
            if (quest == null) {
                return Error("No tracked quest, can't complete next step!");
            }
            foreach (var step in quest.Steps) {
                if (step.IsActive) {
                    step.SetState(QuestStepState.Completed);
                    return OK("Completed quest step: {0}", step);
                }
            }
            return Error("No active steps in tracked quest! WTF: {0}", quest);
        }

        private ConsoleCommandResult SetGlobalInt(ConsoleCommandArguments args) {
            if (!args.HasArgument(3)) {
                return Error("usage: set_global_int <name> <int>");
            }
            var var = args.GetString(2);
            var num = args.GetInt(3);
            var data = A.S.Query.GetSharedQuestData.Ask();
            data.SetInt(var, num);

            return OK("Global quest var: {0} = {1}", var, num);
        }

        private ConsoleCommandResult ListQuests(ConsoleCommandArguments args) {
            return OK("Available quests:\n - {0}", string.Join("\n - ",
                QuestDef.All.Select(v => v.Id)));
        }

        private ConsoleCommandResult ForceSignalQuest(ConsoleCommandArguments args) {
            if (!args.HasArgument(2)) {
                return Error("Usage: quest force_signal <questId>");
            }
            var questId = args.GetString(2);
            var def = QuestDef.FastGet(Hashes.S(questId), false);
            if (def == null || (def.Providers & QuestProviderType.Signal) == QuestProviderType.None) {
                return Error("Signal quest not found: {0}", questId);
            }
            A.S.BB.Set(BBKeyH.DevForceSignalQuest, def.Id);
            return OK("The next Signal response will offer quest: {0}", def.Id);
        }

        private ConsoleCommandResult OfferShipQuest(ConsoleCommandArguments args) {
            if (!args.HasArgument(3)) {
                return OK("Usage: offer_ship_quest <questId> <shipId> [seed]. You can list ship ids using 'npc_ships list'.");
            }
            var qId = args.GetString(2);
            var def = QuestDef.Get(qId);
            if (def == null) {
                return OK("Available quest ids:\n - {0}", string.Join("\n - ",
                    QuestDef.All.Select(v => v.Id)));
            }
            var shipId = args.GetUInt(3);
            var ship = A.S.Query.NPCShipById.Ask(shipId);
            if (ship == null) {
                return OK("Ship not found by id {0}. You can list ship ids using 'npc_ships list'.", shipId);
            }
            Rng rng = Rng.Unseeded;
            if (args.HasArgument(3)) {
                var seed = args.GetUInt(3);
                if (seed != 0) {
                    rng = new Rng(seed);
                }
            }
            var q = A.S.Query.ForceQuestFor.Ask(ship, def, rng);
            if (q != null) {
                A.S.UIQuery.ShowQuest.Ask(q);
                return OK("Showing Quest: {0} ({1})", qId, q);
            }
            return Error("Failed forcing a quest: {0} on ship {1}", q, ship);
        }
        private ConsoleCommandResult QuestAdd(ConsoleCommandArguments args) {
            if (args.HasArgument(2)) {
                var qId = args.GetString(2);
                var def = QuestDef.Get(qId);
                Rng rng = Rng.Unseeded;
                if (args.HasArgument(3)) {
                    var seed = args.GetUInt(3);
                    if (seed != 0) {
                        rng = new Rng(seed);
                    }
                }
                if (def != null) {
                    var data = QuestData.CreateFor(A.S, def, rng);
                    var q = A.S.Query.RegisterAvailableQuest.Ask(data, true);
                    if (q != null) {
                        return OK("Added Quest: {0} ({1})", qId, q);
                    }
                }
            }
            return OK("Available quest ids:\n - {0}", string.Join("\n - ",
                QuestDef.All.Select(v => v.Id)));
        }

        private ConsoleCommandResult QuestActivate(ConsoleCommandArguments args) {
            if (args.HasArgument(2)) {
                var qId = args.GetString(2);
                var def = QuestDef.Get(qId);
                Rng rng = Rng.Unseeded;
                if (args.HasArgument(3)) {
                    var seed = args.GetUInt(3);
                    if (seed != 0) {
                        rng = new Rng(seed);
                    }
                }
                if (def != null) {
                    var data = QuestData.CreateFor(A.S, def, rng);
                    var q = A.S.Query.AddQuest.Ask(data, true);
                    if (q != null) {
                        return OK("Activated Quest: {0} ({1})", qId, q);
                    }
                }
            }
            return OK("Available quest ids:\n - {0}", string.Join("\n - ",
                QuestDef.All.Select(v => v.Id)));
        }
        public override string Autocomplete(ConsoleCommandArguments args) {
            var cmd = args.GetString(1);
            if (cmd == "add" || cmd == "activate" || cmd == "offer_ship_quest"
                    || cmd == "force_signal") {
                var arg = args.GetString(2);
                if (arg == null) {
                    return $"{cmd} ";
                }
                foreach (var q in QuestDef.All) {
                    if (cmd == "force_signal"
                            && (q.Providers & QuestProviderType.Signal) == QuestProviderType.None) {
                        continue;
                    }
                    var id = q.Id.ToLowerInvariant();
                    if (id.StartsWith(arg) || id.Contains(arg)) {
                        return $"{cmd} {q.Id}";
                    }
                }
            }
            return base.Autocomplete(args);
        }

        protected ConsoleCommandResult ShowDebug(ConsoleCommandArguments args) {
            var questSys = A.S.Sys.Get<QuestSys>(QuestSys.SysId);
            if (questSys == null) {
                return Error("QuestSys not found!");
            }
            DebugOutputWidget.SpawnFor(new QuestDebugOutput(questSys));
            return Close();
        }

        private ConsoleCommandResult RecoverChain(ConsoleCommandArguments args) {
            var questSys = A.S.Sys.Get<QuestSys>(QuestSys.SysId);
            if (questSys == null) {
                return Error("QuestSys not found!");
            }
            var sb = new StringBuilder();
            var changes = questSys.RecoverStalledChain(sb);
            if (changes == 0) {
                return OK("Nothing to recover - quest chain is consistent.");
            }
            return OK("Recovered {0} change(s):\n{1}", changes, sb.ToString());
        }

        private ConsoleCommandResult SkipTutorials(ConsoleCommandArguments args) {
            var questSys = A.S.Sys.Get<QuestSys>(QuestSys.SysId);
            if (questSys == null) {
                return Error("QuestSys not found!");
            }
            var sb = new StringBuilder();
            var changes = questSys.SkipTutorials(sb);
            if (changes == 0) {
                return OK("No tutorial quests left to skip.");
            }
            return OK("Skipped {0} tutorial quest(s):\n{1}", changes, sb.ToString());
        }

        private ConsoleCommandResult DedupQuests(ConsoleCommandArguments args) {
            var questSys = A.S.Sys.Get<QuestSys>(QuestSys.SysId);
            if (questSys == null) {
                return Error("QuestSys not found!");
            }
            var removed = questSys.ValidateNoDuplicateQuests();
            if (removed == 0) {
                return OK("Nothing to dedup - no duplicate tutorial/main/unique quests found.");
            }
            return OK("Removed {0} duplicate quest instance(s), see log for details.", removed);
        }

        private ConsoleCommandResult ShowChain(ConsoleCommandArguments args) {
            var questSys = A.S.Sys.Get<QuestSys>(QuestSys.SysId);
            if (questSys == null) {
                return Error("QuestSys not found!");
            }
            var sb = new StringBuilder();
            questSys.DescribeChain(sb);
            if (QuestDef.FindDeadEnds().Count > 0) {
                return Error(sb.ToString());
            }
            return OK(sb.ToString());
        }

        public override void Initialize() {
            Name = "quest";
            HelpLine = "For debugging quests";
        }
    }
}
