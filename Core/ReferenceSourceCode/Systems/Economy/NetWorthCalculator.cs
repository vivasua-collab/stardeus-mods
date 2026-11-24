using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Systems.Ships;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Economy {
    /// <summary>
    /// Owns the whole wealth sweep behind Situation.BaseNetWorth / NetWorth: money, materials,
    /// loose and stored objects, equipped gear, installed body parts and finished structures.
    /// Every term is kept as its own bucket so `economy networth` and `story raid_report` can
    /// show where the wealth actually is instead of printing a remainder computed by
    /// subtraction. Structures/gear/implants are counted because otherwise converting materials
    /// into a fortified, armed, cybernetically-enhanced colony *lowers* measured wealth instead
    /// of raising it, which is backwards for anything (raids, story pacing) that scales
    /// challenge off wealth. Kept out of EconomySys, which is already large.
    /// </summary>
    public sealed class NetWorthCalculator {
        /// <summary>All buckets at base (non market-adjusted) prices, in credits.</summary>
        public struct Breakdown {
            public double Money;
            /// <summary>Materials inside storage devices.</summary>
            public double MaterialsStored;
            /// <summary>UnstoredMatComp piles: on the floor or carried by a being.</summary>
            public double MaterialsUnstored;
            /// <summary>Sellable non-equipment objects (body parts, misc) in object storage.</summary>
            public double ObjectsStored;
            /// <summary>Sellable non-equipment objects lying around.</summary>
            public double ObjectsUnstored;
            /// <summary>Equipment items (weapons, clothing, armor) in object storage.</summary>
            public double InventoryStored;
            /// <summary>Equipment items lying around.</summary>
            public double InventoryUnstored;
            /// <summary>Equipment worn by player beings.</summary>
            public double EquippedGear;
            /// <summary>Body parts installed in player beings.</summary>
            public double BodyParts;
            /// <summary>Finished structures on the player's island, valued by WorthOfStructure
            /// (materials x tier multiplier + ship class points, x Def.WealthValueMul).</summary>
            public double Structures;
            /// <summary>The materials-only part of Structures, so the valuation's effect is
            /// visible in `economy networth` / raid_report without a debugger.</summary>
            public double StructuresMats;
            /// <summary>
            /// Money + materials + objects at market-adjusted prices. Only Situation.NetWorth
            /// (the player-facing, stock-market-sensitive figure) uses this; structures, gear
            /// and implants aren't liquid stock the player is trading, so market volatility is
            /// not a meaningful signal for them and they're added at base price to both totals.
            /// </summary>
            public double LiquidAdjusted;
            /// <summary>Total material stack count, for Situation.Mats.</summary>
            public int MatCount;
            /// <summary>
            /// Finished-constructable + being visit count from the last Compute(), for the
            /// `economy networth` sweep-cost readout and the counter-asserting test.
            /// </summary>
            public int ComponentsVisited;

            public double Materials => MaterialsStored + MaterialsUnstored;
            public double Objects => ObjectsStored + ObjectsUnstored;
            public double Inventory => InventoryStored + InventoryUnstored;
            /// <summary>
            /// The pre-widening BaseNetWorth: money + materials + loose/stored sellable objects.
            /// Kept so downstream curves tuned against the old, narrower number (story points,
            /// quest rewards, gifts) can be rescaled against a measured ratio instead of guessed.
            /// </summary>
            public double Liquid => Money + Materials + Objects + Inventory;
            public double Total => Liquid + EquippedGear + BodyParts + Structures;

            /// <summary>
            /// How much of a structure's value counts toward ThreatTotal. Popular colony sim's storyteller
            /// wealth counts buildings at half so that turning a stockpile into infrastructure
            /// is rewarded over hoarding the same value loose; the full-price Total is what the
            /// player sees and what story pacing / quest rewards are tuned against. Tunable
            /// (Config/Tunable/Economy.json), held at 1.0 until the structure valuation is
            /// calibrated - see the comment there.
            /// </summary>
            public static double StructureThreatWeight => Tunables.StructureThreatWeight;
            /// <summary>
            /// Wealth as the raid model sees it: Total with structures at StructureThreatWeight.
            /// Feeds raid strength (CombatRaid.ComputePlan) and cadence (RaidStoryAspect) only;
            /// everything else keeps reading Total / Situation.BaseNetWorth.
            /// </summary>
            public double ThreatTotal => Liquid + EquippedGear + BodyParts
                + Structures * StructureThreatWeight;
        }

        private struct StructureWorth {
            public double Mats;
            public double Total;
        }

        // Def.IdH -> worth of that def as a finished structure (and its materials-only part),
        // i.e. MarketItem.Value at load time. Safe to cache for the life of the process only
        // because EconomySys.ValidateMarketItems settles every MarketItem.Value before the first
        // UpdateNetWorth() call; if that call ever moves earlier, this cache would need
        // invalidating too.
        private readonly Dictionary<int, StructureWorth> structureWorthByDef = new();
        // Def.IdH -> ShipDeviceDef.ClassPoints for ship-combat devices; built on first use, after
        // ShipDeviceDef.LinkDefs has resolved each device's tile Def (CoreLoaded1).
        private Dictionary<int, int> classPointsByDef;
        private static Curve tierValueMulCurve;
        private readonly Dictionary<MatType, int> storedMats = new();
        private readonly Dictionary<MatType, int> unstoredMats = new();

        public Breakdown Compute(GameState S, EconomySys economy) {
            var result = new Breakdown();
            result.Money = S.Wallet.Amount;
            result.LiquidAdjusted = result.Money;
            SumMaterials(S, economy, ref result);
            SumObjects(S, economy, ref result);
            result.Structures = SumStructures(S, economy, out result.StructuresMats,
                out var structuresVisited);
            SumBeingGearAndParts(S, economy, out result.EquippedGear, out result.BodyParts,
                out var beingsVisited);
            result.ComponentsVisited = structuresVisited + beingsVisited;
            return result;
        }

        private void SumMaterials(GameState S, EconomySys economy, ref Breakdown result) {
            S.Sys.Inventory.LoadRemainingMaterials(storedMats, unstoredMats);
            result.MaterialsStored = SumMats(storedMats, economy, ref result);
            result.MaterialsUnstored = SumMats(unstoredMats, economy, ref result);
        }

        private static double SumMats(Dictionary<MatType, int> mats, EconomySys economy,
                ref Breakdown result) {
            double total = 0d;
            foreach (var m in mats) {
                if (m.Key.IgnoreMarket) { continue; }
                total += economy.WorthOf(m.Key.Def, out _, false) * m.Value;
                result.LiquidAdjusted += economy.WorthOf(m.Key.Def, out _, true) * m.Value;
                result.MatCount += m.Value;
            }
            return total;
        }

        // Equipped objects are counted per player being below (an enemy's rifle isn't the
        // colony's wealth); InDevice objects are transient and the device may consume them.
        private static void SumObjects(GameState S, EconomySys economy, ref Breakdown result) {
            var objs = S.Query.GetAllObjs.Ask();
            foreach (var o in objs.Values) {
                // mats are handled separately
                if (!o.CanSell) { continue; }
                var worth = economy.WorthOf(o.Def, out _, false);
                result.LiquidAdjusted += economy.WorthOf(o.Def, out _, true);
                var isStored = o.State == ObjState.Stored;
                if (o.Item != null) {
                    if (isStored) {
                        result.InventoryStored += worth;
                    } else {
                        result.InventoryUnstored += worth;
                    }
                } else {
                    if (isStored) {
                        result.ObjectsStored += worth;
                    } else {
                        result.ObjectsUnstored += worth;
                    }
                }
            }
        }

        /// <summary>
        /// Only the player's own island counts, so a docked-but-unclaimed derelict doesn't
        /// inflate wealth (and trigger raids) before the player has actually taken it. Once
        /// connected it becomes part of the ship's island and starts counting normally.
        /// </summary>
        private double SumStructures(GameState S, EconomySys economy, out double matsTotal,
                out int visited) {
            visited = 0;
            matsTotal = 0d;
            var comps = S.Components.AllByType<ConstructableComp>();
            if (comps == null) { return 0d; }

            var areas = S.Sys.Areas;
            var playerIslandId = -1;
            if (areas.IslandCount > 1) {
                playerIslandId = areas.TryGetShipIsland(out var shipIsland) ? shipIsland : -1;
            }

            double total = 0d;
            foreach (var c in comps) {
                if (!(c is ConstructableComp con) || !con.Entity.IsActive || !con.IsFinished) {
                    continue;
                }
                if (playerIslandId != -1 && areas.IslandAt(con.Tile.PosIdx) != playerIslandId) {
                    continue;
                }
                visited++;
                var worth = WorthOfConstructedDef(con.Tile.Definition, economy);
                total += worth.Total;
                matsTotal += worth.Mats;
            }
            return total;
        }

        private StructureWorth WorthOfConstructedDef(Def def, EconomySys economy) {
            if (structureWorthByDef.TryGetValue(def.IdH, out var cached)) {
                return cached;
            }
            var worth = new StructureWorth();
            worth.Total = WorthOfStructure(def, economy, out worth.Mats);
            structureWorthByDef[def.IdH] = worth;
            return worth;
        }

        /// <summary>
        /// What a finished structure of this def is worth, uncached. Materials are a floor on
        /// a building's worth, not a measure of it (a shield emitter that decides a ship battle
        /// came to two rifles' worth of parts), so on top of the materials:
        /// - a multiplier by Def.Tier (the research tier the def sits behind, inherited from its
        ///   research node at load and already kept monotonic in cost by the research design
        ///   rules) - hull at tier 0-1 stays at 1.0x on purpose;
        /// - credits per ship class point for ship-combat devices, the authored measure of what
        ///   a device is worth to the ship's combat budget. That prices what a device costs to
        ///   field, not what it does, so it stays on the right side of the raid model's
        ///   no-combat-feedback rule (RaidScaling.md);
        /// - Def.WealthValueMul, the per-def override, as a multiplier so retuning materials or
        ///   tiers still flows through.
        /// Popular colony sim's equivalent is ingredients + a tiny work term + hand-authored overrides;
        /// WorkAmount was rejected here because it would raise hull the most.
        /// </summary>
        public double WorthOfStructure(Def def, EconomySys economy, out double matsWorth) {
            matsWorth = 0d;
            var mats = def.ConstructedMats;
            if (mats != null) {
                foreach (var mat in mats) {
                    matsWorth += economy.WorthOf(mat, false);
                }
            }
            tierValueMulCurve ??= CurveDef.Get(CurveId.Economy_StructureTierValueMul);
            var worth = matsWorth * tierValueMulCurve.Evaluate(def.Tier);
            worth += ClassPointsOf(def) * (double) Tunables.StructureClassPointValue;
            return worth * def.WealthValueMul;
        }

        private int ClassPointsOf(Def def) {
            if (classPointsByDef == null) {
                classPointsByDef = new Dictionary<int, int>();
                foreach (var device in ShipDeviceDef.All.Values) {
                    if (device.Def == null) { continue; }
                    classPointsByDef[device.Def.IdH] = device.ClassPoints;
                }
            }
            return classPointsByDef.TryGetValue(def.IdH, out var cp) ? cp : 0;
        }

        private void SumBeingGearAndParts(GameState S, EconomySys economy,
                out double gearWorth, out double bodyPartWorth, out int visited) {
            gearWorth = 0d;
            bodyPartWorth = 0d;
            visited = 0;
            var beings = S.Query.AllBeingData.Ask();
            foreach (var b in beings.Values) {
                // A being can be mid-setup with no Mind assigned yet; EffectiveFaction logs a
                // D.Err in that case, which an hourly sweep must not trigger routinely.
                if (b.Mind == null || !b.EffectiveFaction.IsPlayer) { continue; }
                visited++;
                if (b.Inventory != null) {
                    foreach (var slot in b.Inventory.Equipment.Values) {
                        if (slot.ObjData == null) { continue; }
                        gearWorth += economy.WorthOf(slot.ObjData.Def, out _, false);
                    }
                }
                if (b.Body?.Slots != null) {
                    foreach (var slot in b.Body.Slots) {
                        // Same usability check CombatRaid.StrengthOf uses, so wealth and
                        // combat strength agree on which installed parts actually count.
                        if (slot.InstalledDef == null || !slot.IsUsableWithParent) { continue; }
                        bodyPartWorth += economy.WorthOf(slot.InstalledDef.Def, out _, false);
                    }
                }
            }
        }
    }
}
