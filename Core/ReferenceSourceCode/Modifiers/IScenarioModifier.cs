using Game.Scenarios;

namespace Game.Modifiers {
    public interface IScenarioModifier {
        public float DefaultValueForScenario(Scenario scenario);
        public bool ShowInScenario(Scenario scenario);
    }
}
