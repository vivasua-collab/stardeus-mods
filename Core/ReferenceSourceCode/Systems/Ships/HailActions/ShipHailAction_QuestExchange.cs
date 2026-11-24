using System;
using Game.Components;
using Game.Constants;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Diplomacy;
using Game.Systems.Quests;
using Game.Systems.Trade;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipHailAction_QuestExchange : ShipHailAction, IShipHailAction_P2N {
        public const string ActId = "QuestExchange";
        public override string Id => ActId;
        public static readonly int ActIdH = Hashes.S(ActId);

        [SelfInit]
        private static void Register() {
            Register(new ShipHailAction_QuestExchange());
        }

        public void P2N_LoadHailNPCBlock(ShipHailActionUI ui, NPCShip target, Lifetime life, RectTransform container, ref int idx) {
            var quest = A.S.Query.GetQuestFor.Ask(target, false, false);
            if (quest?.IsActive != true) {
                D.Err("Quest inactive or null! {0}", quest);
                return;
            }
            string title = T.VerbAskQuest;
            string icon = IconId.CQuestSide;
            for (int i = quest.Steps.Length - 1; i >= 0; i--) {
                QuestStep step = quest.Steps[i];
                if (step.IsActive && step.Check is IExchangeWithShipQuestStep xStep) {
                    if (xStep.CanExchangeWithShip(target, out icon, out title)) {
                        break;
                    }
                }
            }
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(icon, title, () => {
                if (TradeUI.FindTradePortal(ui.S) == null) {
                    ui.S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {TradePortalComp.NotAvailable}");
                    return;
                }
                OnHailNPC(target, ui);
            });
        }

        private void OnHailNPC(NPCShip target, ShipHailActionUI ui) {
            if (!CanFlyToPlayerOrIsNearby(target, ui)) { return; }
            ui.SetResponse(isLastMsg: false, ShipChatTopic.QuestDeliveryResponse,
                ShipChatResponseType.Neutral);
            NPCShipBehavior_Sector_ExchangeGift.AssignTo(target, A.Player,
                GiftDirection.PlayerToNPC, GiftMode.Quest);
        }

        public bool P2N_IsCompatible(NPCShip target) {
            var quest = A.S.Query.GetQuestFor.Ask(target, false, false);
            if (quest?.IsActive == true) {
                for (int i = quest.Steps.Length - 1; i >= 0; i--) {
                    QuestStep step = quest.Steps[i];
                    if (step.IsActive && step.Check is IExchangeWithShipQuestStep xStep) {
                        if (xStep.CanExchangeWithShip(target, out _, out _)) {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
