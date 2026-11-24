using Game.Components;
using Game.Constants;
using Game.Data;
using UnityEngine;

namespace Game.Systems.Firex {

    // A fire that starts while nothing else worth extinguishing is burning gets a
    // temporary head start: it grows and spreads faster, and firefighting effort
    // against it is significantly reduced. Once the boost expires, fire behaves
    // normally again and can be brought under control.
    public sealed class FireBoost {
        private readonly GameState S;
        // Tick at which the current boost expires
        public long UntilTicks;

        public FireBoost(GameState s) {
            S = s;
        }

        public bool IsActive => S.Ticks < UntilTicks;

        public float GrowthMul => IsActive ? S.Difficulty.FireBoostGrowthMul : 1f;

        // Below full strength while boosted, so firefighters can't stop a fire from
        // spreading just by holding its strength below the cap. Not scaled down for
        // beings: this is a single job wide scalar, and a burning colonist seeding
        // fires around them does not change how long they themselves burn
        public float SpreadStrThreshold => IsActive
            ? S.Difficulty.FireBoostSpreadThreshold : 1f;

        public float SpreadMulFor(FlammableComp flam) {
            if (!IsActive) { return 1f; }
            return Scaled(S.Difficulty.FireBoostSpreadMul, flam);
        }

        public float FirefightingMulFor(FlammableComp flam) {
            if (!IsActive) { return 1f; }
            return Scaled(S.Difficulty.FireBoostFirefightingMul, flam);
        }

        // Beings burn under a weaker version of the boost, so catching fire is not
        // an automatic death sentence
        private static float Scaled(float mul, FlammableComp flam) {
            if (flam?.Being == null) { return mul; }
            return Mathf.Lerp(1f, mul, Tunables.FireBoostBeingScale);
        }

        public void TryStart() {
            // Ship generation sets a lot of things on fire before the game starts
            if (!Ready.Gen) { return; }
            var minutes = S.Difficulty.FireBoostMinutes;
            if (minutes < 1) { return; }
            var variance = Tunables.FireBoostDurationVariance;
            var until = S.Ticks + Mathf.RoundToInt(minutes * Consts.TicksPerMinute
                * S.Rng.Range(1f - variance, 1f + variance));
            if (until > UntilTicks) {
                UntilTicks = until;
            }
        }
    }
}
