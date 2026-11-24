using System;
using Game.Systems.AI.Combat;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Naming;
using Game.Systems.Ships;
using Game.Systems.Trade;
using Game.Utils;
using HarmonyLib;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Diplomacy {
    /// <summary>
    /// Responsible for creating gift packages that are offered as gifts to
    /// NPC ships, or vice versa.
    ///
    /// Algorithm:
    /// 1. the richer you are, the bigger the packages should get
    /// 2. we determine the $ cost of the package
    /// 3. we pick random shit and subtract its cost from the package cost
    /// 4. repeat until we added enough shit to make the package cost match
    /// 5. choosing items should have some special logic, for expensive packages
    ///    we want to pick items that can be valuable, not garbage.
    ///    ex: a bot, a colonist, a pet, an engine, matter reactor, solar panel,
    ///    battery,
    /// </summary>
    public sealed class GiftGen {
        private delegate bool ItemGenerator(Rng rng, out GiftItem item);
        private readonly GameState S;
        private readonly NPCShip ship;
        private readonly Curve attitudeMulCurve;
        private static Curve _matCountValueCurve;
        private static Curve matCountValueCurve => _matCountValueCurve ??= Tunables.GiftMatValueOwnershipMul;
        private readonly HashSet<GiftItemType> excludedTypes = new();
        private readonly HashSet<object> excludedObjs = new();
        private GiftPackage currentPackage;
        private readonly GiftMode mode;
        private readonly GiftDirection direction;
        private double topPackageValue;
        public GiftGen(GameState s, NPCShip ship, GiftMode mode, GiftDirection direction) {
            S = s;
            this.ship = ship;
            this.mode = mode;
            this.direction = direction;
            attitudeMulCurve = Tunables.AttitudeGiftChanceMul;
            if (direction != GiftDirection.PlayerToNPC) {
                GiftTypeWeights_Top.Remove(GiftItemType.Tile);
                GiftTypeWeights_Mid.Remove(GiftItemType.Tile);
                GiftTypeWeights_Mid.Remove(GiftItemType.Being);
                if (s.Situation.ColonySizeDiff < 1) {
                    GiftTypeWeights_Top.Remove(GiftItemType.Being);
                }
            }
        }

        private readonly Dictionary<GiftItemType, float> GiftTypeWeights_Top = new() {
            { GiftItemType.Being, 2f },
            { GiftItemType.Money, 0.5f },
            { GiftItemType.Tile, 2f },
            { GiftItemType.Mat, 0.9f },
            { GiftItemType.Obj, 1f },
            { GiftItemType.Fuel, 0.1f },
        };
        private readonly Dictionary<GiftItemType, float> GiftTypeWeights_Mid = new() {
            { GiftItemType.Being, 0.05f },
            { GiftItemType.Money, 1f },
            { GiftItemType.Tile, 0.15f },
            { GiftItemType.Mat, 1.5f },
            { GiftItemType.Obj, 1f },
            { GiftItemType.Fuel, 0.1f },
        };
        private readonly Dictionary<GiftItemType, float> GiftTypeWeights_Low = new() {
            { GiftItemType.Money, 0.5f },
            { GiftItemType.Mat, 2.0f },
            { GiftItemType.Obj, 1f },
            { GiftItemType.Fuel, 1f },
        };

        public GiftPackage CreateNoGift() {
            var pack = new GiftPackage(direction, mode, ship, -1);
            pack.IsCancel = true;
            return pack;
        }

        public GiftPackage Create(int number, int total) {
            switch (direction) {
                case GiftDirection.PlayerToNPC: {
                    return Create_P2N(number, total);
                }
                case GiftDirection.NPCToPlayer: {
                    return Create_N2P(number, total);
                }
                default: {
                    D.Err("GiftPackager direction not implemented: {0}", direction);
                    return null;
                }
            }
        }
        private GiftPackage Create_N2P(int number, int total) {
            var rng = ship.SeededRng(Hashes.Quick(Primes.Prime006, number,
                (int) (G.Ticks / Consts.TicksPerWeek)));
            var pack = CreatePackage(number);

            // cleanup excluded objects or we won't be able to generate packages
            // correctly
            excludedObjs.Clear();

            var nw = ship.SO.Trader.NetWorth(S);
            if (topPackageValue == 0) {
                switch (mode) {
                    case GiftMode.Fuel: {
                        var cellValue = S.Market.ValueOf(
                            The.Defs.FastGet(DefIdsH.ObjMaterialsFusionCell));
                        topPackageValue = rng.Range(3, 6) * cellValue;
                        break;
                    }
                    case GiftMode.SurrenderRansom: {
                        topPackageValue = nw * rng.Range(0.3f, 0.75f);
                        if (topPackageValue < 10000f) {
                            topPackageValue = 10000f;
                        }
                        break;
                    }
                    case GiftMode.Extortion: {
                        topPackageValue = nw * rng.Range(0.25f, 0.5f);
                        if (topPackageValue < 7500f) {
                            topPackageValue = 7500f;
                        }
                        break;
                    }
                    case GiftMode.Gift: {
                        topPackageValue = nw * rng.Range(0.15f, 0.3f);
                        if (topPackageValue < 5000f) {
                            topPackageValue = 5000f;
                        } else if (topPackageValue > 1_000_000f) {
                            topPackageValue = 1_000_000f;
                        }
                        break;
                    }
                    default: {
                        D.Err("Unhandled mode: {0}", mode);
                        break;
                    }
                }
            }
            pack.TopValue = topPackageValue;

            // First item of the nth package:
            switch (number) {
                // most valuable package
                case 0: {
                    pack.WantedValue = topPackageValue;
                    AddItem(rng, pack, TryTopItem_N2P);
                    break;
                }
                // second valuable
                case 1: {
                    pack.WantedValue = topPackageValue * rng.Range(0.45f, 0.55f);
                    AddItem(rng, pack, TryMiddleItem_N2P);
                    break;
                }
                // third + package
                default: {
                    pack.WantedValue = topPackageValue * rng.Range(0.25f, 0.33f);
                    if (rng.Chance(0.25f)) {
                        AddItem(rng, pack, TryMiddleItem_N2P);
                    } else {
                        AddItem(rng, pack, TryLowItem_N2P);
                    }
                    break;
                }
            }
            // remaining items
            int maxItems = 10;
            // we won't find many items if remaining value is super small
            while (pack.RemainingValue > 100 && maxItems-- > 0) {
                switch (number) {
                    // most valuable package
                    case 0: {
                        if (rng.Chance(0.15f)) {
                            AddItem(rng, pack, TryTopItem_N2P);
                        } else {
                            AddItem(rng, pack, TryMiddleItem_N2P);
                        }
                        break;
                    }
                    // second valuable
                    case 1: {
                        if (rng.Chance(0.15f)) {
                            AddItem(rng, pack, TryLowItem_N2P);
                        } else {
                            AddItem(rng, pack, TryMiddleItem_N2P);
                        }
                        break;
                    }
                    // third + package
                    default: {
                        if (rng.Chance(0.15f)) {
                            AddItem(rng, pack, TryMiddleItem_N2P);
                        } else {
                            AddItem(rng, pack, TryLowItem_N2P);
                        }
                        break;
                    }
                }
            }
            return pack;
        }

        /// <summary>
        /// P2N = player to npc.
        /// Number will determine the accept chance.
        /// Lower number = better package = higher accept chance
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        private GiftPackage Create_P2N(int number, int total) {
            var rng = ship.SeededRng(Hashes.Quick(Primes.Prime006, number,
                (int)(G.Ticks / Consts.TicksPerWeek)));
            GiftPackage pack = CreatePackage(number);

            pack.AcceptChance = GetAcceptChance(rng, number, total);

            double nw = S.Situation.NetWorth * 1_000_000d;
            // if (topPackageValue == 0) {
            //     topPackageValue = nw * rng.Range(0.1f, 0.15f);
            // }

            if (topPackageValue == 0) {
                switch (mode) {
                    case GiftMode.Fuel: {
                        var cellDef = The.Defs.FastGet(DefIdsH.ObjMaterialsFusionCell);
                        var cellValue = S.Market.ValueOf(cellDef);
                        var ownedFuel = S.Sys.Inventory.CountOf(cellDef);
                        var maxFuelToGive = Mathf.Min(5, ownedFuel);
                        topPackageValue = maxFuelToGive * cellValue;
                        break;
                    }
                    case GiftMode.SurrenderRansom: {
                        topPackageValue = nw * rng.Range(0.1f, 0.15f);
                        break;
                    }
                    case GiftMode.Extortion:
                    case GiftMode.Gift: {
                        topPackageValue = nw * rng.Range(0.05f, 0.1f);
                        break;
                    }
                    default: {
                        D.Err("Unhandled mode: {0}", mode);
                        break;
                    }
                }
            }

            pack.TopValue = topPackageValue;

            // First item of the nth package:
            switch (number) {
                // most valuable package
                case 0: {
                    pack.WantedValue = topPackageValue;
                    AddItem(rng, pack, TryTopItem_P2N);
                    break;
                }
                // second valuable
                case 1: {
                    switch (mode) {
                        case GiftMode.Fuel: {
                            pack.WantedValue = topPackageValue * 0.51f;
                            break;
                        }
                        default: {
                            pack.WantedValue = topPackageValue * rng.Range(0.33f, 0.5f);
                            break;
                        }
                    }
                    AddItem(rng, pack, TryMiddleItem_P2N);
                    break;
                }
                // third + package
                default: {
                    switch (mode) {
                        case GiftMode.Fuel: {
                            var cellDef = The.Defs.FastGet(DefIdsH.ObjMaterialsFusionCell);
                            var cellValue = S.Market.ValueOf(cellDef);
                            pack.WantedValue = cellValue; // 1 cell for low
                            AddItem(rng, pack, TryLowItem_P2N);
                            break;
                        }
                        default: {
                            pack.WantedValue = topPackageValue * rng.Range(0.05f, 0.25f);
                            if (rng.Chance(0.25f)) {
                                AddItem(rng, pack, TryMiddleItem_P2N);
                            } else {
                                AddItem(rng, pack, TryLowItem_P2N);
                            }
                            break;
                        }
                    }
                    break;
                }
            }
            if (pack.Mode == GiftMode.Fuel) {
                return pack;
            }

            // remaining items
            int maxItems = 10;
            // we won't find many items if remaining value is super small
            while (pack.RemainingValue > 100 && maxItems-- > 0) {
                switch (number) {
                    // most valuable package
                    case 0: {
                            if (rng.Chance(0.15f)) {
                                AddItem(rng, pack, TryTopItem_P2N);
                            } else {
                                AddItem(rng, pack, TryMiddleItem_P2N);
                            }
                            break;
                        }
                    // second valuable
                    case 1: {
                            if (rng.Chance(0.15f)) {
                                AddItem(rng, pack, TryLowItem_P2N);
                            } else {
                                AddItem(rng, pack, TryMiddleItem_P2N);
                            }
                            break;
                        }
                    // third + package
                    default: {
                            if (rng.Chance(0.15f)) {
                                AddItem(rng, pack, TryMiddleItem_P2N);
                            } else {
                                AddItem(rng, pack, TryLowItem_P2N);
                            }
                            break;
                        }
                }
            }

            return pack;
        }

        private GiftPackage CreatePackage(int number) {
            var pack = new GiftPackage(direction, mode, ship, number);
            currentPackage = pack;
            switch (direction) {
                case GiftDirection.PlayerToNPC: {
                    pack.Icon = IconId.CUninstall;
                    break;
                }
                case GiftDirection.NPCToPlayer: {
                    pack.Icon = IconId.CInstall;
                    break;
                }
                default: {
                    D.Err("Unhandled package direction: {0}", direction);
                    pack.Icon = IconId.COpenPackage;
                    break;
                }
            }
            return pack;
        }
        public static float ValueOf(GameState S, ObjData obj, int owned) {
            var marketItem = S.Market.Find(obj.Def.IdH);
            if (marketItem == null) {
                return 0f;
            }
            var value = marketItem.Adjusted().Value;
            value *= obj.Quality;

            if (value > 0f && obj.Mind != null && !obj.BodyPartState.HasCriticalDamage) {
                // FIXME TODO: actual mind value
                if (obj.Mind.Faction.IsPlayer) {
                    value *= 10f;
                } else if (!obj.Mind.Species.IsBiological) {
                    // usable enemy core
                    value *= 2f;
                }
            }

            return value;
        }

        public static float ValueOf(GameState S, MatType mat, int owned) {
            var marketItem = S.Market.Find(mat.Def.IdH);
            if (marketItem == null) {
                return 0f;
            }
            var value = marketItem.Adjusted().Value;
            var adj = matCountValueCurve.Evaluate(owned);
            if (adj <= 0f) {
                D.Err("Refusing to do curve adjustment for mat value: {0} => {1} => {2}",
                    mat, value, adj);
                return value;
            }

            return value * adj;
        }

        public static float ValueOf(GameState S, Tile tile, int owned) {
            float baseValue = tile.Definition.Tier * 3f;
            var value = baseValue / owned;
            if (tile.ENode == null) {
                // device without connector, less valuable
                value *= 0.75f;
            } else {
                // solar panels, reactors
                if (tile.ENode.Node.IsProducer || tile.ENode.Node.IsProducerSmart) {
                    value *= 1.25f;
                }
                // it's in use = more valuable
                if (tile.ENode.IsConsuming) {
                    value *= 1.1f;
                }
            }
            S.Sys.Inventory.LoadRemainingMaterials(Caches.MatCounts);
            foreach (var mat in tile.Definition.ConstructedMats) {
                if (!Caches.MatCounts.TryGetValue(mat.Type, out var cnt)) {
                    cnt = 0;
                }
                value += ValueOf(S, mat.Type, cnt);
            }
            return value;
        }


        /// <summary>
        /// This will be a being or a tile, or an expensive rare resouce that
        /// we have little of
        /// </summary>
        /// <returns></returns>
        public bool TryTopItem_P2N(Rng rng, out GiftItem item) {
            item = new GiftItem();
            var type = GetItemType(rng, GiftTypeWeights_Top);
            switch (type) {
                case GiftItemType.Money: {
                    return TryItem_Money(rng, item, 0.05f, 0.1f);
                }
                case GiftItemType.Being: {
                    return TryItem_Being_P2N(rng, item, true);
                }
                case GiftItemType.Tile: {
                    return TryItem_Tile_P2N(rng, item, 1, true);
                }
                case GiftItemType.Obj: {
                    return TryItem_Obj_P2N(rng, item, 1, true);
                }
                case GiftItemType.Fuel: {
                    if (mode == GiftMode.Fuel) {
                        return TryItem_Fuel_P2N(1, rng, item, 0f);
                    } else {
                        return TryItem_Fuel_P2N(1, rng, item, 0.33f);
                    }
                }
                case GiftItemType.Mat: {
                    return TryItem_Mat_P2N(rng, item, true);
                }
                default: {
                        D.Err("Unhandled case: {0}", type);
                        return false;
                    }
            }
        }
        public bool TryTopItem_N2P(Rng rng, out GiftItem item) {
            item = new GiftItem();
            var type = GetItemType(rng, GiftTypeWeights_Top);
            switch (type) {
                case GiftItemType.Money: {
                    return TryItem_Money(rng, item, 0.05f, 0.1f);
                }
                case GiftItemType.Being: {
                    return TryItem_Being_N2P(rng, item);
                }
                case GiftItemType.Obj: {
                    return TryItem_Obj_N2P(rng, item);
                }
                case GiftItemType.Mat: {
                    return TryItem_Mat_N2P(rng, item);
                }
                case GiftItemType.Fuel: {
                    return TryItem_Fuel_N2P(rng, item, 0.33f);
                }
                default: {
                        D.Err("Unhandled case: {0}", type);
                        return false;
                    }
            }
        }

        private GiftItemType GetItemType(Rng rng, Dictionary<GiftItemType, float> weights) {
            if (mode == GiftMode.Fuel) {
                return GiftItemType.Fuel;
            }
            foreach (var type in excludedTypes) {
                weights.Remove(type);
            }
            var opt = rng.WeightedFrom(weights);
            return opt;
        }

        public bool TryMiddleItem_P2N(Rng rng, out GiftItem item) {
            item = new GiftItem();
            var type = GetItemType(rng, GiftTypeWeights_Mid);
            switch (type) {
                case GiftItemType.Money: {
                    return TryItem_Money(rng, item, 0.02f, 0.03f);
                }
                case GiftItemType.Being: {
                    return TryItem_Being_P2N(rng, item, false);
                }
                case GiftItemType.Tile: {
                    return TryItem_Tile_P2N(rng, item, 2, false);
                }
                case GiftItemType.Fuel: {
                    return TryItem_Fuel_P2N(2, rng, item, 0f);
                }
                case GiftItemType.Mat: {
                    return TryItem_Mat_P2N(rng, item, rng.Chance(0.5f));
                }
                case GiftItemType.Obj: {
                    return TryItem_Obj_P2N(rng, item, 2, rng.Chance(0.5f));
                }
                default: {
                    D.Err("Unhandled case: {0}", type);
                    return false;
                }
            }
        }
        public bool TryMiddleItem_N2P(Rng rng, out GiftItem item) {
            item = new GiftItem();
            var type = GetItemType(rng, GiftTypeWeights_Mid);
            switch (type) {
                case GiftItemType.Money: {
                    return TryItem_Money(rng, item, 0.02f, 0.03f);
                }
                case GiftItemType.Fuel: {
                    return TryItem_Fuel_N2P(rng, item, 0.15f);
                }
                case GiftItemType.Mat: {
                    return TryItem_Mat_N2P(rng, item);
                }
                case GiftItemType.Obj: {
                    return TryItem_Obj_N2P(rng, item);
                }
                default: {
                    D.Err("Unhandled case: {0}", type);
                    return false;
                }
            }
        }
        public bool TryLowItem_P2N(Rng rng, out GiftItem item) {
            item = new GiftItem();
            var type = GetItemType(rng, GiftTypeWeights_Low);
            switch (type) {
                case GiftItemType.Money: {
                    return TryItem_Money(rng, item, 0.001f, 0.01f);
                }
                case GiftItemType.Fuel: {
                    return TryItem_Fuel_P2N(3, rng, item, 0f);
                }
                case GiftItemType.Mat: {
                    return TryItem_Mat_P2N(rng, item, false);
                }
                case GiftItemType.Obj: {
                    return TryItem_Obj_P2N(rng, item, 5, false);
                }
                default: {
                    D.Err("Unhandled case: {0}", type);
                    return false;
                }
            }
        }
        public bool TryLowItem_N2P(Rng rng, out GiftItem item) {
            item = new GiftItem();
            var type = GetItemType(rng, GiftTypeWeights_Low);
            switch (type) {
                case GiftItemType.Money: {
                    return TryItem_Money(rng, item, 0.001f, 0.01f);
                }
                case GiftItemType.Fuel: {
                    return TryItem_Fuel_N2P(rng, item, 0.05f);
                }
                case GiftItemType.Mat: {
                    return TryItem_Mat_N2P(rng, item);
                }
                case GiftItemType.Obj: {
                    return TryItem_Obj_N2P(rng, item);
                }
                default: {
                    D.Err("Unhandled case: {0}", type);
                    return false;
                }
            }
        }
        private bool TryItem_Fuel_P2N(int num, Rng rng, GiftItem item, float heavyFuelChance) {
            var fuelMat = MatType.FastGet(MatIdH.FusionCell);
            var fuelHeavyMat = MatType.FastGet(MatIdH.FusionCellHeavy);
            var fuelCnt = S.Sys.Inventory.AvailableCount(fuelMat);
            var fuelHeavyCnt = S.Sys.Inventory.AvailableCount(fuelHeavyMat);
            if (fuelCnt == 0 && fuelHeavyCnt == 0) { return false; }
            var remainingValue = currentPackage.RemainingValue;
            int minToKeep = 2;
            MatType chosenType = null;
            if (fuelCnt == 0 || (fuelHeavyCnt > 0 && rng.Chance(heavyFuelChance))) {
                if (S.Market.ValueOf(fuelHeavyMat.Def) <= remainingValue) {
                    // give some heavy fuel
                    chosenType = fuelHeavyMat;
                    minToKeep = 0;
                }
            }
            if (fuelCnt == 0) { return false; }
            if (chosenType == null) {
                if (S.Market.ValueOf(fuelMat.Def) > remainingValue) {
                    return false;
                }
                chosenType = fuelMat;
            }
            var maxToGive = S.Sys.Inventory.AvailableCount(chosenType) - minToKeep;
            if (maxToGive < 1) { return false; }
            var wantedCount = ToCount(
                Math.Ceiling(remainingValue / S.Market.ValueOf(chosenType.Def)));

            if (wantedCount > maxToGive) {
                wantedCount = maxToGive;
            }
            item.Type = GiftItemType.Fuel;
            item.Mat = new Mat {
                Type = chosenType,
                StackSize = wantedCount,
            };
            return true;
        }
        // Package values scale with net worth, so a huge fortune makes counts past int range,
        // which a plain cast turns negative and the caps below never catch
        private static int ToCount(double count) {
            return count >= int.MaxValue ? int.MaxValue : (int) count;
        }

        private bool TryItem_Fuel_N2P(Rng rng, GiftItem item, float chanceHeavyFuel) {
            var chosenMat = MatType.FastGet(MatIdH.FusionCell);
            var remainingValue = currentPackage.RemainingValue;
            if (rng.Chance(chanceHeavyFuel)) {
                var cntLongRangeDrives = S.Components.CountMatching<HyperdriveComp>(
                    d => d.IsConstructed && d.Engine.WorksIn(UniverseLayer.Universe), max: 1);
                if (cntLongRangeDrives > 0) {
                    var heavyFuel = MatType.FastGet(MatIdH.FusionCellHeavy);
                    if (S.Market.ValueOf(heavyFuel.Def) <= remainingValue) {
                        chosenMat = heavyFuel;
                    }
                }
            }
            var value = S.Market.ValueOf(chosenMat.Def);
            if (value > remainingValue) { return false; }
            var wantedAmt = ToCount(Math.Ceiling(remainingValue / value));
            if (wantedAmt > 3) {
                wantedAmt = 3;
            }
            item.Type = GiftItemType.Fuel;
            item.Mat = new Mat() {
                Type = chosenMat,
                StackSize = wantedAmt,
            };
            return true;
        }

        private bool TryItem_Mat_P2N(Rng rng, GiftItem item, bool valuableFirst) {
            var w = Caches.WeightedInts;
            w.Clear();
            S.Sys.Inventory.LoadRemainingMaterials(Caches.MatCounts);
            var remainingValue = currentPackage.RemainingValue;
            foreach (var mt in Caches.MatCounts) {
                if (mt.Value < 1) { continue; }
                if (excludedObjs.Contains(mt.Key)) { continue; }
                var value = ValueOf(S, mt.Key, mt.Value);
                if (value > 0f) {
                    if (S.Market.ValueOf(mt.Key.Def) < remainingValue) {
                        w.Add(mt.Key.IdH, value);
                    }
                }
            }
            if (w.Count == 0) {
                D.Err("Could not add any mats to weighted dictionary: RemVal: {0}: {1}",
                    remainingValue,
                    D.DumpInstance(Caches.MatCounts));
                return false;
            }
            Maths.NormalizeOutliers(w);
            if (!valuableFirst) {
                Maths.ReverseWeights(w);
            }
            if (rng.TryWeightedFrom(w, out var idH)) {
                var mat = MatType.FastGet(idH);
                item.Type = GiftItemType.Mat;
                var m = new Mat {
                    Type = mat,
                    StackSize = 1,
                };

                var mVal = S.Market.ValueOf(m);
                var remainingPackValue = currentPackage.WantedValue - currentPackage.Value;
                var wantedCnt = ToCount(Math.Floor(remainingPackValue / mVal));
                if (wantedCnt < 1) {
                    wantedCnt = 1;
                }
                Caches.MatCounts.TryGetValue(mat, out var ownedCnt);
                if (wantedCnt > 3) {
                    wantedCnt = 3;
                }
                if (wantedCnt > ownedCnt) {
                    wantedCnt = ownedCnt;
                }
                m.StackSize = wantedCnt;
                item.Mat = m;
                excludedObjs.Add(mat);

                return true;
            }
            return false;
        }
        private bool TryItem_Mat_N2P(Rng rng, GiftItem item) {
            if (!ship.WillTrade) { return false; }
            var inv = ship.SO.Trader.Inventory;
            var w = Caches.WeightedInts;
            var remainingValue = currentPackage.RemainingValue;
            var items = inv.Where((tr) => {
                if (tr.MatType != null) {
                    return CanAddTradeableItem(tr, remainingValue);
                }
                return false;
            }).ToArray();
            if (Arrays.IsEmpty(items)) { return false; }
            item.Tradeable = rng.From(items);
            // determine quantity
            var val = S.Market.ValueOf(item.Tradeable.Def);
            var wantQuantity = ToCount(Math.Round(remainingValue / val));
            if (wantQuantity > item.Tradeable.Quantity) {
                wantQuantity = item.Tradeable.Quantity;
            }
            item.Type = GiftItemType.Mat;
            item.TradeableQuantity = wantQuantity;
            excludedObjs.Add(item.Tradeable);
            return true;
        }

        private bool TryItem_Obj_N2P(Rng rng, GiftItem item) {
            if (!ship.WillTrade) { return false; }
            var inv = ship.SO.Trader.Inventory;
            var w = Caches.WeightedInts;
            var remainingValue = currentPackage.RemainingValue;
            var items = inv.Where((tr) => {
                if (tr.IsBodyPart || tr.IsInventoryItem) {
                    return CanAddTradeableItem(tr, remainingValue);
                }
                return false;
            }).ToArray();
            if (Arrays.IsEmpty(items)) { return false; }
            item.Type = GiftItemType.Obj;
            item.Tradeable = rng.From(items);
            item.TradeableQuantity = 1;
            excludedObjs.Add(item.Tradeable);
            return true;
        }

        private bool CanAddTradeableItem(TradeableItem tr, double remainingValue) {
            if (tr.Quantity < 1) { return false; }
            if (excludedObjs.Contains(tr)) { return false; }
            return S.Market.ValueOf(tr.Def) <= remainingValue;
        }

        private bool TryItem_Obj_P2N(Rng rng, GiftItem item, int minOwned,
                bool valuableFirst) {
            var w = Caches.WeightedObjData;
            w.Clear();
            var remainingValue = currentPackage.RemainingValue;
            var objs = S.Query.GetAllObjs.Ask();
            var allCounts = S.Query.ObjCounts.Ask();
            foreach (var obj in objs.Values) {
                if (!obj.CanSell) { continue; }
                var owned = allCounts.Get(obj.Def, 0);
                if (owned < minOwned) { continue; }
                var value = ValueOf(S, obj, owned);
                if (value > 0f) {
                    if (S.Market.ValueOf(obj.Def) <= remainingValue) {
                        w.Add(obj, value);
                    }
                }
            }
            if (!valuableFirst) {
                Maths.ReverseWeights(w);
            }

            if (rng.TryWeightedFrom(w, out var o)) {
                item.Type = GiftItemType.Obj;
                item.Obj = o;
                excludedObjs.Add(o);
                return true;
            }

            return false;
        }
        private bool TryItem_Tile_P2N(Rng rng, GiftItem item, int minOwned,
                bool valuableFirst) {
            var w = Caches.WeightedEntities;
            w.Clear();
            var remainingValue = currentPackage.RemainingValue;
            foreach (var tile in S.DataGrids[WorldLayer.Objects].Data) {
                if (tile == null || tile.Transform.IsChild) { continue; }
                // don't offer ship computer, stasis array, etc
                if (!tile.Constructable.IsConstructed) { continue; }
                if (tile.Definition.IsImportant) { continue; }
                if (tile.ENode?.IsConnector == true) { continue; }
                if (tile.MaterialStorage != null) { continue; }
                if (excludedObjs.Contains(tile)) { continue; }
                var owned = S.CountByDef(tile.Definition);
                if (owned < minOwned) { continue; }
                var value = ValueOf(S, tile, owned);
                if (value > 0f) {
                    if (S.Market.ValueOf(tile.Definition) <= remainingValue) {
                        w.Add(tile, value);
                    }
                }
            }

            if (!valuableFirst) {
                Maths.ReverseWeights(w);
            }

            if (rng.TryWeightedFrom(w, out var t)) {
                item.Type = GiftItemType.Tile;
                item.Tile = (Tile) t;
                excludedObjs.Add(t);
                // keep chance to have more tiles
                if (rng.Chance(0.9f)) {
                    excludedTypes.Add(GiftItemType.Tile);
                }
                return true;
            }

            return false;
        }


        private bool TryItem_Being_N2P(Rng rng, GiftItem item) {
            var pool = new Dictionary<Species, float>();
            RaiderSpecies.WeightedFor(ship.Faction,
                RaiderSpecies.CurrentGearTier(S, ship.Faction), false, pool);
            if (!rng.TryWeightedFrom(pool, out var species)) {
                species = Species.Get(SpeciesIdH.Human);
            }
            GenItemFlags invFlags = GenItemFlags.None;
            if (rng.Chance(0.8f)) {
                invFlags |= GenItemFlags.Casual;
            }
            if (rng.Chance(0.5f)) {
                invFlags |= GenItemFlags.SpaceReady;
                if (rng.Chance(0.15f)) {
                    invFlags |= GenItemFlags.JetSuit;
                }
            }

            // unfriendly factions will gift weaponized units
            if (ship.Faction.Friendliness < 0f) {
                if (rng.Chance(-ship.Faction.Friendliness)) {
                    invFlags |= GenItemFlags.Weaponized;
                }
            }

            var vars = new GenBeingVars(species, BeingType.Neutral);
            vars.Body = ship.Faction.GenBodyVars;
            var data = BeingUtils.GenBeingWithInventory(vars, rng, invFlags);
            item.Being = data;
            item.Type = GiftItemType.Being;
            return true;
        }
        private bool TryItem_Being_P2N(Rng rng, GiftItem item, bool valuableFirst) {
            // skip living enemies
            var beings = S.Beings.All;

            var w = Caches.WeightedBeingData;
            w.Clear();

            var remaining = currentPackage.RemainingValue;
            foreach (var b in beings) {
                if (b.IsAlive && !b.Data.IsPlayer) { continue; }
                if (excludedObjs.Contains(b.Data)) { continue; }
                var val = (float) BeingUtils.ValueOf(S, b.Data, true);
                if (val > remaining) {
                    continue;
                }

                w.Add(b.Data, val);
            }
            if (valuableFirst) {
                Maths.ReverseWeights(w);
            }

            if (rng.TryWeightedFrom(w, out var beingData)) {
                item.Type = GiftItemType.Being;
                item.Being = beingData;
                excludedObjs.Add(beingData);
                // keep 5% chance to have 2 beings as a gift package
                if (rng.Chance(0.95f)) {
                    excludedTypes.Add(GiftItemType.Being);
                }
                return true;
            }

            return false;
        }

        private bool TryItem_Money(Rng rng, GiftItem item, float nwFrom, float nwTo) {
            double ourNW = S.Situation.NetWorth * 1_000_000d;
            var amt = rng.Range(ourNW * nwFrom, ourNW * nwTo);
            if (amt > currentPackage.RemainingValue) {
                amt = currentPackage.RemainingValue;
            }
            if (direction == GiftDirection.PlayerToNPC) {
                if (amt > S.Wallet.Amount) {
                    amt = S.Wallet.Amount;
                }
            } else if (ship.WillTrade) {
                if (amt > ship.SO.Trader.Wallet.Amount) {
                    amt = ship.SO.Trader.Wallet.Amount;
                }
            }
            if (amt < 500f) {
                // we don't have enough
                return false;
            }
            item.Type = GiftItemType.Money;
            item.Money = amt;
            // never generate two money packages
            excludedTypes.Add(GiftItemType.Money);
            return true;
        }


        private float GetAcceptChance(Rng rng, int number, int total) {
            var attitude = ship.AttitudeTowardsPlayer;
            var attMul = attitudeMulCurve.Evaluate(attitude);

            float topChance;
            float botChance;
            switch (mode) {
                case GiftMode.Fuel: {
                    topChance = 1f;
                    botChance = 1f;
                    break;
                }
                case GiftMode.Gift: {
                    topChance = 1f;
                    botChance = 0.8f;
                    break;
                }
                case GiftMode.SurrenderRansom: {
                    topChance = 0.95f;
                    botChance = 0.05f;
                    break;
                }
                case GiftMode.Extortion: {
                    topChance = 0.95f;
                    botChance = 0.33f;
                    break;
                }
                default: {
                    D.Err("Unhandled accept chance for mode: {0}", mode);
                    topChance = 1f;
                    botChance = 0.25f;
                    break;
                }
            }

            topChance *= attMul;
            botChance *= attMul;

            float acceptChance;
            if (total > 1) {
                var pct = number / (float) (total - 1);
                acceptChance = Mathf.Lerp(topChance, botChance, pct);
                acceptChance *= rng.RangeSmooth(0.8f, 1.2f);
            } else {
                acceptChance = topChance;
            }

            acceptChance = Mathf.Clamp(acceptChance, 0.03f, 0.99f);
            return acceptChance;
        }

        private void AddItem(Rng rng, GiftPackage pack, ItemGenerator gen) {
            int attempts = 10;
            while (attempts-- > 0) {
                if (gen(rng, out var item)) {
                    pack.AddItem(S, item);
                    break;
                }
            }
        }
    }
}
