using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class MultiFactionRaidStoryEvent : StoryEvent {
        public const string EventId = "MultiFactionRaid";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new MultiFactionRaidStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return A.IsCombatOn &&
                ctx.S.Universe.Player.Parent.Layer != UniverseLayer.Universe;
        }
        private void CreateLogEntry(StoryEventData data) {
            data.CreateLogEntry(GraphicsId.ObjectsDevicesBreachCapsule_Flying,
                T.BreachCapsuleIncoming, T.ObjectsDevicesBreachcapsuleDesc);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            data.SetState(s, StoryEventState.Ending);
            if (!A.IsCombatOn) {
                data.HideLogEntry();
                return;
            }
            CreateLogEntry(data);

            var usedFactions = new HashSet<FactionDef>();
            int additional = ctx.CurrentSector.Biome.Difficulty * s.Difficulty.Level > 0.85f ? 1 : 0;
            new CmdRestrictClock(ClockSpeed.Normal).Execute(s);
            int amt = 2 + additional;

            // One budget for the whole event (plan x RaidStrengthMul), spent up front as one
            // stashed group per faction so the multi-faction flavor survives; the capsules only
            // carry the raiders. Rounding is then bounded by the 2-3 groups, not by capsule
            // count - see RaidScaling.md, "Multi-entry raids".
            // The plan is priced with the first participating faction (which also headlines
            // the event), not a faction rolled only for the plan that may never show up; the
            // other groups rescale their share by their own RaidStrengthMul relative to it.
            var rng = data.SeededRng;
            var firstFaction = s.Sys.Diplomacy.RandomFactionForRaid(usedFactions);
            usedFactions.Add(firstFaction);
            data.Faction = firstFaction;
            var planRaid = RaidEventUtils.NewRaidFor(s, data, rng.Int);
            var plan = planRaid.ComputePlan();
            var perGroup = plan.FinalPoints / amt;

            var totalRaiders = 0;
            var spentPoints = 0f;
            for (int i = 0; i < amt; i++) {
                var raiders = i == 0 ? firstFaction
                    : s.Sys.Diplomacy.RandomFactionForRaid(usedFactions);
                usedFactions.Add(raiders);
                var groupMul = plan.FactionMul > 0f
                    ? raiders.RaidStrengthMul / plan.FactionMul : 1f;
                var groupRaid = new CombatRaid(s, rng.Int) {
                    Faction = raiders,
                    PresetPoints = perGroup * groupMul,
                    PresetGearTier = plan.GearTier,
                    PresetMaxRaiderCost = plan.RaiderCostCap,
                };
                var ids = groupRaid.PrepareStashedRaiders();
                if (Arrays.IsEmpty(ids)) {
                    D.Err("Failed to generate raiders for faction group #{0} ({1})", i, raiders);
                    continue;
                }
                totalRaiders += ids.Length;
                spentPoints += groupRaid.LastPlan.SpentPoints;
                foreach (var id in ids) {
                    var raider = s.Query.FindBeingData.Ask((uint) id);
                    if (raider != null) {
                        data.AddParticipant(raider);
                    }
                }
                LaunchBreachCapsule(s, rng, raiders, ids);
            }
            if (totalRaiders == 0) {
                data.FailEvent(s, "Failed to generate raiders");
                return;
            }
            // Counted once, only for an event that produced raiders: the groups use
            // PresetPoints, which skips CreateArmy's own increment.
            s.Situation.NumRaids++;
            // Once per event, with the whole raid's numbers (see PlayStatsAnalytics.md).
            PlayStats.TrackRaidStarted(plan.Faction?.Id, totalRaiders, plan.FinalPoints,
                plan.GearTier, spentPoints);
        }

        private void LaunchBreachCapsule(GameState s, Rng rng, FactionDef raiders,
                int[] raiderIds) {
            if (!BreachLanding.TryFind(5, 5, out int target)) {
                D.Err("Could not find good target for capsule drop in 10 attempts!");
            }

            // We have a target, let's remember it
            var hitPosition = EntityUtils.CenterOf(target);

            // var curLoc = s.Sys.Ship.Player.Location;
            var cmd = new CmdSpawnObj(hitPosition,
                rng.From(The.Defs.Matching(
                    DefIds.ObjContainersBreachCapsuleFlying)),
                        true);
            cmd.Execute(s);
            var obj = cmd.Obj;
            if (obj != null && obj.TryGetComponent<BreachCapsuleFlyingComp>(out var bc)) {
                bc.SetFaction(raiders);
                bc.RaiderIds = raiderIds;
            } else {
                D.Err("Failed to spawn a breach capsule for {0}; its raiders are lost", raiders);
                RaidPayload.Discard(s, raiderIds);
            }
        }
    }
}
