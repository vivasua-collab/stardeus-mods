using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Components {
    public sealed class CleaningBotComp : BaseComponent<CleaningBotComp>, ICopyableComp {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new CleaningBotComp());
        }

        public bool IsAutoCleanEnabled;
        public int AutoCleanIntervalMinutes;

        public bool HasActiveCleaningGoal =>
            AIGoal.IsActive(forcedGoal) ||
            Being.AIAgent.CurrentAction?.JobType == JobTypeIdH.Cleaning;

        protected override void OnConfig() {
            AutoCleanIntervalMinutes = 60;
            IsAutoCleanEnabled = true;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("AutoCleanInterval", AutoCleanIntervalMinutes);
            data.SetBool("AutoClean", IsAutoCleanEnabled);
        }

        protected override void OnLoad(ComponentData data) {
            AutoCleanIntervalMinutes = data.GetInt("AutoCleanInterval",
                AutoCleanIntervalMinutes);
            IsAutoCleanEnabled = data.GetBool("AutoClean", IsAutoCleanEnabled);
        }

        public override string ToString() {
            return $" * CleaningBot [Interval {AutoCleanIntervalMinutes}]";
        }

        public override void OnLateReady(bool wasLoaded) {
        }

        public bool IsCopyable => Being.IsAlive;
        public string CopyConfigText => Entity.Definition.NameT;

        private AIGoal forcedGoal;

        public bool TrySearchForDirt() {
            if (!S.Sys.Dirt.HasDirtReachableBy(Being)) {
                D.Warn("Cannot reach any dirt: {0}", Being);
                return false;
            }
            var ticks = G.Ticks;
            forcedGoal = AIGoal.DirectOrder(JobTypeIdH.Cleaning,
                    IconId.CMop, T.CleanFloors, Being.AIAgent, ticks)
                // A bit of a hack, initial world state can't be empty
                // this way this works for both idle cleaning and forced cleaning
                .WithWorld(AIVarsH.IsIdle, false)
                .WithDesired(AIVarsH.IsFloorClean, true);
            if (S.Query.ForceGoal.Ask(forcedGoal, true)) {
                return true;
            } else {
                D.Warn("Failed forcing search for dirt goal: {0}", forcedGoal);
                forcedGoal = null;
                return false;
            }
        }

        public bool CopyConfigTo(ICopyableComp target) {
            if (target is CleaningBotComp cb) {
                cb.IsAutoCleanEnabled = IsAutoCleanEnabled;
                cb.AutoCleanIntervalMinutes = AutoCleanIntervalMinutes;
                return true;
            }
            return false;
        }

        public bool CanCopyTo(ICopyableComp comp) {
            return comp is CleaningBotComp;
        }
    }
}
