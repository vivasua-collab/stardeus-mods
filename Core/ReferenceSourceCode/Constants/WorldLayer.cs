using Game.Rendering;

namespace Game.Constants {
    public static class WorldLayer {
        // floor, walls and objects
        public static int DataTileLayerCount => RenderLayers.DataTileLayerCount;
        // Layers with TileRenderer mesh infrastructure
        public static int TileLayerCount => RenderLayers.TileRendererLayerIds.Length;
        // Max logical layer id + 1
        public static int LayerCount => RenderLayers.MaxIdExclusive;

        // DATA LAYERS HAVE TO ALWAYS GO FIRST!
        public const byte Floor = 0;
        public const byte Walls = 1;
        public const byte Objects = 2;
        // Entities
        public const byte FreeEntities = 3;
        // Helper Layers
        public const byte Decals = 4;
        public const byte DecalsAbove = 5;
        // Above layers
        public const byte ObjectsAbove = 6;
        // Helper layers
        public const byte Tools = 7;
        public const byte ToolsAbove = 8;

        // Has to go in sequence ^

        public static int[] TileLayerIds => RenderLayers.TileLayerIds;
        public static int[] PersistentLayerIds => RenderLayers.PersistentComponentLayerIds;

        public static bool IsGridLayer(int layer) {
            return RenderLayers.IsGrid(layer);
        }

        public static bool IsRenderableTileLayer(int layer) {
            return RenderLayers.ShouldResetFromGridData(layer);
        }

        public static bool IsDataTileLayer(int layer) {
            return RenderLayers.IsData(layer);
        }

        public static int ToId(string layer) {
            return RenderLayers.ToId(layer);
        }

        public static string Name(int layer) {
            return RenderLayers.Name(layer);
        }

        public static string NameKey(int layer) {
            return RenderLayers.NameKey(layer);
        }
    }
}
