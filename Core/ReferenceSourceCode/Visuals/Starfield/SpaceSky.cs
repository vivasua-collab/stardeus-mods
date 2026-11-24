using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data.Space;
using Game.Systems.Space;
using Game.Utils;
using UnityEngine;

namespace Game.Visuals {
    // Drives the region sky globals read by SpaceSkyCore.cginc: the sector's difficulty (or a
    // template override) picks a palette, active space storms add a storm layer. Fades run in
    // the shader off _UnscaledTime, so this only writes globals when something changes.
    // See Docs/Architecture/Systems/SpaceSky.md.
    public static class SpaceSky {
        public const float FadeSeconds = 4f;
        public const float StormFadeSeconds = 3f;

        private static SpaceSkyPalette from;
        private static SpaceSkyPalette to;
        private static float fadeStart;
        private static float fadeInvDuration;

        private static Vector4 stormFrom;
        private static Vector4 stormTo;
        private static Vector2 stormFx;
        private static float stormFadeStart;
        private static float stormInvDuration;
        // Lowest priority first: the sector's ambient storm sits at index 0, real storms after
        private static readonly List<string> activeStorms = new();
        private static string ambientStorm;

        [ExecuteOnReload]
        private static void ClearState() {
            from = to = default;
            fadeStart = fadeInvDuration = 0f;
            stormFrom = stormTo = Vector4.zero;
            stormFx = Vector2.zero;
            stormFadeStart = stormInvDuration = 0f;
            activeStorms.Clear();
            ambientStorm = null;
        }

        // Back to the identity sky (the nebula as authored), no storm
        public static void Reset() {
            ClearState();
            WritePalette();
            WriteStorm();
        }

        public static SpaceSkyPalette Target => to;
        public static IReadOnlyList<string> ActiveStorms => activeStorms;

        // Called once per game load, before sector effects and story storms re-apply (they
        // run later: Ready.WhenGameLate and the first tick), so their storms survive.
        public static void OnGameLoaded(SpaceObject location) {
            Reset();
            Apply(location, false);
        }

        public static void Apply(SpaceObject location, bool fade) {
            var sector = SectorOf(location);
            if (sector?.Biome == null) {
                // Hyperspace or the galaxy layer: keep whatever sky we had
                return;
            }
            var biome = sector.Biome;
            var templateId = biome.Template?.Id;
            SetPalette(SpaceSkyDef.Resolve(biome.Difficulty, templateId), fade);
            SetAmbientStorm(SpaceSkyDef.ResolveAmbientStorm(templateId));
        }

        // A storm layer that belongs to the sector itself (black hole flicker, portal pulses).
        // It has the lowest priority, so a real storm in that sector takes over and the
        // ambient one comes back when it ends.
        public static void SetAmbientStorm(string id) {
            if (id == ambientStorm) {
                return;
            }
            if (ambientStorm != null) {
                activeStorms.Remove(ambientStorm);
            }
            ambientStorm = id;
            if (id != null) {
                activeStorms.Remove(id);
                activeStorms.Insert(0, id);
            }
            RefreshStorm();
        }

        public static void SetPalette(in SpaceSkyPalette target, bool fade) {
            if (target.Equals(to) && (fade || fadeInvDuration == 0f)) {
                return;
            }
            var now = GameTimer.unscaledTime;
            if (fade) {
                from = PaletteAt(now);
                fadeStart = now;
                fadeInvDuration = 1f / FadeSeconds;
            } else {
                from = target;
                fadeStart = 0f;
                fadeInvDuration = 0f;
            }
            to = target;
            WritePalette();
        }

        // Storms that disturb the ship's power, so its lamps flicker while one lasts
        private static readonly string[] FlickerStorms = { "MagneticStorm", "PsychicStorm" };
        private const float StormFlickerStrength = 0.45f;

        // How hard every room light flickers right now (Light.compute, via LightSys)
        public static float StormFlicker {
            get {
                foreach (var id in FlickerStorms) {
                    if (activeStorms.Contains(id)) {
                        return StormFlickerStrength;
                    }
                }
                return 0f;
            }
        }

        // Last started storm wins; stopping it reverts to one that is still active.
        public static void SetStorm(string id, bool on) {
            if (on) {
                activeStorms.Remove(id);
                activeStorms.Add(id);
            } else if (!activeStorms.Remove(id)) {
                return;
            }
            RefreshStorm();
        }

        private static void RefreshStorm() {
            SpaceSkyStormDef def = null;
            for (int i = activeStorms.Count - 1; i >= 0; i--) {
                if (SpaceSkyStormDef.TryGet(activeStorms[i], out def)) {
                    break;
                }
            }
            var now = GameTimer.unscaledTime;
            var current = StormAt(now);
            Vector4 target;
            var fx = stormFx;
            if (def != null) {
                target = def.ColorAndStrength;
                fx = new Vector2(def.FlashRate, def.Shimmer);
            } else {
                // Fade out in the color it had, animation keeps going until it is gone
                target = new Vector4(current.x, current.y, current.z, 0f);
            }
            if (target == stormTo && fx == stormFx) {
                return;
            }
            stormFx = fx;
            stormFrom = current;
            stormTo = target;
            stormFadeStart = now;
            stormInvDuration = 1f / StormFadeSeconds;
            WriteStorm();
        }

        public static void OnSettingChanged(string setting) {
            if (setting == Pref.GAnimatedBackground) {
                WriteStorm();
            }
        }

        private static SpaceSkyPalette PaletteAt(float now) {
            var t = Mathf.Clamp01((now - fadeStart) * fadeInvDuration);
            return SpaceSkyPalette.Lerp(from, to, t);
        }

        private static Vector4 StormAt(float now) {
            var t = Mathf.Clamp01((now - stormFadeStart) * stormInvDuration);
            return Vector4.Lerp(stormFrom, stormTo, t);
        }

        // Quiet version of SOUtils.SectorFor, which logs an error when there is no sector
        private static SpaceSector SectorOf(SpaceObject so) {
            for (int depth = 0; so != null && depth < 8; depth++) {
                if (so.Sector != null) {
                    return so.Sector;
                }
                so = so.Parent;
            }
            return null;
        }

        private static void WritePalette() {
            Shader.SetGlobalColor(ShaderVars._SkyShadowA, from.Shadow);
            Shader.SetGlobalColor(ShaderVars._SkyMidA, from.Mid);
            Shader.SetGlobalColor(ShaderVars._SkyHighA, from.Highlight);
            Shader.SetGlobalVector(ShaderVars._SkyParamsA, from.Params);
            Shader.SetGlobalColor(ShaderVars._SkyShadowB, to.Shadow);
            Shader.SetGlobalColor(ShaderVars._SkyMidB, to.Mid);
            Shader.SetGlobalColor(ShaderVars._SkyHighB, to.Highlight);
            Shader.SetGlobalVector(ShaderVars._SkyParamsB, to.Params);
            Shader.SetGlobalVector(ShaderVars._SkyFade,
                new Vector4(fadeStart, fadeInvDuration, 0f, 0f));
        }

        private static void WriteStorm() {
            var animated = The.Prefs == null || The.Prefs.GetBool(Pref.GAnimatedBackground,
                PrefH.GAnimatedBackground, true);
            var fx = animated ? stormFx : Vector2.zero;
            Shader.SetGlobalVector(ShaderVars._SkyStormA, stormFrom);
            Shader.SetGlobalVector(ShaderVars._SkyStormB, stormTo);
            Shader.SetGlobalVector(ShaderVars._SkyStormFx,
                new Vector4(fx.x, fx.y, stormFadeStart, stormInvDuration));
        }
    }
}
