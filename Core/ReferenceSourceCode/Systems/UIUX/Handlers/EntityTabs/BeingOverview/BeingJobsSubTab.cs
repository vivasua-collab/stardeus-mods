using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Utils;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public class BeingJobsSubTab : BaseBeingSubTab {
        public BeingJobsSubTab(EntityOverviewContext ctx) : base(ctx) { }
        private WButtonIconText2 wSubTab;
        private const string Id = "Jobs";

        public override bool Spawn(RectTransform container, int siblingIdx) {
            wSubTab = UISpawn.Widget<WButtonIconText2>(
                lifetime, UIPrefabId.WContent2Button, container, siblingIdx);
            wSubTab.Load(IconId.CHammer, T.NounJobs);
            wSubTab.SetText2(Units.Num(being.Mind.Jobs.VisibleCount));
            wSubTab.SetOnClick(ShowSubPanel);
            // S is gone by the time a game teardown disposes the lifetime
            var jobsChanged = S.Sig.BeingJobsChanged;
            jobsChanged.AddListener(OnJobsChanged);
            lifetime.OnDispose(() => jobsChanged.RemoveListener(OnJobsChanged));
            if (SubPanelWidget.IsOpenFor == Id) {
                ShowSubPanel();
                // since we re-show subpanel immediately, the connector gets lost
                // so we want to reposition it, but we don't want to move the panel
                // or it will be a bit jumpy and annoying.
                // ShowSubPanel() somehow creates the connector off screen when
                // it's done in same frame when respawning the widgets
                Coroutines.OnNextFrame(() => {
                    var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
                    subPanel.RepositionWidget(wSubTab, connectorOnly: true);
                });
            }
            return true;
        }

        // a new ability-gated job or another mind: the rows hold the old job slots
        private void OnJobsChanged(BeingData b) {
            if (b != being) { return; }
            wSubTab.SetText2(Units.Num(being.Mind.Jobs.VisibleCount));
            if (SubPanelWidget.IsOpenFor != Id) { return; }
            var sp = S.UIQuery.GetEntitySubPanel.Ask();
            sp.Close(false);
            ShowSubPanel();
            Coroutines.OnNextFrame(() => {
                sp = S.UIQuery.GetEntitySubPanel.Ask();
                sp.RepositionWidget(wSubTab, connectorOnly: true);
            });
        }

        private void ShowSubPanel() {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.NounJobs);

            var block = UISpawn.Block(l, subPanel.Container, 0);
            var cont = block.Container;

            int idx = 0;
            var isPlayer = being.EffectiveFaction.IsPlayer && being.HasStateFlag(BeingState.Alive);

            foreach (var job in being.Mind.Jobs.JobSlots.Values) {
                if (job.IsInternal) { continue; }
                if (isPlayer) {
                    var w = UISpawn.Widget<WButtonWithIconText2>(
                        l, UIPrefabId.WContent2Text2Btn, cont, idx++);
                    var slot = job;
                    var data = being;
                    w.Item.Load(job.Job.IconId, null, null);
                    w.Button.Load(JobPriorityUI.Text(job),
                        () => JobPriorityUI.Cycle(data, slot, 1));
                    w.Button.SetOnRightClick(() => JobPriorityUI.Cycle(data, slot, -1));
                    UIUtils.AddTipFunc(l, w, () => {
                        slot.GetNameAndLevelText(out _, out _, out string tip);
                        var prio = JobPriorityUI.TooltipLines(slot);
                        if (prio == null) { return tip; }
                        return tip == null ? prio : $"{tip}\n{prio}";
                    });
                    job.ChangedB.Bind(l, () => {
                        job.GetNameAndLevelText(out var jobName, out string lvlText, out string tip);
                        w.Item.Load(job.Job.IconId, jobName, lvlText);
                        w.Button.SetText(JobPriorityUI.Text(job));
                    });
                } else {
                    job.GetNameAndLevelText(out var jobName, out string lvlText, out string tip);
                    var w = UISpawn.Widget<WIconText2>(
                        l, UIPrefabId.WContentText2, cont, idx++);
                    w.Load(job.Job.IconId, jobName, lvlText);
                    UIUtils.AddTip(l, w, tip);
                }
            }

            var allJobs = UISpawn.Widget<WButtonText>(l, UIPrefabId.WPanelButton, subPanel.Footer.Container, 0);
            allJobs.Load(Texts.TitleWithShortcutGlyph(T.NounColonyJobs, Input.ActionType.OpenJobs), () => {
                S.UIQuery.ToggleJobMatrix.Ask(!UIShowing.JobMatrix);
            });
            subPanel.ShowFooter(true);

            subPanel.Show(wSubTab, Id);
        }
    }
}
