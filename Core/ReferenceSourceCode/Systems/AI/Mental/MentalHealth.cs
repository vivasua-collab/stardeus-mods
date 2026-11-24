using System.Collections.Generic;
using Game.AI;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using Game.Systems.Story;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
namespace Game.Systems.Mental {
    public sealed class MentalHealth {
        private const int EffectHistorySize = 10;
        public Mind Owner;
        private static string trend0;
        private static string trendUp1;
        private static string trendUp2;
        private static string trendUp3;
        private static string trendDown1;
        private static string trendDown2;
        private static string trendDown3;
        private static string trendSleeping;
        private static string trendIncapacitated;
        public readonly B<string> Trend = new(string.Empty);
        public readonly B<string> TrendIcon = new(null);
        private MentalHealthComp comp;
        private Stat sanity;
        public Stat Sanity {
            get {
                if (sanity == null) {
                    Initialize();
                }
                return sanity;
            }
        }
        private Stat sanityTarget;
        private Stat sanityBaseline;
        /// <summary>
        /// The sanity level this being is gravitating toward. Equals SanityBaseline plus
        /// the scaled sum of all active mood effects. Updated every tick; not saved.
        /// </summary>
        public Stat SanityTarget {
            get {
                if (sanityTarget == null) {
                    Initialize();
                }
                return sanityTarget;
            }
        }
        public Stat SanityBaseline {
            get {
                if (sanityBaseline == null) {
                    Initialize();
                }
                return sanityBaseline;
            }
        }
        public long LastBreakdownTime;
        public long IgnoreDeadBodiesUntil;
        public bool IsEnlightened;
        private Stat breakdownLightThreshold;
        private Stat breakdownMediumThreshold;
        private Stat breakdownHeavyThreshold;

        /// <summary>
        /// The breakdown severity band this being is currently in, based on its Sanity
        /// relative to the per-being threshold stats. None means no natural breakdown risk
        /// (Enlightened and similar traits drive the thresholds to 0, yielding None).
        /// </summary>
        public MentalBreakdownBand CurrentBreakdownBand {
            get {
                if (sanity == null) { Initialize(); }
                var s = sanity.Value;
                // Clamp, then enforce light >= medium >= heavy while preserving the most
                // breakdown-prone (highest) threshold, so a trait/mod that raises one
                // threshold still takes effect even if it would invert the raw order.
                var l = math.clamp(breakdownLightThreshold.Value, 0f, 1f);
                var m = math.clamp(breakdownMediumThreshold.Value, 0f, 1f);
                var h = math.clamp(breakdownHeavyThreshold.Value, 0f, 1f);
                var light = math.max(l, math.max(m, h));
                var heavy = math.min(h, math.min(l, m));
                var medium = math.clamp(m, heavy, light);
                // A threshold of 0 is treated as disabled (e.g. a mod/trait that zeroes it),
                // so the being can never enter that band — not even at exactly 0 sanity.
                if (heavy > 0f && s <= heavy) { return MentalBreakdownBand.Heavy; }
                if (medium > 0f && s <= medium) { return MentalBreakdownBand.Medium; }
                if (light > 0f && s <= light) { return MentalBreakdownBand.Light; }
                return MentalBreakdownBand.None;
            }
        }

        /// <summary>
        /// Per-hour natural breakdown chance for the current band. The same on every
        /// difficulty: difficulty only moves the resting sanity (Difficulty.SanityBaseline).
        /// </summary>
        public float BreakdownRisk => BreakdownRiskFor(CurrentBreakdownBand);

        /// <summary>
        /// Chance of a natural breakdown within 24 rolled hours at the current band. The roll
        /// is hourly, so this compounds BreakdownRisk; multiplying it by 24 goes past 100%.
        /// </summary>
        public float DailyBreakdownChance => 1f - Mathf.Pow(1f - BreakdownRisk, 24f);

        private static float BreakdownRiskFor(MentalBreakdownBand band) {
            switch (band) {
                case MentalBreakdownBand.Light:
                    return 1f / (Tunables.BreakdownLightMtbDays * 24f);
                case MentalBreakdownBand.Medium:
                    return 1f / (Tunables.BreakdownMediumMtbDays * 24f);
                case MentalBreakdownBand.Heavy:
                    return 1f / (Tunables.BreakdownHeavyMtbDays * 24f);
                default:
                    return 0f;
            }
        }

