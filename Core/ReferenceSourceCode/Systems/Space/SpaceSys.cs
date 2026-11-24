using System;
using System.Collections.Generic;
using Game.Components;
using Game.Data;
using Game.Data.Space;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Space {
    public sealed class SpaceSys : GameSystem, ISaveableSpecial {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new SpaceSys());
        }

        public const string SysId = "SpaceSys";

        public override string Id => SysId;
        private SpaceMap map;
        private SpaceSector currentSector;
        private readonly List<SpaceEffect> activeEffects = new();
        // Effects are shared instances that keep their state in memory only, so timers such
        // as the artillery fungus spawn reset on every load. The ones implementing ISaveable
        // keep theirs here.
        private Dictionary<string, ComponentData> loadedEffectData;
        public void LoadSpecial(SystemsDataSpecial sd) {
            if (sd.Space != null) {
                map = SpaceMapData.Deserialize(S, sd.Space.Map);
                S.Query.GetSpaceMap.SetResponder(GetSpaceMap);
                loadedEffectData = sd.Space.Effects;
            }
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Space ??= new();
            sd.Space.Map = SpaceMapData.Serialize(map);
            Dictionary<string, ComponentData> effects = null;
            for (int i = 0; i < activeEffects.Count; i++) {
                if (activeEffects[i] is ISaveable saveable) {
                    (effects ??= new())[activeEffects[i].Id] = saveable.Save();
                }
            }
            sd.Space.Effects = effects;
        }

        public override void Unload() {
        }

        protected override void OnInitialize() {
            S.Sig.InitializeSpace.AddListener(OnInitSpace);
            S.Sig.SpaceObjectEnter.AddListener(OnEnterSO);
            S.Sig.AfterLoadState.AddListener(OnLoad);
        }

        private void OnLoad(GameState state) {
            S.Clock.OnTick.AddListener(OnTick);
            Ready.WhenGameLate(this, "RestoreSpaceEffects", RestoreSpaceEffects);
        }

        private void RestoreSpaceEffects() {
            currentSector = S.Universe.Player.Parent.Sector;
            if (currentSector != null) {
                if (currentSector.Biome.Effects.Count > 0) {
                    foreach (var effect in currentSector.Biome.Effects) {
                        var eff = effect.Key.Effect;
                        eff.OnLoad(S, currentSector);
                        if (eff is ISaveable saveable && loadedEffectData != null
                                && loadedEffectData.TryGetValue(eff.Id, out var data)) {
                            saveable.Load(data);
                        }
                        activeEffects.Add(eff);
                    }
                }
            }
            loadedEffectData = null;
        }

        private void OnTick(long ticks) {
            for (int i = activeEffects.Count - 1; i >= 0; i--) {
                var fx = activeEffects[i];
                fx.Tick(S, ticks, currentSector);
            }
        }

        private void OnEnterSO(SpaceObject obj, SpaceObject previous) {
            var sector = obj.Sector;
            if (currentSector == sector) {
                D.Err("Trying to enter same sector more than once! {0}", sector);
                return;
            }
            if (activeEffects.Count > 0) {
                for (int i = activeEffects.Count - 1; i >= 0; i--) {
                    SpaceEffect fx = activeEffects[i];
                    fx.OnExitSector(S, currentSector);
                }
                activeEffects.Clear();
            }
            currentSector = sector;
            if (sector == null) {
                return;
            }
            if (sector.Biome.Effects.Count > 0) {
                if (obj.HasTerraformable) {
                    D.Err("Space object is terraforming target, but has permanent space effects! Fixing: {0} | {1}",
                        obj, D.DumpCollection(sector.Biome.Effects));
                    sector.Biome.Effects.Clear();
                }
                foreach (var effect in sector.Biome.Effects) {
                    var eff = effect.Key.Effect;
                    AddEffect(eff, sector);
                }
            }
        }

        /// <summary>
        /// Public for space console command.
        /// </summary>
        public void AddEffect(SpaceEffect eff, SpaceSector sector) {
            // D.Warn("Adding space effect at {0} => {1}. Active effects before: {2}",
            //     sector.Name, eff, activeEffects.Count);
            eff.OnEnterSector(S, sector);
            activeEffects.Add(eff);
        }

        public int RemoveEffects() {
            int activeEffectCount = activeEffects.Count;
            int sectorEffectCount = currentSector?.Biome.Effects.Count ?? 0;
            if (activeEffects.Count > 0) {
                for (int i = activeEffects.Count - 1; i >= 0; i--) {
                    SpaceEffect fx = activeEffects[i];
                    fx.OnExitSector(S, currentSector);
                }
                activeEffects.Clear();
            }
            currentSector?.Biome.Effects.Clear();
            return Math.Max(activeEffectCount, sectorEffectCount);
        }

        private void OnInitSpace(SpaceRegion startingRegion) {
            map = startingRegion.Map;
            // D.Warn("setting map: {0} -> {1}", map, map.Regions[0]);
            S.Query.GetSpaceMap.SetResponder(GetSpaceMap);
        }

        private SpaceMap GetSpaceMap() {
            // D.Warn("getting map: {0} -> {1}", map, map.Regions[0]);
            return map;
        }

        public void SetMap(SpaceMap map) {
            this.map = map;
        }
    }
}
