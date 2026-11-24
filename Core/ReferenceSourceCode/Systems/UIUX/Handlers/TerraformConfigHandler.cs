using System;
using Game.Constants;
using Game.Data;
using Game.UI;

namespace Game.Systems.UIUX {
    public sealed class TerraformConfigHandler : BaseWidgetHandler<TerraformConfigWidget> {
        public TerraformConfigHandler(GameState s, TerraformConfigWidget widget) : base(s, widget) {
            widget.Initialize(s);
            s.UIQuery.ToggleTerraformConfig.SetResponder(ToggleTerraformConfig);
            s.UIQuery.RebuildTerraformConfig.SetResponder(RebuildTerraformConfig);
            S.Sig.TerraformUpdated.AddListener(OnTerraformUpdated);
        }

        private void OnTerraformUpdated(TerraformType type, float amt) {
            if (!UIShowing.Terraforming) { return; }
            widget.OnTerraformUpdated(type, amt);
        }

        private bool ToggleTerraformConfig(bool show) {
            widget.Toggle(show);
            return true;
        }

        private bool RebuildTerraformConfig() {
            if (UIShowing.Terraforming) {
                widget.Load();
                return true;
            }
            return false;
        }
    }
}
