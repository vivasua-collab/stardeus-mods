using System;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionSleeping : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionSleeping());
        }

        public override Priority Importance => Priority.Normal;

        private StatModifier asleepReduce = StatModifier.AddPercent(-0.8f, T.Sleeping);
        private StatModifier asleepZero = StatModifier.Sets(0f, T.Sleeping);

        public override void OnStart(ConditionState cond, long ticks) {
            var st = cond.Owner.StatsBeing;
            st.ToiletRate.TryAddModifier(asleepReduce);
            st.HygieneRate.TryAddModifier(asleepReduce);
            st.DigestionRate.TryAddModifier(asleepReduce);
            st.HungerRate.TryAddModifier(asleepReduce);
            st.FunDropRate.TryAddModifier(asleepZero);
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            OnStart(cond, ticks);
        }

        public override void OnEnd(ConditionState cond, long ticks) {
            var st = cond.Owner.StatsBeing;
            st.ToiletRate.RemoveModifier(asleepReduce);
            st.HygieneRate.RemoveModifier(asleepReduce);
            st.DigestionRate.RemoveModifier(asleepReduce);
            st.HungerRate.RemoveModifier(asleepReduce);
            st.FunDropRate.RemoveModifier(asleepZero);
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            ValidateSleep(cond);
        }

        private static void ValidateSleep(ConditionState cond) {
            if (cond.Owner.Being == null) {
                D.Err("No being for someone who is sleeping! {0}", cond.Owner);
                return;
            }
            var curActId = cond.Owner.Being.AIAgent.CurrentAction?.IdH ?? 0;
            if (curActId != AIActionSleepInBed.ActIdH
                    && curActId != AIActionSleepAnywhere.ActIdH
                    && curActId != AIActionUseHealingDevice.ActIdH) {
                D.Err("Being has Asleep condition but isn't doing sleep actions! {0}: {1}",
                    cond.Owner.Being, cond.Owner.Being.AIAgent.CurrentAction);
                cond.Owner.RemoveCondition(cond.Owner.Being.S, cond.Condition.IdH);
            }
        }

        private ConditionSleeping() : base(ConditionId.Sleeping, IconId.CSleep) {
            nameTFunc = () => T.Sleeping;
            PreventsColonyWork = true;
        }
    }
}
