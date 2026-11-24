using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
#if !PRODUCTION_BUILD
    public class SandboxBlock_TileGraphics : SandboxBlock {
        public const string BlockId = "TileGraphics";
        public override int Order => -60;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() => Register(new SandboxBlock_TileGraphics());

        public override bool ShowFor(GameState s, Tile tile) {
            return tile != null && tile.HasComponent<TileGraphicsComp>();
        }

        public override void LoadContent(SandboxContext ctx, WGroup group, ref int gIdx) {
            var tile = ctx.Tile;
            if (tile == null
                    || !tile.TryGetComponent<TileGraphicsComp>(out var comp)) {
                return;
            }

            var lifetime = ctx.Lifetime;
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load("Graphics Debug");

            var graphicRow = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContent2Text, block.Container, idx++);
            graphicRow.Load(IconId.CInfo, comp.GraphicId);

            var typeRow = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContent2Text, block.Container, idx++);
            typeRow.Load(IconId.CInfo, comp.Type.ToString());

            if (comp.HasShader) {
                var shaderRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                shaderRow.Load(IconId.CInfo, "Shader", comp.Shader?.name ?? "None");
            }

            var debugBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            debugBtn.Load(IconId.WDev, "Submit Icon Debug Info", T.Execute,
                () => D.Warn(comp.GetTileIconsDebugInfo()));
            UIUtils.AddTipFunc(lifetime, debugBtn,
                () => comp.GetTileIconsDebugInfo());
        }
    }
#endif
}
