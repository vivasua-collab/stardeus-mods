using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.AI.Combat;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class BreachCapsuleRaidStoryEvent : StoryEvent {
        public override string Id => "BreachCapsuleRaid";
        [SelfInit]
        private static void Register() {
            Register(new BreachCapsuleRaidStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return A.IsCombatOn && !A.IsInHyperspace;
        }

        private void CreateLogEntry(StoryEventData data) {
            data.CreateLogEntry(GraphicsId.ObjectsDevicesBreachCapsule_Flying,
                T.BreachCapsuleIncoming, T.ObjectsDevicesBreachcapsuleDesc);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            if (!A.IsCombatOn) {
                data.SetState(ctx.S, StoryEventState.Ending);
                data.HideLogEntry();
                return;
            }
            int tooCloseDist = 10;
            // Tests run in smaller environments
            if (The.TestsAreRunning) {
                tooCloseDist = 5;
            }
            if (!BreachLanding.TryFind(tooCloseDist, 5, out int target)) {
                D.ErrSoft("Could not find good target for capsule drop in 10 attempts!");
            }

            var hitPosition = EntityUtils.CenterOf(target);
            if (EntityUtils.IsWithinBounds(target)) {
                SpawnCapsule(ctx, data, hitPosition);
            } else {
                data.FailEvent(ctx.S, "Out of bounds");
            }
        }

        private void SpawnCapsule(StoryContext ctx, StoryEventData data,
                Vector2 hitPosition) {
            var s = ctx.S;
            data.SetState(s, StoryEventState.Ending);
            // Ship combat and the extortion signal start this event by hand rather than
            // through the picker, which is what normally seeds it; the raiders below are
            // generated from data.SeededRng, so an unseeded start must not error out.
            if (data.Seed == 0) {
                data.Seed = s.Rng.Int;
            }
            // Priced and spawned now, carried hidden by the capsule (RaidPayload), instead of a
            // fresh plan on landing: a scheduled raid's storm and jitter are only set for the
            // duration of this call, so a plan computed minutes later saw neither. Same design
            // as the hullpiercer raid - see RaidScaling.md, "Multi-entry raids".
            var raid = RaidEventUtils.NewRaidFor(s, data, data.SeededRng.Int);
            var ids = raid.PrepareStashedRaiders();
            if (Arrays.IsEmpty(ids)) {
                data.FailEvent(s, "Failed to generate raiders");
                return;
            }
            var plan = raid.LastPlan;
            new CmdRestrictClock(ClockSpeed.Normal).Execute(s);
            var cmd = new CmdSpawnObj(hitPosition,
                s.Rng.From(The.Defs.Matching(
                    DefIds.ObjContainersBreachCapsuleFlying)), true);
            cmd.Execute(s);
            var cap = cmd.Obj;
            if (cap == null || !cap.TryGetComponent<BreachCapsuleFlyingComp>(out var bc)) {
                RaidPayload.Discard(s, ids);
                data.FailEvent(s, "Failed to spawn a breach capsule");
                return;
            }
            bc.SetFaction(data.Faction);
            bc.RaiderIds = ids;
            if (data.Def.Vars.Get(PropertyIdH.Stealth, false)) {
                // stealth = instant attach
                bc.SetOrigin(hitPosition);
            } else if (data.Data != null) {
                if (data.Data.TryGetVector2("Origin", out var origin)) {
                    bc.SetOrigin(origin);
                }
            }
            CreateLogEntry(data);
            foreach (var id in ids) {
                var raider = s.Query.FindBeingData.Ask((uint) id);
                if (raider != null) {
                    data.AddParticipant(raider);
                }
            }
            // Once here, with the whole raid's numbers; the landing side only releases the
            // carried raiders and does not track (see PlayStatsAnalytics.md).
            PlayStats.TrackRaidStarted(data.Faction?.Id, ids.Length, plan.FinalPoints,
                plan.GearTier, plan.SpentPoints);
        }
    }
}
