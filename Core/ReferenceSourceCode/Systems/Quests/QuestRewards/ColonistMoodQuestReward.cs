using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Mental;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Grants a long-lasting positive mood effect to the colonist who gave the quest.
    /// Config: Magnitude (int, default 30), DurationHours (float, default 168 = 7 days),
    ///         ReasonKey (string, default "mood.colonist.favor.fulfilled").
    /// </summary>
    public sealed class ColonistMoodQuestReward : QuestReward {
        [SelfInit]
        private static void Register() {
            Registries.QuestRewards.Add(RewardId,
                (s, def, data, seed, l) => new ColonistMoodQuestReward(s, def, data, seed, l));
        }

        public const string RewardId = "ColonistMood";

        private readonly int magnitude;
        private readonly long durationTicks;
        private readonly string reasonKey;

        public ColonistMoodQuestReward(GameState s, QuestRewardDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            if (isLoading && data.IsEnded) { return; }
            magnitude = def.Config.GetInt(PropertyIdH.Magnitude, 15);
            var hours = def.Config.GetFloat(PropertyIdH.DurationHours, 24f * 3f);
            durationTicks = (long)(hours * Consts.TicksPerHour);
            reasonKey = def.Config.GetString(PropertyIdH.ReasonKey,
                data.Def.TitleKey);
            Icon = IconId.CHeart;
            if (data.Provider is ColonistQuestProvider cp) {
                Title = T.QuestRewardColonistMood_V1.F(cp.BeingData.Mind.LinkToShortName);
            } else {
                Title = T.QuestRewardColonistMood_V1.F(Texts.Red(T.Unknown));
            }
        }

        public override string Apply() {
            if (Data.Provider is not ColonistQuestProvider cp) {
                D.Err("ColonistMoodQuestReward: provider is not ColonistQuestProvider | {0}", this);
                return null;
            }
            var beingData = cp.BeingData;
            if (beingData.Mind?.Health == null) {
                D.Err("ColonistMoodQuestReward: being has no MentalHealth | {0}", beingData);
                return null;
            }
            var posIdx = beingData.Being?.PosIdx ?? Pos.Invalid;
            var reason = reasonKey.T();
            var effect = MentalEffect.CreateWithIcon(
                    posIdx, IconId.CMoodGood, G.Ticks, durationTicks, reason, magnitude)
                .WithTooltip(reason);
            beingData.Mind.Health.AddEffect(S, effect);
            return T.QuestRewardColonistMoodApply_V1.F(beingData.Mind.LinkToShortName);
        }
    }
}
