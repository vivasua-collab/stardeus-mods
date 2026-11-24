#if !PRODUCTION_BUILD
// #define DEBUG_STRENGTH
// #define DEBUG_RAID
#endif
using System;
using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.BodyParts;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Economy;
using Game.Systems.Items;
using Game.Systems.Naming;
using Game.Systems.Stats;
using Game.Systems.Story;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;


namespace Game.Systems.AI.Combat {
    /// <summary>
    /// Precomputed raid parameters for logging and the raid_report console command.
    /// All fields are populated by ComputePlan() without spawning any beings.
    /// </summary>
    public struct RaidPlan {
        public float BaseNetWorth;
        /// <summary>BaseNetWorth with structures at Breakdown.StructureThreatWeight - the
        /// wealth the curves below are actually evaluated on. See CombatRaid.ThreatNetWorth.</summary>
        public float ThreatNetWorth;
        /// <summary>Every bucket BaseNetWorth is made of, in credits.</summary>
        public NetWorthCalculator.Breakdown Wealth;
        public float WealthPoints;
        public int Friendlies;
        /// <summary>Sum of RaidThreatWeight.Of() over player beings - Friendlies discounted for
        /// injury, being downed, stored/on expedition, and animals. What BeingPoints is
        /// actually computed from; see RaidThreatWeight.</summary>
        public float ThreatWeight;
        /// <summary>Points per friendly at this wealth (Raid/FriendlyPoints.json).</summary>
        public float FriendlyPointsEach;
        public float BeingPoints;
        /// <summary>Sum of StrengthOf over player beings, the same scale raid points are spent on.</summary>
        public float ColonyStrength;
        public float BasePoints;
        public int NumRaids;
        public float EarlyRampFactor;
        /// <summary>Faction.RaidStrengthMul for the rolled faction (1 = no effect).</summary>
        public float FactionMul;
        public float GameTensionMultiplier;
        public float StrengthMul;
        public float RogueCommitmentMul;
        public float SectorDifficulty;
        public float SectorBonus;
        /// <summary>Raid adaptation (RaidAdaptation): its days, and the multiplier they give
        /// the whole budget at this difficulty.</summary>
        public float AdaptDays;
        public float AdaptationMul;
        public float FinalPoints;
        public FactionDef Faction;
        /// <summary>
        /// Raider gear/body tier derived from FinalPoints (0-2, see RaiderGearTier.json).
        /// Higher tiers bias equipment rolls toward higher-CombatValue gear, raise quality,
        /// and raise cybernetics chance - see GenItemFlags.WellArmed/EliteArmed.
        /// </summary>
        public int GearTier;
        /// <summary>
        /// The most one raider may cost (StrengthOf): Raid/MaxRaiderCost.json at FinalPoints,
        /// shared out over MinRaiders and MaxRaiders. The only gate on raider gear, see
        /// CombatRaid.RaiderCostCap.
        /// </summary>
        public float RaiderCostCap;
        /// <summary>Rough estimate: FinalPoints / typical per-raider cost for GearTier.</summary>
        public int EstimatedRaiders;
        /// <summary>Points actually spent building the army; set by CreateArmy(), 0 on a plan
        /// from ComputePlan() alone (e.g. raid_report, which never spawns raiders).</summary>
        public float SpentPoints;

        /// <summary>Typical StrengthOf of one raider at FinalPoints (Raid/TypicalRaiderCost.json),
        /// the divisor behind EstimatedRaiders. Display only; the spend loop never reads it.</summary>
        public float TypicalRaiderCost;

        /// <summary>Set by CreateArmy(), like SpentPoints: raiders generated (accepted or not),
        /// rejected (over the cap, or armor too good for the weapon), and taken over the cap
        /// because no draw fit (see RaiderPicker).</summary>
        public int RaiderDraws;
        public int RaiderRejections;
        public int RaiderIncoherentKits;
        public int RaidersOverCap;

