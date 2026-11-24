using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Modifiers;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class StorySys : GameSystem, ISaveableSpecial {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new StorySys());
        }
        private const string SysId = "StorySys";
        public override string Id => SysId;
        private StoryGenDef generator;
        private StorySysData data;
        private StoryContext context;
        private bool needsToLogSituation;

        public override void Unload() {
        }

        protected override void OnInitialize() {
            StoryEventData.LastId = 0;
            S.Sig.AfterLoadState.AddListener(OnLoad);
        }

        private void OnLoad(GameState state) {
            state.Sig.SetStoryGen.AddListener(OnSetStoryGen);
            Ready.When(WhenH.AllSystemsLoaded, RestoreData);
        }

        private void OnAddCustomEvent(StoryEventData data, bool notify) {
            // A custom event never goes through StartEvent, and the log counts its age from
            // StartTick, so an unset one read as the whole game ago ("2y ago")
            if (data.StartTick == 0) {
                data.StartTick = G.Ticks;
                data.EndTick = G.Ticks;
            }
            context.PastEvents.Add(data);
            if (data.State != StoryEventState.Ending) {
                D.Err("Custom event should always be in Ending state when you add it! {0}", data);
                data.SetState(S, StoryEventState.Ending);
            }
            data.SetState(S, StoryEventState.Ended);
            if (notify) {
                data.AddEventNotification(context);
                S.Sig.ShowFanfare.Send(data.LogEntry.Title);
                S.Sys.Log.AddLine(data.LogEntry.Title);
            }
        }

        private void OnMoveInHyperspace(SpaceObject from, SpaceObject to) {
            if (to?.Sector == null) {
                D.Err("Move to sector null! {0}->{1}", from, to);
                return;
            }
            context.CurrentSector = to.Sector;
        }

        private bool StartEvent(StoryEventData data) {
            if (data == null) {
                D.Err("Trying to start event with null data");
                return false;
            }
            var def = data.Def;
            var handler = generator.HandlerFor(def.Outcome);
            if (handler == null) {
                D.Err("Story generator has no handler for triggering event: {0} -> {1} ({2})",
                    generator.Id, def.Outcome, def);
                return false;
            }

            return handler.StartEvent(data, context);
        }

        private StoryEventData TriggerEvent(string eventId) {
            if (eventId == null) {
                var evts = StoryEventDef.Considerable
                    .Where(v => !Arrays.IsEmpty(v))
                    .OrderBy(v => Rng.UValue)
                    .FirstOrDefault();
                if (Arrays.IsEmpty(evts)) {
                    D.Err("No considerable events!");
                    return null;
                }
                var evt = Rng.UFrom(evts);
                eventId = evt.Id;
                if (eventId == null) {
                    D.Err("Failed picking a random event!");
                    return null;
                }
                D.Warn("Triggering random event: {0}", eventId);
            }
            var def = StoryEventDef.Get(eventId);
            if (def == null) {
                D.Err("Failed getting story event def by id {0}", eventId);
                return null;
            }
            var handler = generator.HandlerFor(def.Outcome);
            if (handler == null) {
                D.Err("Story generator has no handler for triggering event: {0} -> {1} ({2})",
                    generator.Id, def.Outcome, def);
                return null;
            }

            return handler.TriggerEvent(def, context);
        }

        private IStoryEventHandler GetHandlerFor(StoryEventOutcome outcome) {
            return generator.HandlerFor(outcome);
        }

        private void RestoreData() {
            bool isLoading = data != null;

            foreach (var def in StoryEventDef.All.Values) {
                def.IsDisabled = ModifierDisableStoryEvents.IsDisabledFor(S, def);
            }

            if (isLoading) {
                StoryEventData.LastId = data.LastEventDataId;
                generator = StoryGenDef.Get(data.GenId);
                if (generator == null) {
                    D.Err("Generator not found by id: '{0}'! Falling back to default",
                        data.GenId);
                }
                context = StoryContextData.Deserialize(S, data.Context);
                RepairPastEventsEndingOnLoad();
            }
            if (generator == null) {
                generator = StoryGenDef.LastSelected ?? StoryGenDef.Default;
            }
            context ??= new StoryContext(S);
            context.Rng ??= new Rng(Hashes.Quick(S.Seed, Primes.Prime030));
            generator.Initialize(context, isLoading);
            context.OnLoad();
            foreach (var asp in generator.StoryAspects) {
                if (asp is ISaveableChild saveAsp) {
                    saveAsp.OnLoad(context.AspectData);
                }
            }
            data = null;

            S.Clock.OnTick.AddListener(OnTick);
            S.Query.GetStoryGen.SetResponder(() => generator);
            S.Query.GetStoryContext.SetResponder(() => context);
            S.Query.StoryEventHandlerFor.SetResponder(GetHandlerFor);
            S.Query.TriggerStoryEvent.SetResponder(TriggerEvent);
            S.Query.StartStoryEvent.SetResponder(StartEvent);
            S.Sig.UniverseRegenerated.AddListener(OnUniverseRegen);
            S.Sig.AddCustomStoryEvent.AddListener(OnAddCustomEvent);
            S.Sig.HyperspaceLocationChanged.AddListener(OnMoveInHyperspace);
        }

        private void RepairPastEventsEndingOnLoad() {
            foreach (var ev in context.PastEvents) {
                if (ev.State != StoryEventState.Ending) { continue; }
                D.Err("Loaded past story event in Ending state! Repairing to Ended: {0}", ev);
                ev.SetState(S, StoryEventState.Ended);
            }
        }

        private void OnUniverseRegen() {
            foreach (var ev in context.OngoingEvents) {
                ev.Location = null;
                ev.TargetSO = null;
            }
            foreach (var ev in context.PastEvents) {
                ev.Location = null;
                ev.TargetSO = null;
            }
        }

        private void OnSetStoryGen(StoryGenDef gen) {
            if (generator != gen) {
                // Aspects keep their state through the same save/load pair a game load uses.
                // Re-initialized without it, a switch reset state such as the 100 day rescue
                // cooldown, and the "random" picker saved ignores it never filled (it threw).
                if (generator != null) {
                    foreach (var asp in generator.StoryAspects) {
                        if (asp is ISaveableChild saveAsp) {
                            saveAsp.OnSave(context.AspectData);
                        }
                    }
                    generator.Unload(context);
                }
                generator = gen;
                gen.Initialize(context, false);
                foreach (var asp in gen.StoryAspects) {
                    if (asp is ISaveableChild saveAsp) {
                        saveAsp.OnLoad(context.AspectData);
                    }
                }
            }
        }

        // public for tests
        public void OnTick(long ticks) {
            // The new game intro is the player's first look at the ship, so nothing may happen
            // to it before they have control. Whatever came due meanwhile happens right after
            if (S.Vars.IsShowingCutscene) { return; }
            generator.OnTick(ticks, context);
            if (needsToLogSituation || ticks % Consts.TicksPerDay == 1) {
                if (Ready.Everything) {
                    needsToLogSituation = false;
                    S.Situation.UpdateTime(ticks);

                    var sh = context.Schedule[(int) StoryEventOutcome.Positive];
                    S.Situation.StoryPointsPositive = sh.PointAllowance;
                    S.Situation.StoryPointsPositiveMax = sh.MaxPointAllowance;

                    sh = context.Schedule[(int) StoryEventOutcome.Negative];
                    S.Situation.StoryPointsNegative = sh.PointAllowance;
                    S.Situation.StoryPointsNegativeMax = sh.MaxPointAllowance;

                    sh = context.Schedule[(int) StoryEventOutcome.Neutral];
                    S.Situation.StoryPointsNeutral = sh.PointAllowance;
                    S.Situation.StoryPointsNeutralMax = sh.MaxPointAllowance;

                    sh = context.Schedule[(int) StoryEventOutcome.Raid];
                    S.Situation.StoryPointsRaid = sh.PointAllowance;
                    S.Situation.StoryPointsRaidMax = sh.MaxPointAllowance;

                    S.Map.LogSituation();
                } else {
                    needsToLogSituation = true;
                }
            }
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Story ??= new();
            sd.Story.LastEventDataId = StoryEventData.LastId;
            sd.Story.GenId = generator.Id;
            foreach (var asp in generator.StoryAspects) {
                if (asp.Def.Aspect is ISaveableChild saveAsp) {
                    saveAsp.OnSave(context.AspectData);
                }
            }
            sd.Story.Context = StoryContextData.Serialize(context);
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            data = sd?.Story;
        }
    }
}
