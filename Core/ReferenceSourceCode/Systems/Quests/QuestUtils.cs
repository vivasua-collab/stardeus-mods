#if !PRODUCTION_BUILD
// #define DEBUG_QUESTS
#endif
using System;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public static class QuestUtils {
        // Where the player is in the tutorial: the tracked tutorial quest (or the first
        // active one) and its first visible active step. For the onboarding funnel on quit.
        public static bool TryGetTutorialProgress(GameState s, out string questId,
                out string stepId) {
            questId = null;
            stepId = null;
            var quest = s.Query.GetTrackedQuest.Ask();
            if (quest == null || !quest.Data.IsActive || !IsChainQuest(quest)) {
                quest = null;
                var all = s.Query.GetAllPlayerQuests.Ask();
                if (all == null) { return false; }
                for (int i = 0; i < all.Count; i++) {
                    if (all[i].Data.IsActive && IsChainQuest(all[i])) {
                        quest = all[i];
                        break;
                    }
                }
            }
            if (quest == null) { return false; }
            questId = quest.Data.Def.Id;
            var steps = quest.Data.Steps;
            if (steps != null) {
                for (int i = 0; i < steps.Length; i++) {
                    if (steps[i].IsActive && IsStepVisible(steps[i])) {
                        stepId = steps[i].Def.Id;
                        break;
                    }
                }
            }
            return true;
        }

        private static bool IsChainQuest(Quest q) {
            return q.Data.Def.IsTutorial || q.Data.Def.IsMain;
        }

        // Same test OperatableDeviceComp.IsAbleToOperate applies for the Crafting Station,
        // which lists no extra RequiredAbilities: the job slot exists (ability-driven) and is
        // neither incapable nor at priority 0.
        public static bool CanAnyoneCraft(GameState s) {
            return s.Beings.FindFirstMatching(b => b.IsAlive && b.Data.IsPlayer
                && b.Data.Mind.Jobs.CanDo(JobTypeIdH.Crafting, out string _)) != null;
        }

        // Scenario intros call this before ReadyForTutorialQuests so the production tutorial
        // variant is picked from the actual crew, not from the species list. Only ever sets
        // the bool, never clears it, so a test can force the Assembler fallback up front.
        public static void MarkCrewCannotCraftIfNeeded(GameState s) {
            if (CanAnyoneCraft(s)) { return; }
            D.Warn("Nobody in the starting crew can craft, using the Assembler tutorial");
            s.Query.SetQuestBool.Ask(QuestBoolIds.CrewCannotCraft, true);
        }

        // Shared between the DisposeOfInfected step (done when none left) and the
        // HasInfectedCorpse validator (quest offered only once there is one), so the gate and
        // the completion check cannot drift apart.
        public static bool HasInfectedCorpse(GameState s) {
            foreach (var being in s.Beings.All) {
                if (being.IsDead && being.Data.Species.IsBiological
                        && being.Data.Species.IsHumanoid
                        && being.Data.HasCondition(ConditionIdH.CrawlerInfected)) {
                    return true;
                }
            }
            return false;
        }

        public static void AppendDeadline(StringBuilder sb, long deadline, long ticks,
                string suffix = "\n") {
            if (deadline == 0L) {
                // sb.AppendLine(Texts.Green(T.DeadlineNone));
                return;
            }
            var ticksTillDeadline = deadline - ticks;
            var deadlineIn = Units.TicksToTime(ticksTillDeadline, shortVersion: true);
            var deadlineLabel = T.NounDeadline;
            if (ticksTillDeadline > Consts.TicksPerWeek) {
                deadlineLabel = Texts.Green(deadlineLabel);
            } else if (ticksTillDeadline > Consts.TicksPer3Days) {
                deadlineLabel = Texts.Yellow(deadlineLabel);
            } else {
                deadlineLabel = Texts.Red(deadlineLabel);
            }
            sb.AppendFormat("{0}: {1}", deadlineLabel, deadlineIn);
            if (suffix != null) {
                sb.Append(suffix);
            }
        }

        public static string IconFor(Quest quest, bool isTracked) {
            string icon;
            if (quest == null) {
                D.Err("Quest is null in IconFor!");
                return GraphicsId.IconsColorQuest;
            }
            if (quest.IsMain) {
                icon = GraphicsId.IconsColorQuest;
            } else {
                icon = GraphicsId.IconsColorQuestSide;
            }
            if (quest.IsFailed) {
                icon = $"{icon}Failed";
                return icon;
            }
            if (quest.Data == null) {
                D.Err("Quest.Data is null in IconFor!");
                return GraphicsId.IconsColorQuest;
            }
            if (quest.Data.State == QuestState.Completed) {
                icon = $"{icon}Done";
                return icon;
            }
            if (quest.IsActive) {
                if (isTracked) {
                    icon = $"{icon}Tracked";
                }
            } else {
                icon = $"{icon}Inactive";
            }
            if (quest.HasDeadline) {
                icon = $"{icon}Clock";
            }
            return icon;
        }

        public static string DecoratedTitle(QuestData data) {
            var title = data.Title;
            #if DEBUG_QUESTS
            title = $"[{data.Id}] {title}";
            #endif
            switch (data.State) {
                case QuestState.Failed:
                case QuestState.Rejected: {
                    return Texts.Red(Texts.Strike(title));
                }
                case QuestState.Deleted: {
                    return Texts.Grey(Texts.Strike(title));
                }
                case QuestState.Completed: {
                    return Texts.Grey(Texts.Strike(title));
                }
                case QuestState.Active: {
                    return title;
                }
            }
            return Texts.Accented(title, bold: false);
        }

        public static string DecoratedTitle(QuestStep step, bool stripTags, bool stateColors) {
            var title = step.Title;
            var isQuestActive = step.Data.IsActive;
            if ((!isQuestActive || step.State == QuestStepState.Inactive) && step.TitleInactive != null) {
                title = step.TitleInactive;
            }
            if (stripTags) {
                title = Texts.StripTags(title);
            } else if (stateColors && (!isQuestActive || !step.IsActive)) {
                title = Texts.StripTagsKeepItemLinks(title);
            }
            if (step.IsOptional) {
                var opt = T.QuestStepOptional;
                opt = Texts.Grey($"[{opt}]");
                title = $"{opt} {title}";
            }
            if (stateColors) {
                switch (step.State) {
                    case QuestStepState.Completed: {
                        title = Texts.Green(Texts.Strike(title));
                        break;
                    }
                    case QuestStepState.Failed: {
                        title = Texts.Red(Texts.Strike(title));
                        break;
                    }
                    case QuestStepState.Inactive: {
                        title = Texts.Grey(title);
                        break;
                    }
                    case QuestStepState.Active: {
                        if (!isQuestActive) {
                            title = Texts.Grey(title);
                        }
                        break;
                    }
                }
            }
            return title;
        }

        public static string IconFor(QuestStep step) {
            if (step.Data.IsAvailable) {
                if (step.Deadline != 0) {
                    return step.IsOptional ? IconId.CCheckDisabledClockOpt : IconId.CCheckDisabledClock;
                } else {
                    return step.IsOptional ? IconId.CCheckDisabledOpt : IconId.CCheckDisabled;
                }
            }
            switch (step.State) {
                case QuestStepState.Completed: {
                    return step.IsOptional ? IconId.CCheckDoneOpt : IconId.CCheckDone;
                }
                case QuestStepState.Failed: {
                    return step.IsOptional ? IconId.CCheckFailOpt : IconId.CCheckFail;
                }
                case QuestStepState.Inactive: {
                    if (step.Deadline != 0) {
                        return step.IsOptional ? IconId.CCheckDisabledClockOpt : IconId.CCheckDisabledClock;
                    } else {
                        return step.IsOptional ? IconId.CCheckDisabledOpt : IconId.CCheckDisabled;
                    }
                }
                case QuestStepState.Active: {
                    if (step.Deadline != 0) {
                        return step.IsOptional ? IconId.CCheckNotDoneClockOpt : IconId.CCheckNotDoneClock;
                    } else {
                        return step.IsOptional ? IconId.CCheckNotDoneOpt : IconId.CCheckNotDone;
                    }
                }
            }
            return string.Empty;
        }
        public static bool IsStepVisible(QuestStep step) {
            if (step.IsHidden) {
                return false;
            }
            if (step.Data.State == QuestState.Undefined || step.Data.State == QuestState.Available) {
                // unaccepted quest
                if (step.Def.Flags.HasFlag(QuestStepFlags.HideIfQuestUnaccepted)) {
                    return false;
                }
            }
            if (step.State == QuestStepState.Inactive) {
                if (step.Def.Flags.HasFlag(QuestStepFlags.HideInactive)) {
                    return false;
                }
            } else if (step.State != QuestStepState.Completed) {
                if (step.Def.Flags.HasFlag(QuestStepFlags.Secret)) {
                    return false;
                }
            }
            return true;
        }
    }
}
