using Game.CodeGen;
using Game.Constants;
using Game.Systems.UIUX;
using UnityEngine;

using Game.Utils;
namespace Game.Data.Space {
    public sealed class SOActionShowTrail : SOAction {
        public const string ActionId = "ShowTrail";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionShowTrail());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            return ctx.SO.Type.IdH == SpaceObjectTypeIdH.ShipHyperTrail;
        }


        public override bool AddOptions(QuickChoice qc) {
            var btn = new QuickChoice.Button() {
                AutoClose = true,
                OnClick = ShowTrail,
                TipFunc = () => {
                    var sb = Caches.StringBuilder;
                    sb.Clear();
                    var trail = ctx.SO.S.Query.FindHyperTrail.Ask(ctx.SO);
                    trail?.LoadInfo(sb);
                    return sb.ToString();
                },
            };
            qc.AddButton(T.Show, btn);
            return true;
        }

        private void ShowTrail() {
            var trail = ctx.S.Query.FindHyperTrail.Ask(ctx.SO);
            if (trail == null) { return; }
            var trailTo = trail.To ?? trail.From;

            ctx.S.UIQuery.StarmapFocusOn.Ask(trailTo.SO, true);
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
