using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
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
    public sealed class ShipHailAction_AskFuel : ShipHailAction, IShipHailAction_P2N,
            IShipHailAction_N2P {
        public const string ActId = "AskFuel";
        public override string Id => ActId;
        public static readonly int ActIdH = Hashes.S(ActId);

        [SelfInit]
        private static void Register() {
            Register(new ShipHailAction_AskFuel());
        }

        public void P2N_LoadHailNPCBlock(ShipHailActionUI ui, NPCShip target, Lifetime life, RectTransform container, ref int idx) {
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(GraphicsId.ObjMaterialsFusionCell, T.VerbAskFuel, () => {
                if (TradeUI.FindTradePortal(ui.S) == null) {
                    ui.S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {TradePortalComp.NotAvailable}");
                    return;
                }
                OnAskForFuel_P2N(target, ui);
            });
            UIUtils.AddTipFunc(life, btn, () => {
                var chance = CalculateChanceFor(target);
                return $"{T.ChanceAccept}: {Units.PercentageColored(chance)}";
            });
        }

        private void OnAskForFuel_P2N(NPCShip target, ShipHailActionUI ui) {
            // NPC should choose if they want to give us fuel here
            float chance = CalculateChanceFor(target);

            if (CheckActionPossibility(target, ui, ShipChatTopic.FuelResponse,
                    target.SeededRng(Primes.Prime057), chance)) {
                if (!CanFlyToPlayerOrIsNearby(target, ui)) { return; }
                NPCShipBehavior_Sector_ExchangeGift.AssignTo(target, A.Player,
                    GiftDirection.PlayerToNPC, GiftMode.Fuel);
            } else {
                NPCShipBehavior_Sector_ExchangeGift.DelayNextGift(target);
            }
        }

        private float CalculateChanceFor(NPCShip target) {
            Curve curve = Tunables.NPCShipGiveFuel_AttitudeChance;
            float chance;
            if (IsAttackingUs(target)) {
                chance = 0f;
            } else {
                chance = curve.Evaluate(target.AttitudeTowardsPlayer);
                var ownedFuel = A.S.Sys.Inventory.CountOf(
                    The.Defs.FastGet(DefIdsH.ObjMaterialsFusionCell));
                var hsState = A.S.Sys.ShipNav.HyperspaceStateFor(
                    SOUtils.GetPlayerSector(A.S).SO.Parent);
                ownedFuel += hsState.Jumps.Value;
                if (ownedFuel == 0) {
                    if (chance < 0.5f) {
                        chance = Mathf.Lerp(0.5f, 0.75f, chance);
                    }
                } else if (ownedFuel < 3) {
                    if (chance < 0.25f) {
                        chance = Mathf.Lerp(0.33f, 0.5f, chance);
                    }
                }
            }

            return chance;
        }

        public bool P2N_IsCompatible(NPCShip target) {
            if (target.IsInCombat) { return false; }
            if (!CanFlyToPlayerOrIsNearby(target)) {
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
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(GraphicsId.ObjMaterialsFusionCell, T.VerbAccept, () => {
                NPCShipBehavior_Sector_ExchangeGift.DoStartExchange_P2N(caller, GiftMode.Fuel);
            });
        }

        public void N2P_OnHailPlayer(ShipHailActionUI ui, NPCShip ship) {
            ui.SetResponse(
                isLastMsg: false,
                ShipChatTopic.FuelRequest, ShipChatResponseType.Neutral);
        }

        public void N2P_OnPlayerChoiceTimeout(NPCShip caller) {
            caller.Memory.SetFollowTarget(null);
            caller.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
        }
    }
}
