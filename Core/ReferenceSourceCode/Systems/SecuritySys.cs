#if !PRODUCTION_BUILD
// #define ADD_SEC_OVERLAY
// #define DEBUG_THREATS
// #define TRACE_THREATS
#endif
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.RangeCheck;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Game.Systems.AI;
using Game.Systems.Notify;
using Game.Systems.UIUX;
using KL.Grid;

namespace Game.Systems {
    public sealed class SecuritySys : GameSystem {
        public const string SysId = "SecuritySys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new SecuritySys());
        }

        /// <summary> NEVER call Threats.Add() directly, use AddThreat
        public readonly List<IThreat> Threats = new();
        private int threatCount;
        private readonly List<ITurret> turrets = new();
        private readonly List<XenodetectorComp> xenodetectors = new();
        // Threat entity id
        private readonly List<int> knownXenothreats = new();
        // For tests
        public List<int> KnownXenothreats => knownXenothreats;
        private readonly Dictionary<int, NotificationData> xenothreatNotifications = new();
        // For tests
        public Dictionary<int, NotificationData> XenothreatNotifications => xenothreatNotifications;
        private readonly List<NotificationData> reusableXenoNotifications = new();
        private const int XenoScanTickOffset = 2;
        public int XenothreatScans;
        public int XenothreatDetections;
        private readonly List<EmergencySirenComp> sirens = new();
        public List<EmergencySirenComp> Sirens => sirens;
        private readonly List<IForceFieldCollider> ffcolliders = new();
        public bool HasActiveForceFieldColliders => ffcolliders.Count > 0;

        public Entity Entity => null;

        public bool HasThreats => threatCount > 0;
        public bool HasActiveSpaceThreats {
            get {
                if (threatCount == 0) { return false; }
                for (int i = Threats.Count - 1; i >= 0; i--) {
                    IThreat threat = Threats[i];
                    if (IsSpaceThreat(threat)) {
                        return true;
                    }
                }
                return false;
            }
        }
        public bool WantsDangerSoundtrack {
            get {
                if (threatCount == 0) { return false; }
                for (int i = Threats.Count - 1; i >= 0; i--) {
                    IThreat thr = Threats[i];
                    #if !PRODUCTION_BUILD
                    if (!thr.IsThreatActive && thr.Entity is not Being) {
                        // beings can be inactive with obey chip
                        D.ErrSoft("Inactive threat in Threats! {0} | {1}", thr, thr?.Entity);
                    }
                    #endif
                    if (thr.AffectsSoundtrack) {
                        return true;
                    }
                }
                return false;
            }
        }

        public static bool IsSpaceThreat(IThreat threat) {
            return threat?.IsThreatActive == true
                && IsSpaceThreatType(threat.ThreatType);
        }

        public static bool IsSpaceThreatType(int threatType) {
            return threatType == ThreatIdH.Asteroid
                || threatType == ThreatIdH.Meteor
                || threatType == ThreatIdH.ShieldableSpaceThreat;
        }

        public void RegisterSiren(EmergencySirenComp siren, bool add) {
            if (add) {
                sirens.Add(siren);
            } else {
                sirens.Remove(siren);
            }
        }

        #if TRACE_THREATS
        private readonly Dictionary<IThreat, string> addTraces = new();
        #endif


