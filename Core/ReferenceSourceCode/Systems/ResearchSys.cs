// #define RESEARCH_LAYOUT_SYNC
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Research;
using Game.Systems.Hints;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Priority_Queue;
using UnityEngine;
using Game.Systems.UIUX;
using Game.UI.Research;
using System.Threading.Tasks;
using Game.Systems.Notify;

namespace Game.Systems {
    public sealed class ResearchSys : GameSystem, ISaveable {
        public const string SysId = "ResearchSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ResearchSys());
        }
        private ResearchTree tree;
        private IResearchLayout layout;
        [ClearOnReload]
        private static Dictionary<string, ResearchTreeNode> calculatedLayout;
        private Task layoutTask;
        private bool isCalculatingLayout;
        private bool isEventRefreshQueued;
        private readonly List<ResearchProgress> bookmarks = new(8);
        public List<ResearchProgress> Bookmarks => bookmarks;
        public int CurrentDiskUse => tree.UsedDisk;
        public bool IsAnythingBookmarked => bookmarks.Count > 0;

        public string GetName() {
            return Id;
        }

        private void OnLoadSave(GameState s) {
            #if !PRODUCTION_BUILD
            if (!The.TestsAreRunning) {
                new ResearchEvaluator(s).Evaluate(false);
            }
            #else
            new ResearchEvaluator(s).Evaluate(false);
            #endif
            s.Research.InitializeDatoids();
            s.Research.Balance();
            s.Research.ValidateTree();
            s.Research.UpdateSit();
            s.Clock.OnTick.AddListener(OnTick);
            tree = s.Research;
            if (!Arrays.IsEmpty(loadedQ)) {
                for (int i = 0; i < loadedQ.Length; i++) {
                    var researchItem = S.Research.Get(
                            Hashes.S(loadedQ[i]));
                    if (researchItem != null) {
                        // researchItem.GetBookmarkUDB(this);
                        bookmarks.Add(researchItem);
                        researchItem.IsBookmarked = true;
                    }
                }
                loadedQ = null;
            }
            #if RESEARCH_LAYOUT_SYNC
            CalculateLayout();
            #else
            if (calculatedLayout == null) {
                layoutTask = Task.Run(CalculateLayout);
            }
            #endif
            s.UIQuery.GetResearchLayout.SetResponder(GetLayout);
            // UI = new ResearchUI(s, this);
        }

        private Dictionary<string, ResearchTreeNode> GetLayout() {
            if (isCalculatingLayout) {
                D.Warn("Still waiting for layout calculation...");
                layoutTask.Wait();
                if (isCalculatingLayout) {
                    D.Err("Layout is still being calculated even after waiting for task!");
                }
            }
            return calculatedLayout;
        }

        private void CalculateLayout() {
            isCalculatingLayout = true;
            D.Warn("Calculating research layout...");
            var start = GameTimer.realtimeSinceStartupAsDouble;
            var resItems = ResearchTree.All;
            calculatedLayout = new ResearchLayout().Calculate(S, resItems);
            isCalculatingLayout = false;
            D.Warn("Finished calculating research layout in {0:0.00} seconds.",
                GameTimer.realtimeSinceStartupAsDouble - start);
        }

        private void OnTick(long tick) {
            var tickMod = tick % 10;
            // Energy runs on 7
            if (tickMod != 6) { return; }
            if (!Ready.Gen) { return; }

            ValidateCPUCap();
            RefreshResearchEvent();
        }

        private void RefreshResearchEvent() {
            if (S.Research.IsAllUnlocked) {
                // Still need to update that
                if (S.Research.UpdateDatoidCounts()) {

                }
                // UI.UpdateResourceBlocks(true);
                if (researchEvent != null) {
                    researchEvent.Remove(false);
                    researchEvent = null;
                }
                return;
            }
            CheckResearch();
        }

        // Ticks never run while paused, so unlocking or bookmarking then would leave the
        // alert pointing at research that is no longer ready (STA-1611)
        public void QueueResearchEventRefresh() {
            if (isEventRefreshQueued || tree == null) { return; }
            isEventRefreshQueued = true;
            S.Clock.ExecuteAfterFramesForUI(1, RefreshQueuedResearchEvent);
        }

        private void RefreshQueuedResearchEvent() {
            isEventRefreshQueued = false;
            if (!Ready.Gen) { return; }
            RefreshResearchEvent();
        }

        public void NotifyAboutCPUCap(int cap) {
            WPopup.Warning(T.OperationFailed,
                T.CpuCapReached_V1.F(cap));
        }

