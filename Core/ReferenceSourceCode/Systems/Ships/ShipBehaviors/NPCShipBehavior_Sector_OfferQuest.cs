using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Diplomacy;
using Game.Systems.Quests;
using Game.Systems.Trade;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    /// <summary>
    /// This behavior doesn't have assignment def, because it will be assigned
    /// manually from code when needed (from the reaction)
    /// </summary>
    public sealed class NPCShipBehavior_Sector_OfferQuest : NPCShipBehavior {
        public const string BehaviorId = "Sector_OfferQuest";
        public static int BehaviorIdH = Hashes.S(BehaviorId);
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Sector_OfferQuest());
        }
        public override string Id => BehaviorId;
        public override NPCShipBehaviorScope Scope => NPCShipBehaviorScope.Sector;
        public override bool CanHyperjump => false;
        public override bool CanInterruptForNotice(NPCShip observer, NPCShip target) {
            // a bit of special treatment for the player so NPCs will react
            // to player more when they are exchanging stuff with each other
            if (target == null) {
                return observer.Memory.FollowTarget?.Ship?.IsPlayer != true;
            }
            return target.IsPlayer && observer.Memory.FollowTarget != target.SO;
        }
        public override bool IsBusy(NPCShip ship, out string message) {
            if (ship.Memory.IsNearFollowTarget) {
                message = Texts.Accented(T.AdjBusy);
            } else {
                var target = ship.Memory.FollowTarget;
                var targetName = target.Ship?.NameWithTag ?? target.StarmapLink;
                message = $"{Texts.Accented(T.AdjBusy)}: {T.VerbApproaching_V1.F(targetName)}";
            }
            return true;
        }

        public static bool AssignTo(NPCShip ship, SpaceObject target) {
            if (CheckCanReach(ship, target)) {
                ship.SetBehavior(Registries.NPCShipBehaviors.Get(BehaviorId),
                    NPCShipBehaviorScope.Sector);
                ship.Memory.SetFollowTarget(target);
                return true;
            }
            return false;
        }

        public override void OnStart(NPCShip ship) {
            ship.Memory.IsNearFollowTarget = false;
            ship.Memory.OnReachFollowTarget = DoOfferQuest;
            ship.Memory.OnReachFollowTargetFail = FailReachTarget;
        }

        public override void OnLoad(NPCShip ship) {
            OnStart(ship);
            ship.Memory.IsNearFollowTarget = false;
        }

        public override void OnEnd(NPCShip ship) {
            ship.StopMoving(true);
        }

        private void DoOfferQuest(NPCShip ship, SpaceObject so) {
            if (so.Type.IdH == SpaceObjectTypeIdH.Player) {
                ship.HailUI?.HideHailPanel();
                A.S.Query.DelayNextNPCShipQuest.Ask(null);
                ShipHailAction_Quests.HailPlayer(ship);
            } else {
                D.Err("Offering quest NPC 2 NPC! Should never happen! {0} -> {1}", ship, so);
            }
        }

        public override void DoTurn(NPCShip ship) {
            TryFollow(ship);
        }
    }
}
