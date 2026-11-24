using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class ColonizePlanetQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new ColonizePlanetQuestStep(s, c, l));
        }
        public const string CheckId = "ColonizePlanet";
        private readonly SpaceObject planet;
        public ColonizePlanetQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            planet = GetQuestVarValByKey<SpaceObject>(PropertyIdH.Var_Target);
            step.Title = T.SettleIn_V1.F(planet.StarmapLink);
            step.Desc = T.QuestStepColonizePlanetDesc_V3.F(
                planet.ParentPath(false), Texts.LinkToUI(T.Starmap, UIId.Starmap),
                    T.SettleIn_V1.F(planet.StarmapLink));
        }

        public void Tick(long ticks) {
            if (step.IsActive && CheckCompleted()) {
                CompleteStep();
            }
        }

        public bool CheckCompleted() {
            return planet.Flags.HasFlag(SOFlags.Settled)
                && planet.Faction?.IsPlayer == true;
        }
    }
}
