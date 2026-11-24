using System;
using System.Linq;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Quests {
    [MessagePackObject]
    public sealed class QuestRewardData {
        [Key("Type")] public string Type;
        // The seed a reward rolls its contents with. Not saved before, a loaded reward got
        // Def.GetHashCode(), which differs per process, so the reward changed on every load
        [Key("Seed")] public int Seed;

        public static QuestRewardData[] Serialize(QuestReward[] rewards) {
            if (Arrays.IsEmpty(rewards)) {
                return null;
            }
            var res = new QuestRewardData[rewards.Length];
            for (int i = rewards.Length - 1; i >= 0; i--) {
                QuestReward reward = rewards[i];
                res[i] = Serialize(reward);
            }
            return res;
        }

        private static QuestRewardData Serialize(QuestReward reward) {
            var d = new QuestRewardData();
            d.Type = reward.Def.Type;
            d.Seed = reward.Seed;
            return d;
        }

        public static bool TryDeserialize(GameState s, QuestRewardData[] rewards, QuestData q) {
            if (Arrays.IsEmpty(rewards)) {
                return true;
            }

            q.Rewards = new QuestReward[rewards.Length];
            bool needsCleanup = false;
            for (int i = rewards.Length - 1; i >= 0; i--) {
                QuestRewardData d = rewards[i];
                var reward = Deserialize(s, i, d, q);
                if (reward == null) {
                    needsCleanup = true;
                } else {
                    q.Rewards[i] = reward;
                }
            }
            if (needsCleanup) {
                q.Rewards = q.Rewards.Where(r => r != null).ToArray();
            }
            return true;
        }

        private static QuestReward Deserialize(GameState s, int idx, QuestRewardData data,
                QuestData questData) {
            var def = Arrays.SafeGet(questData.Def.Rewards, idx);
            if (def == null) {
                D.Err("QuestRewardDef with id {0} that was saved for quest {1} is no longer defined in the quest def",
                    data.Type, questData.Def);
                return null;
            }
            if (def.Type != data.Type) {
                D.Err("QuestRewardDef Type mismatch! Loading {0} when actual type is {1} | {2}",
                    data.Type, def.Type, questData);
                return null;
            }
            // saves from before the seed was saved get one that is at least stable per quest
            var seed = data.Seed != 0 ? data.Seed : Hashes.Quick((int) questData.Id, idx + 1);
            var reward = def.CreateFor(s, questData, seed, isLoading: true);
            return reward;
        }
    }
}