        public string SanityPctWithColor {
            get {
                var v = Sanity.Value;
                var txt = Units.PercentageColored(v);
                switch (v) {
                    case >= 0.66f:
                        return $"{txt} {GlyphMap.SpriteFaceHappy}";
                    case >= 0.33f:
                        return $"{txt} {GlyphMap.SpriteFaceNormal}";
                    default:
                        return $"{txt} {GlyphMap.SpriteFaceSad}";
                }
            }
        }

        public readonly List<MentalEffect> CurrentEffects = new();
        public readonly List<MentalEffect> PastEffects = new();
        public readonly FunMemory FunMemory = new();
        private float sanityIncreaseRatePerTick;
        private float sanityDropRatePerTick;
        private static Curve cpuObeyCurve;
        private static string lastObeyText;
        private static int lastObeyEff = 0;
        public readonly BCallback EffectsChanged = new();
        public void Tick(long ticks) {
            float sum = 0;
            bool effectsChanged = false;

            ProcessMoodEffects(ticks, ref sum, ref effectsChanged);


            if (ticks % Consts.TicksPer15Minutes == 12) {
                var beingData = Owner.Owner;
                TickObeyChip(beingData);
                TickMoodRegulator(beingData);
                TickOutlook();
            }

            // Compute the target sanity: baseline shifted by active mood effects.
            var target = math.clamp(
                SanityBaseline.Value + sum * Tunables.SanityEffectImpact,
                0f, 1f);
            // Use the property accessor so Initialize() is guaranteed to have run.
            SanityTarget.UpdateBase(target);
            // what the stat makes of it: modifiers a mod or trait may add
            target = math.clamp(SanityTarget.ValueFresh, 0f, 1f);
            var trend = target - sanity.Value;

            if (trend0 == null) {
                trend0 = string.Empty;
                trendUp1 = GlyphMap.SpriteUp1;
                trendUp2 = GlyphMap.SpriteUp2;
                trendUp3 = GlyphMap.SpriteUp3;
                trendDown1 = GlyphMap.SpriteDown1;
                trendDown2 = GlyphMap.SpriteDown2;
                trendDown3 = GlyphMap.SpriteDown3;
                trendSleeping = GlyphMap.SpriteSleeping;
                trendIncapacitated = GlyphMap.SpriteCross;
            }

            bool sleeping = Owner.Owner.HasCondition(ConditionIdH.Sleeping);
            bool incapacitated = !sleeping && Owner.Owner.Body.IsIncapacitated;

            string trendTxt;
            string trendIcon;
            if (sleeping) {
                trendTxt = trendSleeping;
                trendIcon = IconId.CSleep;
            } else if (incapacitated) {
                trendTxt = trendIncapacitated;
                trendIcon = IconId.CCross;
            } else if (trend >= 0.2f) {
                trendTxt = trendUp3;
                trendIcon = IconId.CUp3;
            } else if (trend >= 0.1f) {
                trendTxt = trendUp2;
                trendIcon = IconId.CUp2;
            } else if (trend >= 0.01f) {
                trendTxt = trendUp1;
                trendIcon = IconId.CUp1;
            } else if (trend <= -0.2f) {
                trendTxt = trendDown3;
                trendIcon = IconId.CDown3;
            } else if (trend <= -0.1f) {
                trendTxt = trendDown2;
                trendIcon = IconId.CDown2;
            } else if (trend <= -0.01f) {
                trendTxt = trendDown1;
                trendIcon = IconId.CDown1;
            } else {
                trendTxt = trend0;
                trendIcon = null;
            }
            Trend.Set(trendTxt);
            TrendIcon.Set(trendIcon);

            // Glide Sanity toward the target — frozen while sleeping or incapacitated.
            bool frozen = sleeping || incapacitated;
            if (!frozen) {
                var diff = target - Sanity.Value;
                // Recover faster than deteriorating (difficulty scales the drop rate).
                var rate = diff >= 0f ? sanityIncreaseRatePerTick : sanityDropRatePerTick;
                // Clamp so we never overshoot the target, and enforce the daily cap.
                var step = math.clamp(diff, -rate, rate);
                ShiftSanity(step);
            }

            if (effectsChanged) {
                EffectsChanged.Invoke();
            }

            if (ticks % Consts.TicksPerHour == 7) {
                var being = Owner.Owner.Being;
                if (being != null) {
                    TryStartBreakdown(being);
                }
            }
        }

