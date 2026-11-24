#if !PRODUCTION_BUILD
// #define DEBUG_TELEPORTERS
#endif
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX.Codex.Pages;
using Game.UI;
using Game.Utils;
using KL.Grid;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class TeleporterTab : BaseEntityTab {
        public const string TabId = "Teleporter";

        [SelfInit]
        private static void Register() {
            RegisterTab(new TeleporterTab());
        }

        private Tile tile;
        private TeleporterComp comp;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 20;
        public override string TabName => T.ObjectsDevicesTeleporter;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (!tile.TryGetComponent<TeleporterComp>(out var c)) { return false; }
            S = s;
            this.tile = tile;
            comp = c;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            if (comp.Outbound != null) {
                LoadLinked(block.Container, ref idx);
            } else {
                LoadUnlinked(block.Container, ref idx);
            }

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            if (comp.Outbound != null) {
                return false;
            }
            var howItWorksBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent4Button, group.Container, 0);
            howItWorksBtn.Load(IconId.CInfo, T.HowItWorks, null, () => {
                A.S.UIQuery.ShowCodexEntry.Ask(
                    CodexConceptPage.FindById(TeleportersPage.PageId));
            });
            return true;
        }

        private void LoadLinked(RectTransform container, ref int idx) {
            var outbound = comp.Outbound;
            var name = EntityUtils.PosOrLabelOf(outbound.Tile);

            var row = UISpawn.Widget<WContentButton2Text2>(lifetime,
                UIPrefabId.WContent2Button2Text2, container, idx++);
            row.Item.Load(IconId.CTargetSpot, name, T.Linked, () => {
                EntityUtils.SelectAndFocus(outbound.Tile);
            });
            row.Button.Load(T.Unlink, () => {
                comp.UnsetOutbound();
                ReloadTab();
            });

            #if DEBUG_TELEPORTERS
            var vfxBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, container, idx++);
            vfxBtn.Load(IconId.CSparkles, "Play VFX", T.Execute,
                () => comp.PlayTeleportVFX(outbound));
            #endif
        }

        private void LoadUnlinked(RectTransform container, ref int idx) {

            #if DEBUG_TELEPORTERS
            var debugBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, container, idx++);
            debugBtn.Load(IconId.WDev, "Fuck With Path System", null, () => {
                S.Sig.RemoveTeleporter.Send(comp, comp.DebugAddedToPathPos);
                S.Sig.AddTeleporter.Send(comp, tile.PosIdx,
                    Pos.NeighborAt(tile.PosIdx, 10, 10));
            });
            #endif

            var teleporters = S.Components.AllByType<TeleporterComp>();
            if (teleporters == null) { return; }

            var reachable = teleporters
                .Where(t => {
                    var tp = (TeleporterComp) t;
                    return tp != comp && tp.CanTeleport
                        && !Pos.AreAdjacent(tile.PosIdx, tp.Entity.PosIdx);
                })
                .OrderBy(a => Pos.SqrDistance(a.Entity.PosIdx, tile.PosIdx));

            bool any = false;
            foreach (var a in reachable) {
                if (a is not TeleporterComp tp) { continue; }
                any = true;
                var captured = tp;
                var dist = Pos.Distance(tp.Tile.PosIdx, tile.PosIdx).ToString("0.#");
                var name = EntityUtils.PosOrLabelOf(tp.Tile);
                var row = UISpawn.Widget<WContentButton2Text2>(lifetime,
                    UIPrefabId.WContent2Button2Text2, container, idx++);
                row.Item.Load(tp.Entity.Definition.Preview, name, dist, () => {
                    EntityUtils.SelectAndFocus(captured.Tile);
                });
                row.Button.Load(T.LinkVerb, () => {
                    comp.SetOutbound(captured);
                    ReloadTab();
                });
                var tip = UIUtils.AddTip(lifetime, row, T.DistanceTo_V1.F(dist));
                tip.HoverFun = (h) => {
                    EntityUtils.ShowEntityLink(null, captured.Tile, h);
                };
            }

            if (!any) {
                var empty = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            }
        }

        protected override void Cleanup() {
            tile = null;
            comp = null;
            S = null;
        }
    }
}
