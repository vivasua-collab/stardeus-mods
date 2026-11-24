using KL.Utils;
using Unity.Mathematics;

namespace Game.Systems.Light {
    public struct LightSource {
        public float2 Position;
        public float Range;
        public float3 Color;
        public float3 Color2;
        public float PulseFreq;
        // 0 = main light (static)
        // 1 = non-main light (static)
        // 2 = dynamic light
        public int Type;
        // 0..1: how hard the light flickers (power deficit, damage). Keep the field order in
        // sync with Resources/Shaders/Compute/Includes/LightSource.cginc
        public float Flicker;
        // How strongly the near white pixels of the light's own sprite glow (LightGlowBuffer),
        // 0 for none
        public float Glow;

        public override string ToString() {
            if (PulseFreq > 0) {
                return $"LightSource[R:{Range},P:[{Position.x}:{Position.y}],{Colors.ToHex(Colors.From(Color))}->{Colors.ToHex(Colors.From(Color2))}@{PulseFreq}]";
            } else {
                return $"LightSource[R:{Range},P:[{Position.x}:{Position.y}],{Colors.ToHex(Colors.From(Color))}]";
            }
        }
    }
}
