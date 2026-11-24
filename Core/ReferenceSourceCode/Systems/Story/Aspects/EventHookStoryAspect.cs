#if !PRODUCTION_BUILD
// #define DEBUG_HOOKS
#endif
using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Ships;
using Game.Systems.Space;
using KL.Randomness;
using KL.Signals;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class EventHookStoryAspect : StoryAspect {
        [SelfInit]
        private static void Register() {
            Register(AspectId, Factory);
        }

        private static StoryAspect Factory() {
            return new EventHookStoryAspect();
        }
        public const string AspectId = "EventHook";
        public override string Id => AspectId;

        public override void OnTick(StoryContext ctx) {
            for (int i = ctx.ActiveHooks.Count - 1; i >= 0; i--) {
                var hook = ctx.ActiveHooks[i];

                if (hook.BoundToSO != 0 && hook.BoundToSO != ctx.CurrentSector?.SO.Id) {
                    D.Warn("Removing story event hook that was bound to SO: {0}", hook);
                    ctx.ActiveHooks.RemoveAt(i);
                    continue;
                }

                if (ProcessHook(ctx, hook) != null) {
                    ctx.ActiveHooks.RemoveAt(i);
                }
            }
        }

        private StoryEventData AddHook(StoryContext ctx, StoryEventHook hook) {
            if (hook.IsInstant) {
                return ProcessHook(ctx, hook);
            } else {
                ctx.ActiveHooks.Add(hook);
                return null;
            }
        }

        private StoryEventData ProcessHook(StoryContext ctx, StoryEventHook hook) {
            ctx.PotentialHookEvents.Clear();
            using (D.Timer("EnqueueHookEvents")) {
                var forcedEvId = ctx.S.BB.Get(BBKeyH.DevForceHook, string.Empty);
                if (!string.IsNullOrEmpty(forcedEvId)) {
                    var ev = StoryEventDef.Get(forcedEvId);
                    ctx.S.BB.Remove(BBKeyH.DevForceHook, forcedEvId);
                    if (ev == null) {
                        D.Err("Failed forcing hook, event does not exist by id: {0}",
                            forcedEvId);
                    } else {
                        D.Warn("Forcing event hook: {0}->{1}", hook, ev);
                        ctx.PotentialHookEvents.Add(ev, 1f);
                    }
                } else {
                    EnqueueHookEvents(ctx, hook, StoryPickIgnore.None, false);
                    if (ctx.PotentialHookEvents.Count == 0 &&
                                (hook.Ignore & StoryPickIgnore.Schedule) == StoryPickIgnore.Schedule) {
                        EnqueueHookEvents(ctx, hook, StoryPickIgnore.Schedule, false);
                    }
                    if (ctx.PotentialHookEvents.Count == 0 &&
                            (hook.Ignore & StoryPickIgnore.RepeatDelay) == StoryPickIgnore.RepeatDelay) {
                        EnqueueHookEvents(ctx, hook, StoryPickIgnore.RepeatDelay, false);
                    }
                    if (ctx.PotentialHookEvents.Count == 0 &&
                                (hook.Ignore & StoryPickIgnore.All) == StoryPickIgnore.All) {
                        EnqueueHookEvents(ctx, hook, StoryPickIgnore.All, false);
                    }
                    // Last resort: only events flagged as Fallback, ignoring
                    // points, schedule and repeat delay. Hooks that ignore
                    // nothing (see SOActionRespond) opt out of this too.
                    if (ctx.PotentialHookEvents.Count == 0 &&
                                hook.Ignore != StoryPickIgnore.None) {
                        EnqueueHookEvents(ctx, hook, StoryPickIgnore.Fallback, true);
                    }
                }
            }
            if (ctx.PotentialHookEvents.Count == 0) {
                if (!hook.IsOptional) {
                    D.Err("Couldn't find any compatible hook events for: {0}", hook);
                #if DEBUG_HOOKS
                } else {
                    D.Warn("Couldn't find any compatible hook events for optional hook: {0}", hook);
                #endif
                }
                return null;
            }
            var rng = ctx.Rng;
            if (hook.Seed != 0) {
                rng = new Rng(hook.Seed);
            }
            if (rng.TryWeightedFrom(ctx.PotentialHookEvents, out var evt)) {
                var handler = ctx.S.Query.StoryEventHandlerFor.Ask(evt.Outcome);
                var data = handler.TryPreparePotential(ctx, ctx.PotentialHookEvents, 0f);
                if (StoryEventData.IsOK(data)) {
                    ResolveHookTarget(hook, data);
                    hook.BeforeStart?.Invoke(data);
                    if (handler.StartEvent(data, ctx)) {
                        hook.AfterStart?.Invoke(data);
                        return data;
                    }
                }
                if (hook.IsOptional) {
                    D.Warn("Failed to start optional hook event: {0} | {1}", data, hook);
                } else {
                    D.Err("Failed to start non-optional hook event: {0} | {1}", data, hook);
                }
            }
            return null;
        }

        private static void ResolveHookTarget(StoryEventHook hook, StoryEventData data) {
            if (hook.Target == null) { return; }
            Entity entity = null;
            if (hook.Target is Entity ntt) {
                entity = ntt;
            } else if (hook.Target is IComponent comp) {
                entity = comp.Entity;
            } else if (hook.Target is SpaceObject so) {
                data.TargetSO = so;
                return;
            } else if (hook.Target is ISpaceEntity spaceEnt) {
                data.TargetSO = spaceEnt.SO;
                return;
            } else if (hook.Target is NPCShip ship) {
                data.TargetSO = ship.SO;
                return;
            }
            if (entity != null) {
                if (entity is Tile tile) {
                    data.TargetTile = tile;
                    return;
                } else if (entity is Obj obj) {
                    data.TargetObj = obj.Data;
                    return;
                } else if (entity is Being b) {
                    data.TargetBeing = b.Data;
                    return;
                }
            } else {
                if (hook.Target is BeingData bd) {
                    data.TargetBeing = bd;
                    return;
                } else if (hook.Target is ObjData od) {
                    data.TargetObj = od;
                    return;
                }
            }
            // D.Warn("Could not resolve hook target, setting as TargetOther: {0} -> {1}",
            //     hook.Target, hook);
            data.TargetOther = hook.Target;
        }

        private static void EnqueueHookEvents(StoryContext ctx,
                StoryEventHook hook,
                StoryPickIgnore ignore,
                bool fallbackOnly) {
            var dict = ctx.PotentialHookEvents;
            foreach (StoryEventOutcome outcome in StoryEventDef.AllOutcomes) {
                var handler = ctx.S.Query.StoryEventHandlerFor.Ask(outcome);
                foreach (var def in StoryEventDef.ByOutcome[(int)outcome]) {
                    if ((def.Flags & hook.Flags) != hook.Flags) {
                        continue;
                    }
                    if (fallbackOnly && (def.Flags & StoryEventFlags.Fallback)
                            == StoryEventFlags.None) {
                        continue;
                    }
                    if (!def.Event.CheckHook(ctx, def, hook)) {
                        #if DEBUG_HOOKS
                        D.Warn("Evaluating hook event: {0} -> CheckHook == false", def);
                        #endif
                        continue;
                    }
                    var w = handler.GetEventWeight(ctx, def, ignore);
                    #if DEBUG_HOOKS
                    D.Warn("Evaluating hook event: {0} -> w: {1}", def, w);
                    #endif
                    if (w > 0f) {
                        if (hook.EventFilter != null && !hook.EventFilter(def)) {
                            #if DEBUG_HOOKS
                            D.Warn("Event didn't pass hook filter: {0} -> {1})",
                                def, hook.EventFilter.Target);
                            #endif
                            continue;
                        }
                        dict.Add(def, w);
                    }
                }
            }
        }


        public override void Initialize(StoryContext ctx, bool isLoading) {
            ctx.S.Query.AddStoryEventHook.SetResponder((hook) => AddHook(ctx, hook));
        }

        public override void Unload(StoryContext ctx) {
            ctx.S.Query.AddStoryEventHook.RemoveResponder();
        }
    }
}
