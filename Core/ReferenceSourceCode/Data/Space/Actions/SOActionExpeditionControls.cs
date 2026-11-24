using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionExpeditionControls : SOAction {
        public const string ActionId = "ExpeditionControls";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionExpeditionControls());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            var so = ctx.SO;
            if (so.Type.IdH != SpaceObjectTypeIdH.Shuttle) {
                return false;
            }

            var exp = ctx.S.Sys.Expedition.ForShuttleSO(so);
            return exp != null;
        }

        public override bool AddOptions(QuickChoice qc) {
            var exp = ctx.S.Sys.Expedition.ForShuttleSO(ctx.SO);
            var btn = new QuickChoice.IconButton() {
                Icon = Images.Sprite(GraphicsId.ObjectsDevicesShuttleFlying_R),
                AutoClose = true,
                OnClick = () => ctx.S.UIQuery.ShowExpeditions.Ask(true),
                TipFunc = () => exp.CurrentStage.TitleTooltip,
            };
            qc.AddIconButton(T.Expedition, btn);
            if (exp.CurrentStage.CanCallBack(exp)) {
                btn = new QuickChoice.IconButton() {
                    Icon = Images.Sprite(GraphicsId.ObjectsDevicesShuttleFlying_L),
                    AutoClose = true,
                    OnClick = () => ctx.S.Sys.Expedition.CallExpeditionBack(exp),
                };
                qc.AddIconButton(T.CallBack, btn);
            }

            return true;
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
