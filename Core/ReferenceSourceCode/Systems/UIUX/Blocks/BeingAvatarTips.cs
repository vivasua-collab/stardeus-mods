using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.UIUX {
    public enum AvatarStatusKind {
        None,
        Expedition,
        DirectControl,
        Combat,
        Idle,
        Goal
    }

    // Tooltip text for the status icons of WBeingAvatar
    public static class BeingAvatarTips {
        private const int MaxEffects = 8;
        private static readonly StringBuilder sb = new();
        private static readonly List<MentalEffect> effects = new();

        public static string StatusTip(GameState s, BeingData being, AvatarStatusKind kind) {
            var agent = being.Being?.AIAgent;
            switch (kind) {
                case AvatarStatusKind.Expedition:
                    return T.OnExpedition;
                case AvatarStatusKind.DirectControl:
                    return T.DirectControl;
                case AvatarStatusKind.Combat:
                    var target = agent?.CombatUnit?.AttackTarget;
                    if (target == null) { return T.Fighting; }
                    return $"{T.Fighting} {GlyphMap.ArrowRight} {Texts.LinkToBeing(target.Data)}";
                case AvatarStatusKind.Idle:
                case AvatarStatusKind.Goal:
                    var goal = agent?.Goal;
                    if (goal == null || goal.Type == AIGoalType.Idle) { return T.Idle; }
                    sb.Clear();
                    s.Query.LoadGoalTooltip.Ask(goal, false, sb);
                    return sb.ToString();
                default:
                    return null;
            }
        }

        public static string SanityTip(BeingData being, ConditionState cond, bool incapacitated) {
            sb.Clear();
            if (incapacitated) {
                sb.Append(Texts.SubTitle(T.BeingStateIncapacitated));
            } else if (cond != null) {
                sb.Append(Texts.SubTitle(cond.Condition.NameT));
                sb.Append(cond.Tooltip(false).TrimEnd());
            }
            var health = being.Mind.Health;
            if (health == null) {
                return sb.Length > 0 ? sb.ToString() : null;
            }
            if (sb.Length > 0) {
                sb.Append(GlyphMap.BRHR);
            }
            // Sanity target stays internal: only the Stats tab shows it
            sb.AppendFormat("{0}: <b>{1}</b>", T.StatSanity, Units.Percentage(health.Sanity.Value));
            var trend = health.Trend.Value;
            if (!string.IsNullOrEmpty(trend)) {
                sb.Append(' ');
                sb.Append(trend);
            }
            var band = health.CurrentBreakdownBand;
            sb.AppendFormat("\n{0}: {1}", T.MentalBreakdownRisk, BeingSanityTab.BandLabel(band));
            if (band != MentalBreakdownBand.None) {
                sb.AppendFormat(" ({0})", Units.Percentage(health.DailyBreakdownChance));
            }
            AppendEffects(health);
            return sb.ToString();
        }

        public static string WeaponTip(BeingData being) {
            sb.Clear();
            InventoryItemSlot weapon = null;
            if (being.Inventory?.Equipment.TryGetValue(InventorySlotIdH.Weapon, out var slot) == true
                    && slot.Item != null) {
                weapon = slot;
            }
            sb.AppendFormat("{0}: <b>{1}</b>", T.EquipmentSlotWeapon,
                weapon == null ? Texts.RedMinus : weapon.ItemName);
            if (weapon != null) {
                sb.AppendFormat("\n{0}: {1}", T.NounQuality,
                    Units.PercentageColored(weapon.ObjData.Quality, clamp: false));
                // The weapon's attacks the being can use: ones its jobs rule out are not listed
                bool any = false;
                var methods = being.Body.AttackMethods;
                for (int i = 0; i < methods.Count; i++) {
                    if (methods[i].Provider == weapon) {
                        AppendDps(methods[i]);
                        any = true;
                    }
                }
                if (!any) {
                    AppendDps(null);
                }
            } else {
                AppendDps(being.Being?.AIAgent.CombatUnit?.PreviewBestAttack());
            }
            return sb.ToString();
        }

        private static void AppendDps(AttackMethod attack) {
            if (attack == null) {
                sb.AppendFormat("\n{0}: {1}", T.StatDPS, Texts.RedMinus);
                return;
            }
            sb.AppendFormat("\n{0}: <b>{1}</b> {2}", T.StatDPS, attack.Stats.DPS.Display(),
                Texts.GreyParentheses(attack.NameT));
        }

        private static void AppendEffects(MentalHealth health) {
            if (health.CurrentEffects.Count == 0) { return; }
            effects.Clear();
            effects.AddRange(health.CurrentEffects);
            // Same order as the Sanity tab: permanent first, then the newest
            effects.Sort((a, b) => {
                if (a.IsPermanent != b.IsPermanent) {
                    return a.IsPermanent ? -1 : 1;
                }
                return b.StartTick.CompareTo(a.StartTick);
            });
            sb.Append(GlyphMap.BRHR);
            sb.Append(Texts.SubTitle(T.AdjOngoing));
            var ticks = G.Ticks;
            var shown = effects.Count > MaxEffects ? MaxEffects : effects.Count;
            for (int i = 0; i < shown; i++) {
                var e = effects[i];
                if (i > 0) {
                    sb.Append('\n');
                }
                sb.AppendFormat("{0} {1}", Texts.ColoredSignedNum(e.Effect), e.Reason);
                if (!e.IsPermanent) {
                    sb.Append(' ');
                    sb.Append(Texts.GreyParentheses(Units.TicksToTime(e.EndTick - ticks)));
                }
            }
            if (effects.Count > shown) {
                sb.Append('\n');
                sb.Append(Texts.Grey($"+{effects.Count - shown}"));
            }
            effects.Clear();
        }
    }
}
