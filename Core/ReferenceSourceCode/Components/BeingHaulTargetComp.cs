using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.Creatures;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class BeingHaulTargetComp : BaseComponent<BeingHaulTargetComp>,
            IRelocatable, ICopyableComp {
        public ElectricNodeComp ENode => Tile.ENode;
        private StorageFilterMode mode;
        public StorageFilterMode Mode => mode;
        private BInt modeB;
        public BInt ModeB => modeB ??= new BInt((int) mode);
        public bool NeedsElectricity;
        public BeingStorageComp Storage;
        private List<Def> filter;
        public List<Def> Filter => filter;
        public bool IsCopyable => true;
        public string CopyConfigText => T.Storage;
        public string FilterModeName => StorageUtils.FormatStorageFilterMode(mode);

        public int SelectedFilterCount => filter?.Count ?? 0;

        public IEnumerable<Def> SelectedFilterDefs() {
            if (filter == null) { yield break; }
            foreach (var def in filter) {
                if (def != null) { yield return def; }
            }
        }

        public int HaulPriority;
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BeingHaulTargetComp());
        }

        protected override void OnConfig() {
            NeedsElectricity = Config.GetBool(PropertyIdH.NeedsElectricity, false);
            HaulPriority = Config.GetInt(PropertyIdH.Priority, 3);
            mode = Enums.FromString<StorageFilterMode>(
                Config.GetString(PropertyIdH.Mode, "AllowAll"));
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("Priority", HaulPriority);
            data.SetInt("Mode", (int) mode);
            if (filter != null) {
                data.SetIntSet("Filter",
                    filter.Select(def => def.IdH).ToArray());
            }
        }

        public bool Accepts(BeingData data) {
            if (data == null) {
                D.Err("Trying to check if haul target accepts null beingdata: {0}", Entity);
                return false;
            }
            if (Storage == null) {
                D.Err("Trying to check if haul target accepts beingdata: {0} when Storage is null! {1}",
                    data, Entity);
                return false;
            }
            if (mode == StorageFilterMode.ForbidAll) { return false; }

            if (NeedsElectricity && !Tile.ENode.IsPowerable) {
                return false;
            }

            if (!data.IsStorable()) {
                return false;
            }

            if (!Storage.Accepts(data)) { return false; }

            float bodyQuality = data.Body?.BodySlot?.Quality ?? 0f;
            if (bodyQuality < Storage.MinQuality) {
                return false;
            }

            if (mode == StorageFilterMode.AllowAll) { return true; }

            if (mode == StorageFilterMode.AllowSelected) {
                if (filter == null) { return false; }
                var fc = filter.Count;
                for (int i = 0; i < fc; i++) {
                    Def def = filter[i];
                    if (data.Species.Def == def) {
                        return true;
                    }
                }
                return false;
            }
            if (mode == StorageFilterMode.ForbidSelected) {
                if (filter == null) { return true; }
                var fc = filter.Count;
                for (int i = 0; i < fc; i++) {
                    Def def = filter[i];
                    if (data.Species.Def == def) {
                        return false;
                    }
                }
                return true;
            }

            D.Err("Unhandled mode {0} in haul target {1}", mode, Tile);
            return false;
        }

        public override void OnLateReady(bool wasLoaded) {
            if (!entity.TryGetComponent(out Storage)) {
                D.Err("BeingHaulTargetComp requires BeingStorageComp! {0}",
                    entity.DefinitionId);
            }
        }

        protected override void OnLoad(ComponentData data) {
            HaulPriority = data.GetInt("Priority", HaulPriority);
            mode = (StorageFilterMode) data.GetInt("Mode", 0);
            var filter = data.GetIntSet("Filter", null);
            if (filter != null) {
                this.filter = new List<Def>();
                foreach (var f in filter) {
                    var def = The.Defs.FastGet(f);
                    if (def != null) {
                        this.filter.Add(def);
                    }
                }
            }
        }

        public void SetFilterMode(StorageFilterMode newMode) {
            mode = newMode;
            ModeB?.Set((int) mode);
        }

        public bool GetFilterState(Def def) {
            return filter?.Contains(def) == true;
        }

        public void SetFilterState(Def def, bool value) {
            if (value) {
                filter ??= new();
                if (!filter.Contains(def)) {
                    filter.Add(def);
                }
            } else {
                filter?.Remove(def);
            }
        }

        public override string ToString() {
            return $" * BeingHaulTarget [{Tile}]";
        }

        public bool CopyConfigTo(ICopyableComp target) {
            if (target is BeingHaulTargetComp ht && ht != this) {
                D.Log("Copying from {0} to: {1}", this, target);
                if (filter == null) {
                    ht.filter = null;
                } else {
                    ht.filter = new List<Def>();
                    foreach (var d in filter) {
                        ht.filter.Add(d);
                    }
                }
                ht.mode = mode;
                ht.HaulPriority = HaulPriority;
                if (Storage != null && ht.Storage != null) {
                    ht.Storage.UpdateQualityRange(Storage.MinQuality);
                }
                return true;
            }
            return false;
        }

        public bool CanCopyTo(ICopyableComp comp) {
            return comp.Entity.DefinitionId == entity.DefinitionId;
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<BeingHaulTargetComp>(out var ht)) {
                CopyConfigTo(ht);
            }
        }
    }
}
