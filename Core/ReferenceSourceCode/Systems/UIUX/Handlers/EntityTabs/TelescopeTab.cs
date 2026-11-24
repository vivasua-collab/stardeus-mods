using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class TelescopeTab : BaseEntityTab {
        public const string TabId = "Telescope";

        [SelfInit]
        private static void Register() {
            RegisterTab(new TelescopeTab());
        }

        private Tile tile;
        private TelescopeComp comp;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 20;
        public override string TabName => T.ObjectsDevicesTelescope;

        public override bool ShowFor(GameState s, Tile t) {
            if (!t.IsConstructed) { return false; }
            if (t.ENode?.IsReachable == false) { return false; }
            if (!t.TryGetComponent<TelescopeComp>(out var c)) { return false; }
            S = s;
            tile = t;
            comp = c;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            if (A.S.TelescopeScanCount > 0) {
                var foundW = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContent2Header, block.Container, idx++);
                foundW.Load(T.ScanReport_V1.Replace(":", string.Empty).Replace("{0}", string.Empty).Trim());
                var terraform = S.Sys.Terraform;
                var known = terraform.KnownTerraformable.FirstOrDefault();
                if (known != null) {
                    var planetW = UISpawn.Widget<WIconText>(lifetime,
                        UIPrefabId.WContent2Text, block.Container, idx++);
                    planetW.Load(IconId.CTerraform,
                        known.Parent.StarmapLink + " → " + known.StarmapLink);
                } else {
                    var report = UISpawn.Widget<WText>(lifetime,
                        UIPrefabId.WContentTextBlock, block.Container, idx++);
                    report.Load(T.TerraformingInfo);
                }
                return true;
            }

            var progress = UISpawn.Widget<WProgressWithLabel>(lifetime,
                UIPrefabId.WContentHeaderProgress, block.Container, idx++);
            progress.Label.Load(T.ScanProgress);
            progress.Progress.Load(Units.Percentage(S.TelescopeProgress), S.TelescopeProgress);

            #if !PRODUCTION_BUILD
            var devBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            devBtn.Load(IconId.WDev, "Dev hack", "Fill progress", () => {
                WPopup.Create(false, IconId.WDev, "Dev hack", "Fill progress").Spawn();
                S.TelescopeProgress = 0.99f;
                ReloadTab();
            });
            #endif

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            var btn = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WPanelButton, group.Container, 0);
            btn.Load(T.Terraforming,
                () => S.UIQuery.ToggleTerraformConfig.Ask(!UIShowing.Terraforming));
            return true;
        }

        protected override void Cleanup() {
            tile = null;
            comp = null;
            S = null;
        }
    }
}
