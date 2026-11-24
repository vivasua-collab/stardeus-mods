using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using UnityEngine;

namespace Game.Procgen {
    // The single source for "how much of each material does scenario X start with".
    //
    // Scenarios place that table across more than one gen stage - some of it pre-stocked
    // into storage units, the rest topped up as loose rubble - and every one of them has to
    // read the same numbers out of the same tunable keys. Keeping the lookup here is also
    // what lets tests state the expected amounts without re-deriving them from those keys.
    public static class ScenarioStartingMats {
        // Difficulty and the Resource Multiplier stack down to 0.25x, and small stacks are
        // the scarce materials a scenario hands out on purpose because the player cannot
        // craft them yet (Microchip2, GoldWire). Scaling 2 down to 0 does not make a start
        // harder, it removes a material outright. So a reduction leaves amounts up to this
        // untouched and never takes a larger one below it.
        private const int reductionFloor = 5;

        // difficultyMul is Difficulty.StartingResources. share is the part of the budget a
        // stage that places only some of it (storage units vs loose rubble) is responsible
        // for. It is taken from the floored budget, so the floor protects the scenario total
        // rather than each half of it. Rounding up per material keeps a small share from
        // rounding away to nothing.
        public static void Load(string scenario, float difficultyMul,
                Dictionary<MatType, int> into, float share = 1f) {
            into.Clear();
            foreach (var m in MatType.Known) {
                #pragma warning disable
                if (!Tunable.TryInt($"{scenario}_{m.Key}", out int amt)) { continue; }
                #pragma warning restore
                var budget = Scale(amt, difficultyMul);
                into[m.Value] = share < 1f ? Mathf.CeilToInt(budget * share) : budget;
            }
        }

        private static int Scale(int amt, float difficultyMul) {
            var scaled = ModifierResourceMul.Apply(Mathf.CeilToInt(amt * difficultyMul));
            if (scaled >= amt) { return scaled; }
            return Mathf.Max(scaled, Mathf.Min(amt, reductionFloor));
        }
    }
}
