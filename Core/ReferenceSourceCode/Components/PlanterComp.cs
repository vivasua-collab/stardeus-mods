using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Components {
    //[Obsolete("FIXME create a PlantSys")]
    public sealed class PlanterComp : BaseComponent<PlanterComp>,
            ICopyableComp, IRelocatable, IRotationDependent, ICanBeUnconfigured {

        [SelfInit]
        private static void Register() {
            availablePlants = null;
            AddComponentPrototype(new PlanterComp());
        }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(plantDefId);
        private string hashedPlantDefId;
        private int plantDefIdH;
        public int PlantDefIdH {
            get {
                if (!ReferenceEquals(hashedPlantDefId, plantDefId)) {
                    hashedPlantDefId = plantDefId;
                    plantDefIdH = string.IsNullOrWhiteSpace(plantDefId) ? 0 : Hashes.S(plantDefId);
                }
                return plantDefIdH;
            }
        }
        public string DeviceCategory => T.Planter;
        public Tile DeviceTile => Tile;
        private bool isMarkedUnconfigured;
        private const string Default = "d";
        private PlantComp[] currentPlants;
        public PlantComp[] CurrentPlants => currentPlants;
        public RefillableStorageComp Water;
        private static List<PlantComp> availablePlants;
        public static List<PlantComp> AvailablePlants {
            get {
                EnsureAvailablePlants();
                return availablePlants;
            }
        }
        private int[] plantIds;
        private string plantDefId;
        private Dictionary<string, Vector2[]> plantOffsets;
        private Dictionary<string, int> plantCapacities;
        public bool IsCopyable => !IsConfigured || S.Research.IsDefUnlocked(The.Defs.Get(plantDefId));
        private readonly static string[] defaultPlantSizes = new [] { "Small" };
        private string[] supportedPlantSizes;
        public bool IsAutoHarvest;
        private bool isRegistered;
        private bool isIgnoreUnconfiguredOn;
        public bool IsIgnoreUnconfiguredOn {
            get => isIgnoreUnconfiguredOn;
            set {
                isIgnoreUnconfiguredOn = value;
                if (value) {
                    MarkConfigured();
                }
            }
        }

        public bool HasUnlockedPlant {
            get {
                var plants = AvailablePlants;
                for (int i = 0; i < plants.Count; i++) {
                    var plant = plants[i];
                    if (CanGrow(plant) && S.Research.IsDefUnlocked(plant.Def)) {
                        return true;
                    }
                }
                return false;
            }
        }


        public bool HasPlantWithId(int id) {
            return Arrays.ContainsSafe(plantIds, id);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetString("PlantDefId", plantDefId);
            data.SetIntSet("PlantIds", plantIds);
            data.SetBool("AutoHarvest", IsAutoHarvest);
            data.SetBool("IgnoreUnconfigured", isIgnoreUnconfiguredOn);
        }

        protected override void OnLoad(ComponentData data) {
            plantIds = data.GetIntSet("PlantIds", null);
            plantDefId = data.GetString("PlantDefId", null);
            IsAutoHarvest = data.GetBool("AutoHarvest", IsAutoHarvest);
            isIgnoreUnconfiguredOn = data.GetBool("IgnoreUnconfigured", isIgnoreUnconfiguredOn);
        }

        protected override void OnConfig() {
            supportedPlantSizes = Config.GetStringSet(PropertyIdH.PlantSizes, defaultPlantSizes);
            IsAutoHarvest = Config.GetBool(PropertyIdH.AutoHarvest, true);
            plantOffsets = new();
            plantCapacities = new();
            var defaultPlantOffsets = Config.GetVector2Set(PropertyIdH.PlantOffsets, null);
            for (int i = 0; i < supportedPlantSizes.Length; i++) {
                if (defaultPlantOffsets != null) {
                    plantOffsets[Default] = defaultPlantOffsets;
                }
                string size = supportedPlantSizes[i];
                var sizeOverride = Config.GetVector2Set(Hashes.S($"{size}PlantOffsets"), null);
                if (sizeOverride != null) {
                    plantOffsets[size] = sizeOverride;
                }
                plantCapacities[size] = Config.GetInt(Hashes.S($"{size}PlantCapacity"), 1);
            }
        }

        private void MarkUnconfigured() {
            if (isMarkedUnconfigured) { return; }
            if (isIgnoreUnconfiguredOn) { return; }
            // A planter salvaged before any of its plants is researched has nothing to pick yet
            if (!HasUnlockedPlant) { return; }
            isMarkedUnconfigured = true;
            ShowInfoIcon(IconId.CNotConfigured, T.InfoDeviceUnconfigured);
            S.Sig.UnconfiguredChanged.Send(this, true);
        }

        private void MarkConfigured() {
            if (!isMarkedUnconfigured) { return; }
            isMarkedUnconfigured = false;
            HideInfoIcon();
            S.Sig.UnconfiguredChanged.Send(this, false);
        }

        public override string ToString() {
            if (IsConfigured) {
                return string.Format(" * Planter [{0}]", plantDefId);
            } else {
                return " * Planter [no plant]";
            }
        }


        public override void OnLateReady(bool wasLoaded) {
            if (!Entity.TryGetComponent(out Water)) {
                D.Err("Planter requires a RefillableStorage component configured to use Water!");
            } else {
                Water.OnStockChange = OnWaterAdded;
            }
            if (!Tile.IsConstructed) { return; }
            hadWater = true;
            RegisterPlanter(true);
        }

        /// <summary>
        /// Runs every 30 in-game minutes
        /// </summary>
        public void Tick(float baseGrowthAmount, int ticksPassed) {
            if (IsConfigured) {
                EnsureCurrentPlant(Tile.Transform.Position);
            }
            if (!IsConfigured) {
                MarkUnconfigured();
                Water.Stop();
                return;
            }

            GrowPlants(baseGrowthAmount, ticksPassed);
        }

        private bool hadWater;

        private void OnWaterAdded(float amt) {
            if (amt > 0f) {
                // tell the plants to remove the not growing warning
                GrowPlants(0.0001f, 0);
            }
        }

        private void GrowPlants(float growthAmt, int ticksPassed) {
            if (currentPlants == null) { return; }
            if (Water.IsEmpty) {
                Water.NotifyLack();
                Water.CreateOrUpdateRefillGoal();
                if (hadWater) {
                    hadWater = false;
                } else {
                    return;
                }
            } else {
                hadWater = true;
            }
            float waterUsed = 0;
            for (int i = currentPlants.Length - 1; i >= 0; i--) {
                var plant = currentPlants[i];
                if (!plant.Obj.IsActive) {
                    D.Warn("Regrowing dead plant at {0}", Entity);
                    var offsets = plantOffsets.Get(plant.Size, null)
                        ?? plantOffsets.Get(Default, null);
                    StartGrowingPlant(plant.Def, offsets, i);
                    plant = currentPlants[i];
                }
                waterUsed += plant.WaterUsePerTick * ticksPassed;
                plant.IsAutoHarvest = IsAutoHarvest;
                plant.Grow(growthAmt, hadWater);
            }
            if (ticksPassed > 0) {
                if (waterUsed > 0f) {
                    if (Water.TryConsume(waterUsed)) {
                        Water.ClearDeficitFlag();
                        Water.CreateOrUpdateRefillGoal();
                    }
                } else {
                    D.Warn("Water used: zero! Water empty: {0}", Water.IsEmpty);
                }
            }
        }

        public void RealignPlants() {
            if (plantIds == null) { return; }
            if (currentPlants != null) {
                var pos = Tile.Position;
                for (int i = plantIds.Length - 1; i >= 0; i--) {
                    var plant = currentPlants[i];
                    EnsurePlantPosition(pos, i, plant);
                }
                return;
            }
        }

        private void EnsureCurrentPlant(Vector2 planterPos, bool force = false) {
            if (plantIds == null) { return; }
            if (currentPlants != null) {
                if (force) {
                    for (int i = plantIds.Length - 1; i >= 0; i--) {
                        var plant = currentPlants[i];
                        EnsurePlantPosition(planterPos, i, plant);
                    }
                }
                return;
            }
            currentPlants = new PlantComp[plantIds.Length];
            for (int i = 0; i < plantIds.Length; i++) {
                int pId = plantIds[i];
                var plant = S.Objs.Find(pId);
                if (plant == null) {
                    D.Err("Missing plant by id {0}", pId);
                    plantIds[i] = 0;
                    plantDefId = null;
                    currentPlants = null;
                    StopGrowing();
                    break;
                } else {
                    var p = currentPlants[i] = plant.GetComponent<PlantComp>();
                    EnsurePlantPosition(planterPos, i, p);
                }
            }
        }

        public void EnsurePlants() {
            EnsureCurrentPlant(Tile.Transform.Position);
        }

        private void EnsurePlantPosition(Vector2 planterPos, int i, PlantComp plant) {
            var offsets = plantOffsets.Get(plant.Size, null)
                    ?? plantOffsets.Get(Default, null);

            if (Arrays.IsEmpty(offsets)) {
                D.Err("Planter does not have {0}PlantOffsets in config ({1}.json)",
                    plant.Size, entity.DefinitionId);
            }
            var pos = planterPos + offsets[i];
            plant.Obj.Transform.TeleportTo(pos);
            plant.Obj.Graphics.TeleportTo(pos);
        }

        /*
        public override void UpdatePosIdx(int pos, int dx, int dy) {
            EnsureCurrentPlant(Tile.Position, true);
        }
        */

        public void StopGrowing() {
            plantDefId = null;
            if (!Arrays.IsEmpty(currentPlants)) {
                for (int i = 0; i < currentPlants.Length; i++) {
                    PlantComp p = currentPlants[i];
                    p.DestroyPlant();
                }
            }
            MarkUnconfigured();
            currentPlants = null;
        }

        public bool CanGrow(PlantComp plant) {
            return Arrays.Contains(supportedPlantSizes, plant.Size);
        }

        public static void EnsureAvailablePlants() {
            if (availablePlants != null) { return; }
            availablePlants = new List<PlantComp>();
            foreach (var def in The.Defs.LayerDefs(WorldLayer.FreeEntities)) {
                var plantCompConf = def.ComponentConfigFor(ComponentIdH.Plant, false);
                if (plantCompConf != null) {
                    var plant = EntityUtils.CreatePrototype<Obj>(def.Id);
                    if (plant.TryGetComponent<PlantComp>(out var plantComp)) {
                        availablePlants.Add(plantComp);
                    } else {
                        D.Err("Failed to get PlantComp! {0}", def.Id);
                    }
                }
            }
        }

        public void StartGrowing(string defId) {
            var plant = EntityUtils.CreatePrototype<Obj>(defId);
            var plantable = plant.GetComponent<PlantComp>();
            StartGrowing(plantable);
        }

        public void StartGrowing(PlantComp plant) {
            var capacity = plantCapacities.Get(plant.Size, 0);
            if (capacity == 0) {
                D.Err("Trying to start growing plant {0} in planter that has {1} capacity of 0!",
                    plant, plant.Size);
                return;
            }
            var offsets = plantOffsets.Get(plant.Size, null)
                ?? plantOffsets.Get(Default, null);

            if (offsets == null || offsets.Length != capacity) {
                D.Err("Plant Offsets configured incorrectly for planter/plant: {0}/{1} -> Expected plant size: {2}, Capacity for plant size: {3}. Offsets: {4}",
                    Tile, plant.Obj, plant.Size, capacity, offsets);
                offsets = new Vector2[capacity];
            }

            if (!Tile.IsConstructed) {
                // Auto rebuild copies the plant choice onto a blueprint, and its builder moves
                // loose objects off the footprint (AIActionConstruct.MoveObstructions), so the
                // plants are spawned on ConstructionFinished instead
                plantDefId = plant.Id;
                return;
            }

            currentPlants = new PlantComp[capacity];
            plantIds = new int[capacity];
            Water.Stop();

            for (int i = 0; i < capacity; i++) {
                StartGrowingPlant(plant.Def, offsets, i);
            }
            GrowPlants(0.001f, 0); // should do it?

            plantDefId = plant.Id;

            MarkConfigured();
        }

        private void StartGrowingPlant(Def plantDef, Vector2[] offsets, int i) {
            var planterPos = Tile.Transform.Position;
            var offset = offsets == null ? Consts.V2Zero : offsets[i];
            var plantObj = S.Objs.Create(planterPos + offset, plantDef);
            currentPlants[i] = plantObj.GetComponent<PlantComp>();
            currentPlants[i].PlanterId = EntityId;
            plantIds[i] = plantObj.Id;
            currentPlants[i].Grow(0.001f, hadWater);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                RegisterPlanter(true);
                if (IsConfigured && plantIds == null) {
                    StartGrowing(plantDefId);
                }
            }
        }

        public override void OnRemove() {
            RegisterPlanter(false);
            if (isMarkedUnconfigured) {
                S.Sig.UnconfiguredChanged.Send(this, false);
            }
            if (Tile.Reinstallable?.IsMidRelocation == true) {
                // The plants will migrate
                return;
            }
            int mpCount = Tile.Transform.GetSubtilePos(Caches.PosIdx);
            for (int i = 0; i < mpCount; i++) {
                int p = Caches.PosIdx[i];
                if (S.Objs.ByPos.TryGetValue(p, out var list)) {
                    for (int i1 = list.Count - 1; i1 >= 0; i1--) {
                        Obj obj = list.SafeGet(i1);
                        if (obj == null) { continue; }
                        if (obj.TryGetComponent<PlantComp>(out var plant)) {
                            S.Objs.Destroy(obj);
                        }
                    }
                }
            }
        }

        public string CopyConfigText => T.CopyConfigPlanter;

        public bool CanCopyTo(ICopyableComp target) {
            return target is PlanterComp;
        }

        public bool CopyConfigTo(ICopyableComp target) {
            bool changed = false;
            if (target is PlanterComp p) {
                if (IsConfigured && !p.IsConfigured) {
                    p.StartGrowing(plantDefId);
                    changed = true;
                } else if (!IsConfigured && p.IsConfigured) {
                    p.StopGrowing();
                    changed = true;
                } else if (IsConfigured && p.IsConfigured) {
                    if (plantDefId != p.plantDefId) {
                        p.StopGrowing();
                        p.StartGrowing(plantDefId);
                        changed = true;
                    }
                }
                if (IsAutoHarvest != p.IsAutoHarvest) {
                    p.IsAutoHarvest = IsAutoHarvest;
                    changed = true;
                }
                if (isIgnoreUnconfiguredOn != p.isIgnoreUnconfiguredOn) {
                    p.IsIgnoreUnconfiguredOn = isIgnoreUnconfiguredOn;
                    changed = true;
                }
                // final case is when both are not configured
            }
            return changed;
        }

        public void RelocateTo(Entity target) {
            if (!target.TryGetComponent<PlanterComp>(out var other)) {
                D.Err("Planter relocate target is not a planter: {0}", target);
                return;
            }
            other.IsIgnoreUnconfiguredOn = isIgnoreUnconfiguredOn;
            if (!IsConfigured) { return; }
            other.plantDefId = plantDefId;
            other.currentPlants = currentPlants;
            other.plantIds = plantIds;
            if (currentPlants == null) {
                other.StartGrowing(plantDefId);
                return;
            }
            // var plant = currentPlants[0];
            // var offsets = other.plantOffsets.Get(plant.Size, null)
            //         ?? other.plantOffsets.Get(Default, null);
            // MovePlants(target.Position, offsets, target.Id);
            other.AfterRotationUpdated();
            if (S.Clock.IsStopped) {
                GrowPlants(0.001f, 0); // should do it?
            }
        }

        private void MovePlants(Vector2 planterPos, Vector2[] offsets, int planterId) {
            for (int i = currentPlants.Length - 1; i >= 0; i--) {
                var offset = offsets == null ? Consts.V2Zero : offsets[i];
                var plObj = currentPlants[i].Obj;
                var newPos = planterPos + offset;
                currentPlants[i].PlanterId = planterId;
                plObj.Transform.TeleportTo(newPos);
                plObj.Graphics.TeleportTo(newPos);
            }
        }

        public void AfterRotationUpdated() {
            if (!IsConfigured) { return; }
            if (currentPlants == null) { return; }
            var plant = currentPlants[0];
            var offsets = plantOffsets.Get(plant.Size, null)
                    ?? plantOffsets.Get(Default, null);
            MovePlants(entity.Position, offsets, entity.Id);
        }

        private void RegisterPlanter(bool add) {
            if (isRegistered != add) {
                isRegistered = add;
                S.Sig.RegisterPlanter.Send(this, add);
            }
        }
    }
}
