using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;
namespace Game.Visuals {
    [VFXType(VFXTypeAttribute.Usage.GraphicsBuffer), StructLayout(LayoutKind.Sequential)]
    public struct VFXLineData {
        public Vector2 From;
        public Vector2 To;
        public Color Color;
    }
}
