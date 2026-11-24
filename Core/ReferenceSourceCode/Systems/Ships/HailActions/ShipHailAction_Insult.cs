using System;
using Game.Constants;
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
    public sealed class ShipHailAction_Insult : ShipHailAction, IShipHailAction_P2N {
        public const string ActId = "Insult";
        public override string Id => ActId;
        public static readonly int ActIdH = Hashes.S(ActId);
        private static ShipHailAction_Insult instance;

        [SelfInit]
        private static void Register() {
            Register(instance = new ShipHailAction_Insult());
        }

        public void P2N_LoadHailNPCBlock(ShipHailActionUI ui, NPCShip target, Lifetime life, RectTransform container, ref int idx) {
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(IconId.CMoodBad, T.VerbInsult, () => {
                OnInsult_P2N(target, ui);
            });
            UIUtils.AddTipFunc(life, btn, () => {
                var powerDiff = target.StrengthDifferenceAgainst(A.Player.Ship);
                float attackChance = AttackChance(target, powerDiff);
                return $"{T.NounRisk}: {Units.PercentageColoredInverse(attackChance)}";
            });
        }

        private void OnInsult_P2N(NPCShip target, ShipHailActionUI ui) {
            bool attacked = DoInsult(target, ui, asReinforcement: false);
            if (attacked) {
                DoRetaliate(target, false);
            }
        }

        public static void SectorShipsTryRetaliateFor(NPCShip target, bool forKill) {
            instance.DoRetaliate(target, forKill);
        }

        public static void Insult(NPCShip ship) {
            instance.DoInsult(ship, null, asReinforcement: false);
        }

        /// <summary>
        /// The existing same-faction/police pile-on that fires whenever the player
        /// attacks, insults, or kills a ship. Now shares the enemy-slot budget with
        /// NPCShipBehavior_Sector_JoinCombatAgainst's scan (see the "Enemy
        /// reinforcement" section of ShipCombatSys.cs): the budget already counts this
        /// target itself once it is on Sector_Combat / in Enemies, so re-asking before
        /// each candidate is self-correcting without a manual decrement or trusting
        /// DoInsult's return value (which reports true for a ship that was already
        /// attacking, too). The kill-revenge call site (forKill: true) is also gated by
        /// IsShipCombatPlayerInitiated in NPCShip.TurnIntoDerelict - see that gate's
        /// comment for why.
        /// </summary>
        private void DoRetaliate(NPCShip target, bool forKill) {
            var s = target.SO.S;
            var allShips = s.Query.NPCShipsAt.Ask(target.Sector);
            var player = A.Player.Ship;
            foreach (var ship in allShips) {
                if (s.Query.GetShipCombatEnemySlots.Ask() <= 0) { break; }
                if (ship.IsPlayer || ship == target) {
                    continue;
                }
                if (!NPCShip.CanNPCNotice(ship, player)) {
                    continue;
                }
                if (ship.IsPolice && target.IsOutlaw) { continue; }
                bool retaliate = false;
                if (ship.IsPolice && (forKill || target.IsPolice)) {
                    retaliate = true;
                }
                if (ship.Faction == target.Faction) {
                    // If target is an outlaw, only other outlaws of same faction should defend
                    if (target.IsOutlaw && !ship.IsOutlaw) {
                        // Don't defend outlaws unless we're also outlaws
                        continue;
                    }
                    retaliate = true;
                }
                if (ship.IsPolice && target.IsPolice) {
                    retaliate = true;
                }
                // never send a ship that is allied with the player or with a current
                // NPC ally - the same invariant ShipJoinAgainstMotive.GetMotive enforces
                // for the scan, just reached from a dispatch path that has no motive
                // system of its own. JoinCombatAgainstPlayer rechecks this again at
                // arrival, since it can change while the ship is flying over.
                if (retaliate && NPCShipBehavior_Sector_JoinCombatAgainst.IsAlliedWithPlayerSide(s, ship)) {
                    continue;
                }
                if (retaliate && DoInsult(ship, null, asReinforcement: true)) {
                    D.Warn("Ship {0} will retaliate for {1} (for kill: {2})",
                        ship, target, forKill);
                }
            }
        }

        /// <param name="ui">can be null if it's not a direct insult</param>
        /// <param name="asReinforcement">
        /// True when dispatched from DoRetaliate: the arrival routes through the
        /// enemy-cap-checked JoinShipCombatAgainstPlayer commit instead of the uncapped
        /// StartShipCombatWith, so the cap respected at dispatch still holds on arrival.
        /// </param>
        /// <returns>true if insult ended with attack</returns>
        private bool DoInsult(NPCShip target, ShipHailActionUI ui, bool asReinforcement) {
            // perhaps the ship is fighting someone else and this is indirect insult
            if (ui == null && target.IsInCombat) {
                return false;
            }

            NPCShipBehavior_Sector_ExchangeGift.DelayNextGift(target);
            if (ui != null) {
                NPCShipBehavior_Sector_ExchangeGift.DelayNextExtortion(target);
            }

            if (IsAttackingUs(target)) {
                if (ui == null) { return true; }
                if (target.IsInCombat) {
                    ui.SetResponse(
                        isLastMsg: true,
                        ShipChatTopic.ExtortionResponse,
                        ShipChatResponseType.Negative);
                } else {
                    ui.SetResponse(
                        isLastMsg: true,
                        ShipChatTopic.CombatEngage,
                        ShipChatResponseType.Neutral);
                }
                return true;
            }

            var powerDiff = target.StrengthDifferenceAgainst(A.Player.Ship);
            float attackChance = AttackChance(target, powerDiff);

            if (ui != null) {
                target.ChangeAttitudeTowards(FactionDef.Player,
                    Rng.URange(-0.001f, -0.005f));
            }

            var rng = Rng.Unseeded;
            if (rng.Chance(attackChance)) {
                if (ui != null && target.NumThrustersActual == 0 ) {
                    target.NearbyPosFrom(A.Player.Location, out var isNear);
                    if (!isNear) {
                        // ignore
                        ui.SetResponse(
                            isLastMsg: true,
                            ShipChatTopic.ExtortionResponse,
                            ShipChatResponseType.Negative);
                        return false;
                    }
                }
                // approach the play and begin combat
                // ui.S.Query.StartShipCombatWith.Ask(target);
                if (asReinforcement) {
                    NPCShipBehavior_Sector_Combat.AssignAsReinforcement(target, A.Player.Ship);
                } else {
                    NPCShipBehavior_Sector_Combat.AssignTo(target, A.Player.Ship);
                }
                if (ui != null && A.S.Clock.IsStopped) {
                    A.S.Clock.SetSpeed(ClockSpeed.Normal);
                }
                ui?.SetResponse(
                    isLastMsg: true,
                    ShipChatTopic.CombatEngage,
                    ShipChatResponseType.Neutral);
                return true;
            }
            if (ui != null) {
                if (A.Player.Ship.CanBeHostileTowards(target)) {
                    NPCShipBehavior_Sector_AvoidHostileShips.AssignTo(target);
                    ui.SetResponse(
                        isLastMsg: true,
                        ShipChatTopic.SurrenderRequest,
                        ShipChatResponseType.Neutral);
                } else {
                    ui.SetResponse(
                        isLastMsg: true,
                        ShipChatTopic.ExtortionResponse,
                        ShipChatResponseType.Negative);
                }
            }
            return false;
        }

        private static float AttackChance(NPCShip target, float powerDiff) {
            if (target.IsInCombat) { return 0f; }

            // Don't pile on if player is already outgunned
            var playerShip = A.Player.Ship;
            if (playerShip.IsInCombat) {
                var enemies = playerShip.SO.S.Query.GetEnemyShips.Ask();
                if (enemies != null) {
                    float totalEnemyStrength = 0f;
                    foreach (var e in enemies) {
                        totalEnemyStrength += e.CombatStrength;
                    }
                    if (totalEnemyStrength >= playerShip.CombatStrength * 0.8f) {
                        return 0f;
                    }
                }
            }

            var attackChance = target.Faction.BraveryCurve.Evaluate(powerDiff);
            var fear = target.FearOf(A.Player.Ship);
            attackChance = (attackChance + 1f - fear) / 2f;

            return attackChance;
        }

        public bool P2N_IsCompatible(NPCShip target) {
            if (CanFlyToPlayerOrIsNearby(target)) {
                return NPCShipBehavior_Sector_Combat.CanPlayerAttack(target);
            }
            return false;
        }
    }
}
