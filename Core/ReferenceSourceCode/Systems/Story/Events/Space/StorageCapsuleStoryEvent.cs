using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using System;

namespace Game.Systems.Story {
    public sealed class StorageCapsuleStoryEvent : StoryEvent {
        public const string EventId = "StorageCapsule";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new StorageCapsuleStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ctx.S.Universe.Player.Parent.Layer != UniverseLayer.Universe;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            // if (!Ready.Areas) { return; }
            var S = ctx.S;
            int landingSpot;
            bool needsExplosion;
            data.SetState(S, StoryEventState.Ending);
            if (data.Data?.TryGetInt("PosIdx", out landingSpot) != true) {
                landingSpot = EntityUtils.FindLandingTargetForIncoming(S.Sys.Areas,
                    0, out needsExplosion);
            } else {
                needsExplosion = !EntityUtils.IsEmptyFloorAt(landingSpot, true);
            }

            if (landingSpot == Pos.Invalid) {
                D.Err("Failed to find a landing spot for Storage Capsule");
                data.FailEvent(ctx.S, "Failed to find a landing spot");
                return;
            }

            // The multi-capsule raid deals pre-generated raiders in through data; a single
            // scheduled capsule raid (Raid outcome, no ids yet) generates its own here, priced
            // now so the storm/jitter set around this call land on it instead of being lost
            // to a plan computed when the capsule opens - see RaidScaling.md, "Multi-entry
            // raids". Combat off keeps the old deferred path (the rigged event hides itself).
            // A capsule physically fits one being, so the budget only prices and gears that one.
            var raiderIds = data.Data?.GetIntSet("RaiderIds", null);
            RaidPlan stashedPlan = default;
            var stashedHere = false;
            if (Arrays.IsEmpty(raiderIds) && data.Def.Outcome == StoryEventOutcome.Raid
                    && A.IsCombatOn) {
                var raid = RaidEventUtils.NewRaidFor(S, data, data.SeededRng.Int);
                raid.MaxRaiders = 1;
                raiderIds = raid.PrepareStashedRaiders();
                if (Arrays.IsEmpty(raiderIds)) {
                    data.FailEvent(S, "Failed to generate raiders");
                    return;
                }
                stashedPlan = raid.LastPlan;
                stashedHere = true;
            }

            //D.Log("Dropping capsule at {0}", Pos.String(target));
            var cmd = new CmdSpawnObj(EntityUtils.CenterOf(landingSpot),
                The.Defs.FastGet(DefIdsH.ObjContainersStorageCapsule),
                    true, false);
            cmd.Execute(S);
            if (cmd.Obj == null) {
                D.Err("Failed spawning capsule!");
                RaidPayload.Discard(S, raiderIds);
                data.FailEvent(ctx.S, "Failed spawning capsule");
                return;
            }
            if (cmd.Obj.TryGetComponent<StorageCapsuleComp>(out var capsule)) {
                if (data.Faction != null) {
                    capsule.FactionId = data.Faction.Id;
                }
                var raidPoints = data.Data?.GetFloat("RaidPoints", 0f) ?? 0f;
                if (raidPoints > 0f) {
                    capsule.RaidPoints = raidPoints;
                    capsule.RaidGearTier = data.Data?.GetInt("RaidGearTier", -1) ?? -1;
                }
                if (!Arrays.IsEmpty(raiderIds)) {
                    capsule.RaiderIds = raiderIds;
                }
                // No participants: the log entry would name the raiders of a capsule that
                // still looks like a supply drop. The opening event names them instead.
                if (stashedHere) {
                    PlayStats.TrackRaidStarted(data.Faction?.Id, raiderIds.Length,
                        stashedPlan.FinalPoints, stashedPlan.GearTier, stashedPlan.SpentPoints);
                }
                if (needsExplosion) {
                    capsule.RigForExplosionAt(landingSpot);
                }
                if (data.Def.Vars.TryString(PropertyIdH.RiggedEvent, out string evtId)) {
                    capsule.RiggedEventId = evtId;
                }
                if ((data.Flags & StoryEventDataFlags.SubEvent) == StoryEventDataFlags.SubEvent) {
                    data.HideLogEntry();
                } else {
                    data.CreateLogEntry(capsule.Obj.Definition.Preview,
                        T.StorageContainerIncoming, T.ObjContainersStoragecapsuleDesc);
                }
                var autoOpenTo = data.Def.Vars.Get(PropertyIdH.AutoOpenInMinutesTo, 0);
                if (autoOpenTo > 0) {
                    var autoOpenFrom = data.Def.Vars.Get(PropertyIdH.AutoOpenInMinutesFrom, 0);
                    var rng = data.SeededRng;
                    capsule.AutoOpenInTicks = rng.Range(autoOpenFrom, autoOpenTo)
                        * Consts.TicksPerMinute;
                }
                data.TargetObj = capsule.Obj.Data;
            } else {
                D.Err("Failed to get StorageCapsuleComp on {0}", cmd.Obj);
                RaidPayload.Discard(S, raiderIds);
                data.FailEvent(ctx.S, "Failed getting StorageCapsuleComp on spawned object!");
                return;
            }
        }
    }
}
