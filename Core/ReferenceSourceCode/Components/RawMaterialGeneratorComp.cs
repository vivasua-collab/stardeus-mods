using Game.Data;
using UnityEngine;
using Game.Constants;
using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.Firex;
using Game.Components.Upgrades;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class RawMaterialGeneratorComp : BaseComponent<RawMaterialGeneratorComp>,
            ICopyableComp, IExplosivenessProvider,
            ICanBeUnconfigured, IRelocatable,
            IEnergyEfficiencyUpgradeable, ISpeedUpgradeable, IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            barCache.Clear();
            AddComponentPrototype(new RawMaterialGeneratorComp());
        }
        private static readonly Dictionary<int, string> barCache = new();
        private IMatStorage storage => Tile.MaterialStorage;
        private bool isConfigured;
        private MatType targetMaterial;
        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;
        public string DeviceCategory => T.MenuProduction;
        public Tile DeviceTile => Tile;
        private int lastPCT;
        private float progress;
        private BFloat progressB;
        public BFloat ProgressB => progressB ??= new BFloat(progress);
        private BCallback uiChanged;
        public BCallback UIChanged => uiChanged ??= new BCallback();
        public bool IsConfigured => isConfigured;
        public MatType TargetMaterial => targetMaterial;
        public bool IsIgnoreUnconfiguredOn { get; set; }
        private float generationSpeedMultiplier;
        public bool IsExplosivenessProviderDisabled { get; private set; }
        private float upgradeEnergyEfficiency = 1f;
        private float upgradeSpeed = 1f;

        public bool IsEnergyEfficiencyUpgradeable => true;
        public bool IsSpeedUpgradeable => true;

        public void ApplyEfficiencyUpgrade(float amt) {
            upgradeEnergyEfficiency += amt;
            if (upgradeEnergyEfficiency < 1f) {
                D.Err("Upgrade Efficiency < 1f after apply: {0} -> {1}",
                    amt, upgradeEnergyEfficiency);
                upgradeEnergyEfficiency = 1f;
            }
        }

        public void ApplySpeedUpgrade(float amt) {
            upgradeSpeed += amt;
            if (upgradeSpeed < 1f) {
                D.Err("Upgrade speed < 1f after apply: {0} -> {1}",
                    amt, upgradeSpeed);
                upgradeSpeed = 1f;
            }
        }
        public int ExplosionLevel {
            get {
                if (targetMaterial == null || progress == 0f) { return 0; }
                return Mathf.RoundToInt(
                    Mathf.Lerp(0f, targetMaterial.ExplosionLevel, progress));
            }
        }

        public Curve ExplosivenessCurve {
            get {
                if (targetMaterial == null) { return null; }
                if (Tile.ENode.IsConsuming) {
                    return targetMaterial.ExplosivenessCurve;
                }
                return null;
            }
        }
        private bool isMarkedUnconfigured;
        private bool showingCollectionInfo;


        public bool IsCopyable => true;

        public string CopyConfigText => T.CopyConfigParticleCollector;


        public static float GetEnergyCost(MatType type) {
            if (type.CollectorCost > 0) {
                return type.CollectorCost;
            }
            return Mathf.Max(1000f / type.Rarity,
                // This makes collecting uranium very expensive
                type.EnergyOutput * 0.02f);
        }

        // Over every material the target list offers
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var min = float.MaxValue;
            var max = 0f;
            foreach (var pair in MatType.RawByRarity) {
                var cost = GetEnergyCost(pair.Key);
                if (cost < min) {
                    min = cost;
                }
                if (cost > max) {
                    max = cost;
                }
            }
            if (max > 0f) {
                power.AddConsumption(min, max);
            }
        }
        public override void OnLateReady(bool wasLoaded) {
            Tile.Transform.EnsurePortPresence(false, false, true, this);
        }

        protected override void OnLoad(ComponentData data) {
            isConfigured = data.GetBool("IsConfigured", false);
            var targetMat = data.GetString("TargetMaterial", null);
            progress = data.GetFloat("Progress", 0f);
            IsIgnoreUnconfiguredOn = data.GetBool("IgnoreUnconfigured", IsIgnoreUnconfiguredOn);
            if (targetMat != null) {
                targetMaterial = MatType.Get(targetMat);
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetString("TargetMaterial", targetMaterial?.Id);
            data.SetBool("IsConfigured", isConfigured);
            data.SetBool("IgnoreUnconfigured", IsIgnoreUnconfiguredOn);
            data.SetFloat("Progress", progress);
        }

        private void Generate() {
            var increment = Tunables.ParticleCollectorRate;
            increment *= S.BB.Get(BBKeyH.DustMultiplier, 1f);
            increment *= generationSpeedMultiplier;
            if (upgradeSpeed > 1f) {
                increment *= upgradeSpeed;
            }
            progress += increment * Tile.Health.HealthPercent;
            if (progress > 1f) {
                int amt;
                if (storage == null) {
                    amt = 1;
                } else {
                    // excess
                    amt = storage.Store(targetMaterial, null, 1, null);
                }
                if (amt > 0) {
                    EntityUtils.SpawnRawMaterial(targetMaterial, 1,
                        Tile.Transform.OutPort, 0.1f, true, true);
                    S.Sig.MatProduced.Send(targetMaterial, 1);
                }
                progress = 0f;
            }
            progressB?.Set(progress);
            UpdateProgressBar();
        }
        private void UpdateProgressBar() {
            var pct = Mathf.FloorToInt(progress * 100);
            if (lastPCT == pct) { return; }
            lastPCT = pct;
            if (!barCache.TryGetValue(pct, out var barText)) {
                barText = T.ResourceAccumulationBar_V1.F(Units.Percentage(progress));
                barCache.Add(pct, barText);
            }
            var bar = ShowInfoBar(Consts.ColorFuelBar, progress,
                progress, 1, barText);
            bar.IsPretranslated = true;
        }

        protected override void OnConfig() {
            IsExplosivenessProviderDisabled =
                Config.GetBool(PropertyIdH.DisableExplosivenessProvider, false);
            generationSpeedMultiplier = Config.GetFloat(PropertyIdH.SpeedMultiplier, 1f);
        }
        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    this.elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                }
            }
        }

        private void AfterTickGrid() {
            var canWarn = UnconfiguredWarning.IsApplicable(Tile);
            if (!canWarn) {
                HideUnconfiguredWarning();
            }
            if (!elNode.IsPowerable) { return; }
            if (!isConfigured) {
                showingCollectionInfo = false;
                if (canWarn && !IsIgnoreUnconfiguredOn && !isMarkedUnconfigured) {
                    isMarkedUnconfigured = true;
                    ShowInfoIcon(IconId.CNotConfigured,
                        T.InfoDeviceUnconfigured);
                    S.Sig.UnconfiguredChanged.Send(this, true);
                }
                return;
            } else if (!showingCollectionInfo) {
                ShowCollectionInfo();
            }

            float con = GetEnergyCost(targetMaterial);
            if (upgradeEnergyEfficiency > 1f) {
                con *= 1f / upgradeEnergyEfficiency;
            }
            elNode.SetConsumerWantedInput(con);
            if (!elNode.IsConsuming) { return; }

            Generate();
        }

        private void ShowCollectionInfo() {
            showingCollectionInfo = true;
            ShowInfoIcon(targetMaterial.IconId,
                T.CollectingMat_V1.F(targetMaterial.Def.CodexLink));
        }

        public void Stop() {
            isConfigured = false;
            targetMaterial = null;
            progress = 0f;
            progressB?.Set(progress);
            elNode.SetConsumerWantedInput(0f);
            HideInfoBar();
            HideInfoIcon();
            showingCollectionInfo = false;
            UIChanged?.Invoke();
        }

        public void HideUnconfiguredWarning() {
            if (!isMarkedUnconfigured) { return; }
            isMarkedUnconfigured = false;
            HideInfoIcon();
            S.Sig.UnconfiguredChanged.Send(this, false);
        }

        public bool SetTargetMaterial(MatType material) {
            if (!Tile.IsConstructed || !Entity.IsActive) {
                if (!Tile.Reinstallable.IsMidRelocation) {
                    return false;
                }
            }
            HideInfoIcon();
            showingCollectionInfo = false;
            // check if we had something loaded previously!
            if (isConfigured) {
                if (targetMaterial == material) {
                    return false;
                } else {
                    Stop();
                }
            }
            isConfigured = true;
            targetMaterial = material;
            if (isMarkedUnconfigured) {
                isMarkedUnconfigured = false;
                S.Sig.UnconfiguredChanged.Send(this, false);
                // don't need this because ShowCollectionInfo will overwrite it
                //HideInfoIcon();
            }
            ShowCollectionInfo();
            elNode?.SetConsumerWantedInput(GetEnergyCost(material));
            UIChanged?.Invoke();
            return true;
        }

        public bool CanCopyTo(ICopyableComp target) {
            return target is RawMaterialGeneratorComp m;
        }

        public bool CopyConfigTo(ICopyableComp target) {
            if (target is not RawMaterialGeneratorComp m) {
                return false;
            }
            bool copied = false;
            if (m.IsIgnoreUnconfiguredOn != IsIgnoreUnconfiguredOn) {
                m.IsIgnoreUnconfiguredOn = IsIgnoreUnconfiguredOn;
                if (IsIgnoreUnconfiguredOn) {
                    m.HideUnconfiguredWarning();
                }
                copied = true;
            }
            if (isConfigured) {
                if (targetMaterial != m.targetMaterial) {
                    copied |= m.SetTargetMaterial(targetMaterial);
                }
            } else if (m.isConfigured) {
                m.Stop();
                copied = true;
            }
            return copied;
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<RawMaterialGeneratorComp>(out var gen)) {
                CopyConfigTo(gen);
                gen.progress = progress;
                gen.progressB?.Set(gen.progress);
            }
        }
        public void OnExplode() {
            // nothing to do
        }
        public override string ToString() {
            return " * RawMaterialGenerator";
        }
    }
}
