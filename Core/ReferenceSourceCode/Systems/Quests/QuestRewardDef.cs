using System;
using System.Text;
using Game.Components;
using Game.Data;
using Game.Utils;
using KL.Utils;
using Newtonsoft.Json;

namespace Game.Systems.Quests {
    [Serializable]
    public sealed class QuestRewardDef {
        public string Id;
        public string Type;
        [JsonEnum(typeof(Game.Systems.Quests.QuestRewardFlags))]
        public string[] RewardFlags;
        public float Chance;
        public QuestRewardFlags Flags;
        private QuestReward.Factory factory;
        [JsonConverter(typeof(ComponentConfigJsonConverter))]
        public ComponentConfig Config;
        public bool Deserialize(StringBuilder err, object context) {
            if (string.IsNullOrWhiteSpace(Id)) {
                err.AppendFormat("{0}: {1}: field missing: Id\n", context, this);
                return false;
            }
            if (string.IsNullOrWhiteSpace(Type)) {
                err.AppendFormat("{0}: {1}: field missing: Type\n", context, this);
                return false;
            }
            Flags = Enums.FlagsFromStringArray_Int<QuestRewardFlags>(RewardFlags, context);
            factory = Registries.QuestRewards.Get(Type);
            if (factory == null) {
                err.AppendFormat("Couldn't find QuestReward implementation by id {0} for: {1}",
                    Type, context);
                return false;
            }
            return true;
        }

        public override string ToString() {
            return $"QuestRewardDef[{Id}|{Type}|{Flags}]";
        }

        public QuestReward CreateFor(GameState s, QuestData data, int seed, bool isLoading) {
            try {
                return factory.Invoke(s, this, data, seed, isLoading);
            } catch (Exception ex) {
                D.LogEx(ex, "Failed creating QuestReward: {0}", this);
                return null;
            }
        }

        public static QuestReward[] CreateRewardsFor(GameState s, QuestRewardDef[] defs,
                QuestData questData, int seed, bool isLoading) {
            if (Arrays.IsEmpty(defs)) {
                return null;
            }
            var rewards = new QuestReward[defs.Length];
            for (int i = defs.Length - 1; i >= 0; i--) {
                QuestRewardDef rw = defs[i];
                var reward = rw.CreateFor(s, questData, seed, isLoading: isLoading);
                if (reward == null) {
                    throw new Exception(string.Format(
                        "Failed creating quest reward from def {0} for quest {1}",
                            rw, questData));
                }
                rewards[i] = reward;
            }
            return rewards;
        }

        public string DataKey(string k) {
            return $"r.{Id}.{k}";
        }
    }
}
