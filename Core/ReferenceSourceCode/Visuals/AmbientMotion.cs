using Game.CodeGen;
using Game.Constants;
using Game.Utils;
using UnityEngine;

namespace Game.Visuals {
    // The "Ambient motion" video setting and the clock that idle world motion (plant sway in
    // GPUR/SpriteSway) runs on. The clock runs at the VFX time scale (Clock.FXTimeScale, which
    // the clock also sets as Time.timeScale): still while paused, and only as fast as the
    // particles at high game speeds. See Docs/Architecture/Systems/AmbientMotion.md.
    public static class AmbientMotion {
        public const bool DefaultOn = true;

        private static float time;
        // Seconds of idle motion so far, the clock plant brushes are timed on (PlantBrush)
        public static float Time => time;

        // Cached, the walk bob and the downed wiggle read it for every walking or lying being
        // on every frame
        public static bool IsOn { get; private set; } = DefaultOn;

        [ExecuteOnReload]
        private static void ClearState() {
            time = 0f;
        }

        [SelfInit(SelfInit.Stage.Finalize)]
        private static void ApplyOnBoot() {
            Apply();
            The.SysSig.SettingChanged.AddListener(OnSettingChanged);
        }

        private static void OnSettingChanged(string setting) {
            if (setting == Pref.GAmbientMotion) {
                Apply();
            }
        }

        private static void Apply() {
            IsOn = The.Prefs.GetBool(Pref.GAmbientMotion, PrefH.GAmbientMotion, DefaultOn);
            Shader.SetGlobalInteger(ShaderVars._AmbientMotion, IsOn ? 1 : 0);
        }

        // Every frame, from Bootstrap with the other shader globals, with the frame time scaled
        // by the VFX time scale (0 while paused)
        public static void UpdateShaderTime(float fxDeltaTime) {
            // Also while paused: a save loads paused and must drop the last game's brushes
            PlantBrush.Update(fxDeltaTime > 0f);
            if (fxDeltaTime <= 0f) {
                return;
            }
            time += fxDeltaTime;
            Shader.SetGlobalFloat(ShaderVars._AmbientTime, time);
        }
    }
}
