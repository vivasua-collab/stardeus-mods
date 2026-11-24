#if UNITY_EDITOR
// #define DEBUG_AI_AGENT
#endif
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Conditions;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.AI;
using KL.Collections;

namespace Game.Components {

    public sealed class ConditionsComp : BaseComponent<ConditionsComp>, IAIGoalProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ConditionsComp());
        }

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded) {
                Ready.When(WhenH.BeingsLoaded, InitHealthLoaded);
            } else {
                entity.AfterInitialize += InitHealth;
            }
        }

        private void InitHealthLoaded() {
            if (Being.Id == 0 || Being.Data == null) {
                return;
            }
            // The wounds' pain is not saved, and a save from before pain shock has no shock
            // condition: without this a being downed by the old damaged parts rule would get
            // up on load and only go down again a tick later
            if (Being.Data.Body.IsAlive && PainShock.HasWoundCondition(Being.Data)) {
                PainShock.Recheck(S, Being.Data);
            }
            InitHealth();
        }

        private void InitHealth() {
            if (Being.Id == 0 || Being.Data == null) {
                return;
            }
            var body = Being.Data.Body;
            body.CheckCondition(true);
            if (Being.Id == 0) {
                D.Err("Being was destroyed after CheckCondition in InitHealth! (Rebuilding?) {0}", Being);
                return;
            }
            var isAble = body.IsAbleToWork;
            Being.Graphics.SetVertical(isAble);
            var isAlive = body.IsAlive;
            if (isAlive) {
                if (!S.Beings.Alive.AddUniqueInefficient(Being)) {
                    D.Err("Adding duplicate being to Alive list! {0}", Being);
                }
            } else {
                string icon;
                string msg;
                if (Being.Data.Species.IsBiological || Being.Data.Body.HasRemovableParts) {
                    icon = IconId.CSkullTransparent;
                    msg = T.InfoBeingDead;
                } else {
                    icon = IconId.CBody;
                    msg = T.Empty;
                }
                Being.Data.Body.Incapacitate(icon,
                    ImmobilizeReasonIdH.Dead, msg, true);
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.HealthWentToZero) {
                if (TryGetGoal(out var g)) {
                    AIGoal.Cancel(g, G.Ticks, T.Dead);
                }
                Being.Data.DeactivateConditionsOnDeath();
            }
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
        }
    }
}
