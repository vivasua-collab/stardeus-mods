using System;
using KL.Clock;
using KL.Randomness;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using Game.Constants;
using Game.Data;
using Game.CodeGen;

namespace Game.Utils {
    public static class Sounds {
        public static void PlayMusic(string mood, bool changeTrackNow = false, float silenceChance = 0f) {
            if (Rng.UChance(silenceChance)) {
                The.SysSig.StopMusic.Send();
            } else {
                The.SysSig.SetMusicMood.Send(mood, changeTrackNow);
            }
        }
        public static void StopMusic() {
            The.SysSig.StopMusic.Send();
        }
        // Fades out whatever plays and holds silence until PlayIntroTrack
        public static void FadeOutForIntro() {
            The.SysSig.FadeOutForIntro.Send();
        }
        // Starts the intro track from the beginning, even if it is already playing
        public static void PlayIntroTrack() {
            The.SysSig.PlayIntroTrack.Send();
        }
        public static void PlayRandomAt(Vector2 pos, string[] options,
                bool useGlobalPitch = false) {
            if (Arrays.IsEmpty(options)) { return; }
            // D.Warn("Playing sound for {0}", D.Dump(options));
            The.SysSig.PlaySound.Send(Rng.UFrom(options), pos, useGlobalPitch);
        }
        public static void PlayExplosion(Vector2 pos) {
            The.SysSig.PlaySoundLoud.Send(
                Rng.UOneOf(
                    "FX/Explosion_01",
                    "FX/Explosion_02",
                    "FX/Explosion_03"), pos, false, 1f);
            The.SysSig.GamepadRumble.Send(0.5f, 0.3f, 0.1f);
        }
        public static void PlayWaterImpact(Vector2 pos) {
            The.SysSig.PlaySound.Send(
                Rng.UOneOf(
                    "FX/WaterImpact01",
                    "FX/WaterImpact02",
                    "FX/WaterImpact03"), pos, false);
        }

        public static void PlaySpikes(Vector2 pos) {
            The.SysSig.PlaySound.Send(
                Rng.UOneOf(
                    "FX/SpikesSFX_01",
                    "FX/SpikesSFX_02",
                    "FX/SpikesSFX_03"), pos, false);
        }

        public static void PlaySoftPop(Vector2 pos) {
            The.SysSig.PlaySoundVol.Send(Rng.UFrom(softPopSounds), pos, true, 1f);
        }

        public static void LockMusic() {
            The.BB.Set(BBKeyH.GlobalLockMusic, true);
        }

        public static void UnlockMusic() {
            The.BB.Set(BBKeyH.GlobalLockMusic, false);
        }

        public static void PlayObjectSelect(Vector2 pos) {
            The.SysSig.PlaySoundVol.Send(
                Rng.UOneOf(
                    "FX/ObjectSelect_01",
                    "FX/ObjectSelect_02",
                    "FX/ObjectSelect_03"), pos, false, 1f);
        }
        private static readonly string[] teleportInSounds = new[] {
            "FX/Teleport_In_01",
            "FX/Teleport_In_02",
            "FX/Teleport_In_03",
            "FX/Teleport_In_04",
        };
        private static readonly string[] teleportOutSounds = new[] {
            "FX/Teleport_Out_01",
            "FX/Teleport_Out_02",
            "FX/Teleport_Out_03",
            "FX/Teleport_Out_04",
            "FX/Teleport_Out_05",
        };
        private static readonly string[] softPopSounds = new[] {
            "FX/SoftPop01",
            "FX/SoftPop02",
            "FX/SoftPop03",
            "FX/SoftPop04",
            "FX/SoftPop05",
            "FX/SoftPop06",
            "FX/SoftPop07",
            "FX/SoftPop08",
        };
        private static readonly string[] constructSounds = new[] {
            "FX/Nanobots_2_01",
            "FX/Nanobots_2_02",
            "FX/Nanobots_2_03",
            "FX/Nanobots_2_04",
            "FX/Nanobots_2_05",
            "FX/Nanobots_2_06",
            "FX/Nanobots_2_07",
            "FX/Nanobots_2_08",
            "FX/Nanobots_2_09",
        };
        private static readonly string[] repairSounds = new[] {
            "FX/Nanobots_01",
            "FX/Nanobots_02",
            "FX/Nanobots_03",
            "FX/Nanobots_04",
            "FX/Nanobots_05",
            "FX/Nanobots_06",
            "FX/Nanobots_07",
            "FX/Nanobots_08",
            "FX/Nanobots_09",
        };
        private static readonly string[] punchSoftSounds = new[] {
            "FX/Punch_Soft_01",
            "FX/Punch_Soft_02",
            "FX/Punch_Soft_03",
            "FX/Punch_Soft_04",
            "FX/Punch_Soft_05",
        };
        public static void PlayTeleportIn(Vector2 pos) {
            The.SysSig.PlaySound.Send(Rng.UFrom(teleportInSounds), pos, true);
        }
        public static void PlayTeleportOut(Vector2 pos) {
            The.SysSig.PlaySound.Send(Rng.UFrom(teleportOutSounds), pos, true);
        }
        public static void PlayRepairs(Vector2 pos) {
            var v = Tunables.RepairsVolume;
            if (Rng.UChance(0.8f)) {
                The.SysSig.PlaySoundVol.Send(Rng.UFrom(repairSounds), pos, true, v);
            } else {
                The.SysSig.PlaySoundVol.Send(Rng.UFrom(constructSounds), pos, true, v);
            }
        }
        public static void PlayConstruct(Vector2 pos) {
            var v = Tunables.ConstructionVolume;
            if (Rng.UChance(0.2f)) {
                The.SysSig.PlaySoundVol.Send(Rng.UFrom(repairSounds), pos, true, v);
            } else {
                The.SysSig.PlaySoundVol.Send(Rng.UFrom(constructSounds), pos, true, v);
            }
        }
        public static void PlayNanobots(Vector2 pos) {
            var v = Tunables.RepairsVolume;
            The.SysSig.PlaySoundVol.Send(Rng.UFrom(repairSounds), pos, true, v);
        }
        public static void Play(Vector2 pos, string sound, bool useGlobalPitch) {
            #if !PRODUCTION_BUILD
            if (pos.sqrMagnitude < Maths.Epsilon) {
                D.Err("Playing sound at zero coordinate! {0}: {1}", pos, sound);
            }
            #endif
            The.SysSig.PlaySound.Send(sound, pos, useGlobalPitch);
        }

