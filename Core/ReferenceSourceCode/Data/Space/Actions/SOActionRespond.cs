using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Systems.Trade;
using Game.Systems.Quests;
using Game.Systems.Story;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Data.Space {
    // respond to signal
    public sealed class SOActionRespond : SOAction {
        public const string ActionId = "Respond";
        private const float SignalExtortionChance = 0.2f;
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionRespond());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            return ctx.SO.Type.IdH == SpaceObjectTypeIdH.Signal
                && ctx.S.Sys.Ship.IsNearby(ctx.SO)
                && !SOUtils.IsQuestTarget(ctx.SO);
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.VerbRespond, ActivateSignal);
            return true;
        }

        // public override UDB GetUIBlock() {
        //     return dataBlock ??= UDB.Create(
        //         this, UDBT.DBtn, IconId.CCallPickUp,
        //             T.VerbRespond).WithClickFunction(ActivateSignal);
        // }

        private void ActivateSignal() {
            if (!IsAvailable(ctx)) {
                UISounds.PlayActionDenied();
                return;
            }
            if (!ctx.S.Sys.Ship.HasCommunicator()) {
                ctx.S.Sig.ShowErrorMessage.Send(T.CommunicatorMissing);
                return;
            }

            var so = ctx.SO;
            var s = ctx.S;
            DoActivateSignal(so, s);
        }

        public static void DoActivateSignal(SpaceObject so, GameState s) {
            var devEvtId = s.BB.Get(BBKeyH.DevForceSignalEvent, string.Empty);
            if (devEvtId != string.Empty) {
                s.BB.Remove(BBKeyH.DevForceSignalEvent, devEvtId);
                if (TryStartStoryEvent(so, s, devEvtId)) {
                    return;
                }
            }

            var devQuestId = s.BB.Get(BBKeyH.DevForceSignalQuest, string.Empty);
            if (devQuestId != string.Empty) {
                s.BB.Remove(BBKeyH.DevForceSignalQuest, devQuestId);
                var def = QuestDef.FastGet(Hashes.S(devQuestId), false);
                if (def == null || (def.Providers & QuestProviderType.Signal) == QuestProviderType.None) {
                    D.Warn("Invalid forced Signal quest: {0}", devQuestId);
                } else {
                    var rng = new Rng(Hashes.Quick(so.Seed, Primes.Prime015));
                    var forcedQuest = s.Query.ForceQuestFor.Ask(so, def, rng);
                    if (forcedQuest != null) {
                        ShowQuest(so, s, forcedQuest);
                        return;
                    }
                }
            }

            if (so.HasData) {
                var legacyEventId = so.Data.GetString("SignalEventId", null);
                if (!string.IsNullOrEmpty(legacyEventId)) {
                    if (StoryEventDef.All.ContainsKey(Hashes.S(legacyEventId))) {
                        if (TryStartStoryEvent(so, s, legacyEventId)) {
                            return;
                        }
                    } else {
                        so.Data.SetString("SignalEventId", null);
                    }
                }
            }

            var cachedQuest = s.Query.GetQuestFor.Ask(so, false, false);
            if (cachedQuest != null) {
                ShowQuest(so, s, cachedQuest);
                return;
            }

            if (so.QuestId != 0) {
                var quests = s.Query.GetAllPlayerQuests.Ask();
                for (int i = quests.Count - 1; i >= 0; i--) {
                    var quest = quests[i];
                    if (quest.Data.Id == so.QuestId && !quest.IsEnded) {
                        The.Dialog.ShowSimple(IconId.CSignal, T.SignalQuestNoResponse);
                        return;
                    }
                }
                so.QuestId = 0;
            }

            var seed = Hashes.Quick(so.Seed, Primes.Prime015);
            if (Rng.Unseeded.Chance(SignalExtortionChance)) {
                var hook = StoryEventHook.CreateFor(so, StoryEventFlags.Signal)
                    .WithSeed(seed)
                    .Instant()
                    .WithIgnore(StoryPickIgnore.None)
                    .Optional();
                var hookData = s.Query.AddStoryEventHook.Ask(hook);
                if (StoryEventData.IsOK(hookData)) {
                    so.Data.SetString("SignalEventId", hookData.Def.Id);
                    return;
                }
            }

            if (TradeUI.FindTradePortal(s) == null) {
                s.Sig.ShowErrorMessage.Send(TradePortalComp.NotAvailable);
                return;
            }
            var newQuest = s.Query.GetQuestFor.Ask(so, true, false);
            if (newQuest != null) {
                ShowQuest(so, s, newQuest);
            }
        }

        private static bool TryStartStoryEvent(SpaceObject so, GameState s, string eventId) {
            if (!StoryEventDef.All.TryGetValue(Hashes.S(eventId), out var def)) {
                D.Warn("Signal story event not found: {0}", eventId);
                return false;
            }

            var ctx = s.Query.GetStoryContext.Ask();
            for (int i = ctx.OngoingEvents.Count - 1; i >= 0; i--) {
                StoryEventData ev = ctx.OngoingEvents[i];
                if (ev.TargetSO == so) {
                    ctx.OngoingEvents.RemoveAt(i);
                }
            }

            var data = StoryEventData.CreateFor(def);
            data.TargetSO = so;
            if (s.Query.StartStoryEvent.Ask(data)) {
                so.Data.SetString("SignalEventId", def.Id);
                return true;
            }
            D.Warn("Failed starting Signal story event: {0}", eventId);
            return false;
        }

        private static void ShowQuest(SpaceObject so, GameState s, Quest quest) {
            for (int i = 0; i < quest.Steps.Length; i++) {
                if (quest.Steps[i].Check is BaseSignalTransactionQuestStep transaction) {
                    so.UpdateName(transaction.DealSummary());
                    break;
                }
            }
            s.UIQuery.ShowNewQuest.Ask(quest);
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
