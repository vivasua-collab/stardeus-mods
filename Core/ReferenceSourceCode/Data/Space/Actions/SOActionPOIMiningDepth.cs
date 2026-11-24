using Game.Constants;
using Game.CodeGen;
using Game.Data.Planets;
using Game.Utils;
using KL.Utils;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionPOIMiningDepth : SOAction, ITickListener {
        public const string ActionId = "POIMiningDepth";

        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionPOIMiningDepth());
        }

        private B<string> textB;

        protected override void OnFlush() {
            textB = null;
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (ctx.SO.Type.IdH == SpaceObjectTypeIdH.ResourceUnknown) {
                return false;
            }
            if (ctx.S.Sys.Planets.IsMinableResource(ctx.SO, out _, out var mat)) {
                return mat.Deposit.IsWithDepth;
            }
            return false;
        }

        public override bool AddOptions(QuickChoice qc) {
            var dynText = new QuickChoice.DynamicText() {
                TextB = textB = new B<string>(string.Empty),
                Tip = T.MiningDepthTip,
            };
            Tick(0);
            qc.AddDynamicText(textB.Value, dynText);
            return true;
        }

        public void Tick(long currentTick) {
            if (textB == null) {
                return;
            }
            if (ctx.S.Sys.Planets.TryGetResourceFor(ctx.SO, out var resource, out _)) {
                UpdateDepth(resource);
            }
        }

        private void UpdateDepth(PlanetResource resource) {
            textB.Set($"{T.NounDepth}: {Units.Depth(resource.MiningDepth)}");
        }

        public override string Id => ActionId;

        public override int UIPriority => 201;
    }
}
