using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Completes when any wild (non-player) Crawler being is alive and incapacitated.
    /// The step polls on every tick; no dedicated incapacitation signal exists.
    /// </summary>
    public sealed class CaptureIncapacitatedCrawlerQuestStep : BaseQuestStep,
            ITickListener, IPreCheckedQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new CaptureIncapacitatedCrawlerQuestStep(s, c, l));
        }

        public const string CheckId = "CaptureIncapacitatedCrawler";

        private readonly BeingData colonist;

        public CaptureIncapacitatedCrawlerQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            if (step.Data.Provider is ColonistQuestProvider cp) {
                colonist = cp.BeingData;
            }
            if (colonist == null) {
                D.Throw("CaptureIncapacitatedCrawler: quest provider is not a ColonistQuestProvider | {0}", step);
            }

            step.Title = T.QuestStepCaptureBeingTitle_V1.F(Texts.LinkToCodex(DefIdsH.BeingsCrawler01));
            step.Desc = T.QuestStepCaptureBeingDesc_V1.F(Texts.LinkToCodex(DefIdsH.BeingsCrawler01));
        }

        public void Tick(long ticks) {
            if (step.IsActive && CheckCompleted()) {
                CompleteStep();
            }
        }

        public bool CheckCompleted() {
            foreach (var b in S.Beings.Alive) {
                if (IsCapturableCrawler(b.Data)) { return true; }
            }
            return false;
        }

        public static bool IsCapturableCrawler(BeingData data) {
            if (data.Species.IdH != SpeciesIdH.Crawler) { return false; }
            if (data.IsPlayer) { return false; }
            if (data.Being == null) { return false; }
            if (!data.Body.IsAlive) { return false; }
            if (!data.Body.IsIncapacitated) { return false; }
            if (data.HasStateFlag(BeingState.Dead)) { return false; }
            if (data.HasStateFlag(BeingState.Stored)) { return false; }
            if (data.HasStateFlag(BeingState.Expedition)) { return false; }
            if (data.HasStateFlag(BeingState.InDevice)) { return false; }
            return true;
        }

        public void PreCheck() {
            if (CheckCompleted()) {
                CompleteStep();
            }
        }
    }
}