        public void Describe(StringBuilder sb) {
            if (Mathf.Approximately(ThreatNetWorth, BaseNetWorth)) {
                sb.AppendFormat("\n  Wealth ({0:0.#}M) → <b>{1:0.#} pts</b>", BaseNetWorth, WealthPoints);
            } else {
                sb.AppendFormat("\n  Wealth ({0:0.#}M, <b>{1:0.#}M for threat</b> with structures at {2:0.##}x) → <b>{3:0.#} pts</b>",
                    BaseNetWorth, ThreatNetWorth, NetWorthCalculator.Breakdown.StructureThreatWeight,
                    WealthPoints);
            }
            DescribeWealth(sb, Wealth);
            if (Mathf.Approximately(ThreatWeight, Friendlies)) {
                sb.AppendFormat("\n  Friendlies ({0} × {1:0.##}) → <b>{2:0.#} pts</b>",
                    Friendlies, FriendlyPointsEach, BeingPoints);
            } else {
                sb.AppendFormat("\n  Friendlies ({0} → <b>{1:0.#} effective</b> × {2:0.##}) → <b>{3:0.#} pts</b>",
                    Friendlies, ThreatWeight, FriendlyPointsEach, BeingPoints);
            }
            sb.AppendFormat("\n  Base points: <b>{0:0.#}</b>", BasePoints);
            if (FactionMul != 1f) {
                sb.AppendFormat("\n  × Faction ({0}): <b>{1:0.##}</b>", Faction?.Id ?? "none", FactionMul);
            }
            sb.AppendFormat("\n  × Game tension ({0:0.##}x)", GameTensionMultiplier);
            sb.AppendFormat("\n  × Early ramp (raid #{0}): <b>{1:0.##}</b>", NumRaids, EarlyRampFactor);
            if (StrengthMul != 1f && StrengthMul > 0f) {
                sb.AppendFormat("\n  × Strength override: <b>{0:0.##}</b>", StrengthMul);
            }
            if (RogueCommitmentMul != 1f && RogueCommitmentMul > 0f) {
                sb.AppendFormat("\n  × Rogue AI commitment: <b>{0:0.##}</b>",
                    RogueCommitmentMul);
            }
            var sectorPts = SectorBonus * GameTensionMultiplier * EarlyRampFactor;
            if (GameTensionMultiplier != 1f || EarlyRampFactor != 1f) {
                sb.AppendFormat("\n  + Sector bonus (diff {0:0.##}): <b>+{1:0.#} pts</b> ({2:0.#} × tension {3:0.##} × ramp {4:0.##})",
                    SectorDifficulty, sectorPts, SectorBonus, GameTensionMultiplier,
                    EarlyRampFactor);
            } else {
                sb.AppendFormat("\n  + Sector bonus (diff {0:0.##}): <b>+{1:0.#} pts</b>",
                    SectorDifficulty, sectorPts);
            }
            sb.AppendFormat("\n  × Adaptation ({0:0} days): <b>{1:0.##}</b>", AdaptDays, AdaptationMul);
            sb.AppendFormat("\n  <b>Final points: {0:0.#}</b>", FinalPoints);
            sb.AppendFormat("\n  Faction: <b>{0}</b>", Faction?.Id ?? "none");
            sb.AppendFormat("\n  Raider gear tier: <b>{0}</b>", GearTier);
            sb.AppendFormat("\n  Max cost per raider: <b>{0:0.#}</b>", RaiderCostCap);
            if (RaiderDraws > 0) {
                sb.AppendFormat("\n  Raiders drawn: {0}, {1} rejected ({2} for armor past their weapon), {3} taken over the cost cap",
                    RaiderDraws, RaiderRejections, RaiderIncoherentKits, RaidersOverCap);
            }
            sb.AppendFormat("\n  Raider mix: <b>{0:0}% bots</b> (faction AI share), bots of tier {1}",
                (Faction?.AI ?? 0f) * 100f, GearTier);
            sb.AppendFormat("\n  Estimated raiders: <b>~{0}</b> (at ~{1:0.#} pts each)",
                EstimatedRaiders, TypicalRaiderCost);
            if (ColonyStrength > 0f) {
                sb.AppendFormat("\n  Colony combat strength: <b>{0:0.#}</b> (raid is {1:0}% of it)",
                    ColonyStrength, FinalPoints / ColonyStrength * 100f);
            } else {
                sb.Append("\n  Colony combat strength: <b>0</b>");
            }
        }

        /// <summary>
        /// One line per wealth bucket, in millions, indented under a "Wealth" heading. Shared
        /// by raid_report and `economy networth` so both always agree on what was counted.
        /// </summary>
        public static void DescribeWealth(StringBuilder sb, in NetWorthCalculator.Breakdown w) {
            const double M = 1_000_000d;
            sb.AppendFormat("\n    Money: {0:0.##}M", w.Money / M);
            sb.AppendFormat("\n    Materials: {0:0.##}M stored, {1:0.##}M unstored",
                w.MaterialsStored / M, w.MaterialsUnstored / M);
            sb.AppendFormat("\n    Objects: {0:0.##}M stored, {1:0.##}M unstored",
                w.ObjectsStored / M, w.ObjectsUnstored / M);
            sb.AppendFormat("\n    Inventory: {0:0.##}M stored, {1:0.##}M unstored, {2:0.##}M equipped",
                w.InventoryStored / M, w.InventoryUnstored / M, w.EquippedGear / M);
            sb.AppendFormat("\n    Implants: {0:0.##}M", w.BodyParts / M);
            sb.AppendFormat("\n    Structures: {0:0.##}M ({1:0.##}M in materials)",
                w.Structures / M, w.StructuresMats / M);
        }
    }

    public sealed class CombatRaid {
        public int MinRaiders;
        public int MaxRaiders;
        public int NumRaiders;
        /// <summary>Populated by CreateArmy() for analytics tracking via TryStartRaid.</summary>
        public RaidPlan LastPlan;
        public FactionDef Faction;
        public bool ForceHumans;
        public bool ForceFlyingOnly;
        public int PosIdx;
        // above 0: the raid gathers where it arrives until this tick (Army.WithStaging)
        public long StageUntilTick;
        /// <summary>Accumulated via AdjustStrength(). Multiplicative, not overwriting, so a
        /// caller can fold in both a fixed difficulty bump and an event's own strength
        /// multiplier (e.g. BreachCapsuleComp sets a difficulty-based multiplier and its own
        /// RaidStrengthMul); both must land, not just whichever call happens last.</summary>
        private float strengthMul = 1f;
        private readonly Rng rng;
        private readonly GameState S;

        // Curves for the new points-based formula
        private static Curve wealthPointsCurve;
        private static Curve earlyRaidRampCurve;
        private static Curve sectorDifficultyBonusCurve;
        private static Curve rogueCommitmentCurve;
        private static Curve raiderGearTierCurve;
        private static Curve friendlyPointsCurve;
        private static Curve durabilityMulCurve;
        private static Curve durabilityMulRobotCurve;
        private static Curve typicalRaiderCostCurve;
        private static Curve maxRaiderCostCurve;

        /// <summary>Minimum raid points regardless of how weak/small the colony is.</summary>
        private const float MinRaidPoints = 2f;

        /// <summary>
        /// The only cap on a raid, points not bodies (Popular colony sim clamps its threat
        /// points to 10000 the same way and has no headcount cap either). Every raider costs at
        /// least 1 point, so this also bounds the spend loop; with durability-priced raiders
        /// (see StrengthOf) 1000 points is on the order of 60-100 bodies. It exists so that
        /// stacked multipliers (tension x faction x rogue x storm x a Mega event's 4x) can't
        /// produce a number nothing downstream can spend.
        /// </summary>
        private const float MaxRaidPoints = 1000f;

