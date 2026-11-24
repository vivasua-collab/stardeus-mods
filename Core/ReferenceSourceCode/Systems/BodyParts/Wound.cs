using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Conditions;
using Game.Utils;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.BodyParts {
    [MessagePackObject]
    public struct Wound {
        [Key("Type")] public DamageType Type;
        [Key("Severity")] public Severity Severity;
        [Key("BleedRate")] public float BleedLitersPerHour;
        /// <summary>
        /// 0 - not healed at all, 1 - fully healed
        /// </summary>
        [Key("HealAmt")] public float HealAmt;
        [Key("CreatedAt")] public long CreatedAt;
        /// <summary>
        /// Ignores ConditionId
        /// </summary>
        [Key("Flags")] public WoundFlags Flags;
        /// <summary>
        /// If this wound is caused by a condition, it cannot start healing before
        /// condition is removed. This is useful for disease induced wounds.
        /// </summary>
        [Key("ConditionIds")] public int[] ConditionIds;
        /// <summary>
        /// Source of this wound. If it's deepened, source could be overwritten.
        /// </summary>
        [Key("Source")] public string Source;
        // Severity of the scar this wound may leave once healed: one step milder than the worst
        // severity it reached. Irrelevant when it can't scar.
        [Key("ScarSeverity")] public Severity ScarSeverity;
        // Derived rather than saved, so tuning (and old saves) always use the current chances
        [IgnoreMember] public float ScarChance => Scar.ChanceFor(Type, ScarSeverity);
        // A destroyed part's wound whose pain has fully settled (WoundPain)
        [IgnoreMember] public bool IsSettled => Severity >= Severity.Irreparable && HealAmt >= 1f;

        public bool CanHeal(BeingData being, bool ignoreCondition) {
            var canHeal = Severity < Severity.Irreparable
                && ((Flags & WoundFlags.CantHeal) != WoundFlags.CantHeal);
            if (canHeal && !ignoreCondition && ConditionIds != null && being != null) {
                // if we still have the condition, we can't start healing this wound
                for (int i = ConditionIds.Length - 1; i >= 0; i--) {
                    int condId = ConditionIds[i];
                    if (being.HasCondition(condId)) {
                        return false;
                    }
                }
            }
            return canHeal;
        }

        public void AddCausingCondition(int conditionIdH) {
            if (conditionIdH == 0) { return; }
            if (ConditionIds == null) {
                ConditionIds = new int[1] { conditionIdH };
                return;
            }
            for (int i = ConditionIds.Length - 1; i >= 0; i--) {
                int cid = ConditionIds[i];
                if (conditionIdH == cid) {
                    return;
                }
            }

            var idx = ConditionIds.Length;
            Array.Resize(ref ConditionIds, idx + 1);
            ConditionIds[idx] = conditionIdH;
        }

        public override string ToString() {
            return D.DumpInstance(this, true);
            // return $"Wound[{Type}|{Severity}|Bleed:{BleedPercentPerHour}]";
        }

        public string TitleFor(BodyPartSlot slot) {
            bool isBio;
            if (slot.IsPartRemoved) {
                isBio = slot.Body.Owner.Species.IsBiological;
            } else {
                isBio = (slot.InstalledDef.Flags & BodyPartFlags.Biological) == BodyPartFlags.Biological;
            }
            string title;
            if (isBio) {
                title = T.WoundTitle_V2.F(
                    Severities.NameFor(Severity, false), DamageTypes.NameFor(Type));
            } else {
                title = T.PartDamageTitle_V2.F(
                    Severities.NameFor(Severity, false), DamageTypes.NameFor(Type));
            }
            return title;
        }

        // Wound is a struct, so a tooltip closure that captures one shows frozen HealAmt.
        // Resolve the live wound by Type on every call; fall back to the snapshot if the
        // wound was removed before the UI row got rebuilt.
        public static string LiveTooltipFor(BodyPartState state, in Wound snapshot, bool isBio) {
            if (state != null && state.TryGetWound(snapshot.Type, out var live)) {
                return TooltipFor(live, isBio);
            }
            return TooltipFor(snapshot, isBio);
        }

        public static string TooltipFor(Wound wnd, bool isBio) {
            var isRepairable = wnd.Severity < Severity.Irreparable;
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.Append(Texts.SubTitle(isBio ? T.NounWound : T.NounDamage));
            var hasTooltipRow = false;
            void AppendRowPrefix() {
                if (hasTooltipRow) {
                    sb.Append('\n');
                } else {
                    hasTooltipRow = true;
                }
            }

            if (wnd.ConditionIds != null
                    && (wnd.Source == null || wnd.ConditionIds.Length > 1)) {
                AppendRowPrefix();
                sb.AppendFormat(" {0} {1}: ", GlyphMap.ArrowRight, T.NounCause);
                var wl = wnd.ConditionIds.Length;
                bool needsComma = false;
                if (wnd.Source != null) {
                    sb.Append(Texts.Accented(wnd.Source));
                    needsComma = true;
                }
                for (int i = 0; i < wl; i++) {
                    int cond = wnd.ConditionIds[i];
                    var cnd = Registries.Conditions.FastGet(cond);
                    if (cnd == null) {
                        D.Err("Could not find {0} ({1}) condition!", cond,
                            ConditionIdH.ResolveName(cond));
                        continue;
                    }
                    // already added
                    if (cnd.NameT == wnd.Source) {
                        continue;
                    }
                    if (needsComma) {
                        sb.Append(", ");
                    } else {
                        needsComma = true;
                    }
                    sb.Append(Texts.Accented(cnd.NameT));
                }
            } else {
                if (wnd.Source != null) {
                    var msg = wnd.Source;
                    if (!msg.Contains('<')) {
                        msg = Texts.Accented(msg);
                    }
                    AppendRowPrefix();
                    sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight, T.InflictedBy_V1.F(msg));
                } else {
                    if (wnd.Type != DamageType.Decay) {
                        AppendRowPrefix();
                        sb.AppendFormat(" {0} {1}: <b>{2}</b>", GlyphMap.ArrowRight, T.NounCause, T.Unknown);
                    }
                }
            }
            AppendRowPrefix();
            sb.AppendFormat(" {0} {1}: <b>{2}</b>", GlyphMap.ArrowRight, T.DamageType, DamageTypes.NameFor(wnd.Type));
            AppendRowPrefix();
            sb.AppendFormat(" {0} {1}: <b>{2}</b>", GlyphMap.ArrowRight, T.NounSeverity, Severities.NameFor(wnd.Severity, false));
            var scarChance = wnd.ScarChance;
            if (scarChance > 0f) {
                AppendRowPrefix();
                sb.AppendFormat(" {0} {1}: <b>{2}</b> ({3})", GlyphMap.ArrowRight, T.ChanceScar,
                    Units.Percentage(scarChance), Severities.NameForNegative(wnd.ScarSeverity));
            }
            if (wnd.Type != DamageType.Decay) {
                AppendRowPrefix();
                sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight, T.WoundInflictedTimeAgo_V1.F(
                    Texts.Accented(Units.TicksToTime(G.Ticks - wnd.CreatedAt, true))));
            }
            if (isBio) {
                if (isRepairable && wnd.HealAmt > 0f) {
                    AppendRowPrefix();
                    sb.AppendFormat(" {0} {1}: <b>{2}</b>", GlyphMap.ArrowRight, T.HealAmount,
                        Units.Percentage(wnd.HealAmt));
                }
                AppendRowPrefix();
                sb.AppendFormat(" {0} {1}: <b>{2:0} {3}</b>", GlyphMap.ArrowRight, T.BleedRate,
                    wnd.BleedLitersPerHour * 1000f, T.MillilitersPerHour);
            } else {
                if (isRepairable && wnd.HealAmt > 0f) {
                    AppendRowPrefix();
                    sb.AppendFormat(" {0} {1}: <b>{2}</b>", GlyphMap.ArrowRight, T.PartRepairProgress,
                        Units.Percentage(wnd.HealAmt));
                }
            }


            if (!wnd.CanHeal(null, false)) {
                AppendRowPrefix();
                sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight, T.CantHeal);
            }
            return sb.ToString();
        }

        public static float GetBleedChance(Severity sev, DamageType type,
                float totalBloodAmt, out float bleedLitersPerHour) {

            float bleedChanceSev = 0f;
            float bleedRateLitersSev = 0f;

            switch (sev) {
                case Severity.Mild: {
                    bleedRateLitersSev = 0.05f;
                    bleedChanceSev = 0.05f;
                    break;
                }
                case Severity.Moderate: {
                    bleedRateLitersSev = 0.1f;
                    bleedChanceSev = 0.33f;
                    break;
                }
                case Severity.Severe: {
                    bleedRateLitersSev = 0.15f;
                    bleedChanceSev = 0.8f;
                    break;
                }
                case Severity.Critical: {
                    bleedRateLitersSev = 0.25f;
                    bleedChanceSev = 1.5f;
                    break;
                }
                case Severity.Irreparable: {
                    bleedRateLitersSev = 0.25f;
                    bleedChanceSev = 2.0f;
                    break;
                }
                case Severity.Fatal: {
                    bleedRateLitersSev = 1.0f;
                    bleedChanceSev = 2.0f;
                    break;
                }
            }

            float bleedChanceAddType = 0f;
            float bleedRateMulType = 0f;

            switch (type) {
                case DamageType.Blunt: {
                    bleedChanceAddType = -0.3f;
                    bleedRateMulType = 0.5f;
                    break;
                }
                case DamageType.Sharp: {
                    bleedChanceAddType = 1.0f;
                    bleedRateMulType = 1.25f;
                    break;
                }
                case DamageType.Piercing: {
                    bleedChanceAddType = 1.0f;
                    bleedRateMulType = 1f;
                    break;
                }
                // shrapnel and torn flesh: between piercing and blunt (it used to never bleed)
                case DamageType.Explosive: {
                    bleedChanceAddType = 0.35f;
                    bleedRateMulType = 0.75f;
                    break;
                }
                case DamageType.Energy: {
                    bleedChanceAddType = -0.33f;
                    bleedRateMulType = 0.25f;
                    break;
                }
            }


            bleedLitersPerHour = bleedRateLitersSev * bleedRateMulType * totalBloodAmt;
            var bleedChance = bleedChanceSev + bleedChanceAddType;
            // D.Warn("Bleed chance {0} / {1}L/h for {2} {3}",
            //     bleedChance, bleedLitersPerHour, sev, type);
            return bleedChance;
        }
        /// <returns>true if wound was removed</returns>
        public static bool TryHeal(BeingData data, float healRate,
                BodyPartSlot slot, ref Wound w,
                    ref bool needsCondUpdate,
                    ref bool needsCondUIUpdate) {
            if (healRate < 0.00001f || w.Severity >= Severity.Irreparable) {
                return false;
            }
            // locked by the condition that keeps causing it (Starvation, Toxemia), which
            // releases it when the cause is gone
            if ((w.Flags & WoundFlags.CantHeal) == WoundFlags.CantHeal) {
                return false;
            }
            if (w.ConditionIds != null) {
                // check if any of the causing conditions are still present
                for (int i = w.ConditionIds.Length - 1; i >= 0; i--) {
                    int cond = w.ConditionIds[i];
                    if (data.HasCondition(cond)) {
                        // can't heal properly yet
                        healRate *= 0.15f;
                        break;
                        // return false;
                    }
                }
            }

            var qualityBefore = slot.State.Quality;
            var removed = TryHeal(slot.State, healRate, ref w,
                ref needsCondUpdate, ref needsCondUIUpdate, data);
            // a scar formed: removing the wound doesn't always change condition (a crippled part
            // stays capped), so UpdateCondition alone could leave the stats at the old quality
            if (slot.State.Quality != qualityBefore) {
                slot.OnQualityChanged();
            }
            return removed;
        }

        public static bool TryHeal(BodyPartState state, BodyPartType type,
                float healRate) {
            if (state == null || type == null || healRate < 0.00001f
                    || Arrays.IsEmpty(state.Wounds)) {
                return false;
            }
            bool changed = false;
            bool needsCondUpdate = false;
            bool needsCondUIUpdate = false;
            for (int i = state.Wounds.Count - 1; i >= 0; i--) {
                var w = state.Wounds[i];
                if (!w.CanHeal(null, true)) { continue; }
                var healBefore = w.HealAmt;
                var severityBefore = w.Severity;
                if (TryHeal(state, healRate, ref w,
                        ref needsCondUpdate, ref needsCondUIUpdate, null)) {
                    changed = true;
                    continue;
                }
                state.Wounds[i] = w;
                if (w.HealAmt != healBefore || w.Severity != severityBefore) {
                    changed = true;
                }
            }
            if (needsCondUpdate) {
                state.UpdateCondition(true, type);
            }
            return changed;
        }

        private static bool TryHeal(BodyPartState state, float healRate,
                ref Wound w, ref bool needsCondUpdate,
                ref bool needsCondUIUpdate, BeingData data) {
            // healing the organ / body part
            var healBefore = w.HealAmt;
            var healAfter = healBefore + healRate;
            if (healAfter >= 1f) {
                if (Severities.TryDecrease(w.Severity, out var newS)) {
                    w.Severity = newS;
                    w.HealAmt = 0f;
                    if (w.Type == DamageType.Decay) {
                        state.Decay = ConditionDecay.SeverityToDecayLowerBound(newS);
                    }
                    needsCondUpdate = true;
                    needsCondUIUpdate = true;
                    return false;
                }
                // healed, remove wound
                if (state.RemoveWound(w.Type)) {
                    Scar.TryFormFrom(state, w);
                    needsCondUpdate = true;
                    needsCondUIUpdate = true;
                    return true;
                } else {
                    D.Err("failed to remove a wound: {0} for {1}",
                        D.DumpInstance(w), data);
                }
            }
            w.HealAmt = healAfter;
            return false;
        }

        // Only call for biological parts. Mild wounds never scar, Irreparable ones never heal.
        // Tracks the worst severity, so a wound that already healed down keeps its scar size.
        public static void UpdateScarSeverity(ref Wound wound) {
            if (wound.Severity <= Severity.Mild || wound.Severity > Severity.Critical) { return; }
            if (!Scar.CanScar(wound.Type)) { return; }
            var scarSev = wound.Severity - 1;
            if (scarSev > wound.ScarSeverity) {
                wound.ScarSeverity = scarSev;
            }
        }
    }
}
