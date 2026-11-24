using System;
using System.Buffers;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Hints;
using Game.Systems.Story;
using Game.Utils;
using Game.Visuals;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using UnityEngine.Pool;
using Game.Systems.Notify;

namespace Game.Systems.Firex {
    public sealed class Explosions {
        private readonly FirexSys firex;
        private readonly GameState S;
        public readonly List<Explosion> Pending = new();
        public readonly List<Explosion> Processed = new();
        public Explosions(FirexSys sys) {
            firex = sys;
            S = sys.S;
            S.Sig.CreateExplosion.AddListener(OnCreateExplosion);
        }


        private void OnCreateExplosion(Explosion expl) {
            foreach (var p in Pending) {
                if (p.PosIdx == expl.PosIdx && p.Position == expl.Position) {
                    D.ErrSoft("Skipping duplicate explosion: {0} and {1}",
                        D.DumpInstance(expl, forLog: true), D.DumpInstance(p, forLog: true));
                    return;
                }
            }
            // D.Warn("Creating explosion at {0} | {1}", Pos.String(expl.PosIdx), expl.Position);
            if (!Ready.Gen && !expl.IsImmediate) {
                D.Err("Explosion when !Ready.Gen not immediate! {0}", expl);
                expl.Flags |= ExplosionFlags.Immediate;
            }
            if (expl.IsImmediate) {
                ProcessExplosion(expl, G.Ticks);
                return;
            }
            Pending.Add(expl);
        }

        public void Tick(long ticks) {
            if (Pending.Count == 0) { return; }
            for (int i = Pending.Count - 1; i >= 0; i--) {
                Explosion expl = Pending[i];
                if (ProcessExplosion(expl, ticks)) {
                    Processed.Add(expl);
                }
            }
            if (Processed.Count > 0) {
                for (int i = Processed.Count - 1; i >= 0; i--) {
                    Explosion expl = Processed[i];
                    Pending.Remove(expl);
                }
                Processed.Clear();
            }
        }

        private static StoryEventFlagCooldownDef explosionFlagCooldown;

        private bool ProcessExplosion(Explosion expl, long ticks) {
            if (expl.IsExecuted) {
                D.Err("Explosion already executed! {0}", expl);
                return true;
            }
            if (expl.Tick > ticks && !expl.IsImmediate) {
                return false;
            }

            if (expl.PosIdx != 0) {
                expl.Position = EntityUtils.CenterOf(expl.PosIdx);
            } else {
                expl.PosIdx = EntityUtils.ToPosSafe(expl.Position);
            }

            if (expl.IsFireOnly) {
                expl.Flags |= ExplosionFlags.SkipEvent;
                expl.Flags |= ExplosionFlags.SkipFX;
                if (expl.FireChance == 0f) {
                    D.Err("Explosion is fire only but fire chance is 0f! Setting 0.5f {0}", expl);
                    expl.FireChance = 0.5f;
                }
            }

            var rng = expl.Seed == 0 ? Rng.Unseeded : new Rng(expl.Seed);


            if (expl.Radius < Maths.Epsilon || expl.Strength < Maths.Epsilon) {
                D.Err("Explosion has no radius or strength! {0}", expl);
                ForLevel(expl.Level, out var r, out var s);
                expl.Radius = Mathf.Max(r, expl.Radius);
                expl.Strength = Mathf.Max(s, expl.Strength);
            }

            if (!expl.SkipFX) {
                CreateExplosionFX(S, expl.Position, expl.Radius, expl.Strength, true);
            }
            if (DealDamage(expl, rng)) {
                if (expl.Level > 0) {
                    if (Ready.Game && Ready.Gen) {
                        var ctx = S.Query.GetStoryContext.Ask();
                        ctx.ChangePoints(StoryEventOutcome.Negative, -expl.Level,
                            null, "Explosion");

                        // Cooldown explosions
                        explosionFlagCooldown ??= StoryEventFlagCooldownDef.Get(
                            StoryEventFlags.Explosion);
                        ctx.CoolDownFlag(StoryEventFlags.Explosion,
                            explosionFlagCooldown.GetCoolDownTicks(A.S.Difficulty.Level, rng));
                    }
                } else {
                    D.Err("Explosion dealing damage has zero level! {0}", expl);
                }
                if (!expl.SkipEvent) {
                    var atTitle = T.ExplosionAt_V1.F(Pos.String(expl.PosIdx));
                    Notification.Build(IconId.CExplosion, atTitle,
                        Consts.TicksPerHour, NotificationType.Critical)
                        .Group("expl")
                        .Emergency(true)
                        .WithOnClick(() => EntityUtils.CameraFocusOn(expl.Position, new Vector2(expl.Radius, expl.Radius)))
                        .WithArea(expl.Position, expl.Radius)
                        .Show();
                    var logLine = Texts.Red(T.ExplosionAt_V1.F(Texts.LinkToPos(expl.PosIdx)));
                    S.Sig.ShowFanfare.Send(logLine);
                    S.Sys.Log.AddLine(logLine);
                }
            }
            expl.BeforeExplode?.Invoke();
            expl.BeforeExplode = null;
            expl.Flags |= ExplosionFlags.Executed;
            return true;
        }

