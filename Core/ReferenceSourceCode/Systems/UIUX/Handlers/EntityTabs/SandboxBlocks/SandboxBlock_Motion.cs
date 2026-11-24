using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Motion;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
#if !PRODUCTION_BUILD
    public class SandboxBlock_Motion : SandboxBlock {
        public const string BlockId = "Motion";
        public override int Order => -70;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() => Register(new SandboxBlock_Motion());

        public override bool ShowFor(GameState s, BeingData being) {
            return being?.Being != null && being.Being.HasComponent<MotionComp>();
        }

        public override void LoadContent(SandboxContext ctx, WGroup group, ref int gIdx) {
            var being = ctx.Being;
            if (being?.Being == null
                    || !being.Being.TryGetComponent<MotionComp>(out var comp)) {
                return;
            }

            var lifetime = ctx.Lifetime;
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load("Motion Debug");

            var navRow = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContent2Text, block.Container, idx++);
            navRow.Load(IconId.CDestination, "Navigation Info");
            UIUtils.AddTipFunc(lifetime, navRow,
                () => comp.GetMotionDebugInfo());
        }
    }
#endif
}
