using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using Game.Systems.AI;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class PettableComp : BaseComponent<PettableComp>,
            IEntityPanelGoalProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new PettableComp());
        }
        public long NextPetAt;
        public long LastPetAt;
        public string LastPetBy;
        public int LastPetById;
        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetLong("NextPetAt", NextPetAt);
            data.SetLong("LastPetAt", LastPetAt);
            data.SetInt("LastPetById", LastPetById);
            data.SetString("LastPetBy", LastPetBy);
        }

        protected override void OnLoad(ComponentData data) {
            NextPetAt = data.GetLong("NextPetAt", 0);
            LastPetAt = data.GetLong("LastPetAt", 0);
            LastPetById = data.GetInt("LastPetById", 0);
            LastPetBy = data.GetString("LastPetBy", null);
        }

        public override string ToString() {
            return $" * Pettable [NextPet: {NextPetAt}]";
        }

        public bool TryOrderPetting() {
            if (HasGoal) {
                return false;
            }
            var g = CreateGoal(JobTypeIdH.Task, IconId.CPaw, T.AbilityPetanimal, this)
                    .WithWorldToDesired(AIVarsH.IsAnimalPetted, false, true)
                    .ByPlayer(true)
                    .WithRequiredAbilities(AbilityIdH.PetAnimal)
                    .WriteInitialState();
            return entity.TrySetGoal(g);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.HealthWentToZero) {
                if (TryGetGoal(out var g)) {
                    AIGoal.Cancel(g, G.Ticks, T.Dead);
                }
            }
        }

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        public void MarkPetted(long ticks, Being by) {
            LastPetAt = ticks;
            NextPetAt = ticks + Rng.URange(Consts.TicksPerHour, Consts.TicksPer3Hours);
            LastPetBy = by.Data.Mind.Name.ShortName;
            LastPetById = by.Id;
            stateChangedB?.Invoke();
        }

        public string LastPetTooltip() {
            if (LastPetAt == 0 || LastPetById == 0) {
                return T.NotPettedBefore;
            }
            var petTime = Units.TicksAgoFromNow(LastPetAt);
            if (LastPetById != 0) {
                var who = S.Beings.Find(LastPetById);
                if (who == null) {
                    return T.LastPettedBy_V2.F(T.Unknown, petTime);
                }
                return T.LastPettedBy_V2.F(who.Data.Mind.Name.ShortName, petTime);
            } else if (LastPetBy != null) {
                return T.LastPettedBy_V2.F(LastPetBy, petTime);
            }
            D.Err("LastPetAt is > 0, but couldn't find the petter!");
            return T.NotPettedBefore;
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
        }
    }
}
