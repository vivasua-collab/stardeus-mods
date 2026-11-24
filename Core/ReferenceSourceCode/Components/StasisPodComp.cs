using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Story;
using Game.Visuals;
using KL.Randomness;
using KL.Utils;

using Game.Utils;
namespace Game.Components {
    public sealed class StasisPodComp : BaseComponent<StasisPodComp>,
            IRelocatable, ITickListener {
        private bool isOpened;
        public bool IsOpened => isOpened;
        public int OutPort => Tile.Transform.OutPort;
        public int ContentsSeed;
        public long LandingTime;
        public string RiggedEventId;
        private Particles dustFx;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new StasisPodComp());
        }
        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsOpened", isOpened);
            data.SetInt("ContentsSeed", ContentsSeed);
            data.SetLong("LandingTime", LandingTime);
            data.SetString("RiggedEvent", RiggedEventId);
        }

        protected override void OnLoad(ComponentData data) {
            isOpened = data.GetBool("IsOpened", false);
            ContentsSeed = data.GetInt("ContentsSeed", 1);
            LandingTime = data.GetLong("LandingTime", 0);
            RiggedEventId = data.GetString("RiggedEvent", null);
        }

        public override string ToString() {
            return " * StasisPod";
        }

        private void CheckImmutable() {
            Tile.SetImmutable(this, !isOpened);
        }

        public override void OnLateReady(bool wasLoaded) {
            if (ContentsSeed == 0) {
                ContentsSeed = S.Rng.Range(1, int.MaxValue);
            }
            Tile.Transform.EnsurePortPresence(false, false, true, this);
            if (!Tile.Reinstallable.IsPending && Tile.IsConstructed) {
                if (wasLoaded) {
                    Ready.WhenGame(this, "Check Immutable", CheckImmutable);
                } else {
                    CheckImmutable();
                }
            }
        }
        public void ShowDustFX() {
            if (dustFx != null) { return; }
            dustFx = Particles.Create("StasisPodDustFX", Rng.UUint,
                entity.Position);
        }

        public void KillDustFX() {
            if (dustFx == null) { return; }
            dustFx.Play(false);
            dustFx.DestroyAfter(30);
            dustFx = null;
        }

        public void Open() {
            isOpened = true;
            Tile.SetImmutable(this, false);
            KillDustFX();
        }

        public override void OnRemove() {
            KillDustFX();
        }

        public void Launch() {
            if (!Entity.IsActive) {
                D.Err("Trying to launch inactive stasis pod: {0}", Tile);
                return;
            }
            var podFlying = S.Objs.Create(Entity.Position,
                The.Defs.Get(DefIds.ObjContainersStasisPodFlying), false);
            if (podFlying != null) {
                if (podFlying.TryGetComponent<StasisPodFlyingComp>(out var flying)) {
                    flying.LaunchUpwards(Tile);
                }
            }
            Tile.MaterialStorage.DestroyContents();
        }

        public void RelocateTo(Entity target) {
            var pod = target.GetComponent<StasisPodComp>();
            if (isOpened) {
                pod.Open();
                HideInfoBar();
            }
            pod.LandingTime = LandingTime;
            pod.ContentsSeed = ContentsSeed;
            pod.CheckImmutable();
        }

        private void CountdownToOpen(long currentTick) {
            if (LandingTime == 0) {
                LandingTime = currentTick;
            }
            var openAt = LandingTime + Consts.TicksPer2Hours;
            if (openAt <= currentTick) {
                if (isOpened) {
                    D.Err("Trying to trigger a story event for a stasis pod that was already opened! {0}", Tile);
                    return;
                }
                Open();
                HideInfoBar();
                TriggerStoryEvent();
            } else {
                ShowDustFX();
                var openProgress = (currentTick - LandingTime)
                    / (float) Consts.TicksPer2Hours;
                var bar = ShowInfoBar(Consts.ColorFuelBar,
                    openProgress, openProgress, 1, T.Waiting);
                bar.IsPretranslated = true;
            }
        }

        private void TriggerStoryEvent() {
            if (RiggedEventId != null) {
                var riggedEvent = StoryEventDef.Get(RiggedEventId);
                if (riggedEvent == null) {
                    D.Err("StorageCapsule RiggedEventId not available: {0} | {1}",
                        RiggedEventId, entity);
                } else {
                    var data = StoryEventData.CreateFor(riggedEvent);
                    data.TargetTile = Tile;
                    data.Seed = ContentsSeed;
                    if (S.Query.StartStoryEvent.Ask(data)) {
                        return;
                    }
                    // A soft failure means the rigged event's premise went away while the
                    // pod was landing (e.g. the deserter it was bringing back is gone), which
                    // is expected - the pod falls back to a regular stasis pod event below
                    if ((data.Flags & StoryEventDataFlags.SoftFailure) == 0) {
                        D.Err("Failed starting rigged stasis pod event: {0} | {1}",
                            data, entity);
                    }
                }
            }
            var hook = StoryEventHook.CreateFor(this, StoryEventFlags.StasisPod)
                .Instant();
            S.Query.AddStoryEventHook.Ask(hook);
        }

        public void Tick(long currentTick) {
            if (isOpened) { return; }
            if (!Tile.IsConstructed) { return; }
            CountdownToOpen(currentTick);
        }
    }
}
