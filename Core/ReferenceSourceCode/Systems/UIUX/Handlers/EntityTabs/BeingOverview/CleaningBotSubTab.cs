using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class CleaningBotSubTab : BaseBeingSubTab {
        public CleaningBotSubTab(EntityOverviewContext ctx) : base(ctx) { }
        private WButtonIconText2 wSubTab;
        private const string Id = "CleaningBot";

        public override bool Spawn(RectTransform container, int siblingIdx) {
            if (being.Being == null || being.Being.IsDead) { return false; }
            if (!being.Being.AIAgent.DirectControl.IsControllable) { return false; }
            if (!being.Being.TryGetComponent<CleaningBotComp>(out _)) { return false; }

            wSubTab = UISpawn.Widget<WButtonIconText2>(
                lifetime, UIPrefabId.WContent2Button, container, siblingIdx);
            UpdateSubTabButton();

            if (SubPanelWidget.IsOpenFor == Id) {
                ShowSubPanel();
                Coroutines.OnNextFrame(() => {
                    var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
                    subPanel.RepositionWidget(wSubTab, connectorOnly: true);
                });
            }

            return true;
        }

        private void UpdateSubTabButton() {
            if (!being.Being.TryGetComponent<CleaningBotComp>(out var comp)) { return; }
            var subtitle = comp.IsAutoCleanEnabled
                ? Texts.Grey($"{comp.AutoCleanIntervalMinutes} min")
                : Texts.Grey(T.AutocleanDisabled);
            wSubTab.Load(IconId.CMop, T.CleaningBot, subtitle, ShowSubPanel);
        }

        private void ShowSubPanel() {
            if (being.Being == null) { return; }
            if (!being.Being.TryGetComponent<CleaningBotComp>(out var comp)) { return; }

            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.CleaningBot);
            int gIdx = 0;

            var block = UISpawn.Block(l, subPanel.Container, gIdx++);
            int idx = 0;

            var autoClean = UISpawn.Widget<WToggleText>(l,
                UIPrefabId.WContent2Toggle, block.Container, idx++);
            autoClean.Load(T.AutocleanEnabled, comp.IsAutoCleanEnabled, isOn => {
                comp.IsAutoCleanEnabled = isOn;
                UpdateSubTabButton();
                var sp = S.UIQuery.GetEntitySubPanel.Ask();
                sp.Close(false);
                ShowSubPanel();
                Coroutines.OnNextFrame(() => {
                    sp = S.UIQuery.GetEntitySubPanel.Ask();
                    sp.RepositionWidget(wSubTab, connectorOnly: true);
                });
            });

            if (comp.IsAutoCleanEnabled) {
                var sliderRow = UISpawn.Widget<WSliderWithIconText2>(l,
                    UIPrefabId.WContent2Text2Slider, block.Container, idx++);
                sliderRow.Item.Load(IconId.CClockFace, T.AutocleanIntervalMinutes, null);
                sliderRow.Slider.Load(comp.AutoCleanIntervalMinutes, v => {
                    var newV = Mathf.RoundToInt(v);
                    if (newV < comp.AutoCleanIntervalMinutes) {
                        being.Being.AIAgent.BB.SetCooldown(NeedIdH.CleanFloors, G.Ticks, 0, 0);
                    }
                    comp.AutoCleanIntervalMinutes = newV;
                    UpdateSubTabButton();
                }, 15, 300);
                sliderRow.Slider.SetValueTextFunc(v => Units.Num(Mathf.RoundToInt(v)));
            }

            subPanel.ShowFooter(false);
            subPanel.Show(wSubTab, Id);
        }
    }
}