        /// <summary>
        /// When >= 0, the raid spends exactly this many points instead of computing a fresh
        /// budget. Used by story events that pre-distribute one plan's points across multiple
        /// spawned objects. When set, NumRaids is NOT incremented (the spawning story event
        /// counts the raid once up front).
        /// </summary>
        public float PresetPoints = -1f;

        /// <summary>
        /// When >= 0 (and PresetPoints is also set), overrides the gear tier that would
        /// otherwise be derived from PresetPoints alone. Story events that split one plan's
        /// points across several spawned objects (breach capsules, hullpiercers) pass the
        /// parent plan's tier here, because each object's own, smaller point share would
        /// otherwise fall under the tier-1/2 threshold even when the parent plan crossed it,
        /// silently losing the tier the parent plan earned.
        /// </summary>
        public int PresetGearTier = -1;

        /// <summary>
        /// When >= 0 (and PresetPoints is also set), the per-raider cost cap, for the same
        /// reason as PresetGearTier: MultiFactionRaidStoryEvent splits one plan over several
        /// groups, and a group's own smaller share would cap its raiders lower than the plan
        /// they are part of.
        /// </summary>
        public float PresetMaxRaiderCost = -1f;

        /// <summary>
        /// Whether this army advances Situation.NumRaids (and so EarlyRaidRamp). Thematic
        /// encounters the player walks into (derelict crews, a hostile stasis pod) set this
        /// false: they are priced off the same budget but should not burn through the opening
        /// ramp that exists to ease scheduled raids in. Popular colony sim ramps by days
        /// passed, not by fights the player started, for the same reason.
        /// </summary>
        public bool CountsAsRaid = true;

        public CombatRaid(GameState S, int seed) {
            this.S = S;
            if (seed == 0) {
                D.Err("Initializing combat with 0 seed!");
                seed = Rng.UInt;
            }
            rng = new Rng(seed);
        }

        public bool TryStartRaid() {
            return TryStartRaid(out _);
        }

        public Army PrepareArmy() {
            if (PosIdx < 1) {
                D.Err("PosIdx invalid when starting raid! {0}", PosIdx);
                return null;
            }
            if (!A.IsCombatOn) {
                D.Warn("Trying to start a raid when combat is disabled!");
                return null;
            }

            Faction ??= S.Sys.Diplomacy.RandomFactionForRaid(forceHumans: ForceHumans);
            return CreateArmy();
        }

        public bool TryStartRaid(out Army army) {
            army = PrepareArmy();
            army = S.Query.StartRaid.Ask(army);
            if (army != null) {
                PlayStats.TrackRaidStarted(LastPlan.Faction?.Id, NumRaiders,
                    LastPlan.FinalPoints, LastPlan.GearTier, LastPlan.SpentPoints);
            }
            return army != null;
        }

        /// <summary>
        /// Computes all raid parameters without spawning any beings.
        /// Used by CreateArmy() and the story raid_report console command. A preview plan
        /// (RaiderSpecies.CurrentGearTier, for derelict crews and gifts) never marks a pending
        /// calm-before-storm multiplier as consumed.
        /// </summary>
        public RaidPlan ComputePlan(bool preview = false) {
            EnsureCurves();
            Faction ??= S.Sys.Diplomacy.RandomFactionForRaid(forceHumans: ForceHumans);

            var sit = S.Situation;
            var sectorDiff = A.PlayerSector.SpaceEntity.Biome.Difficulty;

            var plan = new RaidPlan();
            plan.BaseNetWorth = sit.BaseNetWorth;
            plan.Wealth = S.Sys.Economy.WealthBreakdown;
            plan.ThreatNetWorth = ThreatNetWorth(sit, plan.Wealth);
            plan.WealthPoints = wealthPointsCurve.Evaluate(plan.ThreatNetWorth);
            plan.Friendlies = sit.Friendlies;
            // Effective headcount: RaidThreatWeight discounts injured/downed/stored/expedition
            // beings and animals, so a mauled or partly-stashed colony isn't priced like a
            // healthy one. Old saves have no ThreatWeight until the first hourly sweep -
            // fall back to raw headcount so a raid fired in that window isn't free.
            plan.ThreatWeight = sit.ThreatWeight > 0f ? sit.ThreatWeight : sit.Friendlies;
            // Per-friendly points grow with wealth (Popular colony sim's PointsPerColonistByWealth
            // shape): headcount multiplies the colony's stage rather than adding a flat
            // constant, so a 60-being late colony is not priced like a 60-being early one.
            plan.FriendlyPointsEach = friendlyPointsCurve.Evaluate(plan.ThreatNetWorth);
            plan.BeingPoints = plan.ThreatWeight * plan.FriendlyPointsEach;
            plan.ColonyStrength = sit.CombatStrength;
            plan.BasePoints = plan.WealthPoints + plan.BeingPoints;

            plan.GameTensionMultiplier = S.Difficulty.WantedTensionMultiplier;
            plan.NumRaids = sit.NumRaids;
            plan.EarlyRampFactor = earlyRaidRampCurve.Evaluate(sit.NumRaids);
            // Fold in any pending "storm after calm" bonus set by RaidStoryAspect.
            // Non-scheduled raids (stasis pods, derelicts, player-opened capsules) always see 1.
            // Mark it consumed so the aspect knows the storm actually landed: a scheduled raid
            // whose event only spawns a carrier and computes its plan later never reaches
            // this line, and its storm must stay pending rather than be silently cleared.
            var storyCtx = S.Query.GetStoryContext.Ask();
            var schedMul = storyCtx?.NextScheduledRaidStrengthMul ?? 1f;
            if (schedMul != 1f && !preview) {
                storyCtx.ScheduledRaidStrengthMulConsumed = true;
            }
            // A faction's own combat culture (Virus/Capitalist run harder, Adventurer softer)
            // is a multiplier like every other term here, not a flat addition to BasePoints -
            // an additive term dwarfs an early-game budget and is noise against a late one.
            plan.FactionMul = Faction?.RaidStrengthMul ?? 1f;
            plan.StrengthMul = strengthMul * schedMul;
            // The galaxy pushes back harder the deeper the player goes down the Rogue AI
            // branch. Applies to every raid path, since they all come through ComputePlan.
            plan.RogueCommitmentMul = rogueCommitmentCurve.Evaluate(
                RogueAIUtils.RogueCommitmentFraction(S));

            plan.SectorDifficulty = sectorDiff;
            plan.SectorBonus = sectorDifficultyBonusCurve.Evaluate(sectorDiff);

            float pts = plan.BasePoints
                * plan.GameTensionMultiplier
                * plan.EarlyRampFactor
                * plan.FactionMul
                * plan.StrengthMul
                * plan.RogueCommitmentMul;
            // Additive after the faction / storm / rogue multipliers on purpose (a hard sector
            // keeps biting once the colony's own term is large), but scaled by game difficulty
            // and by the opening ramp: unscaled by difficulty, a Peaceful colony in a
            // difficulty-1.0 sector got +18 on a ~10-point base; unramped, a day-0 colony in a
            // 0.46 sector got +5.3 on a 2.6-point base, so two thirds of the very first raid
            // was the sector and the ramp only eased a third of it.
            pts += plan.SectorBonus * plan.GameTensionMultiplier * plan.EarlyRampFactor;
            // The colony's losses and its run without them (RaidAdaptation) scale the whole
            // budget, the sector's share included, as in the popular colony sim
            plan.AdaptationMul = RaidAdaptation.PointsMul(S, out plan.AdaptDays);
            pts *= plan.AdaptationMul;
            pts = Mathf.Clamp(pts, MinRaidPoints, MaxRaidPoints);

            plan.FinalPoints = pts;
            plan.Faction = Faction;
            plan.GearTier = Mathf.Clamp(
                Mathf.RoundToInt(raiderGearTierCurve.Evaluate(pts)), 0, 2);
            plan.RaiderCostCap = RaiderCostCap(pts);
            plan.TypicalRaiderCost = Mathf.Max(1f, typicalRaiderCostCurve.Evaluate(pts));
            plan.EstimatedRaiders = Mathf.Max(1,
                Mathf.RoundToInt(pts / plan.TypicalRaiderCost));
            return plan;
        }

