using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Creatures {
    public sealed class CarryController {
        public Being Being { get; private set; }
        private Entity primaryCarried;
        private readonly List<Obj> additionalCarried = new();

        public Entity PrimaryCarried => primaryCarried;
        public IReadOnlyList<Obj> AdditionalCarried => additionalCarried;
        public bool IsCarrying => primaryCarried?.IsActive == true;

        public CarryController(Being being) {
            Being = being;
        }

        public int TotalCarriedStackSize {
            get {
                int total = 0;
                if (primaryCarried is Obj obj && obj.Pile != null) {
                    total += obj.Pile.StackSize;
                }
                for (int i = 0; i < additionalCarried.Count; i++) {
                    var o = additionalCarried[i];
                    if (o?.Pile != null) {
                        total += o.Pile.StackSize;
                    }
                }
                return total;
            }
        }

        public UnstoredMatComp FindCarriedMat(MatType type) {
            if (primaryCarried is Obj obj && obj.Pile != null
                    && obj.Pile.Type == type) {
                return obj.Pile;
            }
            for (int i = 0; i < additionalCarried.Count; i++) {
                var o = additionalCarried[i];
                if (o?.Pile != null && o.Pile.Type == type) {
                    return o.Pile;
                }
            }
            return null;
        }

        public bool CarryAdditional(Obj o) {
            if (o == null) {
                D.Err("Trying to carry additional null obj! {0}", Being);
                return false;
            }
            if (!o.IsActive) {
                D.Err("Trying to carry additional inactive obj! {0} {1}", o, Being);
                return false;
            }
            if (o.Pile == null) {
                D.Err("Trying to carry additional non-pile obj! {0} {1}", o, Being);
                return false;
            }
            if (!o.SetCarrier(Being)) {
                return false;
            }

            // Chain attachment: attach to the last carried item's sprite set
            var gfx = Being.Graphics;
            var looksDef = gfx.LooksDef;
            o.Graphics.OverrideSortOrder(looksDef.CarriedObjSortOrder(gfx.CurrentFacing));
            AnimatedSpriteSet parentSet;
            if (additionalCarried.Count > 0) {
                var lastObj = additionalCarried[additionalCarried.Count - 1];
                parentSet = lastObj.Graphics.SpriteSet;
            } else if (primaryCarried is Obj primaryObj) {
                parentSet = primaryObj.Graphics.SpriteSet;
            } else {
                parentSet = gfx.SpriteSet;
            }
            o.Graphics.SpriteSet.AttachTo(parentSet);
            // Small vertical offset so stacked items don't overlap
            parentSet.CarryOffset = new Vector2(0f, 0.1f);
            o.HideAllInfo();
            additionalCarried.Add(o);
            return true;
        }

        // Takes one item out of the stack and keeps carrying the rest, e.g. when a
        // carried pile is destroyed. Items after it hang off its sprite set, so they
        // are detached and chained again behind the item that was in front of it.
        public bool ReleaseAdditional(Obj o) {
            var idx = additionalCarried.IndexOf(o);
            if (idx < 0) { return false; }
            for (int i = additionalCarried.Count - 1; i >= idx; i--) {
                additionalCarried[i]?.Graphics.SpriteSet?.Detach();
            }
            additionalCarried.RemoveAt(idx);
            o.Graphics.RestoreSortOrder();
            o.SetCarrier(null);
            if (o.IsActive) {
                o.Transform.UpdateInSpace(true);
            }
            var gfx = Being.Graphics;
            for (int i = idx; i < additionalCarried.Count; i++) {
                var item = additionalCarried[i];
                if (item == null || !item.IsActive) { continue; }
                AnimatedSpriteSet parentSet;
                if (i > 0) {
                    parentSet = additionalCarried[i - 1].Graphics.SpriteSet;
                } else if (primaryCarried is Obj primaryObj) {
                    parentSet = primaryObj.Graphics.SpriteSet;
                } else {
                    parentSet = gfx.SpriteSet;
                }
                item.Graphics.SpriteSet.AttachTo(parentSet);
                parentSet.CarryOffset = new Vector2(0f, 0.1f);
            }
            return true;
        }

        public void DropAllAdditional(bool skipInventoryCheck = false) {
            // Drop in reverse order to properly unchain sprites
            for (int i = additionalCarried.Count - 1; i >= 0; i--) {
                var o = additionalCarried[i];
                if (o == null || !o.IsActive) { continue; }
                o.Graphics.RestoreSortOrder();
                o.Graphics.SpriteSet?.Detach();
                o.SetCarrier(null);
                o.StorableObj?.UpdateForbiddenIcon();
                if (!skipInventoryCheck && o.IsActive) {
                    var mat = o.Pile;
                    if (mat != null) {
                        if (!mat.IsAddedToInventory && mat.StackSize > 0) {
                            mat.AddToInventory();
                        }
                        if (!EntityUtils.TryMerge(mat)) {
                            o.Transform.UpdateInSpace(true);
                            mat.UpdateForbiddenIcon(true);
                        }
                    }
                } else {
                    o.Transform.UpdateInSpace(true);
                }
            }
            additionalCarried.Clear();
        }

        public void DropAll(bool skipInventoryCheck = false) {
            DropAllAdditional(skipInventoryCheck);
            if (primaryCarried != null) {
                Being.Graphics.Drop(primaryCarried, skipInventoryCheck);
            }
        }

        public void OnPrimarySet(Entity e) {
            primaryCarried = e;
        }

        public void OnPrimaryCleared() {
            primaryCarried = null;
        }

        public void ClearIfInactive() {
            if (primaryCarried?.IsActive != true) {
                primaryCarried = null;
            }
            for (int i = additionalCarried.Count - 1; i >= 0; i--) {
                if (additionalCarried[i]?.IsActive != true) {
                    additionalCarried.RemoveAt(i);
                }
            }
        }

        public void GetAllCarriedObjIds(List<int> ids) {
            ids.Clear();
            if (primaryCarried is Obj primaryObj) {
                ids.Add(primaryObj.Id);
            }
            for (int i = 0; i < additionalCarried.Count; i++) {
                ids.Add(additionalCarried[i].Id);
            }
        }

        public int AdditionalCount => additionalCarried.Count;

        public void UpdateAdditionalPositions(Vector2 position, float zOff) {
            // Additional items follow via chained sprite attachment,
            // but we need to teleport their transforms for physics/selection
            for (int i = 0; i < additionalCarried.Count; i++) {
                var o = additionalCarried[i];
                if (o == null || !o.IsActive) { continue; }
                o.Transform.TeleportTo(position);
                o.Graphics.TeleportTo(position);
                o.Graphics.SetZOffset(zOff);
            }
        }
    }
}
