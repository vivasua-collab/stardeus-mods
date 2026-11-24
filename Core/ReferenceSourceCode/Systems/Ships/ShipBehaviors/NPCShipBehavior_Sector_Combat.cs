using System;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Diplomacy;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    /// <summary>
    /// This behavior doesn't have assignment def, because it will be assigned
    /// manually from code when needed
    /// </summary>
    public sealed class NPCShipBehavior_Sector_Combat : NPCShipBehavior {
        public const string BehaviorId = "Sector_Combat";
        public const string Key_Bool_SurrenderOffered = "surrender.offered";
        public const string Key_Long_AttackAvailableAfterTick = "attack.after.tick";
        /// <summary>
        /// Set when this ship was dispatched by ShipHailAction_Insult.DoRetaliate rather
        /// than by a direct player attack, so arrival routes through the capped
        /// JoinShipCombatAgainstPlayer commit instead of the uncapped StartShipCombatWith.
        /// Persisted (NPCShipMemoryData.Custom is serialized), so the intent survives a
        /// save/load taken while the ship is still mid-flight.
        /// </summary>
        public const string Key_Bool_IsReinforcement = "combat.reinforcement";
        public static int BehaviorIdH = Hashes.S(BehaviorId);
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Sector_Combat());
        }
        public override string Id => BehaviorId;
        public override NPCShipBehaviorScope Scope => NPCShipBehaviorScope.Sector;
        public override bool CanHyperjump => false;
        public override bool CanInterruptForNotice(NPCShip observer, NPCShip target) {
            return false;
        }
        public override bool IsBusy(NPCShip ship, out string message) {
            if (ship.IsInCombat) {
                message = null;
                return false;
            }
            var target = ship.Memory.FollowTarget;
            var targetName = target.Ship?.NameWithTag ?? target.StarmapLink;
            message = $"{Texts.Accented(T.AdjBusy)}: {T.VerbApproaching_V1.F(targetName)}";
            return true;
        }

        public override void OnEnd(NPCShip ship) {
            ship.StopMoving(true);
            if (ship.Memory.HasCustom) {
                ship.Memory.Custom.RemoveBool(Key_Bool_SurrenderOffered);
                ship.Memory.Custom.RemoveBool(Key_Bool_IsReinforcement);
            }
        }

        public override void OnLoad(NPCShip ship) {
            // D.Warn("Loading combat behavior: {0}: IsNear {1}", ship,
            //     ship.Memory.IsNearFollowTarget);
            if (!ship.Memory.IsNearFollowTarget) {
                SetOnFollowTarget(ship);
            }
        }

        public static bool AssignTo(NPCShip ship, NPCShip target) {
            if (ship.Faction == target.Faction) {
                D.Err("Won't assign attack behavior for two ships of same faction: {0} -> {1}",
                    ship, target);
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, true);
                return false;
            }
            if (CheckCanReach(ship, target.SO)) {
                ship.SetBehavior(Registries.NPCShipBehaviors.FastGet(BehaviorIdH),
                    NPCShipBehaviorScope.Sector);
                ship.Memory.SetFollowTarget(target.SO);
                ship.Memory.ChaseTargetId = target.Id;
                SetOnFollowTarget(ship);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Same dispatch as AssignTo, flagged as a reinforcement so arrival routes
        /// through the enemy-cap-checked JoinShipCombatAgainstPlayer commit rather than
        /// the uncapped StartShipCombatWith. Used by ShipHailAction_Insult.DoRetaliate -
        /// a plain AssignTo there would let retaliators arrive uncapped even though the
        /// dispatch itself respected the budget.
        /// </summary>
        public static bool AssignAsReinforcement(NPCShip ship, NPCShip target) {
            if (!AssignTo(ship, target)) { return false; }
            ship.Memory.Custom.SetBool(Key_Bool_IsReinforcement, true);
            return true;
        }

        public static void AssignFollow(NPCShip ship, NPCShip target) {
            ship.SetBehavior(Registries.NPCShipBehaviors.FastGet(BehaviorIdH),
                NPCShipBehaviorScope.Sector);
            if (ship.NumThrustersActual > 0) {
                ship.Memory.FollowTarget = target.SO;
                ship.Memory.ChaseTargetId = target.Id;
            }
        }

        private static void SetOnFollowTarget(NPCShip ship) {
            ship.Memory.OnReachFollowTarget = (ship, target) => {
                // D.Warn("Reached target: {0}->{1}", ship, target);
                if (target.Ship?.IsPlayer == true) {
                    if (ship.Memory.TryCustom(Key_Bool_IsReinforcement, out bool reinforcement)
                            && reinforcement) {
                        // dispatched under the enemy cap by DoRetaliate - arrival must
                        // re-check that cap too, or the budget only held on departure
                        if (!target.S.Query.JoinShipCombatAgainstPlayer.Ask(ship)) {
                            ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                        }
                    } else {
                        target.S.Query.StartShipCombatWith.Ask(ship, false);
                    }
                } else {
                    target.S.Query.StartNPCShipCombatBetween.Ask(ship, target.Ship);
                }
            };
            ship.Memory.OnReachFollowTargetFail = (ship, target) => {
                // do something else
                D.ErrSoft("Ship failed to attack: {0}->{1}", ship, target);
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
            };
        }

        public override void DoTurn(NPCShip ship) {
            if (!TryFollow(ship)) {
                bool endCombat = false;
                if (ship.Memory.FollowTarget == null) {
                    D.ErrSoft("No follow target, ending combat: {0}", ship);
                    endCombat = true;
                } else if (ship.Memory.FollowTarget.Parent != ship.SO.Parent) {
                    D.Err("Follow target in different parent, ending combat: {0}", ship, ship.Memory.FollowTarget);
                    endCombat = true;
                } else {
                    var wantPos = ship.NearbyPosFrom(ship.Memory.FollowTarget.Location, out var isNear);
                    if (!isNear && Vectors.SqrDistance(ship.LastKnownPosition, wantPos) > 9f) {
                        D.Err("Follow target too far, ending combat: {0}", ship, ship.Memory.FollowTarget);
                        endCombat = true;
                    }

                }
                if (endCombat) {
                    if (ship.IsInCombat) {
                        A.S.Query.EndShipCombatWith.Ask(ship, ShipStateFlags.None);
                    }
                    if (ship.IsDerelict || ship.Class.IsSpaceStation) {
                        NPCShipBehavior_Sector_Idle.AssignTo(ship);
                    } else {
                        ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                    }
                }
            }
        }

        public static void OfferSurrender_P2N(ShipHailActionUI ui, NPCShip target) {
            if (target.Memory.TryCustom(Key_Bool_SurrenderOffered, out bool offered)) {
                if (offered) {
                    D.Err("Trying to offer surrender more than once!");
                    ui.SetResponse(
                        isLastMsg: false,
                        ShipChatTopic.SurrenderResponse, ShipChatResponseType.Negative);
                    return;
                }
            }
            target.Memory.Custom.SetBool(Key_Bool_SurrenderOffered, true);
            var rng = target.SeededRng(Primes.Prime025);
            var surrenderChance = CalculateSurrenderChance(target, A.Player.Ship);
            if (rng.Chance(surrenderChance)) {
                ui.SetResponse(
                    isLastMsg: false,
                    ShipChatTopic.ExtortionRequest, ShipChatResponseType.Neutral);

                NPCShipBehavior_Sector_ExchangeGift.DoStartExchange_P2N(target,
                    GiftMode.SurrenderRansom);
            } else {
                ui.SetResponse(
                    isLastMsg: false,
                    ShipChatTopic.SurrenderResponse, ShipChatResponseType.Negative);
            }
        }

        public static bool CanOfferSurrenderToPlayer(NPCShip target) {
            if (target.Memory.TryCustom(Key_Bool_SurrenderOffered, out bool offered)) {
                if (offered) {
                    return false;
                }
            }
            return true;
        }
        public static bool CanPlayerAttack(NPCShip target) {
            if (target.Memory.TryCustom(Key_Long_AttackAvailableAfterTick, out long after)) {
                if (after > 0L) {
                    return G.Ticks > after;
                }
            }
            return true;
        }

        public static void SetPlayerAttackDelay(NPCShip target, int ticks) {
            target.Memory.Custom.SetLong(Key_Long_AttackAvailableAfterTick, G.Ticks + ticks);
        }

        public static float CalculateSurrenderChance(NPCShip target, NPCShip ship) {
            var attitude = target.AttitudeTowards(ship.Faction);
            Curve chanceCurve = Tunables.NPCShipSurrenderAccept_AttitudeChance;
            return chanceCurve.Evaluate(attitude) + Mathf.Clamp01(target.Faction.Friendliness * 0.15f);
        }

        public static void CleanupMemoryFor(NPCShip target) {
            if (target.Memory.HasCustom) {
                target.Memory.Custom.RemoveBool(Key_Bool_SurrenderOffered);
            }
        }
    }
}
