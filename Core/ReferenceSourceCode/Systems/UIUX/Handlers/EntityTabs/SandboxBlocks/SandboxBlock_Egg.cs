using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Story.Events;
using Game.Systems.Items;
using Game.Systems.Story;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
#if !PRODUCTION_BUILD
    public class SandboxBlock_Egg : SandboxBlock {
        public const string BlockId = "Egg";
        public override int Order => -50;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() => Register(new SandboxBlock_Egg());

        public override bool ShowFor(GameState s, ObjData obj) {
            return obj?.Obj != null && obj.Obj.HasComponent<EggComp>();
        }

        public override void LoadContent(SandboxContext ctx, WGroup group, ref int gIdx) {
            var objData = ctx.Obj;
            if (objData?.Obj == null
                    || !objData.Obj.TryGetComponent<EggComp>(out var comp)) {
                return;
            }

            var lifetime = ctx.Lifetime;
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load("Egg Debug");

            var progressRow = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContent2Text, block.Container, idx++);
            progressRow.Load(IconId.CInfo,
                $"Hatch: {comp.DebugHatchProgress:P0} / Discovered: {comp.DebugIsDiscovered}");

            var hatchBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            hatchBtn.Load(IconId.CWarning, "Force Hatch Crawlers", T.Execute, () => {
                var data = StoryEventData.CreateFor(
                    StoryEventDef.Get("Egg/EggHatchCrawlers"));
                data.TargetObj = objData;
                ctx.S.Query.StartStoryEvent.Ask(data);
            });
        }
    }
#endif
}
