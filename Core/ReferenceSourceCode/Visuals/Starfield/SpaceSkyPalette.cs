using System;
using UnityEngine;

namespace Game.Visuals {
    // CPU mirror of one palette in SpaceSkyCore.cginc. default (all zeros) is the identity:
    // strength 0 leaves the nebula as it was authored.
    public struct SpaceSkyPalette : IEquatable<SpaceSkyPalette> {
        public Color Shadow;
        public Color Mid;
        public Color Highlight;
        // x strength, y intensity, z brightness range, w hull rim strength
        public Vector4 Params;

        public float Strength => Params.x;

        public static SpaceSkyPalette Lerp(in SpaceSkyPalette a, in SpaceSkyPalette b, float t) {
            return new SpaceSkyPalette {
                Shadow = Color.LerpUnclamped(a.Shadow, b.Shadow, t),
                Mid = Color.LerpUnclamped(a.Mid, b.Mid, t),
                Highlight = Color.LerpUnclamped(a.Highlight, b.Highlight, t),
                Params = Vector4.LerpUnclamped(a.Params, b.Params, t),
            };
        }

        public bool Equals(SpaceSkyPalette other) {
            return Shadow == other.Shadow && Mid == other.Mid
                && Highlight == other.Highlight && Params == other.Params;
        }

        public override bool Equals(object obj) {
            return obj is SpaceSkyPalette other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(Shadow, Mid, Highlight, Params);
        }

        public override string ToString() {
            return $"SpaceSkyPalette[{ColorUtility.ToHtmlStringRGB(Shadow)}/{ColorUtility.ToHtmlStringRGB(Mid)}/{ColorUtility.ToHtmlStringRGB(Highlight)} {Params}]";
        }
    }
}
