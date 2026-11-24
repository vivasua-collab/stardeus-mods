using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Ships {
    [Serializable]
    public sealed class ShipCombatTargetStrategyDef : ShipCombatStrategyDef {
        /// <summary>
        /// One of ShipCombatTargetMode
        /// </summary>
        public string Mode;
        /// <summary>
        /// How long an interval mode keeps a target before picking again.
        /// Unused by the event driven modes.
        /// </summary>
        public int SwitchIntervalHoursFrom;
        public int SwitchIntervalHoursTo;

        private ShipCombatTargetMode mode;
        public ShipCombatTargetMode TargetMode => mode;
        /// <summary>
        /// Modes that pick again once their switch interval runs out. The others only
        /// react to events (their target dying, leaving, or the roster changing).
        /// </summary>
        public bool IsIntervalMode => mode == ShipCombatTargetMode.Strongest
            || mode == ShipCombatTargetMode.Weakest
            || mode == ShipCombatTargetMode.Fickle;
        /// <summary>
        /// Modes that are only meaningful when shooting at the player's side
        /// </summary>
        public bool IsEnemySideOnly => mode == ShipCombatTargetMode.Primary
            || mode == ShipCombatTargetMode.Support;

        public static Dictionary<int, ShipCombatTargetStrategyDef> All = new();

        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }
        public static ShipCombatTargetStrategyDef TryGet(string id) {
            if (All.TryGetValue(Hashes.S(id), out var def)) {
                return def;
            }
            return null;
        }

        public static ShipCombatTargetStrategyDef Get(string id) {
            if (!All.TryGetValue(Hashes.S(id), out var def)) {
                D.Err("ShipCombatTargetStrategyDef not found by id: {0}", id);
                D.Err("Available: {0}", string.Join(", ",
                    All.Values.Select(v => v.Id)));
                def = All.Values.FirstOrDefault();
            }
            return def;
        }
        public static ShipCombatTargetStrategyDef FastGet(int idH) {
            if (!All.TryGetValue(idH, out var def)) {
                D.Err("ShipCombatTargetStrategyDef not found by id hash: {0}", idH);
                def = All.Values.FirstOrDefault();
            }
            return def;
        }

        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<ShipCombatTargetStrategyDef>(
                "Config/Ships/CombatStrategies/Targets");
            foreach (var type in types) {
                OnLoadKnown(type);

                if (Enum.TryParse<ShipCombatTargetMode>(type.Mode, out var parsed)) {
                    type.mode = parsed;
                } else {
                    D.Err("ShipCombatTargetStrategyDef has an unknown Mode: {0}: {1}",
                        type.Id, type.Mode);
                    type.mode = ShipCombatTargetMode.Sticky;
                }
                if (type.IsIntervalMode && type.SwitchIntervalHoursTo <= 0) {
                    D.Err("ShipCombatTargetStrategyDef has no SwitchIntervalHours!: {0}",
                        type.Id);
                    type.SwitchIntervalHoursFrom = 1;
                    type.SwitchIntervalHoursTo = 3;
                }

                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("ShipCombatTargetStrategyDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
        }

        public override string ToString() {
            return $"ShipCombatTargetStrategy[{Id}|{mode}]";
        }

        /// <summary>
        /// Whether a target that is still a valid opponent should be dropped anyway and
        /// picked again. Plain combat ticks never do this: only an expired interval or a
        /// roster change can make a strategy change its mind.
        /// </summary>
        public bool WantsReselectOn(FocusResolveReason reason) {
            switch (reason) {
                case FocusResolveReason.IntervalExpired: {
                    return IsIntervalMode;
                }
                case FocusResolveReason.RosterChange: {
                    // Support has to notice allies arriving or dying, and who is the
                    // strongest or weakest opponent changes with the roster
                    return mode == ShipCombatTargetMode.Support
                        || mode == ShipCombatTargetMode.Strongest
                        || mode == ShipCombatTargetMode.Weakest;
                }
                default: {
                    return false;
                }
            }
        }

        /// <returns>0 when this mode has no scheduled switch</returns>
        public long NextSwitchTick(Rng rng) {
            if (!IsIntervalMode) { return 0L; }
            return G.Ticks + rng.Range(SwitchIntervalHoursFrom, SwitchIntervalHoursTo)
                * Consts.TicksPerHour;
        }

        public NPCShip SelectTarget(ShipCombatState cs, List<NPCShip> opponents) {
            switch (mode) {
                case ShipCombatTargetMode.Strongest: {
                    return ByStrength(opponents, strongest: true);
                }
                case ShipCombatTargetMode.Weakest: {
                    return ByStrength(opponents, strongest: false);
                }
                case ShipCombatTargetMode.Primary: {
                    return PlayerAmong(opponents);
                }
                case ShipCombatTargetMode.Support: {
                    return SupportTarget(cs, opponents);
                }
                case ShipCombatTargetMode.Fickle: {
                    return AnyOtherThanCurrent(cs, opponents);
                }
                case ShipCombatTargetMode.Sticky: {
                    return cs.Rng.From(opponents);
                }
                default: {
                    D.Err("Unhandled ship combat target mode: {0}", mode);
                    return cs.Rng.From(opponents);
                }
            }
        }

        private static NPCShip ByStrength(List<NPCShip> opponents, bool strongest) {
            NPCShip best = null;
            var bestStr = 0;
            for (int i = 0; i < opponents.Count; i++) {
                var opponent = opponents[i];
                var str = opponent.CombatState.WeaponCombatStrength;
                if (best == null) {
                    best = opponent;
                    bestStr = str;
                    continue;
                }
                var isBetter = strongest ? str > bestStr : str < bestStr;
                if (isBetter || (str == bestStr && opponent.Id < best.Id)) {
                    best = opponent;
                    bestStr = str;
                }
            }
            return best;
        }

        private static NPCShip PlayerAmong(List<NPCShip> opponents) {
            for (int i = 0; i < opponents.Count; i++) {
                if (opponents[i].IsPlayer) { return opponents[i]; }
            }
            return opponents[0];
        }

        private static readonly List<NPCShip> picks = new();

        private static NPCShip SupportTarget(ShipCombatState cs, List<NPCShip> opponents) {
            picks.Clear();
            for (int i = 0; i < opponents.Count; i++) {
                if (!opponents[i].IsPlayer) {
                    picks.Add(opponents[i]);
                }
            }
            if (picks.Count == 0) {
                return PlayerAmong(opponents);
            }
            var picked = cs.Rng.From(picks);
            picks.Clear();
            return picked;
        }

        private static NPCShip AnyOtherThanCurrent(ShipCombatState cs, List<NPCShip> opponents) {
            var current = cs.FocusedEnemy;
            if (current == null || opponents.Count < 2) {
                return cs.Rng.From(opponents);
            }
            picks.Clear();
            for (int i = 0; i < opponents.Count; i++) {
                if (opponents[i] != current) {
                    picks.Add(opponents[i]);
                }
            }
            if (picks.Count == 0) {
                return current;
            }
            var picked = cs.Rng.From(picks);
            picks.Clear();
            return picked;
        }

        private static readonly Dictionary<ShipCombatTargetStrategyDef, float> weighted = new();

        public static void ChooseFor(NPCShip ship, NPCShip opponent, bool isPlayerSide) {
            weighted.Clear();
            var strDiff = ship.StrengthDifferenceAgainst(opponent);
            var rng = ship.SeededRng(Primes.Prime021);
            foreach (var st in All.Values) {
                if (isPlayerSide && st.IsEnemySideOnly) { continue; }
                if (st.CanAssignTo(ship, strDiff, rng, out var weight)) {
                    weighted.Add(st, weight);
                }
            }
            if (!rng.TryWeightedFrom(weighted, out var strat)) {
                // ShipCombatAI falls back to picking a random opponent
                D.Err("Couldn't choose target strategy for ship: {0}", ship);
                return;
            }
            ship.CombatState.SetTargetStrategy(strat);
        }
    }
}
