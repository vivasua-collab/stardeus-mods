using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
#if !PRODUCTION_BUILD
    public class SandboxBlock_VFX : SandboxBlock {
        public const string BlockId = "VFX";
        public override int Order => -10;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() => Register(new SandboxBlock_VFX());

        public override bool ShowFor(GameState s, Tile tile) {
            return tile.IsConstructed && tile.HasComponent<VFXComp>();
        }

        public override void LoadContent(SandboxContext ctx, WGroup group, ref int gIdx) {
            var tile = ctx.Tile;
            if (tile == null || !tile.TryGetComponent<VFXComp>(out var comp)) { return; }

            var lifetime = ctx.Lifetime;
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load("VFX");

            var vfxIdRow = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContent2Text, block.Container, idx++);
            vfxIdRow.Load(IconId.CInfo, comp.DebugVFXId ?? "(none)");

            var particleRow = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContent2Text, block.Container, idx++);
            particleRow.Load(IconId.CSparkles, comp.Particle?.ToString() ?? "(no particle)");

            if (tile.Transform.IsRotatable) {
                var facingRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                facingRow.Load(IconId.CInfo, $"Facing: {tile.Transform.Rotation}");
            }

            var offsets = comp.DebugOffsets;
            if (offsets != null) {
                for (int i = 0; i < offsets.Length; i++) {
                    var isActive = tile.Transform.IsRotatable
                        && (int)tile.Transform.Rotation == i;
                    var offRow = UISpawn.Widget<WIconText>(lifetime,
                        UIPrefabId.WContent2Text, block.Container, idx++);
                    offRow.Load(isActive ? IconId.CCheck : IconId.CCross,
                        $"Offset_{i}: {offsets[i]}");
                }
            }

            var oX = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            oX.Item.Load(IconId.CWarning, "Offset X", comp.Offset.x.ToString("F3"));
            oX.Slider.Load(comp.Offset.x, (v) => {
                comp.RemoveParticle();
                comp.Offset = new Vector2((float)v, comp.Offset.y);
                oX.Item.SetText2(comp.Offset.x.ToString("F3"));
                comp.AddParticle();
            }, -3f, 3f);

            var oY = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            oY.Item.Load(IconId.CWarning, "Offset Y", comp.Offset.y.ToString("F3"));
            oY.Slider.Load(comp.Offset.y, (v) => {
                comp.RemoveParticle();
                comp.Offset = new Vector2(comp.Offset.x, (float)v);
                oY.Item.SetText2(comp.Offset.y.ToString("F3"));
                comp.AddParticle();
            }, -3f, 3f);

            var toggleBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            toggleBtn.Load(comp.Particle?.Visibility > 0f ? IconId.CCheck : IconId.CCross,
                "Toggle Visibility", T.Execute, () => {
                    if (comp.Particle != null) {
                        comp.Particle.Visibility = comp.Particle.Visibility > 0f ? 0f : 1f;
                    }
                });

            var removeBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            removeBtn.Load(IconId.CSparkles, "Remove Particle", T.Execute, comp.RemoveParticle);

            var addBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            addBtn.Load(IconId.CSparkles, "Add Particle", T.Execute, comp.AddParticle);

            var reinitBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            reinitBtn.Load(IconId.CSparkles, "Reinit Renderer", T.Execute,
                () => ctx.S.Sys.Rend.Reinit());
        }
    }
#endif
}
