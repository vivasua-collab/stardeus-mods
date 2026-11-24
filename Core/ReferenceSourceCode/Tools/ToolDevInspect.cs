using System.Collections.Generic;
using System.Text;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Systems;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Grid;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using Game.VFXR;
using Game.Input;
using Game.Systems.Rendering;

namespace Game.Tools {
    public sealed class ToolDevInspect : Tool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolDevInspect());
        }
        public const string ToolId = "dev_inspect";
        public override string Id => ToolId;

        public override bool IsConfigurable => false;
        public override bool IsDevTool => true;

        public override string MenuIcon => IconId.WDev;
        private int lastPos;

        private MapRenderer renderer;
        private ParticlesSys ps;

        public override void Activate() {
            renderer = GameObject.FindFirstObjectByType<MapRenderer>();
        }

        public override void Deactivate() {
            The.SysSig.HideTooltip.Send();
            ps?.SelectionVFX.HideAll();
        }

        private List<TileLayerChunk> chunks;

        private readonly EntityInfoItems[] infos = new EntityInfoItems[16];

        public override void UpdateVFX(ParticlesSys ps) {
            this.ps = ps;
            var fx = ps.SelectionVFX.MarkedTiles;
            var count = RenderBeingOffsets(fx, 0);
            count = RenderObjectOffsets(fx, count);
            count = RenderAds(fx, count);
            fx.Show(count);
        }

        private int RenderBeingOffsets(VFXTiles fx, int count) {
            foreach (Being b in S.Beings.All) {
                if (!b.IsActive) { continue; }
                if (b.Graphics == null || b.Graphics.SpriteSet == null) { continue; }
                count = fx.AddPos(b.SelectPosition,
                        Colors.ToF4(Color.magenta), b.SelectableSize.x, count);

                count = fx.AddPos(b.Graphics.Position, Colors.ToF4(Color.yellow), b.Size.x, count);
            }
            return count;
        }

        private int RenderAds(VFXTiles fx, int count) {
            /*
            foreach (var ad in S.Adverts.AvailableAdverts) {
                count = fx.AddPos(EntityUtils.CenterOf(ad.PosIdx),
                    Colors.ToF4(Color.cyan), 0.05f, count);
            }
            foreach (var ead in S.Adverts.EntityAdverts) {
                var ad = ead.Value;
                count = fx.AddPos(EntityUtils.CenterOf(ad.PosIdx),
                    Colors.ToF4(Color.blue), 0.07f, count);
            }
            */
            return count;
        }

        private int RenderObjectOffsets(VFXTiles fx, int count) {
            foreach (Obj o in S.Objs.All) {
                if (!o.IsActive) { continue; }
                if (o.Graphics == null || o.Graphics.SpriteSet == null) { continue; }
                count = fx.AddPos(o.SelectPosition,
                            Colors.ToF4(Color.blue), o.SelectableSize.x, count);
                count = fx.AddPos(o.Graphics.Position, Colors.ToF4(Color.red), o.Size.x, count);
            }
            return count;
        }

        private readonly List<GPURenderSlot> slotsCache = new();

        public override void OnHover(Vector2 pos) {
            if (!The.Bindings.IsPressed(ActionType.AltModifier) || !EntityUtils.IsWithinBounds(pos)) {
                The.SysSig.HideTooltip.Send();
                return;
            }
            var posIdx = EntityUtils.ToPos(pos);
            if (lastPos == posIdx && !The.Bindings.IsPressed(ActionType.CtrlModifier)) {
                return;
            }
            lastPos = posIdx;
            var sb = new StringBuilder();
            sb.AppendFormat("PosIdx: {0} {1} [{2:0.00},{3:0.00}]\n", posIdx, Pos.String(posIdx), pos.x, pos.y);
            foreach (var grid in S.Map.Grids) {
                if (grid == null) { continue; }
                sb.AppendFormat("{0} -> {1}\n", grid.Name, grid.Get(posIdx));
            }
            if (S.Beings.ByPos.TryGetValue(lastPos, out var beings)) {
                if (beings.Count > 0) {
                    sb.AppendFormat(" -> {0} beings in pos\n", beings.Count);
                }
            }

            /*
            if (S.Sys.Fire.CheckIsOnFire(posIdx, false)) {
                sb.AppendFormat("ON FIRE! Total on fire: {0}\n",
                    S.Sys.Fire.OnFireCount);
            }
            */

            // var b = S.Beings.FindClosestVisual(pos, null, 1);
            // if (b?.CombatUnit != null) {
            //     sb.AppendFormat("CombatUnit: {0}\n", b.CombatUnit);
            //     sb.AppendFormat("{0}\n", b.CombatUnit.Squad.Sit);
            //     sb.AppendFormat("{0}\n", b.CombatUnit.Sit);
            // }

            // obj in space
            var obj = S.Objs.FindClosestVisual(pos);
            if (obj != null) {
                sb.AppendFormat("{0}\n", obj);
                sb.AppendFormat("Obj in space: {0}\n", obj.Transform.IsInSpace);
            }

            sb.AppendFormat("Energy Node Count: {0}\n",
                A.Grid.NodeComponents.Count);
            var area = S.Sys.Areas.At(posIdx);
            sb.AppendFormat("Room Id: {0}\n", area);
            sb.AppendFormat("Room Reachable: {0}\n", S.Sys.Areas.IsAreaReachable(area));
            var island = S.Sys.Areas.IslandAt(posIdx);
            sb.AppendFormat("Section Id: {0}\n", island);
            sb.AppendFormat("Tile area type: {0}\n", S.Sys.Areas.AreaTypeAt(posIdx));
            sb.AppendFormat("{0}\n", S.Sys.Dirt.Stats);
            sb.AppendFormat("Is dirty: {0}\n", S.Sys.Dirt.IsDirty(posIdx));
            var dirt = S.Sys.Dirt.At(posIdx);
            for (int i = 0; i < dirt.Count; i++) {
                DirtData d = dirt[i];
                sb.AppendFormat("{0}\n", d);
            }

            var foundInfos = S.Sys.Info.InfoFor(posIdx, infos);
            for (int i = 0; i < foundInfos; i++) {
                sb.AppendFormat("{0}\n", infos[i]);
            }

            slotsCache.Clear();
            S.Sys.Rend.GetSlotsAt(pos, slotsCache);
            foreach (var slot in slotsCache) {
                sb.AppendFormat(" {0} {1}\n", GlyphMap.ArrowRight, slot.DebugString);
            }
            slotsCache.Clear();
            chunks = renderer.TileRenderer.GetChunksAt(pos);
            foreach (var chunk in chunks) {
                sb.AppendFormat("{0}\n", chunk);
            }
            The.SysSig.ShowTooltip.Send(sb.ToString(), false);
        }

        public override void OnClickDown(Vector2 pos) {
            if (!Arrays.IsEmpty(chunks)) {
                foreach (var chunk in chunks) {
                    chunk.Rebuild();
                }
            }
        }

        public override string ToString() {
            return Id;
        }
    }
}
