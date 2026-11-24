using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    [Serializable]
    public sealed class ShipCombatWeaponStrategyDef : ShipCombatStrategyDef {
        /// <summary>
        /// Custom target priority keys - values. otherwise everything else is 3
        /// </summary>
        public string[] TargetPriorityKeys;
        public int[] TargetPriorityVals;

        public static Dictionary<int, ShipCombatWeaponStrategyDef> All = new();

        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }
        public static ShipCombatWeaponStrategyDef TryGet(string id) {
            if (All.TryGetValue(Hashes.S(id), out var def)) {
                return def;
            }
            return null;
        }

        public static ShipCombatWeaponStrategyDef Get(string id) {
            if (!All.TryGetValue(Hashes.S(id), out var def)) {
                D.Err("ShipCombatWeaponStrategyDef not found by id: {0}", id);
                D.Err("Available: {0}", string.Join(", ",
                    All.Values.Select(v => v.Id)));
                def = All.Values.FirstOrDefault();
            }
            return def;
        }
        public static ShipCombatWeaponStrategyDef FastGet(int idH) {
            if (!All.TryGetValue(idH, out var def)) {
                D.Err("ShipCombatWeaponStrategyDef not found by id hash: {0}", idH);
                def = All.Values.FirstOrDefault();
            }
            return def;
        }

        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<ShipCombatWeaponStrategyDef>(
                "Config/Ships/CombatStrategies/Weapons");
            foreach (var type in types) {
                OnLoadKnown(type);

                if (Arrays.Count(type.TargetPriorityKeys) != Arrays.Count(type.TargetPriorityVals)) {
                    D.Err("ShipCombatWeaponStrategyDef counts of TargetPriorityKeys and TargetPriorityVals do not match!: {0}",
                        type.Id);
                    type.TargetPriorityKeys = null;
                    type.TargetPriorityVals = null;
                }

                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("ShipCombatWeaponStrategyDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
        }

        public override string ToString() {
            return $"ShipCombatWeaponStrategy[{Id}]";
        }

        private static readonly Dictionary<ShipCombatWeaponStrategyDef, float> weighted = new();

        public static void ChooseFor(NPCShip ship, NPCShip enemy) {
            weighted.Clear();
            var strDiff = ship.StrengthDifferenceAgainst(enemy);
            var rng = ship.SeededRng(Primes.Prime020);
            foreach (var st in All.Values) {
                if (st.CanAssignTo(ship, strDiff, rng, out var weight)) {
                    weighted.Add(st, weight);
                }
            }
            if (!rng.TryWeightedFrom(weighted, out var strat)) {
                D.Err("Couldn't choose weapon strategy for ship (will return random): {0}", ship);
                strat = rng.From(All.Values);
            }
            ship.CombatState.SetWeaponStrategy(strat);
        }
    }
}
