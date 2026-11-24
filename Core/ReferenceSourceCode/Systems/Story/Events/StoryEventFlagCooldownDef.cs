using System;
using System.Collections.Generic;
using Game.Constants;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class StoryEventFlagCooldownDef {
        public static readonly Dictionary<StoryEventFlags, StoryEventFlagCooldownDef> All = new();

        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public string Id;
        public string FlagName;
        [NonSerialized] public StoryEventFlags Flag;
        public float[] CooldownDaysDifficultyCurve;
        /// <summary>
        /// Normally when a cooldown already exists, if new cooldown is longer,
        /// it will extend the existing cooldown. If this is set to true,
        /// the existing cooldown will remain untouched unless it's already
        /// expired.
        /// </summary>
        public bool DoNotExtend;
        private Curve _cooldownCurve;
        private Curve cooldownDaysDifficultyCurve =>
            _cooldownCurve ??= CooldownDaysDifficultyCurve;

        public static StoryEventFlagCooldownDef Get(StoryEventFlags flag) {
            if (All.TryGetValue(flag, out var def)) {
                return def;
            }
            return null;
        }
        public int GetCoolDownTicks(float difficulty, Rng rng) {
            var days = cooldownDaysDifficultyCurve.Evaluate(difficulty);
            if (days == 0f) {
                D.Err("GetCoolDownTicks is 0 for curve: {0}. Curve: {1}. Diff: {2}", Id,
                    cooldownDaysDifficultyCurve, difficulty);
                days = 0.01f;
            }
            days *= rng.Range(0.8f, 1.2f);
            return Mathf.RoundToInt(days * Consts.TicksPerDay);
        }

        private static void LoadKnown() {
            if (All.Count > 0) { return; }

            var defs = The.ModLoader.LoadObjects<StoryEventFlagCooldownDef>(
                "Config/Story/FlagCooldowns");
            foreach (var def in defs) {
                if (!System.Enum.TryParse(def.FlagName, out def.Flag)) {
                    D.Err("Invalid flag name in StoryEventFlagCooldownDef: {0} | {1}",
                        def.FlagName, def.Id);
                    continue;
                }

                if (Arrays.IsEmpty(def.CooldownDaysDifficultyCurve)) {
                    D.Err("StoryEventFlagCooldownDef has no CooldownDaysDifficultyCurve: {0}",
                        def.Id);
                    continue;
                }

                // Only allow coolable flags
                if ((StoryEventFlags.Coolable & def.Flag) != def.Flag) {
                    D.Err("StoryEventFlagCooldownDef flag is not coolable: {0} | {1}",
                        def.Flag, def.Id);
                    continue;
                }

                if (!All.TryAdd(def.Flag, def)) {
                    D.Err("StoryEventFlagCooldownDef with such flag already exists: {0} | {1}",
                        def.Flag, def.Id);
                    continue;
                }
            }

            var flags = Enum.GetValues(typeof(StoryEventFlags));
            foreach (StoryEventFlags flag in flags) {
                if (flag == StoryEventFlags.None) { continue; }
                if (flag == StoryEventFlags.Coolable) { continue; }
                if (flag == StoryEventFlags.RunASAPWhenPrioritized) { continue; }
                if ((StoryEventFlags.Coolable & flag) == flag) {
                    if (!All.ContainsKey(flag)) {
                        D.Err("Config/Story/FlagCooldowns/{0}.json is missing for a coolable flag!", flag);
                    }
                }
            }
        }

        public override string ToString() {
            return $"StoryEventFlagCooldownDef[{Id}|{Flag}]";
        }
    }
}
