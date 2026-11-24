using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class MultiStorageCapsuleRaidStoryEvent : StoryEvent {
        public const string EventId = "MultiStorageCapsuleRaid";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new MultiStorageCapsuleRaidStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ctx.S.Universe.Player.Parent.Layer != UniverseLayer.Universe;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            data.SetState(s, StoryEventState.Ending);
            if (!A.IsCombatOn) {
                data.HideLogEntry();
                return;
            }
            if (!data.Def.Vars.TryString(PropertyIdH.RiggedEvent, out string evtId)) {
                data.FailEvent(s, "RiggedEventId not set in vars");
                return;
            }
            var capsuleEvent = StoryEventDef.Get(evtId);
            if (capsuleEvent == null) {
                data.FailEvent(s, $"Event not found by id: {evtId}");
                return;
            }

            // One spend loop for the whole event; the capsules only carry the raiders. Sizing
            // capsules by points let the one-raider-per-capsule cap decide headcount instead of
            // the budget - see RaidScaling.md, "Multi-entry raids". A capsule physically fits
            // one being, so headcount is capped at the capsule count.
            var maxCap = data.Def.Vars.Get(PropertyIdH.MaxCapsules, 18);
            var raid = RaidEventUtils.NewRaidFor(s, data, data.SeededRng.Int);
            raid.MaxRaiders = maxCap;
            var ids = raid.PrepareStashedRaiders();
            if (Arrays.IsEmpty(ids)) {
                data.FailEvent(s, "Failed to generate raiders");
                return;
            }
            var plan = raid.LastPlan;
            // One raider per capsule.
            var numCapsules = Mathf.Clamp(ids.Length, 1, maxCap);
            var shares = RaidPayload.Split(ids, numCapsules);

            D.Warn("Will launch {0} raid capsules carrying {1} raiders ({2:0.#} pts)",
                numCapsules, ids.Length, plan.SpentPoints);

            var except = Caches.IntSet2;
            except.Clear();
            var areas = s.Sys.Areas;
            var lastPos = Pos.Invalid;
            var nextClusterSize = data.SeededRng.Range(2, 5);
            var subEventsStarted = 0;
            for (int i = 0; i < numCapsules; i++) {
                int pos = Pos.Invalid;
                if (lastPos > 0) {
                    // try nearby pos
                    Pos.ForEachAroundRandom(data.SeededRng, lastPos, 2, false, true, (p) => {
                        if (!except.Contains(p) && EntityUtils.IsEmptyFloorWithIntegrityAt(p)) {
                            pos = p;
                            return false;
                        }
                        return true;
                    });
                }
                if (pos < 1) {
                    pos = EntityUtils.FindLandingTargetForIncoming(areas, 0,
                        out _, except);
                }
                except.Add(pos);
                if (i % nextClusterSize == nextClusterSize - 1) {
                    lastPos = Pos.Invalid;
                    nextClusterSize = data.SeededRng.Range(2, 5);
                } else {
                    lastPos = pos;
                }
                var d = StoryEventData.CreateFor(capsuleEvent);
                d.Faction = data.Faction;
                d.Flags = StoryEventDataFlags.SubEvent;
                d.Seed = data.SeededRng.Int;
                var dData = d.GetOrCreateData();
                dData.SetInt("PosIdx", pos);
                dData.SetIntSet("RaiderIds", shares[i]);
                ctx.StartEvent(d);
                if (!StoryEventData.IsOK(d)) {
                    D.Err("Failed launching capsule event #{0}", i);
                    RaidPayload.Discard(s, shares[i]);
                } else {
                    subEventsStarted++;
                }
            }
            if (subEventsStarted == 0) {
                data.FailEvent(s, "Failed to launch all capsules");
            } else {
                // No participants: the log entry would name the raiders before any capsule
                // opens. Each capsule's opening event names its own raider.
                // Once per event, with the whole raid's numbers (see PlayStatsAnalytics.md).
                PlayStats.TrackRaidStarted(data.Faction?.Id, ids.Length, plan.FinalPoints,
                    plan.GearTier, plan.SpentPoints);
                data.CreateLogEntry(GraphicsId.ObjContainersStorageCapsule,
                    $"{T.StorageContainerIncoming} <b>x{subEventsStarted}</b>",
                    T.ObjContainersStoragecapsuleDesc);
            }
        }
    }
}
