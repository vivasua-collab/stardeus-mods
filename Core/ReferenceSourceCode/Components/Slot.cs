using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using KL.Randomness;

namespace Game.Components {
    public sealed class Slot {
        private BCallback stateB;
        public BCallback StateB => stateB ??= new BCallback();

        public ISlots Parent;
        public Being ContainedBeing { get; private set; }
        public Vector2 ContainedPosition;
        // Offset from ContainedPosition, which should be center of PosIdx
        public Vector2 ContainedOffset;
        public Facing.Type Facing;
        public int SlotPosIdx;
        public int Number;
        public Being ReservedBy { get; private set; }
        public long LastReserveTime;
        public bool ReserveSide(Being being, int sidePosIdx) {
            if (Sides == null) {
                D.Err("Trying to reserve slot side that has no sides! {0} {1}", this,
                    being.AIAgent.CurrentAction);
                return false;
            }
            if (sideReservations == null) {
                sideReservations = new Dictionary<int, Being>();
            } else if (sideReservations.TryGetValue(sidePosIdx, out var res)) {
                D.Err("Trying to reserve a side pos that is already reserved! {0} {1}. Reserved by {2}",
                    this, being.AIAgent, res);
                return false;
            }
            sideReservations[sidePosIdx] = being;
            stateB?.Invoke();
            return true;
        }

        public bool Reserve(Being being) {
            if (Sides != null) {
                D.Err("Trying to reserve slot that has sides! {0} {1}", this,
                    being.AIAgent.CurrentAction);
                return false;
            }
            if (ReservedBy != null) {
                if (ReservedBy == being) {
                    return true;
                }
                if (being != null) {
                    if (ReservedBy?.IsDead == true) {
                        D.Err("Trying to reserve something that is reserved by someone who is dead: {0}.Will allow", this);
                    } else if (LastReserveTime != 0 && LastReserveTime < G.Ticks - Consts.TicksPerDay) {
                            D.Err("Trying to reserve something that is reserved by someone else for longer than a day! {0}. Will allow", this);
                        return false;
                    } else {
                        return false;
                    }
                }
            }
            ReservedBy = being;
            if (being == null) {
                LastReserveTime = 0;
            } else {
                LastReserveTime = G.Ticks;
            }
            stateB?.Invoke();
            return true;
        }
        public bool Unreserve(Being being, int sidePosIdx = -1) {
            if (Sides != null) {
                if (sidePosIdx != -1) {
                    if (sideReservations != null &&
                            sideReservations.TryGetValue(sidePosIdx, out var res)) {
                        if (res == being) {
                            sideReservations.Remove(sidePosIdx);
                            stateB?.Invoke();
                            return true;
                        }
                        D.Err("Trying to remove side reservation that doesn't belong to the being! {0}, trying to remove: {1}, actual {2}",
                            this, being, res);
                        return false;
                    } else {
                        D.Err("Trying to remove a non-existent side reservation: {0} {1}",
                            this, being);
                        return false;
                    }
                } else {
                    // we don't know which side we have reserved
                    if (sideReservations != null) {
                        foreach (var sr in sideReservations) {
                            if (sr.Value == being) {
                                sideReservations.Remove(sr.Key);
                                stateB?.Invoke();
                                return true;
                            }
                        }
                    }
                    return true;
                }
            }
            if (ReservedBy != being && ReservedBy != null) {
                D.ErrSoft("Trying to unreserve a slot {0} at {1} that was not reserved by being: {2}. Reserved by: {3}. {4}",
                    Parent.Entity.DefinitionId, Pos.String(SlotPosIdx),
                        being, ReservedBy, Parent.Entity);
                return false;
            }
            ReservedBy = null;
            LastReserveTime = 0;
            stateB?.Invoke();
            return true;
        }
        private Dictionary<int, Being> sideReservations;
        public IReadOnlyDictionary<int, Being> SideReservations => sideReservations;

        public List<int> Sides;

        // for tests
        public int UseCount;

