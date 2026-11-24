using Game.Achievements;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.Story;

namespace Game.Components {
    public sealed class EggComp : BaseComponent<EggComp>, IRareTickListener, IXenoDetectable {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new EggComp());
        }
        // temperature Kelvin => percent per day
        private static readonly Curve hatchRateTempCurve = new float[] {
            0f, 0.2f, // 5 days
            273f, 0.33f, // 3 days
            273f + 50f, 1f, // 1 day
        };

        private bool isDiscovered;
        private bool hasHatched;
        // will go from 0f to 1f and will spawn at 1f
        private float hatchProgress;
        public string ForcedEventId;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.HealthWentToZero) {
                EntityUtils.SpawnRawMaterial(MatIdH.CrawlerGoo, 1, Obj.PosIdx, 0.1f);
                ProduceShell();
            } else if (message == MsgIdH.EntitySelected && !isDiscovered) {
                Discover();
            }
        }

        // Invoked from the story event
        public bool TryHatch(long ticks) {
            if (hasHatched) {
                D.Err("Trying to hatch an egg that has already hatched! {0}", Obj);
                return false;
            }
            S.Objs.Destroy(Obj);
            ProduceShell();
            return true;
        }

        private void ProduceShell() {
            if (hasHatched) { return; }
            hasHatched = true;
            var shellDefId = $"{Entity.DefinitionId}_Hatched";
            var shellDef = The.Defs.TryGet(shellDefId);
            if (shellDef != null) {
                var cmd = new CmdSpawnObj(Obj.Position, shellDef, true, false);
                S.CmdQ.Enqueue(cmd);
            } else {
                D.Err("Shell Def is not found for egg: {0}", Obj);
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsDiscovered", isDiscovered);
            data.SetFloat("HatchProgress", hatchProgress);
        }

        protected override void OnLoad(ComponentData data) {
            isDiscovered = data.GetBool("IsDiscovered", false);
            hatchProgress = data.GetFloat("HatchProgress", 0f);
        }

        public override string ToString() {
            return $" * Egg [Hatch:{hatchProgress}/Discovered:{isDiscovered}]";
        }

        private void Discover() {
            isDiscovered = true;
            The.AchievementTracker.Unlock(AchievementDiscoverEgg.AchievementId);
        }

        #if !PRODUCTION_BUILD
        public float DebugHatchProgress => hatchProgress;
        public bool DebugIsDiscovered => isDiscovered;
        #endif
        private long lastTick;
        public void RareTick(long currentTick) {
            if (lastTick == 0) {
                lastTick = currentTick;
                return;
            }
            var ticksPassed = currentTick - lastTick;
            lastTick = currentTick;
            // 1. read the temperature
            // 2. apply temperature to hatch rate
            // 3. move spawnProgress forward by hatch rate
            // 4. if spawnProgress >=1f, spawn


            var temp = S.GetTemp(Entity.PosIdx);
            var hatchRatePerDay = hatchRateTempCurve.Evaluate(temp);
            var ratePerTick = hatchRatePerDay / Consts.TicksPerDay;
            ratePerTick *= Tunables.EggHatchRate;
            hatchProgress += ticksPassed * ratePerTick;
            // D.Warn("RatePerday: {0}. Per tick: {1}. Ticks passed: {2}. Prog: {3}",
            //     hatchRatePerDay, ratePerTick, ticksPassed, hatchProgress);

            if (hatchProgress >= 1f) {
                HideInfoBar();
                TriggerStoryEvent();
            } else {
                var bar = ShowInfoBar(Consts.ColorFuelBar,
                    hatchProgress, hatchProgress, 1, T.VerbHatching);
                bar.IsPretranslated = true;
            }
        }
        public int Seed => Hashes.Quick(12497, EntityId);
        private void TriggerStoryEvent() {
            if (ForcedEventId != null) {
                var def = StoryEventDef.Get(ForcedEventId);
                var forcedData = StoryEventData.CreateFor(def);
                forcedData.Seed = Seed;
                forcedData.TargetObj = Obj.Data;
                if (S.Query.StartStoryEvent.Ask(forcedData)) {
                    return;
                }
                D.Err("Failed starting forced egg event: {0} on {1}",
                    ForcedEventId, Obj);
            }
            var hook = StoryEventHook.CreateFor(Obj, StoryEventFlags.Egg)
                .WithSeed(Seed)
                .Instant();
            var data = S.Query.AddStoryEventHook.Ask(hook);
            if (!StoryEventData.IsOK(data)) {
                D.Err("Failed spawning egg loot: {0}", Obj);
            }
        }
    }
}
