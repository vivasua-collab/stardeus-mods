using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Firex;
using Game.Systems.Notify;
using Game.Utils;
using KL.Randomness;
using KL.Signals;
using KL.Utils;

namespace Game.Systems.Story {
    public sealed class CleaningIncidentStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new CleaningIncidentStoryEvent());
        }

        public const string EventId = "CleaningIncident";
        public override string Id => EventId;

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            var cnt = ctx.S.Components.CountByType<CleanableComp>();
            return cnt > 0;
        }

        public override void OnLoad(StoryContext ctx, StoryEventData data) {
            ListenToCleaningEvent(ctx, data);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            if (!CheckConditions(ctx, data.Def)) {
                data.FailEvent(ctx.S, "No cleanables", isHardFailure: false);
                return;
            }
            ListenToCleaningEvent(ctx, data);
        }

        private void ListenToCleaningEvent(StoryContext ctx, StoryEventData data) {
            if (data.EventListener != null) { return; }
            var listener = ctx.S.Sig.CleanableCleaned.AddListener(
                (c, b) => OnCleanCleanable(ctx, data, c, b));
            data.SetState(ctx.S, StoryEventState.WaitingForHook);
            data.EventListener = listener;
            // D.Warn("Listening to cleanable cleaned: {0}", data);
        }

        // OnEnd runs only for events that reach Ending: one that failed (its hook timed out)
        // kept this listener, and a later dirty toilet still exploded
        public override void OnFinalize(StoryContext ctx, StoryEventData data) {
            if (data.EventListener is Signal2<CleanableComp, BeingData>.SignalListener listener) {
                ctx.S.Sig.CleanableCleaned.RemoveListener(listener);
                data.EventListener = null;
            }
            base.OnFinalize(ctx, data);
        }

        private void OnCleanCleanable(StoryContext ctx, StoryEventData data,
                CleanableComp cleanable, BeingData being) {
            if (data.State != StoryEventState.WaitingForHook) { return; }
            if (TryExplodeToilet(ctx, data, cleanable, being)) {
                data.SetState(ctx.S, StoryEventState.Ending);
                if (data.EventListener is Signal2<CleanableComp, BeingData>.SignalListener listener) {
                    // D.Warn("No longer listening to cleanable cleaned: {0}", data);
                    ctx.S.Sig.CleanableCleaned.RemoveListener(listener);
                }
            #if !PRODUCTION_BUILD
            } else {
                // D.Warn("Failed to explode toilet");
            #endif
            }
        }

        private bool TryExplodeToilet(StoryContext ctx, StoryEventData data,
                CleanableComp cleanable, BeingData cleaner) {
            float fullness = cleanable.DirtAmountBeforeClean / cleanable.DirtCapacity;
            var minDirt = data.Def.Vars.Get(PropertyIdH.MinDirtPercent, 0.5f);
            if (fullness < minDirt) {
                // too clean before cleaning
                return false;
            }
            if (!cleanable.Tile.HasComponent<ToiletComp>()) {
                // wrong type of cleanable
                return false;
            }
            if (cleaner == null) {
                bool canHappen = false;
                if (cleanable.TryGetGoal(out var goal)) {
                    canHappen = goal.IsNanobotWorkable;
                }
                if (!canHappen) {
                    return false;
                }
            }
            var rng = data.SeededRng;
            if (cleaner != null) {
                if (cleaner.Mind.Jobs.CanDo(JobTypeIdH.Cleaning, out byte lvl)) {
                    var chanceCurve = data.Def.Vars.GetCurve(PropertyIdH.SkillFailChanceCurve);
                    var chanceToFail = chanceCurve.Evaluate(lvl);
                    if (!rng.Chance(chanceToFail)) {
                        // saved by the skill
                        return false;
                    }
                }
            }
            // var wt = 1f; // TODO
            var tile = cleanable.Tile;
            NotifyFailure(data, tile, cleaner);
            ToiletExplosion.Explode(ctx.S, tile, rng, fullness,
                data.Def.Vars.GetCurve(PropertyIdH.SizeDistanceCurve));
            return true;
        }
        private void BuildLogEntry(StoryEventData data, Tile tile, BeingData cleaner) {
            string cleanerName;
            if (cleaner == null) {
                cleanerName = T.ObjMaterialsNanobots;
            } else {
                cleanerName = Texts.LinkToBeing(cleaner);
            }
            var evDesc = T.EvtCleaningIncidentDesc_V2.F(cleanerName, tile.NameLink);
            data.CreateLogEntry(IconId.CNoSparkles, T.EvtCleaningIncident, evDesc);
        }
        private void NotifyFailure(StoryEventData data, Tile tile, BeingData cleaner) {
            data.TargetTile = tile;
            data.TargetBeing = cleaner;
            BuildLogEntry(data, tile, cleaner);
            var S = tile.S;

            Notification.BuildFor(tile, IconId.CNoSparkles, data.LogEntry.Title, Consts.TicksPer8Hours)
                .Group("inc", T.NounIncident, IconId.CWarning)
                .WithOnClick(() => EntityUtils.CameraFocusOn(tile))
                .WithTooltip(data.LogEntry.Desc)
                .Show();
            // if (A.FocusOnEventsAndIdle) {
            //     EntityUtils.CameraFocusOn(tile);
            // }
            string msg;
            if (cleaner == null) {
                msg = T.EvtCleaningIncidentDesc_V2.F(
                    T.ObjMaterialsNanobots,
                    tile.NameLink);
            } else {
                msg = T.EvtCleaningIncidentDesc_V2.F(
                    Texts.LinkToBeing(cleaner),
                    tile.NameLink);
            }
            S.Sig.ShowFanfare.Send(msg);
            S.Sys.Log.AddLine(msg);
        }
    }
}
