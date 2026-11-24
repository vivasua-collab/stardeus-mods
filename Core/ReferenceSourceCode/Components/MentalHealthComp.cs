// #define DEBUG_MOOD
using System;
using System.Linq;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Utils;
using UnityEngine;
using Game.Systems.Stats;
using Game.Systems.Mental;
using Game.Input;

namespace Game.Components {
    public sealed class MentalHealthComp : BaseComponent<MentalHealthComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new MentalHealthComp());
        }

        public override string ToString() {
            return " * MentalHealth []";
        }

        private Stat sanityStat;
        private Stat SanityStat => sanityStat ??= Being.Data.StatsBeing.Sanity;
        private MentalHealth health;
        private MentalHealth Health => health ??= Being.Data.Mind.Health;
        public void AfterReplaceMind() {
            health = null;
        }

        private string trend => Health.Trend.Value;

        public string Tooltip(bool full) {
            if (Health == null) { return null; }
            var sb = Caches.StringBuilder2;
            sb.Clear();
            sb.AppendFormat("{0}: {1} ", T.MentalBreakdownRisk, Units.PercentageColoredInverse(Health.DailyBreakdownChance));
            sb.AppendLine(trend);
            if (!full || health.CurrentEffects.Count == 0) {
                return sb.ToString();
            }
            foreach (var e in Health.CurrentEffects.OrderByDescending(c => c.IsPermanent)
                    .ThenByDescending(c => c.StartTick)) {
                var tr = e.Reason;
                var tip = e.IsPermanent
                    ? T.StatusPermanent
                    : T.EndsIn_V1.F(Units.TicksToTime(e.EndTick - G.Ticks));
                var v = Texts.ColoredSignedNum(e.Effect);
                sb.AppendFormat("{0} {1}: {2} {3}\n", Texts.Accented(GlyphMap.ArrowRight), tr, v, Texts.GreyParentheses(tip));
            }

            return sb.ToString();
        }

        public void AddTooltip(StringBuilder sb) {
            if (Health == null) { return; }
            sb.AppendFormat("<b>{0}</b>: {1}", T.StatSanity, SanityStat.Display());
            // No trend while sanity sits at its target, or before the first tick
            var t = trend;
            if (!string.IsNullOrEmpty(t)) {
                sb.AppendFormat(" ({0})", t);
            }
            sb.AppendFormat("\n{0}: {1}", T.MentalBreakdownRisk,
                Units.Percentage(Health.DailyBreakdownChance));
        }



        public void AfterAdd(MentalEffect effect, bool isNew) {
            if (isNew && Math.Abs(effect.Effect) >= 5) {
                DisplayMoodBubble(effect);
            }
        }

        public void DisplayMoodBubble(MentalEffect effect) {
            var icon = effect.Icon;
            var e = effect.Effect;
            string text = $"{effect.Reason}: {Texts.ColoredSignedNum(e)}";
            if (icon == null) {
                if (e > 0) {
                    icon = IconId.CMoodGood;
                } else if (effect.Effect <= -10) {
                    icon = IconId.CMoodBad;
                } else { // 0 to -9
                    icon = IconId.CMoodNeutral;
                }
            }
            ShowThoughtBubble(icon, text, 5);
        }
    }
}
