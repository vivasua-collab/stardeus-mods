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

namespace Game.Systems {
    public sealed class AudioSys : GameSystem {
        public sealed class AmbienceData {
            public string Owner;
            public Vector2 Position;
            public int Id;
            public string ClipId = "UninitializedAudioClip";
            public Vector2 Rolloff;
            public bool Play;
            public float Pitch = 1f;
            public float Volume = 1f;
        }
        internal readonly List<AmbienceData> Ambiences = new();
        internal readonly Dictionary<int, GameAudioSource> AmbienceAudioSources = new();
        internal readonly Dictionary<int, List<AmbienceData>> GroupedAmbiences = new();

        public const string SysId = "AudioSys";
        public override string Id => SysId;


        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new AudioSys());
        }

        private Vector2 listenerPosition;
        public int PlayingCount;

        internal bool IsUpdateRequired;
        internal void UpdateSources() {
            listenerPosition = RenderUtils.CameraPosition;
            PlayingCount = 0;
            foreach (var kv in AmbienceAudioSources) {
                var closestAmbience = FindClosestAmbienceFor(kv.Key);
                var audio = kv.Value;
                // There are no ambiences playing this clip right now
                if (closestAmbience == null) {
                    audio.Pause();
                } else {
                    audio.Unpause();
                    PlayingCount++;
                    audio.MoveTo(closestAmbience.Position);
                }
            }
            IsUpdateRequired = false;
            #if !PRODUCTION_BUILD
            if (Time.frameCount % 3435 == 0) {
                VerifyPlayingIntegrity();
            }
            #endif
        }

        internal void VerifyPlayingIntegrity() {
            int actuallyPlayingCount = 0;
            foreach (var audio in AmbienceAudioSources) {
                if (audio.Value.Audio.isPlaying) {
                    actuallyPlayingCount++;
                }
            }
            if (PlayingCount != actuallyPlayingCount) {
                D.Err("Audio System integrity check failure - playing {0}, actually playing {1}. Playing right now: {2}",
                    PlayingCount, actuallyPlayingCount, D.Dump(AmbienceAudioSources
                        .Values.Where(s => s.Audio.isPlaying)
                            .Select(s => s.Audio.name).ToArray()));
            }
        }

        private bool CloseEnough(float dist) {
            // 2 squared
            return dist < 4;
        }

        private AmbienceData FindClosestAmbienceFor(int id) {
            var clipAmbs = GroupedAmbiences[id];
            AmbienceData closest = null;
            var closestDist = float.MaxValue;
            for (int i = clipAmbs.Count - 1; i >= 0; i--) {
                AmbienceData amb = clipAmbs[i];
                if (!amb.Play) { continue; }
                var dist = DistToListener(amb.Position);
                if (dist < closestDist) {
                    closest = amb;
                    closestDist = dist;
                    if (CloseEnough(closestDist)) {
                        return closest;
                    }
                }
            }
            return closest;
        }

        private float DistToListener(Vector2 pos) {
            return Vectors.SqrDistance(pos, listenerPosition);
        }

        public void AddAmbience(AmbienceData ambience) {
            // 1. see if we already have this clip for this type of ambience
            if (!AmbienceAudioSources.TryGetValue(ambience.Id, out var audio)) {
                audio = CreateClip(ambience);
                AmbienceAudioSources.Add(ambience.Id, audio);
            }

            if (!GroupedAmbiences.TryGetValue(ambience.Id, out var group)) {
                group = new();
                GroupedAmbiences.Add(ambience.Id, group);
            }
            group.Add(ambience);
            Ambiences.Add(ambience);
            IsUpdateRequired = true;
        }

        private GameAudioSource CreateClip(AmbienceData data) {
            var audioSource = GameAudioSource.Create("AudioSourceAmbience", data.Position);
            var a = audioSource.Audio;
            a.clip = The.Sounds.Find(data.ClipId);
            a.name = string.Format("Ambience: {0} => {1}", data.Owner, data.ClipId);
            a.loop = true;
            a.playOnAwake = false;
            a.minDistance = data.Rolloff.x;
            a.maxDistance = data.Rolloff.y;
            a.volume = data.Volume;
            a.pitch = data.Pitch;
            a.Play();
            a.Pause();
            return audioSource;
        }

        public void RemoveAmbience(AmbienceData ambience) {
            if (GroupedAmbiences.TryGetValue(ambience.Id, out var group)) {
                group.Remove(ambience);
            }
            Ambiences.Remove(ambience);
            IsUpdateRequired = true;
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadState);
        }

        private void OnLoadState(GameState state) {
            state.Clock.OnUpdate60.AddListener(UpdateSources);
        }

        public override void Unload() {
            Ambiences.Clear();
            GroupedAmbiences.Clear();
            AmbienceAudioSources.Clear();
        }

        public string GetName() {
            return "AudioSys";
        }
    }

}
