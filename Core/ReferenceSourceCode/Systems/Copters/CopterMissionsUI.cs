using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Copters {
    public sealed class CopterMissionsUI {
        private readonly GameState S;
        private Lifetime lifetime;
        private WPanel widget;
        private Vector2? widgetPos;
        private Vector2 widgetSize;
        private WIconText2 availableCoptersRow;
        private WIconText2 ongoingMissionsRow;
        private WContentBlock missionBlock;
        private Lifetime missionRowsLifetime;
        private readonly Dictionary<int, WButtonIconText2> missionRows = new();
        private readonly List<int> missionIds = new();
        private WContentBlock minerBlock;
        private Lifetime minerRowsLifetime;
        private readonly Dictionary<int, WContentButton2Text2> minerRows = new();
        private readonly Dictionary<int, int> minerShownAmounts = new();
        private readonly List<int> minerSiteIds = new();
        private readonly List<int> scannedSiteIds = new();

        public CopterMissionsUI(GameState s) {
            S = s;
        }

        public bool IsVisible => lifetime != null && widget != null;

        public void Show() {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = Lifetime.Create("CopterMissionsUI");
            widget = UISpawn.Widget<WPanel>(lifetime, UIPrefabId.WListChoicePopup,
                UIPoolRoot.PopupContainer, 0);
            widget.Rect.SetAsLastSibling();
            widget.Load(T.CopterMissions, () => {
                S.Sig.HideTargetOverlay.Send(CoptersSys.Overlay);
                Close();
            });
            widget.ShowFooter(false);

            RebuildContents();

            if (widgetPos.HasValue) {
                widget.Rect.anchoredPosition = widgetPos.Value;
                widget.Rect.sizeDelta = widgetSize;
            } else {
                UIUtils.AfterShowPopup(widget.Rect, true);
            }
        }

        public void Refresh() {
            if (!IsVisible) {
                return;
            }
            UpdateSummary();
            if (NeedsMissionRebuild()) {
                RebuildMissionRows();
            } else {
                UpdateMissionRows();
            }
            if (NeedsMinerRebuild()) {
                RebuildMinerRows();
            } else {
                UpdateMinerRows();
            }
        }

        public void Close() {
            if (widget != null) {
                widgetPos = widget.Rect.anchoredPosition;
                widgetSize = widget.Rect.sizeDelta;
            }
            availableCoptersRow = null;
            ongoingMissionsRow = null;
            missionBlock = null;
            missionRowsLifetime = null;
            missionRows.Clear();
            missionIds.Clear();
            minerBlock = null;
            minerRowsLifetime = null;
            minerRows.Clear();
            minerShownAmounts.Clear();
            minerSiteIds.Clear();
            widget = null;
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }

        private void RebuildContents() {
            int blockIdx = 0;
            var overviewBlock = UISpawn.Block(lifetime, widget.Content, blockIdx++);
            int idx = 0;
            availableCoptersRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, overviewBlock.Container, idx++);
            ongoingMissionsRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, overviewBlock.Container, idx++);

            missionBlock = UISpawn.Block(lifetime, widget.Content, blockIdx++);
            RebuildMissionRows();
            minerBlock = UISpawn.Block(lifetime, widget.Content, blockIdx++);
            RebuildMinerRows();
            UpdateSummary();
        }

        private void UpdateSummary() {
            if (availableCoptersRow == null || ongoingMissionsRow == null) {
                return;
            }
            availableCoptersRow.Load(IconId.CCopter, T.AvailableCopters,
                Units.Num(S.Components.CountOfConstructed<CopterComp>()));
            ongoingMissionsRow.Load(IconId.CCopter, T.OngoingMissions,
                Units.Num(S.Sys.Copters.OngoingMissionCount));
        }

        private bool NeedsMissionRebuild() {
            int missionCount = S.Sys.Copters.OngoingMissionCount;
            if (missionIds.Count != missionCount) {
                return true;
            }
            int idx = 0;
            for (int i = S.Sys.Copters.Missions.Count - 1; i >= 0; i--) {
                if (missionIds[idx++] != S.Sys.Copters.Missions[i].Id) {
                    return true;
                }
            }
            return false;
        }

        private void RebuildMissionRows() {
            if (missionBlock == null) {
                return;
            }
            // Rows of the last rebuild go with their own lifetime, or every rebuild stacked
            // a new list under the old one
            missionRowsLifetime?.Dispose();
            missionRowsLifetime = lifetime.CreateChild("CopterMissionRows");
            var life = missionRowsLifetime;
            missionRows.Clear();
            missionIds.Clear();

            int idx = 0;
            var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header,
                missionBlock.Container, idx++);
            header.Load(T.OngoingMissions);

            if (S.Sys.Copters.OngoingMissionCount == 0) {
                var empty = UISpawn.Widget<WIconText2>(life,
                    UIPrefabId.WContent2Text2, missionBlock.Container, idx);
                empty.Load(IconId.CWarning, T.NotAvailable, null);
                return;
            }

            for (int i = S.Sys.Copters.Missions.Count - 1; i >= 0; i--) {
                var mission = S.Sys.Copters.Missions[i];
                var row = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, missionBlock.Container, idx++);
                row.Load(IconId.CCopter, string.Empty, string.Empty,
                    () => FocusMission(mission.Id));
                UIUtils.AddTipFunc(life, row, () => MissionTooltip(mission.Id));
                missionRows[mission.Id] = row;
                missionIds.Add(mission.Id);
            }
            UpdateMissionRows();
        }

        private void UpdateMissionRows() {
            foreach (var missionId in missionIds) {
                if (!missionRows.TryGetValue(missionId, out var row)) {
                    continue;
                }
                if (!S.Sys.Copters.TryGetMission(missionId, out var mission)) {
                    continue;
                }
                row.SetText(CopterMissionUtils.MissionTitleFor(S, mission) ?? T.Unknown);
                row.SetText2(CopterMissionUtils.MissionDurationFor(mission));
            }
        }

        // Every deployed auto miner, wherever it is. Recalling one used to mean finding its
        // deposit on the starmap, region by region (STA-1794)
        private void ScanMinerSites(List<int> into) {
            into.Clear();
            var planets = S.Sys.Planets;
            for (int i = 0; planets.TryGetResource(i, out var res); i++) {
                if (res.IsValid && res.AutoMineSpeed > 0f) {
                    into.Add(res.Id);
                }
            }
        }

        private bool NeedsMinerRebuild() {
            ScanMinerSites(scannedSiteIds);
            if (scannedSiteIds.Count != minerSiteIds.Count) {
                return true;
            }
            for (int i = 0; i < scannedSiteIds.Count; i++) {
                if (scannedSiteIds[i] != minerSiteIds[i]) {
                    return true;
                }
            }
            return false;
        }

        private void RebuildMinerRows() {
            if (minerBlock == null) {
                return;
            }
            minerRowsLifetime?.Dispose();
            minerRowsLifetime = lifetime.CreateChild("CopterMinerRows");
            var life = minerRowsLifetime;
            minerRows.Clear();
            minerShownAmounts.Clear();
            ScanMinerSites(minerSiteIds);
            minerBlock.SetActiveFast(minerSiteIds.Count > 0);
            if (minerSiteIds.Count == 0) {
                return;
            }

            int idx = 0;
            var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header,
                minerBlock.Container, idx++);
            header.Load(T.ResearchMiningautomation);
            foreach (var soId in minerSiteIds) {
                var so = S.Universe.Find(soId);
                if (so == null || !S.Sys.Planets.TryGetResourceFor(so, out _, out var mt)) {
                    continue;
                }
                var row = UISpawn.Widget<WContentButton2Text2>(life,
                    UIPrefabId.WContent2Button2Text2, minerBlock.Container, idx++);
                row.Item.Load(mt.IconId, mt.NameT, null, () => S.Sys.Universe.ShowInStarmap(so));
                row.Button.Load(T.VerbRecall, () => RecallMiner(so));
                UIUtils.AddTip(life, row.Button, T.RetrieveResourceMiner);
                minerRows[soId] = row;
            }
            UpdateMinerRows();
        }

        private void UpdateMinerRows() {
            foreach (var kv in minerRows) {
                var so = S.Universe.Find(kv.Key);
                if (so == null || !S.Sys.Planets.TryGetResourceFor(so, out var res, out _)) {
                    continue;
                }
                var row = kv.Value;
                row.Button.SetActiveFast(S.Sys.Copters.CanRetrieveMinerFrom(so));
                var amount = (int) res.Unretrieved;
                if (minerShownAmounts.TryGetValue(kv.Key, out var shown) && shown == amount) {
                    continue;
                }
                minerShownAmounts[kv.Key] = amount;
                var storage = Units.XOutOfY(amount, res.UnretrievedMax);
                var where = so.Parent?.DisplayName;
                row.Item.SetText2(where != null ? $"{storage}, {where}" : storage);
            }
        }

        private void RecallMiner(SpaceObject so) {
            if (!S.Sys.Copters.TryOrderMinerRetrievalFrom(so)) {
                UISounds.PlayActionDenied();
                return;
            }
            UISounds.PlayDigitalConfirm();
        }

        private string MissionTooltip(int missionId) {
            if (!S.Sys.Copters.TryGetMission(missionId, out var mission)) {
                return T.NotAvailable;
            }
            return CopterMissionUtils.MissionTooltipFor(S, mission);
        }

        private void FocusMission(int missionId) {
            if (!S.Sys.Copters.TryGetMission(missionId, out var mission)) {
                return;
            }
            if (CopterMissionUtils.GetSiteSO(S, mission, out var targetSO)) {
                S.Sys.Universe.ShowInStarmap(targetSO);
                return;
            }
            if (CopterMissionUtils.GetCopterSO(S, mission, out var copterSO)) {
                S.Sys.Universe.ShowInStarmap(copterSO);
            }
        }
    }
}
