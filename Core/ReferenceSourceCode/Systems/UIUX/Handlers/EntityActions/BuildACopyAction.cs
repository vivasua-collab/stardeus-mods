using System;
using System.Collections.Generic;
using System.Text;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Research;
using Game.Tools;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class BuildACopyAction : BaseEntityAction {
        public const string ActionId = "BuildACopy";
        [SelfInit]
        private static void Register() {
            RegisterAction(new BuildACopyAction());
        }
        public override string Id => ActionId;
        public override string HotkeyLabel => T.BuildACopy;
        public override string HotkeyDefault => "<Keyboard>/q";
        // Same button as the build tool, which this shadows while a tile is selected
        public override string HotkeyDefaultGamepad => "<Gamepad>/select";

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            var prio = tile.Constructable.IsReachableForCommonAction ?
                Priority.High : Priority.Low;
            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, tile), prio);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            if (!BuildACopy(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt), Priority.High);
            return true;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Tile> tiles, int cnt) {
            if (cnt <= 0) {
                var disabled = SpawnButton(l, false, p, i);
                disabled.Load(IconId.CHammer,
                    Texts.Strike(T.BuildACopy), () => { });
                return disabled;
            }
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CHammer,
                T.BuildACopy, () => {
                    BuildACopy(tiles, false, out _);
                });
            return widget;
        }

        private WBase Create(Lifetime lifetime, RectTransform p, int i, Tile tile) {
            var available = tile.Constructable.IsReachableForCommonAction;
            var widget = SpawnButton(lifetime, available, p, i);
            var text = T.BuildACopy;
            if (!available) {
                text = Texts.Strike(text);
            }
            widget.Load(IconId.CHammer, text, () => {
                if (available) {
                    var tool = ToolPlaceTiles.StartBuilding(tile.DefinitionId);
                    if (tile.Transform.IsRotatable) {
                        tool.SetRotation(tile.Transform.Rotation);
                    }
                } else {
                    var sb = Caches.StringBuilder4;
                    sb.Clear();
                    AppendBuildError(sb, tile);
                    tile.S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {sb}");
                }
            });
            return widget;
        }
        public static bool BuildACopy(List<Tile> tiles, bool dryRun, out int cnt) {
            if (!ValidateLimit(tiles.Count, T.BuildACopy, dryRun)) {
                cnt = 0;
                return false;
            }
            var comps = new List<ConstructableComp>();
            StringBuilder sb = null;
            int countUnresearched = 0;
            int totalUnresearched = 0;
            var registeredUnresearched = Caches.IntSet;
            registeredUnresearched.Clear();
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (p.Constructable?.IsReachableForCommonAction == true) {
                    comps.Add(t.Constructable);
                } else if (!dryRun) {
                    if (sb == null) {
                        sb = Caches.StringBuilder2;
                        sb.Clear();
                        sb.Append(GlyphMap.BRHR);
                    }
                    if (registeredUnresearched.Add(t.Definition.IdH)) {
                        totalUnresearched++;
                        if (countUnresearched < 3) {
                            countUnresearched++;
                            AppendBuildError(sb, t);
                            sb.AppendLine();
                        }
                    }
                }
            }
            cnt = comps.Count;
            if (!dryRun) {
                ApplyBuildACopy(comps, countUnresearched, sb);
            }
            return true;
        }

        private static void AppendBuildError(StringBuilder sb, Tile t) {
            sb.Append(t.Definition.CodexLink);
            sb.Append(' ');
            sb.Append(GlyphMap.ArrowRight);
            sb.Append(' ');
            if (!t.Definition.IsInSeason) {
                sb.Append(Texts.Red(T.ResearchSeasonalLocked_V1.F(t.Definition.Seasonal.RangeText)));
            } else if (!Researchable.IsEmptyPrereq(t.Definition)) {
                sb.AppendFormat(Texts.Red("{0}: {1}"), T.CodexMResearch, T.ResearchLocked);
            } else {
                sb.Append(T.DeviceLootableOnly);
            }
        }

        private static void ApplyBuildACopy(List<ConstructableComp> constructed,
                int countUnresearched, StringBuilder sb) {
            if (countUnresearched > 0) {
                sb.Append(Texts.Grey($"+{countUnresearched}"));
                A.S.Sig.ShowErrorMessage.Send($"{T.BuildACopy}{sb}");
            }

            if (Arrays.IsEmpty(constructed)) { return; }
            if (!ValidateLimit(constructed.Count, T.BuildACopy, false)) {
                return;
            }
            if (constructed.Count == 1) {
                EntityUtils.HideEntityLink();
                ToolPlaceTiles.StartBuilding(constructed[0].Entity.DefinitionId);
                return;
            }
            var allPositions = new HashSet<int>();
            var sources = new List<Vector2>();
            for (int i = 0; i < constructed.Count; i++) {
                var c = constructed[i];
                ApplyOneBuildACopy(allPositions, c, sources);
            }

            var posRect = Pos.Encapsulate(allPositions);
            var botLeft = posRect.Min;

            EntityUtils.HideEntityLink();

            ToolChoosePos.BeginTiles(allPositions, Consts.YellowRectColor,
                (p, v) => OnChoosePos(p, v, constructed),
                (p, v, set) => ValidatePos(p, v, set, constructed), sources);
        }
        private static ChoosePosResult OnChoosePos(int p, Vector2 v,
                List<ConstructableComp> constructed) {
            var tool = The.Toolbox.Get<ToolChoosePos>(ToolChoosePos.ToolId);
            Pos.ToXY(p, out var x, out var y);
            int motionX = (int) v.x - x;
            int motionY = (int) v.y - y;
            // Snapshot rotated destinations up front. ApplyBuildACopy below
            // re-enters ToolChoosePos for the next batch, and placement can
            // trigger signals that cascade to tool changes — either can clear
            // ToolChoosePos rotation state. Resolve per-tile math while it's
            // still intact.
            var plan = new List<(ConstructableComp c, int posTo, Facing.Type rot)>(
                constructed.Count);
            foreach (var r in constructed) {
                var def = r.Tile.Definition;
                if (!r.S.Research.IsDefUnlocked(def)) { continue; }
                var srcTr = r.Tile.Transform;
                var newRot = srcTr.IsRotatable
                    ? tool.RotateTileRotation(srcTr.Rotation, srcTr.ValidRotations)
                    : srcTr.Rotation;
                var rotatedAnchor = tool.RotatedTileAnchor(r.Tile.PosIdx,
                    srcTr.RotatedWidth, srcTr.RotatedHeight);
                var posTo = Pos.NeighborAt(rotatedAnchor, motionX, motionY);
                plan.Add((r, posTo, newRot));
            }
            foreach (var (c, posTo, rot) in plan) {
                var proto = EntityUtils.CreatePrototype<Tile>(c.Tile.Definition);
                proto.Transform.Rotation = rot;
                // Match ToolPlaceTiles.BuildTile: refresh graphics dims from
                // the prototype's transform so downstream rendering picks up
                // the rotated orientation.
                proto.Graphics.OnReady(false);
                new CmdPlaceTile(posTo, proto, warn: true, false,
                    skipValidation: false).Execute(c.S);
            }
            ApplyBuildACopy(constructed, 0, null);
            return new ChoosePosResult {
                IsSuccess = true,
                ClearTool = false,
            };

        }
        private static bool ValidatePos(int p, Vector2 motion, HashSet<int> set, List<ConstructableComp> constructed) {
            var tool = The.Toolbox.Get<ToolChoosePos>(ToolChoosePos.ToolId);
            var rotatedPos = Pos.NeighborAt(p, -Mathf.FloorToInt(motion.x),
                -Mathf.FloorToInt(motion.y));
            var origPos = tool.UnrotatePos(rotatedPos);
            var obj = EntityUtils.GetTopmostTile(origPos);
            if (obj == null) {
                D.Warn("BuildACopy: obj null. p={0} motion={1} rotatedPos={2} origPos={3} rotStep={4}",
                    Pos.String(p), motion, Pos.String(rotatedPos),
                    Pos.String(origPos), tool.RotationStep);
                return false;
            }
            if (obj.Transform.IsChild) {
                obj = obj.Transform.Parent;
            }
            if (!constructed.Contains(obj.Constructable)) {
                var fl = EntityUtils.FloorAt(origPos);
                if (fl == null || !constructed.Contains(fl.Constructable)) {
                    D.Warn("BuildACopy: obj {0} not in constructed. origPos={1} rotStep={2}",
                        obj, Pos.String(origPos), tool.RotationStep);
                    return false;
                }
                obj = fl;
            }
            var tr = obj.Transform;
            var rotatedAnchor = tool.RotatedTileAnchor(tr.PosIdx,
                tr.RotatedWidth, tr.RotatedHeight);
            var newPos = Pos.NeighborAt(rotatedAnchor,
                Mathf.FloorToInt(motion.x), Mathf.FloorToInt(motion.y));

            bool canPlace;
            if (tool.RotationStep != 0 && tr.IsRotatable) {
                var oldRot = tr.Rotation;
                var newRot = tool.RotateTileRotation(oldRot, tr.ValidRotations);
                tr.Rotation = newRot;
                canPlace = CmdPlaceTile.CanPlace(newPos, obj,
                    obj.Definition.LayerId, warn: false, null, false);
                tr.Rotation = oldRot;
            } else {
                canPlace = CmdPlaceTile.CanPlace(newPos, obj,
                    obj.Definition.LayerId, warn: false, null, false);
            }
            if (!canPlace) {
                D.Warn("BuildACopy: CanPlace=false. obj={0} srcPos={1} newPos={2} motion={3} rotStep={4}",
                    obj, Pos.String(tr.PosIdx), Pos.String(newPos), motion,
                    tool.RotationStep);
                return false;
            }
            return true;
        }
        private static void ApplyOneBuildACopy(HashSet<int> allPositions,
                ConstructableComp c, List<Vector2> sources) {
            var cnt = c.Tile.Transform.GetSubtilePos(Caches.PosIdx);
            for (int ti = 0; ti < cnt; ti++) {
                allPositions.Add(Caches.PosIdx[ti]);
            }
            sources.Add(c.Tile.Position);
        }
    }

}
