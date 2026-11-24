using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.RangeCheck;
using Game.Systems.Reg;
using Game.Utils;
using Game.Visuals;
using HarmonyLib;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Game.Systems.Nanobots {
    public sealed class NanobotSys : GameSystem, ISaveable {
        public struct NanobotFX {
            public NanobotFX(Vector2 from, Vector2 to, Vector2 size) {
                From = from;
                To = to;
                Size = size;
                FromEntity = null;
                TargetEntity = null;
            }
            public NanobotFX(Entity fromEntity, Entity targetEntity,
                    Vector2 size) {
                FromEntity = fromEntity;
                TargetEntity = targetEntity;
                From = fromEntity?.Position ?? Vector2.zero;
                To = targetEntity?.Position ?? Vector2.zero;
                Size = size;
            }
            public Vector2 From;
            public Vector2 To;
            public Vector2 Size;
            public Entity FromEntity;
            public Entity TargetEntity;
            public bool IsDynamic => TargetEntity != null;
        }
        private sealed class DynamicNanobotFX {
            public ParticlesVFX Particle;
            public Entity FromEntity;
            public Entity TargetEntity;
            public Vector2 FromFallback;
            public Vector2 Size;
        }
        public const string SysId = "NanobotSys";
        public override string Id => SysId;
        private readonly Dictionary<int, INanobotWorkItem> nanobotWorkItems = new();

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new NanobotSys());
        }

        private readonly List<NanobotHiveComp> hubs = new();
        private readonly List<NanobotHiveComp> workingHives = new();
        private readonly Dictionary<int, NanobotHiveComp> workingHivesById = new();
        // Working hives bucketed by NanobotReachability.AreaKeyOf, so failed
        // hauling goal recovery only checks hives that share an area with the
        // goal instead of every hive on the ship.
        private readonly Dictionary<int, List<NanobotHiveComp>>
            workingHivesByAreaKey = new();
        private readonly List<List<NanobotHiveComp>> hiveListCache = new();
        private int usedHiveLists;
        private readonly Dictionary<int, RangeCheckHit> closestHits = new();
        private const int particlesPerFrame = 16;
        private readonly Queue<NanobotFX> pendingParticles = new();
        private readonly List<DynamicNanobotFX> dynamicParticles = new();
        private readonly NanobotHaulingController haulingController = new();
        private HashSet<int> disabledJobs;

        public override void Unload() {
            nanobotWorkItems.Clear();
            hubs.Clear();
            workingHives.Clear();
            workingHivesById.Clear();
            workingHivesByAreaKey.Clear();
            hiveListCache.Clear();
            usedHiveLists = 0;
            pendingParticles.Clear();
            for (int i = dynamicParticles.Count - 1; i >= 0; i--) {
                dynamicParticles[i].Particle?.Destroy();
            }
            dynamicParticles.Clear();
            haulingController.Unload();
        }

        public string GetName() {
            return Id;
        }

        private void FlushPendingParticles() {
            int particlesThisFrame = 0;
            while (particlesThisFrame < particlesPerFrame
                    && pendingParticles.TryDequeue(out var paintBit)) {
                particlesThisFrame++;
                ShowParticlesAt(paintBit);
            }
        }
        public void ShowParticlesAt(NanobotFX p) {
            var from = EntityUtils.ClampToBounds(CurrentFrom(p));
            var to = EntityUtils.ClampToBounds(CurrentTo(p));
            var particle = ParticlesVFX.CreateWithBudget(S, "NanobotsVFX",
                from, BBKeyH.NanobotsPlayingVFX, Tunables.NanobotsMaxVFX);
            var hasFx = particle.TryGetFX(out _);
            UpdateParticleTarget(particle, from, to, p.Size, true);
            particle.SetClock(S.Clock);
            if (p.IsDynamic) {
                if (hasFx && p.TargetEntity?.IsActive == true) {
                    dynamicParticles.Add(new DynamicNanobotFX {
                        Particle = particle,
                        FromEntity = p.FromEntity,
                        TargetEntity = p.TargetEntity,
                        FromFallback = from,
                        Size = p.Size,
                    });
                } else {
                    particle.Destroy();
                }
            } else {
                particle.DestroyAfterGameTime(Tunables.NanobotsVFXTimeoutGameTime);
            }
        }

        private static Vector2 CurrentFrom(NanobotFX p) {
            return p.FromEntity?.IsActive == true
                ? p.FromEntity.Position
                : p.From;
        }

        private static Vector2 CurrentTo(NanobotFX p) {
            return p.TargetEntity?.IsActive == true
                ? p.TargetEntity.Position
                : p.To;
        }

        private static void UpdateParticleTarget(ParticlesVFX particle,
                Vector2 from, Vector2 to, Vector2 size,
                bool setStartTime) {
            if (!particle.TryGetFX(out var fx)) { return; }
            var particleDir = to - from;
            // D.Warn("Showing particle: {0} -> {1}", from, particleDir);
            fx.SetVector2(ShaderVars.Size, size);
            fx.SetVector3(ShaderVars.Target, particleDir);
            fx.SetVector4(ShaderVars.Color, Consts.ColorChartBlueBright);
            if (setStartTime) {
                fx.SetFloat(ShaderVars.StartTime, GameTimer.time);
            }
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.RegisterNanobotHive.AddListener(OnRegisterHive);
            S.Query.HasWorkingNanobots.SetResponder(HasWorkingNanobots);
            S.Query.IsNanobotsJobEnabled.SetResponder(IsJobEnabled);
            S.Query.ToggleNanobotsJob.SetResponder(ToggleJob);
        }

        private bool HasWorkingNanobots() {
            // AISys uses this as job-capability, not per-tick hive availability.
            // Temporary fuel/power/busy gaps should not fail nanobot-compatible goals.
            return hubs.Count > 0;
        }

        private void OnLoadSave(GameState state) {
            haulingController.Initialize(state);
            state.Clock.OnTick.AddListener(OnTick);
            state.Clock.OnTickProgress.AddListener(OnTickProgress);
            state.Clock.OnUpdate60.AddListener(OnUpdate);
        }

        private bool ToggleJob(int jobIdH, bool enable) {
            if (enable) {
                if (disabledJobs == null) { return true; }
                disabledJobs.Remove(jobIdH);
                if (disabledJobs.Count == 0) {
                    disabledJobs = null;
                }
                return true;
            }
            // disable
            disabledJobs ??= new HashSet<int>();
            disabledJobs.Add(jobIdH);
            return true;
        }

        private bool IsJobEnabled(int jobIdH) {
            if (disabledJobs == null) {
                return true;
            }
            return !disabledJobs.Contains(jobIdH);
        }

        private void OnUpdate() {
            FlushPendingParticles();
            UpdateDynamicParticles();
        }

        private void UpdateDynamicParticles() {
            for (int i = dynamicParticles.Count - 1; i >= 0; i--) {
                var p = dynamicParticles[i];
                if (!UpdateDynamicParticle(p)) {
                    p.Particle?.Destroy();
                    dynamicParticles.RemoveAt(i);
                }
            }
        }

        private bool UpdateDynamicParticle(DynamicNanobotFX p) {
            if (p.Particle == null || p.Particle.IsDestroyed) {
                return false;
            }
            if (p.TargetEntity?.IsActive != true
                    || p.TargetEntity.NanobotCarrierHiveId == 0) {
                return false;
            }
            var fromRaw = p.FromEntity?.IsActive == true
                ? p.FromEntity.Position
                : p.FromFallback;
            var from = EntityUtils.ClampToBounds(fromRaw);
            var to = EntityUtils.ClampToBounds(p.TargetEntity.Position);
            p.Particle.MoveTo(from);
            UpdateParticleTarget(p.Particle, from, to, p.Size, false);
            return true;
        }

        private void OnTickProgress(float progress) {
            haulingController.OnTickProgress(S.Clock.TicksPerUpdate, G.Ticks);
        }

        private void OnTick(long ticks) {
            if (hubs.Count == 0) {
                workingHives.Clear();
                workingHivesById.Clear();
                ClearWorkingHiveAreaBuckets();
                haulingController.Coverage.Begin(S.GridWidth, S.GridHeight);
                haulingController.SetMaxHiveRange(0f);
                haulingController.ValidateActiveOps();
                ClearNanobotWorkableFlags();
                return;
            }
            workingHives.Clear();
            workingHivesById.Clear();
            ClearWorkingHiveAreaBuckets();
            haulingController.Coverage.Begin(S.GridWidth, S.GridHeight);
            var maxHiveRange = 0f;
            for (int i = hubs.Count - 1; i >= 0; i--) {
                NanobotHiveComp hub = hubs[i];
                if (hub.CanWork) {
                    workingHives.Add(hub);
                    workingHivesById[hub.EntityId] = hub;
                    AddWorkingHiveToAreaBucket(hub);
                    haulingController.Coverage.Add(hub.Entity.Position, hub.RangeSqr);
                    if (hub.Range > maxHiveRange) {
                        maxHiveRange = hub.Range;
                    }
                }
            }
            haulingController.SetMaxHiveRange(maxHiveRange);
            haulingController.ValidateActiveOps();
            // check with AI system if there's anything we can work on
            nanobotWorkItems.Clear();
            // the fire check chases pointers through every goal's target, pointless
            // while nothing burns
            var isAnyFlammableOnFire = S.Sys.Firex.FlammablesOnFire > 0;
            foreach (var goal in S.Sys.AI.ColonyGoals.Values) {
                if (!goal.IsNanobotCompatible) { continue; }
                // Cheap dictionary/hashset lookups first, so the pricier
                // IsBlockedByFire check (a chain of null-checks through the
                // goal target) only runs for goals that pass both. Kept
                // ahead of HasActiveOp so a burning target still interrupts
                // an in-flight haul op, same as before this reordering.
                if (disabledJobs != null && disabledJobs.Contains(goal.JobIdH)) {
                    goal.Flags &= ~AIGoalFlags.NanobotWorkable;
                    continue;
                }
                if (isAnyFlammableOnFire && AIGoal.IsBlockedByFire(goal)) {
                    goal.Flags &= ~AIGoalFlags.NanobotWorkable;
                    continue;
                }
                if (haulingController.HasActiveOp(goal)) {
                    goal.Flags |= AIGoalFlags.NanobotWorkable;
                    continue;
                }
                if (!goal.IsAvailabilityGateOpen(S)) {
                    goal.Flags &= ~AIGoalFlags.NanobotWorkable;
                    continue;
                }
                if (!AIGoal.IsActive(goal)
                        && !TryRecoverFailedNanobotHaulingGoal(goal, ticks)) {
                    goal.Flags &= ~AIGoalFlags.NanobotWorkable;
                    continue;
                }
                if (haulingController.TryCreateWorkItem(goal, out var haulItem)) {
                    nanobotWorkItems[haulItem.Id] = haulItem;
                    goal.Flags &= ~AIGoalFlags.NanobotWorkable;
                } else if (goal.JobIdH == JobTypeIdH.Hauling) {
                    goal.Flags &= ~AIGoalFlags.NanobotWorkable;
                } else if (goal.Target is INanobotCompatible nanoComp) {
                    var eId = nanoComp.EntityId;
                    nanobotWorkItems[eId] = new NanobotDirectWorkItem(goal,
                        nanoComp);
                    goal.Flags &= ~AIGoalFlags.NanobotWorkable;
                } else {
                    D.Err("Goal marked as nanobot compatible but target does not implement INanobotCompatible: {0} -> {1}",
                        goal, goal.Target);
                }
            }

            if (nanobotWorkItems.Count == 0) { return; }

            if (workingHives.Count == 0) { return; }

            var targets = new NativeArray<RangeCheckTarget>(nanobotWorkItems.Count,
                Allocator.TempJob);
            var scanners = new NativeArray<RangeCheckScanner>(workingHives.Count,
                Allocator.TempJob);

            var hits = new NativeQueue<RangeCheckHit>(Allocator.TempJob);

            for (int i = workingHives.Count - 1; i >= 0; i--) {
                var hub = workingHives[i];
                scanners[i] = new RangeCheckScanner() {
                    Id = hub.EntityId,
                    Position = hub.Entity.Position,
                    RangeSqr = hub.RangeSqr,
                    ErrorRadius = 1f,
                };
            }

            var tIdx = 0;
            foreach (var workItem in nanobotWorkItems) {
                targets[tIdx++] = new RangeCheckTarget() {
                    Id = workItem.Key,
                    Position = workItem.Value.Position,
                };
            }

            var job = new RangeCheckMultiJob() {
                Targets = targets,
                Scanners = scanners,
                OutHits = hits.AsParallelWriter(),
            };
            JobHandle dependency = new();
            var handle = job.ScheduleParallel(nanobotWorkItems.Count, 1, dependency);
            handle.Complete();
            targets.Dispose();
            scanners.Dispose();

            closestHits.Clear();
            while (hits.TryDequeue(out var hit)) {
                if (hit.TargetId == 0) { continue; }
                if (!nanobotWorkItems.TryGetValue(hit.TargetId,
                        out var workItem)) {
                    continue;
                }
                if (!workingHivesById.TryGetValue(hit.ScannerId, out var hive)) {
                    continue;
                }
                // Every work item enters this loop with NanobotWorkable
                // cleared, so a set flag means an earlier hit already proved
                // the goal reachable. If this hive also has a strictly closer
                // accepted hit, this hit cannot change anything - skip the
                // reachability resolution entirely.
                if (workItem.Goal.IsNanobotWorkable
                        && closestHits.TryGetValue(hit.ScannerId,
                            out var accepted)
                        && accepted.SqrDist < hit.SqrDist) {
                    continue;
                }
                if (!workItem.CanHiveReach(hive)) { continue; }
                workItem.Goal.Flags |= AIGoalFlags.NanobotWorkable;
                if (!workItem.CanHiveWork(hive)) { continue; }
                // check if we already have a closer hit
                var sqDist = hit.SqrDist;
                if (closestHits.TryGetValue(hit.ScannerId, out var closest)) {
                    if (closest.SqrDist < sqDist) { continue; }
                }
                // reassign closest hit
                closestHits[hit.ScannerId] = hit;
            }
            hits.Dispose();
            for (int i = workingHives.Count - 1; i >= 0; i--) {
                var hub = workingHives[i];
                if (closestHits.TryGetValue(hub.EntityId, out var hit)) {
                    var workItem = nanobotWorkItems[hit.TargetId];
                    PerformWorkOn(ticks, workItem, hub);
                }
            }
            closestHits.Clear();

        }

        private bool TryRecoverFailedNanobotHaulingGoal(AIGoal goal,
                long ticks) {
            if (goal.JobIdH != JobTypeIdH.Hauling) { return false; }
            if (!AIGoal.IsFailed(goal) || goal.NonRetryable) { return false; }
            if (goal.Target?.Entity?.IsActive != true) { return false; }
            if (!goal.IsAvailabilityGateOpen(S)) { return false; }
            if (!IsJobEnabled(goal.JobIdH)) { return false; }
            if (haulingController.CanAnyHiveReachFailedGoal(goal,
                    workingHivesById, workingHivesByAreaKey)) {
                return AIGoal.TryRetry(goal, ticks, true, false);
            }
            return false;
        }

        private void ClearWorkingHiveAreaBuckets() {
            workingHivesByAreaKey.Clear();
            usedHiveLists = 0;
        }

        private void AddWorkingHiveToAreaBucket(NanobotHiveComp hub) {
            var areaKey = NanobotReachability.AreaKeyAt(S,
                hub.Entity.PosIdx);
            if (!workingHivesByAreaKey.TryGetValue(areaKey, out var bucket)) {
                if (usedHiveLists == hiveListCache.Count) {
                    hiveListCache.Add(new List<NanobotHiveComp>());
                }
                bucket = hiveListCache[usedHiveLists++];
                bucket.Clear();
                workingHivesByAreaKey[areaKey] = bucket;
            }
            bucket.Add(hub);
        }

        private void ClearNanobotWorkableFlags() {
            foreach (var goal in S.Sys.AI.ColonyGoals.Values) {
                if (goal.IsNanobotCompatible
                        && !haulingController.HasActiveOp(goal)) {
                    goal.Flags &= ~AIGoalFlags.NanobotWorkable;
                }
            }
        }

        private void PerformWorkOn(long ticks, INanobotWorkItem workItem,
                NanobotHiveComp hive) {
            // D.Warn("NanobotHive {0} working on {1}", hive.Tile, workItem.Entity);
            var entity = workItem.Entity;
            workItem.AddWork(hive, ticks, out var goal);
            if (goal == null) { return; }
            entity = workItem.Entity ?? entity;
            if (hive.ShowParticles && hive.Tile.IsActive && entity?.IsActive == true
                    && A.IsCosmeticParticlesOn) {
                hive.ShowParticles = false;
                var p1 = hive.Tile.Position;
                var p2 = entity.Position;
                var size = workItem.Size;
                if (size.x < 1f || size.y < 1f) {
                    size = Consts.V2One;
                }
                if (goal.JobIdH == JobTypeIdH.Hauling
                        && haulingController.HasActiveOp(goal)) {
                    pendingParticles.Enqueue(new NanobotFX(hive.Tile,
                        entity, size));
                } else {
                    pendingParticles.Enqueue(new NanobotFX(
                        EntityUtils.ClampToBounds(p1),
                        EntityUtils.ClampToBounds(p2),
                        size));
                }
            }
        }

        private void OnRegisterHive(NanobotHiveComp hub, bool added) {
            // D.Warn("Register hub: {0}->{1}", added, hub);
            if (added) {
                if (!hub.Tile.IsConstructed) {
                    D.Err("Trying to add (register) an unconstructed hub! {0}", hub.Tile);
                    return;
                }
                hubs.Add(hub);
            } else {
                hubs.Remove(hub);
            }
        }
        private ComponentData data;
        private ComponentData Data => data ??= new(0, GetName());

        public ComponentData Save() {
            if (disabledJobs == null && !haulingController.HasActiveOps) {
                return null;
            }
            if (disabledJobs != null) {
                Data.SetIntSet("DisabledJobs", disabledJobs.ToArray());
            } else {
                Data.RemoveIntSet("DisabledJobs");
            }
            haulingController.Save(Data);
            return Data;
        }

        public void Load(ComponentData data) {
            this.data = data;
            haulingController.Load(data);
            if (data != null) {
                var disabled = data.GetIntSet("DisabledJobs", null);
                if (disabled != null && disabled.Length > 0) {
                    disabledJobs ??= new();
                    disabledJobs.AddRange(disabled);
                }
            }
        }
    }
}