        private void AddThreat(IThreat target) {
            // if (G.Ticks < Consts.TicksPer4Hours) { return; }
            #if DEBUG_THREATS
            D.Warn("Adding threat: {0}", target.Entity);
            #endif
            if (Threats.Contains(target)) {
                #if TRACE_THREATS
                D.Err("Skipping already registered target: {0}. Trace: {1}", target, addTraces[target]);
                #else
                D.Err("Skipping already registered target: {0}!", target);
                #if !PRODUCTION_BUILD
                D.Err("Enable #define TRACE_THREATS to see where the first registration came from!");
                #endif
                #endif
                return;
            }
            #if TRACE_THREATS
            addTraces[target] = Environment.StackTrace;
            #endif
            #if !PRODUCTION_BUILD
            if (!target.IsThreatActive) {
                if (target is not FlammableComp) {
                    D.Err("Trying to add inactive threat: {0}", target);
                }
            }
            #endif
            if (target.ThreatType == ThreatIdH.Being) {
                var b = (Being) target.Entity;
                if (b.IsDead || b.Data.EffectiveFaction.IsPlayer || b.Data.Body.IsIncapacitated) {
                    D.ErrSoft("Refusing to add a threat for non-threatening being: {0}", target);
                    return;
                }
            }
            Threats.Add(target);
            S.Situation.ActiveThreats = threatCount = Threats.Count;
            if (target is IWithTrajectory withTr) {
                S.Sig.AddObjWithTrajectory.Send(withTr);
            }
            RefreshShipCombatMiniIfSpaceThreat(target);
        }

        private void RemoveThreat(IThreat target) {
            #if DEBUG_THREATS
            D.Warn("Removing threat: {0}", target);
            #endif
            #if TRACE_THREATS
            addTraces.Remove(target);
            #endif
            // FIXME this sometimes fails to remove!
            if (Threats.Remove(target)) {
                S.Situation.ActiveThreats = threatCount = Threats.Count;
                if (threatCount == 0) {
                    wasPausedSinceThreatsWereActive = false;
                }
                RefreshShipCombatMiniIfSpaceThreat(target);
            }
        }

        private void RefreshShipCombatMiniIfSpaceThreat(IThreat target) {
            if (IsSpaceThreatType(target.ThreatType)) {
                S.UIQuery.ToggleShipCombatMini.Ask(true);
            }
        }

        public void AddForceFieldCollider(IForceFieldCollider ffc) {
            if (!ffcolliders.AddUniqueInefficient(ffc)) {
                D.Err("Already added Force Field Collider: {0}", ffc);
            }
        }

        public void RemoveForceFieldCollider(IForceFieldCollider ffc) {
            ffcolliders.Remove(ffc);
        }

        public void AddTurret(ITurret turret) {
            if (!turrets.AddUniqueInefficient(turret)) {
                D.Err("Already added turret: {0}", turret);
            }
        }

        public void RemoveTurret(ITurret turret) {
            turrets.Remove(turret);
        }
        private BBool radarActiveB;
        public BBool RadarActiveB =>
            radarActiveB ??= new BBool(IsRadarActive());

        public bool IsRadarActive() {
            var lastRadarPing = S.BB.Get(BBKeyH.RadarPing, (long) -Consts.TicksPerDay);
            return G.Ticks - lastRadarPing <= Consts.TicksPerHour;
        }

        protected override void OnInitialize() {
            #if ADD_SEC_OVERLAY
            overlays.Add(new OverlayInfo(OverlayType.Controls,
                OverlayId.Security, IconId.WSecurity));
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            #endif

            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.TriggerSirens.AddListener(OnTriggerSirens);
            S.Sig.RegisterThreat.AddListener(OnRegisterThreat);
        }

        private void OnRegisterThreat(IThreat threat, bool add) {
            if (add) {
                AddThreat(threat);
            } else {
                RemoveThreat(threat);
            }
        }

        private void OnTriggerSirens() {
            foreach (var s in sirens) {
                if (s.IsAlertAutomatic) {
                    s.SetAlertWanted(true);
                }
            }
        }

        private bool wasPausedSinceThreatsWereActive;

        public void CheckFirstThreat() {
            if (threatCount == 0) {
                //D.Err("Trying to pause on threat when threats count is 0!");
                return;
            }
            // emergency pause if needed
            if (wasPausedSinceThreatsWereActive) { return; }
            wasPausedSinceThreatsWereActive = true;
            bool autoPause = S.Prefs.GetBool(Pref.AutoPauseWhenDanger,
                PrefH.AutoPauseWhenDanger, false);
            if (!autoPause) { return; }
            TryEmergencyPause();
        }
        private double lastEmergencyPauseRealtime = -60;

