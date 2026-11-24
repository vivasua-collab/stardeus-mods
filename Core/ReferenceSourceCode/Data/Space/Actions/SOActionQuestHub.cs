using Game.CodeGen;
using Game.Constants;
using Game.Systems.Quests;
using Game.Systems.UIUX;
using Game.Utils;
using UnityEngine;

namespace Game.Data.Space {
    public sealed class SOActionQuestHub : SOAction {
        public const string ActionId = "QuestHub";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionQuestHub());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            var typeId = ctx.SO.Type.IdH;
            if (typeId == SpaceObjectTypeIdH.SpaceStation
                    || typeId == SpaceObjectTypeIdH.Port) {
                return ctx.S.Sys.Ship.IsNearby(ctx.SO);
            }
            return false;
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.NounQuestHub, () => ctx.S.UIQuery.ShowQuestHubFor.Ask(ctx.SO));
            return true;
        }

        // public override UDB GetUIBlock() {
        //     return UDB.Create(this, UDBT.DBtn, IconId.CQuestSide, T.NounQuestHub)
        //         .WithClickFunction(() => ctx.S.UIQuery.ShowQuestHubFor.Ask(ctx.SO));
        // }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