        // for dev menu
        internal int UnlockCurrentQueue() {
            S.IsAchievementsBlocked = true;
            var unlocked = bookmarks.Count;
            foreach (var r in bookmarks) {
                r.Unlock();
                r.IsBookmarked = false;
            }
            bookmarks.Clear();
            CheckResearch();
            return unlocked;
        }

        private void ValidateCPUCap() {
            if (A.InstantBuild) { return; }
            int cpuCap = S.BB.Get(BBKeyH.CPUCap, 1);
            int cpus = 0;
            bool appliedCPUCap = false;
            var shipComputers = S.Components.AllByType<ShipComputerComp>();
            if (Arrays.IsEmpty(shipComputers)) { return; }
            foreach (ShipComputerComp r in shipComputers) {
                if (r.ENode == null) { continue; }
                if (r.ENode.Grid.Size > 1) {
                    if (++cpus > cpuCap) {
                        appliedCPUCap = true;
                        A.Grid.DisconnectAll(r.ENode);
                        continue;
                    }
                }
            }
            if (appliedCPUCap) {
                NotifyAboutCPUCap(cpuCap);
            }
        }

        public void CheckResearch() {
            int diskBudget = A.Grid.TotalDisk - tree.UsedDisk;

            bool rebuildIfViewing = false;
            if (S.Research.UpdateDatoidCounts()) {
                // counts changed
                S.Research.ClearAvailableCache();
                rebuildIfViewing = true;
            }
            S.Research.UpdateAvailable();
            // We are looking at the queue
            var available = S.Research.Available;
            int unlockableAny = 0;
            int unlockableBookmarked = 0;
            bool isAnyLackingDiskOnly = false;
            for (int i = available.Count - 1; i >= 0; i--) {
                var def = available[i];
                // Seasonally locked nodes can't be unlocked, don't advertise them as ready
                if (!def.IsInSeason) { continue; }
                var prog = S.Research.Get(def.IdH);
                if (!S.Research.IsDiscovered(prog.IdH)) { continue; }
                var unlockable = CheckResources(diskBudget, prog, prog.Researchable,
                    true, S.Research.DatoidCounts, out var isLackingDisk,
                    out var isLackingDatoids);
                if (unlockable) {
                    unlockableAny++;
                    if (prog.IsBookmarked) {
                        unlockableBookmarked++;
                        FocusedProgress = prog;
                    } else {
                        if (unlockableBookmarked == 0) {
                            FocusedProgress = prog;
                        }
                    }
                } else {
                    if (isLackingDisk && !isLackingDatoids) {
                        isAnyLackingDiskOnly = true;
                        FocusedProgress = prog;
                    }
                }
            }

            UpdateResearchEvent(G.Ticks, available.Count, unlockableAny,
                unlockableBookmarked, isAnyLackingDiskOnly);

            if (rebuildIfViewing && UIShowing.Research) {
                S.Sig.ResearchChanged.Send(null);
            }
        }

        public bool IsLackingDatoids(Researchable r,
                Dictionary<MatType, BInt> datoidCounts) {
            if (r.DatoidCosts != null) {
                foreach (var cost in r.AdjustedDatoidCosts(S.Difficulty)) {
                    if (!datoidCounts.TryGetValue(cost.Type, out var available)
                            || available < cost.StackSize) {
                        return true;
                    }
                }
            }
            return false;
        }


        // Returns true if item is now unlockable
        private bool CheckResources(int diskBudget,
                ResearchProgress prog, Researchable r,
                bool wasUpdated, Dictionary<MatType, BInt> datoidCounts,
                out bool isLackingDisk, out bool isLackingDatoids) {
            isLackingDisk = diskBudget < r.Storage;
            prog.IsLackingDisk = isLackingDisk;
            isLackingDatoids = IsLackingDatoids(r, datoidCounts);
            bool canBeUnlocked = !isLackingDisk && !isLackingDatoids;
            // if (UIShowing.Research) {
            //     prog.Changed();
            // } else {
            //     prog.RefreshBookmarksUDB(wasUpdated);
            // }
            return canBeUnlocked;
        }

        public void NotifyCompleted(ResearchProgress prog) {
            var currentDef = prog.Def;
            var name = currentDef.NameT;
            UISounds.PlayResearchComplete();

            var researchLogTitle = T.EvtResearchTitle_V1.F(Texts.LinkToResearch(currentDef, name));
            S.Sys.Log.AddLine(Texts.Green(researchLogTitle));
            S.Research.ClearAvailableCache();
            S.Research.UpdateAvailable();
            D.Warn("Unlocked research on day {0}: {1}.\nDatoid reserves: {2}", GameTimeText.Day,
                currentDef.Id, D.Dump(S.Research.DatoidCounts));
            // Send after UpdateAvailable so dependent nodes' CanBeResearchedNow is
            // already fresh when the research tree widget reacts to this signal.
            S.Sig.ResearchChanged.Send(prog.Def);
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
        }

