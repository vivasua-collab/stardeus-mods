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
    public sealed class ColonistRevengeHuntStoryEvent : StoryEvent {
        public const string EventId = "ColonistRevengeHuntQuest";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new ColonistRevengeHuntStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            // ForceQuestFor skips quest validators, and this quest needs a ship that can fire
            if (!QuestSys.PassesValidators(ctx.S,
                    QuestDef.FastGet(QuestIdH.Random_Personal_ColonistRevengeHunt))) {
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
            if (!ColonistQuestProvider.CanOffer(data,
                    QuestDef.FastGet(QuestIdH.Random_Personal_ColonistRevengeHunt))) {
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
            var questDef = QuestDef.FastGet(QuestIdH.Random_Personal_ColonistRevengeHunt);
            var quest = s.Query.ForceQuestFor.Ask(provider, questDef, ctx.Rng);
            if (quest == null) {
                data.FailEvent(s, "Failed to generate colonist revenge hunt quest", isHardFailure: false);
                return;
            }

            ColonistQuestOffer.Show(colonist, quest);

            data.HideLogEntry();
        }
    }
}
