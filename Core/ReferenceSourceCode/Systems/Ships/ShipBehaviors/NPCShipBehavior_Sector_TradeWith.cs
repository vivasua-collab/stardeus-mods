using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    /// <summary>
    /// This behavior doesn't have assignment def, because it will be assigned
    /// manually from code when needed
    /// </summary>
    public sealed class NPCShipBehavior_Sector_TradeWith : NPCShipBehavior, ICancellableNPCShipBehavior {
        public const string BehaviorId = "Sector_TradeWith";
        public const string Key_Bool_TradeByPlayer = "trade.by.player";
        public static int BehaviorIdH = Hashes.S(BehaviorId);
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Sector_TradeWith());
        }
        public override string Id => BehaviorId;
        public override bool CanHyperjump => false;
        public override bool CanInterruptForNotice(NPCShip observer, NPCShip target) {
            // a bit of special treatment for the player so NPCs will react
            // to player more when they are exchanging stuff with each other
            if (target == null) {
                return observer.Memory.FollowTarget?.Ship?.IsPlayer != true;
            }
            return target.IsPlayer && observer.Memory.FollowTarget != target.SO;
        }
        public override NPCShipBehaviorScope Scope => NPCShipBehaviorScope.Sector;
        public override bool IsBusy(NPCShip ship, out string message) {
            var target = ship.Memory.FollowTarget;
            var targetName = target.Ship?.NameWithTag ?? target.StarmapLink;
            if (ship.Memory.IsNearFollowTarget) {
                message = T.VerbTradingWith_V1.F(targetName);
            } else {
                message = T.VerbApproaching_V1.F(targetName);
            }
            return true;
        }

        public static bool AssignTo(NPCShip ship, SpaceObject target,
                bool initiatedByPlayer) {
            if (CheckCanReach(ship, target)) {
                ship.SetBehavior(Registries.NPCShipBehaviors.Get(BehaviorId),
                    NPCShipBehaviorScope.Sector);
                ship.Memory.SetFollowTarget(target);
                ship.Memory.Custom.SetBool(Key_Bool_TradeByPlayer, initiatedByPlayer);
                return true;
            }
            return false;
        }

        public override void OnStart(NPCShip ship) {
            ship.Memory.OnReachFollowTarget = StartTrade;
            ship.Memory.OnReachFollowTargetFail = FailReachTarget;
        }
        public override void OnLoad(NPCShip ship) {
            OnStart(ship);
            ship.Memory.IsNearFollowTarget = false;
        }

        public override void OnEnd(NPCShip ship) {
            ship.StopMoving(true);
        }

        private void StartTrade(NPCShip ship, SpaceObject so) {
            if (so.Type.IdH == SpaceObjectTypeIdH.Player) {
                ship.Memory.TryCustom(Key_Bool_TradeByPlayer, out bool byPlayer);
                if (byPlayer) {
                    DoStartTrade(ship, so);
                } else {
                    ShipHailAction_Trade.HailPlayer(ship);
                }
                // DoStartTrade(ship, so);
            } else {
                if (ship.Faction != so.Ship.Faction) {
                    so.S.Query.ChangeFactionAttitude.Ask(
                        ship.Faction, so.Ship.Faction, Rng.URange(0.05f, 0.1f));
                    so.S.Query.ChangeFactionAttitude.Ask(
                        so.Ship.Faction, ship.Faction, Rng.URange(0.05f, 0.1f));
                }
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
            }
        }

        public static void DoStartTrade(NPCShip ship, SpaceObject so) {
            if (!ship.IsPlayer && so.Ship?.IsPlayer == true) {
                ship.HailUI?.EndCall();
            }
            NPCShipBehavior_Sector_Idle.AssignTo(ship);
            var tradeUI = so.S.Query.StartTrade.Ask(ship.SO.Trader);
            if (tradeUI == null || tradeUI.IsComplete) {
                NPCShipBehavior_Sector_Orbit.AssignTo(ship);
                return;
            }
            tradeUI.OnEnd = (ui) => {
                NPCShipBehavior_Sector_Orbit.AssignTo(ship);
            };
        }

        public override void DoTurn(NPCShip ship) {
            TryFollow(ship);
        }

        public void LoadCancelBlock(ShipHailActionUI ui, NPCShip ship, Lifetime life, RectTransform container, ref int idx) {
            if (ship.Memory.FollowTarget?.Ship?.IsPlayer != true) {
                return;
            }
            if (ship.Memory.TryCustom(Key_Bool_TradeByPlayer, out bool byPlayer)) {
                if (!byPlayer) {
                    return;
                }
            }

            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContentTextButton, container, idx++);
            btn.Load(IconId.CStarcreds, T.OngoingTrade, T.Cancel, () => {
                ship.ChangeAttitudeTowards(FactionDef.Player, -0.001f);
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                ui.EndCall();
            });
        }
    }
}