        /// <summary>
        /// The most one raider of a raid worth these points may cost, as the popular colony sim
        /// caps it: Raid/MaxRaiderCost.json, and no more than an even share over MinRaiders.
        /// A carrier that caps headcount (MaxRaiders: one raider per storage capsule, two per
        /// hullpiercer) raises it to an even share over MaxRaiders, so its few raiders still
        /// carry the whole plan's strength; the popular colony sim has no headcount caps, so
        /// this rule is ours. The curve's value at 0 is a floor, so MinRaiders can always be
        /// bought with the cheapest raiders.
        /// </summary>
        public float RaiderCostCap(float points) {
            EnsureCurves();
            var (minRaiders, maxRaiders) = Headcount();
            var cap = Mathf.Min(maxRaiderCostCurve.Evaluate(points), points / minRaiders);
            if (maxRaiders > 0) {
                cap = Mathf.Max(cap, points / maxRaiders);
            }
            return Mathf.Max(cap, MinRaiderCostCap);
        }

        private static float MinRaiderCostCap => maxRaiderCostCurve.Evaluate(0f);

        // MinRaiders and MaxRaiders as the spend loop honors them (0 max = no cap)
        private (int min, int max) Headcount() {
            var min = Mathf.Max(1, MinRaiders);
            var max = MaxRaiders;
            if (max > 0 && max < min) {
                max = min + 1;
            }
            return (min, max);
        }

        /// <summary>
        /// Wealth (millions) as the raid model sees it: BaseNetWorth with structures discounted
        /// to Breakdown.StructureThreatWeight. Shared by ComputePlan (strength) and
        /// RaidStoryAspect (cadence) so both read the same number. Derived from
        /// Situation.BaseNetWorth rather than Breakdown.ThreatTotal on purpose: tests, the sweep
        /// harness and console commands pin BaseNetWorth directly to walk a synthetic wealth
        /// ladder, and this keeps that working while still discounting whatever structures the
        /// loaded save actually has.
        /// </summary>
        public static float ThreatNetWorth(Situation sit, in NetWorthCalculator.Breakdown w) {
            var discount = w.Structures * (1d - NetWorthCalculator.Breakdown.StructureThreatWeight);
            return Mathf.Max(0f, sit.BaseNetWorth - (float) (discount / 1_000_000d));
        }

        private Army CreateArmy() {
            var raiders = SpawnRaiders();
            if (raiders == null) { return null; }
            var army = Army.Create(Faction)
                .WithObjective(CombatObjectiveIdH.Raid);
            if (StageUntilTick > 0) {
                army.WithStaging(StageUntilTick);
            }
            for (int i = 0; i < raiders.Count; i++) {
                army.WithUnit(raiders[i]);
            }
            return army;
        }

