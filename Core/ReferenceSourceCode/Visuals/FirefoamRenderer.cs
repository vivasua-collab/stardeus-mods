using Game.Constants;
using Game.Rendering;
using KL.Utils;
using UnityEngine;

namespace Game.Visuals {
    // Firefoam on the floor: one map-sized quad drawn above the dirt and below walls and
    // devices. Firefoam.shader works out every pixel from the per-tile foam buffer that
    // DirtSys keeps (FirefoamBuffer), so nothing here follows the foam. Drawn only while some
    // tile has foam.
    public sealed class FirefoamRenderer {
        private readonly Material material;
        private readonly int cullingLayer;
        private Mesh quad;

        public FirefoamRenderer() {
            var shader = Shader.Find("Firefoam");
            if (shader == null) {
                D.Err("Firefoam shader not found, firefoam is not drawn");
                return;
            }
            material = new Material(shader) { name = "Firefoam" };
            The.Alloc.Register(material);
            cullingLayer = RenderLayers.ById(WorldLayer.Floor).CullingLayer;
        }

        public void Resize(int width, int height) {
            if (material == null) { return; }
            if (quad == null) {
                quad = new Mesh { name = "FirefoamQuad" };
                The.Alloc.Register(quad);
            }
            quad.SetVertices(new[] {
                new Vector3(0, 0, 0), new Vector3(0, height, 0),
                new Vector3(width, height, 0), new Vector3(width, 0, 0)
            });
            quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            quad.RecalculateBounds();
        }

        public void Render(int foamTiles) {
            if (quad == null || foamTiles == 0) { return; }
            Graphics.DrawMesh(quad, Matrix4x4.identity, material, cullingLayer);
        }

        public void Dispose() {
            if (quad != null) {
                Object.Destroy(quad);
                The.Alloc.Unregister(quad);
                quad = null;
            }
            if (material != null) {
                Object.Destroy(material);
                The.Alloc.Unregister(material);
            }
        }
    }
}
