using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
#if !PRODUCTION_BUILD
    public class SandboxBlock_BeingLooks : SandboxBlock {
        public const string BlockId = "BeingLooks";
        public override int Order => -40;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() => Register(new SandboxBlock_BeingLooks());

        public override bool ShowFor(GameState s, BeingData being) {
            return being.Being != null && being.Being.HasComponent<BeingLooksComp>();
        }

        public override void LoadContent(SandboxContext ctx, WGroup group, ref int gIdx) {
            var being = ctx.Being;
            if (being?.Being == null
                    || !being.Being.TryGetComponent<BeingLooksComp>(out var comp)) {
                return;
            }

            var lifetime = ctx.Lifetime;
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load("Looks Debug");

            var body = being.Body;
            if (body.SkinColorId != null) {
                var color = Palette.Get(body.SkinColorId);
                if (color != null) {
                    var skinRow = UISpawn.Widget<WIconText2>(lifetime,
                        UIPrefabId.WContent2Text2, block.Container, idx++);
                    skinRow.Load(color.SpriteId, "Skin Color", color.NameT);
                }
            }
            if (body.HairColorId != null) {
                var color = Palette.Get(body.HairColorId);
                if (color != null) {
                    var hairRow = UISpawn.Widget<WIconText2>(lifetime,
                        UIPrefabId.WContent2Text2, block.Container, idx++);
                    hairRow.Load(color.SpriteId, "Hair Color", color.NameT);
                }
            }

            var hiddenParts = comp.DebugHiddenParts;
            foreach (var part in comp.Looks.Parts) {
                var isHidden = (hiddenParts & part.Type.Bit) != 0UL;
                var icon = isHidden ? IconId.CCross : $"{part.SpriteId}_D";
                var text = part.SpriteId ?? $"No {part.Type}";
                if (part.SpriteId == null) { icon = IconId.CInfo; }
                var partRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                partRow.Load(icon, text);
                UIUtils.AddTipFunc(lifetime, partRow, () => {
                    var sb = Caches.StringBuilder;
                    sb.Clear();
                    if (part.HidesMask != 0UL) {
                        sb.AppendFormat("Hides: {0}\n", string.Join(", ", part.HidesIds));
                    }
                    sb.AppendFormat("PosFilter: {0} ({1})\n",
                        part.PosFilter, part.PosFilterH);
                    return sb.ToString();
                });
            }

            var clearCacheBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            clearCacheBtn.Load(IconId.CWarning, "Clear Cache", T.Execute,
                comp.ClearCache);

            var rebuildBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            rebuildBtn.Load(IconId.CWarning, "Rebuild", T.Execute,
                () => comp.Rebuild(force: true));

            var rebuildHidesBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            rebuildHidesBtn.Load(IconId.CWarning, "Rebuild Hides", T.Execute,
                comp.DebugRebuildHides);

            var setHiddenBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            setHiddenBtn.Load(IconId.CWarning, "Set Hidden", T.Execute,
                () => being.Being.Graphics.SetHidden());

            var setVisibleBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            setVisibleBtn.Load(IconId.CWarning, "Set Visible", T.Execute,
                () => being.Being.Graphics.SetVisible());

            var reinitBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            reinitBtn.Load(IconId.CWarning, "Reinit Renderers", T.Execute,
                () => ctx.S.Sys.Rend.Reinit());
        }
    }
#endif
}