        /// <summary>
        /// Spawns the whole army, costs it, then stashes the raiders hidden (RaidPayload)
        /// instead of forming an Army. For multi-entry story events that spend one budget up
        /// front and deliver it later through several objects. Returns the raider ids, or null.
        /// </summary>
        public int[] PrepareStashedRaiders() {
            if (!A.IsCombatOn) {
                D.Warn("Trying to prepare a stashed raid when combat is disabled!");
                return null;
            }
            if (PosIdx < 1) {
                // The raiders exist for one call before being hidden; a map-edge spot keeps
                // that brief existence out of the ship, like a MapEdge raid's arrival point.
                PosIdx = Mathf.Max(1, Pos.FromVector(EntityUtils.RandomMapEdgePoint(rng, 0.05f)));
            }
            Faction ??= S.Sys.Diplomacy.RandomFactionForRaid(forceHumans: ForceHumans);
            var raiders = SpawnRaiders();
            if (raiders == null) { return null; }
            // the capsules are on their way: time to arm
            ArmUpNudge.Send(S);
            return RaidPayload.Stash(S, raiders);
        }

        // The situation raider gear is rolled against. Item SpawnRequirements gate on the
        // sector, Days and Friendlies, and raiders ignore all three: their gear is gated by the
        // raid's own points instead, through the per-raider cost cap (RaiderCostCap), as in the
        // popular colony sim. Gated by the sector, a rich colony in a calm sector was raided by
        // Knives and Nail Guns forever; keyed on wealth, a small early raid of a rich colony
        // brought Sniper and Energy Rifles. Every other gate key still holds, and a Rarity of 0
        // or less still keeps craft-only gear (Power Armor) off raiders.
        private Situation RaiderGearSituation() {
            var gear = S.Situation.Copy();
            gear.SectorDifficulty = float.MaxValue;
            gear.Days = int.MaxValue;
            gear.Friendlies = int.MaxValue;
            return gear;
        }

        private List<CombatUnit> SpawnRaiders() {
            if (!A.IsCombatOn) {
                D.Err("Trying to start a raid when combat is disabled!");
                return null;
            }

            if (!ForceHumans && !Faction.CanRaid) {
                D.Err("Trying to start a raid by faction that can't do it: {0}",
                    Faction);
                Faction = S.Sys.Diplomacy.RandomFactionForRaid(forceHumans: ForceHumans);
            }

            // Compute the raid budget via the shared plan (also used by raid_report).
            var plan = ComputePlan();

            // Allow callers to inject a pre-distributed point share instead of a full budget.
            if (PresetPoints >= 0f) {
                plan.FinalPoints = PresetPoints;
                plan.GearTier = PresetGearTier >= 0 ? PresetGearTier : Mathf.Clamp(
                    Mathf.RoundToInt(raiderGearTierCurve.Evaluate(PresetPoints)), 0, 2);
                plan.TypicalRaiderCost = Mathf.Max(1f,
                    typicalRaiderCostCurve.Evaluate(PresetPoints));
                plan.EstimatedRaiders = Mathf.Max(1,
                    Mathf.RoundToInt(PresetPoints / plan.TypicalRaiderCost));
                plan.RaiderCostCap = PresetMaxRaiderCost >= 0f
                    ? Mathf.Max(PresetMaxRaiderCost, MinRaiderCostCap)
                    : RaiderCostCap(PresetPoints);
            }

            var raidPoints = plan.FinalPoints;

            // Raise cybernetics/implant chance for higher gear tiers, on top of whatever the
            // faction already rolls, so richer raids field better bodies as well as better
            // gear. GenBeingBodyVars is a struct, so this doesn't mutate Faction.GenBodyVars.
            var raiderBodyVars = Faction.GenBodyVars;
            if (plan.GearTier >= 2) {
                raiderBodyVars.ChanceBionic = Mathf.Max(raiderBodyVars.ChanceBionic, 0.5f);
                raiderBodyVars.ChanceImplant = Mathf.Max(raiderBodyVars.ChanceImplant, 0.5f);
                raiderBodyVars.ChanceHighQualityParts =
                    Mathf.Max(raiderBodyVars.ChanceHighQualityParts, 0.6f);
            } else if (plan.GearTier >= 1) {
                raiderBodyVars.ChanceBionic = Mathf.Max(raiderBodyVars.ChanceBionic, 0.2f);
                raiderBodyVars.ChanceImplant = Mathf.Max(raiderBodyVars.ChanceImplant, 0.2f);
                raiderBodyVars.ChanceHighQualityParts =
                    Mathf.Max(raiderBodyVars.ChanceHighQualityParts, 0.3f);
            }

            #if DEBUG_RAID
            var dbgSb = new StringBuilder();
            dbgSb.Append("[RAID PLAN]");
            plan.Describe(dbgSb);
            D.Warn(dbgSb.ToString());
            #endif

            // A caller-set MaxRaiders (thematic encounters) is the only headcount limit. With
            // none, the points budget is the cap: MaxRaidPoints bounds it and every raider costs
            // at least 1. EstimatedRaiders is never used as a cap - it is a display estimate,
            // and pre-capping at it once stopped well-armed raids from spending their budget.
            var (minRaiders, maxRaiders) = Headcount();
            MinRaiders = minRaiders;
            MaxRaiders = maxRaiders;
            int effectiveMax = MaxRaiders > 0 ? MaxRaiders : int.MaxValue;

            var raiders = new List<CombatUnit>(Mathf.Max(MinRaiders, plan.EstimatedRaiders));
            var picker = new RaiderPicker(S, rng, Faction, plan.GearTier, raiderBodyVars,
                ForceHumans, ForceFlyingOnly);
            float spentPoints = 0f;

            // Raider gear ignores the sector, Days and Friendlies gates for every roll in the
            // loop, the jet-suit re-roll and RaiderWeapons.EnsureArmed included: the cost cap
            // gates it instead.
            ItemSpawnSituation.Set(RaiderGearSituation());
            try {
                while (raiders.Count < effectiveMax) {
                    // Stop when budget spent, but always honour MinRaiders.
                    if (raiders.Count >= MinRaiders && spentPoints >= raidPoints) {
                        break;
                    }

                    // Every raider fits the cap and the points left, as in the popular colony
                    // sim, so the last raiders of a raid are cheaper ones rather than one that
                    // overshoots the budget. MinRaiders top-ups past the budget roll plain gear
                    // at the cap's floor.
                    var withinBudget = spentPoints < raidPoints;
                    var maxCost = Mathf.Min(plan.RaiderCostCap,
                        Mathf.Max(raidPoints - spentPoints, MinRaiderCostCap));
                    var data = picker.Pick(maxCost, withinBudget);

                    var raider = S.Beings.Create(EntityUtils.CenterOf(PosIdx)
                        + Rng.UInsideUnitCircle() * 0.5f, data);

                    if (ForceFlyingOnly && !raider.Data.HasAbility(AbilityIdH.Fly)) {
                        D.Err("Spawned flying-only raider without ability to fly! {0}", raider);
                    }

                    if (S.Difficulty.Level > 0.5f && data.Species.IdH == SpeciesIdH.Human
                            && rng.Chance(0.05f * S.Difficulty.Level)) {
                        ConditionState.CreateFor(S, ConditionIdH.CrawlerInfected,
                            data, Severity.Irrelevant, true);
                    }

                    var cost = StrengthOf(raider.Data);
                    spentPoints += Mathf.Max(cost, 1f); // guard against zero-cost beings

                    #if DEBUG_RAID
                    D.Warn("[RAID] Spawned #{0}: {1} (cost {2:0.#} of max {3:0.#}, spent {4:0.#}/{5:0.#}) {6}",
                        raiders.Count + 1, raider, cost, maxCost, spentPoints, raidPoints,
                        KitOf(data));
                    #endif

                    raiders.Add(raider.AIAgent.CombatUnit);
                }
            } finally {
                ItemSpawnSituation.Set(null);
            }

            NumRaiders = raiders.Count;
            plan.SpentPoints = spentPoints;
            plan.RaiderDraws = picker.Draws;
            plan.RaiderRejections = picker.Rejections;
            plan.RaiderIncoherentKits = picker.Incoherent;
            plan.RaidersOverCap = picker.OverCap;
            LastPlan = plan;
            // Increment the raid counter AFTER army is built (so ComputePlan sees NumRaids
            // before this raid, matching what raid_report would have shown).
            // Skip when PresetPoints is set: the spawning story event already counted the raid.
            // Thematic encounters (CountsAsRaid = false) never count, see the field.
            if (CountsAsRaid && PresetPoints < 0f) {
                S.Situation.NumRaids++;
            }

            #if DEBUG_RAID
            D.Warn("[RAID] Raiders spawned: {0}, spent {1:0.#}/{2:0.#} pts, cap {3:0.#}, {4} draws, {5} rejected, {6} over the cap",
                raiders.Count, spentPoints, raidPoints, plan.RaiderCostCap, picker.Draws,
                picker.Rejections, picker.OverCap);
            #endif

            return raiders;
        }

