#if UNITY_EDITOR
// #define ALWAYS_SHOW_DEBUG
#endif
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Utils;

namespace Game.Systems.UIUX {
    /// <summary>
    /// Entity tab showing debug/sandbox info for any entity type. Visible in sandbox
    /// mode (A.InstantBuild) or in non-production builds. Contains per-entity blocks
    /// (entity ID, game seed, VFX, TileAnim, BeingGraphics, BeingLooks) registered
    /// via SandboxBlock subclasses.
    /// </summary>
    public sealed class DevDebugTab : BaseEntityTab {
        public const string TabId = "DevDebug";

        [SelfInit]
        private static void Register() {
            #if !STARDEUS_DEMO
            RegisterTab(new DevDebugTab());
            #endif
        }

        public override string Id => TabId;
        public override int Order => int.MaxValue;
        public override string TabName => A.InstantBuild ? T.SandboxMode : T.DevDebug;
        public override bool SuppressWarningOverlay => true;

        private readonly SandboxContext ctx = new();
        private SandboxBlock[] blocks;

        public override bool ShowFor(GameState s, BeingData beingData) {
            if (!IsVisible()) { return false; }
            InitBlocks();
            ctx.S = s;
            ctx.Being = beingData;
            return true;
        }

        public override bool ShowFor(GameState s, Tile tile) {
            if (!IsVisible()) { return false; }
            InitBlocks();
            ctx.S = s;
            ctx.Tile = tile;
            return true;
        }

        public override bool ShowFor(GameState s, ObjData objData) {
            if (!IsVisible()) { return false; }
            InitBlocks();
            ctx.S = s;
            ctx.Obj = objData;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            InitBlocks();
            ctx.Lifetime = lifetime;
            int gIdx = 0;
            for (int i = blocks.Length - 1; i >= 0; i--) {
                blocks[i].LoadContent(ctx, group, ref gIdx);
            }
            return gIdx > 0;
        }

        protected override void Cleanup() {
            ctx.Reset();
        }

        private void InitBlocks() {
            if (blocks != null) { return; }
            blocks = Registries.SandboxBlocks.All.Values
                .OrderByDescending(b => b.Order).ToArray();
        }

        private static bool IsVisible() {
            #if ALWAYS_SHOW_DEBUG
            return true;
            #else
            return A.InstantBuild;
            #endif
        }
    }
}
