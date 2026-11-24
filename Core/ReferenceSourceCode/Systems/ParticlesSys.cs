#if !PRODUCTION_BUILD
// #define DISABLE_ENERGY
// #define VALIDATE_PARTICLES
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using Game.Components;
using Game.Components.Upgrades;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Clock;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using Priority_Queue;
using Game.Rendering;
using UnityEngine;
using System.Runtime.CompilerServices;
using System.Linq;
using KL.Grid;
using KL.Pool;
using Game.UI;
using Game.Systems.UIUX;
using Unity.Mathematics;
using System.Diagnostics;

namespace Game.Systems {
    public sealed class ParticlesSys : GameSystem {
        public sealed class Data {
            // Emit only particles have 1 shader particle system
            // and they just emit periodically with given EmitParams
            // Non emit only will create a dedicated particle system and
            // potentially if it's not static, it will update the position
            // every frame (think drone trails)
            public bool IsEmitOnly;
            public bool IsStatic;
            public Entity Owner;
            public ParticleSystem.EmitParams EmitParams;
            public float EmitChance;
            public bool CanHaveZeroEmitChance;
            public Vector2 Position;
            public Vector2 Offset;
            public bool InheritScale;
            public float DestroyDelay;
            public string PrefabName = "UNKNOWN PARTICLE SYSTEM";
            public bool Play;
            public bool IsPlaying;