        #if DEBUG_RAID
        private static string KitOf(BeingData data) {
            var sb = new StringBuilder();
            if (data.Inventory != null) {
                foreach (var slot in data.Inventory.Equipment.Values) {
                    if (slot.Item != null) {
                        sb.Append(slot.Item.Def.Id).Append(' ');
                    }
                }
            }
            return sb.ToString();
        }
        #endif

        // Not BeingState.IncapableOfCombat: a being away on an expedition (person or robot)
        // still counts toward colony combat strength, because it can be recalled quickly.
        // Dead, stored in stasis and left-the-ship (InColony) beings can't.
        private const BeingState NotDefending =
            BeingState.Dead | BeingState.Stored | BeingState.InColony;

        private static readonly DamageType[] ProtectionDamageTypes = {
            DamageType.Sharp, DamageType.Piercing, DamageType.Blunt, DamageType.Energy,
        };

        /// <summary>
        /// What a being can do times how long it can keep doing it. Offense is species base +
        /// body-part CombatStrengthBonus (mobility parts) + best-attack DPS; durability is a
        /// measured curve over ProtectionOf, the defense the being goes down by. Worn
        /// armor enters only through that protection term - the item's authored
        /// CombatStrengthBonus is deliberately not added, it would price the same armor twice
        /// (it still drives the gear-tier weighting via InventoryItemDef.CombatValue).
        /// </summary>
        private static Curve meleeSkillAccuracy;
        private static Curve shootingSkillAccuracy;

        // How often this being lands an attack, as far as its skill decides: a melee hit is the
        // skill's hit chance alone (melee weapons have no accuracy), a shot the weapon's peak
        // times the skill's factor at the reference distance. Skill moves this from about x0.6
        // to x1.45, so a skill-0 raider and a skill-15 one must not cost the same.
        // From the skill level through the same curves as MeleeAccuracy and ShootingAccuracy,
        // not from those stats: a being without an entity (a raider waiting in a capsule) has
        // its stats unticked, and a price that changes once it spawns breaks raid budgets.
        // Sight and Dexterity are left to the injury factor.
        public static float HitChanceOf(BeingData data, bool melee, float weaponAccuracy) {
            meleeSkillAccuracy ??= CurveDef.FastGet(CurveIdH.Attacks_MeleeSkill_Accuracy);
            shootingSkillAccuracy ??= CurveDef.FastGet(CurveIdH.Attacks_ShootingSkill_Accuracy);
            int level = 0;
            var jobs = data.Mind?.Jobs;
            if (jobs != null && jobs.TryJob(melee ? JobTypeIdH.Melee : JobTypeIdH.Shooting,
                    out var skill) && !skill.Incapable) {
                level = skill.Level;
            }
            if (melee) {
                return StatsBeing.MeleeHitChanceBase + meleeSkillAccuracy.Evaluate(level);
            }
            var mul = 1f + shootingSkillAccuracy.Evaluate(level);
            return Mathf.Min(1f, weaponAccuracy * mul);
        }