        private void TryEmergencyPause() {
            if (!S.Clock.IsStopped) {
                var t = GameTimer.realtimeSinceStartupAsDouble;
                var diff = t - lastEmergencyPauseRealtime;
                lastEmergencyPauseRealtime = t;
                if (diff < 30f) { return; }
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning) {
                    return;
                }
                #endif
                S.Clock.DangerPause();
            }
        }

        private void OnLoadSave(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
        }

        public int NumActiveSirens;
        public readonly EmergencySirenComp[] ActiveSirens = new EmergencySirenComp[256];

        private void OnTick(long ticks) {
            HandleForceFields(ticks);
            HandleThreats(ticks);
            if (ticks % Consts.TicksPer3Minutes == 1) {
                HandleSirens(ticks);
                radarActiveB?.Set(IsRadarActive());
            }
            if (ticks % Consts.TicksPerHour == XenoScanTickOffset) {
                RunXenodetectors(ticks);
            }
        }

        private void RunXenodetectors(long ticks) {
            var xc = xenodetectors.Count;
            if (xc == 0) { return; }

            var activeDetectors = 0;
            for (int i = xc - 1; i >= 0; i--) {
                var xd = xenodetectors[i];
                if (xd.IsScanning) {
                    activeDetectors++;
                }
            }

            if (activeDetectors == 0) {
                return;
            }

            var detectableCnt = S.Components.FindAllByInterface(Caches.XenoDetectables);
            var infectedCount = 0;
            Caches.BeingList.Clear();
            for (int i = S.Beings.All.Count - 1; i >= 0; i--) {
                Being b = S.Beings.All[i];
                if (b.Species.IsHumanoid) {
                    if (b.Data.HasCondition(ConditionIdH.CrawlerInfected)) {
                        Caches.BeingList.Add(b);
                        infectedCount++;
                    }
                }
            }

            if (detectableCnt == 0 && infectedCount == 0) {
                //clean
                foreach (var evt in xenothreatNotifications.Values) {
                    evt.Remove(false);
                }
                xenothreatNotifications.Clear();
                knownXenothreats.Clear();
                return;
            }

            var targets = new NativeArray<RangeCheckTarget>(detectableCnt + infectedCount,
                Allocator.TempJob);
            var scanners = new NativeArray<RangeCheckScanner>(activeDetectors,
                Allocator.TempJob);

            int scIdx = 0;
            var bestErrorRadius = float.MaxValue;
            for (int i = xc - 1; i >= 0; i--) {
                var xd = xenodetectors[i];
                if (xd.IsScanning) {
                    scanners[scIdx++] = new RangeCheckScanner() {
                        Id = xd.EntityId,
                        Position = xd.Entity.Position,
                        RangeSqr = xd.RangeSqr,
                        ErrorRadius = xd.ErrorRadius,
                    };
                    if (xd.ErrorRadius < bestErrorRadius) {
                        bestErrorRadius = xd.ErrorRadius;
                    }
                }
            }

            var tIdx = 0;
            if (detectableCnt > 0) {
                for (int i = 0; i < detectableCnt; i++) {
                    var detectable = Caches.XenoDetectables[i];
                    targets[tIdx++] = new RangeCheckTarget() {
                        Id = detectable.EntityId,
                        Position = detectable.Entity.Position,
                    };
                }
            }
            if (infectedCount > 0) {
                for (int i = 0; i < infectedCount; i++) {
                    var being = Caches.BeingList[i];
                    targets[tIdx++] = new RangeCheckTarget() {
                        Id = being.Id,
                        Position = being.Position,
                    };
                }
            }

            var hits = new NativeArray<RangeCheckHit>(targets.Length,
                Allocator.TempJob);

            var job = new RangeCheckJob() {
                Targets = targets,
                Scanners = scanners,
                OutHits = hits,
                RandomSeed = Rng.UValue,
                BestErrorRadius = bestErrorRadius + 0.1f,
            };
            JobHandle dependency = new();
            var handle = job.ScheduleParallel(targets.Length, 1, dependency);
            handle.Complete();
            scanners.Dispose();

            XenothreatScans++;

            // Group hits by region: each region gets one aggregated notification
            var regionToFirstHit = new Dictionary<int, RangeCheckHit>();
            Caches.IntSet.Clear();
            for (int i = hits.Length - 1; i >= 0; i--) {
                var hit = hits[i];
                var targetId = hit.TargetId;
                // dud
                if (targetId == 0) { continue; }
                //D.Warn("Scanner Hit: {0}", hit);
                Caches.IntSet.Add(targetId);
                // Keyed by the real position, not the jittered hit: the jitter would hop a
                // still target between rooms, walls and object footprints on every scan
                var regionId = A.S.Query.RegionAt.Ask(Pos.FromVector(targets[i].Position));
                if (!regionToFirstHit.ContainsKey(regionId)) {
                    regionToFirstHit[regionId] = hit;
                }
                if (knownXenothreats.AddUniqueInefficient(targetId)) {
                    XenothreatDetections++;
                }
            }
            targets.Dispose();
            hits.Dispose();

            // Notifications of regions without detections can be reused by newly
            // detected regions, so a moving threat does not ping again
            reusableXenoNotifications.Clear();
            Caches.IntList.Clear();
            foreach (var kvp in xenothreatNotifications) {
                if (!regionToFirstHit.ContainsKey(kvp.Key)) {
                    Caches.IntList.Add(kvp.Key);
                    if (IsXenoNotificationAlive(kvp.Value)) {
                        reusableXenoNotifications.Add(kvp.Value);
                    }
                }
            }
            for (int i = Caches.IntList.Count - 1; i >= 0; i--) {
                xenothreatNotifications.Remove(Caches.IntList[i]);
            }

            // Update or create one notification per region
            foreach (var kvp in regionToFirstHit) {
                var regionId = kvp.Key;
                if (xenothreatNotifications.TryGetValue(regionId, out var evt)
                        && !IsXenoNotificationAlive(evt)) {
                    evt.Remove(false);
                    evt = null;
                }
                var rc = reusableXenoNotifications.Count;
                if (evt == null && rc > 0) {
                    evt = reusableXenoNotifications[rc - 1];
                    reusableXenoNotifications.RemoveAt(rc - 1);
                }
                var isNew = evt == null;
                if (isNew) {
                    evt = Notification.Build(IconId.CXenodetection,
                        T.AlienLifeformDetected, Consts.TicksPer4Hours, NotificationType.Warning)
                        .Group("xeno_alert", T.AlienLifeformDetected, IconId.CXenodetection)
                        .WithTooltipFunc(TimeTillNextXenoUpdate);
                }
                UpdateXenoNotification(evt, kvp.Value);
                xenothreatNotifications[regionId] = evt;
                if (isNew) {
                    evt.Show();
                }
            }

            // Cleanup: remove notifications for regions that have no active detections
            for (int i = reusableXenoNotifications.Count - 1; i >= 0; i--) {
                reusableXenoNotifications[i].Remove(false);
            }
            reusableXenoNotifications.Clear();

            // Cleanup: remove entity ids no longer detected
            for (int i = knownXenothreats.Count - 1; i >= 0; i--) {
                if (!Caches.IntSet.Contains(knownXenothreats[i])) {
                    knownXenothreats.RemoveAt(i);
                }
            }
        }

        private static bool IsXenoNotificationAlive(NotificationData evt) {
            return evt.Handle.IsValid
                && (evt.Flags & NotificationFlags.Removed) == NotificationFlags.None;
        }

        private static void UpdateXenoNotification(NotificationData evt, RangeCheckHit hit) {
            // Kept alive for as long as the scans keep detecting it
            evt.ExpireTick = G.Ticks + Consts.TicksPer4Hours;
            var size = Consts.V2One * hit.ErrorRadius * 2f;
            Vector2 pos = hit.TargetPosition;
            evt.WithArea(pos, size, true);
            evt.OnClick = () => EntityUtils.CameraFocusOn(pos, size);
        }

        private string TimeTillNextXenoUpdate() {
            var ticksLeft = XenoScanTickOffset - G.Ticks % Consts.TicksPerHour;
            if (ticksLeft <= 0) {
                ticksLeft += Consts.TicksPerHour;
            }
            return T.NextScanIn_V1.F(Units.TicksToTime(ticksLeft));
        }

        private bool wasShieldProtecting;

        private void HandleForceFields(long ticks) {
            // (isUp, center, radius)
            // Asked even without colliders, a stale flag would make a body that enters
            // later look like it was inside when the shield came up
            var shield = S.Query.GetPlayerShield.Ask();
            var isProtecting = shield.Item1;
            var cameUp = isProtecting && !wasShieldProtecting;
            wasShieldProtecting = isProtecting;
            var ffcc = ffcolliders.Count;
            if (ffcc == 0 || !isProtecting) { return; }
            if (cameUp) {
                IgnoreCollidersInside(shield.Item2, shield.Item3);
            }
            for (int i = ffcc - 1; i >= 0; i--) {
                var collider = ffcolliders[i];
                CheckCollision(collider, shield.Item2, shield.Item3);
            }
        }

        private void IgnoreCollidersInside(Vector2 center, float radius) {
            var radiusSqr = radius * radius;
            for (int i = ffcolliders.Count - 1; i >= 0; i--) {
                var col = ffcolliders[i];
                var posWithZ = Vectors.SetZ(col.Position, col.Altitude);
                if (Vectors.SqrDistance(posWithZ, center) < radiusSqr) {
                    col.IgnoresForceField = true;
                }
            }
        }

        public bool CheckCollision(IForceFieldCollider col, Vector2 center, float radius) {
            // A destroyed body stays in ffcolliders until GameState flushes its deferred
            // removal, which can come after this system's next tick. Hitting it again would
            // split and destroy it a second time.
            if (col.IgnoresForceField || col.Entity?.IsActive != true) { return false; }
            var shield = S.Query.GetPlayerShield.Ask();
            if (!shield.Item1) { return false; }
            var posWithZ = Vectors.SetZ(col.Position, col.Altitude);
            if (Vectors.SqrDistance(posWithZ, center) < radius * radius) {
                if (col.HitForceField(posWithZ, out var softFlux, out var hardFlux)) {
                    S.Query.ShakePlayerShipShield.Ask(1f);
                    A.Player.Ship.CombatState.Flux.AddFlux(S,
                        softFlux, hardFlux);
                    return true;
                }
            }
            return false;
        }
        public bool CheckCollision(IForceFieldCollider col) {
            if (col.IgnoresForceField || col.Entity?.IsActive != true) { return false; }
            var shield = S.Query.GetPlayerShield.Ask();
            if (!shield.Item1) { return false; }
            var radius = shield.Item3;
            var posWithZ = Vectors.SetZ(col.Position, col.Altitude);
            return Vectors.SqrDistance(posWithZ, shield.Item2) < radius * radius;
        }

        private void HandleSirens(long ticks) {
            int activeSirensNow = 0;
            for (int i = sirens.Count - 1; i >= 0; i--) {
                EmergencySirenComp siren = sirens[i];
                if (siren.IsAlertActive) {
                    activeSirensNow++;
                }
            }
            if (NumActiveSirens > 0 && activeSirensNow == 0) {
                NumActiveSirens = 0;
                HailColonistsToSirens(ticks, false);
            } else if (activeSirensNow > 0) {
                NumActiveSirens = activeSirensNow;
                int activeCnt = 0;
                for (int i = sirens.Count - 1; i >= 0; i--) {
                    EmergencySirenComp siren = sirens[i];
                    if (siren.IsAlertActive) {
                        ActiveSirens[activeCnt++] = siren;
                    }
                }
                HailColonistsToSirens(ticks, true);
            }
        }

        private void HandleThreats(long ticks) {
            if (threatCount == 0) { return; }
            #if DEBUG_THREATS
            foreach (var thr in Threats) {
                if (!thr.IsThreatActive) {
                    D.ErrSoft("Inactive threat in list of threats! {0}", thr);
                }
            }
            #endif

            TickTurrets(ticks);
        }
        private readonly int actionIdH = Hashes.S("EmergencySiren");

        private void HailColonistsToSirens(long ticks, bool sirensActive) {
            foreach (var b in S.Beings.Alive) {
                var d = b.Data;
                if (!d.Species.IsBiological) { continue; }
                if (b.Motion.IsImmobilized) { continue; }
                if (!d.EffectiveFaction.IsPlayer) { continue; }
                if (b.AIAgent.Goal?.IsNonInterruptable == true) { continue; }
                bool hasReactGoal = b.AIAgent.BB.TryNeedGoal(actionIdH, out var g);
                if (!sirensActive) {
                    if (hasReactGoal) {
                        if (AIGoal.IsActive(g)) {
                            D.Warn("Cancelling emergency siren for {0} | {1}", g, b.AIAgent);
                            AIGoal.Cancel(g, ticks);
                            g.ResetWorldState();
                        }
                        b.AIAgent.BB.NeedGoals.Remove(actionIdH);
                    }
                    continue;
                }
                if (!hasReactGoal) {
                    g = AIGoal.CreatePersonal(JobTypeIdH.Need,
                        IconId.CSiren, T.EmergencySiren, b.AIAgent, ticks)
                        .WithPriority(5)
                        .WithWorldToDesired(AIVarsH.ReactedToEmergencySiren, false, true);
                    g.GoalImportance = 2;
                    g.WithInterrupt(3, 0);
                    b.AIAgent.BB.SetNeedGoal(actionIdH, g);
                }
                g.WithInterrupt(3, 0);
                g.GoalImportance = 2;
                g.GoalPriority = 5;
                if (g.State == AIGoalState.InProgress) {
                    continue;
                }
                if (g.State != AIGoalState.Available) {
                    g.ChangeStateTo(AIGoalState.Available, ticks);
                    g.ResetWorldState();
                    continue;
                }
            }
        }

        private void TickTurrets(long ticks) {
            var turC = turrets.Count;
            if (turC == 0) { return; }
            for (int i = 0; i < turC; i++) {
                var turret = turrets[i];
                if (!turret.IsTurretUsable) { continue; }
                // WTF DUDE
                //Arrays.Shuffle(Rng.Unseeded, Threats);
                int startIdx = Rng.URange(0, threatCount);
                int remainingCount = threatCount;
                int i1 = startIdx;
                while (remainingCount-- > 0) {
                    i1 = (i1 + 1) % threatCount;
                    var t = Threats[i1];
                    if (!t.IsThreatActive) {
                        // D.Warn("Removing inactive threat: {0}", t);
                        RemoveThreat(t);
                        remainingCount--;
                        continue;
                    }
                    if (turret.CanHandle(t) && turret.TryShootAt(t, ticks)) {
                        break;
                    }
                }
            }
        }

        public override void Unload() {
            Array.Clear(ActiveSirens, 0, ActiveSirens.Length);
            Threats.Clear();
            threatCount = 0;
            turrets.Clear();
            ffcolliders.Clear();
            wasShieldProtecting = false;
            xenodetectors.Clear();
            xenothreatNotifications.Clear();
            reusableXenoNotifications.Clear();
            knownXenothreats.Clear();
            #if TRACE_THREATS
            addTraces.Clear();
            #endif
        }


        public void Add(XenodetectorComp xd) {
            if (!xenodetectors.AddUniqueInefficient(xd)) {
                D.Err("Xenodetector already added! {0}", xd);
            }
        }

        public void Remove(XenodetectorComp xd) {
            xenodetectors.Remove(xd);
        }
    }
}
