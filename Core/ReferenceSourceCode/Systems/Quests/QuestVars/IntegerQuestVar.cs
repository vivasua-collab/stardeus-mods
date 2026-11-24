using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Quests {
    public sealed class IntegerQuestVar : QuestVar, IQuestVar<int> {
        [SelfInit]
        private static void Register() {
            Registries.QuestVars.Add(VarId,
                (s, def, data, seed, l) => new IntegerQuestVar(s, def, data, seed, l));
        }

        public const string VarId = "Integer";
        private int val;
        public int Value => val;

        public IntegerQuestVar(GameState s, QuestVarDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            Curve amtCurve = def.Config.GetFloatSet(PropertyIdH.Curve);
            var rng = new Rng(seed);
            var amt = Mathf.RoundToInt(rng.Curve(amtCurve));
            // var step = def.Config.GetInt(PropertyIdH.CurveStep, 0);
            // if (step > 0) {
            //     var x = amt / step;
            //     amt = x * step;
            // }
            val = amt;
        }
    }
}