        private string[] loadedQ;
        private NotificationData researchEvent;

        private ComponentData data;
        private ComponentData Data => data ??= new(0, GetName());

        public Entity Entity => null;
        private readonly B<string> researchEventText = new(null);
        private readonly B<string> researchEventText2 = new(null);
        private string researchEventTip;
        internal string ResearchEventTitle => researchEvent == null ? null : researchEventText.Value;
        public ResearchProgress FocusedProgress;
        private void UpdateResearchEvent(long tick,
                int available,
                int unlockableAny,
                int unlockableBookmarked, bool isAnyLackingDiskOnly) {

            // Everything is unlocked
            if (available == 0) {
                if (researchEvent != null) {
                    researchEvent.Remove(true);
                    researchEvent = null;
                }
                return;
            }
            bool needsShow = researchEvent == null;
            if (unlockableAny < 1) {
                if (isAnyLackingDiskOnly) {
                    EnsureResearchEvent();
                    researchEvent.Type = NotificationType.Warning;
                    researchEventText.Set(Texts.Red(T.ResearchLacksDisk));
                    researchEventTip = null;
                } else {
                    // let's not show any event if we don't have datoids
                    if (researchEvent != null) {
                        researchEvent.Remove(true);
                        researchEvent = null;
                    }
                    return;
                    // researchEventText.Set(Texts.Red(T.ResearchLacksResources));
                    // researchEventTip = null;
                }
            } else {
                EnsureResearchEvent();
                researchEvent.Type = NotificationType.Info;
                if (unlockableBookmarked > 0) {
                    researchEventText.Set(T.ResearchReadyBookmarked);
                    researchEventText2.Set(Units.Num(unlockableBookmarked));
                    researchEventTip = T.ResearchReadyBookmarkedTip_V2.F(
                        unlockableBookmarked, unlockableAny);
                } else {
                    researchEventText.Set(T.ResearchReadyToUnlock);
                    researchEventText2.Set(Units.Num(unlockableAny));
                    researchEventTip = T.ResearchReadyToUnlockTip_V1.F(
                        unlockableAny);
                }
            }
            if (needsShow) {
                researchEvent.Show();
            }
        }

        private void EnsureResearchEvent() {
            if (researchEvent == null) {
                researchEvent = Notification.Build(IconId.CResearch, null,
                    0, NotificationType.Info)
                    .Sticky()
                    .WithOnClick(() => {
                        ShowBestUnlockableResearch();
                    })
                    .WithTitleB(researchEventText)
                    .WithText2B(researchEventText2)
                    .WithTooltipFunc(ResearchEventTip);
            }
        }

        // Same definition of "unlockable" the "Research ready" notification uses:
        // discovered, prerequisites/tier met, in season, and enough disk + datoids.
        public bool CanUnlockNow(ResearchProgress prog) {
            if (prog.IsUnlocked || prog.Researchable == null) { return false; }
            if (!prog.Def.IsInSeason) { return false; }
            S.Research.UpdateAvailable();
            if (!prog.Researchable.CanBeResearchedNow) { return false; }
            if (!S.Research.IsDiscovered(prog.IdH)) { return false; }
            return CheckResources(A.Grid.TotalDisk - tree.UsedDisk, prog, prog.Researchable,
                true, S.Research.DatoidCounts, out _, out _);
        }

