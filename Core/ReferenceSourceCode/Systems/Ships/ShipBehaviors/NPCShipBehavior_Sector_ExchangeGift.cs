using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Creatures;
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
    /// manually from code when needed
    /// </summary>
    public sealed class NPCShipBehavior_Sector_ExchangeGift : NPCShipBehavior,
            ICancellableNPCShipBehavior {
        public const string BehaviorId = "Sector_ExchangeGift";
        public const string Key_Int_GiftDirection = "gift.direction";
        public const string Key_Int_GiftMode = "gift.mode";
        public const string Key_Long_ExtortionAvailableAfter = "extortion.available.after";
        public const string Key_Long_NPCGiftAvailableAfter = "npc.gift.available.after";
        public const string Key_Long_GiftAvailableAfter = "gift.available.after";
        public static int BehaviorIdH = Hashes.S(BehaviorId);
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Sector_ExchangeGift());
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

        public static bool AssignTo(NPCShip ship, SpaceObject target,
                GiftDirection direction, GiftMode mode) {
            if (CheckCanReach(ship, target)) {
                ship.SetBehavior(Registries.NPCShipBehaviors.Get(BehaviorId),
                    NPCShipBehaviorScope.Sector);
                ship.Memory.SetFollowTarget(target);
                ship.Memory.Custom.SetInt(Key_Int_GiftMode, (int) mode);
                ship.Memory.Custom.SetInt(Key_Int_GiftDirection, (int) direction);
                return true;
            }
            return false;
        }

        public override void OnStart(NPCShip ship) {
            ship.Memory.IsNearFollowTarget = false;
            ship.Memory.OnReachFollowTarget = StartExchange;
            ship.Memory.OnReachFollowTargetFail = FailReachTarget;
        }

        public override void OnLoad(NPCShip ship) {
            OnStart(ship);
            ship.Memory.IsNearFollowTarget = false;
        }

        public override void OnEnd(NPCShip ship) {
            ship.StopMoving(true);
        }

        private void StartExchange(NPCShip ship, SpaceObject so) {
            if (so.Type.IdH == SpaceObjectTypeIdH.Player) {
                ship.HailUI?.HideHailPanel();
                ship.Memory.TryCustomEnum<GiftMode>(Key_Int_GiftMode, out var mode);
                ship.Memory.TryCustomEnum<GiftDirection>(Key_Int_GiftDirection, out var dir);
                if (dir == GiftDirection.PlayerToNPC) {
                    if (mode == GiftMode.Fuel || mode == GiftMode.Extortion) {
                        DoStartExchange_N2P(ship, mode);
                    } else {
                        DoStartExchange_P2N(ship, mode);
                    }
                } else {
                    if (mode == GiftMode.Fuel) {
                        ShipHailAction_AskFuel.HailPlayer(ship);
                    } else if (mode == GiftMode.Extortion) {
                        ShipHailAction_Extort.HailPlayer(ship);
                    } else {
                        ShipHailAction_Gift.HailPlayer(ship);
                    }
                }
            } else {
                // D.Warn("Exchanging gifts between NPC ships: {0} -> {1}",
                //     ship, so.Ship);
                if (ship.Faction != so.Ship.Faction) {
                    so.S.Query.ChangeFactionAttitude.Ask(
                        ship.Faction, so.Ship.Faction, Rng.URange(0.05f, 0.1f));
                    so.S.Query.ChangeFactionAttitude.Ask(
                        so.Ship.Faction, ship.Faction, Rng.URange(0.05f, 0.1f));
                }
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
            }
        }

        public static void DoStartExchange_P2N(NPCShip ship, GiftMode mode) {
            D.Warn("Starting P2N exchange: {0}", mode);
            NPCShipBehavior_Sector_Idle.AssignTo(ship);
            var s = ship.SO.S;

            if (mode == GiftMode.Fuel) {
                var ownedFuel = s.Sys.Inventory.CountOf(
                    The.Defs.FastGet(DefIdsH.ObjMaterialsFusionCell));
                if (ownedFuel == 0) {
                    s.Sig.ShowErrorMessage.Send(T.NoFuel);
                    D.Warn("Aborting fuel exchange, no fuel on player's hands");
                    return;
                }
            }

            ship.HailUI?.HideHailPanel();

            var ui = GiftChoiceUI.For(s, ship, mode, GiftDirection.PlayerToNPC);

            if (mode == GiftMode.Quest) {
                var quest = s.Query.GetQuestFor.Ask(ship, false, false);
                if (quest == null) {
                    D.Err("Quest not found for P2N exchange");
                    ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                    return;
                }
                bool added = false;
                for (int i = quest.Steps.Length - 1; i >= 0; i--) {
                    var step = quest.Steps[i];
                    if (step.IsActive && step.Check is IExchangeWithShipQuestStep xStep) {
                        added |= xStep.AddQuestPackageOptions(ship, ui, GiftDirection.PlayerToNPC);
                        if (added) {
                            break;
                        }
                    }
                }

                if (!added) {
                    D.Err("Quest couldn't add package for exchange {0}", quest);
                } else {
                    ShowPackageChoice(ship, mode, s, ui);
                }

                return;
            }

            if (mode == GiftMode.SendBeings) {
                DoStartExchange_SendBeings(ship, s, ui);
                return;
            }


            var packager = ui.Generator;

            float maxAttChance = Tunables.GiftMaxFactionAttitudeChange;
            float maxAttChanceNeg = Tunables.GiftMinFactionAttitudeChange;

            var rng = ship.SeededRng(Primes.Prime078);

            var numOpts = Tunables.GiftPackageOptions;

            var facText = T.RelationshipWith_V1.F(Texts.Accented(ship.Faction.NameWithGlyph));
            for (int i = 0; i < numOpts; i++) {
                var opt = packager.Create(i, numOpts);
                float attChange;
                switch (mode) {
                    case GiftMode.Gift: {
                        if (i == 0) {
                            attChange = 0.1f * rng.Range(0.9f, 1.1f);
                        } else if (i == 1) {
                            attChange = 0.05f * rng.Range(0.9f, 1.1f);
                        } else {
                            attChange = 0.02f * rng.Range(0.9f, 1.1f);
                        }
                        break;
                    }
                    case GiftMode.Fuel: {
                        if (i == 0) {
                            attChange = 0.05f;
                        } else if (i == 1) {
                            attChange = 0.03f;
                        } else {
                            attChange = 0.01f;
                        }
                        break;
                    }
                    case GiftMode.Extortion: {
                        attChange = 0.03f * Mathf.Clamp01(opt.ValuePercent);
                        break;
                    }
                    case GiftMode.SurrenderRansom: {
                        attChange = 0.03f;
                        break;
                    }
                    default: {
                        attChange = maxAttChance * opt.ValuePercent
                            * rng.Range(0.9f, 1.1f);
                        break;
                    }
                }
                float attChangeNeg = maxAttChanceNeg
                    * Mathf.Clamp01(1f - opt.ValuePercent)
                    * rng.Range(0.9f, 1.1f);
                if (mode == GiftMode.Extortion) {
                    attChangeNeg = -0.03f;
                } else if (mode == GiftMode.SurrenderRansom) {
                    attChangeNeg = -0.005f;
                }

                // top (most expensive) package has a very low chance of rejection
                // which means that most likely we will lose the items
                // but let's have a chance for a ship with positive attitude towards us
                // to let us keep the gift but still give positive attitude

                if (i == 0 && ship.AttitudeTowardsPlayer > 0f) { // top package
                    if (mode != GiftMode.SurrenderRansom && mode != GiftMode.Extortion) {
                        attChangeNeg = maxAttChance * rng.Range(0.9f, 1.1f);
                    }
                }

                var acceptText = $"{facText}: {Units.PercentageSigned(attChange, 2, true)}";
                opt.WithAccept((p) => {
                    // on accept action
                    ship.ChangeAttitudeTowards(FactionDef.Player, attChange);
                }, GiftPackage.NewConsequence(IconId.CCheck, acceptText));

                if (mode == GiftMode.Extortion) {
                    var rejectText = $"{facText}: {Units.PercentageSigned(attChangeNeg, 2, true)}";

                    opt.WithReject((p) => {
                        ship.ChangeAttitudeTowards(FactionDef.Player, attChangeNeg);
                        ui.S.Query.StartShipCombatWith.Ask(ship, false);
                    },
                    GiftPackage.NewConsequence(IconId.CCross, rejectText),
                    GiftPackage.NewConsequence(IconId.CAttackTarget, T.NounCombat));
                } else if (mode == GiftMode.SurrenderRansom) {

                    opt.WithAccept((p) => {
                        ship.ChangeAttitudeTowards(FactionDef.Player, attChange);
                        ui.S.Query.EndShipCombatWith.Ask(ship, ShipStateFlags.Active);
                        ship.Memory.ChaseTargetId = 0;
                    },
                    GiftPackage.NewConsequence(IconId.CCheck, acceptText),
                    GiftPackage.NewConsequence(IconId.CPeace, T.NounEndOfCombat));

                    var rejectText = $"{facText}: {Units.PercentageSigned(attChangeNeg, 2, true)}";
                    opt.WithReject((p) => {
                        ship.ChangeAttitudeTowards(FactionDef.Player, attChangeNeg);
                    },
                    GiftPackage.NewConsequence(IconId.CCross, rejectText),
                    GiftPackage.NewConsequence(IconId.CAttackTarget, T.NounCombat));

                } else if (mode != GiftMode.Fuel) {
                    var rejectText = $"{facText}: {Units.PercentageSigned(attChangeNeg, 2, true)}";
                    opt.WithReject((p) => {
                        ship.ChangeAttitudeTowards(FactionDef.Player, attChangeNeg);
                    }, GiftPackage.NewConsequence(IconId.CCross, rejectText));
                } else {
                    // fuel will always be accepted
                    opt.AcceptChance = 1f;
                }
                ui.AddOption(opt);
            }

            if (mode == GiftMode.Gift) {
                var noGift = packager.CreateNoGift();
                var attChangeNeg = -0.005f;
                var rejectText = $"{facText}: {Units.PercentageSigned(attChangeNeg, 2, true)}";
                noGift.WithReject((p) => {
                    ship.ChangeAttitudeTowards(FactionDef.Player, attChangeNeg);
                }, GiftPackage.NewConsequence(IconId.CCross, rejectText));
                ui.AddOption(noGift);
            }

            ShowPackageChoice(ship, mode, s, ui);
        }

        /// <summary>
        /// The player is handing guests of the hailed ship's faction back home in one
        /// package (SendBeingsGift), paid out by the receiving ship on accept.
        /// </summary>
        private static void DoStartExchange_SendBeings(NPCShip ship, GameState s,
                GiftChoiceUI ui) {
            var payer = ship.WillTrade ? ship.SO.Trader : null;
            var pack = SendBeingsGift.CreatePackage(s, ship.Faction, ship, payer, out _);
            if (pack == null) {
                // The guests died, left or got quest-locked while the ship was flying over.
                D.Warn("No sendable guests left for SendBeings exchange {0}", ship);
                var actUI = ShipHailActionUI.For(s, ship, false, null);
                if (actUI != null) {
                    actUI.SetResponse(isLastMsg: true,
                        ShipChatTopic.Generic, ShipChatResponseType.Negative);
                    actUI.ShowHailPanel();
                }
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                return;
            }

            // Declining after the ship flew over for them costs a little standing
            var facText = T.RelationshipWith_V1.F(Texts.Accented(ship.Faction.NameWithGlyph));
            var noGift = ui.Generator.CreateNoGift();
            var attChangeNeg = -0.005f;
            var rejectText = $"{facText}: {Units.PercentageSigned(attChangeNeg, 2, true)}";
            noGift.WithReject((p) => {
                ship.ChangeAttitudeTowards(FactionDef.Player, attChangeNeg);
            }, GiftPackage.NewConsequence(IconId.CCross, rejectText));

            ui.AddOption(pack);
            ui.AddOption(noGift);

            ShowPackageChoice(ship, GiftMode.SendBeings, s, ui);
        }

        private static void ShowPackageChoice(NPCShip ship,
                GiftMode mode, GameState s, GiftChoiceUI ui) {
            s.Clock.ForceStop(ForceStopReason.Choice);
            var shown = s.UIQuery.ShowGiftChoice.Ask(ui.Choice, (selected) => {
                // Refused before the gift delay starts, so the player can hail again
                // once the items are back (a hauler carrying a pile makes it short)
                if (!selected.CanPay(s)) {
                    s.Sig.ShowErrorMessage.Send(
                        $"{T.OperationFailed}: {T.SigTransactionFailedLackItems}");
                    s.Clock.ForceResume(ForceStopReason.Choice);
                    return;
                }
                DelayNextGift(ship);
                var rng = ship.SeededRng(Primes.Prime011);
                var actUI = ShipHailActionUI.For(s, ship, false, null);
                if (selected.Offer(s, rng)) {
                    if (mode == GiftMode.Quest) {
                        actUI?.SetResponse(
                            isLastMsg: true,
                            ShipChatTopic.QuestDeliveryResponse,
                            ShipChatResponseType.Positive);
                    } else {
                        actUI?.SetResponse(
                            isLastMsg: true,
                            ShipChatTopic.GiftResponse,
                            ShipChatResponseType.Positive);
                    }
                } else {
                    if (mode == GiftMode.Extortion) {
                        actUI?.SetResponse(
                            isLastMsg: true,
                            ShipChatTopic.CombatEngage,
                            ShipChatResponseType.Neutral);
                    } else {
                        actUI?.SetResponse(
                            isLastMsg: true,
                            ShipChatTopic.GiftResponse,
                            ShipChatResponseType.Negative);
                    }
                }
                if (actUI != null) {
                    actUI.ShowHailPanel();
                }
                s.Clock.ForceResume(ForceStopReason.Choice);
            });
            if (!shown) {
                s.Clock.ForceResume(ForceStopReason.Choice);
                D.Err("Failed to show gift choice widget, another modal is active");
            }
        }

        public static void DelayNextGift(NPCShip ship) {
            ship.Memory.Custom.SetLong(Key_Long_GiftAvailableAfter,
                G.Ticks + Tunables.GiftIntervalHours * Consts.TicksPerHour);
        }
        public static void DelayNextExtortion(NPCShip ship) {
            ship.Memory.Custom.SetLong(Key_Long_ExtortionAvailableAfter,
                G.Ticks + Tunables.GiftIntervalHours * Consts.TicksPerHour);
        }

        public static void DoStartExchange_N2P(NPCShip ship, GiftMode mode) {
            // D.Warn("Starting N2P exchange: {0} | {1}", mode, ship);
            var s = ship.SO.S;
            var tp = TradeUI.FindTradePortal(s);
            if (tp == null) {
                s.Sig.ShowErrorMessage.Send($"{T.TradeFailed}: {TradePortalComp.NotAvailable}");
                return;
            }
            ship.HailUI?.EndCall();
            var ui = GiftChoiceUI.For(s, ship, mode, GiftDirection.NPCToPlayer);

            var packager = ui.Generator;

            // idea: if you choose the cheapest package, you'll get faction relationships bonus
            // if you choose the most expensive package, faction relationship will suffer a bit
            // the mid package will not change the relationship


            var rng = ship.SeededRng(Primes.Prime044);

            int opts = 3;
            switch (mode) {
                case GiftMode.SurrenderRansom:
                case GiftMode.Extortion:
                case GiftMode.Gift: {
                    opts = Tunables.GiftPackageOptions;
                    break;
                }
                case GiftMode.Fuel: {
                    opts = Tunables.FuelPackageOptions;
                    break;
                }
                case GiftMode.Quest: {
                    opts = 0;
                    break;
                }
                default: {
                    D.Err("Unhandled package options for mode: {0}", mode);
                    break;
                }
            }

            if (mode == GiftMode.Quest) {
                var quest = s.Query.GetQuestFor.Ask(ship, false, false);
                if (quest == null) {
                    D.Err("Quest not found for exchange");
                    return;
                }
                bool added = false;
                for (int i = quest.Steps.Length - 1; i >= 0; i--) {
                    var step = quest.Steps[i];
                    if (step.IsActive && step.Check is IExchangeWithShipQuestStep xStep) {
                        added |= xStep.AddQuestPackageOptions(ship, ui, GiftDirection.NPCToPlayer);
                        if (added) {
                            break;
                        }
                    }
                }

                if (!added) {
                    D.Err("Quest couldn't add package for exchange {0}", quest);
                    return;
                }
            }



            for (int i = 0; i < opts; i++) {
                var opt = packager.Create(i, opts);
                float attChange = AttitudeChangeForOpt_N2P(rng, i, opt);
                var facText = T.RelationshipWith_V1.F(Texts.Accented(ship.Faction.NameWithGlyph));
                var acceptText = $"{facText}: {Units.PercentageSigned(attChange, 2, true)}";
                if (attChange != 0f) {
                    opt.WithAccept((p) => {
                        // on accept action
                        ship.ChangeAttitudeTowards(FactionDef.Player, attChange);
                    }, GiftPackage.NewConsequence(IconId.CCheck, acceptText));
                }
                ui.AddOption(opt);
            }

            s.Clock.ForceStop(ForceStopReason.Choice);
            var shown = s.UIQuery.ShowGiftChoice.Ask(ui.Choice, (selected) => {
                ship.Memory.Custom.SetLong(
                    Key_Long_NPCGiftAvailableAfter,
                    G.Ticks + Tunables.GiftIntervalHours * Consts.TicksPerHour);
                var rng = ship.SeededRng(Primes.Prime049);
                selected.Take(s, ship, rng);
                if (mode == GiftMode.SurrenderRansom) {
                    NPCShipBehavior_Sector_Combat.SetPlayerAttackDelay(
                        ship, Consts.TicksPer24Hours);
                    s.Query.EndShipCombatWith.Ask(ship, ShipStateFlags.None);
                }
                NPCShipBehavior_Sector_Orbit.AssignTo(ship);
                s.Clock.ForceResume(ForceStopReason.Choice);
            });
            if (!shown) {
                s.Clock.ForceResume(ForceStopReason.Choice);
                D.Err("Failed to show gift choice widget, another modal is active");
            }
        }

        private static float AttitudeChangeForOpt_N2P(Rng rng, int i, GiftPackage opt) {
            float maxAttChanceNeg = Tunables.GiftMinFactionAttitudeChange;
            switch (opt.Mode) {
                case GiftMode.Extortion: {
                    return maxAttChanceNeg * Mathf.Clamp01(opt.ValuePercent)
                        * rng.Range(1.8f, 2.2f);
                }
                case GiftMode.SurrenderRansom: {
                    if (i == 0) {
                        return -0.05f;
                    } else if (i == 1) {
                        return -0.03f;
                    } else {
                        return -0.01f;
                    }
                }
                case GiftMode.Fuel: {
                    return maxAttChanceNeg * Mathf.Clamp01(opt.ValuePercent)
                        * rng.Range(0.9f, 1.1f);
                }
                case GiftMode.Gift: {
                    if (i == 0) {
                        return maxAttChanceNeg * Mathf.Clamp01(opt.ValuePercent)
                            * rng.Range(0.9f, 1.1f);
                    } else if (i == 1) {
                        return 0f;
                    } else {
                        return 0.02f;
                    }
                }
                default: {
                    D.Err("Unhandled gift mode: {0} | {1}", opt.Mode, opt);
                    return 0f;
                }
            }
        }

        public override void DoTurn(NPCShip ship) {
            TryFollow(ship);
        }

        public void LoadCancelBlock(ShipHailActionUI ui, NPCShip ship, Lifetime life, RectTransform container, ref int idx) {
            if (ship.Memory.FollowTarget?.Ship?.IsPlayer != true) {
                return;
            }

            if (ship.Memory.TryCustomEnum<GiftDirection>(Key_Int_GiftDirection, out var dir)) {
                if (dir != GiftDirection.PlayerToNPC) {
                    return;
                }
            }
            if (ship.Memory.TryCustomEnum<GiftMode>(Key_Int_GiftMode, out var mode)) {
                if (mode != GiftMode.Fuel && mode != GiftMode.Gift
                        && mode != GiftMode.SendBeings) {
                    return;
                }
            }

            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContentTextButton, container, idx++);
            btn.Load(IconId.CStop, T.Cancel, () => {
                // player changed their mind about giving a gift / sending beings
                ship.ChangeAttitudeTowards(FactionDef.Player, -0.001f);
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                ui.EndCall();
            });
        }
    }
}
