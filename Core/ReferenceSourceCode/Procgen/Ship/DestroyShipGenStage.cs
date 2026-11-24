using KL.Grid;
using Game.Data;
using UnityEngine;
using Game.Commands;
using KL.Utils;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Modifiers;
using Unity.Mathematics;
using Game.Systems.Firex;
using Game.Components;

namespace Game.Procgen {
    public class DestroyShipGenStage : BaseShipGenStage {
        public override bool ProducesIslands => true;
        public override bool IsAsync => true;
        public override bool RequiresAreas => true;
        private int initialCount = 0;
        private int initialIslands = 0;
        /// <summary>
        /// Raw destruction-level percentage (0.15..0.75) driving this gen pass.
        /// Defaults to 0.75 (the historic, only) value, so every caller that
        /// doesn't explicitly pass one (Sandbox, CloneGameUI, mid-game derelict
        /// ship story events, etc.) is completely unaffected by the Wrecked
        /// Empty Ship scenario's destruction-level slider.
        /// </summary>
        private readonly float destructionLevel;
        public DestroyShipGenStage(float destructionLevel = 0.75f) {
            this.destructionLevel = destructionLevel;
        }
        public override IEnumerator ApplyAsync() {
            initialIslands = S.Sys.Areas.IslandCount;
            initialCount = 0;
            spec.ForEachTile((i) => {
                if (EntityUtils.IsConstructedTileAt(i, WorldLayer.Floor)) {
                    initialCount++;
                }
            });

            var minX = RandomX(0, 0);
            var maxX = RandomX(1, 1);
            var minY = RandomY(0, 0);
            var maxY = RandomY(1, 1);

            var aX = RandomX(0, 0.30f);
            var bX = RandomX(0.4f, 0.6f);
            var cX = RandomX(0.7f, 1f);

            // invert the pattern
            var swap = rng.Chance(0.5f);
            if (swap) {
                minX = maxX;
                maxX = RandomX(0, 0);
                minY = maxY;
                maxY = RandomY(0, 0);
                aX = cX;
                cX = RandomX(0, 0.3f);
            }

            var exp1x = rng.Range(aX, rng.Range(aX, bX));
            var exp1y = RandomY(0.15f, 0.5f);

            var exp2x = rng.Range(bX, rng.Range(bX, cX));
            var exp2y = RandomY(0.5f, 0.9f);

            var exp3x = rng.Range(rng.Range(bX, cX), cX);
            var exp3y = RandomY(0.1f, 0.5f);

            var size = ModifierShipSize.Current;
            var explosionSizeAdj = Mathf.Lerp(0.25f, 1f, size);
            // Wrecked scenario's destruction-level slider; 1.0 (destructionLevel
            // == 0.75, the historic/only value for every other caller) reproduces
            // the original strength/radius scaling exactly.
            inten = destructionLevel / 0.75f;
            // dampen it
            inten = Mathf.Lerp(0.5f, 1f, inten);
            explosionSizeAdj *= inten;

            var thicknessRangeMin = new Vector2(1, 3);
            var thicknessRangeMax = new Vector2(2, 4);
            var thicknessRange = Vector2.Lerp(thicknessRangeMin, thicknessRangeMax, size) * inten;

            if (destructionLevel >= 0.75f) {
                // Historic behavior, byte-for-byte unchanged (including rng draw
                // order, which curated QuickStart seeds depend on for their look).
                Explode(exp1x, exp1y,
                    new Vector2(spec.Width * 0.05f, spec.Height * 0.05f) * explosionSizeAdj,
                    new Vector2(300, 500) * inten);

                S.Sig.ForceAreas.Send();
                //yield return Ready.WaitForAreas(this);
                if (DestroyedEnough()) { yield break; }

                Explode(exp2x, exp2y,
                    new Vector2(spec.Width * 0.1f, spec.Height * 0.1f) * explosionSizeAdj,
                    new Vector2(250, 400) * inten);

                S.Sig.ForceAreas.Send();
                //yield return Ready.WaitForAreas(this);
                if (DestroyedEnough()) { yield break; }

                if (!DestroyedEnough()) {
                    Explode(exp3x, exp3y,
                        new Vector2(spec.Width * 0.07f, spec.Height * 0.07f) * explosionSizeAdj,
                        new Vector2(350, 550) * inten);
                }

                S.Sig.ForceAreas.Send();
                //yield return Ready.WaitForAreas(this);
                if (DestroyedEnough()) { yield break; }

                // V shaped slices
                SliceThoughShip(aX, maxY, bX, minY,
                    thicknessRange, new Vector2(250, 500) * inten);


                S.Sig.ForceAreas.Send();
                //yield return Ready.WaitForAreas(this);
                if (DestroyedEnough()) { yield break; }

                SliceThoughShip(bX, minY, cX, maxY,
                    thicknessRange, new Vector2(250, 500) * inten);

                S.Sig.ForceAreas.Send();
                //yield return Ready.WaitForAreas(this);
                if (DestroyedEnough()) { yield break; }

                SliceThoughShip(
                    // Top Left ish corner
                    RandomX(0, 0.25f), RandomY(0.75f, 1),
                    // Bottom Right ish corner
                    RandomX(0.75f, 1), RandomY(0, 0.25f),
                    thicknessRange, new Vector2(250, 500) * inten);
                yield break;
            }

            // Below the historic default: vary the destruction *pattern*, not
            // just its strength. Pick a random subset of the explosion/slice
            // "slots" below to actually execute (size of the subset is fixed by
            // destructionLevel; *which* slots survive the cut is randomized per
            // ship seed via `rng`, so two seeds at the same destruction level
            // don't always lose the same cut).
            GetActionBudget(destructionLevel, out var numExplosions, out var numSlices);

            var explosionOrder = new List<int> { 0, 1 };
            Arrays.Shuffle(rng, explosionOrder);
            var chosenExplosions = new HashSet<int>(explosionOrder.Take(numExplosions));

            var sliceOrder = new List<int> { 0, 1, 2 };
            Arrays.Shuffle(rng, sliceOrder);
            var chosenSlices = new HashSet<int>(sliceOrder.Take(numSlices));

            if (chosenExplosions.Contains(0)) {
                Explode(exp1x, exp1y,
                    new Vector2(spec.Width * 0.05f, spec.Height * 0.05f) * explosionSizeAdj,
                    new Vector2(300, 500) * inten);
                S.Sig.ForceAreas.Send();
                if (DestroyedEnough()) { yield break; }
            }

            if (chosenExplosions.Contains(1)) {
                Explode(exp2x, exp2y,
                    new Vector2(spec.Width * 0.1f, spec.Height * 0.1f) * explosionSizeAdj,
                    new Vector2(250, 400) * inten);
                S.Sig.ForceAreas.Send();
                if (DestroyedEnough()) { yield break; }
            }
            // No exp3 top-up below the historic default — the tier table never
            // calls for more than 2 explosions.

            if (chosenSlices.Contains(0)) {
                SliceThoughShip(aX, maxY, bX, minY,
                    thicknessRange, new Vector2(250, 500) * inten);
                S.Sig.ForceAreas.Send();
                if (DestroyedEnough()) { yield break; }
            }

            if (chosenSlices.Contains(1)) {
                SliceThoughShip(bX, minY, cX, maxY,
                    thicknessRange, new Vector2(250, 500) * inten);
                S.Sig.ForceAreas.Send();
                if (DestroyedEnough()) { yield break; }
            }

            if (chosenSlices.Contains(2)) {
                SliceThoughShip(
                    // Top Left ish corner
                    RandomX(0, 0.25f), RandomY(0.75f, 1),
                    // Bottom Right ish corner
                    RandomX(0.75f, 1), RandomY(0, 0.25f),
                    thicknessRange, new Vector2(250, 500) * inten);
            }
        }

