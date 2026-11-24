using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

using Game.Utils;
using Game.Systems.Traits;

namespace Game.Systems.AI {
    public sealed class AINeedLight : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedLight());
        }

        public override int[] RelatedStats => new [] { StatIdH.Light, StatIdH.Sight };

        // A shadow dweller enjoys the dark and still gets the well lit bonus, so no light
        // level leaves the need unmet
        public override bool TryGetSatisfaction(BeingData data, out float satisfaction) {
            satisfaction = data.HasTrait(TraitIdH.ShadowDweller) ? 1f
                : MainStatSatisfaction(data.StatsBeing.Light);
            return true;
        }

        public override void Tick(GameState s, BeingData data, long ticks) {
            var stats = data.StatsBeing;
            var light = stats.Light;
            var pos = data.Being.PosIdx;
            // the light map only lights rooms, so outside the ship it reads 0 everywhere;
            // starlight keeps Sight, work and movement out there from acting pitch dark
            var lightAmt = s.Sys.Areas.IsOutside(pos)
                ? Tunables.OutsideLight
                : s.Query.LightAtPos.Ask(pos);
            light.UpdateBase(lightAmt);
            if (ticks % Consts.TicksPer10Minutes == 9) {
                TickWindowless(s, data, pos, ticks);
            }
            if (ticks % Consts.TicksPerHour != 19) { return; }
            if (data.HasCondition(ConditionIdH.Sleeping) || data.Body.IsIncapacitated
                    || s.Sys.Areas.IsOutside(pos)) {
                return;
            }
            if (data.Mind.Health != null && Rng.UChance(0.5f)) {
                if (lightAmt >= 0.85f) {
                    int bonus = 3;
                    s.Sig.AddMentalEffect.Send(data, MentalEffect.CreateWithIcon(
                        data.Being.PosIdx, IconId.CLightbulb, ticks,
                            Consts.TicksPerHour, T.EnvWelLit, bonus));
                } else if (lightAmt < 0.33f) {
                    var name = T.EnvTooDark;
                    int bonus = -Mathf.RoundToInt((1f - lightAmt) * 7f);
                    if (data.HasTrait(TraitIdH.ShadowDweller)) {
                        name = TraitDef.MoodReason(T.EnvTooDark, TraitIdH.ShadowDweller);
                        bonus = -bonus + 3;
                    }
                    s.Sig.AddMentalEffect.Send(data, MentalEffect.CreateWithIcon(
                        data.Being.PosIdx, IconId.CLightbulb,
                        ticks, Consts.TicksPerHour, name, bonus));
                }
            }
        }

        // Only an hour spent in windowless areas counts, so walking through a windowless
        // hallway or an airlock does not. The mood is refreshed while the being stays and
        // fades within an hour after it leaves.
        private static void TickWindowless(GameState s, BeingData data, int pos, long ticks) {
            if (data.Mind.Health == null) { return; }
            var areaId = s.Sys.Areas.At(pos);
            // A door belongs to no room: stepping through one between two windowless rooms
            // neither counts nor resets the time
            if (areaId == -1) { return; }
            var counters = data.Counters;
            var timeIn = counters.Get(CounterIdH.TicksInWindowlessArea);
            if (!IsInWindowlessArea(s, data, areaId, ticks)) {
                if (timeIn != 0) {
                    counters.Set(CounterIdH.TicksInWindowlessArea, 0);
                }
                return;
            }
            if (timeIn < Consts.TicksPerHour) {
                timeIn = Math.Min(timeIn + Consts.TicksPer10Minutes, Consts.TicksPerHour);
                counters.Set(CounterIdH.TicksInWindowlessArea, timeIn);
                if (timeIn < Consts.TicksPerHour) { return; }
            }
            s.Sig.AddMentalEffect.Send(data, MentalEffect.CreateWithIcon(
                pos, IconId.CLightbulb, ticks,
                Consts.TicksPerHour, T.NounWindowlessRoom, -3));
        }

        private static bool IsInWindowlessArea(GameState s, BeingData data, int areaId,
                long ticks) {
            // outside the ship (AreaSys.IsOutside) or not in a room
            if (areaId <= 0) { return false; }
            if (data.HasCondition(ConditionIdH.Sleeping) || data.Body.IsIncapacitated) {
                return false;
            }
            var numWindows = s.Query.NumWindowsInArea.Ask(areaId);
            if (numWindows > 0) { return false; }
            #if !PRODUCTION_BUILD
            // the uncached count walks every window in the game, so only once an hour
            if (ticks % Consts.TicksPerHour == 19) {
                var actualNumWindows = s.Sys.Areas.WindowsByAreaUncached(areaId);
                if (actualNumWindows != numWindows) {
                    D.Err("Mismatch of actual windows VS cached in area: {0}. Cached: {1}. Actual: {2}. Being: {3}",
                        areaId, numWindows, actualNumWindows, data);
                    EntityUtils.DebugPauseFocusOn(data.Being);
                }
            }
            #endif
            return true;
        }

        public override void InitializeFor(BeingData data) {
        }
        public override void InitializeForNew(BeingData data) {
        }

        public AINeedLight() : base(NeedId.Light) {}
    }
}
