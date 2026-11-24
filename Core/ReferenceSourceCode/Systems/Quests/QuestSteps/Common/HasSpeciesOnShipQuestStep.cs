using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Completes when at least one player-faction being of the configured species
    /// is alive on the ship. Species is set via the step's Config key "SpeciesId"
    /// (e.g. "Cat", "Dog", "Pig"). Checks on every tick until fulfilled.
    /// </summary>
    public sealed class HasSpeciesOnShipQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new HasSpeciesOnShipQuestStep(s, c, l));
        }

        public const string CheckId = "HasSpeciesOnShip";
        public override bool SaveLoadTexts => true;

        private readonly int speciesIdH;
        private readonly string speciesId;

        public HasSpeciesOnShipQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            speciesId = config.GetString(PropertyIdH.SpeciesId, null);
            if (string.IsNullOrEmpty(speciesId)) {
                D.Err("HasSpeciesOnShipQuestStep: no SpeciesId in config! {0}", step);
                speciesIdH = 0;
            } else {
                speciesIdH = Hashes.S(speciesId);
            }

            if (!isLoading) {
                var sp = Species.Get(speciesIdH);
                var spName = sp?.Def.CodexLink ?? speciesId;
                step.Title = T.QuestStepGetSpeciesTitle_V1.F(spName);
                step.Desc = T.QuestStepGetSpeciesDesc_V1.F(spName);
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                if (speciesIdH != 0 && CheckCompleted()) {
                    CompleteStep();
                }
            }
        }

        public void Tick(long ticks) {
            if (!step.IsActive) { return; }
            if (ticks % 20 != 7) { return; }
            if (speciesIdH != 0 && CheckCompleted()) {
                CompleteStep();
            }
        }

        private bool CheckCompleted() {
            foreach (var being in S.Beings.Alive) {
                if (!being.Data.IsPlayer) { continue; }
                if (being.Species.IdH == speciesIdH) { return true; }
            }
            return false;
        }
    }
}
