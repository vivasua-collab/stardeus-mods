using Game.Constants;
using Game.Commands;
using Game.Data;
using KL.Grid;
using UnityEngine;
using System.Collections.Generic;
using Game.Rendering;
using KL.Randomness;
using KL.Utils;
using Game.Utils;
using Game.CodeGen;
using Game.Systems;

namespace Game.Tools {
    public sealed class ToolDamageTiles : TileTool, IModifyTool {
        private const int damageSeed = 1231515;
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolDamageTiles());
        }
        public bool CanBuild => false;
        public bool CanRemove => true;
        public override string MenuIcon => IconId.WDamage;

        public const string ToolId = "damage_tiles";
        public override string Id => ToolId;

        public override bool IsConfigurable => true;

        private string mode = "square";

        private readonly List<string> availableParams = new() {
            "square",
            "line",
        };
        public override string MenuNameFor(string param) {
            return string.Format("tool.deal_damage.{0}", param);
        }
        public override string MenuIconFor(string param) {
            switch (param) {
                case "square" : return IconId.WSquare;
                case "line" : return IconId.WLine;
                default: return $"damage_tiles_{param}";
            }
        }

        public override List<string> AvailableParams => availableParams;
        public override bool IsDevTool => true;

        private Tile tileToDamage;
        private Vector2 lastDragStart;
        private Vector2 lastDragEnd;

        public override bool Configure(string param) {
            mode = param;
            return true;
        }

        public override void OnHover(Vector2 coords) {
            isHoverHidden = false;
            if (isDragging) {
                OnHoverDrag();
                return;
            }

            var pos = EntityUtils.ToPosSafe(coords);
            if (pos != lastHoverPos || tileToDamage == null) {
                tileToDamage = EntityUtils.GetTopmostTile(pos);
                ToolGrid.TrySet(lastHoverPos, null);
                if (tileToDamage != null) {
                    ShowRedTile(pos);
                } else {
                    ClearPreview();
                }
            }
            lastHoverPos = pos;
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dragDistance) {
            isDragging = true;
            var rect = DragRect(start, end);
            lastDragStart = start;
            lastDragEnd = end;
            HandleDragRect(rect, Consts.RedRectColor);
        }


        public override bool Cancel() {
            tileToDamage = null;
            return false;
        }

        public override void Deactivate() {
            tileToDamage = null;
            base.Deactivate();
        }

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            if (isDragging) {
                var rng = new Rng(damageSeed);
                foreach (var pos in dragPositions) {
                    float dist = 0f;
                    float range = 1f;
                    if (mode == "line") {
                        var ray = new Ray(startPos, position - startPos);
                        dist = Vector3.Cross(ray.direction,
                            (Vector3) EntityUtils.ToVector2(pos) - ray.origin)
                                .magnitude;
                        range = rng.Range(1.5f, 2.5f);
                        if (dist > range) { continue; }
                        // filter out the dragPositions that are not on the line
                        // between startPos and position
                    }
                    var tile = EntityUtils.GetTopmostTile(pos);
                    if (tile == null) { continue; }
                    EnqueueDamage(pos, range - dist);
                }
            } else {
                if (tileToDamage != null) {
                    var removalPos = lastHoverPos;
                    if (tileToDamage.Transform.IsChild) {
                        removalPos = tileToDamage.Transform.ParentPos;
                        tileToDamage = tileToDamage.Transform.Parent;
                    }
                    EnqueueDamage(removalPos);
                    lastHoverPos = Pos.Invalid;
                }
            }
            ClearDragPositions();
            isDragging = false;
        }

        public override void UpdateVFX(ParticlesSys ps) {
            if (!isDragging) {
                ps.SelectionVFX.MarkedTiles.Stop();
                return;
            }
            int count = 0;
            if (mode == "square") {
                D.Err("TODO");
                //count = AddDragRectParticles(ps.SelectionVFX.MarkedTiles, count);
                return;
            }
            var rng = new Rng(damageSeed); // random static seed
            var ray = new Ray(lastDragStart, lastDragEnd - lastDragStart);

            lastRect.ForEach((posIdx) => {
                var pos = EntityUtils.CenterOf(posIdx);
                var dist = Vectors.DistanceToRay(ray, pos);
                var range = rng.Range(1.5f, 2.5f);
                if (dist <= range) {
                    count = ps.SelectionVFX.MarkedTiles.AddTile(posIdx,
                        Colors.ToF4(rectColor), RenderUtils.TilePulse, count);
                }
            });
            ps.SelectionVFX.MarkedTiles.Show(count);
        }

        private void EnqueueDamage(int pos, float amount = 1) {
            // Data layers are floor, walls and objects.
            foreach (var layer in RenderLayers.DataLayerIds) {
                Command cmd;
                if (!EntityUtils.IsConstructedTileAt(pos, layer)) {
                    cmd = new CmdRemoveTile(pos, layer);
                } else {
                    cmd = new CmdDealDamage(pos, layer,
                        Rng.URange(150, 240) * amount, DamageType.Explosive);
                }
                S.CmdQ.Enqueue(cmd);
            }
        }

        public override string ToString() {
            return Id;
        }
    }
}
