using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class FactionAttitudeQuestReward : QuestReward {
        [SelfInit]
        private static void Register() {
            Registries.QuestRewards.Add(RewardId,
                (s, def, data, seed, l) => new FactionAttitudeQuestReward(s, def, data, seed, l));
        }

        public const string RewardId = "FactionAttitude";
        private readonly float change;
        private readonly FactionDef faction;
        public FactionAttitudeQuestReward(GameState s, QuestRewardDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            if (isLoading && data.IsEnded) { return; }
            var cfg = def.Config;
            var factionId = cfg.GetString(PropertyIdH.FactionId, null);
            if (factionId == null) {
                faction = data.Provider?.Faction;
            } else {
                faction = FactionDef.Get(factionId);
            }
            if (faction == null) {
                D.Throw("FactionAttitude could find faction by FactionId({0}) and / or data.Provider.Faction | {1}",
                    factionId, this);
            }
            var amt = cfg.GetInt(PropertyIdH.AttitudeChange, 0);
            if (amt == 0) {
                D.Throw("FactionAttitude reward has not defined AttitudeChange: {0}", this);
            }
            change = amt / 100f;
            Icon = faction.FlagIcon;
            Title = $"{T.RelationshipWith_V1.F(faction.NameT)}: {Units.PercentageColoredSigned(change)}";
        }

        public override string Apply() {
            S.Query.ChangeFactionAttitude.Ask(faction, FactionDef.Player, change);
            return Title;
        }
    }
}