        /// <summary>
        /// How many of the 2 explosion slots and 3 slice slots to keep, below
        /// the historic 75% destruction default. See WreckedShipDestructionLevel.md.
        /// </summary>
        private static void GetActionBudget(float destructionLevel,
                out int numExplosions, out int numSlices) {
            if (destructionLevel >= 0.60f) {
                numExplosions = 2;
                numSlices = 2;
            } else if (destructionLevel >= 0.40f) {
                numExplosions = 1;
                numSlices = 2;
            } else if (destructionLevel >= 0.20f) {
                numExplosions = 1;
                numSlices = 1;
            } else {
                numExplosions = 2;
                numSlices = 0;
            }
        }

        private float inten = 1f;
        private bool DestroyedEnough() {
            if (S.Sys.Areas.IslandCount > initialIslands + 1) {
                return true;
            }
            var count = 0;
            spec.ForEachTile((i) => {
                if (EntityUtils.IsConstructedTileAt(i, WorldLayer.Floor)) {
                    count++;
                }
            });
            // D.Err("Count now: {0}", count);
            // At inten == 1 (today's default), this reduces to the historic
            // count < Min(initialCount * 0.75, 1500). Lower inten keeps more
            // tiles intact and relaxes the 1500 hard floor.
            var surviveFrac = Mathf.Lerp(0.95f, 0.75f, inten);
            var hardFloor = Mathf.Lerp(initialCount, 1500f, inten);
            var enough = count < Maths.Min(initialCount * surviveFrac, hardFloor);
            return enough;
        }

        private void Explode(int posX, int posY, Vector2 radiusRange, Vector2 strRange) {
            var explRng = rng.Fork();
            int seed = explRng.Int;
            float str = rng.Range(strRange);
            float rad = rng.Range(radiusRange);
            str = Mathf.Max(str, 150f);
            Explosion.Create(S, (int) rad + 1,
                position: EntityUtils.ToVector2(posX, posY),
                customStr: str,
                customRadius: rad,
                ignorePositions: spec.InvincibleParts,
                seed: seed,
                flags: ExplosionFlags.SkipEvent
                    | ExplosionFlags.SkipFX
                    | ExplosionFlags.Immediate
                    | ExplosionFlags.IgnoreObstacles
                    | ExplosionFlags.StructureOnly);
        }
    }
}
