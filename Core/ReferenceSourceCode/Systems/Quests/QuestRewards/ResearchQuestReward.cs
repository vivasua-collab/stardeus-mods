using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class ResearchQuestReward : QuestReward, IRewardPackage {
        [SelfInit]
        private static void Register() {
            Registries.QuestRewards.Add(RewardId,
                (s, def, data, seed, l) => new ResearchQuestReward(s, def, data, seed, l));
        }

        public const string RewardId = "Research";
        public readonly Def researchDef;
        public ResearchQuestReward(GameState s, QuestRewardDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            var defId = config.GetString(PropertyIdH.Definition, null);
            researchDef = The.Defs.Get(defId);
            Icon = researchDef.Preview;
            Title = $"{T.ResearchDiscovered}: {researchDef.NameT}";
            if (researchDef?.Researchable?.IsTreeNode != true) {
                D.Throw("Definition not found or not Researchable: {0} for reward {1}",
                    defId, this);
            }
        }

        public bool PopulatePackage(QuestPackage package) {
            package.ResearchDefId = researchDef.Id;
            return true;
        }

        public override string Apply() {
            var res = S.Research.Get(researchDef.IdH);
            if (res.IsUnlocked && res.IsDiscovered) {
                return null;
            }
            var path = S.Research.TreePathFor(researchDef);
            if (!res.IsDiscovered) {
                S.Research.DiscoverWithPrerequisites(researchDef.IdH);
            }
            if (!config.GetBool(PropertyIdH.DiscoverOnly, false)) {
                S.Research.UnlockAndUnbookmark(res);
                return $"{Texts.Accented(T.ResearchUnlockedFull)}: {path}";
            } else {
                return $"{Texts.Accented(T.ResearchDiscovered)}: {path}";
            }
        }
    }
}
