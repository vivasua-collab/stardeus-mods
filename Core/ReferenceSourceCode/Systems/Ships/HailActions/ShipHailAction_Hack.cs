using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Diplomacy;
using Game.Systems.Trade;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    /// <summary>
    /// Intrusion Suite: reaches into an NPC ship's systems through the comms channel
    /// we already opened to hail them. Deliberately a hail action rather than a sixth
    /// IShipDevice type - see Docs/Architecture/Plans/RogueAIExpansion.md. Rogue AI tech.
    /// </summary>
    public sealed class ShipHailAction_Hack : ShipHailAction, IShipHailAction_P2N {
        public const string ActId = "Hack";
        public override string Id => ActId;
        public static readonly int ActIdH = Hashes.S(ActId);

        public const string Key_Long_HackAvailableAfter = "hack.after.tick";

        /// <summary>
        /// Success chance against an evenly matched ship. Bigger ships carry more
        /// counter intrusion hardware and pull this down.
        /// </summary>
        private const float BaseChance = 0.6f;
        private const float StrengthWeight = 0.5f;

        private enum Outcome { FluxSurge, WeaponsOffline, Surrender, CrewTurned }

        [SelfInit]
        private static void Register() {
            Register(new ShipHailAction_Hack());
        }

        public bool P2N_IsCompatible(NPCShip target) {
            if (target.IsDerelict) { return false; }
            if (!A.S.Research.IsUnlocked(DefIdsH.ResearchRogueAIIntrusionSuite)) {
                return false;
            }
            if (target.Memory.TryCustom(Key_Long_HackAvailableAfter, out long after)) {
                return G.Ticks > after;
            }
            return true;
        }

        public void P2N_LoadHailNPCBlock(ShipHailActionUI ui, NPCShip target,
                Lifetime life, RectTransform container, ref int idx) {
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                container, idx++);
            btn.Load(IconId.CRobotEye, T.VerbHack, () => {
                if (IntrusionSuiteComp.Find(ui.S) == null) {
                    ui.S.Sig.ShowErrorMessage.Send(
                        $"{T.OperationFailed}: {IntrusionSuiteComp.NotAvailable}");
                    return;
                }
                OnHack_P2N(target, ui);
            });
            UIUtils.AddTipFunc(life, btn, () => {
                var chance = CalculateSuccessChance(target);
                return $"{T.ChanceSuccess}: {Units.PercentageColored(chance)}";
            });
        }

        public static float CalculateSuccessChance(NPCShip target) {
            var powerDiff = target.StrengthDifferenceAgainst(A.Player.Ship);
            return Mathf.Clamp(BaseChance - powerDiff * StrengthWeight, 0.05f, 0.95f);
        }

        private void OnHack_P2N(NPCShip target, ShipHailActionUI ui) {
            // Seeded on the tick as well as the ship, so hacking the same ship twice
            // does not replay the same outcome.
            var rng = new Rng(Hashes.Quick(target.Seed, (int) G.Ticks));
            var powerDiff = target.StrengthDifferenceAgainst(A.Player.Ship);
            var chance = CalculateSuccessChance(target);
            target.Memory.Custom.SetLong(Key_Long_HackAvailableAfter,
                G.Ticks + Consts.TicksPer24Hours);

            if (!rng.Chance(chance)) {
                OnHackFailed(target, ui, rng, powerDiff);
                return;
            }
            var outcome = RollOutcome(rng, powerDiff);
            if (outcome == Outcome.Surrender && TradeUI.FindTradePortal(ui.S) == null) {
                // A ransom needs somewhere for the goods to land.
                outcome = Outcome.WeaponsOffline;
            }
            var flux = target.CombatState?.Flux;
            if (outcome == Outcome.FluxSurge && flux == null) {
                outcome = Outcome.WeaponsOffline;
            }
            switch (outcome) {
                case Outcome.FluxSurge: {
                    flux.AddFlux(ui.S, flux.Capacity * Tunables.ShipHackFluxSurgePercent, 0f);
                    Report(ui, T.HackOutcomeFluxSurge);
                    break;
                }
                case Outcome.WeaponsOffline: {
                    var ticks = (int) (Tunables.ShipHackWeaponsOfflineHours * Consts.TicksPerHour);
                    target.SetWeaponsHackedUntil(G.Ticks + ticks);
                    Report(ui, T.HackOutcomeWeaponsOffline_V1.F(
                        Units.TicksToTime(ticks)));
                    break;
                }
                case Outcome.Surrender: {
                    Report(ui, T.HackOutcomeSurrender);
                    target.ChangeAttitudeTowards(FactionDef.Player, Rng.URange(-0.08f, -0.15f));
                    // They pay us. P2N is the player paying, the surrender we offer them
                    // (STA-1903). N2P ends the call and opens the ransom choice itself.
                    NPCShipBehavior_Sector_ExchangeGift.DoStartExchange_N2P(target,
                        GiftMode.SurrenderRansom);
                    return;
                }
                case Outcome.CrewTurned: {
                    Report(ui, T.HackOutcomeCrewTurned);
                    // Ends the call itself, and leaves the hull boardable.
                    target.TurnIntoDerelict(A.Player.Ship, crewAbandoned: true);
                    return;
                }
                default: {
                    D.Err("Unhandled hack outcome: {0}", outcome);
                    break;
                }
            }
            target.ChangeAttitudeTowards(FactionDef.Player, Rng.URange(-0.08f, -0.15f));
            ui.ShowHailPanel();
        }

        private static Outcome RollOutcome(Rng rng, float powerDiff) {
            // Out-teching the target shifts the roll towards the harsher outcomes.
            var roll = rng.Value + Mathf.Max(0f, -powerDiff) * 0.25f;
            if (roll < 0.45f) { return Outcome.FluxSurge; }
            if (roll < 0.75f) { return Outcome.WeaponsOffline; }
            if (roll < 0.92f) { return Outcome.Surrender; }
            return Outcome.CrewTurned;
        }

        private void OnHackFailed(NPCShip target, ShipHailActionUI ui, Rng rng,
                float powerDiff) {
            Report(ui, T.HackOutcomeRepelled);
            target.ChangeAttitudeTowards(FactionDef.Player, Rng.URange(-0.1f, -0.2f));
            if (target.IsInCombat) {
                ui.ShowHailPanel();
                return;
            }
            if (!NPCShipBehavior_Sector_Combat.CanPlayerAttack(target)) {
                ui.ShowHailPanel();
                return;
            }
            var attackChance = target.Faction.BraveryCurve.Evaluate(powerDiff);
            if (rng.Chance(attackChance) && CanFlyToPlayerOrIsNearby(target)) {
                NPCShipBehavior_Sector_Combat.AssignTo(target, A.Player.Ship);
                ui.SetResponse(
                    isLastMsg: true,
                    ShipChatTopic.CombatEngage,
                    ShipChatResponseType.Neutral);
                return;
            }
            ui.ShowHailPanel();
        }

        private static void Report(ShipHailActionUI ui, string msg) {
            ui.SetResponse(msg);
            ui.S.Sys.Log.AddLine(msg);
        }
    }
}
