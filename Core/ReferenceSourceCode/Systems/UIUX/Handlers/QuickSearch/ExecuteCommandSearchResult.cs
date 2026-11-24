using System;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class ExecuteCommandSearchResult : BaseSearchResult {
        private readonly BaseSearchCommand cmd;
        public override string Id => cmd.Id;
        public override int IdH => cmd.IdH;
        public ExecuteCommandSearchResult(BaseSearchAdapter adapter, BaseSearchCommand cmd, int order) : base(adapter) {
            this.cmd = cmd;
            Order = order;
        }

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life, UIPrefabId.WQuickSearchResult, wContainer.Container, idx);
            w.Load(cmd.Icon, cmd.TitleT, cmd.DescT, OnSelect);
            w.SetPinnable(this);
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchPinnedItem>(life, UIPrefabId.WQuickSearchButton, wContainer.Container, idx);
            w.Load(cmd.Icon, cmd.TitleT, OnSelect, this);
            if (cmd.Tooltip != null) {
                w.SetOnHover(ShowTooltip);
            }
        }

        private void ShowTooltip(bool hovering) {
            if (hovering) {
                The.SysSig.ShowTooltip.Send(cmd.Tooltip, true);
            } else {
                The.SysSig.HideTooltip.Send();
            }
        }

        public override void OnSelect() {
            if (cmd.Execute()) {
                A.S.UIQuery.ShowQuickSearch.Ask(false);
            }
        }
    }
}