            #if VALIDATE_PARTICLES
            public void Validate(Particles part) {
                if (IsEmitOnly || !IsPlaying) {
                    return;
                }
                if (part.Fx == null) { return; }
                var ppos = (Vector2) part.Fx.transform.position;
                if (Vectors.SqrDistance(Owner.Position, ppos) > 3f) {
                    D.Err("Particles are mispositioned! {0} | {1} -> {2}",
                        Owner, Owner.Position, ppos);
                }
            }
            #endif
            public override string ToString() {
                return $"ParticleSys.Data[{PrefabName}/P:{Play}/{IsPlaying}]";
            }
        }
        private bool isDisabled;
        private bool isRendering;
        private readonly HashSet<IVFXParticles> customParticles = new();
        internal readonly HashSet<Data> particles = new();
        internal readonly List<Data> trails = new();
        private readonly HashSet<DamageableComp> damagedDevices = new();
        private readonly HashSet<HullIntegrityComp> leaksAtmosphere = new();
        internal readonly Dictionary<string, Particles> EmitInstances = new();
        internal readonly Dictionary<Data, Particles> ParticleInstances = new();
        internal readonly Dictionary<Data, Trails> TrailInstances = new();
        private readonly List<Data> orphanParticles = new();
        public const string SysId = "ParticlesSys";
        public override string Id => SysId;
        public readonly Dictionary<int, XParticles> X = new();
        //public EnergyVFX EnergyVFX;
        public ElectricityVFX ElectricityVFX;
        public SelectionVFX SelectionVFX;
        public GuidelinesVFX GuidelinesVFX;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ParticlesSys());
        }

        public void AddCustomParticles(IVFXParticles particles) {
            customParticles.Add(particles);
        }

        public void RemoveCustomParticles(IVFXParticles particles) {
            customParticles.Remove(particles);
        }

        public void DestroyCustomParticles() {
            foreach (var p in customParticles) {
                p.Destroy();
            }
            customParticles.Clear();
        }

        protected override void OnInitialize() {
            CheckEnabled();
            S.Sig.AfterLoad.AddListener(OnAfterLoad);
            S.Sig.ToggleRendering.AddListener(OnToggleRendering);
            S.Sig.WinchEnded.AddListener(OnWinchEnded);
        }

        // Ambient device FX (oxygen pumps, heaters, hull leaks, sleep/heal FX, etc.)
        // are shared, emit-and-forget world-space particles for performance: moving
        // the emitter only affects future emissions (see UpdatePosIdx overrides),
        // already-emitted particles keep drifting from their old spawn point for
        // their remaining lifetime. Rather than rearchitecting into per-entity
        // particle systems, hard-clear the shared pools once a move completes.
        private void OnWinchEnded(bool moved) {
            if (!moved) { return; }
            foreach (var p in EmitInstances) {
                if (p.Value.Fx != null) {
                    p.Value.Fx.Clear(true);
                }
            }
        }

        private void OnToggleRendering(bool on) {
            isRendering = on;
        }

        private void OnAfterLoad() {
            X[ParticlesIdH.MouseRect] = new XParticles("SelectionRect", 1);

            var bounds = S.Grid.Bounds;

            SelectionVFX = new SelectionVFX(S, bounds);
            #if !DISABLE_ENERGY
            ElectricityVFX = new ElectricityVFX(S, bounds);
            ElectricityVFX.SelVFX = SelectionVFX;
            #endif
            GuidelinesVFX = new GuidelinesVFX(S, bounds);

            S.Clock.OnUpdate60.AddListener(UpdateAll);
            S.Clock.OnTick.AddListener(OnTick);
        }

        public void AddLeak(HullIntegrityComp leak) {
            leaksAtmosphere.Add(leak);
        }

        public void RemoveLeak(HullIntegrityComp leak) {
            var p = leak.LeakParticles;
            if (p != null) {
                particles.Remove(p);
            }
            leaksAtmosphere.Remove(leak);
        }

        public void AddDamaged(DamageableComp damaged) {
            if (isDisabled) { return; }
            damagedDevices.Add(damaged);
            if (damaged.SparkParticles == null) {
                damaged.SparkParticles = new Data() {
                    IsEmitOnly = true,
                    EmitChance = 0.5f,
                    Owner = damaged.Entity,
                    Position = damaged.Entity.Position,
                    IsStatic = true,
                    PrefabName = "DamagedDeviceFX",
                };
                AddParticles(damaged.SparkParticles);
            }
            damaged.SparkParticles.Play = true;
        }

        public void RemoveDamaged(DamageableComp damaged) {
            var p = damaged.SparkParticles;
            if (p != null) {
                particles.Remove(p);
            }
            damagedDevices.Remove(damaged);
        }

        public void AddParticles(Data data) {
            if (isDisabled) { return; }
            //D.Log("Adding particle: {0}", data);

            if (data.Owner.PosIdx < 1 || data.Owner.Position == Consts.V2Zero) {
                D.Err("Adding particles when owner is at 0! {0}. Pos: {1}",
                    data.Owner, data.Owner.PosIdx);
            }
            if (data.IsEmitOnly) {
                if (!data.CanHaveZeroEmitChance && data.EmitChance < 0.0000001f) {
                    D.Err("Adding particles with 0 emit chance! {0}. Changing to 1", data.PrefabName);
                    data.EmitChance = 1f;
                }
                if (!EmitInstances.TryGetValue(data.PrefabName, out var instance)) {
                    var p = Particles.Create("MultiParticleRootFX", Rng.UUint,
                        Consts.V3Zero, autoplay: false);
                    if (p?.Fx == null) {
                        return;
                    }
                    var prefab = ResourceLibrary.FindPrefab<ParticleSystem>(data.PrefabName);
                    if (prefab == null) {
                        WPopup.Create(true, IconId.CWarning, "CONTENT CONFIGURATION ERROR", $"Particle system named {data.PrefabName} not found for {data.Owner.DefinitionId}").Spawn();
                        p.Destroy();
                        return;
                    }
                    var subP = PrefabPool.Spawn(prefab, Consts.V3Zero, Consts.QId, p.Fx.transform);
                    var main = subP.main;
                    main.loop = false;
                    main.playOnAwake = false;
                    p.GameObject.isStatic = true;
                    var sub = p.Fx.subEmitters;
                    if (sub.subEmittersCount > 0) {
                        sub.RemoveSubEmitter(0);
                    }
                    sub.AddSubEmitter(subP, ParticleSystemSubEmitterType.Birth,
                        ParticleSystemSubEmitterProperties.InheritNothing, 1);
                    sub.enabled = true;
                    p.SetClock(S.Clock);
                    EmitInstances.Add(data.PrefabName, p);
                }
                var pars = new ParticleSystem.EmitParams {
                    position = data.Owner.Position + data.Offset,
                };
                data.EmitParams = pars;
                particles.Add(data);
            } else {
                var p = Particles.Create(data.PrefabName, Rng.UUint,
                    data.Owner.Position + data.Offset, autoplay: false);
                if (p?.Fx == null) {
                    return;
                }
                if (data.IsStatic) {
                    p.GameObject.isStatic = true;
                }
                float scale = data.InheritScale ? data.Owner.Size.x : 1f;
                p.Fx.transform.localScale = Consts.V3One * scale;
                p.SetClock(S.Clock);
                ParticleInstances.Add(data, p);
                particles.Add(data);
            }
        }

        public void AddTrails(Data data) {
            if (isDisabled) { return; }
            var trail = Trails.Create(data.PrefabName,
                data.Owner.Position + data.Offset);
            if (trail == null) {
                return;
            }
            trails.Add(data);
            TrailInstances.Add(data, trail);
        }
        public void RemoveParticles(Data data) {
            if (data == null) { return; }
            //D.Log("Removing particle: {0}", data);
            RemoveParticleInstance(data);
            particles.Remove(data);
        }

        public void MoveParticles(Data data, float dx, float dy) {
            if (data == null) { return; }
            data.Position += new Vector2(dx, dy);
            data.EmitParams.position += new Vector3(dx, dy, 0);
            if (ParticleInstances.TryGetValue(data, out var p)) {
                p.MoveBy(new Vector3(dx, dy, 0));
            }
        }

        private void RemoveParticleInstance(Data data) {
            if (data == null) { return; }
            particles.Remove(data);
            data.IsPlaying = false;
            if (ParticleInstances.TryGetValue(data, out var p)) {
                if (data.DestroyDelay > 0) {
                    p.DestroyAfter(data.DestroyDelay);
                } else {
                    p.Destroy();
                }
                ParticleInstances.Remove(data);
            }
        }
        public void RemoveTrails(Data data) {
            if (data == null) { return; }
            trails.Remove(data);
            if (TrailInstances.TryGetValue(data, out var trail)) {
                if (data.DestroyDelay > 0) {
                    trail.DestroyAfter(S, data.DestroyDelay);
                } else {
                    trail.Destroy();
                }
                TrailInstances.Remove(data);
            }
        }

        private float nextParticleTime;
        private float nextLeakTime;

        private void OnTick(long ticks) {
            if (UIShowing.FullScreenWidget) { return; }
            #if VALIDATE_PARTICLES
            foreach (var p in ParticleInstances) {
                p.Key.Validate(p.Value);
                if (!particles.Contains(p.Key)) {
                    D.Err("Particle unregister but instance is here! {0}", p.Key.Owner, p.Value.Fx?.transform.position);
                }
            }
            #endif
            if (isDisabled || !isRendering) { return; }
            var t = Time.time;
            if (nextParticleTime < t) {
                // every 2 sec in scaled time we want to fire particles
                var ts = S.Clock.FXTimeScale;
                if (ts > 0.0001f) {
                    // Every 2 seconds in realtime scaled by FXTimeScale we trigger the particle effects
                    ts = 2f / ts;
                }
                nextParticleTime = t + ts;

                foreach (Data p in particles) {
                    if (p.IsEmitOnly && p.Play && Rng.UChance(p.EmitChance)) {
                        if (EmitInstances.TryGetValue(p.PrefabName, out var sys)) {
                            sys.Fx.Emit(p.EmitParams, 1);
                        }
                    }
                }
            } else if (nextLeakTime < t) {
                nextLeakTime = t + 2.32f;
                ProcessLeaks();
            }
        }

        private void ProcessLeaks() {
            var time = Time.realtimeSinceStartupAsDouble;
            const double budget = 1d/60d;
            foreach (var l in leaksAtmosphere) {
                var p = l.PosIdx;
                var oxy = S.GetO2(p);
                var n = Pos.N(p);
                var e = Pos.E(p);
                var s = Pos.S(p);
                var w = Pos.W(p);
                var oxyN = S.GetO2(n);
                var oxyE = S.GetO2(e);
                var oxyS = S.GetO2(s);
                var oxyW = S.GetO2(w);
                var max = oxy;
                Facing.Type dir = Facing.Type.Down;
                if (oxyN > max) {
                    max = oxyN;
                    dir = Facing.Type.Down;
                }
                if (oxyE > max) {
                    max = oxyE;
                    dir = Facing.Type.Left;
                }
                if (oxyS > max) {
                    max = oxyS;
                    dir = Facing.Type.Up;
                }
                if (oxyW > max) {
                    max = oxyW;
                    dir = Facing.Type.Right;
                }
                if (l.LayerId == WorldLayer.Floor) {
                    if (max > 0.03f) {
                        // Show Leak
                        LeakParticles(l, dir).Play = true;
                    } else if (l.LeakParticles != null) {
                        // Hide leak;
                        l.LeakParticles.Play = false;
                    }
                } else if (l.LayerId == WorldLayer.Walls) {
                    var min = oxy;
                    var isWallN = EntityUtils.IsAirtightWallAt(n);
                    var isWallE = EntityUtils.IsAirtightWallAt(e);
                    var isWallS = EntityUtils.IsAirtightWallAt(s);
                    var isWallW = EntityUtils.IsAirtightWallAt(w);
                    if (!isWallN && oxyN < min) { min = oxyN; }
                    if (!isWallE && oxyE < min) { min = oxyE; }
                    if (!isWallS && oxyS < min) { min = oxyS; }
                    if (!isWallW && oxyW < min) { min = oxyW; }
                    if (Math.Abs(min - max) > 0.02f) {
                        // Show Leak
                        LeakParticles(l, dir).Play = true;
                    } else if (l.LeakParticles != null) {
                        // Hide leak;
                        l.LeakParticles.Play = false;
                    }
                }
                if (Time.realtimeSinceStartupAsDouble - time > budget) {
                    #if UNITY_EDITOR
                    if (!System.Diagnostics.Debugger.IsAttached) {
                        break;
                    }
                    #else
                    break;
                    #endif
                }
            }
        }

        private string LeakPrefab(HullIntegrityComp l, Facing.Type dir) {
            if (l.LayerId == WorldLayer.Floor) {
                var a = l.Tile.Health.HealthPercent;
                if (a < 0.33f) {
                    return "OxygenLeak3FX";
                } else if (a < 0.66f) {
                    return "OxygenLeak2FX";
                }
                return "OxygenLeak1FX";
            } else if (l.LayerId == WorldLayer.Walls) {
                switch (dir) {
                    case Facing.Type.Down: return "OxygenLeakFX_D";
                    case Facing.Type.Left: return "OxygenLeakFX_L";
                    case Facing.Type.Right: return "OxygenLeakFX_R";
                    case Facing.Type.Up: return "OxygenLeakFX_U";
                }
            }
            // fallback
            return "OxygenLeak1FX";
        }

        private Data LeakParticles(HullIntegrityComp l, Facing.Type dir) {
            var requiredPrefab = LeakPrefab(l, dir);
            var lp = l.LeakParticles;
            if (lp != null) {
                if (lp.PrefabName == requiredPrefab) {
                    lp.EmitChance = l.ParticleEmitChance;
                    return lp;
                }
                particles.Remove(lp);
            }
            switch (l.LayerId) {
                case WorldLayer.Walls:
                case WorldLayer.Floor: {
                    l.LeakParticles = new Data() {
                        IsEmitOnly = true,
                        CanHaveZeroEmitChance = true,
                        EmitChance = l.ParticleEmitChance,
                        Owner = l.Entity,
                        Position = l.Entity.Position,
                        PrefabName = requiredPrefab,
                        IsStatic = true,
                    };
                    AddParticles(l.LeakParticles);
                    return l.LeakParticles;
                }
            }
            D.Err("Hull integrity but no on wall or floor! {0}", l.Entity);
            return null;
        }

        private void UpdateAll() {
            UpdateX();
            if (!isRendering || isDisabled) { return; }
            foreach (Data p in particles) {
                if (!p.IsEmitOnly) {
                    if (!ParticleInstances.TryGetValue(p, out var inst)) {
                        orphanParticles.Add(p);
                        continue;
                    }
                    if (p.Play && !p.IsPlaying) {
                        inst.Play(true);
                        p.IsPlaying = true;
                    } else if (!p.Play && p.IsPlaying) {
                        inst.Play(false);
                        p.IsPlaying = false;
                    }
                    if (p.IsPlaying && !p.IsStatic) {
                        inst.MoveTo(p.Owner.Position + p.Offset);
                        if (p.InheritScale) {
                            inst.ScaleTo(p.Owner.Size.x);
                        }
                    }
                }
                // TODO culling
                //RenderUtils.IsVisibleSafe()
            }
            if (orphanParticles.Count > 0) {
                foreach (var p in orphanParticles) {
                    particles.Remove(p);
                }
                orphanParticles.Clear();
            }
            for (int i = trails.Count - 1; i >= 0; i--) {
                Data p = trails[i];
                var inst = TrailInstances[p];
                if (p.Play && !p.IsPlaying) {
                    p.IsPlaying = true;
                    inst.Play(true);
                } else if (!p.Play && p.IsPlaying) {
                    p.IsPlaying = false;
                    inst.Play(false);
                }
                if (p.IsPlaying) {
                    inst.MoveTo(p.Owner.Position + p.Offset);
                    if (p.InheritScale) {
                        inst.ScaleTo(p.Owner.Size.x);
                    }
                }
            }
        }

        [Conditional("UNITY_EDITOR")]
        private static void ValidateParticle(Vector2 pos) {
            if (pos.sqrMagnitude < 1) {
                D.Err("Placing particle at zero coordinates!");
            }
        }

        //[Obsolete("Migrate to VFX")]
        public static ParticleSystem.Particle ParticleAt(Vector2 pos, Color color,
                float size, float life) {
            ValidateParticle(pos);
            return new ParticleSystem.Particle() {
                position = pos,
                startLifetime = life,
                remainingLifetime = life,
                startColor = color,
                startSize = size,
            };
        }
        //[Obsolete("Migrate to VFX")]
        public static ParticleSystem.Particle ParticleAt(Vector2 pos, Color color,
                Vector2 size, float life) {
            ValidateParticle(pos);
            return new ParticleSystem.Particle() {
                position = pos,
                startLifetime = life,
                remainingLifetime = life,
                startColor = color,
                startSize3D = new(size.x, size.y, 1f),
            };
        }

        private void UpdateX() {
            var tool = S.Toolbox.ActiveTool;
            tool.AddParticles(X);
            tool.UpdateVFX(this);
        }

        private void ReleaseParticles() {
            foreach (var p in X) {
                p.Value.Cleanup();
            }
            DestroyCustomParticles();
            X.Clear();
            //EnergyVFX?.Destroy();
            ElectricityVFX?.Destroy();
            SelectionVFX?.Destroy();
            GuidelinesVFX?.Destroy();
            // destroy all particles
            foreach (var p in ParticleInstances) {
                p.Value.Destroy();
            }
            ParticleInstances.Clear();
            foreach (var p in EmitInstances) {
                p.Value.Destroy();
            }
            EmitInstances.Clear();
            foreach (var p in TrailInstances) {
                p.Value.Destroy();
            }
            TrailInstances.Clear();
            particles.Clear();
            trails.Clear();
            leaksAtmosphere.Clear();
            damagedDevices.Clear();
        }

        private void CheckEnabled() {
            isDisabled = RenderUtils.DisableCosmeticVFX;
            isRendering = !isDisabled;
        }

        public override void Unload() {
            ReleaseParticles();
        }

        public string GetName() {
            return Id;
        }
    }
}
