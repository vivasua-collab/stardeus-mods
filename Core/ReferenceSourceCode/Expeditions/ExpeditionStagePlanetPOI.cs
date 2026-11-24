using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Story.Events;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using KL.Randomness;
using Game.Systems.Story;
using Game.Systems.Mental;

namespace Game.Expeditions {
    public sealed class ExpeditionStagePlanetPOI : BaseExpeditionStage {
        [SelfInit]
        private static void Register() {
            ExpeditionStages.Register(ExpeditionStageTypeH.PlanetPOI , () => {
                return new ExpeditionStagePlanetPOI(false, ExpeditionStageTypeH.PlanetPOI);
            });
            ExpeditionStages.Register(ExpeditionStageTypeH.Anomaly , () => {
                return new ExpeditionStagePlanetPOI(true, ExpeditionStageTypeH.Anomaly);
            });
        }
        private const string Var_HasEvent = "exp.event.has_event";
        // Story event ids carry their folder under Config/Story/Events
        internal const string FallbackPOIEventId = "Expedition/ExpeditionFoundMarketItem_CommonMat";
        internal const string FallbackAnomalyEventId = "Expedition/ExpeditionFoundMarketItem_RareMat";
        private readonly int stageType;
        public override int StageType => stageType;
        public readonly bool IsAnomaly;

        public override string MiniWidgetStatusIcon => IconId.CMagnifier;

        public ExpeditionStagePlanetPOI(bool anomaly, int type) {
            IsAnomaly = anomaly;
            stageType = type;
            MiniWidgetText.Set(T.ExpStageExploring);
        }

        private bool isStageCancellable;
        public override bool IsStageCancellable => isStageCancellable;

        public override string Title => evData?.Data?.GetString("ExpTitle", null)
            ?? T.ExpStageExploring;
        private StoryEventData evData;
        private IExpeditionStage nextStage;
        private int nextStageType;

        public override bool CanCallBack(Expedition exp) {
            if (evData == null) { return true; }
            if (evData.Def.Event is ExpeditionStoryEvent expEvt) {
                return expEvt.IsCancellable(evData, exp);
            } else {
                D.Err("Expedition StoryEventData event def is not ExpeditionStoryEvent! {0}",
                    evData);
            }
            return true;
        }

        public override bool TryCancelStage(out int nextStage) {
            if (IsStageCancellable) {
                nextStage = ExpeditionStageTypeH.PrepForReturn;
                return true;
            } else {
                nextStage = ExpeditionStageTypeH.Done;
                return false;
            }
        }

        public override void Abort(Expedition exp) {
            if (!RestoreEvent(exp)) {
                return;
            }
            if (evData == null) {
                D.Err("Restored event but evData is null! {0}", exp);
                return;
            }
            if (evData.Def.Event is ExpeditionStoryEvent expEvt) {
                expEvt.Abort(evData, exp);
            } else {
                D.Err("Expedition StoryEventData event def is not ExpeditionStoryEvent! {0}",
                    evData);
            }
            exp.StageData.RemoveBool(Var_HasEvent);
        }

        private bool RestoreEvent(Expedition exp) {
            if (evData != null) {
                return true;
            }
            var hadEvent = exp.StageData.GetBool(Var_HasEvent, false);
            if (!hadEvent) {
                return false;
            }
            var ctx = exp.S.Query.GetStoryContext.Ask();
            foreach (var evt in ctx.OngoingEvents) {
                if (evt.Def.Event is ExpeditionStoryEvent expEvt) {
                    if (evt.TargetSO == exp.Target) {
                        evData = evt;
                        return true;
                    }
                }
            }
            // After save/load the event may have already ended (moved to PastEvents)
            // before the POI stage advanced. Restore it so the Ended branch in Tick
            // can run RemovePOI + ReturnToBase normally.
            foreach (var evt in ctx.PastEvents) {
                if (evt.Def?.Event is ExpeditionStoryEvent) {
                    if (evt.TargetSO == exp.Target) {
                        evData = evt;
                        return true;
                    }
                }
            }
            D.Err("Expedition had an event but can't find one! {0}->{1}. Past: {2}",
                exp, D.DumpCollection(ctx.OngoingEvents),
                D.DumpCollection(ctx.PastEvents));
            exp.StageData.RemoveBool(Var_HasEvent);
            return false;
        }

