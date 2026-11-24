using Game.Data;
using Game.Rendering;
using UnityEngine;
using Game.Systems;
using Game.CodeGen;

using Game.Utils;
namespace Game.Components {
    public sealed class ParticlesComp : BaseComponent<ParticlesComp> {
        public bool IsPlaying => data.Play;
        private ParticlesSys.Data data;
        private bool isAdded;
        private Vector2[] particleOffsets;
        public bool IsAutoplay { get; private set; }

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ParticlesComp());
        }
        public void Play() {
            data.Play = true;
        }
        public void Stop() {
            data.Play = false;
        }
        public override void OnRemove() {
            if (isAdded) {
                isAdded = false;
                S.Sys.Particles.RemoveParticles(data);
            }
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            if (isAdded) {
                S.Sys.Particles.MoveParticles(data, dx, dy);
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (IsAutoplay && message == MsgIdH.ConstructionFinished) {
                Play();
            }
        }

        protected override void OnConfig() {
            IsAutoplay = Config.GetBool(PropertyIdH.IsAutoplay, false);
            var particlesName = Config.GetString(PropertyIdH.Particles);
            if (particlesName != null) {
                particleOffsets = Config.GetVector2Set(PropertyIdH.Offsets, null);
                data = new ParticlesSys.Data() {
                    Owner = Entity,
                    IsStatic = true,
                    PrefabName = particlesName,
                    Play = IsAutoplay,
                };
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            if (RenderUtils.DisableAllVFX) { return; }
            data.Position = Entity.Position;
            data.Play = IsAutoplay && Tile.IsConstructed;
            if (!isAdded) {
                isAdded = true;
                if (particleOffsets != null) {
                    data.Offset = particleOffsets[(int) Tile.Transform.Rotation];
                }
                S.Sys.Particles.AddParticles(data);
            }
        }

        public override string ToString() {
            return $" * Particles [{data.PrefabName}]";
        }
    }
}
