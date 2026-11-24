using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Quests {
    public sealed class FloatQuestVar : QuestVar, IQuestVar<float> {
        [SelfInit]
        private static void Register() {
            Registries.QuestVars.Add(VarId,
                (s, def, data, seed, l) => new FloatQuestVar(s, def, data, seed, l));
        }

        public const string VarId = "Float";
        private float val;
        public float Value => val;

        public FloatQuestVar(GameState s, QuestVarDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            Curve amtCurve = def.Config.GetFloatSet(PropertyIdH.Curve);
            var rng = new Rng(seed);
            val = rng.Curve(amtCurve);
        }
    }
}
