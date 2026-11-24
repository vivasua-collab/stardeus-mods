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
    public abstract class ShipCombatStrategyDef {
        public string Id;
        public int IdH;
        /// <summary>
        /// How likely this faction ship will have this strategy
        /// </summary>
        public string[] FactionChanceKeys;
        public float[] FactionChanceVals;
        public float MinOffensiveness;
        public float MaxOffensiveness;
        public float[] StrengthDifferenceChance;
        private Curve strengthDifferenceCurve;
        public Curve StrengthDifferenceCurve => strengthDifferenceCurve ??= StrengthDifferenceChance;

        protected static void OnLoadKnown(ShipCombatStrategyDef type) {
            type.IdH = Hashes.S(type.Id);

            if (type.MaxOffensiveness == 0f) {
                D.Err("ShipCombatStrategyDef has no Min/MaxOffensiveness!: {0}", type.Id);
                type.MaxOffensiveness = 1f;
            }

            if (Arrays.Count(type.FactionChanceKeys) != Arrays.Count(type.FactionChanceVals)) {
                D.Err("ShipCombatStrategyDef counts of FactionChanceKeys and FactionChanceVals does not match!: {0}",
                    type.Id);
                type.FactionChanceKeys = null;
                type.FactionChanceVals = null;
            }
        }

        protected bool CanAssignTo(NPCShip ship, float strDiff, Rng rng, out float weight) {
            if (!ship.Tag.CanAssignCombatStrategy(this, ship)) {
                // D.Warn("Tag disallowed assigning strategy: {0} | {1}",
                //     ship.Tag.Id, this);
                weight = 0f;
                return false;
            }
            weight = StrengthDifferenceCurve.Evaluate(strDiff);
            if (FactionChanceKeys != null) {
                var idx = Array.IndexOf(FactionChanceKeys, ship.Faction.Id);
                if (idx != -1) {
                    weight *= FactionChanceVals[idx];
                }
            }
            return weight > 0f;
        }
    }
}
