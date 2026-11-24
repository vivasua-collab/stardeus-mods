using System;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Diplomacy;
using Game.Systems.Quests;
using Game.Systems.Trade;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipHailAction_Quests : ShipHailAction,
            IShipHailAction_P2N, IShipHailAction_N2P {
        public const string ActId = "Quests";
        public override string Id => ActId;
        public static readonly int ActIdH = Hashes.S(ActId);

        [SelfInit]
        private static void Register() {
            Register(new ShipHailAction_Quests());
        }

        public void P2N_LoadHailNPCBlock(ShipHailActionUI ui, NPCShip target, Lifetime life, RectTransform container, ref int idx) {
            var quest = A.S.Query.GetQuestFor.Ask(target, false, false);
            string title = T.VerbAskQuest;
            string icon = IconId.CQuestSide;
            if (quest == null || quest.Data.State == QuestState.Undefined) {
            } else if (quest.IsActive) {
                for (int i = quest.Steps.Length - 1; i >= 0; i--) {
                    QuestStep step = quest.Steps[i];
                    if (step.IsActive && step.Check is IInteractiveQuestStep iStep) {
                        if (iStep.CanInteractWith(target.SO, out icon, out title)) {
                            break;
                        }
                    }
                }
            }
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(icon, title, () => {
                OnAskAboutQuests_P2N(target, ui);
            });
        }

        private void OnAskAboutQuests_P2N(NPCShip target, ShipHailActionUI ui) {
            var quest = A.S.Query.GetQuestFor.Ask(target, true, false);
            if (quest == null) {
                ui.SetResponse(
                    isLastMsg: true,
                    ShipChatTopic.QuestResponse,
                    ShipChatResponseType.Negative);
                return;
            }

            if (quest.IsActive) {
                for (int i = quest.Steps.Length - 1; i >= 0; i--) {
                    QuestStep step = quest.Steps[i];
                    if (step.IsActive && step.Check is IInteractiveQuestStep iStep) {
                        if (iStep.InteractWithShip(target, ui)) {
                            return;
                        }
                    }
                }
                // we've accepted the quest but we're clicking the ask about quest button again!
                ui.ShowHailPanel();
                return;
            }

            ui.SetResponse(
                isLastMsg: true,
                ShipChatTopic.QuestResponse,
                ShipChatResponseType.Positive);
            A.S.UIQuery.ShowNewQuest.Ask(quest);
        }

        public bool P2N_IsCompatible(NPCShip target) {
            if (target.IsInCombat) {
                return false;
            }
            if (!target.IsQuestGiver || target.NextQuestAvailableAt > G.Ticks) {
                return false;
            }
            var quest = A.S.Query.GetQuestFor.Ask(target, false, false);
            if (quest == null || quest.Data.State == QuestState.Undefined) {
                return true;
            }
            if (quest.IsEnded) {
                return false;
            }
            if (quest.IsActive) {
                for (int i = quest.Steps.Length - 1; i >= 0; i--) {
                    QuestStep step = quest.Steps[i];
                    if (step.IsActive && step.Check is IInteractiveQuestStep iStep) {
                        if (iStep.CanInteractWith(target.SO, out _, out _)) {
                            return true;
                        }
                    }
                }
            }
            // quest is ongoing
            return false;
        }
        public static void HailPlayer(NPCShip ship) {
            var act = Registries.ShipHailActions.FastGet(ActIdH);
            ShipHailActionUI.For(ship.SO.S, ship, true, act)?.HailPlayer(act);
        }

        public void N2P_LoadPlayerChoice(ShipHailActionUI ui, NPCShip caller, Lifetime life, RectTransform container, ref int idx) {
            ui.IsWaitingForPlayerChoice = true;
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(IconId.CQuestSide, T.VerbShowQuest, () => {
                var quest = A.S.Query.GetQuestFor.Ask(caller, true, false);
                if (quest == null) {
                    D.Err("Quest not available when it should for N2P! {0}", caller);
                    return;
                }
                ui.EndCall();
                A.S.UIQuery.ShowNewQuest.Ask(quest);
            });
        }

        public void N2P_OnHailPlayer(ShipHailActionUI ui, NPCShip ship) {
            ui.SetResponse(
                isLastMsg: false,
                ShipChatTopic.QuestRequest, ShipChatResponseType.Neutral);
        }

        public void N2P_OnPlayerChoiceTimeout(NPCShip caller) {
            // TODO impact relationships
            caller.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
        }
    }
}
