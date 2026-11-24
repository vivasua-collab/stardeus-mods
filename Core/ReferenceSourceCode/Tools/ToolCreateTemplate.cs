using Game.Constants;
using Game.Data;
using KL.Grid;
using UnityEngine;
using System.Collections.Generic;
using KL.Utils;
using Game.Utils;
using Game.CodeGen;
using Game.Components;
using Game.UI;
using Game.Systems.UIUX;
using System;

namespace Game.Tools {
    public sealed class ToolCreateTemplate : TileTool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolCreateTemplate());
        }
        public override string MenuIcon => IconId.WOverlayArea;
        public const string ToolId = "create_template";
        public override string Id => ToolId;

        public override bool IsConfigurable => false;
        public override bool IsDevTool => true;
        private Vector2 lastDragStart;
        private Vector2 lastDragEnd;

        public override void OnHover(Vector2 coords) {
            isHoverHidden = false;
            if (isDragging) {
                OnHoverDrag();
                return;
            }

            var pos = EntityUtils.ToPosSafe(coords);
            if (pos != lastHoverPos) {
                var topmost = EntityUtils.GetTopmostTile(pos);
                ToolGrid.TrySet(lastHoverPos, null);
                if (topmost != null) {
                    ShowGreenTile(pos);
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
            HandleDragRect(rect, Consts.GreenRectColor);
        }

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            if (!isDragging) { return; }

            var rekt = DragRect(startPos, position);
            if (rekt.Width > 16 || rekt.Height > 16) {
                WPopup.Create(false, IconId.WOverlayArea, T.CannotCreateTemplate, T.TemplateTooBig).Spawn();
                return;
            }

            // Detect if we are grabbing outside or inside!
            bool isOutside = false;
            foreach (var p in dragPositions) {
                var t = EntityUtils.GetTopmostTile(p);
                if (t == null || t.Constructable == null) { continue; }
                if (t.Definition.LayerId == WorldLayer.Objects &&
                        t.Constructable.IsBuiltInSpace) {
                    isOutside = true;
                    break;
                }
            }

            var dp = new List<int>(dragPositions);

            Action<string, int> dump = (name, offset) => {
                var rootPos = rekt.Min;
                var defs = new List<string>();
                var positions = new List<Vector2>();
                var rotations = new List<string>();
                if (isOutside) {
                    foreach (var pos in dp) {
                        var floor = EntityUtils.FloorAt(pos);
                        var wall = EntityUtils.WallAt(pos);
                        var obj = EntityUtils.ObjAt(pos);
                        Store(floor, rootPos, offset, defs, positions, rotations);
                        Store(wall, rootPos, offset, defs, positions, rotations);
                        Store(obj, rootPos, offset, defs, positions, rotations);
                    }
                } else {
                    foreach (var pos in dp) {
                        var tile = EntityUtils.GetTopmostTile(pos);
                        if (tile == null) { continue; }
                        if (tile.Transform.IsChild) { continue; }
                        if (tile.Definition.LayerId == WorldLayer.Floor &&
                                !tile.Constructable.CanReplaceInSameLayer) {
                            continue;
                        }
                        Store(tile, rootPos, offset, defs, positions, rotations);
                    }
                }
                var def = new Def();
                def.SkipToolListing = true;
                def.Layer = "Objects";
                var compConfig = new List<ComponentConfig> {
                    new() {
                        Component = "TileTransform",
                        Properties = new [] {
                            new SerializableProperty {
                                Key = "Width",
                                Int = rekt.Width,
                            },
                            new SerializableProperty {
                                Key = "Height",
                                Int = rekt.Height - System.Math.Abs(offset),
                            },
                        }
                    },
                    new() { Component = "Passability", },
                    new() {
                        Component = "TileGraphics",
                        Properties = new [] {
                            new SerializableProperty {
                                Key = "Graphic",
                                String = "Icons/White/Square",
                            },
                        }
                    },
                    new() {
                        Component = "Template",
                        Properties = new [] {
                            new SerializableProperty {
                                Key = "PlaceInTheMiddle",
                                Bool = false,
                            },
                            new SerializableProperty {
                                Key = "Chance",
                                Float = 0.8f,
                            },
                            new SerializableProperty {
                                Key = "MinInstances",
                                Int = 1,
                            },
                            new SerializableProperty {
                                Key = "MaxInstances",
                                Int = 5,
                            },
                            new SerializableProperty {
                                Key = "Definitions",
                                StringSet = defs.ToArray(),
                            },
                            new SerializableProperty {
                                Key = "Positions",
                                Vector2Set = positions.ToArray(),
                            },
                            new SerializableProperty {
                                Key = "Rotations",
                                StringSet = rotations.ToArray(),
                            },
                        }
                    },
                };
                if (isOutside) {
                    compConfig.Add(
                        new() {
                            Component = "Constructable",
                            Properties = new [] {
                                new SerializableProperty {
                                    Key = "Placement",
                                    String = "InSpace",
                                },
                            }
                        }
                    );
                }
                def.Components = compConfig.ToArray();
                    var tpl = Json.Dump(def, true);
                    var res = Persist.Write(Application.streamingAssetsPath,
                        $"/Mods/Core/Definitions/Templates/{name}.json",
                        tpl, false);
                    // D.Log("Written template: {0}", res);

            };

            var qc = QuickChoice.Create(T.ToolCreate_Template);
            qc.AddInput(T.EnterName, new QuickChoice.Input() {
                OnSubmit = (name) => {
                    if (isOutside) {
                        AskForOffsetAndThen(dump, name);
                    } else {
                        dump(name, 0);
                    }
                }
            });
            qc.Show();

            ClearDragPositions();
            isDragging = false;
        }

        private void AskForOffsetAndThen(Action<string, int> dump, string name) {
            var qc = QuickChoice.Create(T.ToolCreate_Template);
            qc.WithLabel("Negative if we want to dig down, positive if dig up");
            qc.AddInput("Enter offset", new QuickChoice.Input() {
                Placeholder = "0",
                OnSubmit = (off) => {
                    var offset = Convert.ToInt32(off);
                    dump(name, offset);
                }
            });
            qc.Show();
        }

        private void Store(Tile tile, int rootPos, int offset, List<string> defs,
                List<Vector2> positions, List<string> rotations) {
            // Because if offset is positive, we reduce the size of template
            // height, and it will automatically eat into top with 0 offset
            if (offset > 0) { offset = 0; }
            if (tile == null) { return; }
            if (tile.Transform.IsChild) { return; }
            defs.Add(tile.Definition.Id);

            Pos.ToXY(tile.PosIdx, out var x, out var y);
            Pos.ToXY(rootPos, out var rx, out var ry);
            var shifted = Pos.FromXY(x - rx, y - (ry - offset));
            positions.Add(EntityUtils.ToVector2(shifted));
            rotations.Add(Facing.ConvertRotation(
                tile.Transform.Rotation).ToString());
        }

        public override string ToString() {
            return Id;
        }
    }
}
