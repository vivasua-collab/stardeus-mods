using Game.Achievements;
using Game.Audio;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.AI.Combat;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class HullpiercerRaidStoryEvent : StoryEvent {
        public const string EventId = "HullpiercerRaid";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new HullpiercerRaidStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return A.IsCombatOn && !A.IsInHyperspace;
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            base.OnTick(ctx, data);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            data.SetState(s, StoryEventState.Ending);
            if (!A.IsCombatOn) {
                data.HideLogEntry();
                return;
            }
            // Log entry and notification
            data.CreateLogEntry(GraphicsId.ObjContainersHullpiercer,
                T.EvRaidMapEdgeTitle, T.EvRaidMapEdgeDesc);
            var title = Texts.Red(T.EvRaidMapEdgeTitle);
            s.Sig.ShowFanfare.Send(title);
            s.Sys.Log.AddLine(title);

            // Hullpiercer spawn logic
            var rng = data.SeededRng;
            var cnt = rng.Range(
                    data.Def.Vars.Get(PropertyIdH.MinInstances, 1),
                    data.Def.Vars.Get(PropertyIdH.MaxInstances, 2) + 1);
            // D.Warn("Spawning {0} hullpiercers", cnt);
            var dir = rng.From(Facing.Types);
            new CmdRestrictClock(ClockSpeed.Normal).Execute(s);

            var targets = Caches.IntList;
            targets.Clear();

            var doors = s.Components.AllByType<DoorComp>();
            if (!Arrays.IsEmpty(doors)) {
                foreach (DoorComp door in doors) {
                    if (door.IsConstructed && door.ENode.IsConnected) {
                        targets.Add(door.Tile.PosIdx);
                    }
                }
            }
            targets.Add(s.Components.FindFirstByType<ShipComputerComp>()
                ?.Tile.PosIdx ?? s.Map.Grid.CenterPos);

            var target = Pos.ToVector(rng.From(targets));

            // D.Warn("Hull piercer target: {0}", target);

            // One spend loop for the whole event; the piercers only carry the raiders. Sizing
            // the fleet by points-per-piercer let per-piercer caps and floors decide headcount
            // instead of the budget - see RaidScaling.md, "Multi-entry raids". Each piercer
            // fits MaxRaidersPerPiercer; budget the rolled fleet cannot carry stays unspent.
            var raid = RaidEventUtils.NewRaidFor(s, data, rng.Int);
            raid.MaxRaiders = cnt * HullpiercerComp.MaxRaidersPerPiercer;
            var ids = raid.PrepareStashedRaiders();
            if (Arrays.IsEmpty(ids)) {
                data.FailEvent(s, "Failed to generate raiders");
                return;
            }
            var plan = raid.LastPlan;
            // Never launch more piercers than there are raiders.
            cnt = Mathf.Clamp(cnt, 1, ids.Length);
            var shares = RaidPayload.Split(ids, cnt);

            Vector2? origin = null;
            if (data.Data?.TryGetVector2("Origin", out var orig) == true) {
                origin = orig;
            }

            for (int i = 0; i < cnt; i++) {
                Vector2 pos = origin.HasValue ? origin.Value :
                    EntityUtils.RandomMapEdgePoint(rng, dir, 0.25f);
                var obj = HullpiercerComp.Spawn(ctx.S, rng, target + rng.InsideUnitCircle() * 5f, pos);
                if (obj.TryGetComponent<HullpiercerComp>(out var hp)) {
                    hp.SetFaction(data.Faction);
                    hp.RaiderIds = shares[i];
                } else {
                    RaidPayload.Discard(s, shares[i]);
                }
            }
            foreach (var id in ids) {
                var raider = s.Query.FindBeingData.Ask((uint) id);
                if (raider != null) {
                    data.AddParticipant(raider);
                }
            }
            // Once per event, with the whole raid's numbers, so the calibration telemetry
            // sees one raid rather than one per piercer.
            PlayStats.TrackRaidStarted(data.Faction?.Id, ids.Length, plan.FinalPoints,
                plan.GearTier, plan.SpentPoints);
            D.Warn("Initiating hullpiercer raid: {0} piercers carrying {1} raiders ({2:0.#} pts)",
                cnt, ids.Length, plan.SpentPoints);
            The.AchievementTracker.Unlock(AchievementHullpiercerRaid.AchievementId);
            Sounds.PlayMusic(SoundtrackMood.Danger, true);
            s.Clock.DangerPause();
        }
    }
}
