using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Components.Upgrades;
using Game.Systems.UIUX;
using KL.Pool;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Components {
    public sealed class TractorBeamComp : BaseComponent<TractorBeamComp>,
            ITickProgressListener, ICopyableComp,
            ICanBeUnconfigured, IRelocatable, IEnergyEfficiencyUpgradeable,
            IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TractorBeamComp());
        }

        // Draws what the pulled projectile costs to divert, bigger asteroids cost more
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(HullpiercerComp.TrajectoryChangeCost, float.PositiveInfinity);
        }

        private const float beamZ = -5f;
        private const string beamPrefabName = "TractorBeamLineVFX";
        private LineRenderer _beamLinePrefab;
        private LineRenderer beamLine;
        private float curConsumption;
        private bool isPowered;
        private bool isAutoTarget;
        private int projectileId;
        private IWithTrajectory _projectile;
        public IWithTrajectory Projectile {
            get => EntityUtils.GetCached(S, ref projectileId, ref _projectile);
            set => EntityUtils.SetCached(ref projectileId, ref _projectile, value);
        }
        private int targetId;
        private int lastTargetPos;
        private TractorBeamTargetComp _target;
        public TractorBeamTargetComp Target {
            get => EntityUtils.GetCached(S, ref targetId, ref _target);
            set {
                EntityUtils.SetCached(ref targetId, ref _target, value);
                if (value != null) {
                    lastTargetPos = value.Tile.PosIdx;
                }
            }
        }
        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;
        public string DeviceCategory => T.TractorBeam;
        public Tile DeviceTile => Tile;
        private bool needAutoTarget;
        private float upgradeEnergyEfficiency = 1f;

        public bool IsEnergyEfficiencyUpgradeable => true;

        public void ApplyEfficiencyUpgrade(float amt) {
            upgradeEnergyEfficiency += amt;
            if (upgradeEnergyEfficiency < 1f) {
                D.Err("Upgrade Efficiency < 1f after apply: {0} -> {1}",
                    amt, upgradeEnergyEfficiency);
                upgradeEnergyEfficiency = 1f;
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    elNode.AddAfterTick(this, AfterTickGrid);
                    needAutoTarget = true;
                }
            } else if (message == MsgIdH.ElectricNodeSwitchedOn) {
                needAutoTarget = true;
            } else if (message == MsgIdH.ElectricNodeConnectionChanged) {
                if (Tile.ENode.IsConnected && EntityUtils.IsWithinMargin(Tile.PosIdx, false, false) && Ready.Gen) {
                    S.UIQuery.ShowTutorialPopup.Ask(TractorBeamPage.PageId, 3f);
                }
            }
        }

        private void StopPullVFX() {
            if (beamLine != null) {
                PrefabPool.Despawn(beamLine.gameObject);
                beamLine = null;
            }
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            if (beamLine == null) { return; }
            var bp = Tile.Position;
            if (Vectors.IsSane(bp)) {
                beamLine.SetPosition(0, new Vector3(bp.x, bp.y, beamZ));
            }
            UpdateTargetBeamLine();
        }

        private void UpdateTargetBeamLine() {
            if (Target == null) { return; }
            var tp = Target.Tile.Position;
            if (Vectors.IsSane(tp)) {
                beamLine.SetPosition(2, new Vector3(tp.x, tp.y, beamZ));
            }
        }

        private void ShowPullVFX() {
            if (beamLine != null) { return; }
            if (Projectile == null) { return; }
            if (_beamLinePrefab == null) {
                _beamLinePrefab = ResourceLibrary.FindPrefab<LineRenderer>(beamPrefabName);
            }
            beamLine = PrefabPool.Spawn(_beamLinePrefab, Consts.V3Zero, ResourceLibrary.Root);
            var bp = EntityUtils.ClampToBounds(Tile.Position);
            var pp = EntityUtils.ClampToBounds(Projectile.Entity.Position);
            var tp = EntityUtils.ClampToBounds(Target.Tile.Position);
            beamLine.positionCount = 3;
            beamLine.SetPosition(0, new Vector3(bp.x, bp.y, beamZ));
            beamLine.SetPosition(1, new Vector3(pp.x, pp.y, beamZ));
            beamLine.SetPosition(2, new Vector3(tp.x, tp.y, beamZ));
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("TargetId", targetId);
            data.SetInt("LastTargetPos", lastTargetPos);
            data.SetInt("ProjectileId", projectileId);
            data.SetBool("IsAuto", isAutoTarget);
        }
        protected override void OnLoad(ComponentData data) {
            projectileId = data.GetInt("ProjectileId", 0);
            targetId = data.GetInt("TargetId", 0);
            lastTargetPos = data.GetInt("LastTargetPos", 0);
            isAutoTarget = data.GetBool("IsAuto", true);
        }

        private bool isSubscribed;
        public override void OnLateReady(bool wasLoaded) {
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.AddObjWithTrajectory.AddListener(OnWithTrajectory);
                S.Sig.TractorBeamTargetBuilt.AddListener(OnBuildTractorBeamTarget);
            }
        }

        private void OnBuildTractorBeamTarget(int pos, TractorBeamTargetComp target) {
            if (Target == null && lastTargetPos == pos) {
                D.Warn("Restored tractor beam target for beam: {0} -> {1}", Tile, target.Tile);
                Target = target;
            }
        }

        public override void OnRemove() {
            if (isSubscribed) {
                S.Sig.AddObjWithTrajectory.RemoveListener(OnWithTrajectory);
                S.Sig.TractorBeamTargetBuilt.RemoveListener(OnBuildTractorBeamTarget);
            }
        }

        private void OnWithTrajectory(IWithTrajectory threat) {
            if (!canAutoPullNow) {
                D.Warn("Beam cannot pull now: {0}: Target: {1}. Projectile: {2}", Tile, Target, Projectile);
            }
            if (!threat.CanChangeTrajectory) {
                D.Warn("Can't change trajectory: {0}", threat);
            }
            if (canAutoPullNow && threat.CanChangeTrajectory) {
                // D.Warn("Setting projectile for {0}: {1}->{2}", Tile, Projectile, threat);
                Projectile = threat;
                S.Sys.Log.AddLine(entity, T.TractorBeamAutoPulling_V3.F(
                    threat.Entity.Definition.CodexLink, Pos.String(threat.Entity.PosIdx),
                        EntityUtils.PosOrLabelOf(Target.Tile)));
                stateChangedB?.Invoke();
            }
        }

        public override string ToString() {
            return " * TractorBeam";
        }

        private bool canAutoPullNow => isAutoTarget && isPowered && Target != null
            && Projectile == null && isOutdoors;

        private bool canPullNow => Target != null
            && Projectile != null && isOutdoors;

        private bool isOutdoors => Tile.Outdoors.IsOutdoors;

        public bool IsCopyable => Tile.ENode.IsReachable;

        public string CopyConfigText => T.TractorBeam;

        public bool IsAutoTarget => isAutoTarget;

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        private bool isMarkedUnconfigured;

        protected override void OnConfig() {
            isAutoTarget = true;
        }

        private void UpdateUnconfigured() {
            var isUnconfigured = Target?.IsValidNow != true
                && UnconfiguredWarning.IsApplicable(Tile);
            if (isUnconfigured && !isMarkedUnconfigured) {
                isMarkedUnconfigured = true;
                S.Sig.UnconfiguredChanged.Send(this, true);
                ShowInfoIcon(IconId.CNotConfigured, T.NoTarget);
            } else if (!isUnconfigured && isMarkedUnconfigured) {
                isMarkedUnconfigured = false;
                S.Sig.UnconfiguredChanged.Send(this, false);
                HideInfoIcon();
            }
        }

        private void AfterTickGrid() {
            if (Target != null && !Target.IsValidNow) {
                SetTargetSpot(null);
            }
            // D.Warn("Pulling");
            var hasTP = canPullNow;
            if (hasTP) {
                curConsumption = Projectile.TrajectoryChangeEnergyCost;
                if (upgradeEnergyEfficiency > 1f) {
                    curConsumption *= 1f / upgradeEnergyEfficiency;
                }
            } else {
                curConsumption = 0f;
            }
            UpdateUnconfigured();
            elNode.SetConsumerWantedInput(curConsumption);
            isPowered = elNode.IsPowerable && !elNode.HasDeficit;
            if (isPowered) {
                if (hasTP) {
                    if (!Projectile.CanChangeTrajectory) {
                        Projectile = null;
                        if (isAutoTarget) {
                            AutoSelectProjectile();
                        }
                    } else {
                        var str = Tunables.TractorBeamStrength;
                        if (!Projectile.InfluenceTrajectory(Target.Entity.Position, str)) {
                            //D.Log("Influenced trajectory of {0}", target.Entity);
                            Projectile.WasAligned = true;
                            S.Sys.Log.AddLine(entity, T.TractorBeamSuccess_V2.F(
                                Projectile.Entity.Definition.CodexLink,
                                EntityUtils.PosOrLabelOf(Target.Tile)));
                            ReleaseProjectile();

                            if (isAutoTarget) {
                                AutoSelectProjectile();
                            }

                        }
                        if (Projectile != null && Projectile.Entity.IsActive
                                && Vectors.IsSane(Projectile.Entity.Position)
                                && EntityUtils.IsWithinBounds(Projectile.Entity.Position)) {
                            ShowPullVFX();
                            Target.SetHasProjectile(true);
                        }
                    }
                } else {
                    StopPullVFX();
                    Target?.SetHasProjectile(false);
                    if (needAutoTarget) {
                        AutoSelectProjectile();
                    }
                }
            } else {
                if (!elNode.IsPowerable) {
                    Target?.SetHasProjectile(false);
                }
                StopPullVFX();
            }
        }

        private void AutoSelectProjectile() {
            needAutoTarget = false;
            if (!canAutoPullNow) { return; }
            int found = S.Components.FindAllMatchingByInterface(
                    Caches.Withtrajectories, (t) => t.CanChangeTrajectory
                        && !t.WasAligned);
            var pos = Tile.Position;
            var targetPos = Target.Tile.Position;
            if (found > 0) {
                IWithTrajectory closest = null;
                float distToClosest = float.MaxValue;
                for (int i = 0; i < found; i++) {
                    var curTr = Caches.Withtrajectories[i];
                    var dist = Vectors.SqrDistance(pos, curTr.Entity.Position);
                    if (dist < distToClosest) {
                        distToClosest = dist;
                        closest = curTr;
                    }
                }
                if (closest != null) {
                    OnWithTrajectory(closest);
                }
            }
        }

        public void ReleaseProjectile() {
            Projectile = null;
            Target?.SetHasProjectile(false);
            StopPullVFX();
            stateChangedB?.Invoke();
        }

        public void SetTargetSpot(TractorBeamTargetComp spot) {
            Target?.SetHasProjectile(false);
            Target = spot;
            if (spot != null && beamLine != null) {
                UpdateTargetBeamLine();
            }
            UpdateUnconfigured();
            if (isAutoTarget) {
                needAutoTarget = true;
            }
            stateChangedB?.Invoke();
        }

        public void ToggleAutoTarget() {
            isAutoTarget = !isAutoTarget;
            if (isAutoTarget && Projectile == null) {
                AutoSelectProjectile();
            }
            stateChangedB?.Invoke();
        }

        public void SelectProjectileManual(IWithTrajectory proj) {
            isAutoTarget = false;
            Projectile = proj;
            stateChangedB?.Invoke();
        }

        public TractorBeamTargetComp[] GetCandidateTargets() {
            var pos = Tile.PosIdx;
            return S.Components.GenFindAllMatching<TractorBeamTargetComp>(
                t => t.IsValidNow)?
                    .OrderBy(c => Pos.SqrDistance(pos, c.Entity.PosIdx)).ToArray();
        }

        public IWithTrajectory[] GetCandidateProjectiles() {
            var pos = Tile.PosIdx;
            return S.Components.GenFindAllMatchingByInterface<IWithTrajectory>(
                t => t.CanChangeTrajectory)
                    .OrderBy(c => Pos.SqrDistance(pos, c.Entity.PosIdx)).ToArray();
        }

        public void TickProgress(float tickProgress) {
            if (!isPowered) { return; }
            var proj = Projectile;
            if (proj != null && Target != null && beamLine != null) {
                if (!proj.Entity.IsActive) {
                    ReleaseProjectile();
                    return;
                }
                if (targetId != 0) {
                    if (!Target.IsValidNow) {
                        SetTargetSpot(null);
                        return;
                    }
                    var tp = EntityUtils.ClampToBounds(Target.Tile.Position);
                    beamLine.SetPosition(2, new Vector3(tp.x, tp.y, beamZ));
                }
                var pp = EntityUtils.ClampToBounds(proj.Entity.Position);
                beamLine.SetPosition(1, new Vector3(pp.x, pp.y, beamZ));
            }
        }

        public bool CopyConfigTo(ICopyableComp target) {
            if (target is TractorBeamComp tb && tb.IsCopyable) {
                return DoCopyConfigTo(tb);
            }
            return false;
        }

        private bool DoCopyConfigTo(TractorBeamComp tb) {
            bool changed = false;
            if (tb.isAutoTarget != isAutoTarget) {
                tb.isAutoTarget = isAutoTarget;
                changed = true;
            }
            if (tb.Target != this.Target) {
                tb.SetTargetSpot(this.Target);
                changed = true;
            }
            if (tb.Projectile != Projectile) {
                tb.Projectile = Projectile;
                changed = true;
            }
            return changed;
        }

        public bool CanCopyTo(ICopyableComp comp) {
            return comp is TractorBeamComp tb
                    && tb.entity.Definition == entity.Definition;
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<TractorBeamComp>(out var other)) {
                DoCopyConfigTo(other);
            }
        }
    }
}
