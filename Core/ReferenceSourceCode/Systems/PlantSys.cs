using System;
using System.Buffers;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems {
    public sealed class PlantSys : GameSystem {
        public const string SysId = "PlantSys";
        public override string Id => SysId;


        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new PlantSys());
        }

        private readonly List<PlanterComp> planters = new();
        private readonly List<IWildPlant> wildPlants = new();

        public override void Unload() {
        }

        public string GetName() {
            return Id;
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.RegisterPlanter.AddListener(OnRegisterPlanter);
            S.Sig.RegisterWildPlant.AddListener(OnRegisterWildPlant);
        }

        private void OnLoadSave(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
        }

        private const int plantTickRate = Consts.TicksPer30Minutes;

        private void OnTick(long ticks) {
            var baseRatePerTick = Tunables.BasePlantGrowthPercentPerDay / Consts.TicksPerDay;
            baseRatePerTick *= plantTickRate;

            // we want to tick each planter once per 10 ticks
            var tickMod = ticks % plantTickRate;
            for (int i = planters.Count - 1; i >= 0; i--) {
                var iMod = i % plantTickRate;
                if (tickMod == iMod) {
                    // D.Warn("Ticking planter with idx {0} on tick mod {1}|{2}: {3}",
                    //     i, tickMod, iMod, Units.TicksToTime(ticks));
                    PlanterComp p = planters[i];
                    p.Tick(baseRatePerTick, plantTickRate);
                }
            }

            for (int i = wildPlants.Count - 1; i >= 0; i--) {
                var wp = wildPlants[i];
                wp.Grow(baseRatePerTick, plantTickRate);
            }
        }
        private void OnRegisterWildPlant(IWildPlant plant, bool added) {
            if (added) {
                if (!plant.Obj.IsActive) {
                    D.Err("Trying to add (register) an inactive wild plant! {0}", plant.Obj);
                    return;
                }
                wildPlants.Add(plant);
            } else {
                wildPlants.Remove(plant);
            }
        }

        private void OnRegisterPlanter(PlanterComp planter, bool added) {
            if (added) {
                if (!planter.Tile.IsConstructed) {
                    D.Err("Trying to add (register) an unconstructed planter! {0}", planter.Tile);
                    return;
                }
                planters.Add(planter);
            } else {
                planters.Remove(planter);
            }
        }
    }
}
