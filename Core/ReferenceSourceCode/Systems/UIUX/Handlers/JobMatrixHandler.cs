using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.BodyParts;
using Game.Systems.Mental;
using Game.Systems.Nanobots;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class JobMatrixHandler : BaseWidgetHandler<JobMatrixWidget> {
        private List<WButtonIconText> headerButtons;
        private Lifetime contentLife;
        private List<JobType> sortedJobs;
        private readonly List<NanobotHiveComp> nanobotHives = new();
        private int lastHiveIdx;
        private CoroutineItem buildCrt;

        public JobMatrixHandler(GameState s, JobMatrixWidget widget) : base(s, widget) {
            widget.Initialize();
            s.UIQuery.ToggleJobMatrix.SetResponder(ToggleMatrix);
            widget.OnHide = OnHide;
            s.Sig.BeingInitialized.AddListener(OnBeingChanged);
            s.Sig.BeingUninitialized.AddListener(OnBeingChanged);
            s.Sig.BeingDied.AddListener(OnBeingChanged);
            // the rows hold the job slots they were built with
            s.Sig.BeingJobsChanged.AddListener(OnBeingChanged);
        }

        private void OnBeingChanged(BeingData b) {
            // Non-player beings are never in the matrix, but each raider, guest or wild
            // animal arriving or dying rebuilt the whole of it. Not IncludeIntoMatrix: a
            // colonist that just died is excluded by it, and their row must go.
            if (b != null && !b.IsPlayer) { return; }
            RebuildIfShowing();
        }

        private void RebuildIfShowing() {
            if (widget.gameObject.activeSelf) {
                BuildContent();
            }
        }

        private bool ToggleMatrix(bool show) {
            if (show) {
                BuildHeaders();
                BuildContent();
            }
            widget.Toggle(show);
            return true;
        }

        private void OnHide() {
            CancelBuild();
            contentLife?.Dispose();
            contentLife = null;
        }

        private void CancelBuild() {
            if (buildCrt != null) {
                Coroutines.Cancel(buildCrt);
                buildCrt = null;
            }
        }

        private void BuildHeaders() {
            if (headerButtons != null) { return; }
            sortedJobs = JobType.Public.Values.OrderBy(j => j.NameT).ToList();
            headerButtons = new List<WButtonIconText>(sortedJobs.Count);
            foreach (var job in sortedJobs) {
                var j = job;
                var btn = UISpawn.WidgetPermanent<WButtonIconText>(
                    UIPrefabId.WJobMatrixHeaderButton,
                    widget.TopContainer.Container);
                btn.Load(j.IconId, j.NameT, () => OnHeaderClick(j));
                headerButtons.Add(btn);
            }
        }

        private void BuildContent() {
            CancelBuild();
            contentLife?.Dispose();

            var allBeings = S.Query.AllBeingData.Ask();
            var orderedBeings = allBeings.Values
                .Where(IncludeIntoMatrix)
                .OrderBy(b => b.Species.IsBiological)
                .ThenBy(b => b.Species.IsAnimal)
                .ThenBy(b => b.CategoryTitle)
                .ThenBy(b => b.Species.Name)
                .ThenBy(b => b.Mind.Name.ShortName)
                .ToList();

            contentLife = Lifetime.Create("JobMatrixContent",
                capacity: allBeings.Count * JobType.All.Count + 32);

            buildCrt = Coroutines.Start(BuildContentAsync(orderedBeings),
                this, "JobMatrixBuildContent", false, false);
        }

        private IEnumerator BuildContentAsync(List<BeingData> beings) {
            var cont = widget.Content.Container;
            int idx = 0;
            string lastCat = null;

            foreach (var b in beings) {
                if (contentLife == null) { yield break; }
                var cat = b.CategoryTitle;
                if (lastCat != cat) {
                    lastCat = cat;
                    var header = UISpawn.Widget<WText>(
                        contentLife, UIPrefabId.WContent2Header, cont, idx++);
                    header.Load(cat);
                }
                var row = UISpawn.Widget<WJobMatrixRow>(
                    contentLife, UIPrefabId.WJobMatrixRow, cont, idx++);
                var name = BeingUtils.DecoratedBeingName(b, true, b.Mind.Name.ShortName);
                var being = b;
                row.Load(null as string, name, () => BeingUtils.Select(being, true));
                b.Avatar.Load(row.BeingBtn.Image);
                UIUtils.AddTipFunc(contentLife, row.BeingBtn, () => being.UI.GetFullBeingTooltip());
                ConfigureJobToggles(b, row);
                if (idx % 15 == 0) {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(cont);
                    yield return null;
                }
            }

            if (contentLife != null) {
                AddNanobotRow(ref idx, cont);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(cont);
            buildCrt = null;
        }

        private void ConfigureJobToggles(BeingData b, WJobMatrixRow row) {
            row.EnsureCells(sortedJobs.Count);
            for (int i = 0; i < sortedJobs.Count; i++) {
                var job = sortedJobs[i];
                var cell = row.GetCell(i);
                // Incapable cells stay visible, like in the being's Jobs window: a red cross that
                // refuses clicks (JobSlot.CyclePriority) and says why on hover
                if (b.Mind.Jobs.TryJob(job.IdHash, out var slot)) {
                    var being = b;
                    var s = slot;
                    // A slot that only lacks abilities keeps its priority, so it shows none
                    cell.Load(LevelText(s), JobPriorityUI.Text(s),
                        () => JobPriorityUI.Cycle(being, s, 1),
                        () => JobPriorityUI.Cycle(being, s, -1));
                    // level and incapability change too, like in the being's Jobs subpanel
                    slot.ChangedB.Bind(contentLife, () => {
                        cell.SetText(LevelText(s));
                        cell.SetPriority(JobPriorityUI.Text(s));
                    });
                    AddCellTooltip(b, slot, cell);
                    cell.SetVisible(true);
                } else {
                    cell.SetTooltip(null);
                    cell.SetVisible(false);
                }
            }
        }

        private static string LevelText(JobSlot slot) {
            slot.GetNameAndLevelText(out _, out var lvlText, out _);
            if (slot.Incapable) { return Texts.Red(lvlText); }
            if (slot.Loves) { return Texts.Accented(lvlText); }
            return lvlText;
        }

        private void AddCellTooltip(BeingData b, JobSlot slot, WJobMatrixPriority cell) {
            cell.SetTooltip(() => {
                var sb = Caches.StringBuilder;
                sb.Clear();
                sb.Append(b.Name);
                sb.Append(' ');
                sb.Append(GlyphMap.ArrowRight);
                sb.Append(' ');
                sb.AppendLine(Texts.Accented(slot.Job.NameT));
                if (slot.Incapable) {
                    sb.Append(Texts.Red(T.AdjIncapable));
                    JobAffinities.AppendIncapableReasons(slot, sb);
                    sb.AppendLine();
                    return sb.ToString();
                }
                if (slot.Loves) {
                    sb.Append(Texts.Green(T.VerbLoves));
                    JobAffinities.AppendLovesReasons(slot, sb);
                    sb.AppendLine();
                }
                if (JobAffinities.HasPartialAbilities(slot)) {
                    JobAffinities.AppendPartialAbilities(slot, sb);
                    sb.AppendLine();
                }
                if (!slot.Job.Unskilled) {
                    var lvl = T.JobLevel_V1.F(slot.Level);
                    if (slot.ShownProgress >= 0.01f) {
                        var progress = Units.Percentage(slot.ShownProgress);
                        progress = $"({progress}{GlyphMap.ArrowRight}{slot.Level + 1})";
                        sb.AppendFormat("{0} {1}", lvl, Texts.Grey(progress));
                        sb.AppendLine();
                    } else {
                        sb.AppendLine(lvl);
                    }
                }
                sb.Append(JobPriorityUI.TooltipLines(slot));
                return sb.ToString();
            });
        }

        private void AddNanobotRow(ref int idx, UnityEngine.RectTransform cont) {
            if (!S.Research.IsUnlocked(DefIdsH.ResearchRoboticsNanobots)) {
                return;
            }
            var header = UISpawn.Widget<WText>(
                contentLife, UIPrefabId.WContent2Header, cont, idx++);
            header.Load(T.ObjMaterialsNanobots);

            var row = UISpawn.Widget<WJobMatrixRow>(
                contentLife, UIPrefabId.WJobMatrixRow, cont, idx++);
            row.Load(GraphicsId.ObjectsDevicesNanobotHive, T.ObjMaterialsNanobots,
                OnNanobotClick);
            UIUtils.AddTipFunc(contentLife, row.BeingBtn,
                () => T.ObjMaterialsNanobotsDesc);

            row.EnsureCells(sortedJobs.Count);
            for (int i = 0; i < sortedJobs.Count; i++) {
                var job = sortedJobs[i];
                var cell = row.GetCell(i);
                cell.SetTooltip(null);
                if (job.Flags.HasFlag(JobTypeFlags.NanobotCompatible)) {
                    var j = job;
                    var c = cell;
                    cell.SetVisible(true);
                    // nanobots have no priorities: either click turns the job on or off
                    void Flip() {
                        var on = !S.Query.IsNanobotsJobEnabled.Ask(j.IdHash);
                        S.Query.ToggleNanobotsJob.Ask(j.IdHash, on);
                        c.SetPriority(NanobotCellText(on));
                    }
                    cell.Load(null, NanobotCellText(S.Query.IsNanobotsJobEnabled.Ask(job.IdHash)),
                        Flip, Flip);
                } else {
                    cell.SetVisible(false);
                }
            }
        }

        private static string NanobotCellText(bool on) {
            return on ? GlyphMap.SpriteCheck : Texts.Grey("-");
        }

        private void OnNanobotClick() {
            nanobotHives.Clear();
            var count = S.Components.FindAllMatching(nanobotHives,
                h => h.IsConstructed);
            if (count > 0) {
                if (lastHiveIdx >= count) {
                    lastHiveIdx = 0;
                }
                var hive = nanobotHives[lastHiveIdx++];
                if (hive != null) {
                    S.Sig.SelectEntity.Send(hive.Tile);
                }
            }
        }

        private void OnHeaderClick(JobType job) {
            var allBeings = S.Query.AllBeingData.Ask();
            var slots = ListPool<JobSlot>.Get();
            slots.Clear();
            bool wantOff = true;
            foreach (var b in allBeings.Values) {
                if (!IncludeIntoMatrix(b)) { continue; }
                if (b.HasCondition(ConditionIdH.MentalBreakdown)) { continue; }
                if (b.Mind.Jobs.TryJob(job.IdHash, out var jobSlot)) {
                    if (jobSlot.Incapable) { continue; }
                    slots.Add(jobSlot);
                    if (jobSlot.Priority == 0) {
                        wantOff = false;
                    }
                }
            }

            if (slots.Count == 0) {
                UISounds.PlayActionDenied();
                ListPool<JobSlot>.Release(slots);
                return;
            }

            foreach (var slot in slots) {
                if (wantOff && slot.Priority != 0) {
                    slot.ToggleJob();
                } else if (!wantOff && slot.Priority == 0) {
                    slot.ToggleJob();
                }
            }
            slots.Clear();
            ListPool<JobSlot>.Release(slots);
        }

        private static bool IncludeIntoMatrix(BeingData b) {
            if (b.HasStateFlag(BeingState.Dead)) { return false; }
            if (b.HasStateFlag(BeingState.InColony)) { return false; }
            if (b.Body.HasFlag(BodyFlags.Missing)) { return false; }
            if (!b.IsPlayer) { return false; }
            return true;
        }

        public void Cleanup() {
            CancelBuild();
            if (headerButtons != null) {
                foreach (var btn in headerButtons) {
                    UnityEngine.Object.Destroy(btn.gameObject);
                }
                headerButtons = null;
            }
            sortedJobs = null;
            contentLife?.Dispose();
            contentLife = null;
            nanobotHives.Clear();
        }
    }
}
