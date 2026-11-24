using Game.Constants;
using Game.Data;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Areas {
    // What kind of built floor each tile has, for the ink line along the ship's edge
    // (Ink.cginc) and the foundation under it (Foundation.shader). The area buffer cannot
    // tell: floors that are not walkable and not fully solid (angled corner floors) are open
    // space to the areas, so the floor next to one was inked as if the ship ended there, and
    // the angled edge itself got no line. Kept from the floor grid's change events, which also
    // fire when construction finishes and when a winched or rotated section is written into
    // the grid directly.
    public sealed class FloorKindBuffer {
        // Keep in sync with Ink.cginc and Foundation.shader: kind in bits 0-1, facing in bits
        // 2-3, foundation style in bits 4-15
        private const int None = 0;
        private const int Solid = 1;
        // Built, but open space to the areas (AreaSys.ResolveAreaTypeAt): its transparent
        // part is where the ship ends
        private const int Open = 2;
        // An open floor is an angled corner: its Facing tells the foundation which half of the
        // tile is built
        private const int FacingShift = 2;
        private const int StyleShift = 4;

        private readonly Grid<Tile> floors;
        private readonly int[] data;
        private readonly ComputeBuffer buffer;
        private readonly FoundationStyles foundations = new();
        private bool isDirty;

        public FloorKindBuffer(GameState s) {
            floors = s.Map.Grids[WorldLayer.Floor];
            data = new int[floors.CellCount];
            for (int i = 0; i < data.Length; i++) {
                data[i] = KindOf(floors.Data[i]);
            }
            buffer = new ComputeBuffer(data.Length, sizeof(int));
            buffer.name = "FloorKindBuffer";
            The.Alloc.Register(buffer);
            buffer.SetData(data);
            Shader.SetGlobalBuffer(ShaderVars._FloorKinds, buffer);
            foundations.Flush();
            floors.OnChanged.AddListener(OnFloorChanged);
        }

        private int KindOf(Tile floor) {
            if (floor?.IsConstructed != true) {
                return None;
            }
            var style = foundations.IndexOf(floor.Graphics?.FoundationId) << StyleShift;
            if (!IsAngled(floor)) {
                return Solid | style;
            }
            return Open | ((int) floor.Transform.Rotation << FacingShift) | style;
        }

        // The def says so (TileGraphics IsAngled), or else it is guessed: a floor that is open
        // space to the areas. The guess alone took a mod frame that is not walkable for a
        // corner, so its struts were inked inside the tile and its foundation cut diagonally.
        private static bool IsAngled(Tile floor) {
            return floor.Graphics?.IsAngled ?? !EntityUtils.WillBeSectionFloor(floor);
        }

        private void OnFloorChanged(int pos, Grid<Tile> grid, Tile current, Tile prev) {
            var kind = KindOf(current);
            if (data[pos] != kind) {
                data[pos] = kind;
                isDirty = true;
            }
        }

        public void Flush() {
            if (!isDirty) { return; }
            isDirty = false;
            foundations.Flush();
            buffer.SetData(data);
        }

        public void Dispose() {
            floors.OnChanged?.RemoveListener(OnFloorChanged);
            Shader.SetGlobalBuffer(ShaderVars._FloorKinds, (ComputeBuffer) null);
            buffer.Release();
            The.Alloc.Unregister(buffer);
            foundations.Dispose();
        }
    }
}
