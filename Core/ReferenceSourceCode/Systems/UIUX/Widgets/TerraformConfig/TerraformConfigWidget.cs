using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class TerraformConfigWidget : BaseWidget, ICloseableWidget {
        [SerializeField] private WButtonText wPanelHeader;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        [SerializeField] private WGroup wFooter;
        [SerializeField] private WText aboutText;
        public SubPanelWidget SubPanel;
        public WGroup WItems => wItems;
        private Lifetime lifetime;
        private GameState S;
        private WIconText2Progress overallProgress;
        private readonly Dictionary<TerraformType, WIconText2Progress> typeProgress = new();

        public void Initialize(GameState s) {
            gameObject.SetActiveFast(false);
            Register(UIWidgetId.TerraformConfig, this);
            S = s;
            wPanelHeader.Load(T.Terraforming, Close);
            aboutText.Load(T.TerraformingInfo);
            wFooter.DestroyChildren();
            wItems.DestroyChildren();
        }

        public void OnTerraformUpdated(TerraformType t, float progress) {
            if (typeProgress.TryGetValue(t, out var w)) {
                w.SetPercent(progress);
            }
            if (overallProgress != null && S.Sys.Terraform.Target != null) {
                overallProgress.SetPercent(S.Sys.Terraform.Target.TerraformingProgress);
            }
        }

        public override void Toggle(bool show) {
            if (show) {
                UIShowing.Set(UIShowingFlags.Terraforming);
            } else {
                UIShowing.Unset(UIShowingFlags.Terraforming);
            }
            base.Toggle(show);
            if (show) {
                Load();
                Rect.SetAsLastSibling();
            } else {
                Cleanup();
            }
        }

        public void Load() {
            Cleanup();

            var terraform = S.Sys.Terraform;

            lifetime = Lifetime.Create("TerraformConfig");
            overallProgress = null;
            typeProgress.Clear();

            var block = UISpawn.Block(lifetime, wItems, 0);
            int idx = 0;

            terraform.CheckTarget();
            var target = terraform.Target;
            var knownPlanet = terraform.KnownTerraformable.Count > 0
                ? terraform.KnownTerraformable[0] : null;
            var planet = target ?? knownPlanet;

            wFooter.SetActiveFast(false);
            aboutText.SetActiveFast(planet == null
                || (!planet.Flags.HasFlag(SOFlags.Settled) && planet.TerraformingProgress < 1f));

            var planetHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            planetHeader.Load(T.TerraformablePlanets);

            if (planet == null) {
                var noTargetW = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                noTargetW.Load(IconId.CWarning, T.NoTerraformTarget);
                var noTargetText = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2TextBlock, block.Container, idx++);
                noTargetText.Load(T.NoTerraformTargetHelp);
            } else if (target != null && target.Flags.HasFlag(SOFlags.Settled)) {
                var colonizedW = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                colonizedW.Load(IconId.CTerraform, T.IsColonized_V1.F(target.DisplayName));
            } else if (target != null) {
                if (target.TerraformingProgress >= 1f) {
                    var settleBtn = UISpawn.Widget<WButtonText>(lifetime,
                        UIPrefabId.WPanelButton, wFooter.Container, idx++);
                    settleBtn.Load(T.SettleIn_V1.F(target.DisplayName),
                        terraform.BeginSettleIn);
                    wFooter.SetActiveFast(true);
                }

                overallProgress = UISpawn.Widget<WIconText2Progress>(lifetime,
                    UIPrefabId.WContent2Progress, block.Container, idx++);
                overallProgress.Load(IconId.CTerraform,
                    T.TerraformingNow_V1.F(target.DisplayName),
                    target.TerraformingProgress);

                var tt = TerraformType.All;
                for (int i = 0; i < tt.Count; i++) {
                    var type = tt[i];
                    var typeW = UISpawn.Widget<WIconText2Progress>(lifetime,
                        UIPrefabId.WContent2Progress, block.Container, idx++);
                    typeW.Load(type.IconId, type.NameT, target.TerraformingProgressFor(type));
                    typeProgress[type] = typeW;
                }
            } else {
                // Found by telescope but not yet in range to terraform
                var planetW = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                planetW.Load(IconId.CTerraform,
                    planet.ParentPath(false),
                    Units.PercentageWithFraction4(planet.TerraformingProgress));
            }
        }

        public void Close() {
            S.UIQuery.ToggleTerraformConfig.Ask(false);
        }

        public bool TryCloseWidget() {
            Toggle(false);
            return true;
        }

        public void Cleanup() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            overallProgress = null;
            typeProgress.Clear();
        }
    }
}
