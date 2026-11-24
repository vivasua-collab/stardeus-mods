using System;
using Game.Constants;
using Game.Data.Planets;
using Game.Systems.Planets;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;

namespace Game.Data.Space {
    public sealed class SOActionPOIUnretrievedResource : SOAction, ITickListener {
        public const string ActionId = "POIUnretrievedResource";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionPOIUnretrievedResource());
        }

        private B<string> textB;

        protected override void OnFlush() {
            textB = null;
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (ctx.S.Sys.Planets.TryGetResourceFor(ctx.SO, out var resource, out _)) {
                return resource.Unretrieved >= 3f || resource.AutoMineSpeed > 0f;
            }
            return false;
        }

        public override bool AddOptions(QuickChoice qc) {
            if (ctx.S.Sys.Planets.TryGetResourceFor(ctx.SO, out var resource, out var mt)) {
                var dt = new QuickChoice.DynamicTextIcon() {
                    Icon = Images.Sprite(mt.IconId),
                    Tint = mt.IconTint,
                    TextB = textB = new B<string>(null),
                    TipFunc = ShowTooltip,
                };
                Tick(0);
                qc.AddDynamicTextIcon(textB.Value, dt);
                return true;
            }
            return false;
        }

        private static string ResourceTitle(in PlanetResource resource, MatType mt) {
            int unretrieved = (int) Math.Ceiling((double) resource.Unretrieved);
            if (resource.UnretrievedMax > 0) {
                return $"{mt.NameT} ({Units.XOutOfY(unretrieved, resource.UnretrievedMax)})";
            }
            return $"{mt.NameT} ({Units.Num(unretrieved)})";
        }

        private string ShowTooltip() {
            if (!ctx.S.Sys.Planets.TryGetResourceFor(ctx.SO, out var resource, out var mt)) {
                return T.TempStorage;
            }
            return $"{T.TempStorage}\n{PlanetsSys.TooltipForResource(in resource, mt)}";
        }

        public void Tick(long ticks) {
            if (ctx.S.Sys.Planets.TryGetResourceFor(ctx.SO, out var resource, out var mt)) {
                var text = ResourceTitle(resource, mt);
                textB.Set(text);
            }
        }

        public override string Id => ActionId;

        public override int UIPriority => 175;
    }
}