        // Implants are permanent effects: set on every check while installed, and the same
        // check passes 0 once the implant is gone, which removes the effect.
        private void TickObeyChip(BeingData beingData) {
            int effect = 0;
            if (beingData.Body.IsForcedToObey && beingData.Species.IdH != SpeciesIdH.Mindjar) {
                cpuObeyCurve ??= CurveDef.FastGet(CurveIdH.Beings_CPU_ObeyChipSanityImpact);
                var cpu = A.S.Situation.TotalCPU;
                effect = Mathf.RoundToInt(cpuObeyCurve.Evaluate(cpu));
                if (effect != 0 && (lastObeyEff != effect || lastObeyText == null)) {
                    lastObeyEff = effect;
                    var baseEff = cpuObeyCurve.Evaluate(0);
                    var cpuBonusTxt = $"{T.CpuBonus}: +{effect - baseEff}";
                    lastObeyText = $"{T.BodypartImplantObeychip} {Texts.GreyParentheses(cpuBonusTxt)}";
                }
            }
            SetPermanentEffect(A.S, MoodSourceIdH.ObeyChip, IconId.CChainLink,
                lastObeyText, effect);
        }

        private void TickMoodRegulator(BeingData beingData) {
            SetPermanentEffect(A.S, MoodSourceIdH.MoodRegulator, IconId.CMoodGood,
                T.BodypartImplantMoodregulator, beingData.Body.IsMoodRegulated ? 10 : 0,
                autoLog: false);
        }

        // Optimist / Pessimist are one mood of their own. They used to add one to every other
        // effect, so the trait weighed more the busier a being's day was.
        private const int OutlookMood = 3;

        private void TickOutlook() {
            var traits = Owner.Traits;
            if (traits.HasTrait(TraitIdH.Optimist)) {
                SetPermanentEffect(A.S, MoodSourceIdH.Outlook, IconId.CMoodGood,
                    T.TraitOptimist, OutlookMood, autoLog: false);
            } else if (traits.HasTrait(TraitIdH.Pessimist)) {
                SetPermanentEffect(A.S, MoodSourceIdH.Outlook, IconId.CMoodBad,
                    T.TraitPessimist, -OutlookMood, autoLog: false);
            } else {
                SetPermanentEffect(A.S, MoodSourceIdH.Outlook, null, null, 0);
            }
        }

        [SkipDumpInstance]
        public MentalHealthComp Comp {
            get {
                var b = Owner.Owner.Being;
                if (b?.IsActive != true) {
                    comp = null;
                    return null;
                }
                if (comp?.Being != b) {
                    if (!b.TryGetComponent(out comp)) {
                        D.Err("Being has no MentalHealthComp! {0}", b);
                        return null;
                    }
                }
                return comp;
            }
        }

        public string SanityTabName {
            get {
                var v = Sanity.Value;
                var txt = Units.PercentageColored(v);
                switch (v) {
                    case >= 0.66f:
                        return $"{GlyphMap.SpriteFaceHappy} {T.StatSanity}";
                    case >= 0.33f:
                        return $"{GlyphMap.SpriteFaceNormal} {T.StatSanity}";
                    default:
                        return $"{GlyphMap.SpriteFaceSad} {T.StatSanity}";
                }

            }
        }

        private void ShiftSanity(float sum) {
            // D.Warn("Shifting sanity: {0}->{1}", Trend, sum);
            if (Owner == null) {
                D.Err("PANIC: Mental health has no owner! {0}", this);
            }

            Sanity.TryChange(sum);
        }

        private void ProcessMoodEffects(long ticks, ref float sum, ref bool changed) {
            var bodyId = Owner.Owner.Id;
            for (int i = CurrentEffects.Count - 1; i >= 0; i--) {
                MentalEffect e = CurrentEffects[i];
                e.UpdateProgress(ticks);
                bool ended;
                if (e.IsPermanent) {
                    // Normally the source removes it. These two catch what a source can't see:
                    // the mind moved to another body (transplant, extraction), or the source
                    // stopped checking altogether (a leak).
                    ended = e.BodyId != bodyId || ticks - e.ConfirmTick > PermanentEffectLapseTicks;
                    if (ended) {
                        e.EndTick = ticks;
                    }
                } else {
                    ended = e.EndTick < ticks;
                }
                if (ended) {
                    CurrentEffects.RemoveAt(i);
                    AddToHistory(e);
                    changed = true;
                } else {
                    float change = e.Effect;
                    sum += change;
                }
            }
        }

