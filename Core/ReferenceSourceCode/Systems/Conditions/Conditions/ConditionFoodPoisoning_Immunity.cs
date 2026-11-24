using System;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Conditions {
    public sealed class ConditionFoodPoisoning_Immunity : ConditionImmunity {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionFoodPoisoning_Immunity());
        }

        private ConditionFoodPoisoning_Immunity() : base(ConditionId.FoodPoisoning_Immunity,
                ConditionId.FoodPoisoning, 3f / Consts.TicksPerDay, IconId.CHealth) {
            IsPositive = true;
            nameTFunc = () => T.DiseaseImmunity_V1.F(T.BreakdownBreakdownfoodpoisoning);
        }
    }
}
