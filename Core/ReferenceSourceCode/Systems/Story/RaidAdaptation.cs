using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    /// <summary>
    /// Raid points remember the colony's losses, as in the popular colony sim: an adaptation
    /// score in days, saved with the story, sets a multiplier on every raid's points
    /// (CombatRaid.ComputePlan). It starts at 0 (raids at 0.8x), grows over a run without losses
    /// after an opening grace, toward about 1.47x, and drops when crew dies or is downed by
    /// violence, then recovers quickly. Before this only the beings hurt or dead right now made
    /// a raid smaller (RaidThreatWeight), so a mauled colony met the next raid at full strength
    /// as soon as it had healed. Days and rates are the popular colony sim's with its days
    /// doubled, since one of our days is about half of its days of play.
    /// </summary>
    public static class RaidAdaptation {
        private const string K_AdaptDays = "Raid.AdaptDays";
        // Adaptation days bots have cost in the current hostile fight (the bot guard)
        private const string K_BotLoss = "Raid.AdaptBotLoss";
        public const float MinDays = -120f;
        public const float MaxDays = 200f;
        // No growth above 0 before this game day: the opening is gentle by design
        public const float GraceDays = 60f;
        public const int GrowthIntervalTicks = Consts.TicksPerDay / 2;
        // A downing counts when a hostile hit landed this recently (CombatUnit.LastHostileHitTick)
        private const long ViolentDowningTicks = Consts.TicksPer5Minutes;
        public const float BotWeight = 0.5f;

        /// <summary>When set, every read sees these adaptation days. For tests that price
        /// raids with adaptation out of the way (60 days is a 1.0 multiplier).</summary>
        public static float? PinnedDaysForTests;

        private static Curve pointsMulCurve;
        private static Curve growthCurve;
        private static Curve lossDeathCurve;
        private static Curve lossDownedCurve;

        // Downed this tick, priced on the next one: a being downed and killed by the same
        // blow is one death, not a downing and a death
        private static readonly List<BeingData> pendingDowned = new();

        private static void EnsureCurves() {
            pointsMulCurve ??= CurveDef.Get(CurveId.Raid_AdaptationPointsMul);
            growthCurve ??= CurveDef.Get(CurveId.Raid_AdaptationGrowth);
            lossDeathCurve ??= CurveDef.Get(CurveId.Raid_AdaptationLossDeath);
            lossDownedCurve ??= CurveDef.Get(CurveId.Raid_AdaptationLossDowned);
        }

        public static float AdaptDays(StoryContext ctx) {
            if (PinnedDaysForTests.HasValue) { return PinnedDaysForTests.Value; }
            if (ctx.AspectData.TryGetFloat(K_AdaptDays, out var days)) { return days; }
            // A save from before adaptation: where a run without losses would be by now, so
            // old saves stay in step with the point curves, which were calibrated with it
            days = NoLossAdaptDays(ctx.S, GameDays(ctx.S));
            ctx.AspectData.SetFloat(K_AdaptDays, days);
            return days;
        }

        public static void SetAdaptDays(StoryContext ctx, float days) {
            ctx.AspectData.SetFloat(K_AdaptDays, Mathf.Clamp(days, MinDays, MaxDays));
        }

        /// <summary>The multiplier on raid points, eased toward 1 by the difficulty's
        /// AdaptationEffect. 1 without a story (and so without adaptation).</summary>
        public static float PointsMul(GameState S, out float adaptDays) {
            var ctx = S.Query.GetStoryContext.Ask();
            if (ctx == null) {
                adaptDays = 0f;
                return 1f;
            }
            adaptDays = AdaptDays(ctx);
            return PointsMulAt(S, adaptDays);
        }

        public static float PointsMulAt(GameState S, float adaptDays) {
            EnsureCurves();
            return Mathf.Lerp(1f, pointsMulCurve.Evaluate(adaptDays), S.Difficulty.AdaptationEffect);
        }

        private static float GameDays(GameState S) {
            return S.Ticks / (float) Consts.TicksPerDay;
        }

        // A new game starts at 0 and an old save where it would be without losses (AdaptDays
        // bootstraps both). A story generator switched mid-game keeps what the colony earned.
        public static void Initialize(StoryContext ctx) {
            pendingDowned.Clear();
            AdaptDays(ctx);
        }

        public static void Unload() {
            pendingDowned.Clear();
        }

        // Every tick, before anything that skips the raid schedule: losses are counted on every
        // difficulty, and adaptation grows every half day
        public static void Tick(StoryContext ctx) {
            if (pendingDowned.Count > 0) {
                for (int i = 0; i < pendingDowned.Count; i++) {
                    ResolveDowned(ctx, pendingDowned[i]);
                }
                pendingDowned.Clear();
            }
            if (ctx.Ticks % GrowthIntervalTicks == 0) {
                Grow(ctx);
            }
        }

        public static void Grow(StoryContext ctx) {
            var days = AdaptDays(ctx);
            SetAdaptDays(ctx, GrownOnce(ctx.S, days, GameDays(ctx.S)));
        }

        private static float GrownOnce(GameState S, float adaptDays, float gameDays) {
            if (adaptDays >= 0f && gameDays < GraceDays) { return adaptDays; }
            EnsureCurves();
            var growth = 0.5f * growthCurve.Evaluate(adaptDays);
            if (adaptDays > 0f) {
                growth *= S.Difficulty.AdaptationGrowthOverZero;
            }
            return Mathf.Min(adaptDays + growth, MaxDays);
        }

        /// <summary>Where adaptation would be after this many game days without losses.</summary>
        public static float NoLossAdaptDays(GameState S, float gameDays) {
            float days = 0f;
            var steps = Mathf.FloorToInt(gameDays * Consts.TicksPerDay / GrowthIntervalTicks);
            for (int i = 1; i <= steps; i++) {
                days = GrownOnce(S, days, i * GrowthIntervalTicks / (float) Consts.TicksPerDay);
                if (days >= MaxDays) { break; }
            }
            return days;
        }

        // How much a being counts as crew: people 1, bots half, guests, pets and the rest 0
        public static float CrewWeight(BeingData data) {
            if (data == null || !data.IsPlayer || data.Mind == null || data.Mind.IsBlank) {
                return 0f;
            }
            if (data.Mind.Role != ColonyRole.Unassigned) { return 0f; }
            if (!data.Species.IsBiological) { return BotWeight; }
            return data.Species.IsHumanoid ? 1f : 0f;
        }

        private const BeingState NotCrewNow =
            BeingState.Dead | BeingState.Stored | BeingState.InColony;

        // The crew aboard or out on an expedition, weighted like the losses
        private static float CrewPopulation(GameState S, BeingData except) {
            float population = 0f;
            foreach (var b in S.Query.AllBeingData.Ask().Values) {
                if (b == except || (b.StateFlags & NotCrewNow) != BeingState.Uninitialized) {
                    continue;
                }
                population += CrewWeight(b);
            }
            return population;
        }

        public static void OnBeingDied(StoryContext ctx, BeingData data) {
            pendingDowned.Remove(data);
            var weight = CrewWeight(data);
            if (weight <= 0f) { return; }
            EnsureCurves();
            var population = CrewPopulation(ctx.S, data);
            Lose(ctx, lossDeathCurve.Evaluate(population) * weight, weight < 1f, population, data);
        }

        public static void OnBeingIncapacitated(StoryContext ctx, BeingData data) {
            if (CrewWeight(data) <= 0f || !WasHitByViolence(ctx, data)) { return; }
            if (!pendingDowned.Contains(data)) {
                pendingDowned.Add(data);
            }
        }

        private static bool WasHitByViolence(StoryContext ctx, BeingData data) {
            var unit = data.Being?.AIAgent?.CombatUnit;
            if (unit == null || unit.LastHostileHitTick <= 0) { return false; }
            return ctx.S.Ticks - unit.LastHostileHitTick <= ViolentDowningTicks;
        }

        private static void ResolveDowned(StoryContext ctx, BeingData data) {
            // healed, dead or gone in the meantime
            if (!data.Body.IsIncapacitated || data.HasStateFlag(BeingState.Dead)) { return; }
            var weight = CrewWeight(data);
            if (weight <= 0f) { return; }
            EnsureCurves();
            var population = CrewPopulation(ctx.S, null);
            Lose(ctx, lossDownedCurve.Evaluate(population) * weight, weight < 1f, population, data);
        }

        // Bots cost at most one person's death per fight: cheap bots built to die would
        // otherwise keep raids at their floor
        private static void Lose(StoryContext ctx, float loss, bool isBot, float population,
                BeingData data) {
            if (isBot) {
                var spent = ctx.AspectData.GetFloat(K_BotLoss, 0f);
                var guard = lossDeathCurve.Evaluate(population);
                loss = Mathf.Min(loss, Mathf.Max(0f, guard - spent));
                ctx.AspectData.SetFloat(K_BotLoss, spent + loss);
            }
            if (loss <= 0f) { return; }
            var before = AdaptDays(ctx);
            SetAdaptDays(ctx, before - loss);
            #if !PRODUCTION_BUILD
            D.Log("Raid adaptation: {0} cost {1:0.#} days ({2:0.#} -> {3:0.#})",
                data, loss, before, AdaptDays(ctx));
            #endif
        }

        // A hostile fight started or ended: the bot guard starts over
        public static void OnHostileCombatChanged(StoryContext ctx) {
            ctx.AspectData.SetFloat(K_BotLoss, 0f);
        }
    }
}
