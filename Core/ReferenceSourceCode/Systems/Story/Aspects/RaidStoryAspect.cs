using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Mods;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class RaidStoryAspect : StoryAspect {
        [SelfInit]
        private static void Register() {
            Register(AspectId, Factory);
        }

        private static StoryAspect Factory() {
            return new RaidStoryAspect();
        }
        // Threat wealth (millions) -> mean days until the next scheduled raid. Keyed on wealth
        // only: the previous pair of curves keyed on ColonyCombatStrength gave a weak colony the
        // *shortest* gap (0 strength -> 7-10 days vs 8-20 at 50+), so a colony that had just
        // been mauled was raided sooner - the opposite of Popular colony sim's adaptation and a death
        // spiral. Wealth is the signal raids are meant to track (see RaidScaling.md).
        private Curve raidDelayDaysCurve;
        private float raidDelayJitter;
        private float raidStrengthJitter;

        // ── Calm-before-the-storm AspectData keys ─────────────────────────────
        private const string K_SkipCountdown = "Raid.SkipCountdown";
        private const string K_SkipArmed     = "Raid.SkipArmed";
        private const string K_StormMul      = "Raid.StormMul";
        // Set once the first raid has been pulled in, after the first opened capsule or by
        // the fallback below
        private const string K_FirstPulled   = "Raid.FirstPulled";
        // Set when the fallback pulled it in: raid #0 is then FirstRaidFallbackEventId
        private const string K_FirstFallback = "Raid.FirstFallback";
        // Set while the first raid is being held for a repair station, so it is released with
        // a fresh delay once the gate opens
        private const string K_FirstHeld     = "Raid.FirstHeld";
        // Ticks deaths and fights have added to the raid that is currently cooling down
        private const string K_Extended      = "Raid.Extended";
        // Total delay deaths and fights may add to one raid. The one-week guard alone only
        // bounds how far away a raid can be pushed, not how often: a fight adds 1-2 days, so a
        // colony with a skirmish every day would push its raid back faster than time passes
        // and never be raided
        private const int MaxExtendTicksPerRaid = Consts.TicksPerDay * 2;
        // While the first raid waits for a repair station, the Raid flag is kept cooling down
        // at least this far ahead, so it cannot run out between two hourly checks
        private const int FirstRaidHoldTicks = Consts.TicksPerHour * 2;
        // A held first raid comes this long after the gate opens. Arriving right after the
        // station is built would feel like a punishment for building it
        private const float FirstRaidReleaseDaysMin = 0.5f;
        private const float FirstRaidReleaseDaysMax = 1f;

        // The first raid follows the scripted supply capsule: a few hours after the first
        // capsule is opened, another one arrives with a raider inside. All optional; a
        // generator without them (Chaos) keeps the plain cooldown roll.
        private readonly List<StoryEventDef> firstRaidTriggerDefs = new();
        private Curve firstRaidDelayHoursCurve;
        private StoryEventDef firstRaidDef;
        // Opening a capsule is a player order, and a passive player may never give it. The
        // twist stays tied to the opening, so the player learns first that capsules can be
        // good; this fallback raid comes when the first supply capsule has sat unopened for
        // FirstRaidFallbackDays.
        private StoryEventDef firstRaidFallbackAfterDef;
        private StoryEventDef firstRaidFallbackDef;
        private long firstRaidFallbackTicks;

        public const string AspectId = "Raid";
        public override string Id => AspectId;
        private bool needsStartRaid;
        public override void OnTick(StoryContext ctx) {
            // Before the Peaceful check: a raid_test or a story encounter there still prices
            // its raiders with adaptation
            RaidAdaptation.Tick(ctx);
            // Peaceful has no scheduled raids. Checked here rather than in TryStartRaid, so
            // the raid_test console command still works there
            if (!ctx.S.Difficulty.AllowMajorThreats) { return; }
            if (ctx.IsEveryHour) {
                TryPullFirstRaid(ctx);
                if (HoldFirstRaidForRepairStation(ctx)) {
                    return;
                }
                if (ctx.IsCoolingDown(StoryEventFlags.Raid)) {
                    return;
                }
                needsStartRaid = true;
                var schedule = ctx.ScheduleFor(StoryEventOutcome.Raid);
                if (!schedule.IsAllowed) {
                    schedule.ChangeNextTick();
                    // we need other aspects to tick before we start raid
                    return;
                }
            }
            if (needsStartRaid) {
                needsStartRaid = false;
                TryStartRaid(ctx, isScheduled: true);
            }
        }

        // Read from Seen rather than a signal, so a save made in between, or an older save
        // that already opened a capsule, is handled the same way. Only ever pulls the raid
        // earlier: the initial roll stays the latest it can come, for games where no
        // capsule ever arrives.
        private void TryPullFirstRaid(StoryContext ctx) {
            if (ctx.S.Situation.NumRaids > 0) { return; }
            if (ctx.AspectData.GetBool(K_FirstPulled, false)) { return; }
            if (TryFindFirstOpenedCapsule(ctx, out var openedAt)) {
                ctx.AspectData.SetBool(K_FirstPulled, true);
                var hours = ctx.Rng.Curve(firstRaidDelayHoursCurve);
                PullFirstRaidTo(ctx, openedAt + (long) (hours * Consts.TicksPerHour),
                    "the first capsule was opened");
                return;
            }
            if (firstRaidFallbackAfterDef != null && firstRaidFallbackDef != null
                    && ctx.Seen.TryGetValue(firstRaidFallbackAfterDef.IdH, out var arrived)
                    && arrived.StartCount > 0
                    && ctx.Ticks >= arrived.LastStart + firstRaidFallbackTicks) {
                ctx.AspectData.SetBool(K_FirstPulled, true);
                ctx.AspectData.SetBool(K_FirstFallback, true);
                PullFirstRaidTo(ctx, ctx.Ticks, "the first supply capsule was left unopened");
            }
        }

        private bool TryFindFirstOpenedCapsule(StoryContext ctx, out long openedAt) {
            openedAt = long.MaxValue;
            if (firstRaidDelayHoursCurve == null) { return false; }
            for (int i = 0; i < firstRaidTriggerDefs.Count; i++) {
                if (ctx.Seen.TryGetValue(firstRaidTriggerDefs[i].IdH, out var seen)
                        && seen.StartCount > 0 && seen.LastStart < openedAt) {
                    openedAt = seen.LastStart;
                }
            }
            return openedAt != long.MaxValue;
        }

        // Relaxing and below: the first raid waits until the ship has a repair station, up to
        // Difficulty.FirstRaidWaitsForRepairStationDays. A new player was raided before
        // building one, and their downed bot could not be repaired. Keyed on the station, not
        // the tutorial quest: one built before the quest arrives counts, and abandoning the
        // quest does not let the raid in. Station slots exist from construction until
        // destruction, so this is the same test AINeedMaintenance uses.
        public bool WaitsForRepairStation(StoryContext ctx) {
            var days = ctx.S.Difficulty.FirstRaidWaitsForRepairStationDays;
            if (days <= 0f || ctx.S.Situation.NumRaids > 0) { return false; }
            if (ctx.Ticks >= (long) (days * Consts.TicksPerDay)) { return false; }
            return !ctx.S.Sys.Slots.HasFor(SlotDesignationH.BotRepair);
        }

        // Holds the Raid flag itself rather than skipping the scheduled path, so the
        // on-demand raids (capsule hostiles, derelict pirates, stasis pod raiders) wait too:
        // they check the same flag. Moves the cooldown only when the raid would otherwise
        // come, so a player who builds the station in time sees no difference.
        private bool HoldFirstRaidForRepairStation(StoryContext ctx) {
            var wasHeld = ctx.AspectData.GetBool(K_FirstHeld, false);
            if (WaitsForRepairStation(ctx)) {
                var holdUntil = ctx.Ticks + FirstRaidHoldTicks;
                if (!ctx.FlagCooldowns.TryGetValue(StoryEventFlags.Raid, out var until)
                        || until < holdUntil) {
                    if (!wasHeld) {
                        D.Warn("First raid held until a repair station is built");
                        ctx.AspectData.SetBool(K_FirstHeld, true);
                    }
                    ctx.FlagCooldowns[StoryEventFlags.Raid] = holdUntil;
                }
                return true;
            }
            if (!wasHeld) { return false; }
            ctx.AspectData.SetBool(K_FirstHeld, false);
            // A raid that came some other way meanwhile has rolled its own cooldown
            if (ctx.S.Situation.NumRaids > 0) { return false; }
            var days = ctx.Rng.Range(FirstRaidReleaseDaysMin, FirstRaidReleaseDaysMax);
            var due = ctx.Ticks + (long) (days * Consts.TicksPerDay);
            // Only ever later: a fight before the hold began may have pushed it further already
            if (!ctx.FlagCooldowns.TryGetValue(StoryEventFlags.Raid, out var current)
                    || current < due) {
                ctx.FlagCooldowns[StoryEventFlags.Raid] = due;
            }
            // The released raid gets its own extension budget, like any freshly rolled one
            ctx.AspectData.SetInt(K_Extended, 0);
            D.Warn("First raid released, due in {0}",
                Units.TicksToTime(ctx.FlagCooldowns[StoryEventFlags.Raid] - ctx.Ticks));
            return false;
        }

        private static void PullFirstRaidTo(StoryContext ctx, long due, string why) {
            // Written directly: CoolDownFlag sets the cooldown from now, and no cooldown at
            // all means the raid is already due, so there is nothing to pull in
            if (ctx.FlagCooldowns.TryGetValue(StoryEventFlags.Raid, out var until)
                    && due < until) {
                D.Warn("First raid pulled in because {0}: due in {1}",
                    why, Units.TicksToTime(due - ctx.Ticks));
                ctx.FlagCooldowns[StoryEventFlags.Raid] = due;
            }
        }

        /// <summary>
        /// Triggers a raid right now, bypassing cooldown and all point/schedule gates.
        /// Used by the 'story raid_test [eventId]' console command and tests. With
        /// <paramref name="asScheduled"/> false it never skips or consumes the storm; with it
        /// true the raid goes through the same skip/storm/jitter path as a scheduled one.
        /// <paramref name="only"/> restricts the pick to one raid def.
        /// </summary>
        public StoryEventData TriggerRaidNow(StoryContext ctx, bool asScheduled = false,
                StoryEventDef only = null) {
            // The scheduled path only gets here once the Raid flag cooldown has expired, but a
            // manual trigger right after a raid still has it running, and no StoryPickIgnore
            // flag covers it (EventPickerStoryAspect.CanStartNow -> ctx.IsCoolingDown(def)), so
            // every raid def weighed 0 and nothing started. CooldownRaids re-arms it after.
            var hadCooldown = ctx.FlagCooldowns.TryGetValue(StoryEventFlags.Raid, out var cooldown);
            ctx.ClearCooldown(StoryEventFlags.Raid);
            // Use All | Points so the test fires even if the point budget hasn't accumulated yet.
            var started = TryStartRaid(ctx, isScheduled: asScheduled,
                ignore: StoryPickIgnore.All | StoryPickIgnore.Points, only: only);
            if (started == null && !asScheduled) {
                // A manual trigger that started nothing must not move the raid schedule:
                // TryStartRaid re-armed the cooldown from now. A scheduled one keeps that,
                // since a skipped slot advances the cadence on purpose.
                if (hadCooldown) {
                    ctx.FlagCooldowns[StoryEventFlags.Raid] = cooldown;
                } else {
                    ctx.ClearCooldown(StoryEventFlags.Raid);
                }
            }
            return started;
        }

        /// <summary>
        /// Public read-only access for console reporting: raids remaining before next skip slot.
        /// </summary>
        public int RaidsUntilSkip(StoryContext ctx) =>
            ctx.AspectData.GetInt(K_SkipCountdown, 0);

        /// <summary>
        /// Public read-only access for console reporting: whether the upcoming skip slot will
        /// actually suppress a raid.
        /// </summary>
        public bool NextSkipArmed(StoryContext ctx) =>
            ctx.AspectData.GetBool(K_SkipArmed, false);

        /// <summary>
        /// Public read-only access for console reporting: pending "storm" strength multiplier
        /// for the next scheduled raid (1.0 means no bonus active).
        /// </summary>
        public float PendingStormMul(StoryContext ctx) =>
            ctx.AspectData.GetFloat(K_StormMul, 1f);

        /// <summary>
        /// Public read-only preview of the [min, max] delay range (in days) CooldownRaids
        /// would roll from right now, before the random roll - the wealth-keyed mean spread by
        /// RaidDelayJitter, with no side effects. Used by story report/raid_report-style
        /// displays and tests.
        /// </summary>
        public (float from, float to) PreviewDelayRangeDays(StoryContext ctx) {
            var mean = MeanDelayDays(ctx);
            return (mean * (1f - raidDelayJitter), mean * (1f + raidDelayJitter));
        }

        /// <summary>The +/- fraction scheduled raids' strength is jittered by (0 = none).</summary>
        public float StrengthJitter => raidStrengthJitter;

        // A mod-defined Raid aspect config that predates RaidDelayDaysCurve (or still carries
        // the old CombatStrengthRaidDelayDaysCurve_From/_To pair) leaves the curve null;
        // StoryAspect.LoadCurve already logs the D.Err, so degrade to a fixed gap here instead
        // of crashing the first cadence roll.
        private const float FallbackMeanDelayDays = 9f;

        private float MeanDelayDays(StoryContext ctx) {
            if (raidDelayDaysCurve == null) { return FallbackMeanDelayDays; }
            var s = ctx.S;
            var wealth = CombatRaid.ThreatNetWorth(s.Situation, s.Sys.Economy.WealthBreakdown);
            return raidDelayDaysCurve.Evaluate(wealth);
        }

        private float RollDelayDays(StoryContext ctx) {
            var mean = MeanDelayDays(ctx);
            return mean * ctx.Rng.Range(1f - raidDelayJitter, 1f + raidDelayJitter);
        }

        // ── Skip-window helpers ────────────────────────────────────────────────

        private void RollSkipWindow(StoryContext ctx) {
            var min = Mathf.RoundToInt(Def.Vars.Get(PropertyIdH.RaidSkipIntervalMin, 2f));
            var max = Mathf.RoundToInt(Def.Vars.Get(PropertyIdH.RaidSkipIntervalMax, 3f));
            // Range(min, max+1) so both endpoints are reachable.
            var countdown = ctx.Rng.Range(min, max + 1);
            ctx.AspectData.SetInt(K_SkipCountdown, countdown);
            var skipChance = Def.Vars.Get(PropertyIdH.RaidSkipChance, 0.5f);
            ctx.AspectData.SetBool(K_SkipArmed, ctx.Rng.Chance(skipChance));
        }

        private void ArmStorm(StoryContext ctx) {
            var mulMin = Def.Vars.Get(PropertyIdH.RaidStormMulMin, 1.15f);
            var mulMax = Def.Vars.Get(PropertyIdH.RaidStormMulMax, 1.25f);
            // Always set (never compound) so the storm is bounded at 1.25× no matter what.
            ctx.AspectData.SetFloat(K_StormMul, ctx.Rng.Range(mulMin, mulMax));
        }

        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Returns the started event's data, or null when nothing was started
        /// (combat off, a skip fired, no eligible raid def, or the pick failed).</summary>
        private StoryEventData TryStartRaid(StoryContext ctx, bool isScheduled,
                StoryPickIgnore ignore = StoryPickIgnore.Schedule, StoryEventDef only = null) {
            if (!A.IsCombatOn) {
                D.Warn("Skipping periodic raid, combat disabled");
                return null;
            }

            // ── Calm-before-the-storm logic (scheduled raids only) ─────────
            if (isScheduled) {
                var countdown = ctx.AspectData.GetInt(K_SkipCountdown, 0);
                if (countdown <= 0) {
                    // Skip slot reached. Read the armed flag, then predetermine the next window
                    // immediately (so it's persisted even if we early-return below).
                    var armed = ctx.AspectData.GetBool(K_SkipArmed, false);
                    RollSkipWindow(ctx);
                    if (armed) {
                        // Suppress this raid. Arm a storm for the one that follows.
                        ArmStorm(ctx);
                        var stormMul = ctx.AspectData.GetFloat(K_StormMul, 1f);
                        D.Warn("Raid skipped (calm before the storm). Next raid will be {0:0.##}× stronger.",
                            stormMul);
                        CooldownRaids(ctx);
                        return null;
                    }
                    // Not armed — fall through and fire a normal raid this slot.
                } else {
                    ctx.AspectData.SetInt(K_SkipCountdown, countdown - 1);
                }
            }

            // ── Fire the raid ──────────────────────────────────────────────
            // Apply any pending storm bonus for the duration of this synchronous call.
            var pendingStorm = isScheduled ? ctx.AspectData.GetFloat(K_StormMul, 1f) : 1f;
            if (pendingStorm > 1f) {
                D.Warn("Storm raid firing with strength multiplier {0:0.##}×.", pendingStorm);
            }
            // Scheduled raids also get a small strength jitter from the seeded story RNG, so
            // two raids against the same colony aren't the same size to the point. Lives here
            // next to the storm rather than in ComputePlan so raid_report previews the mean and
            // story-authored raids (derelicts, capsules) keep their exact multiplier.
            var jitter = isScheduled && raidStrengthJitter > 0f
                ? ctx.Rng.Range(1f - raidStrengthJitter, 1f + raidStrengthJitter)
                : 1f;
            ctx.NextScheduledRaidStrengthMul = pendingStorm * jitter;
            ctx.ScheduledRaidStrengthMulConsumed = false;

            StoryEventData started = null;
            try {
                var handler = ctx.S.Query.StoryEventHandlerFor.Ask(StoryEventOutcome.Raid);
                // Raid #0 is the scripted twist when the generator names one (a storage
                // capsule with a raider inside), or the fallback raid when the first supply
                // capsule was left unopened. The weighted pick is the fallback when it cannot
                // start. The forced pick skips the picker gates, so the one that matters here
                // (no raids in hyperspace) is checked by hand
                var firstRaid = ctx.AspectData.GetBool(K_FirstFallback, false)
                    ? firstRaidFallbackDef : firstRaidDef;
                if (only == null && isScheduled && firstRaid != null
                        && ctx.S.Situation.NumRaids == 0 && !A.IsInHyperspace) {
                    started = TryStartPick(ctx, handler, ignore, firstRaid, logFailure: false);
                }
                started ??= TryStartPick(ctx, handler, ignore, only, logFailure: true);
            } finally {
                // Always reset - the multiplier must not bleed into unrelated raids.
                var consumed = ctx.ScheduledRaidStrengthMulConsumed;
                ctx.NextScheduledRaidStrengthMul = 1f;
                ctx.ScheduledRaidStrengthMulConsumed = false;
                if (isScheduled && pendingStorm > 1f) {
                    if (consumed) {
                        // Storm landed on a plan - clear it.
                        ctx.AspectData.SetFloat(K_StormMul, 1f);
                    } else {
                        // The picked event only spawned a carrier (stasis pod) or spawned no
                        // CombatRaid at all (clowns, a failed start), so its plan never saw the
                        // storm. Keep it pending for the next scheduled raid instead of
                        // silently wasting the skipped raid.
                        D.Warn("Storm {0:0.##}x not consumed by this raid's event, kept pending.",
                            pendingStorm);
                    }
                }
            }

            CooldownRaids(ctx);
            return started;
        }

        private static StoryEventData TryStartPick(StoryContext ctx, IStoryEventHandler handler,
                StoryPickIgnore ignore, StoryEventDef only, bool logFailure) {
            var potential = Caches.WeightedEvents;
            potential.Clear();
            if (only != null) {
                // Forced def (raid_test <eventId>, tests, the first raid): skip the weighting
                // gates. The sector-difficulty gate alone is a seeded roll with a 10-25% pass
                // chance for some raid defs, so a weight-based pick of one def is not a usable
                // hook. TryPreparePotential still runs the event's own CheckConditions.
                potential.Add(only, 1f);
            } else {
                foreach (var evt in StoryEventDef.Considerable[(int) StoryEventOutcome.Raid]) {
                    var w = handler.GetEventWeight(ctx, evt, ignore);
                    if (w > 0f) {
                        potential.Add(evt, w);
                    }
                }
            }
            if (potential.Count == 0) {
                return null;
            }
            var data = handler.TryStartPotential(ctx, potential, 0f);
            if (data == null) {
                if (logFailure) {
                    D.ErrSoft("Failed to start a scheduled raid. Potential events: {0}",
                        D.DumpCollection(potential));
                }
                return null;
            }
            if (data.State == StoryEventState.Failed) {
                if (logFailure) {
                    D.ErrSoft("Failed to start a raid: {0}", data);
                }
                return null;
            }
            return data;
        }

        private void CooldownRaids(StoryContext ctx) {
            var delay = RollDelayDays(ctx);
            var sched = ctx.ScheduleFor(StoryEventOutcome.Raid);
            var mul = sched.DifficultyMod?.CooldownMul ?? 1f;
            if (mul == 0f) {
                mul = 1f;
            }
            delay *= mul;
            var pace = ModifierStoryPace.Current;
            if (pace <= 0f) { pace = 1f; }
            delay /= pace;
            ctx.CoolDownFlag(StoryEventFlags.Raid,
                (int)(delay * Consts.TicksPerDay));
            // A new raid is cooling down, with its own extension budget
            ctx.AspectData.SetInt(K_Extended, 0);
            var schedule = ctx.ScheduleFor(StoryEventOutcome.Raid);
            if (schedule.IsAllowed) {
                schedule.ChangeNextTick();
            }
        }

        // Extends the Raid flag cooldown, the only gate on the scheduled raid path. This used to
        // extend the Raid schedule's CooldownUntil, which that path never reads (and
        // ChangeNextTick zeroes right before each raid), so deaths and fights never delayed a
        // raid on any generator.
        private void ExtendRaidCooldown(int ticks, string why) {
            var ctx = A.S.Query.GetStoryContext.Ask();
            if (!ctx.FlagCooldowns.TryGetValue(StoryEventFlags.Raid, out var until)) {
                return;
            }
            // A raid held for a repair station is not coming yet, and pushing it back would
            // only spend the budget it gets once it is released
            if (ctx.AspectData.GetBool(K_FirstHeld, false)) {
                return;
            }
            var cooldownTicksNow = until - G.Ticks;
            // Already due: the raid starts at the next hourly check
            if (cooldownTicksNow <= 0) {
                return;
            }
            // Only a raid that is close is pushed back, which also bounds how far a run of
            // losses can push one
            if (cooldownTicksNow > Consts.TicksPerWeek) {
                return;
            }
            var extended = ctx.AspectData.GetInt(K_Extended, 0);
            var room = MaxExtendTicksPerRaid - extended;
            if (room <= 0) {
                return;
            }
            if (ticks > room) {
                ticks = room;
            }
            D.Warn("Extending raid cooldown by {0}: (remaining: {1}) ({2})",
                Units.TicksToTime(ticks),
                Units.TicksToTime(cooldownTicksNow), why);
            ctx.FlagCooldowns[StoryEventFlags.Raid] = until + ticks;
            ctx.AspectData.SetInt(K_Extended, extended + ticks);
        }

        public override void Initialize(StoryContext ctx, bool isLoading) {
            // the aspect instance is shared by every game: a raid that was due on the tick
            // the last game was left started on the next game's first tick, past its cooldown
            needsStartRaid = false;
            ctx.S.Sig.BeingDied.AddListener(OnBeingDeath);
            ctx.S.Sig.BeingIncapacitated.AddListener(OnBeingIncapacitated);
            ctx.S.Sig.CombatChanged.AddListener(OnCombatChange);
            RaidAdaptation.Initialize(ctx);
            LoadCurve(PropertyIdH.RaidDelayDaysCurve, ref raidDelayDaysCurve);
            raidDelayJitter = Mathf.Clamp(Def.Vars.Get(PropertyIdH.RaidDelayJitter, 0.35f), 0f, 0.9f);
            raidStrengthJitter = Mathf.Clamp(Def.Vars.Get(PropertyIdH.RaidStrengthJitter, 0f), 0f, 0.9f);
            LoadFirstRaidConfig();
            if (!isLoading) {
                var delay = RollDelayDays(ctx);
                delay += Def.Vars.Get(PropertyIdH.InitialExtraDelayDays, 0f);
                // D.Warn("Combat strength: {0}. Cooling down raids for {1} days", str, delay);
                ctx.CoolDownFlag(StoryEventFlags.Raid,
                    (int) (delay * Consts.TicksPerDay));
                ctx.AspectData.SetInt(K_Extended, 0);
                // Seed the first calm-before-the-storm skip window for new games.
                RollSkipWindow(ctx);
            } else if (!ctx.AspectData.TryGetInt(K_SkipCountdown, out _)) {
                // Old save without skip state — bootstrap lazily using the seeded RNG.
                RollSkipWindow(ctx);
            }
        }

        // Aspect instances are shared by every game in a session, so this is rebuilt on every
        // Initialize rather than cached once
        private void LoadFirstRaidConfig() {
            firstRaidTriggerDefs.Clear();
            firstRaidDef = null;
            LoadOptionalCurve(PropertyIdH.FirstRaidDelayHoursCurve, ref firstRaidDelayHoursCurve);
            var flagName = Def.Vars.Get(PropertyIdH.FirstRaidAfterEventFlag, string.Empty);
            if (!string.IsNullOrEmpty(flagName)) {
                if (Enum.TryParse(flagName, out StoryEventFlags flag) && flag != StoryEventFlags.None) {
                    foreach (var def in StoryEventDef.All.Values) {
                        if ((def.Flags & flag) == flag) {
                            firstRaidTriggerDefs.Add(def);
                        }
                    }
                } else {
                    D.Err("Could not parse FirstRaidAfterEventFlag {0} in {1}", flagName, Def);
                }
            }
            var firstRaidId = Def.Vars.Get(PropertyIdH.FirstRaidEventId, string.Empty);
            if (!string.IsNullOrEmpty(firstRaidId)) {
                firstRaidDef = StoryEventDef.Get(firstRaidId);
            }
            firstRaidFallbackAfterDef = null;
            firstRaidFallbackDef = null;
            var fallbackAfterId = Def.Vars.Get(PropertyIdH.FirstRaidFallbackAfterEventId,
                string.Empty);
            if (!string.IsNullOrEmpty(fallbackAfterId)) {
                firstRaidFallbackAfterDef = StoryEventDef.Get(fallbackAfterId);
            }
            var fallbackId = Def.Vars.Get(PropertyIdH.FirstRaidFallbackEventId, string.Empty);
            if (!string.IsNullOrEmpty(fallbackId)) {
                firstRaidFallbackDef = StoryEventDef.Get(fallbackId);
            }
            firstRaidFallbackTicks = (long) (Def.Vars.Get(PropertyIdH.FirstRaidFallbackDays, 2f)
                * Consts.TicksPerDay);
        }

        private void OnCombatChange(bool isOngoing, CombatFlags flags) {
            if (flags == CombatFlags.Friendly) {
                return;
            }
            var ctx = A.S.Query.GetStoryContext.Ask();
            if (ctx != null) {
                RaidAdaptation.OnHostileCombatChanged(ctx);
            }
            if (!isOngoing || Rng.UChance(0.5f)) {
                ExtendRaidCooldown(Consts.TicksPerDay, "Combat");
            }
        }

        private void OnBeingIncapacitated(BeingData data) {
            var ctx = A.S.Query.GetStoryContext.Ask();
            if (ctx != null) {
                RaidAdaptation.OnBeingIncapacitated(ctx, data);
            }
        }

        private void OnBeingDeath(BeingData data) {
            var ctx = A.S.Query.GetStoryContext.Ask();
            if (ctx != null) {
                RaidAdaptation.OnBeingDied(ctx, data);
            }
            if (!data.IsFriendly) { return; }
            // TODO make it more sophisticated / dehardcode it
            float delayDays;
            if (data.Species.IsHumanoid) {
                delayDays = 1.5f;
            } else if (!data.Species.IsBiological) {
                delayDays = 2f;
            } else { // animal, etc
                delayDays = 0.25f;
            }
            var delay = (int) (Consts.TicksPerDay * delayDays
                / Mathf.Clamp(A.S.Difficulty.Level, 0.5f, 3.0f));
            // Hard cap: never extend raids by more than 2 days regardless of difficulty
            delay = Mathf.Min(delay, Consts.TicksPerDay * 2);
            ExtendRaidCooldown(delay, $"Death: {data}");
        }

        public override void Unload(StoryContext ctx) {
            ctx.S?.Sig?.BeingDied.RemoveListener(OnBeingDeath);
            ctx.S?.Sig?.BeingIncapacitated.RemoveListener(OnBeingIncapacitated);
            ctx.S?.Sig?.CombatChanged.RemoveListener(OnCombatChange);
            RaidAdaptation.Unload();
        }
    }
}