        public static void PlayLoud(Vector2 pos, string sound, bool useGlobalPitch, float volume = 1f) {
            #if !PRODUCTION_BUILD
            if (pos.sqrMagnitude < Maths.Epsilon) {
                D.Err("Playing sound at zero coordinate! {0}: {1}", pos, sound);
            }
            #endif
            The.SysSig.PlaySoundLoud.Send(sound, pos, useGlobalPitch, volume);
        }

        public static void PlayConstructionOrder(Vector2 pos) {
            #if !PRODUCTION_BUILD
            if (pos.sqrMagnitude < Maths.Epsilon) {
                D.Err("Playing construction order at zero coordinate! {0}", pos);
            }
            #endif
            The.SysSig.PlaySoundVol.Send("FX/ConstructionPlace", pos, false, 1f);
            The.SysSig.GamepadRumble.Send(0.1f, 0.1f, 0f);
        }

        public static void PlayTileSelect(Vector2 pos) {
            #if !PRODUCTION_BUILD
            if (pos.sqrMagnitude < Maths.Epsilon) {
                D.Err("Playing tile select at zero coordinate! {0}", pos);
            }
            #endif
            The.SysSig.PlaySoundVol.Send("FX/TileSelect", pos, false, 1f);
        }

        public static void PlayConstructionComplete(Vector2 pos) {
            #if !PRODUCTION_BUILD
            if (pos.sqrMagnitude < Maths.Epsilon) {
                D.Err("Playing construction complete at zero coordinate! {0}", pos);
            }
            #endif
            The.SysSig.PlaySound.Send("FX/ConstructionComplete", pos, true);
        }
        public static void PlayEnergyOn(Vector2 pos) {
            if (!Ready.Gen) { return; }
            #if !PRODUCTION_BUILD
            if (pos.sqrMagnitude < Maths.Epsilon) {
                D.Err("Playing energy on at zero coordinate! {0}", pos);
            }
            #endif
            The.SysSig.PlaySoundVol.Send("FX/ElectricityOn", pos, false, 1f);
        }
        public static void PlayEnergyOff(Vector2 pos) {
            if (!Ready.Gen) { return; }
            #if !PRODUCTION_BUILD
            if (pos.sqrMagnitude < Maths.Epsilon) {
                D.Err("Playing energy off at zero coordinate! {0}", pos);
            }
            #endif
            The.SysSig.PlaySoundVol.Send("FX/ElectricityOff", pos, false, 1f);
        }

        public static void PlayDoorOpen(Vector2 pos) {
            if (RenderUtils.IsVisibleSafe(pos)) {
                The.SysSig.PlaySound.Send("FX/DoorOpen", pos, true);
            }
        }
        public static void PlayDoorClose(Vector2 pos) {
            if (RenderUtils.IsVisibleSafe(pos)) {
                The.SysSig.PlaySound.Send("FX/DoorClose", pos, true);
            }
        }
        public static void PlayBreakDown(Vector2 pos) {
            #if !PRODUCTION_BUILD
            if (pos.sqrMagnitude < Maths.Epsilon) {
                D.Err("Playing break down at zero coordinate! {0}", pos);
            }
            #endif
            The.SysSig.PlaySound.Send("FX/BreakDown", pos, true);
            The.SysSig.GamepadRumble.Send(0.2f, 0.2f, 0.1f);
        }

        public static void PlayOneTrack(string tempMood, string nextMood) {
            PlayMusic(tempMood, true);
            PlayMusic(nextMood, false);
        }

        public static void PlayRandomMeleeHitAt(Vector2 pos) {
            The.SysSig.PlaySoundVol.Send(Rng.UFrom(punchSoftSounds), pos, true, 1f);
        }
    }
}
