using Game.CodeGen;
using Game.Constants;
using Game.Data.Planets;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;

namespace Game.Data.Space {
    public sealed class SOActionPOIMiningDifficulty : SOAction, ITickListener {
        public const string ActionId = "POIMiningDifficulty";

        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionPOIMiningDifficulty());
        }

        private B<string> textB;

        protected override void OnFlush() {
            textB = null;
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (ctx.SO.Type.IdH == SpaceObjectTypeIdH.ResourceUnknown) {
                return false;
            }
            return ctx.S.Sys.Planets.IsMinableResource(ctx.SO, out _, out _);
        }

        public override bool AddOptions(QuickChoice qc) {
            var dynText = new QuickChoice.DynamicText() {
                TextB = textB = new B<string>(string.Empty),
                Tip = T.MiningDifficultyInfo,
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
                UpdateDifficulty(resource);
            }
        }

        private void UpdateDifficulty(PlanetResource resource) {
            textB.Set(T.MiningDifficultyValue_V1.F(
                resource.MiningDifficulty.ToString("0.0")));
        }

        public override string Id => ActionId;

        public override int UIPriority => 200;
    }
}
