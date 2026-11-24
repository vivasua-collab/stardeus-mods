using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Drama;
using Game.Systems.Quests;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Story {
    public sealed class ColonistExperimentCrawlerChipStoryEvent : StoryEvent {
        public const string EventId = "ColonistExperimentCrawlerChip";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new ColonistExperimentCrawlerChipStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            if (!ctx.S.Sys.Slots.HasFor(SlotDesignationH.BodyModSpot_Organic)) {
                return false;
            }
            foreach (var b in ctx.S.Beings.Alive) {
                if (IsEligible(b.Data)) { return true; }
            }
            return false;
        }

        private static bool IsEligible(BeingData data) {
            if (!data.IsPlayer) { return false; }
            if (data.Being == null || !data.Being.Species.IsHumanoid) { return false; }
            if (!data.Being.IsAlive) { return false; }
            if (data.StatsBeing.AIAlignment.Value < 0.6f) { return false; }
            if (data.StatsBeing.Sanity.Value < 0.6f) { return false; }
            if (!data.Mind.Jobs.TryJob(JobTypeIdH.Medical, out var medSlot)
                    || medSlot.Level < 5) { return false; }
            if (!data.Mind.Jobs.TryJob(JobTypeIdH.Research, out var resSlot)
                    || resSlot.Level < 5) { return false; }
            if (!ColonistQuestProvider.CanOffer(data,
                    QuestDef.FastGet(QuestIdH.Random_Personal_ColonistExperiment_CrawlerChip))) {
                return false;
            }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            data.SetState(s, StoryEventState.Ending);

            var candidates = Caches.BeingList;
            candidates.Clear();
            foreach (var b in s.Beings.Alive) {
                if (IsEligible(b.Data)) {
                    candidates.Add(b);
                }
            }
            BeingData colonist = ctx.Rng.From(candidates)?.Data;
            if (colonist == null) {
                data.FailEvent(s, "No eligible colonist found", isHardFailure: false);
                return;
            }

            var provider = new ColonistQuestProvider(colonist);
            var questDef = QuestDef.FastGet(QuestIdH.Random_Personal_ColonistExperiment_CrawlerChip);
            var quest = s.Query.ForceQuestFor.Ask(provider, questDef, ctx.Rng);
            if (quest == null) {
                data.FailEvent(s, "Failed to generate colonist crawler chip quest", isHardFailure: false);
                return;
            }

            quest.Data.Title = T.QuestColonistExperimentTitle_V1.F(colonist.Mind.Name.ShortName);
            quest.Data.Desc = T.QuestColonistExperimentCrawlerChipDesc_V2.F(colonist.Mind.LinkToShortName, Texts.LinkToCodex(DefIdsH.BeingsCrawler01));

            ColonistQuestOffer.Show(colonist, quest);

            data.HideLogEntry();
        }
    }
}
