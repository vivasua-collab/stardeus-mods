using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Drama;
using Game.Systems.Quests;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Story {
    public sealed class ColonistWantsPetStoryEvent : StoryEvent {
        public const string EventId = "ColonistWantsPet";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new ColonistWantsPetStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            var spId = def.Vars.Get(PropertyIdH.SpeciesId, "Cat");
            var species = Species.Get(spId);
            if (species == null) {
                return false;
            }
            if (HasPetOnShip(ctx.S, species)) { return false; }
            var questDef = QuestDefFor(def);
            if (questDef == null) { return false; }
            foreach (var b in ctx.S.Beings.Alive) {
                if (IsEligible(b.Data, questDef)) { return true; }
            }
            return false;
        }

        private static bool HasPetOnShip(GameState s, Species species) {
            var allBeings = s.Query.AllBeingData.Ask();
            foreach (var being in allBeings.Values) {
                if (!being.IsPlayer) { continue; }
                if (being.Species != species) { continue; }
                if (!being.HasStateFlag(BeingState.Alive)) { continue; }
                if (being.Body.HasFlag(BodyFlags.Missing)) { continue; }
                return true;
            }
            return false;
        }

        private static QuestDef QuestDefFor(StoryEventDef def) {
            return QuestDef.Get(def.Vars.Get(PropertyIdH.QuestId,
                QuestId.Random_Personal_ColonistWantsPet_Cat));
        }

        private static bool IsEligible(BeingData data, QuestDef questDef) {
            if (!data.IsPlayer) { return false; }
            if (data.Being == null || !data.Mind.Species.IsHumanoid) { return false; }
            if (!data.Being.IsAlive) { return false; }
            if (!ColonistQuestProvider.CanOffer(data, questDef)) { return false; }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            data.SetState(s, StoryEventState.Ending);

            var petSpeciesId = data.Def.Vars.Get(PropertyIdH.SpeciesId, "Cat");
            var petSpecies = Species.Get(petSpeciesId) ?? Species.Get("Cat");

            if (HasPetOnShip(s, petSpecies)) {
                data.FailEvent(s, "Pet already present on ship", isHardFailure: false);
                return;
            }

            var questDef = QuestDefFor(data.Def);
            var candidates = Caches.BeingList;
            candidates.Clear();
            foreach (var b in s.Beings.Alive) {
                if (IsEligible(b.Data, questDef)) {
                    candidates.Add(b);
                }
            }
            BeingData colonist = ctx.Rng.From(candidates)?.Data;
            if (colonist == null) {
                data.FailEvent(s, "No eligible colonist found", isHardFailure: false);
                return;
            }

            var provider = new ColonistQuestProvider(colonist);
            var quest = s.Query.ForceQuestFor.Ask(provider, questDef, ctx.Rng);
            if (quest == null) {
                data.FailEvent(s, "Failed to generate colonist wants pet cat quest", isHardFailure: false);
                return;
            }

            var petSpeciesName = petSpecies.Name;
            quest.Data.Title = T.QuestColonistWantsPetTitle_V2.F(
                colonist.Mind.Name.ShortName, petSpeciesName);
            quest.Data.Desc = T.QuestColonistWantsPetDesc_V2.F(
                colonist.Mind.Name.ShortName, petSpecies.Def.CodexLink);

            ColonistQuestOffer.Show(colonist, quest);

            data.HideLogEntry();
        }
    }
}
