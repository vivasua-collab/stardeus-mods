using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Drama;
using Game.Systems.Mental;
using Game.Systems.Quests;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Story {
    public sealed class ColonistCrushExpeditionStoryEvent : StoryEvent {
        public const string EventId = "ColonistCrushExpeditionQuest";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new ColonistCrushExpeditionStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            foreach (var b in ctx.S.Beings.Alive) {
                if (IsEligibleGiver(ctx.S, b.Data)) { return true; }
            }
            return false;
        }

        private static bool IsEligibleGiver(GameState s, BeingData data) {
            if (!data.IsPlayer) { return false; }
            if (data.Being == null || !data.Being.Species.IsHumanoid) { return false; }
            if (!data.Being.IsAlive) { return false; }
            if (!BeingUtils.CanBeSentOnExpeditionEver(data)) { return false; }
            if (data.Mind.Bonds == null) { return false; }
            if (!ColonistQuestProvider.CanOffer(data,
                    QuestDef.FastGet(QuestIdH.Random_Personal_ColonistCrushExpedition))) {
                return false;
            }
            if (!data.Mind.Bonds.Important.TryGetValue(BeingOpinionFlags.Crush, out var crushes)) {
                return false;
            }
            for (int i = crushes.Length - 1; i >= 0; i--) {
                if (IsEligibleCrush(crushes[i])) { return true; }
            }
            return false;
        }

        private static bool IsEligibleCrush(Mind crush) {
            if (crush?.Owner == null) { return false; }
            if (crush.Health == null) { return false; }
            if (crush.Owner.Being == null) { return false; }
            if (!crush.Owner.IsPlayer) { return false; }
            if (!crush.Owner.Being.Species.IsHumanoid) { return false; }
            if (crush.Owner.Body.IsIncapacitated) { return false; }
            if (!BeingUtils.CanBeSentOnExpeditionEver(crush.Owner)) { return false; }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            data.SetState(s, StoryEventState.Ending);

            var candidates = Caches.BeingList;
            candidates.Clear();
            foreach (var b in s.Beings.Alive) {
                if (IsEligibleGiver(s, b.Data)) {
                    candidates.Add(b);
                }
            }
            BeingData colonist = ctx.Rng.From(candidates)?.Data;
            if (colonist == null) {
                data.FailEvent(s, "No eligible colonist with crush found", isHardFailure: false);
                return;
            }

            var crushPool = Caches.BeingDataList;
            crushPool.Clear();
            if (colonist.Mind.Bonds.Important.TryGetValue(BeingOpinionFlags.Crush, out var crushes)) {
                for (int i = crushes.Length - 1; i >= 0; i--) {
                    if (IsEligibleCrush(crushes[i])) {
                        crushPool.Add(crushes[i].Owner);
                    }
                }
            }
            BeingData chosenCrush = ctx.Rng.From(crushPool);
            if (chosenCrush == null) {
                data.FailEvent(s, "No eligible crush found", isHardFailure: false);
                return;
            }

            var provider = new ColonistQuestProvider(colonist) {
                ExtraBeingIds = new[] { chosenCrush.Mind.Id }
            };
            var questDef = QuestDef.FastGet(QuestIdH.Random_Personal_ColonistCrushExpedition);
            var quest = s.Query.ForceQuestFor.Ask(provider, questDef, ctx.Rng);
            if (quest == null) {
                data.FailEvent(s, "Failed to generate colonist crush expedition quest", isHardFailure: false);
                return;
            }

            quest.Data.Title = T.QuestColonistCrushTitle;
            quest.Data.Desc = T.QuestColonistCrushDesc_V2.F(
                colonist.Mind.LinkToShortName, chosenCrush.Mind.LinkToShortName);

            ColonistQuestOffer.Show(colonist, quest);

            data.HideLogEntry();
        }
    }
}
