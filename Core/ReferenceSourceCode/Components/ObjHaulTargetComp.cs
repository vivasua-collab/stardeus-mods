using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.Items;
using Game.Systems.Inventory;
using KL.Grid;
using KL.Randomness;
using Game.Systems.BodyParts;

namespace Game.Components {
    public sealed class ObjHaulTargetComp : BaseComponent<ObjHaulTargetComp>,
            IRelocatable, ICopyableComp, IHaulTarget {
        public ElectricNodeComp ENode => Tile.ENode;
        private StorageFilterMode mode;
        public StorageFilterMode Mode => mode;
        private BInt modeB;
        public BInt ModeB => modeB ??= new BInt((int) mode);
        public bool IsAutoHaul;
        public bool NeedsElectricity;
        // Plain per-instance state, not config-backed like Mode/AutoHaul - no def has a
        // reason to hardcode "always refuse used gear" out of the box.
        public bool ForbidSecondHand;
        public bool ForbidDeadMans;
        public bool ForbidCrippled;
        public bool ForbidScarred;
        public ObjStorageComp Storage;
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

        public bool HasSubmenuNow => true;
        public string SubmenuTitle => T.StorageSettings;

        public int HaulPriority;
        // Allocated once: TryOrderHauling runs for every storage all game
        private System.Func<ObjData, bool> acceptsInReach;
        private System.Func<ObjData, bool> AcceptsInReachCached =>
            acceptsInReach ??= AcceptsInReach;
        private int reachIsland;
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ObjHaulTargetComp());
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
            data.SetBool("ForbidSecondHand", ForbidSecondHand);
            data.SetBool("ForbidDeadMans", ForbidDeadMans);
            data.SetBool("ForbidCrippled", ForbidCrippled);
            data.SetBool("ForbidScarred", ForbidScarred);
            data.SetInt("Priority", HaulPriority);
            data.SetInt("Mode", (int) mode);
            if (filter != null) {
                data.SetIntSet("Filter",
                    filter.Select(def => def.IdH).ToArray());
            }
        }

        public bool Accepts(ObjData obj) {
            if (obj == null) {
                D.Err("Trying to check if haul target accepts null objdata: {0}", Entity);
                return false;
            }
            if (Storage == null) {
                D.Err("Trying to check if haul target accepts objdata: {0} when Storage is null! {1}",
                    obj, Entity);
                return false;
            }
            if (mode == StorageFilterMode.ForbidAll) { return false; }

            if (NeedsElectricity && !Tile.ENode.IsPowerable) {
                return false;
            }

            // Storages whose contents have no meaningful quality (urns) hide the range,
            // so a range set before it was hidden must not keep refusing their objects.
            if (Storage.HasQuality) {
                var maxQ = Storage.MaxQuality;
                if (maxQ >= 1f) {
                    maxQ = float.MaxValue;
                }

                if (obj.Quality < Storage.MinQuality || obj.Quality > maxQ) {
                    return false;
                }
            }

            // Only stops new arrivals - like the quality range above, it is not
            // retroactive on whatever is already stored here.
            if (ForbidSecondHand && ItemOwnership.IsUsed(obj, null)) { return false; }
            if (ForbidDeadMans && ItemOwnership.IsDeadMansGear(obj)) { return false; }
            if (ForbidCrippled && IsCrippled(obj)) { return false; }
            if (ForbidScarred && IsScarred(obj)) { return false; }

            if (!obj.IsStorable(out var groupH)) {
                return false;
            }

            if (obj.Obj?.Pile?.TreatAsObj == false) {
                return false;
            }

            if (!Storage.AcceptsGroup(groupH)) { return false; }

            if (mode == StorageFilterMode.AllowAll) { return true; }

            if (mode == StorageFilterMode.AllowSelected) {
                if (filter == null) { return false; }
                var fc = filter.Count;
                for (int i = 0; i < fc; i++) {
                    Def def = filter[i];
                    if (obj.Def == def) {
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
                    if (obj.Def == def) {
                        return false;
                    }
                }
                return true;
            }

            D.Err("Unhandled mode {0} in haul target {1}", mode, Tile);
            return false;
        }

        private static bool IsCrippled(ObjData obj) {
            var state = obj.BodyPartState;
            return state != null
                && (state.Flags & BodyPartStateFlags.Crippled) == BodyPartStateFlags.Crippled;
        }

        internal static bool IsScarred(ObjData obj) {
            return !Arrays.IsEmpty(obj.BodyPartState?.Scars);
        }

        // Auto-hauling only: an explicit Store order goes to any storage that Accepts it.
        // See AutoHaulReach
        private bool AcceptsInReach(ObjData obj) {
            var pos = obj.Obj?.PosIdx ?? Pos.Invalid;
            return AutoHaulReach.CanAutoHaul(S.Sys.Areas, reachIsland, pos)
                && Accepts(obj);
        }

        public override void OnLateReady(bool wasLoaded) {
            if (!entity.TryGetComponent(out Storage)) {
                D.Err("ObjHaulTargetComp requires ObjStorageComp! {0}",
                    entity.DefinitionId);
            }
        }

        protected override void OnLoad(ComponentData data) {
            HaulPriority = data.GetInt("Priority", HaulPriority);
            IsAutoHaul = data.GetBool("AutoHaul", IsAutoHaul);
            ForbidSecondHand = data.GetBool("ForbidSecondHand", false);
            ForbidDeadMans = data.GetBool("ForbidDeadMans", false);
            ForbidCrippled = data.GetBool("ForbidCrippled", false);
            ForbidScarred = data.GetBool("ForbidScarred", false);
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

        public bool TryOrderHauling() {
            if (!IsAutoHaul) { return false; }
            if (HaulPriority == 0) { return false; }
            if (!Tile.IsConstructed || Tile.Constructable.HasGoal) { return false; }
            if (entity.Goal != null) { return false; }
            if (Storage.IsFull) { return false; }
            if (Tile.Health.IsDamaged) { return false;}
            if (mode == StorageFilterMode.ForbidAll) { return false; }
            if (NeedsElectricity && !Tile.ENode.IsPowerable) { return false; }
            if (!Rng.UChance(HaulPriority / 5f)) { return false; }

            var cache = Caches.ObjData128;
            reachIsland = S.Sys.Areas.IslandAt(entity.PosIdx);
            var found = S.Query.FindHaulableUnstoredObjs.Ask(cache, AcceptsInReachCached);
            if (found == 0) { return false; }
            ObjData closest = null;
            var pos = entity.PosIdx;
            var dist = 0f;
            for (int i = 0; i < found; i++) {
                var item = cache[i];
                var posIdx = item.Obj.PosIdx;
                var distN = Pos.SqrDistance(pos, posIdx);
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

        private bool CreateGoalFor(ObjData obj) {
            if (entity.Goal != null) {
                D.Err("Has cancellable ad while creating a new one! {0}", this);
                return false;
            }
            if (obj.Obj.StorableObj.TryOrderStoring(false, false, out _)) {
                obj.Obj.Goal.WithPriority(HaulPriority);
                return true;
            }
            return false;
        }
        public override string ToString() {
            return $" * ObjHaulTarget [{Tile}]";
        }

        public void SetFilterMode(StorageFilterMode mode) {
            this.mode = mode;
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

        public bool CopyConfigTo(ICopyableComp target) {
            if (target is ObjHaulTargetComp ht && ht != this) {
                D.Log("Copying from {0} to: {1}", this, target);
                ht.IsAutoHaul = IsAutoHaul;
                ht.ForbidSecondHand = ForbidSecondHand;
                ht.ForbidDeadMans = ForbidDeadMans;
                ht.ForbidCrippled = ForbidCrippled;
                ht.ForbidScarred = ForbidScarred;
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
                    ht.Storage.UpdateQualityRange(Storage.MinQuality, Storage.MaxQuality);
                }
                return true;
            }
            return false;
        }

        public bool CanCopyTo(ICopyableComp comp) {
            return comp.Entity.DefinitionId == entity.DefinitionId;
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<ObjHaulTargetComp>(out var ht)) {
                CopyConfigTo(ht);
            }
        }
    }
}
