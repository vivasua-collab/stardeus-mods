using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Hints;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using KL.Randomness;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class MatHaulTargetComp : BaseComponent<MatHaulTargetComp>,
            IRelocatable, ICopyableComp, IHaulTarget {
        public ElectricNodeComp ENode => Tile.ENode;
        private StorageFilterMode mode;
        public StorageFilterMode Mode => mode;
        private BInt modeB;
        public BInt ModeB => modeB ??= new BInt((int) mode);
        public bool IsAutoHaul;
        public bool NeedsElectricity;
        public IMatStorage Storage => Tile.MaterialStorage;
        // private List<Mat> filter;
        /// <summary>
        /// int = mat type idh, bool = is selected
        /// </summary>
        private Dictionary<int, bool> filter;

        public bool IsCopyable => Storage.IsPublic;

        public string CopyConfigText => T.Storage;

        public string FilterModeName => StorageUtils.FormatStorageFilterMode(mode);

        public int SelectedFilterCount {
            get {
                if (filter == null) { return 0; }
                int count = 0;
                foreach (var kv in filter) { if (kv.Value) { count++; } }
                return count;
            }
        }

        public IEnumerable<MatType> SelectedFilterTypes() {
            if (filter == null) { yield break; }
            foreach (var kv in filter) {
                if (!kv.Value) { continue; }
                var mt = MatType.FastGet(kv.Key);
                if (mt != null) { yield return mt; }
            }
        }

        public int HaulPriority;
        // Allocated once: TryOrderHauling runs for every storage all game
        private System.Func<MatType, bool> acceptsCached;
        private System.Func<MatType, bool> AcceptsCached => acceptsCached ??= Accepts;
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new MatHaulTargetComp());
        }

        protected override void OnConfig() {
            IsAutoHaul = Config.GetBool(PropertyIdH.AutoHaul, true);
            NeedsElectricity = Config.GetBool(PropertyIdH.NeedsElectricity, false);
            HaulPriority = Config.GetInt(PropertyIdH.Priority, 3);
            mode = Enums.FromString<StorageFilterMode>(
                Config.GetString(PropertyIdH.Mode, "AllowAll"));
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("AutoHaul", IsAutoHaul);
            data.SetInt("Priority", HaulPriority);
            data.SetInt("Mode", (int) mode);
            if (filter != null) {
                var filters = new Mat[filter.Count];
                int i = 0;
                foreach (var f in filter) {
                    var mt = MatType.FastGet(f.Key);
                    if (mt != null) {
                        filters[i++] = new Mat {
                            Type = mt,
                            StackSize = f.Value ? 1 : 0,
                            MaxStackSize = 1,
                        };
                    }
                }
                data.SetMats("Filter", filters);
            }
        }

        public bool Accepts(MatType type) {
            if (type == null) {
                D.Err("Trying to check if haul target accepts null mat type: {0}", Entity);
                return false;
            }
            if (Storage == null) {
                D.Err("Trying to check if haul target accepts mat type: {0} when Storage is null! {1}",
                    type, Entity);
                return false;
            }
            if (mode == StorageFilterMode.ForbidAll) { return false; }

            if (NeedsElectricity && !Tile.ENode.IsPowerable) {
                if (Ready.Gen) {
                    return false;
                }
            }

            if (!Storage.AcceptsGroup(type.GroupH)) {
                return false;
            }

            if (mode == StorageFilterMode.AllowAll) { return true; }

            if (mode == StorageFilterMode.AllowSelected) {
                if (filter == null) { return false; }
                if (filter.TryGetValue(type.IdH, out var isSelected)) {
                    return isSelected;
                }
                return false;
            }
            if (mode == StorageFilterMode.ForbidSelected) {
                if (filter == null) { return true; }
                if (filter.TryGetValue(type.IdH, out var isSelected)) {
                    return !isSelected;
                }
                return true;
            }

            D.Err("Unhandled mode {0} in haul target {1}", mode, Tile);
            return false;
        }

        protected override void OnLoad(ComponentData data) {
            HaulPriority = data.GetInt("Priority", HaulPriority);
            IsAutoHaul = data.GetBool("AutoHaul", IsAutoHaul);
            mode = (StorageFilterMode) data.GetInt("Mode", 0);
            var filter = data.GetMats("Filter", null);
            if (filter != null) {
                this.filter = new();
                foreach (var f in filter) {
                    if (f.Type != null) {
                        this.filter[f.Type.IdH] = f.StackSize == 1;
                    }
                }
            }
        }

        public bool TryOrderHauling() {
            if (!IsAutoHaul) { return false; }
            if (HaulPriority == 0) { return false; }
            if (!Tile.IsConstructed || Tile.Constructable.HasGoal) { return false; }
            if (entity.Goal != null) { return false; }
            if (Storage.IsFull) { return false; }
            if (Tile.Health.IsDamaged) { return false;}
            if (mode == StorageFilterMode.ForbidAll) { return false; }
            if (NeedsElectricity && !Tile.ENode.IsPowerable) { return false; }
            if (!Rng.UChance(HaulPriority / 5f)) {
                return false;
            }

            var cache = Caches.Piles128;
            // Only what this storage may haul on its own: see AutoHaulReach
            var found = S.Sys.Inventory.FindHaulableUnstoredMaterials(cache, AcceptsCached,
                Tile.PosIdx);
            if (found == 0) {
                return false;
            }
            UnstoredMatComp closest = null;
            var pos = entity.Position;
            var dist = 0f;
            for (int i = 0; i < found; i++) {
                var item = cache[i];
                /*
                if (item.Entity.HasCancellableAd) {
                    continue;
                }
                */
                var distN = Vectors.SqrDistance(pos, item.Entity.Position);
                if (closest == null || distN < dist) {
                    closest = item;
                    dist = distN;
                }
            }
            if (closest != null) {
                return CreateGoalFor(closest);
            }
            return false;
        }

        private bool CreateGoalFor(UnstoredMatComp pile) {
            if (entity.Goal != null) {
                D.Err("Has cancellable ad while creating a new one! {0}", this);
                return false;
            }
            var goal = pile.CreateStoringGoal(false, false);
            if (goal != null) {
                return pile.Entity.TrySetGoal(goal.WithPriority(HaulPriority));
            }
            return false;
        }

        public override string ToString() {
            return $" * MatHaulTarget [{Tile}]";
        }



        private void MovePriority(int direction) {
            HaulPriority = UIUtils.AdjustPriority0To5(HaulPriority, direction);
        }

        public void CycleFilterMode() {
            mode = (StorageFilterMode) (((int) mode + 1) % 4);
        }

        public void SetFilterMode(StorageFilterMode mode) {
            this.mode = mode;
            ModeB?.Set((int) mode);
        }

        public bool GetFilterState(MatType type) {
            EnsureFilterFor(type);
            return filter.TryGetValue(type.IdH, out var v) && v;
        }

        public void SetFilterState(MatType type, bool value) {
            EnsureFilterFor(type);
            filter[type.IdH] = value;
        }

        private void EnsureFilterFor(MatType type) {
            filter ??= new();
            filter.TryAdd(type.IdH, false);
        }

        public bool CopyConfigTo(ICopyableComp target) {
            if (target is MatHaulTargetComp ht && ht != this) {
                D.Log("Copying from {0} to: {1}", this, target);
                ht.IsAutoHaul = IsAutoHaul;
                if (filter == null) {
                    ht.filter = null;
                } else {
                    ht.filter = new(filter);
                }
                ht.mode = mode;
                ht.HaulPriority = HaulPriority;
                return true;
            }
            return false;
        }

        public bool CanCopyTo(ICopyableComp comp) {
            if (comp.Entity.DefinitionId == entity.DefinitionId) {
                return true;
            }
            if (comp is MatHaulTargetComp other) {
                if (Arrays.EqualContents(Storage.Groups, other.Storage.Groups)) {
                    return true;
                }
            }
            return false;
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<MatHaulTargetComp>(out var ht)) {
                CopyConfigTo(ht);
            }
        }
    }
}