        public bool TryStartBreakdown(Being being, bool force = false) {
            // No hard cooldown: re-breaks are prevented by the breakdown's own sanity
            // recovery plus the post-breakdown Relief effect (see AfterBreakdown).
            var band = CurrentBreakdownBand;
            if (!force && band == MentalBreakdownBand.None) { return false; }
            var a = being.AIAgent;
            if (a.Goal != null && (a.Goal.IsNonInterruptable || a.Goal.IsForceAssigned)) {
                return false;
            }
            if (a.BeingData.Body.IsIncapacitated) {
                return false;
            }
            if (a.BeingData.HasCondition(ConditionIdH.Sleeping)) {
                return false;
            }
            // A being that is neither crew nor a guest breaks down only at the worst, and never
            // while it runs for the map edge: a fleeing raider broke down, came back aboard and
            // went for the reactor (STA-1753)
            if (!force && !a.BeingData.IsFriendly) {
                if (band != MentalBreakdownBand.Heavy) { return false; }
                var cu = a.CombatUnit;
                if (cu != null && (cu.IsLeaving || cu.IsFleeing)) { return false; }
            }
            var s = being.S;
            // Not scaled by difficulty: harder difficulties already rest
            // colonists closer to the bands (Difficulty.SanityBaseline). Multiplying the roll
            // too made Impossible break down every few days no matter what the player did.
            if (!force && !Rng.UChance(BreakdownRiskFor(band))) {
                return false;
            }
            if (force) {
                // Forced breakdowns ignore band filtering for broad compatibility.
                if (TryAssignBreakdownEvent(being, s, MentalBreakdownBand.None)) {
                    return true;
                }
                D.Err("Failed to start forced breakdown for {0}", being);
                return false;
            }
            // Natural breakdown: the roll already decided a breakdown happens, so make sure
            // *something* fires. Try the rolled band, then de-escalate to lower bands if the
            // story generator can't place an event for the current band (e.g. gated Heavy
            // events on a young colony). Heavy -> Medium -> Light.
            for (var b = (int) band; b >= (int) MentalBreakdownBand.Light; b--) {
                if (TryAssignBreakdownEvent(being, s, (MentalBreakdownBand) b)) {
                    return true;
                }
            }
            // No compatible event in any band (e.g. an immobilized being already in the
            // lowest band whose events can't run); nothing to start this hour.
            return false;
        }

        private bool TryAssignBreakdownEvent(Being being, GameState s,
                MentalBreakdownBand filterBand) {
            var hook = StoryEventHook.CreateFor(being, StoryEventFlags.MentalBreakdown)
                .WithEventFilter((def) => CanAssignBreakdown(def, being, filterBand))
                .Instant();
            var data = s.Query.AddStoryEventHook.Ask(hook);
            return StoryEventData.IsOK(data);
        }

        private bool CanAssignBreakdown(StoryEventDef def, Being being,
                MentalBreakdownBand band) {
            // Natural breakdowns are constrained to events tagged with the current band.
            // band == None means "no band filter" (forced breakdowns).
            if (band != MentalBreakdownBand.None) {
                var mb = def.Event as MentalBreakdownStoryEvent;
                if (mb == null || mb.EventBand != band) {
                    return false;
                }
            }
            var a = being.AIAgent;
            if (!a.BeingData.IsFriendly) {
                var playerOnly = def.Vars.Get(PropertyIdH.PlayerFactionOnly, false);
                if (playerOnly) {
                    return false;
                }
            }
            if (a.Motion.IsImmobilized) {
                var supportsImmobilized = def.Vars.Get(PropertyIdH.SupportsImmobilized, false);
                if (!supportsImmobilized) {
                    return false;
                }
            }
            var reqAct = def.Vars.Get(PropertyIdH.RequiredAIAction, string.Empty);
            if (!string.IsNullOrEmpty(reqAct)) {
                var actIdH = Hashes.S(reqAct);
                if (!a.TryGetAIAction(actIdH, out var action)) {
                    // D.Warn("Skipping breakdown on being that doesn't have AI Action: {0} -> {1} -> {2}",
                    //     def.Id, reqAct, being);
                    return false;
                }
                // Beyond static ability/species gates, some breakdowns need a
                // world-aware check (e.g. a safely reachable target) before they
                // can be assigned to this being right now.
                if (action is AIMentalBreakdownAction mbAction && !mbAction.CanBeAssignedTo(a)) {
                    return false;
                }
            }
            if (def.Vars.Get(PropertyIdH.RequiresPersonalQuarters, false)
                    && !a.S.Sys.Areas.HasPersonalQuarters(a.BeingData)) {
                return false;
            }
            return true;
        }

