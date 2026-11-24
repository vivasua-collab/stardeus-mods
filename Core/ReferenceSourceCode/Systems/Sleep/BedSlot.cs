using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Sleep {
    public sealed class BedSlot  {
        public GameState S;
        public byte Number;
        public uint OwnerDataId;
        public bool AutoAssign;
        public bool HidesBody;
        public BedComp Comp;
        public StatFlags Compatibility;
        public Vector2 Offset_D;
        public Vector2 Offset_U;
        public Vector2 Offset_R;
        public Vector2 Offset_L;
        public Vector2 NameOffset_D;
        public Vector2 NameOffset_U;
        public Vector2 NameOffset_R;
        public Vector2 NameOffset_L;
        public float NameRotation_D;
        public float NameRotation_U;
        public float NameRotation_L;
        public float NameRotation_R;
        public float NameRotation {
            get {
                var facing = Comp.Tile.Transform.Rotation;
                switch (facing) {
                    case Facing.Type.Down:  return NameRotation_D;
                    case Facing.Type.Up:    return NameRotation_U;
                    case Facing.Type.Left:  return NameRotation_L;
                    case Facing.Type.Right: return NameRotation_R;
                }
                return 0f;
            }
        }
        public bool IsTemporary;
        public string Name;
        private Facing.Type? rotation;
        public bool IsInUse;
        public Being CurrentUser;
        public int UseCount;
        public Vector2 Position => Comp.Tile.Position;
        public bool CanUse(Being being) {
            return !IsInUse || being == CurrentUser;
        }
        public bool PutUser(Being being) {
            if (IsInUse) {
                if (CurrentUser != being) {
                    D.Err("Someone else is using the bed! {0}. Trying to get in: {1}",
                        CurrentUser, being);
                }
                return false;
            }
            IsInUse = true;
            CurrentUser = being;
            UseCount++;
            PutVisually(being);
            ConditionState.CreateFor(S, ConditionIdH.Sleeping,
                being.Data, Severity.Irrelevant, true);
            Comp.UpdateUIBlock(true);
            return true;
        }

        public void PutVisually(Being being, float sideOffset = 0f) {
            being.Motion.TeleportTo(Position);
            var off = Offset;
            if (sideOffset != 0f) {
                off += SideOffset(sideOffset);
            }
            being.Graphics.SetOffset(off);

            being.Graphics.SetFacing(Rotation);
            if (Comp.HideBeing) {
                being.Graphics.SetHidden();
            } else if (HidesBody && being.Data.Looks.HasPart(LookPartTypeDef.Head)) {
                // hide every part flagged HideInBed (body, clothing, hat, ...) so only the
                // head shows - crawlers keep their hat visible (it covers the crawler, not the head)
                var crawler = being.Data.HasCondition(ConditionIdH.CrawlerAttached);
                var parts = being.Data.Looks.Parts;
                for (int i = parts.Length - 1; i >= 0; i--) {
                    var p = parts[i];
                    if (p.Type?.HideInBed != true) { continue; }
                    if (crawler && p.Type == LookPartTypeDef.Hat) { continue; }
                    p.Color.a = 0f;
                }
            }

            if (!Comp.HideBeing && being.Data.Species.IsHumanoid) {
                // kick any dead bodies out of the bed
                int bedPositions = Comp.Tile.Transform.GetSubtilePos(Caches.IntArray32);
                for (int i = 0; i < bedPositions; i++) {
                    var bedSubPos = Caches.IntArray32[i];
                    KickCorpsesFrom(bedSubPos);
                }
            }

            being.Looks.Rebuild();
            being.Graphics.SetVertical(false);
        }

        public void RefreshCurrentUserPlacement() {
            if (!IsInUse || CurrentUser?.IsActive != true) { return; }
            PutVisually(CurrentUser);
        }

        private Vector2 SideOffset(float sideOffset) {
            switch (Rotation) {
                case Facing.Type.Up: return new Vector2(-sideOffset, 0f);
                case Facing.Type.Down: return new Vector2(sideOffset, 0f);
                case Facing.Type.Left: return new Vector2(0f, sideOffset);
                case Facing.Type.Right: return new Vector2(0f, -sideOffset);
                default: {
                    D.Err("Unhandled facing: {0}", Rotation);
                    return Consts.V2Zero;
                }
            }
        }

        private void KickCorpsesFrom(int pos) {
            var dead = S.Beings.FindFirstMatching((b) => b.PosIdx == pos && b.IsDead);
            if (dead == null) { return; }
            int cnt = Pos.Around(dead.PosIdx, Caches.PosIdx9, false);
            for (int i = 0; i < cnt; i++) {
                var p = Caches.PosIdx9[i];
                if (EntityUtils.IsEmptyFloorAt(p)) {
                    dead.Motion.TeleportTo(EntityUtils.CenterOf(p));
                    D.Warn("Kicked the dead body out of bed: {0}->{1}",
                        Pos.StringFromTo(OutPort, p), dead);
                    break;
                }
            }
        }

        public void RemoveUser(Being being) {
            if (CurrentUser != being && CurrentUser != null) {
                D.Err("Bed slot's current user does not match! Expected: {0}. Actual: {1}",
                    being, CurrentUser);
            }
            CurrentUser = null;
            RemoveVisually(being);
            being.Data.RemoveCondition(S, ConditionIdH.Sleeping);
            IsInUse = false;
            Comp.UpdateUIBlock(true);
        }

        public void RemoveVisually(Being being) {
            being.Graphics.SetOffset(Consts.V2Zero);
            if (Pos.SqrDistance(being.PosIdx, OutPort) > 4) {
                // This reliably fails when running all story tests in device explosion event
                D.ErrSoft("Removing being from bed while being is too far! {0} | {1} | {2} | Goal: {3} | CurAct: {4}",
                    Pos.StringFromTo(OutPort, being.PosIdx),
                    being, being.Data, being.AIAgent.Goal, being.AIAgent.CurrentAction);
            } else {
                being.Motion.TeleportTo(EntityUtils.CenterOf(OutPort));
            }
            if (Comp.HideBeing) {
                being.Graphics.SetVisible();
            } else if (HidesBody && being.Data.Looks.HasPart(LookPartTypeDef.Head)) {
                // unhide every part flagged HideInBed
                var parts = being.Data.Looks.Parts;
                for (int i = parts.Length - 1; i >= 0; i--) {
                    var p = parts[i];
                    if (p.Type?.HideInBed != true) { continue; }
                    p.Color.a = 1f;
                }
            }
            being.Looks.Rebuild();
            being.Graphics.SetVertical(true);
        }

        public void SetRotation(Facing.Type rot) {
            rotation = rot;
        }
        public Facing.Type Rotation {
            get {
                if (rotation.HasValue) {
                    return Facing.Rotate(Comp.Tile.Transform.Rotation,
                        rotation.Value);
                }
                return Comp.Tile.Transform.Rotation;
            }
            set {
                rotation = value;
            }
        }
        public bool IsCompatibleWith(BeingData owner) {
            return owner.HasNeed(NeedIdH.Sleep) &&
                (owner.Mind.Species.StatFlags & Compatibility) == Compatibility
                    || (owner.Species.StatFlags & Compatibility) == Compatibility;
        }
        public Vector2 Offset {
            get {
                var facing = Comp.Tile.Transform.Rotation;
                switch (facing) {
                    case Facing.Type.Down: return Offset_D;
                    case Facing.Type.Up: return Offset_U;
                    case Facing.Type.Left: return Offset_L;
                    case Facing.Type.Right: return Offset_R;
                }
                return Consts.V2Zero;
            }
        }

        public Vector2 NameOffset {
            get {
                var facing = Comp.Tile.Transform.Rotation;
                switch (facing) {
                    case Facing.Type.Down: return NameOffset_D;
                    case Facing.Type.Up: return NameOffset_U;
                    case Facing.Type.Left: return NameOffset_L;
                    case Facing.Type.Right: return NameOffset_R;
                }
                return Consts.V2Zero;
            }
        }

        public bool IsPetSlot => (Compatibility & StatFlags.Pet) == StatFlags.Pet;

        private BeingData owner;

        public BeingData Owner {
            get {
                if (OwnerDataId == 0) { return null; }
                if (owner == null) {
                    var bd = S.Query.FindBeingData.Ask(OwnerDataId);
                    if (bd == null || !bd.HasStateFlag(BeingState.Alive)) {
                        OwnerDataId = 0;
                        return null;
                    }
                    owner = bd;
                }
                return owner;
            }
            set {
                if (value == null) {
                    OwnerDataId = 0;
                    owner = null;
                } else {
                    OwnerDataId = value.Id;
                    owner = value;
                }
                // whose personal quarters a room is may have changed
                S.Sys.Areas?.InvalidatePersonalQuartersOwners();
                Comp.UpdateUIBlock(true);
            }

        }

        public int InPort {
            get {
                if (Comp.SlotUsePorts) {
                    return Comp.Tile.Transform.InPort;
                } else {
                    return Pos.FromVector(Comp.Tile.Position + Offset);
                }
            }
        }
        public int OutPort {
            get {
                if (Comp.SlotUsePorts) {
                    return Comp.Tile.Transform.OutPort;
                } else {
                    return Comp.Tile.Transform.PosIdx;
                }
            }
        }
    }
}
