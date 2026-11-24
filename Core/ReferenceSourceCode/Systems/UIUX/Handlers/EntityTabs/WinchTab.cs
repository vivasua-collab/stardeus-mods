using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class WinchTab : BaseEntityTab {
        public const string TabId = "Winch";

        [SelfInit]
        private static void Register() {
            RegisterTab(new WinchTab());
        }

        private Tile tile;
        private WinchHookComp hook;
        private WinchAnchorComp anchor;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.ResearchWinch;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (tile.TryGetComponent<WinchHookComp>(out var h)) {
                this.tile = tile;
                this.hook = h;
                this.anchor = null;
                S = s;
                return true;
            }
            if (tile.TryGetComponent<WinchAnchorComp>(out var a)) {
                this.tile = tile;
                this.hook = null;
                this.anchor = a;
                S = s;
                return true;
            }
            return false;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            if (hook != null) {
                LoadHookContent(block, ref idx);
            } else {
                LoadAnchorContent(block, ref idx);
            }

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            var helpBtn = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, 0);
            helpBtn.Load(IconId.CInfo, T.HowItWorks, () => {
                S.UIQuery.TryOpenCodexLink.Ask("Manual:BaseBuilding/Winching");
            });
            return true;
        }

        private void LoadHookContent(WContentBlock block, ref int idx) {
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.SelectAnchor);

            var anchors = S.Components.AllByType<WinchAnchorComp>();
            if (Arrays.IsEmpty(anchors)) {
                var emptyRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                emptyRow.Load(IconId.CWarning, T.NoWinchAnchor);
            } else {
                var ordered = anchors
                    .OrderBy(a => Pos.SqrDistance(a.Entity.PosIdx, tile.PosIdx));
                foreach (WinchAnchorComp anch in ordered) {
                    var capturedAnch = anch;
                    var isReachable = hook.CanConnectWith(capturedAnch);
                    var dist = Pos.Distance(capturedAnch.Tile.PosIdx, tile.PosIdx).ToString("0.#");
                    var distDisplay = isReachable ? dist : Texts.Red(dist);

                    var row = UISpawn.Widget<WContentButton2Text2>(lifetime,
                        UIPrefabId.WContent2Button2Text2, block.Container, idx++);
                    row.Item.Load(capturedAnch.Entity.Definition.Preview,
                        EntityUtils.PosOrLabelOf(capturedAnch.Tile),
                        distDisplay,
                        () => S.Sig.CameraFocusRooted.Send(tile, capturedAnch.Tile));
                    row.Button.Load(T.ReelIn, () => {
                        if (!isReachable) {
                            UISounds.PlayActionDenied();
                            return;
                        }
                        hook.TryReelInToAnchor(capturedAnch);
                    });
                    UIUtils.AddHover(lifetime, row,
                        on => EntityUtils.ShowEntityLink(tile, capturedAnch.Tile, on));
                }
            }
        }

        private void LoadAnchorContent(WContentBlock block, ref int idx) {
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.SelectHook);

            var hooks = S.Components.AllByType<WinchHookComp>()
                ?.Where(h => ((WinchHookComp) h).Tile.ENode.IsPowerable);
            if (hooks == null || !hooks.Any()) {
                var emptyRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                emptyRow.Load(IconId.CWarning, T.NoWinchAnchor);
            } else {
                var ordered = hooks
                    .OrderBy(h => Pos.SqrDistance(h.Entity.PosIdx, tile.PosIdx));
                foreach (WinchHookComp h in ordered) {
                    var capturedHook = h;
                    var isReachable = capturedHook.CanConnectWith(anchor);
                    var dist = Pos.Distance(capturedHook.Tile.PosIdx, tile.PosIdx).ToString("0.#");
                    var distDisplay = isReachable ? dist : Texts.Red(dist);

                    var row = UISpawn.Widget<WContentButton2Text2>(lifetime,
                        UIPrefabId.WContent2Button2Text2, block.Container, idx++);
                    row.Item.Load(capturedHook.Entity.Definition.Preview,
                        EntityUtils.PosOrLabelOf(capturedHook.Tile),
                        distDisplay,
                        () => S.Sig.CameraFocusRooted.Send(tile, capturedHook.Tile));
                    row.Button.Load(T.ReelIn, () => {
                        if (!isReachable) {
                            UISounds.PlayActionDenied();
                            return;
                        }
                        capturedHook.TryReelInToAnchor(anchor);
                    });
                    UIUtils.AddHover(lifetime, row,
                        on => EntityUtils.ShowEntityLink(tile, capturedHook.Tile, on));
                }
            }
        }

        protected override void Cleanup() {
            tile = null;
            hook = null;
            anchor = null;
            S = null;
        }
    }
}
