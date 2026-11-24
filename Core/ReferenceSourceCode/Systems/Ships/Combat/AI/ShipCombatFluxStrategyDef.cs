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
    public sealed class ShipCombatFluxStrategyDef : ShipCombatStrategyDef {
        /// <summary>
        /// Blunders are bad strategies that will sometimes be switched for NPC
        /// ships to make it possible to win if the combat is in a deadlock
        /// </summary>
        public bool IsBlunder;
        /// <summary>
        /// Ship won't fire weapons if this flux threshold would exceed
        /// </summary>
        public float ShootFluxThreshold;
        /// <summary>
        /// When total flux exceeds this value, ship will want to vent it
        /// </summary>
        public float ToleratedFluxTotal;
        /// <summary>
        /// When hard flux exceeds this value, ship will want to vent it
        /// </summary>
        public float ToleratedFluxHard;
        /// <summary>
        /// When ship decides to vent, it will not stop venting until hard flux
        /// is below this value (percent)
        /// </summary>
        public float VentFluxStopHard;
        /// <summary>
        /// When ship stars venting, it will not stop until total flux is below
        /// this value (percent)
        /// </summary>
        public float VentFluxStopTotal;
        /// <summary>
        /// Quality rating of this strategy (0 = worst, 1 = optimal). Used to
        /// scale how long the AI stays in this strategy: higher difficulty
        /// reduces time in low-Strength strategies and extends high-Strength ones.
        /// </summary>
        public float Strength;

        public static Dictionary<int, ShipCombatFluxStrategyDef> All = new();

        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }
        public static ShipCombatFluxStrategyDef TryGet(string id) {
            if (All.TryGetValue(Hashes.S(id), out var def)) {
                return def;
            }
            return null;
        }

        public static ShipCombatFluxStrategyDef Get(string id) {
            if (!All.TryGetValue(Hashes.S(id), out var def)) {
                D.Err("ShipCombatFluxStrategyDef not found by id: {0}", id);
                D.Err("Available: {0}", string.Join(", ",
                    All.Values.Select(v => v.Id)));
                def = All.Values.FirstOrDefault();
            }
            return def;
        }
        public static ShipCombatFluxStrategyDef FastGet(int idH) {
            if (!All.TryGetValue(idH, out var def)) {
                D.Err("ShipCombatFluxStrategyDef not found by id hash: {0}", idH);
                def = All.Values.FirstOrDefault();
            }
            return def;
        }

        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<ShipCombatFluxStrategyDef>(
                "Config/Ships/CombatStrategies/Flux");
            foreach (var type in types) {
                OnLoadKnown(type);

                if (type.ShootFluxThreshold == 0f) {
                    D.Err("ShipCombatFluxStrategyDef has no ShootFluxThreshold!: {0}", type.Id);
                    type.ShootFluxThreshold = 0.8f;
                }
                if (type.ToleratedFluxHard == 0f) {
                    D.Err("ShipCombatFluxStrategyDef has no ToleratedHardFlux!: {0}", type.Id);
                    type.ToleratedFluxHard = 0.5f;
                }
                if (type.ToleratedFluxTotal == 0f) {
                    D.Err("ShipCombatFluxStrategyDef has no ToleratedTotalFlux!: {0}", type.Id);
                    type.ToleratedFluxTotal = 0.9f;
                }

                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("ShipCombatFluxStrategyDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
        }

        public override string ToString() {
            return $"ShipCombatFluxStrategy[{Id}]";
        }

        private static readonly Dictionary<ShipCombatFluxStrategyDef, float> weighted = new();

        public static void ChooseFor(NPCShip ship, NPCShip enemy, bool blunder) {
            weighted.Clear();
            ShipCombatFluxStrategyDef strat = null;
            var strDiff = ship.StrengthDifferenceAgainst(enemy);
            var rng = ship.SeededRng(Primes.Prime020);
            foreach (var st in All.Values) {
                if (st.IsBlunder != blunder) { continue; }
                if (st.CanAssignTo(ship, strDiff, rng, out var weight)) {
                    weighted.Add(st, weight);
                }
            }
            if (!rng.TryWeightedFrom(weighted, out strat)) {
                D.Err("Couldn't choose flux strategy for ship (will return random): {0}", ship);
                strat = rng.From(All.Values);
            }
            ship.CombatState.SetFluxStrategy(strat);
        }

    }
}
