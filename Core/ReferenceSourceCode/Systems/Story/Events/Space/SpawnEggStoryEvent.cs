using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class SpawnEggStoryEvent : StoryEvent {
        public const string EventId = "SpawnEgg";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new SpawnEggStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            var s = ctx.S;
            return s.Components.CountByType<EggComp>() < 5 &&
                   s.Universe.Player.Parent.Layer != UniverseLayer.Universe;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;

            var allAreas = s.Sys.Areas.Areas;
            if (allAreas.Count == 0) {
                data.FailEvent(s, "Could not spawn egg, no rooms!",
                    isHardFailure: false);
                return;
            }

            var rng = data.SeededRng;
            var areas = allAreas.Values.OrderBy(a => rng.Value).ToList();

            var wd = Caches.WeightedInts;
            wd.Clear();

            using (D.Timer("FindDarkCornersForEgg", 0.000001f)) {
                foreach (var area in areas) {
                    FindDarkCornersForEgg(s, area, wd);
                }
            }

            if (!rng.TryWeightedFrom(wd, out var targetPos)) {
                data.FailEvent(s, "Could not find corner for egg",
                    needsFlagsCooldown: false, isHardFailure: false);
                return;
            }
            wd.Clear();

            // Spawn the egg
            var egg = s.Objs.Create(
                EntityUtils.CenterOf(targetPos),
                    The.Defs.Get(DefIds.ObjEggsEgg01));
            D.Ass(egg.PosIdx == targetPos, "Egg in place");
            D.Ass(egg.IsActive, "Egg active");
            var eggLight = s.Query.LightAtPos.Ask(targetPos);
            // D.Warn("Egg added at {0}: {1}: {2}. Light: {3}", Pos.String(targetPos),
            //     Units.TicksToTime(G.Ticks), egg, eggLight);

            data.TargetObj = egg.Data;
            data.CreateLogEntry(GraphicsId.ObjEggsEgg01,
                T.EvtEggDiscoverTitle, T.EvtEggDiscoverDesc);
            var text = Texts.Yellow(T.UnidentifiedObjectAppeared);
            s.Sig.ShowFanfare.Send(text);
            s.Sys.Log.AddLine(text);

            data.SetState(s, StoryEventState.Ending);
        }

        private static void FindDarkCornersForEgg(GameState s, HashSet<int> area,
                Dictionary<int, float> wd) {
            foreach (var pos in area) {
                // something is here
                if (!EntityUtils.IsEmptyFloorAt(pos)) { continue; }

                var hasWallNS = EntityUtils.IsWallAt(Pos.N(pos)) ||
                    EntityUtils.IsWallAt(Pos.S(pos));
                if (!hasWallNS) { continue; }
                var hasWallEW = EntityUtils.IsWallAt(Pos.E(pos)) ||
                    EntityUtils.IsWallAt(Pos.W(pos));
                if (!hasWallEW) { continue; }

                // it's a corner
                var targetPos = Pos.Invalid;
                if (EntityUtils.IsEmptyFloorAt(Pos.N(pos))) {
                    targetPos = Pos.N(pos);
                } else if (EntityUtils.IsEmptyFloorAt(Pos.S(pos))) {
                    targetPos = Pos.S(pos);
                } else if (EntityUtils.IsEmptyFloorAt(Pos.E(pos))) {
                    targetPos = Pos.E(pos);
                } else if (EntityUtils.IsEmptyFloorAt(Pos.W(pos))) {
                    targetPos = Pos.W(pos);
                }
                if (targetPos != Pos.Invalid && !wd.ContainsKey(targetPos)) {
                    var light = s.Query.LightAtPos.Ask(pos);
                    var lightAdj = light * 10f;
                    var w = 101f - (lightAdj * lightAdj);
                    // D.Warn("Potential corner: {0}. Light: {1}. Weight: {2}",
                    //     Pos.String(targetPos), light, w);
                    wd.Add(targetPos, w);
                }
            }
        }
    }
}
