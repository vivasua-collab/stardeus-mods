using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Trade {
    public struct TraderRequest {
        public static Curve WealthCurvePoor => CurveDef.FastGet(CurveIdH.Economy_TraderWealth_Poor);
        public static Curve WealthCurveMid => CurveDef.FastGet(CurveIdH.Economy_TraderWealth_Mid);
        public static Curve WealthCurveRich => CurveDef.FastGet(CurveIdH.Economy_TraderWealth_Rich);
        public int MinItems;
        public int MinGroups;
        public float MinCash;
        /// <summary>
        /// Closer to 0 = poorer.
        /// 1 = same as player
        /// > 1 = richer than player
        /// </summary>
        public float WealthFactor;
        /// <summary>
        /// SpaceObject is the object that trader spawned at. Passing a null will
        /// look at the player's current parent space object to determine the trade biome
        /// </summary>
        public SpaceObject SO;
        public bool HasNoSO;
        public FactionDef Faction;
        public Rng Rng;
        public bool Validate() {
            bool isValid = true;
            if (!HasNoSO && SO == null) {
                D.Err("Neither HasNoSO nor SO is set for TraderRequest! {0}", this);
                return false;
            }
            if (Faction == null) {
                D.Warn("Faction not set for TraderRequest! Fallback to Trader: {0} ({1})", this, SO);
                Faction = FactionDef.FastGet(FactionIdH.Trader);
            }
            if (Rng == null) {
                D.Err("Rng not set for TraderRequest! (recovering) {0} ({1})", this, SO);
                Rng = Rng.Unseeded;
            }
            if (WealthFactor == 0f) {
                D.Err("WealthFactor not set for TraderRequest! (fallback to poor) {0}, {1}", this, SO);
                WealthFactor = Rng.Curve(WealthCurvePoor);
            }
            return isValid;
        }

        public override string ToString() {
            return $"TradeReq[W:{WealthFactor}|{Faction}|{SO}]";
        }
    }

}
