using Game.Constants;
using Game.Commands;
using Game.Data;
using KL.Grid;
using UnityEngine;
using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using Game.UI;
using KL.Utils;
using Game.Input;
using Game.Components;
using Game.Systems;
using Game.Systems.UIUX;
using Game.Rendering;
using System;
using Game.Systems.Suggestions;
using Game.Systems.AI;
using KL.Collections;

namespace Game.Tools {
    public sealed class ToolRemoveTiles : TileTool, IModifyTool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolRemoveTiles());
        }
        public override string MenuIcon => IconId.WCross;
        public bool CanBuild => false;
        public bool CanRemove => true;
        public Func<int, bool> Prevalidate;

        public const string ToolId = "remove_tiles";
        public override string Id => ToolId;

        public override bool IsConfigurable => false;
        public override ActionType Shortcut => ActionType.ShortcutRemoveTiles;
        public override string[] ShortcutKeys => new[] { "<Keyboard>/x" };

        private bool instant => A.InstantBuild;

        private Tile tileToRemove;
        public override int Cursor => CursorTypeH.CursorCross;

        public override void AddParticles(Dictionary<int, XParticles> x) {
            if (isHoverHidden) { return; }
            var ps = GetMouseRectParticles(x);
            var psc = AddModeParticles(ps, 0);
            //offset = DrawGuidelines(ps.Particles, offset, dragEnd);
            //if (isDragging) {
                //offset = DrawGuidelines(ps.Particles, offset, dragStart);
            //}
            ps.ShowParticles(psc, false);
        }

        protected override bool WallOrFloorCheck(int idx) {
            if (!EntityUtils.IsWithinBounds(idx)) {
                return false;
            }
            return S.Map.Grids[WorldLayer.Floor].Data[idx] == null ||
                S.Map.Grids[WorldLayer.Walls].Data[idx] != null ||
                S.Map.Grids[WorldLayer.Objects].Data[idx] != null;
        }

        private int AddModeParticles(XParticles x, int offset) {
            var p = x.Particles;
            offset = DrawGuidelines(p, offset, dragEnd);
            if (isInBucketMode && bucketFill.Count > 0) {
                DrawBucketFill((pos) => {
                    if (p.Length > offset) {
                        p[offset++] = ParticlesSys.ParticleAt(Pos.ToVector(pos),
                            rectColor, RenderUtils.TilePulse, 0.1f);
                    } else {
                        offset++;
                    }
                });
            } else if (isDragging) {
                offset = DrawGuidelines(p, offset, dragStart);
                if (isInCircleMode) {
                    DrawCircle((pos) => {
                        offset = DrawCell(p, offset, pos);
                    });
                } else if (isInLineMode) {
                    DrawLine((pos) => {
                        offset = DrawCell(p, offset, pos);
                    });
                } else {
                    offset = AddRectParticles(0, x);
                }
            }
            return offset;

        }
        private int DrawCell(ParticleSystem.Particle[] p, int offset, int pos) {
            if (p.Length > offset) {
                p[offset++] = ParticlesSys.ParticleAt(Pos.ToVector(pos),
                    rectColor,
                    RenderUtils.TilePulse, 0.1f);
            } else {
                offset++;
            }
            return offset;
        }

        public override void OnHover(Vector2 coords) {
            if (!EntityUtils.IsWithinBounds(coords)) { return; }
            isHoverHidden = false;
            OnHoverCommon(coords);
            if (isDragging) {
                CheckDragModes();
                OnHoverDrag();
                return;
            }

            var pos = EntityUtils.ToPosSafe(coords);
            dragEnd = pos;
            CheckNonDragModes(pos);
            if (pos != lastHoverPos) {
                tileToRemove = EntityUtils.GetTopmostParentTile(pos);
                ShowRedTile(pos);
            }
            lastHoverPos = pos;
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dragDistance) {
            if (!wasClickedDown) { return; }
            OnDragCommon(start, end);

            if (isInCircleMode) {
                return;
            } else if (isInLineMode) {
                drawRect = false;
                return;
            }
            if (isInPencilMode) {
                if (lastPencilTile == Pos.Invalid) {
                    RemoveAt(dragEnd);
                } else if (lastPencilTile != dragEnd) {
                    DrawLineCustom(EntityUtils.CenterOf(lastPencilTile),
                        end, RemoveAt);
                }
                S.Sig.UpdatePlan.Send();
                lastPencilTile = dragEnd;
                return;
            }

            var rect = DragRect(start, end);
            HandleDragRect(rect, Consts.RedRectColor);
        }

        private void RemoveAt(int pos) {
            var o = EntityUtils.ParentObjAt(pos);
            if (o != null) {
                EnqueueRemoval(o);
                return;
            }
            var w = EntityUtils.WallAt(pos);
            if (w != null) {
                EnqueueRemoval(w);
            }
            var f = EntityUtils.FloorAt(pos);
            if (f != null) {
                EnqueueRemoval(f);
            }
        }

        private void CollectRemovalAt(int pos, HashSet<Tile> toRemove) {
            var o = EntityUtils.ParentObjAt(pos);
            if (o != null) {
                toRemove.Add(o);
                return;
            }
            var w = EntityUtils.WallAt(pos);
            if (w != null) {
                toRemove.Add(w);
            }
            var f = EntityUtils.FloorAt(pos);
            if (f != null) {
                toRemove.Add(f);
            }
        }

        public override bool Cancel() {
            tileToRemove = null;
            return false;
        }

        public override void Deactivate() {
            lastHoverPos = Pos.Invalid;
            tileToRemove = null;
            base.Deactivate();
        }

        public void RemoveAll(HashSet<Tile> toRemove) {
            if (instant) {
                foreach (var tile in toRemove) { EnqueueRemoval(tile); }
            } else {
                List<Tile> toDeconstruct = null;
                foreach (var tile in toRemove) {
                    if (CanBatchOrderDeconstruct(tile)) {
                        toDeconstruct ??= new();
                        toDeconstruct.Add(tile);
                    } else {
                        EnqueueRemoval(tile);
                    }
                }
                if (toDeconstruct?.Count > 0) {
                    S.CmdQ.Enqueue(new CmdOrderDeconstructTiles(toDeconstruct));
                }
            }
            Sounds.PlayConstructionOrder(The.Bindings.Mouse.WorldPosition);
        }

        private bool CanBatchOrderDeconstruct(Tile t) {
            if (t == null || !t.IsActive || !t.IsConstructed) {
                return false;
            }
            var c = t.Constructable;
            if (c == null) {
                return false;
            }
            if (c.HasGoal) {
                if (t.Goal.JobIdH != JobTypeIdH.Hauling
                        && t.Goal.JobIdH != JobTypeIdH.Construction) {
                    return false;
                }
            }
            return true;
        }

        private void ShowDeconstructOptions(HashSet<Tile> toRemove,
                WPopupData popupData) {
            var qc = QuickChoice.Create(T.Deconstruct);
            qc.Widget = popupData.Widget.Button1.Rect;
            qc.AddButton(T.All, () => {
                RemoveAll(toRemove);
                CloseDeconstructPopup(popupData);
            });
            qc.AddButton(T.AllExceptObjects, () => {
                RemoveAllExceptObjects(toRemove);
                CloseDeconstructPopup(popupData);
            });
            qc.Show();
        }

        private static void CloseDeconstructPopup(WPopupData popupData) {
            var w = popupData.Widget;
            if (w != null && w.gameObject.activeSelf) {
                w.Close();
            }
        }

        private void RemoveAllExceptObjects(HashSet<Tile> toRemove) {
            var filtered = new HashSet<Tile>();
            foreach (var tile in toRemove) {
                if (tile == null || !tile.IsActive) { continue; }
                if (tile.Definition.LayerId == WorldLayer.Objects) { continue; }
                if (tile.Definition.LayerId == WorldLayer.Floor
                        && EntityUtils.ParentObjAt(tile.PosIdx) != null) {
                    continue;
                }
                filtered.Add(tile);
            }
            PreserveSpaceObjectSupports(filtered);
            RemoveAll(filtered);
        }

        private static void PreserveSpaceObjectSupports(HashSet<Tile> filtered) {
            HashSet<Tile> spaceObjects = null;
            foreach (var floor in filtered) {
                if (floor.Definition.LayerId != WorldLayer.Floor) { continue; }
                var p = floor.PosIdx;
                spaceObjects = CollectSpaceAttached(Pos.N(p), spaceObjects);
                spaceObjects = CollectSpaceAttached(Pos.S(p), spaceObjects);
                spaceObjects = CollectSpaceAttached(Pos.E(p), spaceObjects);
                spaceObjects = CollectSpaceAttached(Pos.W(p), spaceObjects);
            }
            if (spaceObjects == null) { return; }
            foreach (var o in spaceObjects) {
                var outline = o.Transform.OutlineNonDiag;
                Tile support = null;
                foreach (var op in outline) {
                    if (Pos.IsOOB(op)) { continue; }
                    var f = EntityUtils.FloorAt(op);
                    if (f == null || !f.IsActive) { continue; }
                    if (!filtered.Contains(f)) {
                        support = null;
                        break;
                    }
                    if (support == null || op < support.PosIdx) {
                        support = f;
                    }
                }
                if (support != null) {
                    filtered.Remove(support);
                }
            }
        }

        private static HashSet<Tile> CollectSpaceAttached(int pos,
                HashSet<Tile> set) {
            if (Pos.IsOOB(pos)) { return set; }
            var o = EntityUtils.ParentObjAt(pos);
            if (o?.IsActive != true || o.Constructable == null) {
                return set;
            }
            var placement = o.Constructable.Placement;
            if (placement != PlacementConstraint.InSpaceAttached
                    && placement != PlacementConstraint.InSpaceAttachedChained) {
                return set;
            }
            set ??= new HashSet<Tile>();
            set.Add(o);
            return set;
        }

        public override void OnClickDown(Vector2 pos) {
            if (Prevalidate?.Invoke(EntityUtils.ToPos(pos)) == false) {
                wasClickedDown = false;
                UISounds.PlayActionDenied();
                return;
            }
            wasClickedDown = true;
        }

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            if (!wasClickedDown) { return; }
            if (isInPencilMode) {
                lastPencilTile = Pos.Invalid;
            }
            if (isInBucketMode) {
                var toRemove = new HashSet<Tile>();
                for (int i = 0; i < bucketFill.Count; i++) {
                    int p = bucketFill[i];
                    CollectRemovalAt(p, toRemove);
                }
                bucketFill.Clear();
                RemoveAll(toRemove);
            } else if (isDragging) {

                if (Prevalidate != null) {
                    var pos = EntityUtils.ToPos(position);
                    if (!Prevalidate(pos)) {
                        UISounds.PlayActionDenied();
                        ClearDragPositions();
                        isDragging = false;
                        return;
                    }
                }

                if (isInPencilMode) {
                } else if (isInCircleMode) {
                    dragInfoText?.SetText(null);
                    var toRemove = new HashSet<Tile>();
                    DrawCircle((pos) => {
                        CollectRemovalAt(pos, toRemove);
                    });
                    RemoveAll(toRemove);
                } else if (isInLineMode) {
                    var toRemove = new HashSet<Tile>();
                    DrawLine((pos) => {
                        CollectRemovalAt(pos, toRemove);
                    });
                    RemoveAll(toRemove);
                } else if (dragPositions.Count > 1) {
                    var toRemove = new HashSet<Tile>();
                    int foundObjects = 0;
                    Caches.TileList.Clear();
                    // First iteration = check what layers we have touched
                    bool affectFloors = false;
                    for (int i = dragPositions.Count - 1; i >= 0; i--) {
                        int p = dragPositions[i];
                        var e = EntityUtils.GetTopmostParentTile(p);
                        if (e?.Definition.LayerId == WorldLayer.Floor) {
                            affectFloors = true;
                            break;
                        }
                    }
                    // Second iteration - order removal
                    for (int i = dragPositions.Count - 1; i >= 0; i--) {
                        int p = dragPositions[i];
                        foundObjects += RemoveAt(p, affectFloors, toRemove, Caches.TileList);
                    }
                    if (foundObjects > 0 && foundObjects != toRemove.Count) {
                        var sb = Caches.StringBuilder2;
                        sb.Clear();
                        sb.Append(T.ConfirmObjectRemovalInfo_V1.F(foundObjects));
                        int i = 0;
                        foreach (var obj in Caches.TileList) {
                            sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight, obj.NameLink);
                            if (++i == 20) { break; }
                        }
                        var left = foundObjects - i;
                        if (left > 0) {
                            sb.AppendFormat("\n +{0}", left);
                        }
                        var popupData = WPopup.Create(false, IconId.CDemolition,
                            T.ConfirmObjectRemoval, null, sb.ToString());
                        popupData.WithAction(T.Options,
                                () => ShowDeconstructOptions(toRemove, popupData))
                            .WithAction2(T.Cancel, null)
                            .WithKeepOpenOnButton1()
                            .Spawn();
                    } else {
                        RemoveAll(toRemove);
                    }
                }
            } else {
                if (tileToRemove != null) {
                    var tile = tileToRemove;
                    DeconstructConfirm.OrderOrConfirm(tile, () => EnqueueRemoval(tile));
                    lastHoverPos = Pos.Invalid;
                }
            }
            ClearDragPositions();
            isDragging = false;
        }

        private int RemoveAt(int p, bool affectFloors, HashSet<Tile> toRemove,
                List<Tile> foundObjects) {
            var foundCnt = 0;
            Tile floor = null;
            if (affectFloors) {
                floor = EntityUtils.FloorAt(p);
            }
            var e = EntityUtils.GetTopmostParentTile(p);
            if (e == null) {
                if (affectFloors && floor != null) {
                    e = floor;
                } else {
                    return foundCnt;
                }
            }
            if (e.Reinstallable?.IsPending == true
                    && e.IsConstructed) {
                if (affectFloors && e.Definition.LayerId > WorldLayer.Floor
                        && floor != null) {
                    toRemove.Add(floor);
                }
                return foundCnt;
            }
            if (e.Definition.LayerId == WorldLayer.Objects) {
                if (toRemove.Add(e)) {
                    if (e.IsConstructed) {
                        if (e.Goal?.JobIdH != JobTypeIdH.Demolition) {
                            foundCnt++;
                            foundObjects.Add(e);
                        }
                    }
                }
            } else {
                toRemove.Add(e);
            }
            if (affectFloors && e.Definition.LayerId > WorldLayer.Floor
                    && floor != null) {
                toRemove.Add(floor);
            }
            return foundCnt;
        }

        public void EnqueueRemoval(Tile t) {
            if (t == null || !t.IsActive) { return; }
            var c = t.Constructable;
            var r = t.Reinstallable;
            Command cmd;
            if (t.IsConstructed) {
                if (instant) {
                    cmd = new CmdRemoveTile(t);
                } else {
                    if (c.HasGoal) {
                        if (t.Goal.JobIdH != JobTypeIdH.Hauling && t.Goal.JobIdH
                                != JobTypeIdH.Construction) {
                            if (AIGoal.IsFailed(t.Goal.State)) {
                                if (!AIGoal.TryRetry(t.Goal, G.Ticks, true, true)) {
                                    D.Err("Failed retrying goal for removal of: {0}: {1}",
                                        t, t.Goal);
                                }
                            }
                            return;
                        }
                    }
                    cmd = new CmdOrderDeconstruct(t.PosIdx, t.Definition.LayerId);
                }
                S.CmdQ.Enqueue(cmd);
            } else if (t.Goal != null) {
                t.ClearGoal("Removing via remove tiles tool");
            } else if (r?.IsPending == true) {
                // It's the ghost
                AIGoal.Cancel(r.Entity.Goal, G.Ticks, "Ghost tile (tool remove tiles)!");
            } else if (c != null) {
                D.Err("Should never happen - constructable not constructed but no ad, {0}", t);
                c.BreakDown(1f);
            } else {
                D.Err("Tile has neither constructable not pending reinstallable, cannot remove! {0} c: {1} r: {2}", t, c, r);
            }
        }

        public override string ToString() {
            return Id;
        }
    }
}