        private bool DealDamage(Explosion expl, Rng rng) {
            var cells = DictionaryPool<int, ExplosionCell>.Get();
            cells.Clear();

            // write the explosion force max value into the center of explosion
            var explPos = expl.Position;

            var rootCell = ExplosionCell.Create(expl.PosIdx, 0f, expl.Strength);
            rootCell.TopmostTile = GetTopmostTile(expl.PosIdx);
            S.Beings.ByPos.TryGetValue(expl.PosIdx, out rootCell.Beings);
            S.Objs.ByPos.TryGetValue(expl.PosIdx, out rootCell.Objs);

            float forceFrom = expl.Strength;
            float forceTo = expl.Strength * 0.1f;

            bool ignoreObstacles = expl.IgnoreObstacles;
            bool onlyWallsBlock = expl.OnlyWallsBlock;
            var r = expl.Radius;
            var pending = new Stack<ExplosionCell>();
            cells[rootCell.PosIdx] = rootCell;
            pending.Push(rootCell);

            int[] around = Caches.PosIdx9;

            while (pending.TryPop(out var cell)) {
                int cnt = Pos.Around(cell.PosIdx, around, false);
                for (int i = 0; i < cnt; i++) {
                    var subPos = around[i];
                    if (cells.ContainsKey(subPos)) {
                        continue;
                    }
                    var posV = EntityUtils.CenterOf(subPos);
                    var dist = Vectors.Distance(posV, explPos);
                    if (dist > r) {
                        // too far
                        continue;
                    }
                    // we are stumbling onto an unprocessed cell
                    var nCell = ExplosionCell.Create(subPos, dist,
                        Mathf.Lerp(forceFrom, forceTo, dist / r));
                    // figure out the neighbor damage closest to center
                    var posTowardsCenter = Vector2.MoveTowards(nCell.Position, explPos, 1f);
                    var prevPos = EntityUtils.ToPos(posTowardsCenter);
                    if (!cells.TryGetValue(prevPos, out var prevCell)) {
                        // it's ok, we still yet have to process the cell, but other
                        // neighbor cell will do this for this one
                        // we have to put this back to the stack
                        // D.Err("Could not get previous cell from explosion cell: {0} | center: {1}",
                        //     Pos.StringFromTo(nCell.PosIdx, prevPos), explPos);
                        continue;
                    // } else {
                    //     D.Warn("Got previous pos: {0}: {1}",
                    //         Pos.StringFromTo(nCell.PosIdx, prevPos), prevCell);
                    }
                    // 1. apply neighbor force reduction
                    nCell.AppliedForce *= prevCell.ForceTransferPercent;

                    nCell.RemainingForce = nCell.AppliedForce;

                    // 2. apply current tile force reduction from env
                    Tile tile = GetTopmostTile(subPos);
                    nCell.TopmostTile = tile;
                    if (onlyWallsBlock) {
                        // the wall cell itself is still reached, what lies behind it is not
                        if (IsWallInTheWay(subPos)) {
                            nCell.RemainingForce = 0f;
                        }
                    } else if (ignoreObstacles || tile == null || tile.Definition.LayerId == WorldLayer.Floor
                            || IsOpenDoorway(tile)) {
                        // no obstacle
                    } else {
                        // keep less remaining force
                        nCell.RemainingForce -= tile.Health.Health * 1.5f;
                    }

                    // check beings in the area
                    if (S.Beings.ByPos.TryGetValue(subPos, out var beings)) {
                        nCell.Beings = beings;
                        if (!onlyWallsBlock) {
                            foreach (var b in beings) {
                                if (b.IsAlive && b.Graphics.IsVertical) {
                                    var body = b.Data.Body.BodySlot?.InstalledDef;
                                    if (body != null) {
                                        nCell.RemainingForce -= body.ResistanceBase * 5f;
                                    }
                                }
                            }
                        }
                    }
                    S.Objs.ByPos.TryGetValue(subPos, out nCell.Objs);

                    if (nCell.RemainingForce < Maths.Epsilon) {
                        nCell.RemainingForce = 0f;
                    }

                    pending.Push(nCell);
                    cells[subPos] = nCell;
                }
            }

            // here we have calculated all forces, now we can apply them

            var processedTiles = HashSetPool<int>.Get();
            processedTiles.Clear();
            var totalDamage = 0f;
            foreach (var cell in cells.Values) {
                totalDamage += cell.Explode(processedTiles, rng, expl, firex);
            }

            #if DEBUG_EXPLOSIONS
            D.Warn("Affected cells: {0} | Damaged cells: {1} | Total dmg: {2} | {1}",
                cells.Count, processedTiles.Count, totalDamage, expl);
            #endif

            processedTiles.Clear();
            HashSetPool<int>.Release(processedTiles);

            cells.Clear();
            DictionaryPool<int, ExplosionCell>.Release(cells);
            return totalDamage > 0f;
        }

