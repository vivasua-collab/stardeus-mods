using Game.Constants;
using Game.Data;
using Game.Systems;
using Game.Rendering;
using KL.Utils;
using Game.CodeGen;

using Game.Utils;
namespace Game.Components {
    public sealed class ParticleTrailComp : BaseComponent<ParticleTrailComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ParticleTrailComp());
        }
        public ParticlesSys.Data Particles;
        public ParticlesSys.Data Trails;
        private bool inheritScale;
        private bool delayStart;
        private bool removeWhenStopped;

        public void SetParticle(string prefab) {
            D.Log("Changing particle: {0} -> {1}", Particles?.PrefabName, prefab);
            if (isParticlesAdded) {
                RemoveParticle();
                Particles.PrefabName = prefab;
                AddParticle();
            } else {
                Particles.PrefabName = prefab;
            }
        }

        protected override void OnConfig() {
            var p = Config.GetString(PropertyIdH.Particles, null);
            inheritScale = Config.GetBool(PropertyIdH.InheritScale, false);
            var destroyDelay = Config.GetFloat(PropertyIdH.DestroyDelay, 0);
            var trailDestroyDelay = Config.GetFloat(PropertyIdH.DestroyDelay, 15f);
            delayStart = Config.GetBool(PropertyIdH.DelayStart, true);
            removeWhenStopped = Config.GetBool(PropertyIdH.RemoveWhenStopped, true);

            if (p != null) {
                var o = Config.GetVector2(PropertyIdH.ParticlesOffset, Consts.V2Zero);
                Particles = new ParticlesSys.Data() {
                    Offset = o,
                    Owner = Entity,
                    PrefabName = p,
                    Play = true,
                    DestroyDelay = destroyDelay,
                    InheritScale = inheritScale,
                };
            }

            var t = Config.GetString(PropertyIdH.Trails, null);
            if (t != null) {
                var o = Config.GetVector2(PropertyIdH.TrailsOffset, Consts.V2Zero);
                Trails = new ParticlesSys.Data() {
                    Offset = o,
                    Owner = Entity,
                    PrefabName = t,
                    Play = true,
                    DestroyDelay = trailDestroyDelay,
                    InheritScale = inheritScale,
                };
            }
        }

        private bool isParticlesAdded;
        private bool isTrailsAdded;
        private bool isParticlesDisabled;
        public void DisableParticles() {
            isParticlesDisabled = true;
            RemoveParticle();
            RemoveTrail();
        }

        public void ReenableParticles() {
            if (isParticlesDisabled) {
                isParticlesDisabled = false;
                InitAll();
            }
        }

        private void InitAll() {
            // might disable particles before InitAll executes after it was enqueued in OnLateReady
            if (isParticlesDisabled) { return; }
            AddParticle();
            AddTrail();
        }

        public override void OnLateReady(bool wasLoaded) {
            if (isParticlesDisabled || RenderUtils.DisableAllVFX) { return; }
            if (Being != null) {
                if (Being.Motion.IsImmobilized) {
                    return;
                }
            }
            if (!wasLoaded && delayStart) {
                S.Clock.ExecuteAfterTicks(1, InitAll);
                // never use delayed lambda for this! coroutines will try to
                // execute in same frame and this shit will freeze
                // S.CmdQ.Enqueue(new CmdDelayedLambda(
                //     InitAll, 1, "ParticleTrailComp.OnLateReady", Entity));
            } else {
                S.Clock.OnNextUpdate(InitAll);
            }
        }
        private void AddParticle() {
            if (isParticlesDisabled) { return; }
            if (!isParticlesAdded && Particles != null) {
                // This is through delayed lambda, so it can happen
                if (!Entity.IsActive) { return; }
                isParticlesAdded = true;
                S.Sys.Particles.AddParticles(Particles);
            }
        }

        public void SetHidden() {
            if (isParticlesDisabled) { return; }
            if (Particles != null) {
                if (removeWhenStopped) {
                    RemoveParticle();
                }
                Particles.Play = false;
            }
            if (Trails != null) {
                if (removeWhenStopped) {
                    RemoveTrail();
                }
                Trails.Play = false;
            }
        }

        public void SetVisible() {
            if (isParticlesDisabled) { return; }
            if (Particles != null) {
                if (removeWhenStopped) {
                    AddParticle();
                }
                Particles.Play = true;
            }
            if (Trails != null) {
                if (removeWhenStopped) {
                    AddTrail();
                }
                Trails.Play = true;
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.GraphicHidden
                    || message == MsgIdH.SwitchedOff) {
                SetHidden();
            } else if (message == MsgIdH.GraphicUnhidden
                    || message == MsgIdH.SwitchedOn) {
                SetVisible();
            } else if (message == MsgIdH.HealthWentToZero) {
                RemoveParticle();
                RemoveTrail();
            }
        }

        private void RemoveParticle() {
            if (isParticlesAdded && Particles != null) {
                isParticlesAdded = false;
                S.Sys.Particles.RemoveParticles(Particles);
            }
        }

        public override void OnRemove() {
            if (isParticlesAdded) {
                isParticlesAdded = false;
                S.Sys.Particles.RemoveParticles(Particles);
            }
            RemoveTrail();
        }
        private void AddTrail() {
            if (!isTrailsAdded && Trails != null) {
                isTrailsAdded = true;
                S.Sys.Particles.AddTrails(Trails);
            }
        }

        private void RemoveTrail() {
            if (isTrailsAdded) {
                isTrailsAdded = false;
                S.Sys.Particles.RemoveTrails(Trails);
            }
        }

        public void TrySwitchTo(string from, string to) {
            if (Particles == null) { return; }
            var name = Particles.PrefabName;
            if (string.IsNullOrEmpty(from)) {
                if (!name.EndsWith(to)) {
                    SetParticle($"{name}{to}");
                }
            } else {
                if (name.EndsWith(from)) {
                    name = name.Remove(name.Length - from.Length);
                    if (!string.IsNullOrEmpty(to)) {
                        name = $"{name}{to}";
                    }
                    SetParticle(name);
                }
            }
        }

        public override string ToString() {
            return $"ParticleTrail[{Trails}|{Particles}]";
        }
    }
}