        // unspawned: data generated for a raider that has not spawned yet (RaiderPicker prices
        // it against the raid's cost cap first), as opposed to a being whose entity went away
        public static float StrengthOf(BeingData data, bool unspawned = false) {
            if ((data.StateFlags & NotDefending) != BeingState.Uninitialized) {
                return 0f;
            }
            if (data.Body.IsIncapacitated) {
                return 0f;
            }
            if (data.Mind.IsBlank) {
                return 0f;
            }
            float injuryMul = 1f;
            if (data.Body.IsUnhealthy) {
                injuryMul = 1f - (data.Body.InjuryPercent * 4f);
                if (injuryMul < 0.1f) {
                    return 0f;
                }
            }
            var str = data.Species.BaseCombatStrength;
            // not a fighter
            if (data.Mind.Jobs?.CanHaveWeapon() != true) {
                return str;
            }

            float bodyPartStr = 0f;

            var set = Caches.IntSet;
            set.Clear();

            foreach (var slot in data.Body.Slots) {
                if (slot.InstalledDef != null && slot.IsUsableWithParent) {
                    if (set.Add(slot.InstalledDef.IdH)) {
                        var toAdd = slot.InstalledDef?.CombatStrengthBonus ?? 0f;
                        if (toAdd != 0f) {
                            bodyPartStr += toAdd;
                        }
                    }
                }
            }
            if (bodyPartStr > 0f) {
                #if DEBUG_STRENGTH
                D.Warn("CombatStrength bonus from body parts: {0} -> {1}",
                    bodyPartStr, data);
                #endif
                str += bodyPartStr;
            }

            var bestAttack = data.Being?.AIAgent.CombatUnit?.SelectAttackMethod(false);
            if (bestAttack != null) {
                #if DEBUG_STRENGTH
                D.Warn("CombatStrength bonus from best attack: {0} -> {1}",
                    bestAttack.Stats.DPS.Value, data);
                #endif
                // weighted like weapon choice: accuracy and reach count (InventoryItemDef.WeightedDps),
                // and the accuracy is this being's own, so skill, Sight and hands are priced
                str += InventoryItemDef.WeightedDps(bestAttack.Stats.DPS.Value,
                    HitChanceOf(data, bestAttack.IsMelee, bestAttack.Stats.AccuracyMax.Value),
                    !bestAttack.IsMelee, bestAttack.Stats.RangeMax.Value);
            } else if (data.Being == null) {
                // No entity to ask (expedition member, hidden, a raider not spawned yet): the
                // best equipped weapon or natural attack from its authored data, so an armed
                // being away from the ship isn't priced at fist level.
                str += BestAttackWithoutEntity(data);
            }

            var protection = ProtectionOf(data, unspawned);
            var durabilityMul = DurabilityMulOf(data.Species, protection);
            #if DEBUG_STRENGTH
            D.Warn("CombatStrength durability x{0:0.##} (protection {1:0.#}) -> {2}",
                durabilityMul, protection, data);
            #endif
            str *= durabilityMul;

            #if DEBUG_STRENGTH
            D.Warn("CombatStrength final: {0} -> {1}", str, data);
            #endif

            str *= injuryMul;

            return str;
        }

        /// <summary>
        /// The defense a being goes down by: the resistance of its Essential external parts
        /// (weighted by BodyPartSlot.Size, over the four armor damage types) plus its armor stats,
        /// since a hit's severity is its damage over that defense (GetHitSeverityIncrease) and a
        /// being goes down when a vital part does. It is scaled by how much of the hit weight
        /// lands on those parts. Measured by DurabilitySweepTests over every raider body and suit
        /// tier: hits to take a body down follow this almost linearly (within about 12%). The mean
        /// resistance over all parts it replaced missed a Cleaning Bot by 45% (its Body takes
        /// three quarters of the hits) and underpriced heavy bots, whose toughness sits in the
        /// Body. Armor stats are only recomputed while an entity exists, but they are saved with
        /// the BeingData and survive deactivation, so a being away on an expedition or stashed
        /// hidden is priced at the armor it left with rather than at 0. A raider that has not
        /// spawned yet (unspawned) has its gear's modifiers on its stats but never had them
        /// applied, so it reads them fresh like a live being.
        /// </summary>
        // fitted to DurabilitySweepTests: the human armor sets line up with the bots at 0.55
        private const float WeakPointCoverWeight = 0.55f;

        public static float ProtectionOf(BeingData data, bool unspawned = false) {
            var fresh = unspawned || data.Being != null;
            float totalSize = 0f;
            float allRes = 0f;
            float vitalSize = 0f;
            float vitalRes = 0f;
            var types = ProtectionDamageTypes;
            foreach (var slot in data.Body.Slots) {
                if (slot.IsPartRemoved || slot.IsInternal) { continue; }
                var def = slot.InstalledDef;
                if (def == null) { continue; }
                var size = slot.Size;
                if (size <= 0f) { continue; }
                var quality = slot.Quality;
                float res = 0f;
                for (int i = 0; i < types.Length; i++) {
                    res += def.DamageResistanceFor(types[i], quality);
                }
                res /= types.Length;
                totalSize += size;
                allRes += size * res;
                if (slot.IsEssential) {
                    vitalSize += size;
                    vitalRes += size * res;
                }
            }
            if (totalSize <= 0f) {
                return 0f;
            }
            // a body without an external vital part falls back to all its parts
            var protection = vitalSize > 0f ? vitalRes / vitalSize : allRes / totalSize;
            var stats = data.StatsBeing;
            if (stats != null) {
                protection += (ArmorOf(data, stats.ArmorSharp, fresh, unspawned)
                    + ArmorOf(data, stats.ArmorPiercing, fresh, unspawned)
                    + ArmorOf(data, stats.ArmorBlunt, fresh, unspawned)
                    + ArmorOf(data, stats.ArmorEnergy, fresh, unspawned)) / 4f;
            }
            var vitalShare = vitalSize > 0f ? vitalSize / totalSize : RefVitalShare;
            protection *= Mathf.Pow(RefVitalShare / vitalShare, VitalShareExponent);
            // Armor that closes its weak spots takes fewer armor gaps and crits, which the
            // sweep measures as extra durability on top of the armor values (a Power Armor set
            // at 0.8 lasts as long as 1.8x its plain protection)
            if (stats != null) {
                var cover = Mathf.Clamp(fresh
                    ? stats.WeakPointCover.ValueFresh : stats.WeakPointCover.Value, 0f, 0.9f);
                protection /= 1f - WeakPointCoverWeight * cover;
            }
            return protection;
        }

