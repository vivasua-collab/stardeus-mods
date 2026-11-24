using System;
using System.Collections;
using System.Collections.Generic;
using Game.Audio;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Creatures;
using Game.Systems.Firex;
using Game.Systems.Notify;
using Game.Systems.Voice;
using Game.Utils;
using Game.Visuals;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceEffectMicroMeteoroids : SpaceEffect {
        [SelfInit]
        private static void Init() {
            Register(new SpaceEffectMicroMeteoroids());
            hitObjects.Clear();
        }

        public override string Id => "MicroMeteoroids";
        public override string Name => T.EvtMicroMeteorsTitle;
        protected override string StoryEventId => "Space/MicroMeteoroidStrike";
        private NotificationHandle warningEvent;
        private long nextHit;
        private bool isDisabled;
        private IThreat threat;
        private Facing.Type directionFrom;
        private static readonly HashSet<int> hitObjects = new();
        protected override void Cleanup() {
            warningEvent = default;
            threat = null;
            nextHit = 0;
            directionFrom = default;
            hitObjects.Clear();
            isDisabled = false;
        }
        private void EnsureWarningEvent(GameState s) {
            if (warningEvent.IsValid) { return; }
            Sounds.PlayMusic(SoundtrackMood.Danger, true);
            s.Clock.DangerPause();
            new CmdRestrictClock(ClockSpeed.Normal).Execute(s);
            warningEvent = Notification.Build(IconId.CWarning,
                    AddSectorPrefix(T.EvtMicroMeteorsTitle), 0, NotificationType.Critical)
                .WithTooltip(T.EvtMicroMeteorsDesc)
                .Sticky()
                .Emergency(false)
                .WithVoice(VoiceLine.MicroMeteoroids)
                .Show();
            var title = AddSectorPrefix(Texts.Red(T.EvtMicroMeteorsTitle));
            s.Sig.ShowFanfare.Send(title);
            s.Sys.Log.AddLine(title);
        }

        private void EnsureThreat(GameState s) {
            if (threat == null) {
                threat = new ShieldableSpaceThreat();
                s.Sig.RegisterThreat.Send(threat, true);
            }

        }
        private void RemoveThreat(GameState s) {
            if (threat != null) {
                s.Sig.RegisterThreat.Send(threat, false);
                threat = null;
            }
        }

        public override void OnEnterSector(GameState s, SpaceSector sector) {
            isDisabled = IsStoryEventDisabled(s);
            if (isDisabled) { return; }
            EnsureThreat(s);
            EnsureWarningEvent(s);
        }

        public override void OnExitSector(GameState s, SpaceSector sector) {
            RemoveThreat(s);
            if (warningEvent.IsValid) {
                Notification.Remove(ref warningEvent);
            }
        }

        public override void OnLoad(GameState s, SpaceSector sector) {
            OnEnterSector(s, sector);
        }

        public override void Tick(GameState s, long ticks, SpaceSector currentSector) {
            if (IsSwitchCheckTick(ticks)) {
                var wasDisabled = isDisabled;
                if (wasDisabled != IsStoryEventDisabled(s)) {
                    if (wasDisabled) {
                        OnEnterSector(s, currentSector);
                    } else {
                        OnExitSector(s, currentSector);
                        isDisabled = true;
                        nextHit = 0;
                    }
                }
            }
            if (isDisabled) { return; }
            if (nextHit == 0) {
                nextHit = NextHitTime(s, ticks);
                if (nextHit > ticks + Consts.TicksPer2Hours) {
                    nextHit = ticks + Consts.TicksPer2Hours;
                }
                return;
            }
            if (nextHit < ticks) {
                nextHit = NextHitTime(s, ticks);
                directionFrom = s.Rng.From(Facing.Types);
                Hit(ticks, Rng.Unseeded, s);
            }
        }

        private void Hit(long ticks, Rng rng, GameState s) {
            var count = Pos.GridW / 32;
            var dir = Facing.Rotate(directionFrom, Facing.Opposite(Facing.FromAngle(s.Sys.Ship.Rotation)));
            var posF = EntityUtils.MeteorVector(
                dir, 25f, count, Rng.Unseeded,
                    out var strikeV);

            var hit = PerformHit(ticks, rng, s, count, posF, strikeV, out var hitShield);
            if (hit && !hitShield) {
                var ctx = s.Query.GetStoryContext.Ask();
                ctx.ChangePoints(Story.StoryEventOutcome.Negative, -3, null,
                    "MicroMeteoroid Space Effect Hit");
            }
        }

        public static bool PerformHit(long ticks, Rng rng, GameState s, int count,
                Func<int, Vector2> posF, Vector2 strikeV, out bool hitShield) {
            var startPos = posF(Mathf.RoundToInt(rng.Range(count * 0.33f, count * 0.66f)));
            hitObjects.Clear();

            var shield = s.Query.GetPlayerShield.Ask();
            bool isShieldUp = shield.Item1;
            Vector2 shieldCenter = shield.Item2;
            float shieldRadSqr = shield.Item3 * shield.Item3;

            var wt = 1f; // TODO
            Vector2 curPos = startPos;
            var stepV = strikeV.normalized;
            bool hit = false;
            hitShield = false;
            if (stepV.sqrMagnitude < 0.001f) {
                D.Err("stepV is zero!");
            } else {
                var healthLeft = rng.Range(200f, 500f) * wt / 5f;
                healthLeft = Maths.Clamp(healthLeft, 200f, 10000f);
                var stepsLeft = Pos.GridW * 2f;
                var a = s.Sys.Areas;
                while (healthLeft > 0 && stepsLeft-- > 0) {
                    curPos += stepV;
                    var p = EntityUtils.ToPos(curPos);
                    if (Pos.IsOOB(p)) { continue; }
                    if (isShieldUp && Vectors.SqrDistance(shieldCenter, curPos) <= shieldRadSqr) {
                        A.Player.Ship.CombatState.Flux.AddFlux(s, 0f, 50f);
                        s.Query.ShakePlayerShipShield.Ask(1f);
                        hit = true;
                        hitShield = true;
                        Explosions.CreateExplosionFX(s, curPos, 2f, 200f, false);
                        break;
                    }
                    var source = NamedEntity.For(T.NounMicrometeoroid);
                    if (s.Beings.ByPos.TryGetValue(p, out var hitBeings)) {
                        for (int i = hitBeings.Count - 1; i >= 0; i--) {
                            Being b = hitBeings[i];
                            BeingUtils.DamageRandomBodyPart(s, b.Data, Rng.Unseeded, DamageType.Piercing,
                                Severity.Fatal, source, 2f);
                            healthLeft -= 100f;
                            hit = true;
                        }
                    }
                    var wall = EntityUtils.WallAt(p);

                    if (wall?.IsConstructed == true) {
                        var wallH = wall.Health.Health;
                        if (rng.Chance(0.7f)) {
                            wallH *= rng.Range(0.85f, 0.99f);
                        }
                        var dmgAmt = Mathf.Min(healthLeft, wallH);
                        wall.Health.TakeDamage(dmgAmt, DamageType.Piercing);
                        healthLeft -= dmgAmt;
                        hit = true;
                        Explosions.CreateExplosionFX(s, curPos, 2f, dmgAmt, true);
                    }
                    var obj = EntityUtils.ParentObjAt(p);
                    if (obj?.IsConstructed == true && hitObjects.Add(obj.PosIdx)
                            && !obj.IsImmutable) {
                        // We hit the multi-tile object for the first time
                        var objH = obj.Health.Health;
                        if (rng.Chance(0.7f)) {
                            objH *= rng.Range(0.85f, 0.99f);
                        }
                        var dmgAmt = Mathf.Min(healthLeft, objH);
                        obj.Health.TakeDamage(dmgAmt, DamageType.Piercing);
                        healthLeft -= dmgAmt;
                        Explosions.CreateExplosionFX(s, curPos, 2f, dmgAmt, true);
                        hit = true;
                    }
                }
            }

            if (Ready.Gen && G.ClockSpeed < ClockSpeed.Unlimited && !RenderUtils.DisableAllVFX) {
                Coroutines.Start(ShowLine(s, ticks, Consts.TicksPer5Minutes,
                    startPos, curPos), "MicroMeteoroid", "ShowLine", false, false);
            }
            return hit;
        }

        private static IEnumerator ShowLine(GameState s, long start, int durationInTicks,
                Vector2 startPos, Vector2 endPos) {
            var line = Lines.Create("BulletTrailLineFX", startPos, endPos);
            var alphaStart = 0.25f;
            var alphaEnd = 0.75f;
            var endTick = start + durationInTicks;
            while (G.Ticks < endTick) {
                var alphaAmount = Mathf.InverseLerp(endTick, start, G.Ticks);
                line.SetAlpha(alphaStart * alphaAmount, alphaEnd * alphaAmount);
                yield return null;
                if (!GameState.IsCurrent(s)) { yield break; }
            }

            line.Destroy();
        }

        private static readonly Curve nextHitTimeCurve = new float[] {
            0f, Consts.TicksPer15Minutes,
            0.1f, Consts.TicksPer30Minutes,
            0.15f, Consts.TicksPer2Hours,
            0.5f, Consts.TicksPer3Hours,
            0.9f, Consts.TicksPer6Hours,
            1f, Consts.TicksPer12Hours
        };

        private long NextHitTime(GameState s, long ticks) {
            var inc = s.Difficulty.Increment;
            var red = s.Difficulty.Reduction;
            var delay = nextHitTimeCurve.Evaluate(Rng.UValue);
            if (red < 1f) {
                //lower difficulty - increase the minimum delay
                var minDelay = Mathf.Lerp(Consts.TicksPer3Hours, Consts.TicksPer15Minutes, red);
                if (delay < minDelay) {
                    delay = minDelay;
                }
            } else if (inc > 1f) {
                // higher difficulty - reduce max delay
                //lower difficulty - increase the minimum delay
                var minDelay = Consts.TicksPer3Hours;
                if (delay > minDelay) {
                    delay = minDelay / inc;
                }
            }

            return ticks + (int) delay;
        }
    }
}
