using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class StasisPodStoryEvent : StoryEvent {
        public const string EventId = "StasisPod";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new StasisPodStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            if (ctx.S.Universe.Player.Parent.Layer == UniverseLayer.Universe) { return false; }
            // Defs rigged to deliver a returning deserter only make sense while one exists.
            if (def.Vars.Get(PropertyIdH.RiggedEvent, string.Empty) == StasisPodDeserterReturnsStoryEvent.DefId) {
                return DeserterUtils.HasReturnableDeserter(ctx.S);
            }
            return true;
        }

        private void CreateLogEntry(StoryEventData data) {
            data.CreateLogEntry(GraphicsId.ObjectsDevicesStasisPod_Flying_D,
                T.EvtStasisPodTitle,
                T.EvtStasisPodDesc);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            data.SetState(s, StoryEventState.Ending);
            var rng = data.SeededRng;
            var landingSpot = ChooseLandingSpot(s, rng, out var needsExplosion);

            if (landingSpot == Pos.Invalid) {
                data.FailEvent(s, "Failed to find a landing spot for StasisPod");
                return;
            }

            //D.Log("Dropping pod at {0}", Pos.String(target));
            var cmd = new CmdSpawnObj(EntityUtils.CenterOf(landingSpot),
                The.Defs.FastGet(DefIdsH.ObjContainersStasisPodFlying),
                    true, false);
            cmd.Execute(s);
            if (cmd.Obj == null) {
                data.FailEvent(s, "Failed spawning stasis pod");
                return;
            }
            if (!cmd.Obj.TryGetComponent<StasisPodFlyingComp>(out var pod)) {
                data.FailEvent(s, $"Spawned obj is not stasis pod: {cmd.Obj}");
                return;
            }
            if (data.Def.Vars.TryString(PropertyIdH.RiggedEvent, out string evtId)) {
                pod.RiggedEventId = evtId;
            }
            if (needsExplosion) {
                pod.RigForExplosionAt(landingSpot);
            }
            CreateLogEntry(data);
        }

        private int ChooseLandingSpot(GameState s, Rng rng, out bool needsExplosion) {
            var allowDangerousLanding = rng.Chance(
                s.Difficulty.Increment *
                    Tunables.StasisPodDangerousLandingChance);
            if (allowDangerousLanding) {
                EntityUtils.TryFindSafeLandingSpot(
                    out var dangerousSpot, 0, out needsExplosion);
                return dangerousSpot;
            }

            var excluded = Caches.IntSet2;
            excluded.Clear();
            const int maxAttempts = 64;
            for (int i = 0; i < maxAttempts; i++) {
                var candidate = EntityUtils.FindLandingTargetForIncoming(
                    s.Sys.Areas, 1f, out needsExplosion, excluded);
                if (candidate == Pos.Invalid) {
                    break;
                }
                if (!needsExplosion && HasSafeLandingArea(candidate)) {
                    return candidate;
                }
                excluded.Add(candidate);
            }

            return EntityUtils.FindLandingTargetFallbackForIncoming(
                s.Sys.Areas, out needsExplosion, excluded);
        }

        private static bool HasSafeLandingArea(int root) {
            Pos.ToXY(root, out var rootX, out var rootY);
            for (int dx = 0; dx <= 2; dx++) {
                for (int dy = 0; dy <= 2; dy++) {
                    var x = rootX + dx;
                    var y = rootY + dy;
                    if (Pos.IsOOBXY(x, y)) {
                        return false;
                    }
                    if (!EntityUtils.IsGoodForLanding(Pos.FromXY(x, y))) {
                        return false;
                    }
                }
            }
            return true;
        }

    }
}
