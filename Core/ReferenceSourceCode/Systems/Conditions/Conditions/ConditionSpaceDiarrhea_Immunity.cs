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
    public sealed class ConditionSpaceDiarrhea_Immunity : ConditionImmunity {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionSpaceDiarrhea_Immunity());
        }

        private ConditionSpaceDiarrhea_Immunity() : base(ConditionId.SpaceDiarrhea_Immunity,
                ConditionId.SpaceDiarrhea, 2f / Consts.TicksPerDay, IconId.CHealth) {
            IsPositive = true;
            nameTFunc = () => T.DiseaseImmunity_V1.F(T.EvtSpaceDiarrheaTitle);
        }
    }
}