        // A fully open door is a gap in the wall, so blasts pass it like floor
        public static bool IsOpenDoorway(Tile tile) {
            return tile?.Door != null && tile.Door.IsOpen;
        }

        // What stops an OnlyWallsBlock explosion: a built wall or a door that is not fully
        // open. Read from the wall layer, not the topmost tile, so an object mounted on a
        // wall cannot hide it.
        public static bool IsWallInTheWay(int pos) {
            var wall = EntityUtils.ParentObj(EntityUtils.WallAt(pos));
            return wall != null && wall.IsConstructed && !IsOpenDoorway(wall);
        }

        private static Tile GetTopmostTile(int subPos) {
            var tile = EntityUtils.GetTopmostParentTile(subPos);
            if (tile != null && !tile.IsConstructed) {
                // get one tile below it
                if (tile.Definition.LayerId > WorldLayer.Floor) {
                    tile = EntityUtils.FloorAt(subPos);
                }
            }
            if (tile != null && !tile.IsConstructed) {
                tile = null;
            }

            return tile;
        }

        // Warm and brighter than white, so it fills the room before fading
        private static readonly Color ExplosionFlashColor = new(2f, 1.45f, 0.8f, 1f);
        private const float ExplosionFlashSeconds = 0.5f;
        // Ring size in tiles: a Shredder impact (strength ~50) gets the smallest ring, and it
        // grows by ShockwaveTilesPerDoubling each time the strength doubles
        private const float ShockwaveMinTiles = 7f;
        private const float ShockwaveMaxTiles = 20f;
        private const float ShockwaveMinStrength = 50f;
        private const float ShockwaveTilesPerDoubling = 1.5f;
        // A wide blast never gets a ring narrower than itself
        private const float ShockwaveRadiusScale = 1.6f;
        // Strength whose ring pushes as hard as the old fixed ring did at its strongest
        private const float ShockwaveFullStrength = 400f;

        public static void CreateExplosionFX(GameState s, Vector2 position,
                float radius, float strength, bool shakeCamera) {
            #if DEBUG_EXPLOSIONS
            D.Warn("Creating explosion fx at {0}", position);
            EntityUtils.CameraFocusOn(position);
            s.Clock.Pause();
            #endif
            if (s.Clock.Speed >= ClockSpeed.Unlimited) {
                return;
            }
            var fx = ParticlesVFX.Create("ExplosionVFX", position,
                    isCosmetic: false, orderInLayer: 3);
            if (fx.TryGetFX(out var vfx)) {
                #if DEBUG_EXPLOSIONS
                D.Warn("rad; {0} str {1}", radius, strength);
                #endif
                var r = Mathf.Max(radius, 5);
                var st = Mathf.Max(strength, 10);
                vfx.SetVector2(ShaderVars.Size, new Vector2(r, st));
            }
            fx.SetClock(s.Clock);
            fx.Play(true);
            #if !PRODUCTION_BUILD
            fx.DestroyAfterGameTime(50f);
            #else
            fx.DestroyAfterGameTime(5f);
            #endif
            // Lights up the surroundings for a moment (GPU only, no gameplay effect)
            s.Sys.Light?.Flashes?.Add(position, ExplosionFlashColor,
                Mathf.Clamp(radius * 1.5f, 3f, 10f), ExplosionFlashSeconds);
            AddRipple(position, radius, strength);
            Sounds.PlayExplosion(position);
            if (shakeCamera) {
                s.Sig.ShakeCamera.Send(0.15f, 0.5f);
            }
        }

