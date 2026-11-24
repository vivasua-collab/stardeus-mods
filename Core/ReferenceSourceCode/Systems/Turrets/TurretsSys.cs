using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.RangeCheck;
using Game.Systems.Ships;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.Turrets {
    public sealed class TurretsSys : GameSystem {
        public const string SysId = "TurretsSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new TurretsSys());
        }

        private TurretGraphics graphics;
        private readonly List<RotatingTurret> turrets = new();
        private readonly List<RotatingTurret> turretsAntiPersonnel = new();
        private readonly List<RotatingTurret> turretsShipCombat = new();
        private readonly Dictionary<int, RotatingTurret> turretsById = new();

        public static Curve TurretAccuracyCurve;

        protected override void OnInitialize() {
            TurretAccuracyCurve = Tunables.TurretAccuracyCurve;
            graphics = new TurretGraphics(S);
            S.Sig.AfterLoadState.AddListener(OnLoad);
            S.Query.RegisterTurret.SetResponder(RegisterTurret);
        }

        private void OnLoad(GameState state) {
            S.Clock.OnTick.AddListener(OnTick);
            S.Clock.OnTickProgress.AddListener(OnTickProgress);
            S.Sig.ShipMoved.AddListener(OnShipMoved);
        }

        private void OnShipMoved(int x, int y) {
            // S.Clock.OnNextUpdate(
            graphics.ResetHeads();
        }

        private bool RegisterTurret(RotatingTurret turret, bool add) {
            if (add) {
                if (turretsById.TryAdd(turret.TurretTile.Id, turret)) {
                    graphics.PrepareFor(turret);
                    turrets.Add(turret);
                    if (turret.Type == TurretType.Undefined) {
                        D.Err("Trying to register a turret that has Undefined turret type. It will not shoot at anything! {0}",
                            turret.TurretTile);
                        if (turret.TurretTile.HasComponent<ShipWeaponComp>()) {
                            turret.Type |= TurretType.ShipCombat;
                        }
                    }
                    if ((turret.Type & TurretType.AntiPersonnel) == TurretType.AntiPersonnel) {
                        turretsAntiPersonnel.Add(turret);
                    }
                    if ((turret.Type & TurretType.ShipCombat) == TurretType.ShipCombat) {
                        turretsShipCombat.Add(turret);
                    }
                } else {
                    D.Err("Turret already added! {0}", turret);
                }
            } else {
                if (!turretsById.Remove(turret.TurretTile.Id)) {
                    D.Err("Failed to remove turret by id: {0}: {1}",
                        turret.TurretTile.Id, turret);
                }
                turrets.Remove(turret);
                turretsAntiPersonnel.Remove(turret);
                turretsShipCombat.Remove(turret);
                graphics.UnloadFor(turret);
            }
            return true;
        }

        private void OnTickProgress(float prog) {
            for (int i = turrets.Count - 1; i >= 0; i--) {
                var turret = turrets[i];
                if (!turret.IsScanning) { continue; }
                // aim
                var target = turret.Target ?? turret.ShipWeapon?.TargetShip?.View;
                if (target == null || turret.ShipWeapon?.HasNoStation == true) {
                    // idle rotate
                    // tying the rotation to FPS looks best.
                    // 360 degrees / 128 sprites * mul * -1 or 1 for dir
                    turret.HeadAngle += 360f / 128f * G.AnimSpeedMul * turret.HeadRotDir * GameTimer.FPSRate;
                    // turret.HeadAngle += turret.HeadRotPerTick * S.Clock.TicksPerUpdate * mul;
                    if (turret.HeadAngle < 0f) {
                        turret.HeadAngle += 360f;
                    } else if (turret.HeadAngle >= 360f) {
                        turret.HeadAngle -= 360f;
                    }
                } else {
                    var tp = turret.TurretTile.Position;
                    turret.HeadAngle = Vectors.AngleClockwiseBetween(new Vector2(tp.x, tp.y - 1f),
                        tp, target.Position);
                }
                // turret.ShowDebug();
            }
            graphics.OnTickProgress(prog);
        }

        private void OnTick(long ticks) {
            using (D.Timer("TargetTurrets", 0.00001f)) {
                var active = TickTurrets(ticks);
                if (active > 0) {
                    TargetAntiPersonnelTurrets(ticks);
                    TargetShipCombatTurrets(ticks);
                }
            }
        }

        public override void Unload() {
        }

        public string GetName() {
            return "TurretsSys";
        }

        private readonly Dictionary<int, RangeCheckHit> closestHits = new();

        private int TickTurrets(long ticks) {
            int active = 0;
            for (int i = turrets.Count - 1; i >= 0; i--) {
                var turret = turrets[i];
                turret.Tick(ticks);
                if (!turret.IsScanning) { continue; }
                active++;
            }
            return active;
        }

        private readonly List<RotatingTurret> activeShipTurrets = new();

        // we shoot ship combat turrets in ship weapons sys
        private void TargetShipCombatTurrets(long ticks) {
            var turretCount = turretsShipCombat.Count;
            if (turretCount == 0) { return; }
            var turretTargets = Caches.ShipTurretTargets;
            var targetCount = S.Components.FindAllByInterface(turretTargets);
            if (targetCount == 0) { return; }
            bool hasActiveThreat = false;
            for (int i = 0; i < targetCount; i++) {
                if (turretTargets[i].IsThreatActive) {
                    hasActiveThreat = true;
                    break;
                }
            }
            if (!hasActiveThreat) { return; }
            var activeTurrets = 0;
            activeShipTurrets.Clear();
            foreach (var turret in turretsShipCombat) {
                if (!turret.IsScanning) { continue; }
                if (ShipCombatAI.CanShootNow(turret.ShipWeapon, true)) {
                    activeTurrets++;
                    activeShipTurrets.Add(turret);
                }
            }

            if (activeTurrets == 0) { return; }
            // now we know that we have at least 1 active turret and a target

            var targets = new NativeArray<RangeCheckTarget>(targetCount,
                Allocator.TempJob);
            var scanners = new NativeArray<RangeCheckScanner>(activeTurrets,
                Allocator.TempJob);

            var hits = new NativeQueue<RangeCheckHit>(Allocator.TempJob);

            int scIdx = 0;
            for (int i = activeShipTurrets.Count - 1; i >= 0; i--) {
                var turret = activeShipTurrets[i];
                scanners[scIdx++] = new RangeCheckScanner() {
                    Id = turret.TurretTile.Id,
                    Position = turret.TurretTile.Position,
                    RangeSqr = turret.RangeSqr,
                    ErrorRadius = 1f,
                };
            }

            var tIdx = 0;
            var inactiveTargets = 0;
            for (int i = 0; i < targetCount; i++) {
                var target = turretTargets[i];
                if (target.Entity.IsActive && target.IsThreatActive) {
                    targets[tIdx++] = new RangeCheckTarget() {
                        Id = target.Entity.Id,
                        Position = target.Position,
                    };
                } else {
                    inactiveTargets++;
                }
            }
            targetCount -= inactiveTargets;

            TargetTurrets(targetCount, ref targets, ref scanners, ref hits);

            closestHits.Clear();
            while (hits.TryDequeue(out var hit)) {
                // D.Warn("Turret hit: {0}", hit);
                if (hit.TargetId == 0) { continue; }
                // check if we already have a closer hit
                var sqDist = hit.SqrDist;
                float closestPrio = 0f;
                float closestSqrDist = float.MaxValue;
                if (closestHits.TryGetValue(hit.ScannerId, out var closest)) {
                    closestPrio = closest.Priority;
                    closestSqrDist = closest.SqrDist;
                }
                if (!turretsById.TryGetValue(hit.ScannerId, out var turret)) {
                    D.Err("missing turret for a hit! {0}", hit);
                    continue;
                }
                // this is potentially the closet hit
                var hitEntity = S.FindEntity<Entity>(hit.TargetId);
                if (hitEntity.TryGetComponent<IShipCombatTurretTarget>(out var tt)) {
                    var weapon = turret.ShipWeapon;
                    if (weapon == null) { continue; }
                    if (!weapon.CanEngage(tt)) { continue; }
                    int prio = weapon.EffectiveSettings.GetPriorityFor(tt.TargetType);
                    if (prio > 0 && prio >= closestPrio && sqDist < closestSqrDist) {
                        hit.Priority = prio;
                        closestHits[hit.ScannerId] = hit;
                    }
                } else {
                    D.Err("Couldn't find entity by id: {0}", hit.TargetId);
                }
            }
            hits.Dispose();

            for (int i = turretsShipCombat.Count - 1; i >= 0; i--) {
                var turret = turretsShipCombat[i];
                if (closestHits.TryGetValue(turret.TurretTile.Id, out var hit)) {
                    var hitEntity = S.FindEntity<Entity>(hit.TargetId);
                    if (hitEntity == null) {
                        D.Err("Hit obj is null by id: {0}", hit.TargetId);
                        continue;
                    }
                    if (hitEntity.TryGetComponent<IShipCombatTurretTarget>(out var target)) {
                        turret.SetTarget(target);
                    }
                // } else {
                    // we won't clear target here, it could have been set manually
                    // if (turret.Target is IShipCombatTurretTarget) {
                    //     turret.SetTarget(null);
                    // }
                }
                // check once again if we're shooting multiple weapons per tick
                if (ShipCombatAI.CanShootNow(turret.ShipWeapon, true)) {
                    var shot = turret.TryShoot(ticks);
                    if (shot) {
                        graphics.ShowTurretShotFX(S, turret);
                        turret.CheckTarget();
                    }
                }
            }
            foreach (var hit in closestHits.Values) {
                if (!turretsById.TryGetValue(hit.ScannerId, out var turret)) {
                    D.Err("missing turret for a hit! {0}", hit);
                    continue;
                }
            }
            closestHits.Clear();
        }

        private void TargetTurrets(int targetCount,
                ref NativeArray<RangeCheckTarget> targets,
                ref NativeArray<RangeCheckScanner> scanners,
                ref NativeQueue<RangeCheckHit> hits) {
            var job = new RangeCheckMultiJob() {
                Targets = targets,
                Scanners = scanners,
                OutHits = hits.AsParallelWriter(),
            };
            JobHandle dependency = new();
            var handle = job.ScheduleParallel(targetCount, 1, dependency);
            handle.Complete();
            targets.Dispose();
            scanners.Dispose();

        }

        private void TargetAntiPersonnelTurrets(long ticks) {
            if (turretsAntiPersonnel.Count == 0) { return; }
            var beingsCache = Caches.BeingList;
            beingsCache.Clear();
            for (int i = S.Beings.Alive.Count - 1; i >= 0; i--) {
                var being = S.Beings.Alive[i];
                if (being.Data.IsFriendly) { continue; }
                if (being.IsDead) { continue; }
                if (being.Data.Body.IsIncapacitated) { continue; }
                beingsCache.Add(being);
            }
            var targetCount = beingsCache.Count;

            var turretCount = turretsAntiPersonnel.Count;
            var activeTurrets = 0;
            for (int i = turretCount - 1; i >= 0; i--) {
                var turret = turretsAntiPersonnel[i];
                if (targetCount == 0 && turret.Target != null) {
                    turret.SetTarget(null);
                }
                if (!turret.IsScanning) { continue; }
                activeTurrets++;
            }

            if (targetCount == 0) { return; }
            if (activeTurrets == 0) { return; }
            // now we know that we have at least 1 active turret and a target

            var targets = new NativeArray<RangeCheckTarget>(targetCount,
                Allocator.TempJob);
            var scanners = new NativeArray<RangeCheckScanner>(activeTurrets,
                Allocator.TempJob);

            var hits = new NativeQueue<RangeCheckHit>(Allocator.TempJob);

            int scIdx = 0;
            for (int i = turretCount - 1; i >= 0; i--) {
                var turret = turretsAntiPersonnel[i];
                if (!turret.IsScanning) { continue; }
                scanners[scIdx++] = new RangeCheckScanner() {
                    Id = turret.TurretTile.Id,
                    Position = turret.TurretTile.Position,
                    RangeSqr = turret.RangeSqr,
                    ErrorRadius = 1f,
                };
            }

            var tIdx = 0;
            for (int i = beingsCache.Count - 1; i >= 0; i--) {
                var being = beingsCache[i];
                targets[tIdx++] = new RangeCheckTarget() {
                    Id = being.Id,
                    Position = being.Position,
                };
            }

            TargetTurrets(targetCount, ref targets, ref scanners, ref hits);

            closestHits.Clear();
            while (hits.TryDequeue(out var hit)) {
                // D.Warn("Turret hit: {0}", hit);
                if (hit.TargetId == 0) { continue; }
                // check if we already have a closer hit
                var sqDist = hit.SqrDist;
                if (closestHits.TryGetValue(hit.ScannerId, out var closest)) {
                    if (closest.SqrDist < sqDist) { continue; }
                }
                if (!turretsById.TryGetValue(hit.ScannerId, out var turret)) {
                    D.Err("missing turret for a hit! {0}", hit);
                    continue;
                }
                // this is potentially the closet hit
                if (EntityUtils.HasLineOfSightCoverFast(hit.ScannerPosition,
                        hit.TargetPosition, 0f, turret.Range)) {
                    // reassign closest hit
                    closestHits[hit.ScannerId] = hit;
                }
            }
            hits.Dispose();

            for (int i = turretsAntiPersonnel.Count - 1; i >= 0; i--) {
                var turret = turretsAntiPersonnel[i];
                if (closestHits.TryGetValue(turret.TurretTile.Id, out var hit)) {
                    var target = S.Beings.Find(hit.TargetId);
                    turret.SetTarget(target.AIAgent);
                } else {
                    turret.SetTarget(null);
                }
                // The shot deals damage, and what that sets off can clear the target (a removed
                // or switched off turret), so its FX flies to where the target was
                var aimedAt = turret.Target;
                var targetPos = aimedAt != null ? aimedAt.Position : default;
                var shot = turret.TryShoot(ticks);
                if (shot) {
                    if (aimedAt != null) {
                        graphics.ShowTurretShotFX(S, turret, targetPos);
                    }
                    turret.CheckTarget();
                }
            }
            foreach (var hit in closestHits.Values) {
                if (!turretsById.TryGetValue(hit.ScannerId, out _)) {
                    D.Err("missing turret for a hit! {0}", hit);
                    continue;
                }
            }
            closestHits.Clear();
        }
    }
}
