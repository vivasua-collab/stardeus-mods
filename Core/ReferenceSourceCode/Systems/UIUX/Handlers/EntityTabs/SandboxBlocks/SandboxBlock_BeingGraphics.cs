using System.Reflection;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
#if !PRODUCTION_BUILD
    public class SandboxBlock_BeingGraphics : SandboxBlock {
        public const string BlockId = "BeingGraphics";
        public override int Order => -30;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() => Register(new SandboxBlock_BeingGraphics());

        public override bool ShowFor(GameState s, BeingData being) {
            return being.Being != null && being.Being.HasComponent<BeingGraphicsComp>();
        }

        public override void LoadContent(SandboxContext ctx, WGroup group, ref int gIdx) {
            var being = ctx.Being;
            if (being?.Being == null
                    || !being.Being.TryGetComponent<BeingGraphicsComp>(out var comp)) {
                return;
            }

            var lifetime = ctx.Lifetime;
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load("Graphics Debug");

            // --- Regular graphics debug ---
            var vertBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            vertBtn.Load(IconId.WMove,
                comp.IsVertical ? "Vertical" : "Horizontal", T.Toggle, () => {
                    comp.SetVertical(!comp.IsVertical);
                });

            var facingBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            facingBtn.Load(IconId.WMove, $"Facing: {comp.CurrentFacing}", T.Toggle, () => {
                comp.SetFacing(Facing.Rotate(comp.CurrentFacing, Facing.Type.Left));
            });

            var rebuildBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            rebuildBtn.Load(IconId.CWrench2, "SpriteSet", "Rebuild", () => {
                comp.ClearLooksCache();
                comp.RebuildLooks(force: true);
            });
            UIUtils.AddTipFunc(lifetime, rebuildBtn,
                () => D.DumpInstance(comp.SpriteSet, true));

            var offsetRow = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            offsetRow.Load(IconId.CMove, "Offset",
                comp.DebugPositionOffset.ToString(), () => {
                    comp.SetOffset(UnityEngine.Vector2.zero);
                });

            if (comp.Carrying != null) {
                var carryRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                carryRow.Load(
                    comp.Carrying.Definition.Preview ?? IconId.CCross,
                    comp.Carrying.ToString());
                UIUtils.AddTip(lifetime, carryRow, "Carrying");
            }

            var spriteInfoRow = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContent2Text, block.Container, idx++);
            spriteInfoRow.Load(IconId.CInfo, "Sprite Info");
            UIUtils.AddTipFunc(lifetime, spriteInfoRow, () => D.DumpInstance(comp.SpriteSet,
                flags: BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic));

            if (comp.SpriteSet?.Slots?.Length > 0) {
                var slotRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                slotRow.Load(IconId.CInfo, "Sprite Slots");
                UIUtils.AddTipFunc(lifetime, slotRow, () => D.DumpInstance(
                    comp.SpriteSet.Slots[0],
                    flags: BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic));
            }
        }
    }
#endif
}
