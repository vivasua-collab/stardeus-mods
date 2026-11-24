using Game.Constants;
using Game.Data;
using Game.CodeGen;
using Game.Data.Planets;
using KL.Utils;
using Game.Utils;
using KL.Randomness;
using Game.Components.Upgrades;
using Game.Input;
using Game.Data.Space;
using Game.Systems.Stats;
using Game.Systems.Planets;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class RemoteDrillComp : BaseComponent<RemoteDrillComp>,
            IRelocatable, IEnergyEfficiencyUpgradeable, ISpeedUpgradeable,
            IWithCPUBonus, IStatProvider, IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new RemoteDrillComp());
        }

        // Harder deposits cost more on top, without a fixed upper bound
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, 0f),
                float.PositiveInfinity);
        }

        private float energyCost;
        private bool isRegistered;
        private float drillProgress;
        private float drillSpeed;
        private string lastMiningText;
        private MatType lastMat;
        private ExtraInfoComp extraInfo;
        private int lastPct;
        /// <summary>
        /// Mine speed fluctuation, so when you have multiple devices they won't
        /// be spawning the output at the same exact rate all the time
        /// </summary>
        private float mineFluc;
        public float MaxDepth;
        private float upgradeEfficiency;
        private float upgradeSpeed;
        private bool isShowingTooDeepIcon;
        private bool isShowingOwnedDepositIcon;

        public bool IsEnergyEfficiencyUpgradeable => true;

        public bool IsSpeedUpgradeable => true;
        private static Curve cpuMaxDepthMulCurve;

        public void HideTooDeepWarning() {
            if (isShowingTooDeepIcon) {
                HideInfoIcon();
                isShowingTooDeepIcon = false;
            }
        }

        private float actualMaxDepth {
            get {
                cpuMaxDepthMulCurve ??= CurveDef.FastGet(CurveIdH.Devices_BeamDrill_CPU_MaxDepth);
                var md = MaxDepth * cpuMaxDepthMulCurve.Evaluate(Tile.ENode.Grid.CPU);
                return md;
            }
        }

        private float lastActualMD;

        public bool TryDrill(MatType mt, ref PlanetResource resource) {
            if (resource.FactionIdH != 0 && resource.FactionIdH != FactionIdH.Player) {
                if (!isShowingOwnedDepositIcon) {
                    var so = S.Universe.Find(resource.Id);
                    if (so != null) {
                        ShowOwnedDepositWarning(so);
                    }
                    StopDrilling();
                }
                return false;
            } else {
                HideOwnedDepositWarning();
            }

            if (!S.Sys.Areas.IsOutdoors(Tile.PosIdx)) {
                HideTooDeepWarning();
                StopDrilling();
                return false;
            }
            var md = actualMaxDepth;
            if (mt.Deposit.IsWithDepth && resource.MiningDepth > md) {
                StopDrilling();
                if (!isShowingTooDeepIcon || !Maths.Approximately(lastActualMD, md)) {
                    lastActualMD = md;
                    isShowingTooDeepIcon = true;
                    var text = $"{T.MiningDepthMax}: {Units.Depth(md)} ({mt.Def.CodexLink} {Texts.Grey(GlyphMap.ArrowRight)} {T.NounDepth}: {Texts.Red(Units.Depth(resource.MiningDepth))})";
                    ShowInfoIcon(IconId.CWarning, text);
                }
                return false;
            } else {
                HideTooDeepWarning();
            }
            if (!Tile.ENode.IsPowerable) {
                drillProgress = 0f;
                UpdateDrillProgress(null);
                return false;
            }
            var elCost = energyCost + (resource.MiningDifficulty * 0.2f);
            if (upgradeEfficiency > 0f) {
                elCost *= 1f - upgradeEfficiency;
            }
            Tile.ENode.SetConsumerWantedInput(elCost);
            if (!Tile.ENode.IsConsuming) { return false; }
            bool mined = false;
            var prog = Tunables.RemoteDrillSpeed * drillSpeed / resource.MiningDifficulty;
            if (upgradeSpeed > 0f) {
                prog *= 1f + upgradeSpeed;
            }
            if (mineFluc == 0f) {
                mineFluc = Rng.URange(0.95f, 1.05f);
            }
            prog *= mineFluc;
            drillProgress += prog;
            // D.Warn("Drill porg: {0}", drillProgress);
            if (drillProgress >= 1f) {
                drillProgress = 0.0001f;
                EntityUtils.SpawnRawMaterial(mt, 1, Tile.Transform.OutPort, 0.1f);
                S.Sig.MatProduced.Send(mt, 1);
                var depthMul = mt.Deposit.IsWithDepth
                    ? MiningDifficultyCurve.Evaluate(resource.MiningDifficulty)
                    : 1f;
                resource.MiningDifficulty += Tunables.RemoteMiningDifficultyIncrease;
                if (mt.Deposit.IsWithDepth) {
                    resource.MiningDepth += Tunables.RemoteMiningDepthIncreaseRate * depthMul;
                }
                mineFluc = 0f;
                mined = true;
            }
            UpdateDrillProgress(mt);
            return mined;
        }

        public void HideOwnedDepositWarning() {
            if (isShowingOwnedDepositIcon) {
                HideInfoIcon();
                isShowingOwnedDepositIcon = false;
            }
        }

        private void ShowOwnedDepositWarning(SpaceObject deposit) {
            if (!isShowingOwnedDepositIcon) {
                isShowingOwnedDepositIcon = true;
                ShowInfoIcon(IconId.CStop,
                    $"{deposit.StarmapLink} {GlyphMap.ArrowRight} {T.OwnedByFaction} ({deposit.Faction.NameWithGlyph})");
            }
        }

        private void UpdateDrillProgress(MatType mt) {
            if (mt == null) {
                HideInfoBar();
                extraInfo.HideInfoIcon();
                return;
            }
            var dp = (int) (drillProgress * 100f);
            if (lastMat != mt || lastPct != dp) {
                lastMiningText = $"{T.JobTypeMining}: {mt.Def.CodexLink}";
                lastMat = mt;
                lastPct = dp;
                ShowInfoBar(Consts.ColorGreenBar, drillProgress,
                    $"{T.NounProgress}: {Units.Percentage(drillProgress)}");
                extraInfo.ShowInfoIcon(mt.IconId, lastMiningText);
            }
        }

        public void StopDrilling() {
            if (drillProgress != 0f) {
                drillProgress = 0f;
                Tile.ENode.SetConsumerWantedInput(0f);
                UpdateDrillProgress(null);
            }
        }
        public override void OnRemove() {
            RegisterDrill(false);
        }
        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                RegisterDrill(true);
            } else if (message == MsgIdH.ElectricNodeSwitchedOff) {
            } else if (message == MsgIdH.RelocationStateChanged) {
                if (Tile.Reinstallable.IsMidRelocation) {
                    RegisterDrill(false);
                } else if (Tile.IsConstructed) {
                    RegisterDrill(true);
                }
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("Progress", drillProgress);
        }

        protected override void OnLoad(ComponentData data) {
            drillProgress = data.GetFloat("Progress", 0f);
        }

        public override void OnLateReady(bool wasLoaded) {
            Tile.Transform.EnsurePortPresence(false, false, true, this);
            if (!entity.TryGetComponent(out extraInfo)) {
                D.Err("ExtraInfo component is required for RemoteDrill! {0}",
                    entity.Definition.Id);
            }
            if (Tile.IsConstructed && !isRegistered) {
                if (wasLoaded) {
                    S.Clock.OnNextUpdate(() => RegisterDrill(true));
                } else {
                    RegisterDrill(true);
                }
            }
        }

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost);
            drillSpeed = Config.GetFloat(PropertyIdH.Speed);
            MaxDepth = Config.GetFloat(PropertyIdH.MiningDepth);
        }

        private void RegisterDrill(bool add) {
            // D.Warn("Reg drill: {0} | {1}", add, Tile);
            if (add && !Tile.IsActive) {
                D.Err("Trying to add inactive drill! {0}", Tile);
                add = false;
            }
            if (isRegistered == add) { return; }
            isRegistered = add;
            if (!S.Query.RegisterRemoteDrill.Ask(this, add)) {
                D.Err("Failed to register drill! {0}", Tile);
            }
        }

        public override string ToString() {
            return " * RemoteDrill";
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<RemoteDrillComp>(out var other)) {
                other.drillProgress = drillProgress;
            }
        }

        public void ApplyEfficiencyUpgrade(float amt) {
            upgradeEfficiency += amt;
        }

        public void ApplySpeedUpgrade(float amt) {
            upgradeSpeed += amt;
        }

        public float GetCPUBonusMultiplier(out string statName, out string finalValue) {
            statName = T.NounDepth;
            cpuMaxDepthMulCurve ??= CurveDef.FastGet(CurveIdH.Devices_BeamDrill_CPU_MaxDepth);
            finalValue = Units.Depth(actualMaxDepth);
            return cpuMaxDepthMulCurve.Evaluate(Tile.ENode.Grid.CPU);
        }

        public string StatsCategoryName => T.ObjectsDevicesBeamdrill;
        public int StatsOrder => 100;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var w = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContentText2, block.Container, idx++);
            w.Load(IconId.CDrill, T.MiningDepthMax, Units.Depth(actualMaxDepth));
        }
    }
}