        // What the live branch would read off the best attack's stats, from the attacks'
        // authored data at the quality of what carries them: weapons, and natural attacks (a
        // Carrier fights with nothing else), which a being without an entity has no attack
        // methods registered for. A ranged attack is picked over any melee one, as
        // CombatUnit.SelectAttackMethod does.
        private static float BestAttackWithoutEntity(BeingData data) {
            float melee = 0f;
            float ranged = 0f;
            if (data.Inventory != null) {
                foreach (var item in data.Inventory.Equipment.Values) {
                    var methods = item.Item?.AttackMethods;
                    if (methods == null || item.ObjData == null) { continue; }
                    for (int i = 0; i < methods.Length; i++) {
                        AddAttackScore(data, methods[i], item.ObjData.Quality, ref melee, ref ranged);
                    }
                }
            }
            foreach (var slot in data.Body.Slots) {
                var methods = slot.InstalledDef?.AttackMethods;
                if (methods == null || slot.IsPartRemoved) { continue; }
                for (int i = 0; i < methods.Length; i++) {
                    AddAttackScore(data, methods[i], slot.Quality, ref melee, ref ranged);
                }
            }
            return ranged > 0f ? ranged : melee;
        }

        private static void AddAttackScore(BeingData data, AttackMethodData am, float quality,
                ref float bestMelee, ref float bestRanged) {
            bool melee = am.AttackAction != "Ranged";
            if (data.Mind?.Jobs?.CanDo(melee ? JobTypeIdH.Melee : JobTypeIdH.Shooting) != true) {
                return;
            }
            InventoryItemDef.QualityMuls(quality, out var dpsMul, out var accMul);
            var accuracy = (am.AccuracyMax > 0f ? am.AccuracyMax : 1f) * accMul;
            var score = InventoryItemDef.WeightedDps(am.DPS * dpsMul,
                HitChanceOf(data, melee, accuracy), !melee, am.RangeMax);
            if (melee) {
                bestMelee = Mathf.Max(bestMelee, score);
            } else {
                bestRanged = Mathf.Max(bestRanged, score);
            }
        }

        // A raider that has not spawned yet lacks what its parts give its stats until the spawn
        // registers them (a bot's plating is most of its armor), so they are added here
        private static float ArmorOf(BeingData data, Stat stat, bool fresh, bool unspawned) {
            var armor = fresh ? stat.ValueFresh : stat.Value;
            if (unspawned) {
                foreach (var slot in data.Body.Slots) {
                    armor += slot.PendingStatEffect(stat);
                }
            }
            return armor;
        }

        // An unarmored human's Body and Head take a third of the hits; the exponent is the
        // DurabilitySweepTests fit (a body hit mostly on its vital parts goes down sooner)
        private const float RefVitalShare = 1f / 3f;
        private const float VitalShareExponent = 0.28f;

        /// <summary>
        /// Accumulates (multiplies) into the strength multiplier rather than overwriting it,
        /// so a caller that folds in more than one source of strength adjustment - e.g. a
        /// difficulty-based bump plus an event's own RaidStrengthMul - gets both, not just
        /// whichever call happens last.
        /// </summary>
        public void AdjustStrength(float mul) {
            if (mul > 0f) {
                strengthMul *= mul;
            }
        }

        // Bots and flesh get separate curves: with pain shock a bot outlasts a human of the same
        // protection (system shock comes later), and one curve between them mispriced both
        public static float DurabilityMulOf(Species species, float protection) {
            EnsureCurves();
            var curve = species.IsBiological ? durabilityMulCurve : durabilityMulRobotCurve;
            return curve.Evaluate(protection);
        }

        private static void EnsureCurves() {
            wealthPointsCurve ??= CurveDef.Get(CurveId.Raid_WealthPoints);
            earlyRaidRampCurve ??= CurveDef.Get(CurveId.Raid_EarlyRaidRamp);
            sectorDifficultyBonusCurve ??= CurveDef.Get(CurveId.Raid_SectorDifficultyBonus);
            rogueCommitmentCurve ??= CurveDef.Get(CurveId.Raid_RogueCommitmentMul);
            raiderGearTierCurve ??= CurveDef.Get(CurveId.Raid_RaiderGearTier);
            friendlyPointsCurve ??= CurveDef.Get(CurveId.Raid_FriendlyPoints);
            durabilityMulCurve ??= CurveDef.Get(CurveId.Raid_DurabilityMul);
            durabilityMulRobotCurve ??= CurveDef.Get(CurveId.Raid_DurabilityMulRobot);
            typicalRaiderCostCurve ??= CurveDef.Get(CurveId.Raid_TypicalRaiderCost);
            maxRaiderCostCurve ??= CurveDef.Get(CurveId.Raid_MaxRaiderCost);
        }
    }
}
