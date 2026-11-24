using System.Collections.Generic;
using System.Diagnostics;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Components {
    public abstract class BaseSlotsComp<T> : BaseComponent<T>,
            IRotationDependent, ISlots where T : IComponent, new() {
        public List<Slot> Slots => slots;
        public ElectricNodeComp ENode => base.Tile.ENode;
        protected List<Slot> slots;
        public new Tile Tile => base.Tile;
        public bool HideBeing { get; private set; }
        public bool HideUI { get; set; }
        public string UITitle => uiTitleText ?? Tile.Definition.NameT;
        public abstract int SlotDesignation { get; }

        //Defines sides in addition
        public bool IsMultislot { get; protected set; }
        public bool RequiresSafeArea { get; protected set; }

        public virtual bool IsAvailable => EntityUtils.IsDeviceUsable(Tile)
            && !Tile.Constructable.HasGoal;

        public virtual bool IsMakeshift { get; protected set; }
        protected void ConfigureSlotsUI(string iconId, string titleText) {
            uiIconId = iconId;
            uiTitleText = titleText;
        }
        protected string uiIconId;
        protected string uiTitleText;
        public bool SlotUseWorkSpot;
        public bool SlotUsePorts;
        private Vector2[] SlotOffsets_D;
        private Vector2[] SlotOffsets_U;
        private Vector2[] SlotOffsets_R;
        private Vector2[] SlotOffsets_L;
        protected bool hasSpotRotation;
        protected Facing.Type spotRotation;

        public virtual Facing.Type SpotRotation {
            get {
                if (hasSpotRotation) {
                    return Facing.Rotate(Tile.Transform.Rotation, spotRotation);
                } else {
                    return Tile.Transform.Rotation;
                }
            }
        }

        // Free slot means that you can place something there permanently,
        // maybe a vase or some artifact. If slot is in use, we can still
        // eat from the surface by sitting or standing next to it
        public bool HasFreeSlot {
            get {
                for (int i = 0; i < slots.Count; i++) {
                    Slot s = slots[i];
                    if (s.ContainedBeing == null
                            && s.ReservedBy == null) {
                        return true;
                    }
                }
                return false;
            }
        }

        protected int numSlots;

        public int UsedSlotCount {
            get {
                int count = 0;
                for (int i = 0; i < slots.Count; i++) {
                    Slot slot = slots[i];
                    if (slot.ContainedBeing != null) {
                        count++;
                    }
                }
                return count;
            }
        }

        public virtual bool HasSubmenuNow => true;
        public virtual string SubmenuTitle => uiTitleText;

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            // could be unbuilt
            if (slots == null) { return; }
            foreach (var s in slots) {
                s.SlotPosIdx = Pos.NeighborAt(s.SlotPosIdx, dx, dy);
                s.ContainedPosition += new Vector2(dx, dy);
                if (s.Sides != null) {
                    for (int i = 0; i < s.Sides.Count; i++) {
                        s.Sides[i] = Pos.NeighborAt(s.Sides[i], dx, dy);
                    }
                }
            }
        }

        [Conditional("UNITY_EDITOR")]
        private void ValidateSlot(Slot slot, int expectedIdx, int num) {
            //if (slot.WasRotated) { return; }
            //if (The.TestsAreRunning) { return; }
            if (slot.SlotPosIdx != expectedIdx) {
                D.ErrSoft("Slot position does not match! Expected: {0} ({1}). Actual: {2} ({3}). Focusing on object {4}",
                    Pos.String(expectedIdx), expectedIdx, Pos.String(slot.SlotPosIdx), slot.SlotPosIdx, Entity);

                //slot.SlotPosIdx = expectedIdx;
            }
            if (num != -1 && num != slot.Number) {
                D.ErrSoft("Slot number does not match! Expected: {0}. Actual: {1}. Focusing on object {2}",
                    num, slot.Number, Entity);
                //slot.Number = num;
            }
        }

        public Slot GetSlot(int idx, int num = -1) {
            if (numSlots == 1) {
                ValidateSlot(slots[0], idx, num);
                return slots[0];
            }
            for (int i = 0; i < slots.Count; i++) {
                Slot slot = slots[i];
                if (slot.SlotPosIdx == idx) {
                    if (num != -1 && slot.Number != num) { continue; }
                    ValidateSlot(slot, idx, num);
                    return slot;
                }
            }
            D.Err("Slots {2} Could not find slot with idx: {0}. Available slots: {1}",
                idx, D.Dump(slots), this);
            return null;
        }

        public Slot GetSlotByNumber(int num) {
            if (numSlots == 1 && num != 0) {
                D.Err("Trying to get slot by number > 0 when there's just 1 slot! {0} {1}",
                    num, this);
                return null;
            }
            if (num >= numSlots) {
                D.Err("Trying to get slot by number {0} that is greater than total slots {1}! {2}",
                    num, numSlots, this);
                return null;
            }
            var slot = slots[num];
            D.Ass(slot.Number == num, "Slot number is not equal to it's index! {0} != {1}",
                slot.Number, num);
            return slot;
        }

        public virtual bool IsCompatibleWith(BeingData being) {
            return being.Species.IsHumanoid;
        }

        public virtual void SlotChanged(Slot slot) {
            stateChangedB?.Invoke();
        }

        protected virtual Vector2[] SlotOffsetFor(Facing.Type facing) {
            switch (facing) {
                case Facing.Type.Down: return SlotOffsets_D;
                case Facing.Type.Up: return SlotOffsets_U;
                case Facing.Type.Left: return SlotOffsets_L;
                case Facing.Type.Right: return SlotOffsets_R;
            }
            return null;
        }

        private void ValidatePorts() {
            if (Tile?.Transform == null) {
                D.Err("Trying to validate ports when Tile.Transform is null! {0}. Define Tile Transform above this component: {1}",
                    Entity.Definition.Id, this);
                return;
            }
            Tile.Transform.EnsurePortPresence(
                SlotUseWorkSpot, SlotUsePorts, SlotUsePorts, this);
        }

        protected static int NumSlotsIn(ComponentConfig config) {
            return config.GetInt(PropertyIdH.Slots, 1);
        }

        protected void OnConfigSlots() {
            HideUI = Config.GetBool(PropertyIdH.HideUI, false);
            HideBeing = Config.GetBool(PropertyIdH.HideBeing, false);
            IsMultislot = Config.GetBool(PropertyIdH.IsMultislot, false);
            IsMakeshift = Config.GetBool(PropertyIdH.IsMakeshift, false);
            RequiresSafeArea = Config.GetBool(PropertyIdH.RequiresSafeArea, true);
            numSlots = NumSlotsIn(Config);
            if (numSlots == 0) {
                D.Err("Loaded numslots 0 on config! {0}", entity);
            }
            SlotUseWorkSpot = Config.GetBool(PropertyIdH.SlotUseWorkSpot, false);
            SlotUsePorts = Config.GetBool(PropertyIdH.SlotUsePorts, false);
            SlotOffsets_D = Config.GetVector2Set(PropertyIdH.SlotOffsets_D, null);
            SlotOffsets_U = Config.GetVector2Set(PropertyIdH.SlotOffsets_U, null);
            SlotOffsets_L = Config.GetVector2Set(PropertyIdH.SlotOffsets_L, null);
            SlotOffsets_R = Config.GetVector2Set(PropertyIdH.SlotOffsets_R, null);

            var spotRotStr = Config.GetString(PropertyIdH.SpotRotation, null);
            if (spotRotStr != null) {
                hasSpotRotation = true;
                spotRotation = Facing.ConvertRotation(spotRotStr[0]);
            }
        }

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        private bool isBeingSlots;
        private bool isObjSlots;

        private bool slotsCreated;

        protected void CreateBeingSlots(string uiIcon, string uiTitle) {
            ValidatePorts();
            if (slotsCreated) {
                D.Err("Trying to create being slots more than once! {0}", this);
                return;
            }
            slotsCreated = true;
            if (uiIcon == null) {
                HideUI = true;
            } else {
                ConfigureSlotsUI(uiIcon, uiTitle);
            }
            if (numSlots == 0) {
                D.Err("Trying to create BaseSlotsComp.CreateBeingSlots with 0 slots! Did you call OnConfigSlots in OnConfig? {0} {1}",
                    this, entity);
                numSlots = 1;
            }
            if (slots == null) {
                isBeingSlots = true;
                slots = new List<Slot>(numSlots);
                var tt = Tile.Transform;
                var offs = SlotOffsetFor(tt.Rotation);
                for (int i = 0; i < numSlots; i++) {
                    var posOff = offs != null ? offs[i] : Vector2.zero;
                    int slotPosIdx = GetSlotPosIdx();
                    var slotPos = tt.Position;
                    var sl = new Slot(this, slotPosIdx, slotPos);
                    sl.Number = i;
                    sl.ContainedOffset = posOff;
                    slots.Add(sl);
                }
            }
            S.Sig.SlotsChanged.Send(this, true);
        }

        private int GetSlotPosIdx() {
            int slotPosIdx;
            if (SlotUseWorkSpot) {
                slotPosIdx = Tile.Transform.WorkSpot;
            } else if (SlotUsePorts) {
                slotPosIdx = Tile.Transform.InPort;
            } else {
                slotPosIdx = Tile.Transform.PosIdx;
            }

            return slotPosIdx;
        }


        /// <summary>
        /// For tables
        /// </summary>
        protected void CreateObjSlots(string uiIcon, string uiTitle) {
            ConfigureSlotsUI(uiIcon, uiTitle);
            if (slots == null) {
                isObjSlots = true;
                var tt = Tile.Transform;
                numSlots = IsMultislot ? tt.Width * tt.Height : 1;
                slots = new List<Slot>(numSlots);
                // WHAT?
                // TODO use slot mask and apply rotation
                if (IsMultislot) {
                    var cnt = tt.GetSubtilePos(Caches.PosIdx);
                    for (int i = 0; i < cnt; i++) {
                        var p = Caches.PosIdx[i];
                        var s = NewSlotWithSides(tt, p, Pos.ToVector(p));
                        s.Number = i;
                        slots.Add(s);
                    }
                } else {
                    var s = NewSlotWithSides(tt, tt.PosIdx, tt.Position);
                    slots.Add(s);
                }
            }
            S.Sig.SlotsChanged.Send(this, true);
        }

        private Slot NewSlotWithSides(TileTransformComp tt, int p, Vector2 v) {
            var s = new Slot(this, p, v);
            s.Sides = new List<int>();
            Pos.ForEachAround(p, 1, false, false, (sideP) => {
                if (!tt.Contains(sideP)) {
                    s.Sides.Add(sideP);
                }
                return true;
            });
            return s;
        }

        protected void RemoveSlots() {
            S.Sig.SlotsChanged.Send(this, false);
        }

        public virtual bool Use(BeingData b, Slot s) {
            // D.Warn("Using slot: {0} -> {1}", b, this);
            return true;
        }

        public void AfterRotationUpdated() {
            var tt = Tile.Transform;
            int slotIdx = 0;
            // WHAT?
            // TODO use slot mask and apply rotation
            if (isObjSlots) {
                if (IsMultislot) {
                    var cnt = tt.GetSubtilePos(Caches.PosIdx);
                    for (int i = 0; i < cnt; i++) {
                        var p = Caches.PosIdx[i];
                        var s = NewSlotWithSides(tt, p, Pos.ToVector(p));
                        s.Number = i;
                        Caches.Slots[slotIdx++] = s;
                    }
                } else {
                    var s = NewSlotWithSides(tt, tt.PosIdx, tt.Position);
                    Caches.Slots[slotIdx++] = s;
                }
            } else if (isBeingSlots) {
                var offs = SlotOffsetFor(tt.Rotation);
                for (int i = 0; i < numSlots; i++) {
                    var posOff = offs != null ? offs[i] : Vector2.zero;
                    var slotPosIdx = GetSlotPosIdx();
                    var slotPos = tt.Position;
                    var sl = new Slot(this, slotPosIdx, slotPos);
                    sl.Number = i;
                    sl.ContainedOffset = posOff;
                    Caches.Slots[slotIdx++] = sl;
                }
            } else {
                D.Err("Neither Being Slots nor Obj Slots were created for {0}", Tile);
                return;
            }
            for (int i = 0; i < numSlots; i++) {
                var newSl = Caches.Slots[i];
                var sl = slots[i];
                sl.SlotPosIdx = newSl.SlotPosIdx;
                sl.Facing = newSl.Facing;
                sl.ContainedOffset = newSl.ContainedOffset;
                sl.ContainedPosition = newSl.ContainedPosition;
                sl.Sides = newSl.Sides;
            }
        }
    }
}
