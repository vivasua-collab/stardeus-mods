using System;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [Obsolete("Not used", error: true)]
    public class BeingConditionsSubTab : BaseBeingSubTab {
        public BeingConditionsSubTab(EntityOverviewContext ctx) : base(ctx) { }
        private WButtonIconText2 wSubTab;
        private Lifetime contentLifetime;
        [ClearOnReload]
        private static bool isExpanded;

        public override bool Spawn(RectTransform container, int siblingIdx) {
            wSubTab = UISpawn.Widget<WButtonIconText2>(
                lifetime, UIPrefabId.WContent2ListButton, container, siblingIdx);
            wSubTab.Load(IconId.CCondition, T.NounConditions);
            wSubTab.SetText2(GetConditionsCount());
            var listButton = wSubTab.GetComponent<WContentListButton>();
            listButton.LoadOnToggle(lifetime, ToggleContent, isExpanded);

            return true;
        }

        private string GetConditionsCount() {
            int cnt = 0;

            foreach (var c in being.Conditions.Values) {
                if (c.IsUndetected) {
                    continue;
                }
                cnt++;
            }

            foreach (var bp in being.Body.Slots) {
                if (bp.IsPartRemoved) { continue; }
                var wounds = bp.State.Wounds;
                var wc = Arrays.Count(wounds);
                cnt += wc;
            }

            return Units.Num(cnt);
        }

        private bool ToggleContent(Lifetime life, RectTransform container, bool willShow, bool wasLoaded) {
            S.UIQuery.CloseEntitySubPanel.Ask();
            if (contentLifetime != null) {
                contentLifetime.Dispose();
                contentLifetime = null;
            }
            isExpanded = willShow;
            if (!willShow) { return wasLoaded; }
            contentLifetime = lifetime.CreateChild("Content");
            int idx = 0;
            // foreach (var slot in being.Conditions) {
            //     var sw = parent.Spawn<WButtonIconText2>(
            //         UIPrefabId.WContent2Button, container, idx++);
            //     sw.Load(slot.SlotIcon, $"{slot.NameT}: {slot.ItemName}");
            // }

            int cnt = 0;

            foreach (var c in being.Conditions.Values) {
                if (c.IsUndetected) {
                    #if PRODUCTION_BUILD
                    continue;
                    #endif
                }
                var cw = UISpawn.Widget<WIconText2>(contentLifetime, UIPrefabId.WContent2Text2, container, idx++);
                string sideText;
                if (c.IsUndetected) {
                    sideText = "DEV: Undetected!";
                } else {
                    cnt++;
                    sideText = Severities.NameFor(c.Severity, c.Condition.IsPositive);
                }
                cw.Load(c.Condition.Icon, c.Condition.NameT, sideText);
            }

            foreach (var bp in being.Body.Slots) {
                if (bp.IsPartRemoved) { continue; }
                var wounds = bp.State.Wounds;
                var wc = Arrays.Count(wounds);
                if (wc > 0) {
                    foreach (var w in wounds) {
                        cnt++;
                        var cw = UISpawn.Widget<WIconText>(contentLifetime, UIPrefabId.WContent2Text, container, idx++);
                        string name = w.TitleFor(bp);
                        cw.Load(IconId.CBlood, name);
                    }
                }
            }

            wSubTab.SetText2(Units.Num(cnt));

            bool addedDevCondLabel = false;
            foreach (var cond in being.ConditionProgress) {
                // developed condition
                if (being.Conditions.ContainsKey(cond.Key)) { continue; }
                if (cond.Value < 0.01f) { continue; }
                if (!addedDevCondLabel) {
                    addedDevCondLabel = true;
                    var label = UISpawn.Widget<WText>(contentLifetime, UIPrefabId.WContent2Header, container, idx++);
                    label.Load(T.NounDevelopingConditions);
                }
                var condition = Registries.Conditions.FastGet(cond.Key);
                var cw = UISpawn.Widget<WIconText2>(contentLifetime, UIPrefabId.WContent2Text2, container, idx++);
                cw.Load(condition.Icon, condition.NameT, Units.Percentage(cond.Value));
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(container);
            LayoutRebuilder.ForceRebuildLayoutImmediate(wSubTab.Rect);
            return true;
        }
    }
}