        public bool HasCurrentEffect(string reason) {
            for (int i = CurrentEffects.Count - 1; i >= 0; i--) {
                MentalEffect e = CurrentEffects[i];
                if (e.Reason == reason) {
                    return true;
                }
            }
            return false;
        }

        public void AddEffect(GameState S, MentalEffect effect,
                float autLogThreshold = 9.9f,
                bool autoLog = true) {
            if (effect.IsPermanent) {
                D.Err("Permanent mood effects are set with SetPermanentEffect, not added! {0}", effect);
                return;
            }
            if (effect.IsAdded) {
                D.Err("Trying to add mental effect that was already added to someone else! Create new instance for each being! {0}", effect);
            }
            effect.IsAdded = true;
            if (Owner.Owner.HasStateFlag(BeingState.Dead)) {
                D.ErrSoft("Trying to add a mood effect for a dead being! {0} -> {1}", effect, Owner.Owner);
                return;
            }
            var found = false;
            // we can still be incapacitated and conscious!
            // if (Owner.Owner.Body.IsIncapacitated) {
            //     D.ErrSoft("Trying to add a mood effect for an incapacitated being! {0} -> {1}",
            //         effect, Owner.Owner);
            //     return;
            // }
            for (int i = CurrentEffects.Count - 1; i >= 0; i--) {
                MentalEffect e = CurrentEffects[i];
                // A permanent effect belongs to its source, a timed one never replaces it
                if (e.IsPermanent) { continue; }
                if (e.Reason == effect.Reason) {
                    // need to update the start tick to know when the condition actually started!
                    // end tick will be fresh already
                    effect.StartTick = e.StartTick;
                    CurrentEffects[i] = effect;
                    found = true;
                    break;
                }
            }
            if (!found) {
                CurrentEffects.Add(effect);
            }
            AnnounceAdded(S, effect, !found, autLogThreshold, autoLog);
        }

        /// <summary>
        /// A day without a confirmation from its source ends a permanent effect. Sources check
        /// every hour at most, so this only fires when a source stopped checking: a leak
        /// backstop, never the normal way a permanent effect ends.
        /// </summary>
        public const long PermanentEffectLapseTicks = Consts.TicksPerDay;

        /// <summary>
        /// Adds or updates in place the permanent effect of a source. A permanent effect has no
        /// end time and shows no countdown: the source calls this on every check while its
        /// cause holds, and with 0 once it doesn't, which removes the effect. Checking both
        /// ways every time means a missed transition can't leave an effect behind.
        /// </summary>
        public void SetPermanentEffect(GameState s, int sourceIdH, string icon, string reason,
                int baseEffect, string tooltip = null, bool autoLog = true) {
            // 0 is what every timed effect has, so it would match and overwrite one of those
            if (sourceIdH == 0) {
                D.Err("Permanent mood effect without a source id: {0}", reason);
                return;
            }
            var ticks = G.Ticks;
            if (baseEffect == 0) {
                RemovePermanentEffect(sourceIdH, ticks);
                return;
            }
            var beingData = Owner.Owner;
            if (beingData.HasStateFlag(BeingState.Dead)) { return; }
            var effect = (sbyte) Mathf.Clamp(baseEffect, -100, 100);
            var pos = beingData.Being?.PosIdx ?? beingData.LastPosition.Idx;
            var existing = PermanentEffect(sourceIdH);
            if (existing != null) {
                existing.ConfirmTick = ticks;
                existing.BodyId = beingData.Id;
                existing.PosIdx = pos;
                if (existing.Effect != effect || existing.Reason != reason
                        || existing.Icon != icon || existing.Tooltip != tooltip) {
                    existing.Effect = effect;
                    existing.Reason = reason;
                    existing.Icon = icon;
                    existing.Tooltip = tooltip;
                    EffectsChanged.Invoke();
                }
                return;
            }
            // Before permanent effects existed, sources kept these alive as timed effects
            // with the same Reason. End such a leftover from an older save, or it would be
            // counted twice until it ran out.
            for (int i = CurrentEffects.Count - 1; i >= 0; i--) {
                var e = CurrentEffects[i];
                if (!e.IsPermanent && e.Reason == reason) {
                    e.EndTick = ticks - 1;
                }
            }
            var added = MentalEffect.CreatePermanent(sourceIdH, beingData.Id, pos, icon,
                ticks, reason, effect);
            added.Tooltip = tooltip;
            added.IsAdded = true;
            CurrentEffects.Add(added);
            AnnounceAdded(s, added, true, 9.9f, autoLog);
        }

