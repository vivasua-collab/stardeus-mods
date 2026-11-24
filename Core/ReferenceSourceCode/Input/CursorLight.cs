using Game.CodeGen;
using Game.Constants;
using Game.Modifiers;
using Game.Rendering;
using Game.Systems.Light;
using Game.UI;
using Game.Utils;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Input {
    public class CursorLight : MonoBehaviour {
        // Camera tools ("Cursor Effects") hide the light for clean shots. Kept apart from
        // the player's Cursor Light pref, which would otherwise be saved as off when the
        // game quits while suppressed
        public static bool IsSuppressed { get; private set; }

        [ExecuteOnReload]
        private static void ClearSuppression() {
            IsSuppressed = false;
        }

        public static void SetSuppressed(bool isSuppressed) {
            if (IsSuppressed == isSuppressed) { return; }
            IsSuppressed = isSuppressed;
            // the listeners below re-evaluate on this key
            The.SysSig.SettingChanged.Send(Pref.GCursorLight);
        }

        private bool isLightEnabled;
        private ushort lightId;
        private LightSource lightSource;
        private bool hasCreatedLight;
        private void Start() {
            OnSettingChanged(Pref.GCursorLight);
        }

        private void LateUpdate() {
            if (!isLightEnabled) { return; }
            if (ModifierFogOfWar.IsEnabled) {
                isLightEnabled = false;
                return;
            }
            LightSys ls;
            if (!hasCreatedLight) {
                if (!Ready.Rendering) { return; }
                ls = The.R?.Sys?.Light;
                if (ls == null) {
                    //D.Err("Light system not found!");
                    return;
                }
                if (!ls.TryReserveSlot(out lightId)) {
                    // some time later some light will be recycled anyway
                    #if !PRODUCTION_BUILD
                    D.Log("Cursor Light failed to get a light slot");
                    #endif
                    return;
                }
                hasCreatedLight = true;
                lightSource = new LightSource() {
                    Color = new float3(0.33f, 0.33f, 0.33f),
                    Range = 7f / 2f,
                    Type = 2,
                };
            } else {
                ls = The.R?.Sys?.Light;
                if (ls == null) {
                    return;
                }
            }
            if (UIShowing.MainMenu) {
                return;
            }
            var pos = RenderUtils.MouseWorldPos;
            lightSource.Position = new float2(pos.x, pos.y);
            ls.SetLight(lightId, lightSource);
        }

        private void OnEnable() {
            The.SysSig.SettingChanged.AddListener(OnSettingChanged, gameObject);
        }

        private void OnDisable() {
            The.SysSig.SettingChanged.RemoveListener(OnSettingChanged);
        }

        private void OnSettingChanged(string key) {
            if (key == Pref.GAmbColor) {
                if (ModifierFogOfWar.IsEnabled) {
                    if (hasCreatedLight) {
                        The.R?.Sys?.Light?.RemoveLight(lightId);
                        hasCreatedLight = false;
                    }
                    isLightEnabled = false;
                } else {
                    CheckEnableLight();
                }
            } else if (key == Pref.GCursorLight) {
                if (!ModifierFogOfWar.IsEnabled) {
                    CheckEnableLight();
                }
            } else if (key == Pref.GLightIntensity) {
                if (hasCreatedLight) {
                    lightSource.Color = GetColor();
                    The.R?.Sys?.Light?.SetLight(lightId, lightSource);
                }
            }
        }

        private void CheckEnableLight() {
            isLightEnabled = !IsSuppressed && The.Prefs.GetBool(Pref.GCursorLight,
                PrefH.GCursorLight, true);
            // if it is enabled but light is not created, it will auto create on update
            if (!isLightEnabled) {
                if (hasCreatedLight) {
                    The.R?.Sys?.Light?.RemoveLight(lightId);
                    hasCreatedLight = false;
                }
            }
        }

        private float3 GetColor() {
            return new float3(0.75f, 0.75f, 0.75f)
                * The.Prefs.GetFloat(Pref.GLightIntensity,
                    PrefH.GLightIntensity, Consts.DefaultLightIntensity);
        }
    }
}
