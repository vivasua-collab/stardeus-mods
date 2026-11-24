using Game.Utils;
using Game.CodeGen;
using Game.Systems.AI;

namespace Game.Components {
    /// <summary>
    /// WARNING Don't forget to replace BaseComponent<ExampleComp>
    /// with correct component class name
    /// </summary>
    public sealed class ExampleComp : BaseComponent<ExampleComp>,
            IAIGoalProvider {

        /// <summary>
        /// Add the following with correct class name to register
        /// the component at runtime when mod loads:
        /// AddComponentPrototype(new ExampleComp());
        /// </summary>
        [SelfInit]
        private static void Register() {
            // AddComponentPrototype(new ExampleComp());
        }

        private int stuff;

        protected override void OnConfig() {
            stuff = Config.GetInt(PropertyIdH.EnergyCost);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("Stuff", stuff);
        }

        protected override void OnLoad(ComponentData data) {
            stuff = data.GetInt("Stuff", 0);
        }

        public override void OnLateReady(bool wasLoaded) {
        }

        public override void OnReady(bool wasLoaded) {
        }

        public override void Receive(IComponent sender, int message) {
        }

        public override void OnRemove() {
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
        }

        public override string ToString() {
            return $" * Example [Stuff {stuff}]";
        }
    }
}
