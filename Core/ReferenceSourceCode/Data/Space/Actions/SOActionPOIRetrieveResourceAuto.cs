using System;
using Game.CodeGen;
using Game.Constants;
using Game.Input;
using Game.Systems.Planets;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;

namespace Game.Data.Space {
    public sealed class SOActionPOIRetrieveResourceAuto : SOAction {
        public const string ActionId = "POIRetrieveResourceAuto";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionPOIRetrieveResourceAuto());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (!ctx.S.BB.Get(BBKeyH.MiningAuto, false) && !A.InstantBuild) {
                return false;
            }
            if (ctx.S.Sys.Planets.TryGetResourceFor(ctx.SO, out var resource, out _)) {
                if (resource.Unretrieved >= 3f) {
                    return ctx.S.Sys.Copters?.HasMissionFor(ctx.SO) != true;
                }
                return false;
            }
            return false;
        }

        public override bool AddOptions(QuickChoice qc) {
            var btn = new QuickChoice.IconButton() {
                Icon = Images.Sprite(IconId.CCopter),
                AutoClose = true,
                OnClick = OrderCollect,
                TipFunc = ShowTooltip,
            };
            qc.AddIconButton(T.VerbCollect, btn);
            return true;
        }

        private void OrderCollect() {
            if (!ctx.S.Sys.Planets.TryGetResourceFor(ctx.SO, out var resource, out _)) {
                UISounds.PlayDigitalCancel();
                return;
            }
            if (ctx.SO.S.Sys.Copters.TryOrderCollect(resource)) {
                UISounds.PlayDigitalConfirm();
                return;
            }
            UISounds.PlayDigitalCancel();
        }

        private string ShowTooltip() {
            if (!ctx.S.Sys.Planets.TryGetResourceFor(ctx.SO, out var resource, out var mt)) {
                return T.VerbCollect;
            }
            return $"{T.TempStorage}\n{PlanetsSys.TooltipForResource(in resource, mt)}";
        }

        public override string Id => ActionId;

        public override int UIPriority => 174;
    }
}