        /// <summary>
        /// Ends the permanent effect of a source, if there is one, and moves it to history.
        /// Works for dead beings too, so a source can always clean up after itself.
        /// </summary>
        public void RemovePermanentEffect(int sourceIdH, long ticks) {
            if (sourceIdH == 0) { return; }
            for (int i = CurrentEffects.Count - 1; i >= 0; i--) {
                var e = CurrentEffects[i];
                if (e.SourceId != sourceIdH) { continue; }
                e.EndTick = ticks;
                CurrentEffects.RemoveAt(i);
                AddToHistory(e);
                EffectsChanged.Invoke();
                return;
            }
        }

        public MentalEffect PermanentEffect(int sourceIdH) {
            if (sourceIdH == 0) { return null; }
            for (int i = CurrentEffects.Count - 1; i >= 0; i--) {
                var e = CurrentEffects[i];
                if (e.SourceId == sourceIdH) {
                    return e;
                }
            }
            return null;
        }

        private void AnnounceAdded(GameState S, MentalEffect effect, bool isNew,
                float autLogThreshold, bool autoLog) {
            var being = Owner.Owner.Being;
            if (isNew && autoLog && math.abs(effect.Effect) >= autLogThreshold) {
                var v = Texts.ColoredSignedNum(effect.Effect);
                if (being != null) {
                    var reason = effect.Reason;
                    if (!Pos.IsOOBNonZero(effect.PosIdx) && !Texts.HasLink(reason)) {
                        reason = Texts.LinkTo(reason, effect.PosIdx);
                    }
                    S.Sys.Log.AddLine(being, $"{reason} ({T.MoodShort_V1.F(v)})");
                }
            }
            EffectsChanged.Invoke();
            if (being?.IsActive != true) {
                comp = null;
                return;
                // D.Err("Trying to add mood effect for an inactive being: {0}", Owner.Owner);
            }
            // don't show happy faces in combat
            if (being.AIAgent.CombatUnit.State == CombatUnitState.Unaware) {
                Comp?.AfterAdd(effect, isNew);
            }
        }
        private void AddToHistory(MentalEffect e) {
            AddToHistorySquashAll(e);
        }
        private void AddToHistorySquashAll(MentalEffect e) {
            for (int i = PastEffects.Count - 1; i >= 0; i--) {
                var h = PastEffects[i];
                if (h.Reason == e.Reason) {
                    PastEffects.RemoveAt(i);
                }
            }

            if (PastEffects.Count > EffectHistorySize) {
                PastEffects.RemoveAt(0);
            }
            PastEffects.Add(e);
        }

        /// <summary>
        /// Whether a mind can have a mood is decided by its own species listing the MentalHealth
        /// need, not by the brain or core it sits in - a mind has to carry its mood between
        /// frames, and a brain is just another frame.
        /// </summary>
        public static MentalHealth TryNewFor(Mind mind) {
            if (mind.Species == null || mind.Species.GrantsMentalHealth) {
                var health = new MentalHealth();
                health.Owner = mind;
                return health;
            }
            return null;
        }

