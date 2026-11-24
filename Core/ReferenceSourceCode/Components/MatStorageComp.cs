using System.Collections.Generic;
using Game.Constants;
using KL.Collections;
using Game.Data;
using KL.Utils;
using UnityEngine;
using Game.Utils;
using Game.CodeGen;
using System;
using System.Diagnostics;
using System.Linq;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.Inventory;
using Game.Systems.Firex;
using Game.Systems.Notify;

namespace Game.Components {
    public sealed class MatStorageComp : BaseComponent<MatStorageComp>,
            IMatStorage, IStorageDevice, IRelocatable,
            IUICustomTooltipProvider,
            IExplosivenessProvider {
        public enum SortMode {
            Alphabetic,
            CountsDesc,
            CountsAsc,
        }
        public static SortMode Sort;

        public IOrderedEnumerable<KeyValuePair<MatType, int>> SortedContents {
            get {
                if (rawMaterials == null) { return null; }
                switch (Sort) {
                    case SortMode.CountsAsc: return rawMaterials.OrderBy(m => m.Value);
                    case SortMode.Alphabetic: return rawMaterials.OrderBy(m => m.Key.NameT);
                    default: return rawMaterials.OrderByDescending(m => m.Value);
                }
            }
        }

        private List<string> sortOptions;

        public List<string> SortOptions{
            get {
                if (sortOptions == null) {
                    sortOptions = new List<string> {
                        T.SortName,
                        $"{T.SortCount} {GlyphMap.ArrowDown}",
                        $"{T.SortCount} {GlyphMap.ArrowUp}",
                    };
                }
                return sortOptions;
            }
        }
        public MatHaulTargetComp HaulTarget => haulTarget;
        private MatStorageReservations reservations;
        public MatStorageReservations Reservations => reservations;
        private StorageType storageType;
        public StorageType StorageType => storageType;
        private HashSet<Being> unreachableByFlying;

        private NotificationHandle unreachableEvt;
        public int ExplosionLevel {
            get {
                if (Arrays.IsEmpty(rawMaterials)) { return 0; }
                int max = 0;
                foreach (var mat in rawMaterials) {
                    if (mat.Value > 0 && mat.Key.ExplosionLevel > max) {
                        max = mat.Key.ExplosionLevel;
                    }
                }
                return max;
            }
        }

        public Curve ExplosivenessCurve {
            get {
                if (Arrays.IsEmpty(rawMaterials)) { return null; }
                int max = 0;
                MatType mostExplosive = null;
                foreach (var mat in rawMaterials) {
                    if (mat.Value > 0 && mat.Key.ExplosionLevel > max) {
                        max = mat.Key.ExplosionLevel;
                        mostExplosive = mat.Key;
                    }
                }
                return mostExplosive?.ExplosivenessCurve;
            }
        }

        private MatHaulTargetComp haulTarget;
        public bool IsPublic { get; private set; }
        public bool HideUI;
        public int MaxCapacity => baseCapacity + ExtraCapacity;
        private int baseCapacity;
        // Added through installing upgrades, should not be saved / loaded
        public int ExtraCapacity;
        // Maintained incrementally by AddMat/RemoveMat/ClearMats and
        // resynced from rawMaterials by RecalculateUsedCapacity(); do not
        // write it directly. See VerifyUsedCapacity for the drift check.
        private int usedCapacity;
        public int UsedCapacity => usedCapacity;
        public int AvailableCapacity => MaxCapacity - UsedCapacity;
        public float FillPercent => UsedCapacity / (float) MaxCapacity;
        public bool IsFull => UsedCapacity >= MaxCapacity;
        // public bool IsNearlyFull => UsedCapacity >= MaxCapacity * 0.95f;
        public bool IsEmpty => UsedCapacity == 0;
        public int InPort => Tile.Transform.InPort;
        public int OutPort => Tile.Transform.OutPort;
        public int Priority => haulTarget?.HaulPriority ?? 0;

        private Dictionary<MatType, int> rawMaterials;
        public Dictionary<MatType, int> Contents => rawMaterials;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new MatStorageComp());
        }
        private int[] groups;
        public int[] Groups => groups;
        //public string GroupId { get; private set; }
        public bool AcceptsGroup(int groupId) {
            if (groupId == 0) {
                return groups == null;
            } else {
                if (groups == null) { return false; }
                return Arrays.Contains(groups, groupId);
            }
        }
        private bool showFulnessBar;
        public bool IsExplosivenessProviderDisabled { get; private set; }

        protected override void OnConfig() {
            IsExplosivenessProviderDisabled =
                Config.GetBool(PropertyIdH.DisableExplosivenessProvider, false);
            baseCapacity = Config.GetInt(PropertyIdH.MaxCapacity, 100);
            IsPublic = Config.GetBool(PropertyIdH.IsPublic, false);
            showFulnessBar = Config.GetBool(PropertyIdH.ShowFulnessBar, false);
            reservations = new MatStorageReservations(this);
            var groupStrings = Config.GetStringSet(PropertyIdH.Groups, null);
            if (!Arrays.IsEmpty(groupStrings)) {
                groups = new int[groupStrings.Length];
                for (int i = 0; i < groupStrings.Length; i++) {
                    groups[i] = Hashes.S(groupStrings[i]);
                }
            }

            var stType = Config.GetString(PropertyIdH.StorageType, null);
            if (stType == null) {
                storageType = StorageType.TakeAndStore;
            } else {
                storageType = Enums.FromString<StorageType>(stType);
            }
        }

        // Why were we adding to inventory here?
        //public override void OnReady(bool wasLoaded) {
            //AddToInventory();
        ////}
        public override void OnLateReady(bool wasLoaded) {
            entity.TryGetComponent(out haulTarget);

            if (storageType == StorageType.StoreOnly) {
                Tile.Transform.EnsurePortPresence(false, true, false, this);
            } else if (storageType == StorageType.TakeOnly) {
                Tile.Transform.EnsurePortPresence(false, false, true, this);
            } else {
                Tile.Transform.EnsurePortPresence(false, true, true, this);
            }

            if (!Tile.IsConstructed) { return; }

            AddToInventory();
            UpdateProgressBar();
            if (wasLoaded && rawMaterials != null) {
                ValidateContents();
            }
        }
        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                AddToInventory();
            }
        }

        private static readonly Version ejectWarnVersion = new Version(0, 6, 26);

        private void ValidateContents() {
            if (StorageType != StorageType.TakeAndStore) { return; }
            RecalculateUsedCapacity();
            List<MatType> toEject = null;
            foreach (var mat in rawMaterials) {
                if (!AcceptsGroup(mat.Key.GroupH)) {
                    (toEject ??= new()).Add(mat.Key);
                }
            }
            if (toEject != null) {
                if (Migrations.Migration.CurrentVersion > ejectWarnVersion) {
                    #if !PRODUCTION_BUILD
                    if (!The.TestsAreRunning) {
                        D.Warn("Ejecting incompatible stuff from storage: {0}: {1}",
                            this, D.Dump(toEject));
                    }
                    #else
                    D.Warn("Ejecting incompatible stuff from storage: {0}: {1}",
                        this, D.Dump(toEject));
                    #endif
                }
                S.Clock.OnNextUpdate(() => {
                    for (int i = 0; i < toEject.Count; i++) {
                        MatType mat = toEject[i];
                        Eject(mat, true);
                    }
                });
            }
        }
        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<MatStorageComp>(out var other)) {
                other.rawMaterials = rawMaterials;
                other.RecalculateUsedCapacity();
                rawMaterials = null;
                usedCapacity = 0;
                other.UpdateProgressBar();
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            Mat[] rawMats;
            if (rawMaterials != null) {
                rawMats = new Mat[rawMaterials.Count];
                var i = 0;
                foreach (var m in rawMaterials) {
                    rawMats[i++] = new Mat {
                        Type = m.Key,
                        StackSize = m.Value
                    };
                }
            } else {
                rawMats = null;
            }
            data.SetMats("RawMats", rawMats);
            data.SetBool("IsHidden", isHidden);
        }

        protected override void OnLoad(ComponentData data) {
            var mats = data.GetMats("RawMats", null);
            isHidden = data.GetBool("IsHidden", isHidden);
            if (mats != null) {
                rawMaterials = new();
                for (int i = 0; i < mats.Length; i++) {
                    var mat = mats[i];
                    rawMaterials[mat.Type] = mat.StackSize;
                }
            }
            RecalculateUsedCapacity();
        }

        // The only paths allowed to write to rawMaterials. Keep every
        // mutation here so usedCapacity (cached to avoid an O(n) scan on
        // every UsedCapacity/AvailableCapacity/IsFull read, which used to
        // dominate hauling hot paths on large ships) can never drift.
        private void AddMat(MatType type, int amount) {
            rawMaterials ??= new Dictionary<MatType, int>();
            rawMaterials.TryGetValue(type, out var cur);
            rawMaterials[type] = cur + amount;
            usedCapacity += amount;
            VerifyUsedCapacity();
        }

        // Clamps to what is actually there, so it is safe to call with a
        // stale amount, and safe to call twice.
        private void RemoveMat(MatType type, int amount) {
            if (rawMaterials == null) { return; }
            if (!rawMaterials.TryGetValue(type, out var cur)) { return; }
            var remaining = cur - amount;
            if (remaining > 0) {
                rawMaterials[type] = remaining;
                usedCapacity -= amount;
            } else {
                rawMaterials.Remove(type);
                usedCapacity -= cur;
            }
            VerifyUsedCapacity();
        }

        private void ClearMats() {
            rawMaterials?.Clear();
            usedCapacity = 0;
        }

        private void RecalculateUsedCapacity() {
            usedCapacity = 0;
            if (rawMaterials == null) { return; }
            foreach (var m in rawMaterials) {
                usedCapacity += m.Value;
            }
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private void VerifyUsedCapacity() {
            if (rawMaterials == null) {
                D.Ass(usedCapacity == 0,
                    "MatStorage usedCapacity drifted (null contents): {0}",
                    this);
                return;
            }
            var actual = 0;
            foreach (var m in rawMaterials) {
                actual += m.Value;
            }
            D.Ass(actual == usedCapacity,
                "MatStorage usedCapacity drifted: cached {0}, actual {1}. {2}",
                usedCapacity, actual, Entity);
        }

        public bool HasReservation(Being who) {
            return reservations.HasAnyReservation(who);
        }

        public bool HasReservation(Being who, MatType type) {
            return reservations.HasReservation(who, type);
        }

        public int AvailableWithoutReservation(MatType type) {
            if (type == null) {
                D.Err("Trying to check if a null type is available!");
                return 0;
            }
            if (rawMaterials == null || rawMaterials.Count == 0) {
                return 0;
            }
            if (rawMaterials.TryGetValue(type, out var cnt)) {
                return cnt;
            } else {
                return 0;
            }
        }

        public int Available(MatType type) {
            if (type == null) {
                D.Err("Trying to check if a null type is available!");
                return 0;
            }
            if (rawMaterials == null || rawMaterials.Count == 0) {
                return 0;
            }
            if (rawMaterials.TryGetValue(type, out var cnt)) {
                if (cnt > 20) {
                    // ignore reservations
                    return cnt;
                }
            } else {
                return 0;
            }
            if (reservations.Mats.TryGetValue(type, out var res)) {
                return cnt - res;
            }
            return cnt;
        }

        public int AvailableForNanobots(MatType type) {
            if (type == null) {
                D.Err("Trying to check if a null type is available for nanobots!");
                return 0;
            }
            if (rawMaterials == null || rawMaterials.Count == 0) {
                return 0;
            }
            if (!rawMaterials.TryGetValue(type, out var cnt)) {
                return 0;
            }
            return Mathf.Max(0, cnt - ReservedAmt(type));
        }

        public int TakeForNanobots(MatType type, int wantedAmount, object ctx) {
            if (wantedAmount < 1) {
                D.Err("Nanobots want to take less than 1 of {0}: {1}! {2}",
                    type, wantedAmount, ctx);
                return 0;
            }
            var available = AvailableForNanobots(type);
            if (available < 1) {
                return 0;
            }
            if (wantedAmount > available) {
                wantedAmount = available;
            }
            return TakeWithoutReservation(type, wantedAmount);
        }

        private int TakeWithoutReservation(MatType type, int wantedAmount) {
            if (rawMaterials == null) { return 0; }
            if (wantedAmount < 1) {
                D.Err("Wants to take less than 1 of {0}: {1}!", type, wantedAmount);
                return 0;
            }
            if (type == null) {
                D.Err("Trying to take a null type from storage!");
                return 0;
            }
            // Without reservations
            if (!rawMaterials.TryGetValue(type, out var available)) {
                return 0;
            }
            if (wantedAmount > available) {
                // D.Warn("Trying to get more than available! {0}: A: {1}: W: {2}",
                //    type, available, wantedAmount);
                wantedAmount = available;
            }

            RemoveMat(type, wantedAmount);

            UpdateUIBlock(true);

            return wantedAmount;
        }

        public int Take(MatType type, Being who, int wantedAmount, object ctx) {
            if (wantedAmount < 1) {
                D.Err("Wants to take less than 1 of {0}: {1}! {2}/{3}",
                    type, wantedAmount, who, ctx);
                return 0;
            }
            if (who == null) {
                return TakeWithoutReservation(type, wantedAmount);
            }

            if (unreachableEvt.IsValid) {
                Notification.Remove(ref unreachableEvt);
                unreachableByFlying.Clear();
                if (!IsFull) {
                    HideInfoIcon();
                }
            }

            var matRes = reservations.GetReservation(who, type);
            if (matRes == null) {
                D.ErrSoft("Trying to take stuff without reservation: {0}, {1} {2} (type: {3})",
                    who.Id, Entity, ctx, type);
                return 0;
            }

            if (wantedAmount != matRes.Amount) {
                D.Err("Trying to take incorrect amount, reserved {0}, taking {1}, {2} <- {3}",
                   matRes.Amount, wantedAmount, Entity, ctx);
                wantedAmount = matRes.Amount;
            }
            if (wantedAmount < 1) {
                // a negative amount here went into RemoveMat and added items
                D.Err("Wants to take {0}! {1}", wantedAmount, ctx);
                return 0;
            }
            if (rawMaterials == null) {
                D.Err("Trying to take something while rawMaterials is null! {0}",
                    ctx);
                return 0;
            }

            if (type == null) {
                D.Err("Trying to take a null type from storage!");
                return 0;
            }
            if (!rawMaterials.TryGetValue(type, out var mat)) {
                // Ejected? or sold
                return 0;
            }
            if (mat < wantedAmount) {
                RemoveReservation(who, matRes);
                UpdateUIBlock(true);
                D.ErrSoft("Storage does not have enough of material (sold?) {0} {1}. {2}",
                    type, ctx, Entity);
                return 0;
            }

            RemoveMat(type, wantedAmount);
            RemoveReservation(who, matRes);
            UpdateUIBlock(true);
            return wantedAmount;
        }

        private void AddReservation(Being worker, MatReservation matRes) {
            reservations.Add(matRes);
            worker.AIAgent.BB.SetReservation(matRes);
        }

        private void RemoveReservation(Being worker, MatReservation matRes) {
            if (matRes.Type == null) {
                D.Err("Removing null mat type reservation: {0}. {1}",
                    worker, matRes);
                return;
            }
            reservations.Remove(worker, matRes.Type);
            worker.AIAgent.BB.RemoveReservation(matRes);
        }

        /// <returns>amount that was NOT stored</returns>
        public int Store(MatType type, Being who, int amount, object ctx) {
            if (amount < 1) {
                D.Err("Wants to store < 1! {0} {1} {2} {3}", type,
                    amount, who, ctx);
                return 0;
            }
            if (type == null) {
                D.Err("Trying to store undefined material! {0}", ctx);
                return amount;
            }
            if (!entity.IsActive) {
                D.Err("Trying to store materials in an inactive storage! {0} | {1}",
                    entity, ctx);
                return amount;
            }
            var actualAmount = Mathf.Min(amount, AvailableCapacity);
            if (actualAmount > 0) {
                AddMat(type, actualAmount);
            }

            UpdateUIBlock(true);

            return amount - actualAmount;
        }

        public override string ToString() {
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.AppendFormat( " * MatStorage ({2}) [Priority: {0}. Res: {1}]:", Priority,
                reservations.Mats.Count, entity.Definition.Id);
            if (rawMaterials?.Count > 0) {
                sb.AppendFormat("\n  - {0}", string.Join("\n  - ", rawMaterials));
            }
            return sb.ToString();
        }

        public int Reserve(MatType type, Being who, int wantedAmount, object ctx) {
            if (type == null) {
                D.Err("Trying to reserve mat with null type! {0} {1}",
                    who, ctx);
                return 0;
            }
            if (!entity.IsActive) { return 0; }
            if (wantedAmount < 1) {
                D.Err("Wants to reserve < 1! {0} {1} {2} {3}", type,
                    wantedAmount, who, ctx);
                return 0;
            }

            var avail = Available(type);
            if (wantedAmount > avail) {
                wantedAmount = avail;
            }
            // Available goes to 0 or below once others reserved everything (above 20 items
            // reservations are not subtracted, so it can go negative): reserving that made
            // 0 or negative reservations, and Take then added the negative amount back
            if (wantedAmount < 1) {
                return 0;
            }

            // Check for existing reservation of the same type
            var existing = reservations.GetReservation(who, type);
            if (existing != null) {
                D.ErrSoft("Worker already has reservation for this mat type! {0} <- {1}. {2}",
                    Entity, who, ctx);
                // the per-type total follows, or Available drifts from the real reservations
                if (reservations.Mats.TryGetValue(type, out var total)) {
                    reservations.Mats[type] = total - existing.Amount + wantedAmount;
                }
                existing.Amount = wantedAmount;
                UpdateUIBlock(true);
                return wantedAmount;
            }

            var matRes = new MatReservation {
                Ctx = ctx,
                Being = who,
                Storage = this,
                Amount = wantedAmount,
                Type = type,
                Since = G.Ticks,
            };

            AddReservation(who, matRes);
            UpdateUIBlock(true);
            return wantedAmount;
        }

        public void DestroyContents() {
            ClearMats();
        }

        public List<Mat> TakeAll() {
            if (Arrays.IsEmpty(rawMaterials)) { return null; }
            var result = new List<Mat>();
            foreach (var mat in rawMaterials) {
                var v = mat.Value;
                result.Add(new Mat {
                    Type = mat.Key,
                    StackSize = v,
                    MaxStackSize = v,
                });
            }
            ClearMats();

            foreach (var res in reservations.AllReservations) {
                if (res.Ctx is AIGoal goal) {
                    AIGoal.Fail(goal, G.Ticks, T.AdRejCouldNotReserve);
                }
            }
            UpdateUIBlock(true);
            return result;
        }
        private bool isHidden;

        public void Hide() {
            RemoveFromInventory();
            isHidden = true;
        }

        public void Show() {
            isHidden = false;
            AddToInventory();
        }

        public bool IsAddedToInventory { get; set; }

        public bool HasSubmenuNow => true;
        public string SubmenuTitle => T.Storage;

        public bool CanShowDetailsWhenUnconstructed => false;

        public bool CanShowDetailsWhenUnreachable => true;

        public void AddToInventory() {
            if (!IsAddedToInventory && IsPublic && !isHidden) {
                S.Sys.Inventory.Add(this);
            }
        }

        public void RemoveFromInventory() {
            if (IsPublic && IsAddedToInventory) {
                S.Sys.Inventory.Remove(this);
            }
        }

        private void UpdateUIBlock(bool wasUpdated) {
            UpdateProgressBar();
            if (wasUpdated) {
                OnChange.Invoke();
            }
        }

        // private bool isShowingFullIcon;
        private InfoBar bar;

        private void UpdateProgressBar() {
            // D.Warn("Updating progress bar: {0}/{1}. Used: {2}/{3}",
            //     showFulnessBar, IsConstructed, UsedCapacity, MaxCapacity);
            if (!showFulnessBar) { return; }
            if (!IsConstructed) { return; }
            bar = ShowInfoBar(Consts.ColorFuelBar, (float)UsedCapacity / MaxCapacity,
                UsedCapacity, MaxCapacity, "storage.capacity.bar");
            bar.CanHaveCustomTooltip = true;
        }

        public UnstoredMatComp Eject(MatType m, bool ignoreReservations = false,
                int amount = 0) {
            if (m == null) { return null; }
            var availableAmt = rawMaterials.Get(m, default);
            var pos = Tile != null ?
                Tile.Transform.AnyPortOutFirst : Entity.PosIdx;
            int amt;
            if (ignoreReservations) {
                amt = amount > 0 ? Math.Min(amount, availableAmt) : availableAmt;
            } else {
                amt = amount > 0 ? Math.Min(amount, Available(m)) : Available(m);
            }

            if (amt > 0) {
                var took = TakeWithoutReservation(m, amt);
                if (took >= availableAmt) {
                    // TakeWithoutReservation already removes the key once
                    // its stack hits zero; this is a no-op safety net for
                    // any stale leftover entry. RemoveMat is idempotent.
                    RemoveMat(m, availableAmt);
                }
                return EntityUtils.SpawnRawMaterial(m, took, pos, 0.25f, true);
            }

            // material was probably taken by someone else
            //D.Err("Couldn't eject material: {0}", m);
            return null;
        }

        public override void OnRemove() {
            RemoveFromInventory();
            var reinstallable = Tile.Reinstallable;
            if (reinstallable != null) {
                if (reinstallable.IsMidRelocation || reinstallable.IsPending) {
                    return;
                }
            }
            DumpContents();
        }

        public void DumpContents(bool forbidHauling = false) {
            var materials = TakeAll();
            if (!Arrays.IsEmpty(materials)) {
                var pos = Tile.Transform.AnyPortOutFirst;
                foreach (var mat in materials) {
                    var ejected = EntityUtils.SpawnRawMaterial(mat, pos, 0.5f);
                    if (forbidHauling && ejected != null) {
                        ejected.IsForbidden = true;
                        ejected.UpdateForbiddenIcon(true);
                    }
                }
                UpdateUIBlock(true);
            }
        }

        // Type-dependent half of WillAccept, independent of current fill
        // level, so callers that already know the fill level (e.g. the
        // nanobot tick index, which precomputes AvailableCapacity once per
        // storage per tick) can skip re-deriving it per candidate.
        public bool AcceptsType(MatType type) {
            if (storageType == StorageType.TakeOnly) { return false; }
            if (!AcceptsGroup(type.GroupH)) { return false; }
            return haulTarget == null || haulTarget.Accepts(type);
        }

        public bool WillAccept(MatType type, int amount, out bool wouldAcceptSome) {
            if (IsFull || !AcceptsType(type)) {
                wouldAcceptSome = false;
                return false;
            }
            wouldAcceptSome = true;
            return AvailableCapacity >= amount;
        }

        public bool CancelReservationFor(Being who, object ctx) {
            // Cancel all reservations for this being
            var list = reservations.GetAllReservations(who);
            if (list == null || list.Count == 0) {
                return false;
            }
            // Remove all reservations for this being
            for (int i = list.Count - 1; i >= 0; i--) {
                var res = list[i];
                who.AIAgent.BB.RemoveReservation(res);
            }
            reservations.RemoveAll(who);
            UpdateUIBlock(true);
            return true;
        }

        public bool CancelReservationFor(Being who, MatType type, object ctx) {
            var matRes = reservations.GetReservation(who, type);
            if (matRes == null) {
                return false;
            }
            RemoveReservation(who, matRes);
            UpdateUIBlock(true);
            return true;
        }

        [Obsolete("Hash the group")]
        public bool AvailableForGroup(int groupId) {
            if (rawMaterials == null || rawMaterials.Count == 0) { return false; }
            if (!AcceptsGroup(groupId)) { return false; }
            foreach (var item in rawMaterials) {
                var type = item.Key;
                if (type.GroupH == groupId) {
                    return Available(type) > 0;
                }
            }
            return false;
        }
        // If someone reserved everything of this group, we still return true
        public bool AvailableForGroup(int groupId, List<Mat> mats) {
            if (rawMaterials == null || rawMaterials.Count == 0) { return false; }
            var found = false;
            mats.Clear();
            if (!AcceptsGroup(groupId)) { return false; }
            foreach (var item in rawMaterials) {
                MatType type = item.Key;
                if (type.GroupH == groupId) {
                    int mat = item.Value;
                    if (mat < 20) {
                        // check reservations
                        if (reservations.Mats.TryGetValue(type, out var res)) {
                            mat -= res;
                        }
                    }
                    if (mat > 0) {
                        found = true;
                        mats.Add(new Mat {
                            Type = type,
                            StackSize = mat,
                            MaxStackSize = mat
                        });
                    }
                }
            }
            return found;
        }
        public void OnExplode() {
            // Remove all materials so they wouldn't get dumped
            TakeAll();
        }

        public int ReservedAmt(MatType type) {
            if (reservations.Mats.TryGetValue(type, out var amt)) {
                return amt;
            }
            return 0;
        }

        public void AppendMatCounts(Dictionary<MatType, int> counts) {
            if (rawMaterials == null || rawMaterials.Count == 0) { return; }
            foreach (var mat in rawMaterials) {
                var matCount = mat.Value;
                if (matCount == 0) { continue; }
                var matType = mat.Key;
                InventorySys.AppendMatCount(counts, matType, matCount);
            }
        }

        public string ShowCustomTooltip(string currentTooltip, InfoItem item) {
            if (item.Type != InfoItemType.Bar) {
                return currentTooltip;
            }
            var sb = Caches.StringBuilder;
            sb.Clear();
            if (bar != null) {
                sb.Append(bar.Info);
            }
            if (IsFull) {
                sb.Append(" (");
                sb.Append(Texts.Red(T.InfoStorageFull));
                sb.Append(')');
            }
            if (rawMaterials == null || rawMaterials.Count == 0) {
                return sb.ToString();
            }
            const int max = 10;
            var ordered = rawMaterials.OrderByDescending(m => m.Value)
                .ThenBy(m => m.Key.NameT).Take(max);
            foreach (var mat in ordered) {
                sb.AppendFormat("\n{0}: x{1}", mat.Key.Def.CodexLink, mat.Value);
            }
            if (rawMaterials.Count > max) {
                sb.AppendLine();
                sb.Append(Texts.Grey($"+{rawMaterials.Count - max}"));
            }
            return sb.ToString();
        }
        public bool IsMarkedUnreachable => unreachableEvt.IsValid;
        public void MarkAsUnreachableByFlying(Being worker) {
            (unreachableByFlying ??= new()).Add(worker);
            if (unreachableByFlying.Count > 1) {
                if (!unreachableEvt.IsValid) {
                    var tip = $"{Texts.Red(T.AdRejUnreachableMatFlying)} ({worker.Data.Mind.LinkToShortName})";
                    unreachableEvt = Notification.BuildFor(Entity, null,
                            T.AdjUnreachable, 0, NotificationType.Warning)
                        .WithTooltip(tip)
                        .Group("unr_str")
                        .Show();
                    if (!IsFull) {
                        ShowInfoIcon(IconId.CWarning, tip);
                    }
                }
            }
        }
    }
}
