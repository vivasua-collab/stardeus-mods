using Game.Constants;
using Game.Data;
using KL.Utils;
using Game.CodeGen;
using Game.Systems.AI;
using Game.Systems.Items;
using System;
using KL.Grid;
using System.Buffers;
using Game.Utils;
using System.Linq;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class ObjStorageComp : BaseComponent<ObjStorageComp>,
            IStorageDevice, IRelocatable, IUICustomTooltipProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ObjStorageComp());
        }
        public ObjHaulTargetComp HaulTarget;

        private int[] groups;
        public int[] Groups => groups;
        public float MinQuality;
        public float MaxQuality;
        private BCallback qualityChanged;
        public BCallback QualityChanged => qualityChanged ??= new();
        public int MaxCapacity;
        private int usedCapacity;
        public int UsedCapacity {
            get => usedCapacity;
            set {
                usedCapacity = value;
                UpdateStats();
            }
        }
        public bool IsFull;
        private bool traitsResolved;
        private StorableTraits traits;
        // Derived from the defs this storage's groups can hold, never saved: a filter
        // that cannot affect anything stored here is hidden and ignored.
        private StorableTraits Traits {
            get {
                if (!traitsResolved) {
                    traitsResolved = true;
                    if (groups != null) {
                        foreach (var g in groups) {
                            traits |= S.Query.StorableTraitsForGroup.Ask(g);
                        }
                    }
                }
                return traits;
            }
        }
        public bool HasQuality => (Traits & StorableTraits.Quality) != 0;
        public bool TracksOwnership => (Traits & StorableTraits.Ownership) != 0;
        public bool HoldsBodyParts => (Traits & StorableTraits.BodyPart) != 0;
        private int storageId;
        public int InPort => Tile.Transform.InPort;
        public int OutPort => Tile.Transform.OutPort;
        private InfoBar bar;
        public void UpdateQualityRange(float min, float max) {
            MinQuality = min;
            MaxQuality = max;
            QualityChanged.Invoke();
        }
        public bool AcceptsGroup(int groupId) {
            if (groupId == 0) {
                return groups == null;
            } else {
                if (groups == null) { return false; }
                return Arrays.Contains(groups, groupId);
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("MinQuality", MinQuality);
            data.SetFloat("MaxQuality", MaxQuality);
            data.SetInt("Used", usedCapacity);
        }

        protected override void OnLoad(ComponentData data) {
            usedCapacity = data.GetInt("Used", 0);
            MinQuality = data.GetFloat("MinQuality", 0f);
            MaxQuality = data.GetFloat("MaxQuality", 1f);
            UpdateCapacity();
        }

        private void UpdateCapacity() {
            IsFull = usedCapacity >= MaxCapacity;
        }

        protected override void OnConfig() {
            // Needs to be in OnConfig, or storage won't be registered!
            entity.TryGetComponent(out HaulTarget);
            MaxCapacity = Config.GetInt(PropertyIdH.MaxCapacity, 100);
            var groupStrings = Config.GetStringSet(PropertyIdH.Groups, null);
            if (!Arrays.IsEmpty(groupStrings)) {
                groups = new int[groupStrings.Length];
                for (int i = 0; i < groupStrings.Length; i++) {
                    groups[i] = Hashes.S(groupStrings[i]);
                }
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            Tile.Transform.EnsurePortPresence(false, true, true, this);
            storageId = EntityId;
            if (wasLoaded) {
                if (!Tile.IsConstructed) { return; }
                Ready.When(WhenH.ObjsLoaded,
                    () => RegisterStorage(EntityChange.Loaded));
            } else {
                MaxQuality = 1f;
            }
        }
        private void RegisterStorage(EntityChange state) {
            S.Sig.ObjStorageChanged.Send(this, state);
            UpdateStats();
        }
        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                RegisterStorage(EntityChange.Constructed);
            }
        }

        public override void OnRemove() {
            S.Sig.ObjStorageChanged.Send(this, EntityChange.Destroyed);
        }

        private void UpdateUIBlock(bool wasUpdated) {
            UpdateProgressBar();
            if (wasUpdated) {
                OnChange.Invoke();
            }
        }

        private void UpdateProgressBar() {
            if (!IsConstructed) { return; }
            if (MaxCapacity == 1) { return; }
            bar = ShowInfoBar(Consts.ColorFuelBar, (float)UsedCapacity / MaxCapacity,
                UsedCapacity, MaxCapacity, "storage.capacity.bar");
            bar.CanHaveCustomTooltip = true;
        }

        public void Eject(ObjData obj, bool bulk) {
            if (!S.Query.UnstoreObj.Ask(obj, this)) {
                D.Err("Failed to eject obj from storage: {0} | {1}", obj, this);
                return;
            }
            if (!bulk) {
                Transmit(this, MsgIdH.ObjStorageChanged);
            }
        }

        public void EjectAll() {
            ForEachStored((obj) => Eject(obj, true));
            Transmit(this, MsgIdH.ObjStorageChanged);
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<ObjStorageComp>(out var other)) {
                if (UsedCapacity > 0) {
                    ForEachStored((obj) => {
                        obj.StorageId = other.EntityId;
                    });
                }
                other.UsedCapacity = UsedCapacity;
                other.UpdateStats();
            }
        }

        public int ForEachStored(Action<ObjData> action, string searchStr = null,
                ObjData.SortMode? sortMode = null) {
            var pool = ArrayPool<ObjData>.Shared;
            var objs = pool.Rent(UsedCapacity);
            if (!sortMode.HasValue) {
                sortMode = ObjData.ObjSortMode;
            }
            var found = S.Query.FindStoredObjData.Ask(
                storageId, objs, searchStr, sortMode.Value);
            if (searchStr == null && found != UsedCapacity) {
                D.Err("Expected {0} items in storage, actual: {1}! {2}",
                    UsedCapacity, found, Tile);
                UsedCapacity = found;
            }

            for (int i = 0; i < found; i++) {
                action(objs[i]);
            }
            pool.Return(objs, true);
            return found;
        }

        public bool Store(AIAgentComp agent, ObjData objData, AIGoal goal) {
            if (agent != null && !Pos.AreAdjacent(agent.PosIdx, InPort)) {
                D.Err("Trying to store an obj from too far away! {0}, {1}, {2}, {3}",
                    Pos.StringFromTo(agent.PosIdx, InPort), agent, Tile, goal);
                return false;
            }
            if (S.Query.StoreObj.Ask(objData, this)) {
                Transmit(this, MsgIdH.ObjStorageChanged);
                return true;
            }
            return false;
        }

        public bool TryTake(AIAgentComp agent, ObjData objData, AIGoal goal) {
            if (!Pos.AreAdjacent(agent.PosIdx, InPort)) {
                D.Err("Trying to take an obj from too far away! {0}, {1}, {2}, {3}",
                    Pos.StringFromTo(agent.PosIdx, InPort), agent, Tile, goal);
                return false;
            }
            if (S.Query.UnstoreObj.Ask(objData, this)) {
                Transmit(this, MsgIdH.ObjStorageChanged);
                return true;
            }
            return false;
        }

        private void UpdateStats() {
            UpdateCapacity();
            UpdateUIBlock(true);
        }

        public string ShowCustomTooltip(string currentTooltip, InfoItem item) {
            var sb = Caches.StringBuilder;
            sb.Clear();
            if (bar != null) {
                sb.Append(bar.Info);
            }
            const int max = 10;
            var map = Caches.MapIntInt;
            map.Clear();
            ForEachStored((obj) => {
                if (!map.TryGetValue(obj.Def.IdH, out var cnt)) {
                    cnt = 0;
                }
                cnt++;
                map[obj.Def.IdH] = cnt;
            }, searchStr: null, sortMode: ObjData.SortMode.Unsorted);
            if (map.Count == 0) {
                return sb.ToString();
            }
            var ordered = map.OrderByDescending(kv => kv.Value).Take(max);
            foreach (var kv in ordered) {
                var defIdH = kv.Key;
                var cnt = kv.Value;
                var def = The.Defs.FastGet(defIdH);
                sb.AppendFormat("\n{0}: x{1}", def.CodexLink, cnt);
            }
            if (map.Count > max) {
                sb.AppendLine();
                sb.Append(Texts.Grey($"+{map.Count - max}"));
            }
            return sb.ToString();
        }
        private void EjectAndDispose(ObjData obj) {
            Eject(obj, true);
            if (obj.Obj.TryGetComponent<DisposableComp>(out var disp)) {
                disp.TryOrderDispose(false, out _);
            }
        }

        public void DisposeByQuality(float v) {
            D.Warn("Disposing by quality: {0} or lower in {1}", Units.Percentage(v), this);
            ForEachStored((obj) => {
                if (obj.Quality <= v) { EjectAndDispose(obj); }
            });
        }

        // Used from the Storage tab's Dispose menu to clear out gear that fails a
        // provenance or condition check (second-hand, dead person's, tattered) rather
        // than a quality threshold.
        public void DisposeWhere(Func<ObjData, bool> predicate, string reason) {
            D.Warn("Disposing {0} in {1}", reason, this);
            ForEachStored((obj) => {
                if (predicate(obj)) { EjectAndDispose(obj); }
            });
        }
        public override string ToString() {
            return $"ObjStorage[{Tile}]";
        }
    }
}
