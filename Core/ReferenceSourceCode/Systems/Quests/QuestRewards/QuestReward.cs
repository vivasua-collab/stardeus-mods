using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Utils;

namespace Game.Systems.Quests {
    public abstract class QuestReward {
        public delegate QuestReward Factory(GameState s, QuestRewardDef def,
            QuestData data, int seed, bool isLoading);
        private static Curve sectorDifficultyMulCurve;
        protected readonly GameState S;
        protected readonly QuestRewardDef def;
        public QuestRewardDef Def => def;
        public string Title;
        public string Desc;
        public string Icon;
        public readonly QuestRewardFlags Flags;
        public readonly QuestData Data;
        public virtual string Tooltip => Desc;
        protected ComponentConfig config => Def.Config;
        protected readonly int seed;
        public int Seed => seed;
        protected QuestReward(GameState s, QuestRewardDef def,
                QuestData data, int seed, bool isLoading) {
            S = s;
            Flags = def.Flags;
            Data = data;
            this.seed = seed;
            this.def = def;
        }

        /// <summary>
        /// Gives reward
        /// </summary>
        /// <returns>reward text line</returns>
        public abstract string Apply();
        /// <summary>
        /// Reward value multiplier (0.5x..2.0x) for the sector the quest was received in.
        /// </summary>
        protected float SectorDifficultyMul() {
            sectorDifficultyMulCurve ??= CurveDef.Get(CurveId.Quests_RewardSectorDifficultyMul);
            return sectorDifficultyMulCurve.Evaluate(S.Situation.SectorDifficulty);
        }
        protected QuestVar GetQuestVar(string id) {
            if (Data.Variables.TryGetValue(Hashes.S(id), out var qVar)) {
                return qVar;
            }
            throw new System.Exception($"No QuestVar with id: {id} | {def} | {Data}");
        }
        protected QuestVar GetQuestVarByKey(int key, bool isRequired = true) {
            var keyStr = def.Config.GetString(key, null);
            if (isRequired && keyStr == null) {
                throw new System.Exception($"No QuestVar by key: {PropertyIdH.ResolveName(key)} | {def} | {Data}");
            }
            return GetQuestVar(keyStr);
        }

        public override string ToString() {
            return $"QuestReward[{Def}|{Data}]";
        }
    }
}
