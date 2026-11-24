using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class BeingDataSearchResult : BaseSearchResult {
        private readonly BeingData being;
        public override string Id => Units.Num(IdH);
        public override int IdH => (int) being.Id;
        public BeingDataSearchResult(BaseSearchAdapter adapter, BeingData data, int order) : base(adapter) {
            being = data;
            Order = order;
        }

        public override bool IsPinnedAvailable => A.S.Query.FindBeingData.Ask((uint) IdH) != null;

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life, UIPrefabId.WQuickSearchResult, wContainer.Container, idx);
            w.Load((Sprite) null, being.Name, being.UI.GetBio(false, true), OnSelect);
            being.Avatar.Load(w.Image);
            w.SetPinnable(this);
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchPinnedItem>(life, UIPrefabId.WQuickSearchButton, wContainer.Container, idx);
            w.Load((Sprite) null, being.Name, OnSelect, this);
            being.Avatar.Load(w.Image);
        }

        public override void OnSelect() {
            if (being.Being != null) {
                BeingUtils.SelectFocused(being);
            } else {
                A.S.UIQuery.ShowBeingPanel.Ask(being);
            }
            A.S.UIQuery.ShowQuickSearch.Ask(false);
        }
    }
}
