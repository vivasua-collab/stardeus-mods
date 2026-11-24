using Game.Rendering;
using UnityEngine;

namespace Game.Utils {
    public static class UIScale {
        public struct ScaleData { public float def, min, max, step; }

        static readonly float[] H =    { 800, 1080, 1440, 2160, 4320 };
        static readonly float[] DEF =  { 0.75f, 1.0f, 1.0f, 1.5f, 3.0f };
        static readonly float[] MIN =  { 0.7f,  0.7f, 0.8f, 1.0f, 2.0f };
        static readonly float[] MAX =  { 0.85f, 1.2f, 1.5f, 2.5f, 4.0f };
        static readonly float[] STEP =  { 0.05f, 0.1f, 0.1f, 0.1f, 0.1f };

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Snap(float x) {
            // prefer quarters; adjust to your UI
            return Mathf.Round(x * 4f) / 4f;
        }


        private static int FindSegment(float h) {
            for (int i = 0; i < H.Length - 1; i++) {
                    if (h <= H[i + 1]) return i;

            }
            return H.Length - 2;
        }

        public static ScaleData Evaluate(float h) {
            int i = FindSegment(h);
            float t = Mathf.InverseLerp(H[i], H[i + 1], h);
            return new ScaleData {
                def = Lerp(DEF[i], DEF[i + 1], t),
                min = Lerp(MIN[i], MIN[i + 1], t),
                max = Lerp(MAX[i], MAX[i + 1], t),
                step = Lerp(STEP[i], STEP[i + 1], t),
            };
        }

        public static float DpiFactor(float dpi) {
            if (dpi <= 0) return 1f;              // unknown DPI
            const float refDpi = 96f;
            float f = Mathf.Sqrt(dpi / refDpi);  // perceptual, not linear
            return Mathf.Clamp(f, 0.9f, 1.25f);
        }

        public static ScaleData GetScaleData() {
            var h = RenderUtils.ScreenHeight;
            if (h == 0) {
                h = Screen.height;
            }
            if (h <= 800 && The.Platform.IsSteamDeck()) {
                return new ScaleData {
                    def = 0.75f,
                    min = 0.7f,
                    max = 0.85f,
                    step = 0.05f,
                };
            }
            var data = Evaluate(h);
            // var factor = DpiFactor(Screen.dpi);
            data.def = Snap(Mathf.Clamp(data.def, data.min, data.max));
            return data;
        }

    }
}
