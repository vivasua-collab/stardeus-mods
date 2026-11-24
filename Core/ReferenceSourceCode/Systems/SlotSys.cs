// #define DEBUG_SLOTS
using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Reg;
using Game.Utils;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems {
    public sealed class SlotSys : GameSystem {
        public const string SysId = "SlotSys";
        public override string Id => SysId;
        private readonly HashSet<ISlots> allSlots = new();
        private readonly Dictionary<int, List<ISlots>> byDesignation = new();

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new SlotSys());
        }

        public bool HasFor(int designation) {
            if (byDesignation.TryGetValue(designation, out var slots)) {
                return slots.Count > 0;
            }
            return false;
        }
        public bool HasCompatibleFor(int designation, BeingData data, bool freeOnly = false) {
            if (byDesignation.TryGetValue(designation, out var slots)) {
                for (int i = 0; i < slots.Count; i++) {
                    ISlots slot = slots[i];
                    if (freeOnly && !(slot.HasFreeSlot && slot.IsAvailable)) {
                        continue;
                    }
                    if (slot.IsCompatibleWith(data)) {
                        return true;
                    }
                }
            }
            return false;
        }

        public ISlots FirstFor(int designation, out int count) {
            count = 0;
            if (byDesignation.TryGetValue(designation, out var slots)) {
                count = slots.Count;
                if (count > 0) {
                    return slots[0];
                }
            }
            return null;
        }

        public bool HasFreeFor(int designation) {
            if (byDesignation.TryGetValue(designation, out var slots)) {
                for (int i = slots.Count - 1; i >= 0; i--) {
                    ISlots slot = slots[i];
                    if (slot.HasFreeSlot && slot.IsAvailable) {
                        return true;
                    }
                }
            }
            return false;
        }

        // Per-being version of HasFreeFor: true only if the slot search would actually hand
        // this being a slot (reachable island, free, compatible, safe)
        public bool HasUsableFor(int designation, BeingData b) {
            if (b?.Being == null) { return false; }
            if (!byDesignation.TryGetValue(designation, out var slots) || slots.Count == 0) {
                return false;
            }
            bool isGrounded = b.Being.Motion.IsGrounded;
            var workerIsland = areas.IslandAt(b.Being.PosIdx);
            if (workerIsland == 0 && isGrounded) {
                return false;
            }
            var safety = b.Safety;
            // the slot searches of the actions that ask this all go by BeingReach
            var reach = S.Sys.Path.Reg.Reach.For(b.Being.Motion);
            for (int i = 0; i < slots.Count; i++) {
                if (IsUsableBy<ISlots>(slots[i], b, safety, b, reach,
                        null, null, out _)) {
                    return true;
                }
            }
            return false;
        }

        protected override void OnInitialize() {
            S.Sig.SlotsChanged.AddListener(SlotsChanged);
            areas = S.Sys.Areas;
        }

        private readonly HashSet<Slot> processedSlots = new();
        private readonly Dictionary<int, Slot> availableWithoutSeat = new();
        private readonly Dictionary<int, Slot> availableWithSeat = new();
        // A position does not identify a slot: slots on a port or work spot sit outside
        // the device, so neighbouring devices can share one (two AutoDocs, STA-1592)
        public Slot FindSlot(int designation, Func<Slot, bool> match) {
            if (!byDesignation.TryGetValue(designation, out var slots)) {
                return null;
            }
            foreach (var sl in slots) {
                for (int i = 0; i < sl.Slots.Count; i++) {
                    Slot s = sl.Slots[i];
                    if (match(s)) {
                        return s;
                    }
                }
            }
            return null;
        }

        public Slot GetCompatible(int designation, int slotPosIdx, BeingData target) {
            if (!byDesignation.TryGetValue(designation, out var slots)) {
                return null;
            }
            foreach (var sl in slots) {
                if (!sl.IsAvailable) { continue; }
                if (!sl.IsCompatibleWith(target)) { continue; }
                for (int i = 0; i < sl.Slots.Count; i++) {
                    Slot s = sl.Slots[i];
                    if (s.SlotPosIdx == slotPosIdx) {
                        return s;
                    }
                }
            }
            return null;
        }

        // Warning: don't use it with beds, because posIdx will be the same
        // for humans and pets
        public Slot Get(int designation, int slotPosIdx) {
            if (!byDesignation.TryGetValue(designation, out var slots)) {
                return null;
            }
            foreach (var sl in slots) {
                for (int i = 0; i < sl.Slots.Count; i++) {
                    Slot s = sl.Slots[i];
                    if (s.SlotPosIdx == slotPosIdx) {
                        return s;
                    }
                }
            }
            return null;
        }

        // FIXME - try to merge makeshift and non-makeshift into one?
        public Slot FindForFood(Being being, HashSet<int> blacklist,
                out int sidePosIdx, Slot tryNearSlot = null) {
            var isProper = true;
            var slot = FindForFood(being, blacklist, isProper, out sidePosIdx, tryNearSlot);
            if (slot == null) {
                isProper = false;
                slot = FindForFood(being, blacklist, isProper, out sidePosIdx, null);
            }
            return slot;
        }
        public Slot FindForDesignationOnBehalfOf<T>(BeingData carrier, BeingData slotUser,
                int designation, HashSet<int> blacklist, Func<T, bool> check, out T obj) where T : ISlots {
            if (!byDesignation.TryGetValue(designation, out var dSlots)) {
                obj = default;
                return null;
            }
            return FindClosestMatching(carrier, slotUser, dSlots, blacklist, check, out obj,
                null, IslandsOf(carrier));
        }
        public Slot FindForDesignation<T>(BeingData b, int designation,
                HashSet<int> blacklist, Func<T, bool> check, out T obj,
                Slot tryNearSlot = null) where T : ISlots {
            if (!byDesignation.TryGetValue(designation, out var dSlots)) {
                obj = default;
                return null;
            }
            return FindClosestMatching(b, b, dSlots, blacklist, check, out obj, tryNearSlot,
                IslandsOf(b));
        }
        // For a being that picks a slot for itself: only slots it can walk to by a route it
        // may take (reach from BeingReach.For). The overloads without a reach judge by floor
        // islands, which colony work (carrying a body, surgery) still goes by
        public Slot FindForDesignation<T>(BeingData b, int designation, ReachQuery reach,
                HashSet<int> blacklist, Func<T, bool> check, out T obj,
                Slot tryNearSlot = null) where T : ISlots {
            if (!byDesignation.TryGetValue(designation, out var dSlots)) {
                obj = default;
                return null;
            }
            return FindClosestMatching(b, b, dSlots, blacklist, check, out obj, tryNearSlot,
                reach);
        }

        public Slot FindForFood(Being being, HashSet<int> blacklist,
                bool isProper, out int sidePosIdx, Slot tryNearSlot) {
            if (!byDesignation.TryGetValue(SlotDesignationH.Surface, out var slots)) {
                sidePosIdx = Pos.Invalid;
                return null;
            }
            // 1. find all reachable non makeshift slots that can be used
            var count = 0;
            var isMakeshift = !isProper;
            var reach = S.Sys.Path.Reg.Reach.For(being.Motion);
            foreach (var sl in slots) {
                if (sl.IsMakeshift != isMakeshift) { continue;  }
                if (!sl.IsAvailable) { continue; }
                if (blacklist?.Contains(sl.EntityId) == true) { continue; }
                // FIXME if table is next to a wall, we may be fucked
                if (!reach.CanReach(sl.Entity)) { continue; }
                if (sl.RequiresSafeArea
                        && !being.Data.Safety.IsActuallySafeAtWithoutEquipment(
                            sl.Entity.PosIdx)) {
                    continue;
                }
                for (int i = 0; i < sl.Slots.Count; i++) {
                    Slot s = sl.Slots[i];

                    // FIXME this check prevents from people using tables when
                    // there is junk on them
                    //var isObstructed = S.Objs.FindFirstInPos(s.SlotPosIdx) == null;

                    if (s.HasFreeSides) {
                        Caches.Slots[count++] = s;
                        if (count == Caches.Slots.Length) { break; }
                    }
                }
            }

            sidePosIdx = -1;
            if (count == 0) { return null; }
            // Now we have filled Caches.Slots with count of potential candidates
            // Let's sort them by distance

            processedSlots.Clear();
            availableWithoutSeat.Clear();
            availableWithSeat.Clear();
            // TODO read this from some trait (i.e. savage)
            const bool wantsSittingSpot = true;
            var closeEnoughSqr = Units.CloseEnoughSqr;
            var closeEnoughChance = Units.CloseEnoughChance;
            var hadTryNearSlot = tryNearSlot != null;

            // Let's try 5 times
            while (processedSlots.Count < Mathf.Min(count, 5)) {
                var closest = Caches.Slots[0];
                float closestDist = float.MaxValue;
                if (tryNearSlot != null) {
                    closest = tryNearSlot;
                    processedSlots.Add(tryNearSlot);
                    // D.Warn("Trying near slot: {0}", tryNearSlot.Parent.Entity);
                    tryNearSlot = null;
                    // TODO add dist check if it's way too far
                } else {
                    for (int i = 0; i < count; i++) {
                        var slot = Caches.Slots[i];
                        var dist = Vectors.SqrDistance(being.Position,
                            slot.ContainedPosition);
                        if (dist < closestDist) {
                            if (processedSlots.Contains(slot)) {
                                continue;
                            }
                            closest = slot;
                            closestDist = dist;
                            if (dist < closeEnoughSqr && S.Rng.Chance(closeEnoughChance)) {
                                break;
                            }
                        }
                    }
                }

                var nav = S.Sys.Path;
                // Now we have a candidate slot, let's pick a side
                foreach (var side in closest.Sides) {
                    if (closest.HasSideReservationFor(side)) {
                        continue;
                    }
                    var pathNode = nav.Nodes[side];
                    if (pathNode.IsWalkable && pathNode.IsPassable) {
                        if (wantsSittingSpot) {
                            var sideObj = EntityUtils.ObjAt(side);
                            if (sideObj != null) {
                                if (sideObj.TryGetComponent<ISlots>(out var slts)) {
                                    if (slts.SlotDesignation == SlotDesignationH.Chair
                                            && slts.IsAvailable) {
                                        // Collect all chair-adjacent sides for random selection
                                        availableWithSeat[side] = closest;
                                    }
                                } else {
                                    availableWithoutSeat[side] = closest;
                                    continue;
                                }
                            } else {
                                availableWithoutSeat[side] = closest;
                                continue;
                            }
                            /*
                            if WantsSittingSpot gets implemented, this will can be uncommented
                        } else {
                            // now we will reserve a side and return
                            sidePosIdx = side;
                            return closest;
                            */
                        }
                    }
                }

                // We burned through the sides of the closest unprocessed slot
                // and there is no walkable space
                processedSlots.Add(closest);
            }
            processedSlots.Clear();

            if (availableWithSeat.Count > 0) {
                int idx; Slot slot;
                if (hadTryNearSlot) {
                    var first = availableWithSeat.First();
                    idx = first.Key; slot = first.Value;
                } else {
                    (idx, slot) = S.Rng.From(availableWithSeat);
                }
                sidePosIdx = idx;
                availableWithSeat.Clear();
                availableWithoutSeat.Clear();
                return slot;
            }

            // We could not find a slot with seat in 5 (or {count}) attempts
            if (availableWithoutSeat.Count > 0) {
                // standing seats available!
                int idx; Slot slot;
                if (hadTryNearSlot) {
                    var first = availableWithoutSeat.First();
                    idx = first.Key; slot = first.Value;
                } else {
                    (idx, slot) = S.Rng.From(availableWithoutSeat);
                }
                sidePosIdx = idx;
                availableWithoutSeat.Clear();
                return slot;
            }

            return null;
        }

        public Slot FindClosest<T>(int designation, BeingData worker,
                HashSet<int> blacklist, out T obj) where T : ISlots {
            if (!byDesignation.TryGetValue(designation, out var slots)) {
                obj = default;
                return null;
            }
            return FindClosestMatching(worker, worker, slots, blacklist, null, out obj,
                null, IslandsOf(worker));
        }
        public Slot FindClosestMatching<T>(int designation, BeingData worker,
                HashSet<int> blacklist, Func<T, bool> check,
                out T obj, Slot tryNearSlot = null) where T : ISlots {
            if (!byDesignation.TryGetValue(designation, out var slots)) {
                obj = default;
                return null;
            }
            return FindClosestMatching(worker, worker, slots, blacklist, check,
                out obj, tryNearSlot, IslandsOf(worker));
        }
        // For a being that picks a slot for itself (see FindForDesignation with a reach)
        public Slot FindClosestMatching<T>(int designation, BeingData worker, ReachQuery reach,
                HashSet<int> blacklist, Func<T, bool> check, out T obj) where T : ISlots {
            if (!byDesignation.TryGetValue(designation, out var slots)) {
                obj = default;
                return null;
            }
            return FindClosestMatching(worker, worker, slots, blacklist, check,
                out obj, null, reach);
        }

        private AreaSys areas;

        // Public versions should use by designation
        private Slot FindClosestMatching<T>(BeingData carrier, BeingData slotUser,
                ICollection<ISlots> slots, HashSet<int> blacklist, Func<T, bool> check,
                out T obj, Slot tryNearSlot, ReachQuery reach) where T : ISlots {

            if (carrier == null || slotUser == null) {
                D.Err("Trying to find closest matching slot for null carrier or user! Carrier: {0}, User: {1}",
                    carrier, slotUser);
                obj = default;
                return null;
            }
            bool isGrounded = carrier.Being.Motion.IsGrounded;
            var workerIsland = S.Sys.Areas.IslandAt(carrier.Being.PosIdx);
            if (workerIsland == 0 && isGrounded) {
                D.ErrSoft("Grounded Being is without an island! {0} Island count: {1}", carrier,
                    S.Sys.Areas.IslandCount, carrier.Being.AIAgent.CurrentAction);
                obj = default;
                return null;
            }
            Slot closest = null;
            float closestDist = float.MaxValue;
            bool closestIsPrivate = false;
            obj = default;
            var closeEnoughSqr = Units.CloseEnoughSqr;
            var closeEnoughChance = Units.CloseEnoughChance;
            var carrierSafety = carrier.Safety;
            var wantedPos = slotUser.Being.Position;

            if (tryNearSlot != null) {
                wantedPos = tryNearSlot.Parent.Entity.Position;
                // try as close as possible
                closeEnoughSqr = 0f;
            }

            foreach (var slot in slots) {
                if (IsUsableBy(slot, carrier, carrierSafety, slotUser, reach,
                        blacklist, check, out T sl)) {
                    var s = slot.Slots[0];
                    var dist = Vectors.SqrDistance(wantedPos, sl.Entity.Position);
                    var isPrivate = IsInOthersQuarters(sl, slotUser);
                    // anything else that is usable beats a device in someone's quarters
                    var isBetter = closest == null
                        || (closestIsPrivate && !isPrivate)
                        || (closestIsPrivate == isPrivate && dist < closestDist);
                    if (isBetter) {
                        obj = sl;
                        closest = s;
                        closestDist = dist;
                        closestIsPrivate = isPrivate;
                        if (!isPrivate && dist < closeEnoughSqr
                                && Rng.UChance(closeEnoughChance)) {
                            return closest;
                        }
                    }
                }
            }
            return closest;
        }

        // The one definition of "this being can use this slot provider right now", shared
        // by the slot search and by planner costs that must not promise a slot the search
        // would then refuse (STA-1370: a Tube Feeder nobody could reach starved the colony)
        private bool IsUsableBy<T>(ISlots slot, BeingData carrier, BeingSafety carrierSafety,
                BeingData slotUser, ReachQuery reach,
                HashSet<int> blacklist, Func<T, bool> check, out T sl) where T : ISlots {
            sl = default;
            var slotPos = slot.Entity.PosIdx;
            if (blacklist?.Contains(slot.EntityId) == true) { return false; }
            if (!reach.CanReach(slot.Entity)) { return false; }
            if (slot is not T typed) { return false; }
            if (!typed.IsAvailable || !typed.HasFreeSlot) { return false; }
            if (!typed.IsCompatibleWith(slotUser)) { return false; }
            if (!typed.Entity.IsActive) {
                D.Err("Inactive entity still in slots! {0}", typed.Entity);
                return false;
            }
            if (check != null && !check(typed)) { return false; }
            if (typed.RequiresSafeArea && !carrierSafety.IsActuallySafeAt(slotPos)) {
                if (carrier != slotUser
                        && !slotUser.Safety.IsActuallySafeAt(slotPos)) {
                    return false;
                }
                return false;
            }
            sl = typed;
            return true;
        }

        // Personal quarters keep their bathroom: others use a toilet, shower or workout device
        // in someone's quarters only when they cannot reach another one. A ranking, never a
        // filter, so nobody is left without a toilet; care devices are never private
        private bool IsInOthersQuarters(ISlots slots, BeingData user) {
            var d = slots.SlotDesignation;
            if (d != SlotDesignationH.Toilet && d != SlotDesignationH.Shower
                    && d != SlotDesignationH.Workout) {
                return false;
            }
            var owner = areas.PersonalQuartersOwnerOf(areas.At(slots.Entity.PosIdx));
            return owner != 0 && owner != user.Id;
        }

        private ReachQuery IslandsOf(BeingData walker) {
            var being = walker?.Being;
            if (being == null) { return default; }
            return ReachQuery.ByIslands(areas, being.PosIdx, being.Motion.IsGrounded);
        }

        private void SlotsChanged(ISlots s, bool added) {
            if (added) {
                if (!s.Tile.IsConstructed) {
                    D.Err("Adding unconstructed slots: {0} | {1}", s, s.Tile);
                }
                if (allSlots.Add(s)) {
                    // D.Warn("Added slots: {0}", slots);
                    if (!byDesignation.TryGetValue(s.SlotDesignation, out var list)) {
                        if (s.SlotDesignation == 0) {
                            D.Err("Slot has designation of 0! Refusing to register: {0}", s);
                            return;
                        }
                        list = new List<ISlots>();
                        byDesignation.Add(s.SlotDesignation, list);
                    }
                    list.Add(s);
                } else {
                    #if DEBUG_SLOTS
                    D.Err("Slots was already there: {0}", slots);
                    #endif
                }
            } else {
                if (allSlots.Remove(s)) {
                    // D.Warn("Removed slots: {0}", slots);
                    if (byDesignation.TryGetValue(s.SlotDesignation, out var list)) {
                        list.Remove(s);
                    } else {
                        D.Err("Removed slot but not found in byDesignation: {0}", s);
                    }
                } else {
                    #if DEBUG_SLOTS
                    D.Err("Slots was not there: {0}", slots);
                    #endif
                }
            }
        }

        public override void Unload() {
            processedSlots.Clear();
            availableWithoutSeat.Clear();
            availableWithSeat.Clear();
            allSlots.Clear();
            byDesignation.Clear();
        }

        public string GetName() {
            return Id;
        }
    }
}
