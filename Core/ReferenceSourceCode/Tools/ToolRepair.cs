using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using System;
namespace Game.Tools {
    public sealed class ToolRepair : TileTool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolRepair());
        }
        public const string ToolId = "repair";
        public override string Id => ToolId;
        public override int Cursor => CursorTypeH.CursorWrench;
        public override string MenuIcon => IconId.WWrench;
        public override bool IsConfigurable => false;
        public override bool IsDevTool => false;
        public override ActionType Shortcut => ActionType.ShortcutRepairTiles;
        public override string[] ShortcutKeys => new[] { "<Keyboard>/r" };
        private bool instant => A.InstantBuild;

        private new Vector2 dragStart;
        private new Vector2 dragEnd;
        private int repairPos = Pos.Invalid;
        private bool isShowingTooltip;

        // The damage marked around the cursor goes too
        protected override void HideHover() {
            base.HideHover();
            markedTiles.Clear();
            repairPos = Pos.Invalid;
        }

        public override void OnHover(Vector2 coords) {
            isHoverHidden = false;
            if (isDragging) {
                if (isShowingTooltip) {
                    isShowingTooltip = false;
                    The.SysSig.HideTooltip.Send();
                }
                OnHoverDrag();
                return;
            }

            var pos = EntityUtils.ToPosSafe(coords);

            if (HasDamageAt(pos)) {
                ShowGreenTile(pos);
            } else {
                ClearPreview();
            }

            if (pos != lastHoverPos || repairPos == Pos.Invalid) {
                if (isShowingTooltip) {
                    The.SysSig.HideTooltip.Send();
                    isShowingTooltip = false;
                }
                repairPos = pos;
                toRepair.Clear();
                FindDamagedTiles(pos, toRepair);
                UpdateMarkedTiles(pos, 30);
            }
            lastHoverPos = pos;
        }

        protected override bool IsTileMarked(int pos) {
            return HasDamageAt(pos);
        }

        public bool HasDamageAt(int pos) {
            if (!EntityUtils.IsWithinBounds(pos)) { return false; }
            var floor = EntityUtils.FloorAt(pos);
            if (EntityUtils.IsPresent(floor) && floor.Health != null) {
                if (floor.Health.IsDamaged) { return true; }
            }
            var wall = EntityUtils.WallAt(pos);
            if (EntityUtils.IsPresent(wall) && wall.Health != null) {
                if (wall.Health.IsDamaged) { return true; }
            }
            var obj = EntityUtils.ParentObjAt(pos);
            if (EntityUtils.IsPresent(obj) && obj.Health != null) {
                if (obj.Health.IsDamaged) { return true; }
            }
            return false;
        }

        private void FindDamagedTiles(int pos, List<Tile> result) {
            var floor = EntityUtils.FloorAt(pos);
            if (EntityUtils.IsPresent(floor) && floor.Health != null) {
                if (floor.Health.IsDamaged) {
                    if (!floor.Health.HasGoal) {
                        result.Add(floor);
                    }
                }
            }
            var wall = EntityUtils.WallAt(pos);
            if (EntityUtils.IsPresent(wall) && wall.Health != null) {
                if (wall.Health.IsDamaged) {
                    if (!wall.Health.HasGoal) {
                        result.Add(wall);
                    }
                }
            }
            var obj = EntityUtils.ParentObjAt(pos);
            if (EntityUtils.IsPresent(obj) && obj.Health != null) {
                if (obj.Health.IsDamaged) {
                    if (!obj.Health.HasGoal) {
                        result.Add(obj);
                    }
                }
            }
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dist) {
            if (!wasClickedDown) { return; }
            isDragging = true;
            dragStart = start;
            dragEnd = end;
            var rect = DragRect(start, end);
            HandleDragRect(rect, Consts.YellowRectColor);
        }

        public override bool Cancel() {
            repairPos = Pos.Invalid;
            return false;
        }

        private void OnRepaired(Tile tile) {
            if (RemoveMarkedTile(tile.PosIdx)) {
                repairPos = Pos.Invalid;
            }
        }

        public override void Activate() {
            base.Activate();
            S.Sig.TileRepaired.AddListener(OnRepaired);
        }

        public override void Deactivate() {
            if (isShowingTooltip) {
                isShowingTooltip = false;
                The.SysSig.HideTooltip.Send();
            }
            lastHoverPos = Pos.Invalid;
            repairPos = Pos.Invalid;
            S.Sig?.TileRepaired.RemoveListener(OnRepaired);
            base.Deactivate();
        }
        private readonly List<Tile> toRepair = new();

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            if (!wasClickedDown) { return; }
            toRepair.Clear();
            if (isDragging) {
                foreach (var pos in dragPositions) {
                    FindDamagedTiles(pos, toRepair);
                }
            } else {
                FindDamagedTiles(lastHoverPos, toRepair);
                lastHoverPos = Pos.Invalid;
            }
            if (toRepair.Count > 0) {
                if (toRepair.Count == 1) {
                    EnqueueRepair(toRepair[0]);
                } else {
                    foreach (var damaged in toRepair) {
                        EnqueueRepair(damaged);
                    }
                    Sounds.PlayConstructionOrder(The.Bindings.Mouse.WorldPosition);
                }
            }
            toRepair.Clear();
            ClearDragPositions();
            isDragging = false;
        }

        private void EnqueueRepair(Tile tile) {
            // D.Log("Will repair: {0}", tile);
            S.CmdQ.Enqueue(new CmdRepairTile(tile, instant));
        }

        public override string ToString() {
            return Id;
        }
    }
}
