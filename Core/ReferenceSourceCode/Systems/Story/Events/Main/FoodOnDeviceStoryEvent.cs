using Game.Constants;
using Game.Data;
using Game.Systems.Firex;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Notify;

namespace Game.Systems.Story {
    public sealed class FoodOnDeviceStoryEvent : StoryEvent {
        public const string EventId = "FoodOnDevice";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new FoodOnDeviceStoryEvent());
        }

        private static bool HasFoodOnDevice(GameState s) {
            var foundCount = s.Sys.Inventory.FindUnstoredMaterialsByGroup(Caches.Piles,
                MatGroupIdH.Food);

            for (int i = 0; i < foundCount; i++) {
                var obj = EntityUtils.ParentObjAt(Caches.Piles[i].Obj.PosIdx);
                if (obj?.ENode?.IsConsuming == true || obj?.ENode?.IsProducing == true) {
                    return true;
                }
            }
            return false;
        }

        private static (Obj meal, Tile device) FindFoodOnDevice(GameState s) {
            var foundCount = s.Sys.Inventory.FindUnstoredMaterialsByGroup(Caches.Piles,
                MatGroupIdH.Food);

            for (int i = 0; i < foundCount; i++) {
                var obj = EntityUtils.ParentObjAt(Caches.Piles[i].Obj.PosIdx);
                if (obj?.ENode?.IsConsuming == true || obj?.ENode?.IsProducing == true) {
                    return (Caches.Piles[i].Obj, obj);
                }
            }
            return (null, null);
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return HasFoodOnDevice(ctx.S);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var (meal, device) = FindFoodOnDevice(s);

            if (meal == null || !meal.IsActive || device == null || !device.IsActive) {
                #if !PRODUCTION_BUILD
                if (!The.TestsAreRunning) {
                    D.Err("FoodOnDevice did not have the data to run");
                }
                #endif
                data.FailEvent(s, "No valid food/device found", isHardFailure: false);
                return;
            }

            data.TargetTile = device;
            data.TargetObj = meal.Data;
            data.SetState(s, StoryEventState.Ending);

            var rng = data.SeededRng;
            var willStartFire = ctx.S.Map.O2.At(device.PosIdx) > 0f && rng.Chance(s.Difficulty.FireChanceScale * 0.5f);
            var willExplode = Explosions.CanExplode(s.Difficulty.FireChanceScale * 0.05f, rng);

            BuildLogEntry(data, meal, device);
            CreateWarningEvent(data, device);

            var maxHealth = device.Health.MaxHealth;
            device.Health.TakeDamage(
                rng.Range(maxHealth * 0.33f, maxHealth * 0.66f),
                    DamageType.Energy);
            if (willStartFire) {
                s.Sys.Firex.TrySetOnFire(meal, Fire.CreateBalanced(s, 1f));
            }
            if (willExplode) {
                Explosion.Create(s, device.Flammable.ExplosionLevel, position: device.Position);
            }
        }

        private void CreateWarningEvent(StoryEventData data, Tile device) {
            data.Event = Notification.BuildFor(device, IconId.CFood,
                T.EvtFoodOnDeviceTitle, Consts.TicksPerDay)
                    .Group("inc", T.NounIncident, IconId.CWarning)
                    .WithOnClick(data.ShowPopup)
                    .Show();
        }

        private void BuildLogEntry(StoryEventData data, Obj meal, Tile device) {
            var evDesc = T.EvtFoodOnDeviceDesc_V2.F(
                meal.Definition.CodexLink,
                device.Definition.CodexLink);
            data.CreateLogEntry(IconId.CEnergy, T.EvtFoodOnDeviceTitle, evDesc);
        }
    }
}
