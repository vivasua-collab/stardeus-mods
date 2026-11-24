using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Expeditions;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Systems.Creatures;
using KL.Clock;
using Game.Systems.AI.Combat;
using Game.Data.Factions;

namespace Game.Systems.Story {
    public class ExpeditionRaiderExtortionStoryEvent : ExpeditionStoryEvent {
        public const string EventId = "ExpeditionRaiderExtortion";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new ExpeditionRaiderExtortionStoryEvent());
        }

        #if !PRODUCTION_BUILD
        // for tests
        public static bool ForceCrewWinFight;
        public static bool ForceCrewLoseFight;
        public static int ForceCrewDeaths = -1;
        #endif

        public override string Title => T.EvtExpRaiderExtortionTitle;

        public override void TickExpedition(StoryEventData data, Expedition exp) {
            if (!exp.Context.HadActionRequired) {
                exp.Context.SetRequiredAction((exp) => ShowExtortionRequest(data, exp));
            }
        }

        private void ShowExtortionRequest(StoryEventData data, Expedition exp) {
            if (IsShowingChoice(data)) { return; }
            SetShowingChoice(data, true);
            var crew = exp.Crew;
            string msg = null;
            var s = exp.S;
            var rng = data.SeededRng;
            // Let's say shuttle is worth something
            var crewValue = 500d;
            foreach (var mem in crew) {
                crewValue += BeingUtils.ValueOf(s, mem, true);
            }
            crewValue *= rng.Range(0.95f, 1.15f);
            crewValue = (long) (crewValue / 100) * 100;
            msg = FormatExtortionRequest(exp, crewValue);
            data.CreateLogEntry(IconId.IconsFactionPirate,
                T.EvtExpRaiderExtortionTitle, msg);
            var title = Texts.Red(T.EvtExpRaiderExtortionTitle);
            s.Sys.Log.AddLine(title);
            var dd = The.Dialog.ShowChoice(IconId.IconsFactionPirate, msg,
                () => {
                    AfterChoiceIsMade(exp);
                    if (s.Wallet.TryPay(crewValue, null)) {
                        // If we do this immediately, custom entry will go into past events
                        // before the current event does, messing up the order of events
                        s.Clock.OnNextTick(() => {
                            var evData = StoryEventData.CreateCustom(IconId.IconsFactionPirate,
                                T.EvtExpRaiderExtortionResolvedTitle,
                                T.EvtExpRaiderExtortionPaidDesc);
                            exp.S.Sig.AddCustomStoryEvent.Send(evData, true);
                        });
                        FinalizeEvent(data, exp);
                        // Force the next phase
                        exp.Tick(G.Ticks);
                    } else {
                        exp.S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.TradeNotEnoughCash}");
                        UISounds.PlayActionDenied();
                        FightRaiders(data, exp, rng);
                    }
                }, () => {
                    AfterChoiceIsMade(exp);
                    FightRaiders(data, exp, rng);
                }, T.VerbPayRansom, T.VerbRefuse, onShow: () => {
                    The.ScreenBlock.SetCenterText(T.EvtExpRaiderExtortionTitle, 0f);
                    The.ScreenBlock.SetSideText(string.Empty);
                    The.ScreenBlock.Show();
                    Ready.SetUserInput(false, "Extortion");
                    s.Clock.Pause();
                    s.Clock.ForceStop(ForceStopReason.Choice);
                }, canCloseWithEsc: false, isSkippable: false);
            dd.OverrideSortOrder = 30;
        }

        private void AfterChoiceIsMade(Expedition exp) {
            The.ScreenBlock.HideImmediate();
            Ready.SetUserInput(true, "Extortion");
            exp.S.Clock.ForceResume(ForceStopReason.Choice);
        }

        private string FormatExtortionRequest(Expedition exp, double crewValue) {
            return T.EvtExpRaiderExtortionChoice_V2.F(
                exp.Shuttle.Name,
                Wallet.ToValueString(crewValue, false));
        }

        private static string causeOfDeath => T.DeathCombatRaiders_V1.F(
            FactionDef.FastGet(FactionIdH.Pirate).NameT);

        private void FightRaiders(StoryEventData data, Expedition exp, Rng rng) {
            var numRaiders = math.round(exp.Crew.Count * rng.Range(0.5f, 2f)
                * exp.S.Difficulty.Level);
            numRaiders = Maths.Clamp(numRaiders, 2, 8);
            var raiderStrength = 0f;
            var crewStrength = 0f;
            for (int i = 0; i < numRaiders; i++) {
                raiderStrength += 3f;
                if (rng.Chance(exp.S.Difficulty.Level)) {
                    // has weapon
                    raiderStrength += 1f;
                }
            }
            foreach (var mem in exp.Crew) {
                crewStrength += 3f;
                if (mem.Inventory?.Equipment.TryGetValue(InventorySlotIdH.Weapon, out var weapon) == true) {
                    if (weapon.Item != null) {
                        crewStrength += CombatRaid.StrengthOf(mem);
                        // FIXME consider more interesting weapons strength
                        crewStrength += 1f;
                    }
                }
            }
            var difference = crewStrength / raiderStrength;
            D.Warn("Crew strength difference: {0} raiders vs {1} crew: {2} vs {3}: {4}",
                numRaiders, exp.Crew.Count, raiderStrength, crewStrength, difference);
            var hasWon = rng.Chance(Maths.Clamp(difference, 0.33f, 0.99f));
            #if !PRODUCTION_BUILD
            // For tests
            if (ForceCrewWinFight) {
                ForceCrewWinFight = false;
                hasWon = true;
            }
            if (ForceCrewLoseFight) {
                ForceCrewLoseFight = false;
                hasWon = false;
            }
            #endif
            if (hasWon) {
                // won the fight
                var deaths = rng.Range(0, exp.Crew.Count);
                #if !PRODUCTION_BUILD
                // For tests
                if (ForceCrewDeaths != -1) {
                    deaths = ForceCrewDeaths;
                    ForceCrewDeaths = -1;
                }
                #endif
                deaths = SimulateCombat(exp.S, exp, deaths);
                // If we do this immediately, custom entry will go into past events
                // before the current event does, messing up the order of events
                exp.S.Clock.OnNextTick(() => {
                    var evData = StoryEventData.CreateCustom(IconId.IconsFactionPirate,
                        T.EvtExpRaiderExtortionResolvedTitle,
                        T.EvtExpRaiderExtortionFightWonDesc);
                    exp.S.Sig.AddCustomStoryEvent.Send(evData, true);
                });
                FinalizeEvent(data, exp);
                // Force the next phase
                exp.Tick(G.Ticks);
            } else {
                var msg = causeOfDeath;
                // lost the fight
                for (int i = exp.Crew.Count - 1; i >= 0; i--) {
                    BeingData mem = exp.Crew[i];
                    mem.StateFlags &= ~BeingState.Expedition;
                    exp.Crew.RemoveAt(i);
                    BeingUtils.Eliminate(exp.S, mem, msg, G.Ticks);
                }
                exp.Shuttle.Hide(true);
                // If we do this immediately, custom entry will go into past events
                // before the current event does, messing up the order of events
                exp.S.Clock.OnNextTick(() => {
                    var evData = StoryEventData.CreateCustom(IconId.IconsFactionPirate,
                        T.EvtExpRaiderExtortionResolvedTitle,
                        T.EvtExpRaiderExtortionFightLostDesc);
                    exp.S.Sig.AddCustomStoryEvent.Send(evData, true);
                });
                FinalizeEvent(data, exp);
                ForceNextStage = ExpeditionStages.Find(ExpeditionStageTypeH.Done);
                // Force the next phase
                exp.Tick(G.Ticks);
            }
        }

        public static int SimulateCombat(GameState s, Expedition exp, int deaths) {
            var msg = causeOfDeath;
            var ws = NamedEntity.For(T.NounCombat);
            foreach (var mem in exp.Crew) {
                if (deaths > 0) {
                    deaths--;
                    BeingUtils.Kill(s, mem, DamageUtils.RandomCombat(Rng.Unseeded),
                        ws, msg, (bp) => true, 0);
                } else {
                    var numWounds = s.Rng.Range(1, 4);
                    BeingUtils.DealHistoricalDamage(s, mem, DamageUtils.RandomCombat(Rng.Unseeded), 0,
                        numWounds, ws, Severity.Mild, Severity.Moderate, Severity.Severe, Severity.Critical);
                }
            }

            return deaths;
        }

        private void FinalizeEvent(StoryEventData data, Expedition exp) {
            exp.Context.ClearRequiredAction();
            data.SetState(exp.S, StoryEventState.Ending);
            exp.S.Clock.SetSpeed(ClockSpeed.Normal);
        }
    }
}
