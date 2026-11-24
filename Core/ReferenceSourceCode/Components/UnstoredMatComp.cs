#if UNITY_EDITOR
// #define DEBUG_RESERVATIONS
// #define DEBUG_UNSTORED_MAT
#endif
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.Systems.AI;
using Game.Systems.Hints;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Systems.Inventory;
using System.Text;
using Game.Systems.Firex;
using Game.Systems.Notify;
using Game.Systems.UIUX;

namespace Game.Components {
    // Pile
    public sealed class UnstoredMatComp : BaseComponent<UnstoredMatComp>,
            IMatStorage, IAIGoalProvider, IDefDescriptionProvider,
            IExplosivenessProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new UnstoredMatComp());
        }

        public StorageType StorageType => StorageType.TakeOnly;
        public const int AbsoluteMaxStackSize = 1000;
        public int Priority => 0;
        public bool IsPublic => true;
        public Mat Mat;
        public MatType Type;
        public MatHaulTargetComp HaulTarget => null;
        public int MaxStackSize = 1;
        private int stackSize = 1;
        public int StackSize {
            get => stackSize;
            set {
                if (IsAddedToInventory && value != stackSize) {
                    S.Sys.Inventory.OnPileStackChanged(this, value - stackSize);
                }
                stackSize = value;
                stackSizeB?.Set(value);
            }
        }
        // Position in its type's list in InventorySys (PileIndex), -1 when not in it
        public int IndexInType = -1;
        private BInt stackSizeB;
        public BInt StackSizeB => stackSizeB ??= new BInt(StackSize);
        public bool IsFull => StackSize == MaxStackSize;
        public bool IsEmpty => StackSize < 1;
        public int Reserved = 0;
        public bool TreatAsObj { get; private set; }
        public bool TryTreatAsObj(bool yes) {
            if (yes && StackSize != 1) {
                D.Err("Trying to treat a pile with non-1 stack size as obj: {0}", Obj);
                TreatAsObj = false;
                return false;
            }
            TreatAsObj = yes;
            if (!yes && Obj.StorableObj?.HasGoal == true) {
                // cancel storable obj storing goal, we are now treating this as a mat
                Obj.ClearGoal(T.NounDirectOrder);
            } else if (yes && HasGoal) {
                // cancel hauling mat, we now treat this as obj
                Obj.ClearGoal(T.NounDirectOrder);
            }
            if (!yes && Obj.StorableObj?.IsForbidden == true)  {
                Obj.StorableObj.IsForbidden = false;
                Obj.StorableObj.UpdateForbiddenIcon();
            }
            UpdateForbiddenIcon(false);
            return true;
        }
        public bool IsFullyReserved => Reserved >= StackSize;
        private bool isForbidden;
        // Player workers leave a forbidden pile alone: they don't store it, build or craft
        // with it, or eat it. See Docs/Architecture/Systems/Forbidding.md
        public bool IsForbidden {
            get => isForbidden;
            set {
                if (isForbidden != value) {
                    isForbidden = value;
                    if (IsAddedToInventory) {
                        S.Sys.Inventory.OnPileForbidChanged(this);
                    }
                    UpdateForbiddenIcon(false);
                }
            }
        }
        // Kept so mods built before the rename still load
        [System.Obsolete("Use IsForbidden")]
        public bool IsHaulingForbidden {
            get => IsForbidden;
            set => IsForbidden = value;
        }
        // Saved before forbid was generic, when the flag only kept a pile out of storage.
        // Resolved by position in OnLateReady
        private bool hasLegacyNoHaul;
        public int UnreservedCount => StackSize - Reserved;
        public int ReservationCount => reservations.Count;
        public string DumpReservations => string.Join(", ", reservations.Values);
        public float FillPercent => StackSize / MaxStackSize;
        // Need to keep it for Old AI
        public bool NeedsFurtherHauling;
        private readonly Dictionary<Being, MatReservation> reservations = new();
        public int InPort => entity.PosIdx;
        public int OutPort => entity.PosIdx;
        private Dictionary<MatType, int> contentsCache;
        public Dictionary<MatType, int> Contents {
            get {
                if (contentsCache == null) {
                    contentsCache = new Dictionary<MatType, int> {
                        { Type, StackSize }
                    };
                }
                return contentsCache;
            }
        }
        public int WorkSpot => entity.PosIdx;
        private bool isFixedSize;
        private int groupId;
        public int[] Groups { get; private set; }
        public bool AcceptsGroup(int id) {
            return groupId == id;
        }

        protected override void OnConfig() {
            StackSize = 1;
            MaxStackSize = 0;
            isFixedSize = Config.GetBool(PropertyIdH.FixedSize, false);
            var matType = Config.GetString(PropertyIdH.Type);
            Type = MatType.Get(matType);
            if (Type == null) {
                ModRegistry.WarnAboutBrokenMod(Entity.Definition,
                    $"Invalid material type: {matType}");
            }
            Mat.Type = Type;
            groupId = Mat.Type.GroupH;
            if (groupId != 0) {
                Groups = new int[1] { groupId };
            }
        }

        public void Absorb(UnstoredMatComp other) {
            StackSize += other.StackSize;
            MaxStackSize = StackSize;
            other.Exhaust();
            UpdateMat();
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            if (StackSize < 1) {
                D.Err("Saving UnstoredMat with StackSize of {0}", StackSize);
            }
            data.SetInt("StackSize", StackSize);
            data.SetBool("IsForbidden", IsForbidden);
        }

        protected override void OnLoad(ComponentData data) {
            StackSize = data.GetInt("StackSize", 1);
            MaxStackSize = StackSize;
            if (data.TryGetBool("IsForbidden", out var forbidden)) {
                isForbidden = forbidden;
            } else {
                hasLegacyNoHaul = data.GetBool("IsNoHaul", false);
            }
            if (StackSize < 1) {
                D.Err("Stack size is zero! {0}", Entity);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            if (hasLegacyNoHaul) {
                hasLegacyNoHaul = false;
                // Everything stays forbidden (loot, moved or ejected piles show the sign and
                // one click allows them) except food in the ship: a forbidden stasis ration
                // would starve a running game with nothing pointing at it
                isForbidden = !EntityUtils.IsWithinMargin(entity.PosIdx, false, false)
                    || !entity.TryGetComponent<EdibleComp>(out _);
            }
            if (MaxStackSize < StackSize) {
                MaxStackSize = StackSize;
            }
            if (StackSize > 0) {
                AddToInventory();
            }
            Entity.AfterInitialize += () => {
                if (StackSize > 0) {
                    UpdateMat();
                } else {
                    Exhaust();
                }
            };
            if (isForbidden && wasLoaded) {
                UpdateForbiddenIcon(false);
            }
        }

        public int AvailableWithoutReservation(MatType type) {
            if (type != Type) { return 0; }
            return StackSize;
        }

        // Whether Take without a worker (selling, gifting) would succeed: the same rule
        // CanReserveOrTake applies when who is null - a pile in someone's hands is theirs
        public bool IsTakeableWithoutWorker =>
            entity.IsActive && !(entity.IsCarriedByAny && entity.Carrier != null);

        public int Available(MatType type) {
            if (type != Type) { return 0; }
            return StackSize - Reserved;
        }

        public bool HasReservation(Being who) {
            return reservations.ContainsKey(who);
        }

        public bool HasReservation(Being who, MatType type) {
            // Piles only have one type, so check if it matches
            if (type != Type) { return false; }
            return reservations.ContainsKey(who);
        }

        private void AddReservation(Being worker, MatReservation matRes) {
            reservations.Add(worker, matRes);
            worker.AIAgent.BB.SetReservation(matRes);
            Reserved += matRes.Amount;
            ValidateReservations(matRes.Ctx);
        }

        private bool wasShowingForbidden;

        public void UpdateForbiddenIcon(bool force) {
            if (IsForbidden && !TreatAsObj) {
                if (!wasShowingForbidden || force) {
                    wasShowingForbidden = true;
                    if (entity.TryGetComponent<ExtraInfoComp>(out var info)) {
                        info.ShowInfoIcon(IconId.CForbidSmall, T.ItemForbiddenTip);
                    } else {
                        D.Err("ExtraInfo component is missing for Def: {0}. Cannot show hauling forbidden icon for pile {1}!",
                            entity.DefinitionId, this);
                    }
                }
            } else {
                if (wasShowingForbidden || force) {
                    wasShowingForbidden = false;
                    if (entity.TryGetComponent<ExtraInfoComp>(out var info)) {
                        info.HideInfoIcon();
                    }
                }
            }
        }

        private void ValidateAvailabilityOnUI() {
            if (!Entity.IsActive) { return; }
            if (StackSize < 1) { return; }
            if (Pos.IsOOB(Entity.PosIdx)) {
                D.ErrSoft("Pile is out of bounds: {0} {1}!", Obj, this);
                Exhaust();
                return;
            }
            if (Entity.Carrier != null) {
                if (!Entity.Carrier.IsActive || Entity.Carrier.IsDead) {
                    D.ErrSoft("Unstored pile is carried by a being that is dead or removed (fixing): {0} -> {1}",
                        Entity, Entity.Carrier);
                    Obj.Graphics.SpriteSet?.Detach();
                    Entity.SetCarrier(null);
                    if (!IsAddedToInventory) {
                        AddToInventory();
                    }
                }
            }
            if (!IsAddedToInventory
                    && ReservationCount == 0
                    && !Entity.IsCarriedByAny
                    && !AIGoal.IsActive(Entity.Goal)) {
                D.ErrSoft("Pile not in inventory while not reserved, not carried, and no associated task! (fixing) {0} {1}",
                    Obj, this);
                AddToInventory();
            }
        }

        private void ValidateReservations(object ctx) {
            var isValid = true;
            if (Reserved > StackSize) {
                D.Err("Reserved is > than StackSize! {0} {1}", Entity, ctx);
                isValid = false;
            }
            if (Reserved < 0) {
                D.Err("Reserved went below zero! {0} {1} {2}", Reserved,
                    D.Dump(reservations), ctx);
                isValid = false;
            }

            if (Reserved == 0 && reservations.Count > 0) {
                D.Err("Reserved is 0 but there are other reservations left! {0} {1}",
                    D.Dump(reservations), ctx);
                isValid = false;
            }
            if (!isValid) {
                Reserved = 0;
                foreach (var r in reservations) {
                    Reserved += r.Value.Amount;
                }
                if (Reserved > StackSize) {
                    D.Err("Panic! Actual reservations are > StackSize. {0}", ctx);
                }
            }
        }

        private void RemoveReservation(Being worker, MatReservation matRes) {
            reservations.Remove(worker);
            worker.AIAgent.BB.RemoveReservation(matRes);
            Reserved -= matRes.Amount;
            ValidateReservations(matRes.Ctx);
        }

        // Returns taken amount
        public int ReserveAndTake(MatType type, Being worker, int actAmt, object ctx) {
            if (actAmt < 1) {
                D.Err("Wants to take < 1! {0} {1} {2} {3}", type,
                    actAmt, worker, ctx);
                return 0;
            }
            var res = Reserve(type, worker, actAmt, ctx);
            if (res != actAmt) {
                D.Err("Worker failed to reserve the right amount from carried pile! WTF {0} | {2} | {3} | {4}",
                    worker.Id, this, Entity, DumpReservations, ctx);
                actAmt = res;
            }
            if (actAmt > 0) {
                return Take(type, worker, actAmt, ctx);
            }
            return 0;
        }

        // Returns taken amount
        public int Take(MatType type, Being who, int wantedAmount, object ctx) {
            if (wantedAmount < 1) {
                D.Err("Wants to take without reservation < 1! type: {0} wanted:{1} who:{2} obj:{3} ctx:{4}", type,
                    wantedAmount, who, Obj, ctx);
                return 0;
            }
            // destroyed?
            if (unreachableEvt.IsValid) {
                Notification.Remove(ref unreachableEvt);
                unreachableByFlying.Clear();
            }
            if (!CanReserveOrTake(type, who, wantedAmount, ctx)) {
                return 0;
            }
            var takenAmount = wantedAmount;
            if (takenAmount > StackSize) { takenAmount = StackSize; }

            // Find the reservation
            if (who != null) {
                if (!IsInTakeRange(who)) {
                    #if !PRODUCTION_BUILD
                    D.Err("Worker trying to take something from a pile that is too far away! {0} {1} {2}. Ship moving: {3}. Pile: {4}. Worker: {5}",
                        Pos.String(entity.PosIdx), Pos.String(who.PosIdx), ctx,
                        S.Sys.Ship.IsMoving, Obj, who);
                    #endif
                    return 0;
                }
                var matRes = reservations.Get(who, null);
                if (matRes != null) {
                    RemoveReservation(who, matRes);
                    // FIXME if you ever going to try to figure this out, this may help
                    if (matRes.Amount != takenAmount) {
                        D.Err("Reserved != taken. Taking {0} {1} (wanted {2}). Reserved was {3}. Pile {4} Ad {5}",
                            takenAmount, type, wantedAmount, matRes.Amount,
                            Entity, ctx);
                    }
                } else {
                    D.Err("Trying to take stuff without reservation: {0}, {1} {2}",
                        who.Id, Entity, ctx);
                    return 0;
                }
            } else {
                ValidateReservations(ctx);
            }

            StackSize -= takenAmount;
            if (StackSize < 1) {
                Exhaust();
            } else {
                UpdateMat();
            }
            return takenAmount;
        }

        public bool IsInTakeRange(Being who) {
            return Pos.SqrDistance(entity.PosIdx, who.PosIdx) <= 3;
        }

        // For removing the pile outright (Embark): a take without a worker skips what is
        // reserved and refuses a carried pile, so whoever holds it stops that work first. Their
        // actions' own cleanup drops the pile and gives the reservations back
        public void ReleaseHolders() {
            if (entity.Goal != null) {
                AIGoal.Cancel(entity.Goal, G.Ticks, T.AdRejTargetUnavailable);
            }
            DropWorkOf(entity.Carrier);
            if (reservations.Count > 0) {
                foreach (var worker in new List<Being>(reservations.Keys)) {
                    DropWorkOf(worker);
                }
            }
        }

        // A goal that refuses an interrupt is left alone: DropGoal would clear it from the
        // agent anyway and orphan it. A take without a worker still removes a reserved pile
        private static void DropWorkOf(Being worker) {
            var agent = worker?.AIAgent;
            if (agent?.Goal == null || agent.Goal.IsNonInterruptable) { return; }
            agent.DropGoal();
        }

        public void UpdateMat(bool updateGraphic = true) {
            var newSS = StackSize - Reserved;
            var changed = Mat.StackSize != newSS || Mat.MaxStackSize != MaxStackSize;
            Mat.StackSize = newSS;
            Mat.MaxStackSize = MaxStackSize;
            if (changed) {
                if (contentsCache != null) {
                    contentsCache[Type] = newSS;
                }
            }
            if (!updateGraphic || isFixedSize) { return; }
            if (StackSize > 0) {
                var s = Mathf.Lerp(0.75f, 1.25f, StackSize / 20f);
                Obj.Graphics.SetSize(new Vector2(s, s));
            }
        }

        private void Exhaust() {
            if (entity.IsActive) {
                S.Objs.Destroy(Obj, false);
            }
            UpdateMat();
            if (IsAddedToInventory) {
                RemoveFromInventory();
            }
        }

        public override void OnRemove() {
            // If someone is working on it, it could be that the pile was just exhausted
            if (entity.Goal != null && entity.Goal.Agent == null) {
                AIGoal.Cancel(entity.Goal, G.Ticks, T.AdRejTargetUnavailable);
            }
            // TODO find out: haulingAd was set to null if someone was workign on it. Why?
            // } else {
            //     haulingAd = null;
            // }
            if (IsAddedToInventory) {
                RemoveFromInventory();
            }
        }

        public int Store(MatType type, Being who, int amount, object ctx) {
            if (amount < 1) {
                D.Err("Wants to store < 1! {0} {1} {2} {3}", type,
                    amount, who, ctx);
                return 0;
            }
            if (type != Type) {
                D.Err("Trying to store incorrect mat type! {0} != {1} {2}",
                    type, Type, ctx);
                return amount;
            }
            if (TreatAsObj) {
                D.Err("Trying to add material into pile that is already treated as obj! {0} | {1} | {2} | {3}",
                    Obj, who, amount, ctx);
                return amount;
            }
            var room = AbsoluteMaxStackSize - StackSize;
            if (amount <= room) {
                StackSize += amount;
                MaxStackSize = StackSize;
                UpdateMat();
                return 0;
            } else {
                StackSize = AbsoluteMaxStackSize;
                UpdateMat();
                return amount - room;
            }
        }

        private bool CanReserveOrTake(MatType type, Being who,
                int wantedAmount, object ctx) {
            bool can = true;
            if (StackSize < 1) {
                D.ErrSoft("Trying to reserve or take when StackSize is 0! {0} -> {1}",
                    Entity, ctx);
                can = false;
            }
            if (!Entity.IsActive) {
                if (IsAddedToInventory) {
                    D.Err("Removed pile is in inventory! {0}", Entity);
                    RemoveFromInventory();
                //} else {
                    // Why would this be an error! just a race condition
                    //D.Err("Trying to reserve or take material for inactive entity! {0} {1}", Entity, ctx);
                }
                can = false;
            }
            if (wantedAmount < 1) {
                D.Err("Trying to reserve or take < 1! {0} {1}", wantedAmount, who);
                can = false;
            }
            if (type != Type) {
                D.Err("Trying to reserve wrong type! {0} != {1}, {2}",
                    type, Type, who?.AIAgent?.CurrentAction);
                can = false;
            }
            bool isCarried = entity.IsCarriedByAny;
            if (isCarried) {
                if (entity.Carrier != null && entity.Carrier != who) {
                    D.ErrSoft("Trying to reserve or take material for a pile that is carried by someone else! Pile: {0}/{1}, My Task: {2}, Carrier Task: {3}",
                        this, Entity, ctx, entity.Carrier.AIAgent.CurrentAction);
                    can = false;
                } else if (entity.NanobotCarrierHiveId != 0 && who != null) {
                    D.ErrSoft("Trying to reserve or take material for a pile carried by nanobots! Pile: {0}/{1}, My Task: {2}",
                        this, Entity, ctx);
                    can = false;
                }
                // same carrier
            } else { // not carried and not in inventory
                if (who != null && !IsAddedToInventory) {
                    if (Entity.IsActive) {
                        D.Err("Trying to reserve or take item that was not in inventory and not carried! {0}, {1}, {2}",
                            this, Entity, ctx);
                    }
                    can = false;
                }
            }
            return can;
        }

        public int Reserve(MatType type, Being who, int wantedAmount, object ctx) {
            if (!entity.IsActive) { return 0; }
            #if DEBUG_RESERVATIONS
            D.Log("Reserving pile {0}/{1} of {2} for worker {3}. {4}",
                wantedAmount, StackSize, Entity, who, ctx);
            #endif
            if (!CanReserveOrTake(type, who, wantedAmount, ctx)) {
                return 0;
            }
            if (reservations.TryGetValue(who, out var existingRes)) {
                D.Err("Worker already reserved {0}! Trying to reserver {1} again! Will try to fix",
                    this, who, ctx);
                if (existingRes.Amount != wantedAmount) {
                    // clamped and counted in Reserved like a new reservation: changing only
                    // the amount let Reserved drift from the reservations
                    var availableToWorker = StackSize - Reserved + existingRes.Amount;
                    if (wantedAmount > availableToWorker) { wantedAmount = availableToWorker; }
                    if (wantedAmount < 1) { return 0; }
                    Reserved += wantedAmount - existingRes.Amount;
                    existingRes.Amount = wantedAmount;
                }
                UpdateMat(false);
                return wantedAmount;
            }
            var available = StackSize - Reserved;
            if (wantedAmount > available) { wantedAmount = available; }
            // a fully reserved pile used to get a reservation of 0
            if (wantedAmount < 1) { return 0; }
            var matRes = new MatReservation {
                Ctx = ctx,
                Being = who,
                Storage = this,
                Amount = wantedAmount,
                Type = type,
                Since = G.Ticks,
            };
            AddReservation(who, matRes);
            UpdateMat(false);
            return wantedAmount;
        }

        public string ObjLinkWithCount() {
            if (StackSize > 1) {
                return Texts.LinkToObj(Obj,
                    Texts.Yellow($"{Obj.Definition.NameT} x{StackSize}"));
            } else {
                return Texts.LinkToObj(Obj);
            }
        }

        public override string ToString() {
            if (entity == null) {
                return "UnstoredMat[uninitialized]";
            }
            return string.Format(" * UnstoredMat[{4}]: {0} ({1}/{2}) (Res: {3}). Active: {6}. Id: {5}",
                Type, StackSize, MaxStackSize, Reserved, Pos.String(entity.PosIdx),
                    EntityId, entity.IsActive);
        }

        public bool IsAddedToInventory { get; set; }

        public void ReaddToInventoryIfNeeded() {
            if (StackSize > 0 && !IsAddedToInventory && entity.IsActive) {
                AddToInventory();
            }
        }

        public void AddToInventory() {
            if (!Entity.IsActive) {
                D.Err("Trying to add inactive raw material pile to inventory! {0}",
                    Entity);
                return;
            }
            if (StackSize < 1) {
                D.Err("Trying to add exhausted raw material pile to inventory! {0}",
                    Entity);
                return;
            }
            if (!IsAddedToInventory) {
                // D.Warn("adding to inventory: {0}", this);
                S.Sys.Inventory.Add(this);
            } else {
                D.Err("Trying to add to inventory twice! {0}", this);
            }
        }

        public void RemoveFromInventory() {
            if (IsAddedToInventory) {
                // D.Warn("Removing from inventory: {0}", this);
                S.Sys.Inventory.Remove(this);
            } else {
                D.Err("Trying to remove something from inventory when it was not added! {0}",
                    this);
            }
        }

        public bool TryOrderStore() {
            // obj mode
            if (TreatAsObj) {
                return false;
            }
            if (entity.Goal != null) { return false; }
            var goal = CreateStoringGoal(false, false);
            if (goal != null) {
                return entity.TrySetGoal(goal);
            }
            return false;
        }

        // A hauler that drops a pile it was storing gives the order back, so the pile is not
        // left waiting: storages do not auto-haul everything (a pile in space, AutoHaulReach).
        // Deferred, because a drop often happens while AISys walks its colony goals, when no
        // goal may be added; the action queue runs every frame, paused or not, so the order
        // is in place before the player can save.
        public void OrderStoreLaterIfLoose() {
            S.ActQ.Enqueue(orderStoreIfLoose ??= OrderStoreIfLoose);
        }

        private System.Action orderStoreIfLoose;

        public void OrderStoreIfLoose() {
            if (!entity.IsActive || StackSize < 1) { return; }
            if (IsForbidden || IsMarkedUnreachable || entity.IsCarriedByAny) { return; }
            if (HasAnyGoal) { return; }
            TryOrderStore();
        }

        public void OrderStore(bool byPlayer) {
            if (entity.Goal != null) { return; }

            if (!HasCompatibleStorage()) {
                if (byPlayer) {
                    S.Sig.ShowErrorMessage.Send(Obj.Definition.StorageUnavailableMessage(Obj));
                }
                return;
            }

            var goal = CreateStoringGoal(byPlayer, false);
            if (goal != null) {
                entity.TrySetGoal(goal);
            }
        }

        public bool HasCompatibleStorage() {
            return S.Sys.Inventory.HasCompatibleStorages(Type);
        }

        public bool HasSubmenuNow => true;
        public string SubmenuTitle => T.Stockpile;

        public bool IsExplosivenessProviderDisabled => StackSize == 0;

        public int ExplosionLevel => Type.ExplosionLevel;

        public Curve ExplosivenessCurve => Type.ExplosivenessCurve;

        /// <summary>
        /// This does not add the goal to AI system, use OrderStore if you want
        /// to order hauling.
        /// </summary>
        /// <seealso cref="OrderStore"/>
        /// <param name="forSplitPile">set to true when this is created for a pile that is too big to haul in one go</param>
        /// <returns>goal or null</returns>
        public AIGoal CreateStoringGoal(bool byPlayer, bool forSplitPile) {
            if (!forSplitPile && entity.Goal != null) { return null; }
            if (!entity.IsActive) {
                D.Err("Trying to create storing goal for inactive entity! {0}", entity);
                return null;
            }
            if (entity.IsCarriedByAny) {
                D.Err("Trying to create storing goal for a carried pile: {0}", entity);
                return null;
            }

            var haulingGoal = CreateGoal(JobTypeIdH.Hauling,
                    IconId.CStore, T.ActionStore, this)
                .WithWorldToDesired(AIVarsH.IsMatStored, false, true)
                // boosting haul tasks sucks
                .ByPlayer(false)
                .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                .NanobotCompatible()
                .WriteInitialState();
            haulingGoal.CheckDoable = CanStoreMat;

            return haulingGoal;
        }

        private AIGoalCheckResult CanStoreMat(GameState S, AIGoal goal) {
            if (HasCompatibleStorage()) {
                return AIGoalCheckResult.Doable;
            } else {
                S.Sig.FailedFindingStorageFor.Send(Entity);
                return AIGoalCheckResult.Fail(T.StorageUnavailable, true);
            }
        }

        public string DetailsTip() {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            if (!HasCompatibleStorage()) {
                sb.AppendLine(Texts.Red(T.MatStorageUnavailable));
            }
            if (Reserved > 0) {
                sb.AppendLine(T.MaterialReserved_V1.F(Reserved));
            }
            Obj.Data.Reservation?.AppendTooltip(sb);
            Obj.Definition.AddStorageInfo(sb, Obj);
            return sb.ToString();
        }

        public List<Mat> TakeAll() {
            // Use transfer instead
            throw new System.NotImplementedException();
        }

        public bool WillAccept(MatType type, int amount, out bool wouldAcceptSome) {
            wouldAcceptSome = false;
            return false;
        }

        private bool triedUnstucking;

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            triedUnstucking = false;
        }

        public bool CancelReservationFor(Being who, object ctx) {
            if (who == null) {
                D.Err("Cancelling reservation for null worker! {0}", this);
                return false;
            }
            // ctx?.WriteHistory(S, "WillUnreserveMat", who.Id);
            var matRes = reservations.Get(who, null);
            if (matRes == null) {
                D.Err("There was no reservation for being: {0}, {1}. {2}", who,
                    Entity, ctx);
                return false;
            }
            var reservedAmount = matRes.Amount;
            #if DEBUG_RESERVATIONS
            D.Log("Cancelling reservation {0}/{1} of {2} to {3}",
                reservedAmount, StackSize, Entity, who);
            #endif
            RemoveReservation(who, matRes);
            UpdateMat(false);
            return true;
        }

        public bool CancelReservationFor(Being who, MatType type, object ctx) {
            // Piles only have one type, so verify it matches
            if (type != Type) { return false; }
            return CancelReservationFor(who, ctx);
        }

        public bool AvailableForGroup(int id) {
            return groupId == id && Reserved < StackSize;
        }

        public bool AvailableForGroup(int id, List<Mat> mats) {
            if (groupId == id && StackSize > 0) {
                mats.Clear();
                mats.Add(new() { Type = Type, StackSize = StackSize, });
                return true;
            }
            return false;
        }

        public int ReservedAmt(MatType type) {
            #if !PRODUCTION_BUILD
            if (type != Type) {
                D.Err("Checking reservations for incorrect type: {0}. Pile is {1}",
                    type, Type);
                return 0;
            }
            #endif
            return Reserved;
        }

        public void AppendMatCounts(Dictionary<MatType, int> counts) {
            if (StackSize < 1) { return; }
            InventorySys.AppendMatCount(counts, Type, StackSize);
        }

        private HashSet<Being> unreachableByFlying;

        private NotificationHandle unreachableEvt;

        public bool IsMarkedUnreachable => unreachableEvt.IsValid;
        public void MarkAsUnreachableByFlying(Being worker) {
            (unreachableByFlying ??= new()).Add(worker);
            if (unreachableByFlying.Count > 1) {
                if (!unreachableEvt.IsValid) {
                    var tip = $"{Texts.Red(T.AdRejUnreachableMatFlying)} ({worker.Data.Mind.LinkToShortName})";
                    // Not forbidden: a grounded worker may still reach it. Auto-haul skips
                    // marked piles instead (InventorySys.IsHaulable)
                    unreachableEvt = Notification.BuildFor(Obj, null, T.UnreachableMat,
                        0, NotificationType.Warning)
                        .WithTooltip(tip)
                        .Group("unr_mat")
                        .Show();
                }
            }
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
            if (AIGoal.IsCancelled(goal)) {
                IsForbidden = true;
            }
            if (AIGoal.IsFailed(to)) {
                // IsFailed also covers Interrupted and CoolingDown, which stay alive
                if (to == AIGoalState.Failed && goal.NonRetryable) {
                    // AISys drops a non-retryable failed goal, but only Completed and
                    // Cancelled detach it from us - left attached, it would block every
                    // future storing order for this pile
                    entity.UnsetGoal(goal);
                }
                if (!triedUnstucking) {
                    triedUnstucking = true;
                    Obj.Transform.UnstuckIfNeeded();
                }
            } else if (AIGoal.IsCompleted(to)) {
                // It could an old ad of a big pile, let's just ignore
                if (NeedsFurtherHauling) {
                    NeedsFurtherHauling = false;
                    if (StackSize < 1 || !Entity.IsActive) { return; }
                    // We know this will be a new ad
                    var goal2 = CreateStoringGoal(false, true);
                    if (goal2 != null) {
                        goal2.WithPriority(goal.GoalPriority);
                        goal2.CheckDoable = CanStoreMat;
                        entity.TrySetGoal(goal2);
                    }
                } else {
                    HideInfoIcon();
                }
            }
            if (unreachableEvt.IsValid) {
                ShowInfoIcon(IconId.CWarning, T.UnreachableMat);
            }
        }

        public void OnGoalLoad(AIGoal goal) {
            goal.CheckDoable = CanStoreMat;
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def,
                bool addedAnything) {
            return addedAnything;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
            // var mat = MatType.FindByDef(def);
            // if (mat == null) { return; }
            // sb.Append(GlyphMap.BRHR);
            // sb.Append(T.MatEnergyOutput);
            // sb.Append(": ");
            // sb.Append(Texts.Accented(Units.EnergyOutput(mat.EnergyOutput, true), false));
            // if (!mat.IsComposite) {
            //     sb.Append("\n");
            //     sb.Append(T.MatPrimary);
            // }
        }

        public void OnExplode() {
        }
    }
}