        public override IExpeditionStage Tick(Expedition exp, long ticks) {
            if (nextStage != null) {
                var ns = nextStage;
                nextStage = null;
                evData = null;
                return ns;
            }

            if (!Ready.Get(WhenH.AllSystemsLoaded)) {
                D.Warn("Ticking ExpeditionPOI while systems not loaded!");
                return this;
            }

            var forcedEvtId = exp.S.BB.Get(BBKeyH.DevForceExpeditionEvent, string.Empty);
            if (!string.IsNullOrEmpty(forcedEvtId)) {
                var evt = StoryEventDef.Get(forcedEvtId);
                var data = StoryEventData.CreateFor(evt);
                data.TargetSO = exp.Target;
                data.TargetTile = exp.Shuttle.Tile;
                data.Seed = exp.Target.Seed;
                exp.S.BB.Remove(BBKeyH.DevForceExpeditionEvent, forcedEvtId);
                if (exp.S.Query.StartStoryEvent.Ask(data)) {
                    evData = data;
                    exp.StageData.SetBool(Var_HasEvent, true);
                    return this;
                }
                D.Err("Couldn't start forced expedition event: {0}", data);
            }

            if (!RestoreEvent(exp)) {
                isStageCancellable = false;
                var hook = StoryEventHook.CreateFor(exp.Target, StoryEventFlags.Expedition)
                    .WithSeed(exp.Target.Seed)
                    .WithBeforeStart((d) => d.TargetTile = exp.Shuttle.Tile)
                    .WithEventFilter((def) => {
                        if (def.Event is ExpeditionStoryEvent expEvt) {
                            return expEvt.IsAvailableFor(exp);
                        }
                        D.Err("Expedition event hook is checking an event that is not ExpeditionStoryEvent! {0}", def);
                        return false;
                    })
                    .Instant();
                if (IsAnomaly) {
                    hook.Flags |= StoryEventFlags.Anomaly;
                }
                evData = exp.S.Query.AddStoryEventHook.Ask(hook);
                if (StoryEventData.IsOK(evData)) {
                    exp.StageData.SetBool(Var_HasEvent, true);
                } else {
                    var fallbackId = IsAnomaly ? FallbackAnomalyEventId : FallbackPOIEventId;
                    D.Warn("Failed getting expedition event for {0}, using fallback: {1}", exp, fallbackId);
                    var fallbackDef = StoryEventDef.Get(fallbackId);
                    if (fallbackDef != null) {
                        var fallbackData = StoryEventData.CreateFor(fallbackDef);
                        fallbackData.TargetSO = exp.Target;
                        fallbackData.TargetTile = exp.Shuttle.Tile;
                        fallbackData.Seed = exp.Target.Seed;
                        if (exp.S.Query.StartStoryEvent.Ask(fallbackData)) {
                            evData = fallbackData;
                            exp.StageData.SetBool(Var_HasEvent, true);
                        } else {
                            D.Err("FAILED GETTING EXPEDITION EVENT AND FALLBACK: {0}", exp);
                            RemovePOI(exp);
                            return ReturnToBase(exp.S);
                        }
                    } else {
                        D.Err("FAILED GETTING EXPEDITION EVENT: fallback def not found for {0}", exp);
                        RemovePOI(exp);
                        return ReturnToBase(exp.S);
                    }
                }
            }
            if (evData.Def.Event is not ExpeditionStoryEvent expEvt) {
                D.Err("Wrong event type, not expedition! {0}", evData);
                RemovePOI(exp);
                return ReturnToBase(exp.S);
            }
            if (evData.State == StoryEventState.Ended) {
                if (expEvt.ShouldRemovePOI) {
                    var parent = exp.Target.Parent;
                    while (parent != null && parent.Sector == null) {
                        D.Err("Unexpected: POI parent is not a sector! {0} => {1}",
                            exp.Target, parent);
                        parent = parent.Parent;
                    }
                    if (parent != null) {
                        if (IsAnomaly) {
                            parent.ChangePOIs(SpaceObjectTypeIdH.Anomaly, -1, syncKnowledge: true);
                        } else {
                            parent.ChangePOIs(SpaceObjectTypeIdH.PlanetPOI, -1, syncKnowledge: true);
                        }
                    }
                    RemovePOI(exp);
                }
                foreach (var mem in exp.Crew) {
                    if (mem.Mind.Jobs.TryJob(JobTypeIdH.Expedition, out var expJob)) {
                        expJob.TrainSkill(Consts.TicksPer4Hours * SkillXp.Default, false);
                    }
                }
                if (expEvt.ForceNextStage != null) {
                    return expEvt.ForceNextStage;
                } else {
                    return ReturnToBase(exp.S);
                }
            } else if (evData.State == StoryEventState.Ongoing) {
                expEvt.TickExpedition(evData, exp);
                isStageCancellable = expEvt.IsCancellable(evData, exp);
            } else if (evData.IsFailed) {
                // An event fails when a choice goes unanswered past its timeout. The stage
                // only knew Ended and Ongoing, so the crew waited at the POI until recalled.
                // The POI stays: nothing was resolved there.
                return ReturnToBase(exp.S);
            }
            return this;
        }

        private static void RemovePOI(Expedition exp) {
            SOUtils.RemoveSO(exp.Target);
            // target needs to exist or it will go wrong in save / load
            exp.SetTarget(exp.S.Universe.Player);
        }

        private IExpeditionStage ReturnToBase(GameState s) {
            if (evData != null && !evData.IsEnded) {
                evData.SetState(s, StoryEventState.Ending);
            }
            evData = null;
            isStageCancellable = false;
            nextStageType = ExpeditionStageTypeH.PrepForReturn;
            nextStage = ExpeditionStages.Find(nextStageType);
            return nextStage;
        }
    }
}
