using Game.Data;
using KL.Utils;
using UnityEngine;
using Game.Systems;
using Game.CodeGen;

using Game.Utils;
namespace Game.Components {
    public sealed class AudioSourceComp : BaseComponent<AudioSourceComp> {
        public bool IsPlaying => data.Play;
        private AudioSys.AmbienceData data;
        private bool isAdded;
        public string AmbienceClip;
        public bool IsAutoplay { get; private set; }
        public bool IsLinkedToEnergy { get; private set; }
        public float MinEnergy;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new AudioSourceComp());
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
                S.Sys.Audio.RemoveAmbience(data);
            }
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            data.Position += new Vector2(dx, dy);
        }

        public override void Receive(IComponent sender, int message) {
            if (IsAutoplay && message == MsgIdH.ConstructionFinished) {
                Play();
            }
        }
        protected override void OnConfig() {
            var clip = Config.GetString(PropertyIdH.AmbienceClip, null);
            AmbienceClip = clip;
            var rolloff = Config.GetVector2(PropertyIdH.Rolloff, new Vector2(3, 15));
            var vol = Config.GetFloat(PropertyIdH.Volume, 1f);
            IsAutoplay = Config.GetBool(PropertyIdH.IsAutoplay, false);
            IsLinkedToEnergy = Config.GetBool(PropertyIdH.IsLinkedToEnergy, true);
            if (IsLinkedToEnergy) {
                MinEnergy = Config.GetFloat(PropertyIdH.MinEnergy, 0f);
            }
            var pitch = Config.GetFloat(PropertyIdH.Pitch, 1f);
            var idHash = Hashes.S(clip);
            idHash = Hashes.Quick(idHash, Mathf.RoundToInt(rolloff.x * 1235));
            idHash = Hashes.Quick(idHash, Mathf.RoundToInt(rolloff.y * 32145));
            idHash = Hashes.Quick(idHash, Mathf.RoundToInt(vol * 351));
            data = new AudioSys.AmbienceData() {
                Owner = Entity.DefinitionId,
                Id = idHash,
                ClipId = clip,
                Rolloff = rolloff,
                Volume = vol,
                Pitch = pitch,
            };
        }

        public override void OnLateReady(bool wasLoaded) {
            data.Position = Entity.Position;
            data.Play = IsAutoplay && Tile?.IsConstructed != false;
            if (!isAdded) {
                isAdded = true;
                S.Sys.Audio.AddAmbience(data);
            }
        }

        public override string ToString() {
            return $" * AudioSource [{data.ClipId}]";
        }
    }
}