        public void AfterBreakdown(long ticks, float minSanity) {
            // D.Warn("Breakdown ended: {0}", Units.TicksToTime(ticks));
            // remove all negative effects, except permanent ones: their cause is still there
            // (still naked, still in pain), and the Relief effect below already lifts the mood
            Owner.Owner.RemoveCondition(The.R.State, ConditionIdH.MentalBreakdown);
            for (int i = CurrentEffects.Count - 1; i >= 0; i--) {
                MentalEffect e = CurrentEffects[i];
                if (e.Effect < 0 && !e.IsPermanent) {
                    e.EndTick = ticks;
                }
                CurrentEffects[i] = e;
            }
            if (Sanity.BaseValue < minSanity) {
                Sanity.UpdateBase(SanityBaseline.Value);
            }
            LastBreakdownTime = ticks;
            // Relief: a temporary mood boost that keeps the
            // being above its thresholds for a while, spreading breaks across the colony
            // and preventing immediate re-breaks.
            var beingData = Owner.Owner;
            if (!beingData.HasStateFlag(BeingState.Dead)) {
                var posIdx = beingData.Being?.PosIdx ?? beingData.LastPosition.Idx;
                AddEffect(A.S, MentalEffect.CreateWithIcon(
                    posIdx, IconId.CMoodGood, ticks,
                    (long)(Tunables.BreakdownReliefDays * Consts.TicksPerDay),
                    T.FeelingRelieved, Tunables.BreakdownReliefMoodBonus));
            }
        }
        public void Initialize() {
            if (Owner.Traits.HasTrait(TraitIdH.Enlightened)) {
                IsEnlightened = true;
            }
            sanity = Owner.Owner.StatsBeing.Sanity;
            sanity.IsUnused = false;
            sanity.CompoundLimit = 1f;

            sanityBaseline = Owner.Owner.StatsBeing.SanityBaseline;
            sanityBaseline.IsUnused = false;
            sanityBaseline.CompoundLimit = 1f;
            if (sanityBaseline.Value == 0f) {
                sanityBaseline.UpdateBase(A.S.Difficulty.SanityBaseline + Rng.URange(-0.02f, 0.02f));
            } else {
                // Baselines are saved per being, so a retuned difficulty would only reach beings
                // generated after it. Lift a saved one into the current generation range. It
                // only ever raises, so running this on every load changes nothing twice.
                var floor = A.S.Difficulty.SanityBaseline - Mind.SanityBaselineSpread;
                if (sanityBaseline.BaseValue < floor) {
                    sanityBaseline.UpdateBase(floor);
                }
            }

            sanityTarget = Owner.Owner.StatsBeing.SanityTarget;
            sanityTarget.IsUnused = false;
            sanityTarget.CompoundLimit = 1f;
            sanityTarget.UpdateBase(sanityBaseline.Value);

            // Breakdown threshold stats. Defaults seed any being that bypassed mental-stat
            // generation/loading; a Sets-to-0 modifier (Enlightened) still wins via .Value.
            breakdownLightThreshold = Owner.Owner.StatsBeing.MentalBreakdownLightThreshold;
            breakdownLightThreshold.IsUnused = false;
            breakdownLightThreshold.CompoundLimit = 1f;
            if (breakdownLightThreshold.Value == 0f) {
                breakdownLightThreshold.UpdateBase(Tunables.BreakdownLightThreshold);
            }
            breakdownMediumThreshold = Owner.Owner.StatsBeing.MentalBreakdownMediumThreshold;
            breakdownMediumThreshold.IsUnused = false;
            breakdownMediumThreshold.CompoundLimit = 1f;
            if (breakdownMediumThreshold.Value == 0f) {
                breakdownMediumThreshold.UpdateBase(Tunables.BreakdownMediumThreshold);
            }
            breakdownHeavyThreshold = Owner.Owner.StatsBeing.MentalBreakdownHeavyThreshold;
            breakdownHeavyThreshold.IsUnused = false;
            breakdownHeavyThreshold.CompoundLimit = 1f;
            if (breakdownHeavyThreshold.Value == 0f) {
                breakdownHeavyThreshold.UpdateBase(Tunables.BreakdownHeavyThreshold);
            }

            // Glide rate toward target. Recovery uses this directly; deterioration is
            // scaled by difficulty.SanityDropRate (higher = falls faster).
            sanityIncreaseRatePerTick = Tunables.SanityRecoveryPerHour / Consts.TicksPerHour;
            sanityDropRatePerTick = Tunables.SanityDropPerHour * A.S.Difficulty.SanityDropRate / Consts.TicksPerHour;
        }

        public void AfterLoad() {
            foreach (var e in CurrentEffects) {
                e.UpdateProgress(G.Ticks);
            }
        }
    }
}