        public bool Put(Being entity) {
            if (ContainedBeing == entity) { return true; }
            if (entity == null) {
                D.Err("Cannot Put(null) into a slot, use Remove(being) instead! {0}",
                    Parent);
                return false;
            }
            if (ContainedBeing != null) {
                #if !PRODUCTION_BUILD
                D.Err("Slot: {2}: Trying to put {0} in a slot {3} that already contains {1}",
                    entity, ContainedBeing, SlotPosIdx, Parent.Entity.DefinitionId);
                #endif
                return false;
            }
            if (ReservedBy != null && entity.Id != ReservedBy.Id) {
                #if !PRODUCTION_BUILD
                D.Err("Slot: {2}: Trying to put {0} in a slot {3} that is reserved by {1}",
                    entity, ReservedBy, SlotPosIdx, Parent.Entity.DefinitionId);
                #endif
                return false;
            }
            if (Pos.SqrDistance(entity.PosIdx, SlotPosIdx) > 9) {
                D.Err("Trying to put being into slot from too far away! {0} | Act: {1} | {2} | {3}",
                    Pos.StringFromTo(entity.PosIdx, SlotPosIdx), entity.AIAgent.CurrentAction,
                        Parent.Tile, entity);
                return false;
            }
            entity.Data.StateFlags |= BeingState.InDevice;
            entity.Motion.ReleaseClaimedPos();
            ContainedBeing = entity;
            UseCount++;
            Parent.SlotChanged(this);
            stateB?.Invoke();

            // Move inside
            PutVisually(entity);

            return true;
        }

        public void PutVisually(Being entity) {
            entity.Graphics.SetOffset(ContainedPosition -
                entity.Position + ContainedOffset);
            if (Parent.HideBeing) {
                entity.Graphics.SetHidden();
            }
        }

        public bool Remove(Being being) {
            // D.Warn("Removing being from slot: {0} | {1}", being, Parent.Tile);
            if (being == null) {
                being = ContainedBeing;
            }
            if (ContainedBeing != null) {
                if (being != ContainedBeing) {
                    D.Err("Trying to remove a different being from a slot {0} at {1}: {2}, existing: {3}",
                        Parent.Entity.DefinitionId, Pos.String(SlotPosIdx), being, ContainedBeing);
                    return false;
                }
                ContainedBeing = null;
                Parent.SlotChanged(this);
                stateB?.Invoke();
            } else {
                // nobody was contained!
                // D.Warn("Trying to remove a being from a slot where it wasn't contained! Cur Act: {0} | Tile: {1} | B: {2} | Goal: {3}",
                    // being.AIAgent.CurrentAction, Parent.Tile, being, being.AIAgent.Goal);
                if ((being.Data.StateFlags & BeingState.InDevice) == BeingState.InDevice) {
                    D.Err("Being was not in a slot, but has InDevice flag! {0} | {1} | {2}",
                        Pos.StringFromTo(being.PosIdx, SlotPosIdx),
                            being, Parent.Tile);
                }
                return false;
            }
            being.Data.StateFlags &= ~BeingState.InDevice;
            RemoveVisually(being);
            return true;
        }

        public void RemoveVisually(Being being) {
            being.Graphics.SetOffset(Consts.V2Zero);
            if (Parent.Tile.Transform.HasOutPort) {
                being.Motion.TeleportTo(
                    EntityUtils.CenterOf(Parent.Tile.Transform.OutPort)
                        + Rng.UInsideUnitCircle() * 0.25f);
            }
            if (Parent.HideBeing) {
                being.Graphics.SetVisible();
            }
        }

        public bool HasFreeSides {
            get {
                if (Sides == null) {
                    D.Err("Trying to check Slot.Sides when Sides are not defined: {0}",
                        Parent.Entity.DefinitionId);
                    return false;
                }
                return sideReservations == null ||
                    sideReservations.Count < Sides.Count;
            }
        }

        public bool HasSideReservationFor(int sideIdx) {
            return sideReservations?.ContainsKey(sideIdx) == true;
        }

        public Slot(ISlots parent, int pos, Vector2 containedPosition) {
            Parent = parent;
            SlotPosIdx = pos;
            ContainedPosition = containedPosition;
            ContainedOffset = Consts.V2Zero;
        }

        public override string ToString() {
            return $"Slot[{Pos.String(SlotPosIdx)}|{Parent.Entity}|Res:{ReservedBy}|Cont:{ContainedBeing}]";
        }
    }
}
