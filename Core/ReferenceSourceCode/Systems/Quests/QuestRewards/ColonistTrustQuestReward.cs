using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Mental;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Changes how much the colonist who gave the quest trusts the AI, for a while. With the
    /// Penalty flag it applies when the quest fails instead.
    /// Config: Magnitude (int percentage points of AI Alignment, negative for a penalty),
    ///         DurationHours (float, default 120 days),
    ///         ReasonKey (string, default trust.favor.fulfilled or trust.favor.failed).
    /// </summary>
    public sealed class ColonistTrustQuestReward : QuestReward {
        [SelfInit]
        private static void Register() {
            Registries.QuestRewards.Add(RewardId,
                (s, def, data, seed, l) => new ColonistTrustQuestReward(s, def, data, seed, l));
        }

        public const string RewardId = "ColonistTrust";

        private readonly int magnitude;
        private readonly long durationTicks;
        private readonly string reasonKey;

        public ColonistTrustQuestReward(GameState s, QuestRewardDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            if (isLoading && data.IsEnded) { return; }
            magnitude = def.Config.GetInt(PropertyIdH.Magnitude, 15);
            var hours = def.Config.GetFloat(PropertyIdH.DurationHours, 24f * 120f);
            durationTicks = (long)(hours * Consts.TicksPerHour);
            reasonKey = def.Config.GetString(PropertyIdH.ReasonKey,
                magnitude >= 0 ? "trust.favor.fulfilled" : "trust.favor.failed");
            Icon = IconId.CHeart;
            var who = data.Provider is ColonistQuestProvider cp
                ? cp.BeingData.Mind.LinkToShortName
                : Texts.Red(T.Unknown);
            Title = $"{who}: {T.StatAialignment} {Units.PercentageSigned(magnitude / 100f)}";
        }

        public override string Apply() {
            if (Data.Provider is not ColonistQuestProvider cp) {
                D.Err("ColonistTrustQuestReward: provider is not ColonistQuestProvider | {0}", this);
                return null;
            }
            var mind = cp.BeingData.Mind;
            if (mind == null) {
                D.Err("ColonistTrustQuestReward: colonist has no mind | {0}", cp.BeingData);
                return null;
            }
            var source = magnitude >= 0 ? TrustSourceIdH.FavorFulfilled : TrustSourceIdH.FavorFailed;
            mind.Trust.Add(source, magnitude, G.Ticks, durationTicks, reasonKey);
            return Title;
        }
    }
}
