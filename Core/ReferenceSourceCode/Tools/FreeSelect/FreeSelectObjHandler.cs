using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Rendering;
using Game.Systems;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Tools {
    public sealed class FreeSelectObjHandler : FreeSelectHandler {
        public const string HandlerId = "FreeSelectObj";

        [SelfInit]
        private static void Register() {
            Register(HandlerId, new FreeSelectObjHandler());
        }

        public override int Order => 10;

        public override void Initialize(FreeSelectContext ctx) {
            base.Initialize(ctx);
            S.Sig.SelectEntity.AddListener(OnSelectEntity);
            S.Sig.SelectEntityUnfocused.AddListener(OnSelectEntityUnfocused);
        }

        public override bool AddSelectedBeingRightClickContext(int playerFaction,
                int canDirectControl, int underDirectControl,
                QuickChoice choice) {
            if (ctx.HoveredObj == null) { return false; }
            if (ctx.SelectedBeings.Count != 1) { return false; }
            var selBeing = ctx.SelectedBeings[0];

            Caches.ObjList.Clear();
            var foundUnderCursor = S.Objs.FindInRadius(Caches.ObjList, ctx.HoveredPosition, 1f);
            if (foundUnderCursor == 0) { return false; }

            bool addedAction = false;
            foreach (var item in Caches.ObjList) {
                if (item.Goal != null) { continue; }
                if (item.Data.Item != null) {
                    if (selBeing.Data.Inventory != null) {
                        var slotType = item.Data.Item.SlotType;
                        // check if we can equip it
                        if (selBeing.Data.Inventory.Equipment.TryGetValue(
                                slotType.IdH, out var slot)) {
                            // we can equip
                            choice.AddButton($"{T.VerbEquip}: {item.Data.ListName}",
                            () => {
                                EntityUtils.HideEntityLink();
                                item.Data.Reservation?.CancelGoal(T.NounDirectOrder);
                                if (BeingUtils.TryOrderEquipItem(selBeing, item.Data)) {
                                    UISounds.PlayDigitalConfirm();
                                    //
                                } else {
                                    UISounds.PlayActionDenied();
                                }
                            });
                            // InventoryItemChoiceUI.AddEquipItemUDB(prov, res,
                            //     selBeing, item.Data, T.VerbEquip);
                            addedAction = true;
                        }
                    }
                }
                // storing / hauling actions won't be added for units under DC
                if (canDirectControl == 1 && underDirectControl == 0) {
                    if (item.TryGetComponent<StorableObjComp>(out var storable)) {
                        // try order storing
                        choice.AddButton($"{T.HaulToStorage}: {item.Data.ListName}", () => {
                            if (!storable.HasCompatibleStorage()) {
                                S.Sig.ShowErrorMessage.Send(item.Definition.StorageUnavailableMessage(item));
                            }
                            if (!storable.TryOrderStoring(true, false, out var failReason)) {
                                if (failReason != null) {
                                    S.Sig.ShowErrorMessage.Send(failReason);
                                } else {
                                    UISounds.PlayActionDenied();
                                }
                                return;
                            } else {
                                // we a goal we need to assign
                                if (storable.TryGetGoal(out var g)) {
                                    if (!BeingUtils.TryAssignGoal(G.Ticks, g, ctx.SelectedBeings[0].AIAgent)) {
                                        S.Sig.ShowErrorMessage.Send(T.AdjIncapable);
                                        return;
                                    }
                                } else {
                                    S.Sig.ShowErrorMessage.Send(T.OperationFailed);
                                    return;
                                }
                            }
                            UISounds.PlayDigitalConfirm();

                        });
                        addedAction = true;
                    } else {
                        var hovPile = ctx.HoveredObj?.Pile;
                        if (hovPile != null && !hovPile.TreatAsObj) {
                            // try order storing
                            choice.AddButton($"{T.HaulToStorage}: {hovPile.Obj.Definition.NameT}", () => {
                                if (!hovPile.HasCompatibleStorage()) {
                                    S.Sig.ShowErrorMessage.Send(hovPile.Obj.Definition.StorageUnavailableMessage(hovPile.Obj));
                                }
                                if (!hovPile.TryOrderStore()) {
                                    UISounds.PlayActionDenied();
                                }
                            });
                        }
                    }
                }
            }
            return addedAction;
        }

        private void OnSelectEntity(Entity entity) {
            // Clearing alone sends no selection signal, the panel and the bar would keep
            // the old selection
            if (entity == null) {
                ctx.DeselectAny();
                return;
            }
            ctx.ClearSelectedObjs();
            if (entity is Obj o) {
                SelectObj(o, true);
            }
        }
        private void OnSelectEntityUnfocused(Entity entity) {
            if (entity == null) {
                ctx.DeselectAny();
                return;
            }
            ctx.ClearSelectedObjs();
            if (entity is Obj o) {
                SelectObj(o, false);
            }
        }

        private void AddSelectedObj(Obj obj) {
            if (ctx.IsSelectionFull()) { return; }
            if (!ctx.SelectedObjs.Contains(obj)) {
                ctx.SelectedObjs.Add(obj);
                obj.Transmit(null, MsgIdH.EntitySelected);
            }
            ctx.SelectionChanged = true;
        }

        private void SelectObj(Obj obj, bool focusCamera) {
            ctx.ClearVFX();
            AddSelectedObj(obj);
            S.Sig.ObjSelected.Send(ctx.SelectedObjs);
            if (focusCamera) {
                EntityUtils.CameraFocusOn(obj);
            }
        }
        public override void OnHover(Vector2 pos) {
            // this is bad because we may have an object in mid tile or multiple objects in a tile
            // if (ctx.HoveredPosChanged) {
            // ctx.HoveredObj = S.Objs.FindFirstInPos(EntityUtils.ToPos(pos));
            // }
            ctx.HoveredObj = S.Objs.FindClosestVisual(pos, boundsSize: 1f);
            if (!S.Objs.ByPos.TryGetValue(ctx.HoveredPos, out var objs)) {
                return;
            }
            if (objs.Count <= 0) { return; }

            StringBuilder sb = null;
            Caches.NamedCounts.Clear();
            for (int i = objs.Count - 1; i >= 0; i--) {
                Obj obj = objs[i];
                if (!obj.IsActive) {
                    D.Err("Inactive object in ByPos! {0}: {1}", obj, pos);
                    continue;
                }
                var name = obj.Definition.CodexLink;
                if (!Caches.NamedCounts.TryGetValue(name, out var cnt)) {
                    cnt = 0;
                }

                var pile = obj.Pile;
                if (pile != null) {
                    cnt += pile.StackSize;
                } else {
                    cnt++;
                }
                Caches.NamedCounts[name] = cnt;
            }

            int total = Caches.NamedCounts.Count;
            if (Caches.NamedCounts.Count > 0) {
                int i = 0;
                foreach (var pileCnt in Caches.NamedCounts.OrderBy(pc => pc.Key)) {
                    if (sb == null) {
                        sb = ctx.BeginTooltipBlock();
                        sb.Append(Texts.SubTitle(T.NounObject));
                    } else {
                        sb.AppendLine();
                    }
                    sb.AppendFormat("{0} {1}", pileCnt.Key, Units.XNum(pileCnt.Value));
                    if (++i == 10) {
                        break;
                    }
                }
                if (i < total) {
                    sb.AppendFormat(Texts.Grey("\n+{0}"), total - i);
                }
            }
        }

        public override void OnClickUp(Vector2 startPos, Vector2 pos, float dragDistance) {
            // Ctrl and shift are tile and being selection modes
            if (ctx.IsCtrlPressed || ctx.IsShiftPressed) {
                ctx.ClearSelectedObjs();
                return;
            }
            if (ctx.SelectedBeings.Count > 0) {
                if (ctx.IsAltPressed) {
                    ctx.ClearSelectedBeings();
                } else {
                    if (ctx.IsDragging || ctx.SelectionChanged) {
                        ctx.ClearSelectedObjs();
                        return;
                    }
                    if (S.Objs.FindClosestVisual(pos, boundsSize: 2) == null) {
                        ctx.ClearSelectedObjs();
                        return;
                    }
                    if (TrySelectObjectLayerTile(pos)) {
                        return;
                    }
                    ctx.ClearSelectedBeings();
                }
            }
            if (ctx.IsAltPressed && ctx.SelectedTiles.Count > 0) {
                ctx.ClearSelectedTiles();
            }
            if (ctx.IsDragging) {
                if (!ctx.IsAltPressed) {
                    // if alt is pressed we will be adding to the selection
                    ctx.ClearSelectedObjs();
                }
                Caches.ObjList.Clear();
                var found = S.Objs.FindVisualInRect(startPos, pos, Caches.ObjList, null);
                Caches.EntityProxyList.Clear();
                var numProxies = S.Query.GetEntityProxiesInRect.Ask(startPos, pos, Caches.EntityProxyList);
                if (numProxies > 0) {
                    found += numProxies;
                    foreach (var pr in Caches.EntityProxyList) {
                        if (pr.Entity is Obj obj) {
                            Caches.ObjList.Add(obj);
                        }
                    }
                }
                if (found > 0) {
                    foreach (var o in Caches.ObjList) {
                        if (ctx.IsSelectionFull()) { break; }
                        AddSelectedObj(o);
                    }
                }
            } else {
                // figure out the bounds. if there are mega asteroids, we need to increase bounds
                float bounds = 10;
                var asteroids = S.Components.AllByType<AsteroidComp>();
                if (!Arrays.IsEmpty(asteroids)) {
                    foreach (var a in asteroids) {
                        bounds = Mathf.Max(bounds, a.Entity.SelectableSize.x);
                    }
                }

                using (D.Timer("ObjQuery", 0.0001f)) {
                    // Unbuilt tiles take priority: skip obj so tile handler runs first.
                    // If a tile is already selected, fall through to cycle tile → obj.
                    if (ctx.HoveredTile?.IsConstructed == false && ctx.SelectedTiles.Count == 0) {
                        ctx.ClearSelectedObjs();
                        return;
                    }
                    int posIdx = EntityUtils.ToPos(pos);
                    if (ctx.ClicksInCurrentPosIdx > 0
                            && S.Grid.IsWithinBounds(posIdx)
                            && FreeSelectTileHandler.CanCycleToSelectableFloor(ctx, posIdx)) {
                        return;
                    }
                    Obj onlySelectedBefore = ctx.SelectedObjs.Count == 1 ? ctx.SelectedObjs[0] : null;
                    if (onlySelectedBefore != null && ctx.IsHoveringSelectableTile && ctx.ClicksInCurrentPosIdx % 2 == 0) {
                        // allow to select the tile
                        ctx.ClearSelectedObjs();
                        return;
                    }
                    var obj = S.Objs.FindClosestVisual(pos, onlySelectedBefore, boundsSize: bounds);
                    if (obj == null && !ctx.IsHoveringSelectableTile && ctx.HoveredBeing == null) {
                        // possibly keep last selected obj
                        obj = S.Objs.FindClosestVisual(pos, boundsSize: bounds);
                    }

                    if (obj != null && obj.TryGetComponent<BreachCapsuleFlyingComp>(out var bcFlying)) {
                        // attached breach capsules won't be selectable;
                        if (bcFlying.IsAttached) {
                            obj = null;
                        }
                    }

                    // toggle last obj
                    if (obj == null && ctx.SelectedObjs.Count == 1) {
                        ctx.ClearSelectedObjs();
                    }
                    TrySelectObj(obj);
                }
            }
        }

        private bool TrySelectObjectLayerTile(Vector2 pos) {
            int posIdx = EntityUtils.ToPos(pos);
            if (!S.Grid.IsWithinBounds(posIdx)) {
                return false;
            }
            var tile = EntityUtils.GetTopmostParentTile(posIdx);
            if (tile?.IsActive != true || tile.Definition.LayerId != WorldLayer.Objects) {
                return false;
            }
            ctx.ClearSelectedBeings();
            ctx.ClearSelectedObjs();
            if (!FreeSelectTileHandler.QueryTiles(ctx, pos)) {
                return false;
            }
            ctx.ClickUpConsumed = true;
            return true;
        }

        private bool TrySelectObj(Obj obj) {
            if (obj == null) { return false; }
            // ToolConnect.ClearShownTexts();
            ctx.ClearSelectedBeings();
            ctx.ClearSelectedTiles();
            if (ctx.IsAltPressed) {
                // toggle if alt is pressed
                if (!ctx.DeselectObj(obj)) {
                    AddSelectedObj(obj);
                }
            } else {
                // add if alt is not pressed
                ctx.ClearSelectedObjs();
                AddSelectedObj(obj);
            }
            return true;
        }
        public override void OnDoubleClick(Vector2 pos) {
            if (ctx.DoubleClicked || ctx.IsCtrlPressed || ctx.IsAltPressed || ctx.IsShiftPressed) {
                return;
            }

            if (ctx.SelectedCount != 1) { return; }
            if (ctx.HoveredObj == null) { return; }

            // if (!ctx.IsExclusivelySelected(ctx.HoveredObj)) { return; }

            var def = ctx.HoveredObj.Definition;
            ctx.ClearSelectedObjs();

            foreach (var o in S.Objs.All) {
                if (ctx.IsSelectionFull()) { break; }
                if (o.Definition == def) {
                    if (RenderUtils.IsVisible(o.Position)) {
                        AddSelectedObj(o);
                    }
                }
            }
            S.Sig.ObjSelected.Send(ctx.SelectedObjs);
            ctx.DoubleClicked = true;
        }
    }
}
