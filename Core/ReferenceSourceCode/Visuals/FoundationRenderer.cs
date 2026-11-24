using Game.Constants;
using Game.Rendering;
using KL.Utils;
using UnityEngine;

namespace Game.Visuals {
    // The ship's foundation: the side of the floor slab, seen below every south-facing edge
    // of built floor. One map-sized quad drawn under the floors; Foundation.shader works out
    // every pixel from the floor buffer (FloorKindBuffer), which also says which strip each
    // floor's face shows (FoundationStyles), so nothing here follows floor changes. A tile
    // quad cannot draw it: the face hangs into the empty tile below the edge.
    public sealed class FoundationRenderer {
        private readonly Material material;
        private readonly int cullingLayer;
        private Mesh quad;

        public FoundationRenderer() {
            var shader = Shader.Find("Foundation");
            if (shader == null) {
                D.Err("Foundation shader not found, the ship's foundation is not drawn");
                return;
            }
            material = new Material(shader) { name = "Foundation" };
            The.Alloc.Register(material);
            cullingLayer = RenderLayers.ById(WorldLayer.Floor).CullingLayer;
        }

        public void Resize(int width, int height) {
            if (material == null) { return; }
            if (quad == null) {
                quad = new Mesh { name = "FoundationQuad" };
                The.Alloc.Register(quad);
            }
            quad.SetVertices(new[] {
                new Vector3(0, 0, 0), new Vector3(0, height, 0),
                new Vector3(width, height, 0), new Vector3(width, 0, 0)
            });
            quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            quad.RecalculateBounds();
        }

        public void Render() {
            if (quad == null) { return; }
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
