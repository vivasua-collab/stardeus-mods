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
    public sealed class ColonistBedRequestStoryEvent : StoryEvent {
        public const string EventId = "ColonistBedRequest";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new ColonistBedRequestStoryEvent());
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
            if (!data.HasNeed(NeedIdH.Sleep)) { return false; }
            if (data.StatsBeing.Sleep.Value > 0.1f) {
                // not sleep deprived
                return false;
            }
            var bed = s.Query.FindBed.Ask(data, false);
            if (bed != null) { return false; }
            if (s.Query.HasSafelyAssignableBedFor.Ask(data)) {
                return false;
            }
            if (!ColonistQuestProvider.CanOffer(data,
                    QuestDef.FastGet(QuestIdH.Random_Personal_ColonistBedRequest))) {
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
            var questDef = QuestDef.FastGet(QuestIdH.Random_Personal_ColonistBedRequest);
            var quest = s.Query.ForceQuestFor.Ask(provider, questDef, ctx.Rng);
            if (quest == null) {
                data.FailEvent(s, "Failed to generate colonist bed request quest", isHardFailure: false);
                return;
            }

            quest.Data.Title = T.QuestColonistBedTitle_V1.F(colonist.Mind.Name.ShortName);
            quest.Data.Desc = T.QuestColonistBedDesc_V1.F(colonist.Mind.LinkToShortName);

            ColonistQuestOffer.Show(colonist, quest);

            data.HideLogEntry();
        }
    }
}