        // A refraction ring, drawn by the heat distortion pass: wider and harder with the
        // strength, so a gun impact ripples less than a ship blowing up
        public static void AddRipple(Vector2 position, float radius, float strength) {
            var doublings = Mathf.Log(Mathf.Max(strength, ShockwaveMinStrength)
                / ShockwaveMinStrength, 2f);
            var size = Mathf.Max(ShockwaveMinTiles + doublings * ShockwaveTilesPerDoubling,
                radius * ShockwaveRadiusScale);
            Shockwaves.Add(position, Mathf.Min(size, ShockwaveMaxTiles),
                Mathf.Clamp(Mathf.Sqrt(strength / ShockwaveFullStrength), 0.6f, 2f));
        }

        public static bool CanExplode(float chance) {
            return A.S.Rng.Chance(chance);
        }

        public static bool CanExplode(float chance, Rng rng) {
            return rng.Chance(chance);
        }
        // worstPart and worstSev: the worst wound this hit left, for the caller's one log line
        public static void DealExplosionDamageToBeing(INamedEntity source, Rng r, float dmg, Being b,
                ref BodyPartSlot worstPart, ref Severity worstSev) {
            // this is a being, we deal damage to beings differently
            b.AIAgent?.CombatUnit?.OnHarmed();
            var wbp = Caches.WeightedBodyParts;
            wbp.Clear();

            BeingUtils.FillWeighedBodyParts(b.Data.Body, wbp, true, null);
            var partHit = r.WeightedFrom(wbp);
            BodyPartSlot internalHit = null;

            if (r.Chance(0.5f)) {
                wbp.Clear();
                BeingUtils.FillWeighedBodyPartsInternal(b.Data.Body, partHit, wbp, null);
                r.TryWeightedFrom(wbp, out internalHit);
            }

            byte maxSevIncrease = partHit.GetHitSeverityIncrease(DamageType.Explosive, dmg);
            if (maxSevIncrease == 0) { return; }
            Severity hitSev;
            if (partHit.TryGetWound(DamageType.Explosive, out var w)) {
                hitSev = Severities.IncreaseBy(w.Severity, maxSevIncrease);
            } else {
                hitSev = (Severity) maxSevIncrease;
            }
            float force = maxSevIncrease / (float) Severity.Fatal;

            if (partHit.TryAddWound(source, DamageType.Explosive, hitSev, G.Ticks, force, out var wound)) {
                Track(partHit, wound.Severity, ref worstPart, ref worstSev);
                if (internalHit != null && maxSevIncrease > 1) {
                    // one step less than what reached the outer part, on top of the organ's own
                    // wound: it used to build on the outer part's existing wound instead
                    var organIncrease = (byte) (maxSevIncrease - 1);
                    Severity organSev;
                    if (internalHit.TryGetWound(DamageType.Explosive, out var organWound)) {
                        organSev = Severities.IncreaseBy(organWound.Severity, organIncrease);
                    } else {
                        organSev = (Severity) organIncrease;
                    }
                    if (internalHit.TryAddWound(source, DamageType.Explosive, organSev,
                            G.Ticks, force * 0.5f, out wound)) {
                        Track(internalHit, wound.Severity, ref worstPart, ref worstSev);
                    }
                }
            }
        }

        private static void Track(BodyPartSlot part, Severity sev,
                ref BodyPartSlot worstPart, ref Severity worstSev) {
            if (sev > worstSev) {
                worstSev = sev;
                worstPart = part;
            }
        }

        // One combat log line and one blood splat per being an explosion wounded, however
        // many hits it took. Explosion wounds were silent and bloodless.
        public static void AfterExplosionDamage(GameState s, INamedEntity source, Being b,
                BodyPartSlot worstPart, Severity worstSev) {
            if (worstPart == null) { return; }
            var msg = T.InflictWoundLog_V5.F(
                source.EntityName,
                Severities.NameFor(worstSev, false),
                DamageTypes.NameFor(DamageType.Explosive),
                b.Data.Mind.CombatLogName,
                worstPart.SlotName);
            s.Sys.Log.AddLine(Texts.ReplaceBoldWithAccented(msg));
            if (worstSev >= Severity.Moderate || !b.Data.Body.IsAlive) {
                b.Data.Body.Bleed(0.25f);
            }
        }

        private static Curve levelToRadiusCurve;
        private static Curve levelToStrengthCurve;
        public static void ForLevel(int level, out float radius, out float str) {
            if (levelToRadiusCurve == null) {
                levelToRadiusCurve = CurveDef.FastGet(CurveIdH.Explosions_LevelToRadius);
                levelToStrengthCurve = CurveDef.FastGet(CurveIdH.Explosions_LevelToStrength);
            }
            radius = levelToRadiusCurve.Evaluate(level);
            str = levelToStrengthCurve.Evaluate(level);
        }
    }
}
