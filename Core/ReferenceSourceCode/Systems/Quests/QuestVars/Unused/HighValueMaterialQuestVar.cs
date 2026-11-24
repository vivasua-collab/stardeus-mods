using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class HighValueMaterialQuestVar : QuestVar, IQuestVar<Mat> {
        // [SelfInit]
        private static void Register() {
            Registries.QuestVars.Add(VarId,
                (s, def, data, seed, l) => new HighValueMaterialQuestVar(s, def, data, seed, l));
        }

        public const string VarId = "HighValueMaterial";
        private Mat mat;
        public Mat Value => mat;

        public HighValueMaterialQuestVar(GameState s, QuestVarDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {

            var matTypeIdKey = def.Config.GetString(PropertyIdH.DVar_MatTypeId);
            var amtKey = def.Config.GetString(PropertyIdH.DVar_Amount);
            if (isLoading) {
                var typeId = data.Data.GetString(matTypeIdKey);
                var amt = data.Data.GetInt(amtKey);
                var matType = MatType.Get(typeId);
                if (matType == null) {
                    D.Err("Failed loading HighValueMaterial type: {0}", typeId);
                } else {
                    mat = new Mat() {
                        Type = matType,
                        StackSize = amt,
                        MaxStackSize = amt,
                    };
                    return;
                }
            }
            var rng = new Rng(seed);
            var netWorthFrac = rng.Curve(def.Config.GetFloatSet(PropertyIdH.NetWorthCurve));
            var min = def.Config.GetInt(PropertyIdH.AmountMin, 0);
            var max = def.Config.GetInt(PropertyIdH.AmountMax, 0);
            var minPrice = def.Config.GetInt(PropertyIdH.MinPrice, 0);
            S.Sys.Economy.UpdateNetWorth();
            var nw = S.Situation.NetWorth;
            var value = nw * netWorthFrac;
            if (min > 0 && value < min) {
                value = min;
            }
            if (max > 0 && value > max) {
                value = max;
            }
            mat = S.Query.RandomMatWithValue.Ask(2, minPrice, value, rng);
            var d = data.GetOrCreateData();
            d.SetString(matTypeIdKey, mat.Type.Id);
            d.SetInt(amtKey, mat.StackSize);
        }
    }
}
