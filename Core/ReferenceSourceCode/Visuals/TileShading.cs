using Game.CodeGen;
using Game.Constants;
using Game.Utils;
using UnityEngine;

namespace Game.Visuals {
    // Pushes the "Floor shading" video setting (floor contact shadows near walls and the hull
    // rim light, both in Tiles.cginc) to the tile shaders as a global, so turning it off costs
    // the GPU only a uniform branch. See Docs/Architecture/Systems/TileShading.md.
    public static class TileShading {
        public const bool DefaultOn = true;

        public static bool IsOn => The.Prefs.GetBool(Pref.GFloorShading, PrefH.GFloorShading,
            DefaultOn);

        [SelfInit(SelfInit.Stage.Finalize)]
        private static void ApplyOnBoot() {
            Apply();
            The.SysSig.SettingChanged.AddListener(OnSettingChanged);
        }

        private static void OnSettingChanged(string setting) {
            if (setting == Pref.GFloorShading) {
                Apply();
            }
        }

        private static void Apply() {
            Shader.SetGlobalInteger(ShaderVars._TileShading, IsOn ? 1 : 0);
        }
    }
}
