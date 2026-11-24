using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Firex;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class ElectricConnectionFailureStoryEvent : StoryEvent {
        public const string EventId = "ElectricConnectionFailure";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new ElectricConnectionFailureStoryEvent());
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var rng = data.SeededRng;

            var numFound = s.Components.FindAllMatching(Caches.ElectricNodes256,
                (n) => n.IsConnector && n.Node.NumConnections > 1);

            if (numFound < 1) {
                data.FailEvent(s, "No active electric connectors with enough connections found.",
                    isHardFailure: false);
                return;
            }

            Arrays.Shuffle(rng, Caches.ElectricNodes256, numFound);
            for (int i = 0; i < numFound; i++) {
                var selectedConnector = Caches.ElectricNodes256[i];

                var conns = A.Grid.ConnectionsFor(selectedConnector.EntityId, Caches.IntArray32);
                if (conns == 0) { continue; }

                Arrays.Shuffle(rng, Caches.IntArray32, conns);

                Tile target = null;
                for (int j = 0; j < conns; j++) {
                    var targetDeviceId = Caches.IntArray32[j];
                    var targetDevice = s.FindEntity<Tile>(targetDeviceId);
                    if (targetDevice.ENode.IsConnector) { continue; }
                    if (!targetDevice.ENode.IsConsuming) { continue; }
                    if (s.GetO2(targetDevice.PosIdx) < Consts.MinOxygen) {
                        // can't set on fire
                        continue;
                    }
                    target = targetDevice;
                }

                if (target == null) { continue; }

                if (!target.IsConstructed) {
                    continue;
                }

                // Gotcha!
                TriggerFailureEffect(ctx, data, target);
                return;
            }
            data.FailEvent(s, "Could not choose a target device for connection failure, no good candidate!",
                isHardFailure: false);
        }

        private void TriggerFailureEffect(StoryContext ctx, StoryEventData data, Tile target) {
            var s = ctx.S;
            var rng = data.SeededRng;

            // A fuse takes the hit whether the failure would explode or start a fire
            if (target.TryGetComponent<WithFuseComp>(out var fuse) && fuse.HasFuse
                    && fuse.TryBlowFuse()) {
                DeviceMalfunctionStoryEvent.CreateMalfunctionEvent(data, target, true);
                data.SetState(s, StoryEventState.Ending);
                return;
            }

            if (target.Flammable != null && target.Flammable.CanExplode
                    && s.Difficulty.Level > 1.25f
                    && Explosions.CanExplode(0.01f, rng)) {
                target.Flammable.CreateExplosion(false);
                // can't use EvtElConFailDesc because it says "while connecting"
                data.CreateLogEntry(IconId.CExplosion, T.EvtElConFail,
                    T.EvtElDeviceDamagedDesc_V1.F(Texts.LinkToTile(target)));
            } else {
                s.Sys.Firex.TrySetOnFire(target, Fire.CreateBalanced(s, 1.2f));
                int nOnFire = 0;
                foreach (var n in target.Transform.Outline) {
                    if (rng.Chance(s.Difficulty.FireChanceScale * 0.1f)) {
                        if (nOnFire > 3 && rng.Chance(0.5f)) { continue; }
                        var t = EntityUtils.GetTopmostParentTile(n);
                        if (!t.Flammable.IsOnFire && s.Sys.Firex.TrySetOnFire(
                                t, Fire.CreateBalanced(s, 1f))) {
                            nOnFire++;
                        }
                    }
                }
                // can't use EvtElConFailDesc because it says "while connecting"
                data.CreateLogEntry(IconId.CFire, T.EvtElConFail,
                    T.EvtElDeviceDamagedDesc_V1.F(Texts.LinkToTile(target)));
            }

            var fanfareText = $"{Texts.Red(T.EvtElConFail)}: {Texts.LinkTo(Texts.LinkColor(target.NameOrLabel), target.PosIdx)}";
            s.Sig.ShowFanfare.Send(fanfareText);
            s.Sys.Log.AddLine(target, Texts.Red(T.EvtElConFail));
            data.TargetTile = target;
            data.SetState(s, StoryEventState.Ending);
        }
    }
}
