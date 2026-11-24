using Game.Audio;
using Game.CodeGen;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using System.Buffers;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class AsteroidStoryEvent : StoryEvent {
        public const string EventId = "Asteroid";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new AsteroidStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ctx.S.Universe.Player.Parent.Layer != UniverseLayer.Universe;
        }

        private int RetargetEmptySpace(GameState s, Rng rng, int oldTarget) {
            var newTarget = oldTarget;
            for (int i = 0; i < s.GridWidth; i++) {
                newTarget = Pos.E(newTarget);
                if (!EntityUtils.IsWithinBounds(newTarget)) {
                    // Returning original target, we went OOB
                    return oldTarget;
                }
                // it's a space
                if (EntityUtils.GetTopmostTile(newTarget) == null) {
                    if (s.Rng.Chance(0.5f)) {
                        // Pretty far miss
                        newTarget = Pos.NeighborAt(newTarget, -rng.Range(10, 30), 0);
                    } else {
                        // Near miss
                        newTarget = Pos.NeighborAt(newTarget, -rng.Range(1, 5), 0);
                    }
                    return newTarget;
                }
            }
            D.Err("WTF. Should return earlier");
            return newTarget;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var rng = data.SeededRng;
            data.SetState(s, StoryEventState.Ending);
            if (!Ready.AreasInitial) {
                data.FailEvent(s, "Initial areas not ready, skipping asteroid");
                return;
            }

            var values = s.Sys.Areas.SolidArea;
            if (Arrays.IsEmpty(values)) {
                data.FailEvent(ctx.S, "No solid area for asteroid to target!");
                return;
            }
            var size = values.Count;
            var pool = ArrayPool<int>.Shared;
            var allPositions = pool.Rent(size);
            for (int i = 0; i < size; i++) {
                allPositions[i] = values[i];
            }
            Arrays.Shuffle(rng, allPositions, size);
            int target = Pos.Invalid;
            bool foundGoodTarget = false;
            // Stasis array, ship computer
            EntityUtils.LoadImportantTilesCache();
            var minDist = data.Def.Vars.Get(PropertyIdH.MinDistToImportantObjects, 12);
            for (int i = 0; i < size; i++) {
                target = allPositions[i];
                // D.Warn("Checking position #{0}: {1}", i, Pos.String(target));
                if (!EntityUtils.IsTooCloseToAnyOf(target, minDist, Caches.TileList)) {
                    foundGoodTarget = true;
                    break;
                }
            }
            pool.Return(allPositions);
            if (!foundGoodTarget) {
                D.ErrSoft("Could not find good target for astroid in all {0} positions!", size);
                target = rng.From(values);
                target = RetargetEmptySpace(s, rng, target);
            }

            // D.Warn("Will hit position #{0}", Pos.String(target));
            // We have a target, let's remember it
            var hitPosition = EntityUtils.CenterOf(target);
            new CmdRestrictClock(ClockSpeed.Normal).Execute(s);
            var asteroidDef = data.Def.Vars.Get(PropertyIdH.AsteroidDef,
                DefIds.ObjAsteroidsAsteroid01);
            var def = rng.From(The.Defs.Matching(asteroidDef));

            // Compute OOB start position and velocity mirroring AsteroidComp.CalculatePath
            var count = Pos.GridW / 32;
            var directionFrom = rng.From(Facing.Types);
            var posF = EntityUtils.MeteorVectorOOB(directionFrom, 35f, count, rng,
                Tunables.MeteorOOBSpawnDistance, out var strikeV);
            var startPos = posF(Mathf.RoundToInt(rng.Range(count * 0.1f, count * 0.9f)));
            var velVecNorm = (hitPosition - startPos).normalized;
            var velocity = velVecNorm * Rng.URange(0.8f, 1.2f) * s.Difficulty.Level;
            velocity = EntityUtils.MapSizeAdjustedVelocity(velocity);

            s.Sys.PendingBodies.Enqueue(new PendingSpaceBody {
                Pos = startPos,
                Velocity = velocity,
                StrikeV = strikeV,
                DefId = def.Id,
                VelMul = 1f,
                ExpireTick = s.Clock.Ticks + Consts.TicksPer6Hours,
                IsAsteroid = true,
                HitPosition = hitPosition,
            });

            data.CreateLogEntry(def.Preview, T.AsteroidIncoming, def.DescT);
            Sounds.PlayOneTrack(SoundtrackMood.Danger, SoundtrackMood.Calm);
            s.Clock.DangerPause();
        }
    }
}
