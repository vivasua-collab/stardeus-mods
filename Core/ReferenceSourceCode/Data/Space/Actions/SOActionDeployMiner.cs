using Game.Components;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionDeployMiner : SOAction {
        public const string ActionId = "DeployMiner";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionDeployMiner());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (!ctx.S.BB.Get(BBKeyH.MiningAuto, false) && !A.InstantBuild) {
                return false;
            }
            return ctx.S.Sys.Copters.CanDeployMinerTo(ctx.SO);
        }

        public override bool AddOptions(QuickChoice qc) {
            string tooltip = null;
            if (ctx.SO.Faction?.IsPlayer == false) {
                tooltip = Texts.Red(T.OwnedByFaction);
            }
            var btn = new QuickChoice.IconButton() {
                Icon = Images.Sprite(IconId.CCopter),
                OnClick = LaunchMiner,
                Tip = tooltip,
                AutoClose = true,
            };
            qc.AddIconButton(T.DeployResourceMiner, btn);
            return true;
        }

        private void LaunchMiner() {
            if (ctx.SO.Faction?.IsPlayer == false) {
                UISounds.PlayActionDenied();
                return;
            }
            if (ResourceMinerComp.FirstAvailable(ctx.S) == null) {
                ctx.S.Sig.ShowErrorMessage.Send(
                    $"{Texts.LinkToCodex(DefIdsH.ObjectsDevicesAutodrillRig)}: {T.NotAvailable}");
                return;
            }
            if (!ctx.S.Sys.Copters.HasAnyCopters()) {
                ctx.S.Sig.ShowErrorMessage.Send(
                    $"{Texts.LinkToCodex(DefIdsH.ObjectsDevicesCargoCopter)}: {T.NotAvailable}");
                return;
            }
            if (!ctx.S.Sys.Copters.TryOrderMinerDeploymentTo(ctx.SO)) {
                UISounds.PlayActionDenied();
                return;
            }
            UISounds.PlayDigitalConfirm();
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
