using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Systems.TileAnim;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
#if !PRODUCTION_BUILD
    public class SandboxBlock_TileAnim : SandboxBlock {
        public const string BlockId = "TileAnim";
        public override int Order => -20;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() => Register(new SandboxBlock_TileAnim());

        public override bool ShowFor(GameState s, Tile tile) {
            return tile.HasComponent<TileAnimationComp>();
        }

        public override void LoadContent(SandboxContext ctx, WGroup group, ref int gIdx) {
            var tile = ctx.Tile;
            if (tile == null || !tile.TryGetComponent<TileAnimationComp>(out var comp)) { return; }

            var anim = comp.DebugAnim;
            var lifetime = ctx.Lifetime;
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load("TileAnim");
            UIUtils.AddTipFunc(lifetime, header, () => D.DumpInstance(anim));

            if (tile.Transform.IsRotatable) {
                var facingRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                facingRow.Load(IconId.CInfo, $"Facing: {tile.Transform.Rotation}");
            }

            var oX = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            oX.Item.Load(IconId.CWarning, "Offset X", anim.PosOffset.x.ToString("F3"));
            oX.Slider.Load(anim.PosOffset.x, (v) => {
                anim.PosOffset = new UnityEngine.Vector2((float)v, anim.PosOffset.y);
                oX.Item.SetText2(anim.PosOffset.x.ToString("F3"));
            }, -3f, 3f);

            var oY = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            oY.Item.Load(IconId.CWarning, "Offset Y", anim.PosOffset.y.ToString("F3"));
            oY.Slider.Load(anim.PosOffset.y, (v) => {
                anim.PosOffset = new UnityEngine.Vector2(anim.PosOffset.x, (float)v);
                oY.Item.SetText2(anim.PosOffset.y.ToString("F3"));
            }, -3f, 3f);

            var sX = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            sX.Item.Load(IconId.CInfo, "Size X", anim.Size.x.ToString("F3"));
            sX.Slider.Load(anim.Size.x, (v) => {
                anim.Size = new UnityEngine.Vector2((float)v, anim.Size.y);
                sX.Item.SetText2(anim.Size.x.ToString("F3"));
            }, 0.25f, 5f);

            var sY = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            sY.Item.Load(IconId.CInfo, "Size Y", anim.Size.y.ToString("F3"));
            sY.Slider.Load(anim.Size.y, (v) => {
                anim.Size = new UnityEngine.Vector2(anim.Size.x, (float)v);
                sY.Item.SetText2(anim.Size.y.ToString("F3"));
            }, 0.25f, 5f);

            var spd = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            spd.Item.Load(IconId.CHaul, "Speed", anim.Speed.ToString("F3"));
            spd.Slider.Load(anim.Speed, (v) => {
                anim.Speed = (float)v;
                spd.Item.SetText2(anim.Speed.ToString("F3"));
            }, 0.25f, 3f);

            if (anim.NeedsEnergy) {
                var runAlwaysBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                runAlwaysBtn.Load(IconId.CEnergy, "Run Always", T.Execute,
                    () => anim.NeedsEnergy = false);
            }

            var toggleRegBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            toggleRegBtn.Load(IconId.CEnergy,
                anim.IsRegistered ? "Unregister" : "Register", T.Execute,
                () => comp.RegisterAnim(!anim.IsRegistered));
            UIUtils.AddTipFunc(lifetime, toggleRegBtn,
                () => anim.IsRegistered ? "Registered" : "Unregistered");
        }
    }
#endif
}
