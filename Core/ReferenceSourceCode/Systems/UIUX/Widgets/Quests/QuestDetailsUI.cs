using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Creatures;
using Game.Systems.Quests;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public static class QuestDetailsUI {
        public static void LoadBlock(Lifetime life, WGroup group, Quest q) {
            int bIdx = 0;
            int idx;

            // Hack to prevent resize flapping (part 1)
            var scroll = group.Rect.GetComponentInParent<ScrollRect>(includeInactive: true);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

            // Title + description
            var infoBlock = UISpawn.Block(life, group, bIdx++);
            idx = 0;
            UISpawn.Widget<WText>(life, UIPrefabId.WContentHeader, infoBlock.Container, idx++).Load(q.Data.Title);
            var txtBlock = UISpawn.Widget<WText>(life, UIPrefabId.WContentTextBlock, infoBlock.Container, idx++);
            txtBlock.Load(Texts.Beautify(q.Data.Desc));
            txtBlock.RebuildTextLayout();

            // Deadline (conditional)
            if (!q.IsEnded && q.Data.GetNextDeadline(out var deadline, out _)) {
                var deadlineBlock = UISpawn.Block(life, group, bIdx++);
                idx = 0;
                UISpawn.Widget<WText>(life, UIPrefabId.WContentHeader, deadlineBlock.Container, idx++)
                    .Load(T.QuestConditions);
                var deadW = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, deadlineBlock.Container, idx++);
                deadW.Load(IconId.CTime, T.NounDeadline);
                A.S.Clock.TickB.Bind(life, (ticks) => {
                    var sb = Caches.StringBuilder4;
                    sb.Clear();
                    QuestUtils.AppendDeadline(sb, deadline, G.Ticks, suffix: "");
                    deadW.SetText(sb.ToString());
                });
            }

            // Objectives
            var objBlock = UISpawn.Block(life, group, bIdx++);
            idx = 0;
            UISpawn.Widget<WText>(life, UIPrefabId.WContentHeader, objBlock.Container, idx++).Load(T.CombatObjectives);
            // The HUD tracker already lists these steps. Repeating them here made players keep
            // this window open while playing, so point at the tracker instead
            if (q.IsActive && A.S.Query.IsTrackedQuest.Ask(q) && QuestTrackerHandoff.IsTrackerVisible()) {
                LoadTrackerHandoff(life, objBlock.Container, idx, q);
            } else {
                LoadSteps(life, objBlock.Container, idx, q);
            }

            // Provider
            if (q.Data.Provider != null) {
                var provider = q.Data.Provider;
                var providerBlock = UISpawn.Block(life, group, bIdx++);
                idx = 0;
                var head = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, providerBlock.Container, idx++);
                head.Load(T.NounQuestProvider);
                if (provider is SpaceObject sob && sob.Ship != null) {
                    provider = sob.Ship;
                }
                if (provider is NPCShip ship) {
                    var wgt = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, providerBlock.Container, idx++);
                    wgt.Load(ship.Class.Preview, ship.NameFull, null, () => {
                        A.S.UIQuery.StarmapFocusOn.Ask(ship.SO, true);
                    });
                } else if (provider is ColonistQuestProvider col) {
                    var wgt = UISpawn.Widget<WBeingListItem>(life, UIPrefabId.WBeingListItem, providerBlock.Container, idx++);
                    wgt.Load(col.BeingData, () => {
                        BeingUtils.SelectFocused(col.BeingData);
                    });
                } else if (provider is SpaceObject so) {
                    var wgt = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, providerBlock.Container, idx++);
                    wgt.Load(so.Type.Icon, so.DisplayName, null, () => {
                        A.S.UIQuery.StarmapFocusOn.Ask(so, true);
                    });
                }
            }


            // Rewards (conditional, dark block)
            if (!Arrays.IsEmpty(q.Data.Rewards)) {
                WContentBlock rewardsBlock = null;
                idx = 0;
                foreach (var reward in q.Data.Rewards) {
                    if ((reward.Flags & (QuestRewardFlags.Penalty
                            | QuestRewardFlags.Surprise
                            | QuestRewardFlags.PackageContent)) != QuestRewardFlags.None) {
                        continue;
                    }
                    if (rewardsBlock == null) {
                        rewardsBlock = UISpawn.Block(life, group, bIdx++);
                        UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, rewardsBlock.Container, idx++).Load(T.QuestRewards);
                    }
                    UISpawn.Widget<WIconText2>(life, UIPrefabId.WContent2Text2, rewardsBlock.Container, idx++).Load(reward.Icon, reward.Title);
                }
            }

            // Hack to prevent resize flapping (part 2)
            Coroutines.OnNextFrame(() => {
                if (!MiscUtils.IsDestroyed(scroll)) {
                    scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                }
            });
        }

        private static void LoadTrackerHandoff(Lifetime life, RectTransform container, int idx, Quest q) {
            var handoff = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContentTextButton,
                container, idx);
            handoff.Load(QuestUtils.IconFor(q, true), T.QuestObjectivesInTracker,
                T.Show, () => {
                    A.S.UIQuery.ShowQuests.Ask(false);
                    QuestTrackerHandoff.FocusTracker(q);
                });
        }

        private static void LoadSteps(Lifetime life, RectTransform container, int idx, Quest q) {
            List<QuestStepLine> lines = null;
            foreach (var step in q.Steps) {
                if (!QuestUtils.IsStepVisible(step)) { continue; }

                var stepW = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentTextBlockIcon, container, idx++);
                lines ??= new List<QuestStepLine>();
                lines.Add(new QuestStepLine(step, stepW, isHeadline: false));
                if (step.Desc != null) {
                    if (step.IsActive) {
                        UIUtils.AddTipFunc(life, stepW, () => Texts.Beautify(step.Desc));
                    }
                }
            }
            if (lines == null) {
                UISpawn.Widget<WIconText>(life, UIPrefabId.WContentTextBlockIcon, container, idx++)
                    .Load(IconId.CCheckDisabled, T.QuestDetailsWillRevealOnAccept);
                return;
            }
            // One listener for all the lines, bound once per load
            A.S.Clock.TickB.Bind(life, _ => {
                for (int i = 0; i < lines.Count; i++) {
                    var line = lines[i];
                    line.Sync();
                    lines[i] = line;
                }
            }, notifyImmediately: false);
        }
    }
}
