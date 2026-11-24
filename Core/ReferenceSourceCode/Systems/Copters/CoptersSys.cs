using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Planets;
using Game.Data.Space;
using Game.Input;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.Copters {
    public sealed class CoptersSys : GameSystem, IOverlayProvider,
            ISaveableSpecial {
        public const string SysId = "CoptersSys";
        public const string Overlay = "Copters";
        public override string Id => SysId;
        public Entity Entity => null;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new CoptersSys());
        }

        private readonly List<CopterMissionData> missions = new();
        public int OngoingMissionCount => missions.Count;
        // For tests
        public List<CopterMissionData> Missions => missions;

        private readonly List<OverlayInfo> overlays = new();
        public List<OverlayInfo> Overlays => overlays;

        private CoptersSysData data;
        private int lastMissionId;
        private CopterMissionsUI ui;

        // Missions whose cargo fits nowhere on the map wait before the next search, since
        // each search scans the whole map. Not saved: after a load the search runs at once.
        private readonly Dictionary<int, long> nextDropSearchTick = new();

        // Where a copter can set down what it carries, or park (CopterDropSpot)
        public bool TryFindDropSpot(in CopterMissionData mission, Def def, Facing.Type rot,
                int near, out int spot) {
            spot = 0;
            var ticks = S.Clock.Ticks;
            if (nextDropSearchTick.TryGetValue(mission.Id, out var next) && ticks < next) {
                return false;
            }
            if (CopterDropSpot.TryFindNear(def, rot, near, out spot)) {
                nextDropSearchTick.Remove(mission.Id);
                return true;
            }
            D.Warn("Nothing on the map fits {0} for copter mission {1}, looking again in an hour",
                def?.Id, mission.Id);
            nextDropSearchTick[mission.Id] = ticks + Consts.TicksPerHour;
            return false;
        }

        public override void Unload() {
            nextDropSearchTick.Clear();
            overlays.Clear();
            missions.Clear();
            ui?.Close();
            ui = null;
        }

        public string GetName() {
            return Id;
        }

        private void OnLoadSave(GameState s) {
            RestoreData();
            ui = new CopterMissionsUI(s);
            s.Clock.OnTick.AddListener(OnTick);
            s.Clock.OnTickProgress.AddListener(OnTickProgress);
        }


        private void RestoreData() {
            missions.Clear();
            if (data == null) { return; }
            lastMissionId = data.LastMissionId;
            Caches.IntSet.Clear();
            if (!Arrays.IsEmpty(data.Missions)) {
                missions.AddRange(data.Missions);
                for (int i = 0; i < missions.Count; i++) {
                    CopterMissionData dep = missions[i];
                    if (dep.Copter.EntityId != 0) {
                        Caches.IntSet.Add(dep.Copter.EntityId);
                    }
                    var minerTileId = dep.Miner.EntityId;
                    if (minerTileId != 0) {
                        var miner = S.Components.Find<ResourceMinerComp>(minerTileId);
                        if (miner == null) {
                            D.Err("Failed finding miner tile by id {0}", minerTileId);
                            dep.SetStage(CopterMissionStageH.Error);
                            // a struct copy: without the write-back the mission kept its stage
                            missions[i] = dep;
                            continue;
                        }
                    }
                }
            }

            var flyingCopters = S.Components.AllByType<CopterFlyingComp>();
            if (flyingCopters != null) {
                foreach (CopterFlyingComp flyingCop in flyingCopters) {
                    if (!Caches.IntSet.Contains(flyingCop.EntityId)) {
                        D.Err("Found stray copter! Parking: {0}", flyingCop.Entity);
                        var park = NewParkMission(flyingCop);
                        missions.Add(park);
                        S.Sig.CopterMissionStarted.Send(park);
                    }
                }
            }
            data = null;
        }

        private bool IsMinerAt(SpaceObject so) {
            return so.Data.GetInt("MinerType", 0) != 0;
        }

        public bool TryOrderCollect(in PlanetResource resource) {
            if (resource.Id == 0) {
                D.Err("Trying to launch a collect mission to resource with id 0! {0}",
                    resource);
                return false;
            }
            if (resource.Unretrieved < 1f) { return false; }
            var newMission = NewMissionTo(resource.Id,
                CopterMissionType.CollectResource);
            missions.Add(newMission);
            S.Sig.CopterMissionStarted.Send(newMission);
            ui?.Refresh();
            return true;
        }

        public CopterMissionData NewMissionTo(int targetId,
                CopterMissionType missionType) {
            return new CopterMissionData() {
                Id = ++lastMissionId,
                Stage = CopterMissionStageH.Start,
                StartTick = G.Ticks,
                TargetId = targetId,
                MissionType = missionType,
            };
        }
        public CopterMissionData NewParkMission(CopterFlyingComp copter) {
            return new CopterMissionData() {
                Id = ++lastMissionId,
                Copter = new CopterData() {
                    EntityId = copter.EntityId,
                    // should work
                    CopterDefId = copter.Entity.DefinitionId.Replace("Flying", ""),
                },
                Stage = CopterMissionStageH.CopterRandomPark,
                StartTick = G.Ticks,
                TargetId = 0,
            };
        }
        public bool TryOrderMinerRetrievalFrom(SpaceObject so) {
            if (!CanRetrieveMinerFrom(so)) { return false; }
            // create mission
            return TryCreateMinerRetrievalMission(so);
        }

        public bool TryOrderMinerDeploymentTo(SpaceObject so) {
            if (!CanDeployMinerTo(so)) { return false; }
            var miner = ResourceMinerComp.FirstAvailable(S);
            if (miner == null) { return false; }
            // create mission
            return TryCreateMinerDeploymentMission(so, miner);
        }

        private bool TryCreateMinerRetrievalMission(SpaceObject so) {
            if (so == null) { return false; }
            var newMission = NewMissionTo(so.Id, CopterMissionType.RetrieveMiner);
            missions.Add(newMission);
            S.Sig.CopterMissionStarted.Send(newMission);
            ui?.Refresh();
            return true;
        }
        private bool TryCreateMinerDeploymentMission(SpaceObject so,
                ResourceMinerComp miner) {
            if (so == null) { return false; }
            if (miner.IsReserved) {
                D.Err("Trying to create a miner deployment mission for a miner that is already reserved! {0}", miner);
            }
            var newMission = NewMissionTo(so.Id, CopterMissionType.DeployMiner);
            newMission.Miner = miner.MissionData;
            miner.IsReserved = true;
            missions.Add(newMission);
            S.Sig.CopterMissionStarted.Send(newMission);
            ui?.Refresh();
            return true;
        }

        public bool HasAnyCopters() {
            // let's assume that yes
            if (missions.Count > 0) {
                return true;
            }
            return S.Components.CountOfConstructed<CopterComp>(max: 1) > 0;
        }

        private void OnTickProgress(float progress) {
            // execute miner deployment missions
            for (int i = missions.Count - 1; i >= 0; i--) {
                CopterMissionRunner.OnTickProgress(S, missions, i);
            }
        }
        private bool hasRemovedUITarget;
        private bool CheckUnlocked() {
            if (S.BB.Get(BBKeyH.MiningAuto, false)) {
                if (!hasRemovedUITarget) {
                    hasRemovedUITarget = true;
                    S.Sig.SetUITarget.Send(null, CameraFocus.FlashColor());
                }
                return true;
            }
            if (A.InstantBuild) {
                return true;
            }
            var msg = T.SysLockedByResearch_V1.F(
                    The.Defs.FastGet(DefIdsH.ResearchSpaceTravelMiningAutomation).Researchable.NameT);
            if (UIShowing.Dialog) {
                WPopup.Create(false, IconId.CCopter, T.NotAvailable, msg).Spawn();
            } else {
                The.Dialog.ShowSimple(IconId.CCopter, msg);
            }
            return false;
        }

        private void OnTick(long ticks) {
            // execute miner deployment missions
            for (int i = missions.Count - 1; i >= 0; i--) {
                CopterMissionRunner.OnTick(S, ticks, missions, i);
            }
            ui?.Refresh();
        }

        protected override void OnInitialize() {
            overlays.Add(new OverlayInfo(
                OverlayType.Controls, Overlay, IconId.WCopter));
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
        }

        private void OnToggleOverlay(OverlayInfo info, bool on) {
            if (!on || info.IdH != OverlayIdH.Copters) {
                ui?.Close();
            } else {
                if (!CheckUnlocked()) { return; }
                ui?.Show();
            }
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Copters ??= new CoptersSysData();
            sd.Copters.LastMissionId = lastMissionId;
            if (missions.Count > 0) {
                sd.Copters.Missions = missions.ToArray();
            } else {
                sd.Copters.Missions = null;
            }
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            data = sd.Copters;
        }

        public bool TryGetMission(int missionId, out CopterMissionData mission) {
            return TryFindMission(missionId, out mission);
        }

        private bool TryFindMission(int missionId, out CopterMissionData mission) {
            for (int i = missions.Count - 1; i >= 0; i--) {
                CopterMissionData m = missions[i];
                if (m.Id == missionId) {
                    mission = m;
                    return true;
                }
            }
            mission = default;
            return false;
        }

        public bool CanDeployMinerTo(SpaceObject so) {
            if (so.Type.IdH == SpaceObjectTypeIdH.ResourceUnknown) {
                return false;
            }
            if (S.Sys.Planets.IsMinableResource(so, out _, out var mat)) {
                // if (!mat.Deposit.IsWithDepth) { return false; }
                if (!IsMinerAt(so) && !HasMissionFor(so)) {
                    return true;
                }
            }
            return false;
        }

        public bool CanRetrieveMinerFrom(SpaceObject so) {
            if (so.Faction != null && !so.Faction.IsPlayer) {
                return false;
            }
            if (S.Sys.Planets.IsMinableResource(so, out _, out var mat)) {
                // if (!mat.IsDepositWithDepth) { return false; }
                if (IsMinerAt(so) && !HasMissionFor(so)) {
                    return true;
                }
            }
            return false;
        }

        public bool HasMissionFor(SpaceObject so) {
            if (so == null) { return false; }
            for (int i = missions.Count - 1; i >= 0; i--) {
                var mission = missions[i];
                if (mission.TargetId == so.Id) {
                    return true;
                }
            }
            return false;
        }

        public bool TryGetMissionFor(SpaceObject so, out CopterMissionData data) {
            for (int i = missions.Count - 1; i >= 0; i--) {
                var mission = missions[i];
                if (mission.TargetId == so.Id) {
                    data = mission;
                    return true;
                }
            }
            data = default;
            return false;
        }

        public bool HasMissionIn(SpaceObject parent) {
            for (int i = missions.Count - 1; i >= 0; i--) {
                var mission = missions[i];
                if (mission.IsDone) { continue; }
                var target = S.Universe.Find(mission.TargetId);
                if (target?.Parent == parent) {
                    return true;
                }
            }
            return false;
        }
    }
}
