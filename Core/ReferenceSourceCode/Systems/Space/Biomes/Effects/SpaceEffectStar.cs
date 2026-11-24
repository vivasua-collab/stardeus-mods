using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Notify;
using Game.Systems.Voice;
using Game.UI;
using Game.Utils;
using Game.Visuals;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceEffectStar : SpaceEffect {
        [SelfInit]
        private static void Init() {
            Register(new SpaceEffectStar());
        }

        public override string Id => "Star";
        public override string Name => null;
        private NotificationHandle notification;
        private Curve tempIncrPerHCurve;
        private Curve solarOutputCurve;
        protected override void Cleanup() {
            notification = default;
            tempIncrPerHCurve = null;
            solarOutputCurve = null;
        }
        private void NotifyStar(GameState s, float tempIncrPerHour, float solarMul) {
            var deg = Units.CelsiusToKelvin(tempIncrPerHour);
            var temp = Texts.Temperature(deg);
            var sol = Units.Percentage(solarMul - 1f, clamp: false);
            notification = Notification.Build(IconId.CWarning,
                    AddSectorPrefix(T.SpaceEffectStar), 0, NotificationType.Critical)
                .Sticky()
                .WithTooltip(T.SpaceEffectStarDesc_V2.F(sol, temp))
                .WithVoice(VoiceLine.StarProximity)
                .Show();
            s.Sig.ShowFanfare.Send(AddSectorPrefix(Texts.Yellow(T.SpaceEffectStar)));
        }

        public override void OnEnterSector(GameState s, SpaceSector sector) {
            solarOutputCurve ??= Tunables.StarSolarOutputMulCurve;
            tempIncrPerHCurve ??= Tunables.StarTempIncreaseDegreesPerHourCurve;
            var val = tempIncrPerHCurve.Evaluate(sector.Biome.Difficulty);
            var solarMul = solarOutputCurve.Evaluate(sector.Biome.Difficulty);
            // reduce heatup at easier difficulties
            val *= s.Difficulty.Reduction;
            s.BB.Set(BBKeyH.SolarOutputMultiplier, solarMul);
            s.BB.Set(BBKeyH.HeatAccumulationPerTick, val / Consts.TicksPerHour);
            NotifyStar(s, val, solarMul);
        }

        public override void OnExitSector(GameState s, SpaceSector sector) {
            if (notification.IsValid) {
                Notification.Remove(ref notification);
            }
            // normally we don't have to do this, because we will enter hyperspace
            // that will set output to 0, but in dev mode we can jump without hyperspace
            // so let's set this to 1 here just in case
            s.BB.Set(BBKeyH.SolarOutputMultiplier, 1f);
            s.BB.Set(BBKeyH.HeatAccumulationPerTick, 0f);
        }

        public override void OnLoad(GameState s, SpaceSector sector) {
            OnEnterSector(s, sector);
        }

        public override void Tick(GameState s, long ticks, SpaceSector currentSector) {
        }
    }
}