        private void ShowBestUnlockableResearch() {
            Def toFocusOn = null;

            ResearchProgress cheapestBookmarked = null;
            for (int i = 0; i < bookmarks.Count; i++) {
                var prog = bookmarks[i];
                if (!CanUnlockNow(prog)) { continue; }
                if (cheapestBookmarked == null
                        || prog.UnlockValue < cheapestBookmarked.UnlockValue
                        || prog.UnlockValue == cheapestBookmarked.UnlockValue
                            && (prog.Storage < cheapestBookmarked.Storage
                                || prog.Storage == cheapestBookmarked.Storage
                                    && ResearchProgress.Comparer.Compare(prog, cheapestBookmarked) < 0)) {
                    cheapestBookmarked = prog;
                }
            }
            toFocusOn = cheapestBookmarked?.Def;

            if (toFocusOn == null) {
                int diskBudget = A.Grid.TotalDisk - tree.UsedDisk;
                var datoidCounts = S.Research.DatoidCounts;
                var available = S.Research.Available;
                ResearchProgress cheapest = null;
                for (int i = available.Count - 1; i >= 0; i--) {
                    var def = available[i];
                    if (!def.IsInSeason) { continue; }
                    var prog = S.Research.Get(def.IdH);
                    if (!S.Research.IsDiscovered(prog.IdH)) { continue; }
                    if (!CheckResources(diskBudget, prog, prog.Researchable,
                            true, datoidCounts, out _, out _)) {
                        continue;
                    }
                    if (cheapest == null
                            || prog.UnlockValue < cheapest.UnlockValue
                            || prog.UnlockValue == cheapest.UnlockValue
                                && (prog.Storage < cheapest.Storage
                                    || prog.Storage == cheapest.Storage
                                        && ResearchProgress.Comparer.Compare(prog, cheapest) < 0)) {
                        cheapest = prog;
                    }
                }
                toFocusOn = cheapest?.Def;
            }

            if (toFocusOn != null) {
                S.UIQuery.ShowInResearchTree.Ask(toFocusOn);
            } else {
                S.UIQuery.ShowResearch.Ask(true);
            }
        }

        private string ResearchEventTip() {
            return researchEventTip;
        }

        public ComponentData Save() {
            var q = new string[bookmarks.Count];
            for (int i = 0; i < bookmarks.Count; i++) {
                q[i] = bookmarks[i].Id;
            }
            Data.SetStringSet("Queue", q);
            return Data;
        }

        public void Load(ComponentData data) {
            loadedQ = data.GetStringSet("Queue", null);
        }

        public void ShowResearch(int researchNodeDefIdH) {
            var prog = S.Research.Get(researchNodeDefIdH);
            if (prog == null) {
                D.Err("Could not find research node {0}",
                    DefIdsH.ResolveName(researchNodeDefIdH));
                return;
            }
            ShowItem(prog);
        }

        private void ShowItem(ResearchProgress r) {
            S.UIQuery.ShowInResearchTree.Ask(r.Def);
        }

        private readonly List<ResearchProgress> prereqs = new(16);

        public bool TryAddBookmark(ResearchProgress progress) {
            if (bookmarks.Contains(progress)) {
                D.Err("Already enqueued: {0}", progress.Id);
                return false;
            }
            prereqs.Clear();
            LoadPrerequisites(progress);
            if (!Arrays.IsEmpty(prereqs)) {
                for (int i = 0; i < prereqs.Count; i++) {
                    if (prereqs[i].IsUnlocked) { continue; }
                    if (bookmarks.Contains(prereqs[i])) { continue; }
                    bookmarks.Add(prereqs[i]);
                    prereqs[i].IsBookmarked = true;
                    prereqs[i].Changed();
                }
            }

            bookmarks.Add(progress);
            bookmarks.Sort(ResearchProgress.Comparer);
            progress.IsBookmarked = true;
            // Ensure that QueueUDB was created for this research
            // progress.GetBookmarkUDB(this);
            progress.Changed();
            RefreshBookmarks();
            QueueResearchEventRefresh();
            return true;
        }

        private void LoadPrerequisites(ResearchProgress progress) {
            var prq = progress.Researchable.Prerequisites;
            if (Arrays.IsEmpty(prq)) { return; }
            foreach (var pr in prq) {
                var item = tree.GetAutoHash(pr);
                if (item == null) { continue; }
                prereqs.Insert(0, item);
                LoadPrerequisites(item);
            }
        }

        public bool TryRemoveBookmark(ResearchProgress progress,
                bool refreshImmediately) {

            if (!bookmarks.Remove(progress)) {
                D.Err("Could not remove bookmark: {0}", progress.Id);
                return false;
            }

            progress.IsBookmarked = false;
            // The node whose bookmark state just changed always repaints; refreshImmediately
            // only governs the *other* bookmarks and the side menu rebuild.
            progress.Changed();
            if (refreshImmediately) {
                RefreshBookmarks();
            }
            QueueResearchEventRefresh();
            return true;
        }

        private void RefreshBookmarks() {
            foreach (var r in bookmarks) {
                r.Changed();
            }
            S.UIQuery.RebuildResearchMenu.Ask();
        }

        public override void Unload() {
        }

        public void ClearBookmarks() {
            for (int i = bookmarks.Count - 1; i >= 0; i--) {
                TryRemoveBookmark(bookmarks[i], false);
            }
            RefreshBookmarks();
        }
    }
}
