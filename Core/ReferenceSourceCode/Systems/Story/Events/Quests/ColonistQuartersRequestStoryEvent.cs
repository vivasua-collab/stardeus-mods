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
    public sealed class ColonistQuartersRequestStoryEvent : StoryEvent {
        public const string EventId = "ColonistQuartersQuest";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new ColonistQuartersRequestStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            foreach (var b in ctx.S.Beings.Alive) {
                if (IsEligible(ctx.S, b.Data)) { return true; }
            }
            return false;
        }

        private static bool IsEligible(GameState s, BeingData data) {
            if (!data.IsPlayer) { return false; }
            if (data.Being == null || !data.Being.Species.IsHumanoid) { return false; }
            if (!data.Being.IsAlive) { return false; }
            if (data.StatsBeing.AIAlignment.Value < 0.6f) { return false; }
            if (data.StatsBeing.Sanity.Value < 0.6f) { return false; }
            // A colonist without a bed has nothing to ask about yet
            var bed = s.Query.FindBed.Ask(data, false);
            if (bed == null) { return false; }
            if (s.Sys.Areas.HasPersonalQuarters(data)) { return false; }
            if (!ColonistQuestProvider.CanOffer(data,
                    QuestDef.FastGet(QuestIdH.Random_Personal_ColonistQuartersRequest))) {
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
                if (IsEligible(s, b.Data)) {
                    candidates.Add(b);
                }
            }
            BeingData colonist = ctx.Rng.From(candidates)?.Data;
            if (colonist == null) {
                data.FailEvent(s, "No eligible colonist found", isHardFailure: false);
                return;
            }

            var provider = new ColonistQuestProvider(colonist);
            var questDef = QuestDef.FastGet(QuestIdH.Random_Personal_ColonistQuartersRequest);
            var quest = s.Query.ForceQuestFor.Ask(provider, questDef, ctx.Rng);
            if (quest == null) {
                data.FailEvent(s, "Failed to generate colonist quarters quest", isHardFailure: false);
                return;
            }

            quest.Data.Title = T.QuestColonistQuartersTitle_V1.F(colonist.Mind.Name.ShortName);
            quest.Data.Desc = T.QuestColonistQuartersDesc_V1.F(colonist.Mind.LinkToShortName);

            ColonistQuestOffer.Show(colonist, quest);

            data.HideLogEntry();
        }
    }
}
