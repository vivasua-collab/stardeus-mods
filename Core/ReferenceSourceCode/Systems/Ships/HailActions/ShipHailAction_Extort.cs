using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Diplomacy;
using Game.Systems.Trade;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipHailAction_Extort : ShipHailAction, IShipHailAction_P2N,
            IShipHailAction_N2P {
        public const string ActId = "Extort";
        public override string Id => ActId;
        public static readonly int ActIdH = Hashes.S(ActId);

        [SelfInit]
        private static void Register() {
            Register(new ShipHailAction_Extort());
        }

        public void P2N_LoadHailNPCBlock(ShipHailActionUI ui, NPCShip target, Lifetime life, RectTransform container, ref int idx) {
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(IconId.CFist, T.VerbExtort, () => {
                if (TradeUI.FindTradePortal(ui.S) == null) {
                    ui.S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {TradePortalComp.NotAvailable}");
                    return;
                }
                OnExtort_P2N(target, ui);
            });
            UIUtils.AddTipFunc(life, btn, () => {
                var powerDiff = target.StrengthDifferenceAgainst(A.Player.Ship);
                float refuseChance = CalculateRefuseChance(target, powerDiff);
                var chance = 1f - refuseChance;
                return $"{T.ChanceAccept}: {Units.PercentageColored(chance)}";
            });
        }

        private void OnExtort_P2N(NPCShip target, ShipHailActionUI ui) {
            // NPC should choose if they want to give us fuel here
            var rng = target.SeededRng(Primes.Prime028);
            var powerDiff = target.StrengthDifferenceAgainst(A.Player.Ship);
            float refuseChance = CalculateRefuseChance(target, powerDiff);

            target.ChangeAttitudeTowards(FactionDef.Player,
                Rng.URange(-0.01f, -0.02f));
            if (rng.Chance(refuseChance)) {
                var attChance = target.Faction.BraveryCurve.Evaluate(powerDiff);
                if (rng.Chance(attChance)) {
                    if (!CanFlyToPlayerOrIsNearby(target, ui)) { return; }
                    DelayNext(target);

                    // approach the play and begin combat
                    NPCShipBehavior_Sector_Combat.AssignTo(target, A.Player.Ship);
                    // ignore
                    ui.SetResponse(
                        isLastMsg: true,
                        ShipChatTopic.CombatEngage,
                        ShipChatResponseType.Neutral);
                } else {
                    DelayNext(target);
                    // ignore
                    ui.SetResponse(
                        isLastMsg: true,
                        ShipChatTopic.ExtortionResponse,
                        ShipChatResponseType.Negative);
                }
            } else {
                if (!CanFlyToPlayerOrIsNearby(target, ui)) { return; }
                DelayNext(target);
                NPCShipBehavior_Sector_ExchangeGift.AssignTo(target, A.Player,
                    GiftDirection.PlayerToNPC, GiftMode.Extortion);
                ui.SetResponse(
                    isLastMsg: true,
                    ShipChatTopic.ExtortionResponse,
                    ShipChatResponseType.Positive);
            }
        }

        private float CalculateRefuseChance(NPCShip target, float powerDiff) {
            float refuseChance;

            if (IsAttackingUs(target)) {
                refuseChance = 1f;
            } else {
                refuseChance = target.Faction.BraveryCurve.Evaluate(powerDiff);
            }

            return refuseChance;
        }

        private static void DelayNext(NPCShip target) {
            NPCShipBehavior_Sector_ExchangeGift.DelayNextGift(target);
            NPCShipBehavior_Sector_ExchangeGift.DelayNextExtortion(target);
            NPCShipBehavior_Sector_Combat.SetPlayerAttackDelay(
                target, Consts.TicksPer24Hours);
        }

        public bool P2N_IsCompatible(NPCShip target) {
            if (target.IsInCombat) { return false; }
            if (!CanFlyToPlayerOrIsNearby(target)) {
                return false;
            }
            if (!NPCShipBehavior_Sector_Combat.CanPlayerAttack(target)) {
                return false;
            }
            if (target.Memory.HasCustom) {
                if (target.Memory.TryCustom(
                        NPCShipBehavior_Sector_ExchangeGift.Key_Long_GiftAvailableAfter,
                            out long availableAfter)) {
                    return G.Ticks > availableAfter;
                }
            }
            return true;
        }

        public static void HailPlayer(NPCShip ship) {
            var act = Registries.ShipHailActions.FastGet(ActIdH);
            ShipHailActionUI.For(ship.SO.S, ship, true, act)?.HailPlayer(act);
        }

        public void N2P_LoadPlayerChoice(ShipHailActionUI actUI, NPCShip caller, Lifetime life, RectTransform container, ref int idx) {
            actUI.IsWaitingForPlayerChoice = true;
            actUI.S.Clock.ForceStop(ForceStopReason.Choice);
            var acceptBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            acceptBtn.Load(IconId.CStarcreds, T.VerbAccept, () => {
                actUI.IsWaitingForPlayerChoice = false;
                actUI.S.Clock.ForceResume(ForceStopReason.Choice);
                NPCShipBehavior_Sector_ExchangeGift.DoStartExchange_P2N(caller, GiftMode.Extortion);
            });
            var refuseBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            refuseBtn.Load(IconId.CFist, T.VerbRefuse, () => {
                actUI.IsWaitingForPlayerChoice = false;
                actUI.S.Clock.ForceResume(ForceStopReason.Choice);
                actUI.S.Query.StartShipCombatWith.Ask(caller, true);
                actUI.SetResponse(
                    isLastMsg: true,
                    ShipChatTopic.CombatEngage,
                    ShipChatResponseType.Neutral);
            });
        }

        public void N2P_OnHailPlayer(ShipHailActionUI ui, NPCShip ship) {
            ui.SetResponse(
                isLastMsg: false,
                ShipChatTopic.ExtortionRequest, ShipChatResponseType.Neutral);
            ui.IsWaitingForPlayerChoice = true;
        }

        public void N2P_OnPlayerChoiceTimeout(NPCShip caller) {
            caller.SO.S.Clock.ForceResume(ForceStopReason.Choice);
            if (caller.IsInHyperspace || A.IsInHyperspace || caller.IsInCombat) {
                return;
            }
            // Starting combat will end call, which will trigger the timeout
            // again immediately, so let's do this on next tick.
            // Also can't do OnNextTick because we are already there.
            A.S.Clock.ExecuteAfterTicks(1, () => {
                A.S.Query.StartShipCombatWith.Ask(caller, true);
            });
        }
    }
}
