using System;
using System.Buffers;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Reg;
using KL.Utils;
using MonoMod.Utils;
using UnityEngine;

using Game.Utils;
using Game.Systems.UIUX;
namespace Game.Systems.Sleep {
    public sealed class SleepSys : GameSystem, ISaveableSpecial {
        public const string SysId = "SleepSys";
        public override string Id => SysId;


        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new SleepSys());
        }

        private readonly List<BedComp> beds = new();

        /// <summary>
        /// Key is BeingData id
        /// </summary>
        private readonly Dictionary<uint, BedAssignment> assignments = new();
        private readonly Dictionary<uint, BCallback> bedUserStateBindables = new();

        public override void Unload() {
        }

        public string GetName() {
            return Id;
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.RegisterBed.AddListener(OnRegisterBed);
            S.Sig.BedSlotStateChanged.AddListener(OnBedSlotStateChanged);
            S.Sig.BeforeSave.AddListener(OnBeforeSave);
            S.Sig.ShipMoved.AddListener(OnShipMoved);
        }

        private void OnShipMoved(int dx, int dy) {
            for (int i = 0; i < beds.Count; i++) {
                var slots = beds[i].Slots;
                for (int j = 0; j < slots.Length; j++) {
                    slots[j].RefreshCurrentUserPlacement();
                }
            }
        }

        private void OnBedSlotStateChanged(uint beingDataId) {
            NotifyBedUserState(beingDataId);
        }

        private void NotifyBedUserState(uint beingDataId) {
            if (bedUserStateBindables.TryGetValue(beingDataId, out var b)) {
                b.Invoke();
            }
        }

        private BCallback GetOrCreateBedUserStateB(uint beingDataId) {
            if (!bedUserStateBindables.TryGetValue(beingDataId, out var b)) {
                b = new BCallback();
                bedUserStateBindables[beingDataId] = b;
            }
            return b;
        }

        private void OnLoadSave(GameState state) {
            state.Query.FindBed.SetResponder(FindBed);
            state.Query.HasSafelyAssignableBedFor.SetResponder(HasSafelyAssignableBedFor);
            state.Query.FindBeds.SetResponder(FindBeds);
            state.Query.AssignBedSlot.SetResponder(AssignBedSlot);
            state.Query.FindBedUsers.SetResponder(FindBedUsers);
            state.Query.FindTemporaryBed.SetResponder(FindTempBed);
            state.Query.NumBedsInArea.SetResponder(NumBedsInArea);
            state.Query.GetBedUserStateB.SetResponder(GetOrCreateBedUserStateB);
        }

        private int NumBedsInArea(int areaId, BeingData sleeper, int max) {
            var area = S.Sys.Areas.AreaById(areaId);
            int num = 0;
            foreach (var bed in beds) {
                if (!area.Contains(bed.Tile.PosIdx)) { continue; }
                bool isAssignable = false;
                foreach (var slot in bed.Slots) {
                    if (!slot.IsTemporary && slot.IsCompatibleWith(sleeper)) {
                        isAssignable = true;
                        break;
                    }
                }
                if (isAssignable) {
                    num++;
                    if (max > 0 && max == num) {
                        break;
                    }
                }
            }
            return num;
        }

        private static bool IsValidBedOwner(BeingData data) {
            return data != null && !data.Body.HasFlag(BodyFlags.Missing);
        }

        private void ValidateAssignments() {
            List<BeingData> toRemove = null;
            List<uint> toRemoveIds = null;
            foreach (var ass in assignments) {
                var beingData = S.Query.FindBeingData.Ask(ass.Key);
                if (!IsValidBedOwner(beingData)) {
                    if (beingData != null) {
                        toRemove ??= new();
                        toRemove.Add(beingData);
                    } else {
                        // logged as removed but kept before: no data to unassign through
                        toRemoveIds ??= new();
                        toRemoveIds.Add(ass.Key);
                    }
                    D.Err("Will remove invalid bed assignment, being missing: {0} -> {1} | {2}",
                        ass.Key, beingData, S.Components.Find<BedComp>(ass.Value.BedId)?.Tile);
                    continue;
                }
            }

            if (toRemove != null) {
                foreach (var missingBeing in toRemove) {
                    AssignBedSlot(missingBeing, null);
                }
            }
            if (toRemoveIds != null) {
                foreach (var id in toRemoveIds) {
                    if (assignments.TryGetValue(id, out var ass)) {
                        var bed = S.Components.Find<BedComp>(ass.BedId);
                        if (bed != null) {
                            foreach (var slot in bed.Slots) {
                                if (slot.OwnerDataId == id) {
                                    slot.Owner = null;
                                }
                            }
                        }
                        assignments.Remove(id);
                    }
                }
            }
        }

        private bool AssignBedSlot(BeingData owner, BedSlot slot) {
            if (slot == null || !slot.Comp.Tile.IsActive) {
                // get the original bed
                if (assignments.TryGetValue(owner.Id, out var oldAss)) {
                    var oldBed = S.Components.Find<BedComp>(oldAss.BedId);
                    if (oldBed != null) {
                        foreach (var sl in oldBed.Slots) {
                            if (sl.OwnerDataId == owner.Id) {
                                sl.Owner = null;
                                break;
                            }
                        }
                        oldBed.UpdateUIBlock(true);
                    }
                    assignments.Remove(owner.Id);
                    NotifyBedUserState(owner.Id);
                    return true;
                }
                return false;
            } else {
                if (!slot.Comp.Tile.IsConstructed) {
                    return false;
                }
                if (!IsValidBedOwner(owner)) {
                    D.Warn("Refusing to assign a bed slot to a being that is not on the ship: {0} | {1}",
                        owner, slot.Comp.Tile);
                    if (slot.OwnerDataId == owner.Id) {
                        slot.Owner = null;
                    }
                    return false;
                }
                if (slot.Owner == owner) {
                    // nothing to do
                    return true;
                }
                if (slot.OwnerDataId != 0) {
                    if (slot.Owner != null) {
                        AssignBedSlot(slot.Owner, null);
                    }
                    // Unassign the old one first
                    assignments.Remove(slot.OwnerDataId);
                }
                if (assignments.TryGetValue(owner.Id, out var oldAss)) {
                    var oldBed = S.Components.Find<BedComp>(oldAss.BedId);
                    if (oldBed != null) {
                        foreach (var sl in oldBed.Slots) {
                            if (sl.OwnerDataId == owner.Id) {
                                sl.Owner = null;
                                break;
                            }
                        }
                        oldBed.UpdateUIBlock(true);
                    }
                }
                slot.Owner = owner;
                assignments[owner.Id] = BedAssignment.For(owner.Id, slot);
                slot.Comp.UpdateUIBlock(true);
                NotifyBedUserState(owner.Id);
                return true;
            }
        }
        private BedComp FindTempBed(BeingData user, bool tempSlot) {
            return FindClosestAssignableBedFor(user, tempSlot, false, false);
        }

        private BedComp FindBed(BeingData owner, bool autoAssign) {
            if (assignments.TryGetValue(owner.Id, out var bedAss)) {
                var bed = S.Components.Find<BedComp>(bedAss.BedId);
                if (bed == null || !bed.Tile.IsActive) {
                    // tile destroyed? bug?
                    assignments.Remove(owner.Id);
                    // we'll fall through and will try to auto-assign a new dock
                } else {
                    return bed;
                }
            }
            if (!autoAssign) {
                return null;
            }

            var freshBed = FindClosestAssignableBedFor(owner, false);
            if (freshBed == null) {
                freshBed = FindClosestAssignableBedFor(owner, true);
            }
            if (freshBed != null) {
                foreach (var slot in freshBed.Slots) {
                    if (slot.OwnerDataId == 0 && slot.IsCompatibleWith(owner)) {
                        if (slot.IsTemporary || AssignBedSlot(owner, slot)) {
                            return freshBed;
                        }
                    }
                }
                D.Err("Failed assigning a slot from fresh bed: {0} to {1}", freshBed, owner);
            }
            return null;
        }

        private BedComp FindClosestAssignableBedFor(BeingData owner, bool temporary,
                bool skipAssigned = true, bool forAutoAssign = true) {
            if (owner.Being == null) {
                return null;
            }
            var numBeds = beds.Count;
            if (numBeds == 0) {
                return null;
            }

            // Find closest assignable beds through region path length query
            var bedPool = ArrayPool<BedComp>.Shared;
            var assBeds = bedPool.Rent(numBeds);
            var numAssBeds = 0;

            var motion = owner.Being.Motion;
            var safety = owner.Safety;
            // the region path lengths below know doors but not air: a bed past rooms the
            // owner can't breathe in would win and the walk to it fail every night (CQGKQ)
            var reach = S.Sys.Path.Reg.Reach.For(motion);
            // build the assignable docks array
            for (int i = beds.Count - 1; i >= 0; i--) {
                var bed = beds[i];
                if (motion != null && motion.HadNavFailureAt(bed.Tile.PosIdx)) {
                    continue;
                }
                if (safety != null && !safety.IsActuallySafeAt(bed.Tile.PosIdx)) {
                    continue;
                }
                if (!reach.CanReach(bed.Tile)) {
                    continue;
                }
                if (bed.IsAssignable(owner, skipAssigned, forAutoAssign, temporary)) {
                    if (temporary && !bed.SlotFor(owner).CanUse(owner.Being)) {
                        continue;
                    }
                    assBeds[numAssBeds++] = bed;
                }
            }

            if (numAssBeds == 0) {
                bedPool.Return(assBeds, true);
                return null;
            }

            var wp = owner.Being.PosIdx;
            var isG = owner.Being.Motion.IsGrounded;

            var reqPool = ArrayPool<RegPathReq>.Shared;
            var regPathReqs = reqPool.Rent(numAssBeds);
            var resPool = ArrayPool<int>.Shared;
            var resPathLengths = resPool.Rent(numAssBeds);

            for (int i = 0; i < numAssBeds; i++) {
                var tran = assBeds[i].Tile.Transform;
                int inPosIdx;
                if (tran.HasInPort) {
                    inPosIdx = tran.InPort;
                } else {
                    // if no in port, just use the tile position as target
                    inPosIdx = tran.PosIdx;
                }
                regPathReqs[i] = RegPathReq.To(wp, inPosIdx, isG);
                regPathReqs[i].Permissions = owner.Being.Motion.Permissions;
            }

            BedComp closest = null;
            float closestDist = float.MaxValue;

            if (S.Query.RegPathLengths.Ask(regPathReqs, numAssBeds, resPathLengths)) {
                for (int i = 0; i < numAssBeds; i++) {
                    var dist = resPathLengths[i];
                    if (dist < 0) { continue; }

                    if (dist < closestDist) {
                        closest = assBeds[i];
                        closestDist = dist;
                        if (dist < 0.1f) {
                            break;
                        }
                    }
                }
            }
            bedPool.Return(assBeds, true);
            reqPool.Return(regPathReqs);
            resPool.Return(resPathLengths);
            return closest;
        }

        private int FindBedUsers(BeingData[] results, BedSlot slot, bool assignable) {
            var found = 0;
            var bd = S.Query.AllBeingData.Ask();
            foreach (var d in bd) {
                var b = d.Value;
                if (!b.HasStateFlag(BeingState.Alive)) {
                    continue;
                }
                if (!IsValidBedOwner(b)) {
                    continue;
                }
                if (!slot.IsCompatibleWith(b)) {
                    continue;
                }
                if (assignable && assignments.ContainsKey(b.Id)) {
                    continue;
                }
                results[found++] = b;
                if (found == results.Length) {
                    break;
                }
            }
            return found;
        }

        private int FindBeds(BedComp[] results, BeingData owner, bool assignable) {
            var found = 0;
            for (int i = beds.Count - 1; i >= 0; i--) {
                var bed = beds[i];
                if (!assignable || bed.IsAssignable(owner, true, false, false)) {
                    results[found++] = bed;
                    if (found == results.Length) {
                        break;
                    }
                }
            }
            return found;
        }

        public bool HasSafelyAssignableBedFor(BeingData data) {
            return FindClosestAssignableBedFor(data, false) != null;
        }

        private void OnRegisterBed(BedComp bed, bool added) {
            if (added) {
                if (!bed.Tile.IsConstructed) {
                    D.Err("Trying to add (register) an unconstructed bed! {0}", bed.Tile);
                    return;
                }
                beds.Add(bed);
                foreach (var slot in bed.Slots) {
                    if (slot.OwnerDataId != 0) {
                        // check if we have the assignment
                        if (!assignments.ContainsKey(slot.OwnerDataId)) {
                            // this is likely a relocated tile
                            var assOwner = slot.Owner;
                            if (assOwner != null) {
                                AssignBedSlot(assOwner, slot);
                            }
                        }
                    }
                }
            } else {
                foreach (var slot in bed.Slots) {
                    if (slot.OwnerDataId != 0) {
                        var bd = S.Query.FindBeingData.Ask(slot.OwnerDataId);
                        if (bd != null) {
                            AssignBedSlot(bd, null);
                        } else {
                            // the owner's data is gone (purged): AssignBedSlot(null) threw
                            // here, and the destroyed bed stayed registered
                            assignments.Remove(slot.OwnerDataId);
                        }
                        slot.Owner = null;
                    }
                }
                beds.Remove(bed);
            }
        }

        // Copied on the main thread before the save hands the state to its worker thread:
        // the worker serialized the live dictionary while a bed assigned from the UI during
        // the save could change it
        private Dictionary<uint, BedAssignment> savedAssignments;
        private void OnBeforeSave(GameState state, bool willExit) {
            savedAssignments = new Dictionary<uint, BedAssignment>(assignments);
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Sleep ??= new();
            sd.Sleep.Assignments = savedAssignments ?? new Dictionary<uint, BedAssignment>(assignments);
            savedAssignments = null;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            if (sd.Sleep?.Assignments != null) {
                // in case
                assignments.Clear();
                assignments.AddRange(sd.Sleep.Assignments);
                Ready.When(WhenH.AIAgentsLoaded, ValidateAssignments);
            }
        }
    }
}
