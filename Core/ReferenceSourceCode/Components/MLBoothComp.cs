using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Systems.AI;
using Game.Systems.Items;
using KL.Utils;
using Game.UI;
using Game.Systems.Mental;
using System.Linq;
using Game.Components.Upgrades;
using Game.Systems.UIUX;
using Game.Systems.BodyParts;

namespace Game.Components {
    public sealed class MLBoothComp : BaseComponent<MLBoothComp>,
            IAIGoalProvider, ILoadsObj, IRelocatable,
            IEnergyEfficiencyUpgradeable, ISpeedUpgradeable, IWithCPUBonus,
            IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new MLBoothComp());
        }
        private const int DefaultEnergyCost = 50;
        private int energyCost;

        // Training draws the sum over every skill being trained, scaled by its level
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var cost = config.GetInt(PropertyIdH.EnergyCost, DefaultEnergyCost);
            if (cost == 0) {
                cost = DefaultEnergyCost;
            }
            power.AddConsumption(cost * Tunables.MLBoothEnergyCost, float.PositiveInfinity);
        }
        public uint ReservedObjId {
            get {
                if (!IsInputLoaded) {
                    return inputId;
                }
                return 0;
            }
        }
        private const float incapableCostMul = 10f;
        private uint inputId;
        private Dictionary<int, int> wantedJobLevels = new();
        public float CurrentTrainCost;
        private int numSkillsTrained;
        private bool needsCostRecalculation;
        private static Curve cpuToTrainSpeedCurve;
        private ObjData input;
        public ObjData Input {
            get {
                if (input == null) {
                    TryGetObjData(ref input, ref inputId);
                }
                return input;
            }
            set {
                TrySetObjData(ref input, ref inputId, value);
            }
        }

        public uint InputId => inputId;
        public int NumSkillsTrained => numSkillsTrained;

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        private BCallback trainingProgressB;
        public BCallback TrainingProgressB => trainingProgressB ??= new BCallback(capacity: 64);

        public bool SetWantedJobLevel(int jobId, byte level) {
            if (Input == null) {
                D.Err("Cannot configure wanted job level before input is selected!");
                return false;
            }
            EnsureWantedJobLevels();
            if (Input.Mind.Jobs.TryJob(jobId, out var jobSlot)) {
                if (jobSlot.Level > level) {
                    return false;
                }
                if (jobSlot.Job.Unskilled && level > 1) {
                    return false;
                }
            }
            wantedJobLevels[jobId] = level;
            needsCostRecalculation = true;
            stateChangedB?.Invoke();
            return true;
        }

        private void TrySetObjData(ref ObjData input, ref uint inputId, ObjData value) {
            if (value == null) {
                inputId = 0;
            } else {
                if (value.IsAvailable) {
                    inputId = value.Id;
                } else {
                    D.Err("Trying to set objdata when it's not available! {0}", input);
                    inputId = 0;
                    input = null;
                    return;
                }
                needsCostRecalculation = true;
            }
            input = value;
        }

        private bool TryGetObjData(ref ObjData input, ref uint inputId) {
            if (input == null) {
                if (inputId != 0) {
                    var inp = S.Query.FindObjData.Ask(inputId);
                    if (inp == null) {
                        D.Err("Failed getting Mind Transfer obj data by id: {0}", inputId);
                        inputId = 0;
                    } else {
                        input = inp;
                    }
                    needsCostRecalculation = true;
                }
            }
            return input != null;
        }

        public int LoadObjSpotIdx => Tile.Transform.InPort;

        public bool IsInputLoaded;
        public RefillableStorageComp Fuel;
        private float fuelConsumptionPerGridTick;
        private float repairRatePerGridTick;
        private float trainSpeed;
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

        protected override void OnConfig() {
            energyCost = Config.GetInt(PropertyIdH.EnergyCost, DefaultEnergyCost);
            if (energyCost == 0) {
                D.Err("Energy cost cannot be 0 for ML booth!");
                energyCost = DefaultEnergyCost;
            }
            trainSpeed = Config.GetFloat(PropertyIdH.Speed, 3);
            // per hour
            fuelConsumptionPerGridTick = Config.GetFloat(PropertyIdH.FuelPerDay, 3f);
            fuelConsumptionPerGridTick /= Consts.TicksPerDay;
            // we tick grid every 10 ticks
            fuelConsumptionPerGridTick *= 10f;
            repairRatePerGridTick = Config.GetFloat(PropertyIdH.RepairRate, 5f);
            repairRatePerGridTick /= Consts.TicksPerDay;
            repairRatePerGridTick *= 10f;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetUInt("InputId", inputId);
            data.SetBool("IsInputLoaded", IsInputLoaded);
            if (wantedJobLevels.Count == 0) {
                data.SetIntSet("JobLevelIds", null);
                data.SetIntSet("JobLevels", null);
            } else {
                data.SetIntSet("JobLevelIds", wantedJobLevels.Keys.ToArray());
                data.SetIntSet("JobLevels", wantedJobLevels.Values.ToArray());
            }
        }


        protected override void OnLoad(ComponentData data) {
            inputId = data.GetUInt("InputId", 0);
            IsInputLoaded = data.GetBool("IsInputLoaded", false);
            var ids = data.GetIntSet("JobLevelIds", null);
            wantedJobLevels.Clear();
            if (ids != null) {
                var levels = data.GetIntSet("JobLevels", null);
                if (levels != null && levels.Length == ids.Length) {
                    for (int i = 0; i < ids.Length; i++) {
                        var id = ids[i];
                        var level = levels[i];
                        wantedJobLevels[id] = level;
                    }
                    needsCostRecalculation = true;
                }
            }
        }

        public override string ToString() {
            return " * ML Booth";
        }

        public override void OnLateReady(bool wasLoaded) {
            Entity.TryGetComponent(out Fuel);
            Tile.Transform.EnsurePortPresence(false, true, true, this);
            cpuToTrainSpeedCurve ??= CurveDef.FastGet(CurveIdH.Devices_MLBooth_CPU_TrainSpeed);
        }

        public override void OnReady(bool wasLoaded) {
        }
        private ElectricNodeComp elNode;
        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                }
            }
        }

        private bool isShowingIdle;

        private void AfterTickGrid() {
            if (needsCostRecalculation) {
                UpdateCurrentTrainCost();
            }
            if (IsInputLoaded) {
                TickTrainSkill();
            } else {
                TickNoInput();
            }
        }

        private void TickNoInput() {
            elNode.SetConsumerWantedInput(0f);
            if (Input == null) { return; }
            if (!Input.IsAvailableIgnoreReservation) {
                // part that we wanted to load is missing or used elsewhere
                AbortTrain();
                return;
            }
            var wantedPartId = inputId;
            if (wantedPartId != 0 && !HasAnyGoal) {
                var g = CreateGoal(JobTypeIdH.Hauling, IconId.CHaul,
                    $"{T.HaulItem}: {T.NounBodyPart}", this)
                        .WithVar(AIGoalVars.ObjId_Uint, wantedPartId)
                        .ByPlayer(true)
                        .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                        .WithWorldToDesired(AIVarsH.IsObjLoaded, false, true);
                entity.TrySetGoal(g);
            }
        }

        private float GetActualTrainCost() {
            var actualCurrentTrainCost = CurrentTrainCost;
            if (upgradeEnergyEfficiency > 1f) {
                actualCurrentTrainCost *= 1f / upgradeEnergyEfficiency;
            }
            return actualCurrentTrainCost;
        }

        private void TickTrainSkill() {
            if (elNode.HasDeficit) {
                // refresh
                elNode.SetConsumerWantedInput(GetActualTrainCost());
                return;
            }
            bool isConsuming = elNode.IsConsuming;
            if (CurrentTrainCost > 0f) {
                if (isConsuming) {
                    if (!Fuel.TryConsume(fuelConsumptionPerGridTick)) {
                        elNode.SetConsumerWantedInput(0f);
                        return;
                    }
                }
            } else {
                ShowIdle();
                elNode.SetConsumerWantedInput(0f);
                return;
            }
            if (Fuel.CurrentStock >= fuelConsumptionPerGridTick) {
                elNode.SetConsumerWantedInput(GetActualTrainCost());
            } else {
                // out of fuel
                Fuel.CreateOrUpdateRefillGoal();
            }
            if (!isConsuming) { return; }
            var jobs = Input?.Mind?.Jobs;
            if (jobs == null) {
                AbortTrain();
                return;
            }
            bool leveledUp = false;
            bool trained = false;
            bool progressChanged = false;
            foreach (var jl in wantedJobLevels) {
                int wanted = jl.Value;
                if (wanted == 0) { continue; }
                int cur = 0;
                if (jobs.TryJob(jl.Key, out var jobSlot)) {
                    cur = jobSlot.Level;
                    if (cur >= wanted) {
                        continue;
                    }
                } else {
                    // we don't have the job, but if wanted > 0 we should add it
                    if (wanted > 0) {
                        if (!JobType.TryGet(jl.Key, out var jobType)) {
                            D.Err("Failed getting job type: {0}", JobTypeIdH.ResolveName(jl.Key));
                            wantedJobLevels.Remove(jl.Key);
                            return;
                        }
                        jobSlot = jobs.AddJob(jobType, 0);
                    }
                }
                // we want to train the skill
                var lvlBefore = jobSlot.Level;
                int pctBefore = (int) (jobSlot.LevelProgress * 100f);
                var cpuAdjTrainSpeed = cpuToTrainSpeedCurve.Evaluate(elNode.Grid.CPU);
                var actualTrainSpeed = trainSpeed * cpuAdjTrainSpeed *
                    Tunables.MLBoothTrainingSpeed;
                if (upgradeSpeed > 1f) {
                    actualTrainSpeed *= upgradeSpeed;
                }
                jobSlot.TrainSkill(actualTrainSpeed * SkillXp.Default, true);
                trained = true;
                if (lvlBefore < jobSlot.Level) {
                    leveledUp = true;
                }
                int pctAfter = (int) (jobSlot.LevelProgress * 100f);
                if (pctBefore != pctAfter) {
                    progressChanged = true;
                }
            }
            if (leveledUp) {
                UpdateCurrentTrainCost();
                stateChangedB?.Invoke();
            } else if (progressChanged) {
                trainingProgressB?.Invoke();
            }
            if (trained) {
                HealInputPart();
                HideIdle();
            } else {
                ShowIdle();
            }
        }

        private void HealInputPart() {
            var input = Input;
            var part = input?.BodyPart;
            var state = input?.BodyPartState;
            if (part == null || state == null) { return; }
            Wound.TryHeal(state, part.SlotType,
                repairRatePerGridTick * upgradeSpeed);
        }

        private void ShowIdle() {
            if (!isShowingIdle) {
                isShowingIdle = true;
                ShowInfoIcon(IconId.CStop, T.Idle);
            }
        }

        private void HideIdle() {
            if (isShowingIdle) {
                isShowingIdle = false;
                HideInfoIcon();
            }
        }

        public override void OnRemove() {
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
        }

        public bool LoadObj(ObjData objData, AIGoal goal) {
            if (objData.Id == inputId) {
                IsInputLoaded = true;
            } else {
                D.Err("WTF u loading bro? in: {0}, loading: {1} | {2}", inputId, objData, goal);
                return false;
            }
            objData.State = ObjState.InDevice;
            objData.DeviceId = Tile.Id;
            S.Objs.Destroy(objData.Obj);
            UpdateCurrentTrainCost();
            stateChangedB?.Invoke();
            return true;
        }

        public void AbortTrain() {
            if (inputId != 0) {
                if (IsInputLoaded) {
                    if (Input == null) {
                        D.Err("Input is null while inputId was not in AbortTrain! {0}", Tile);
                    } else {
                        Input.State = ObjState.Active;
                        Input.TryUnreserve(null);
                        S.Objs.Create(EntityUtils.CenterOf(LoadObjSpotIdx), Input);
                    }
                }
            }
            wantedJobLevels.Clear();
            elNode.SetConsumerWantedInput(0f);
            CurrentTrainCost = 0f;
            numSkillsTrained = 0;
            IsInputLoaded = false;
            Input = null;
            if (Fuel.TryGetGoal(out var fuelG)) {
                AIGoal.Cancel(fuelG, G.Ticks);
            }
            HideIdle();
            if (TryGetGoal(out var g)) {
                AIGoal.Cancel(g, G.Ticks);
            }
            stateChangedB?.Invoke();
        }

        public void InitializeForNewInput(ObjData selected) {
            Input = selected;
            wantedJobLevels.Clear();
            EnsureWantedJobLevels();
        }

        public void AdjustWantedLevel(int jobId, int dir) {
            var jobs = Input?.Mind?.Jobs;
            if (jobs == null) { return; }
            int minLevel = 0;
            if (jobs.TryJob(jobId, out var jobSlot)) {
                minLevel = jobSlot.Level;
            }
            if (!JobType.TryGet(jobId, out var job)) { return; }
            int maxLevel = job.Unskilled ? 1 : JobSlot.MaxLevel;
            if (!wantedJobLevels.TryGetValue(jobId, out var currentWanted)) {
                currentWanted = minLevel;
            }
            int adjusted = UIUtils.AdjustAmount(currentWanted, dir, minLevel, maxLevel);
            wantedJobLevels[jobId] = adjusted;
            UpdateCurrentTrainCost();
            HideIdle();
            stateChangedB?.Invoke();
        }

        public int GetWantedLevel(int jobId) {
            return wantedJobLevels.TryGetValue(jobId, out var v) ? v : 0;
        }

        public void SetWantedLevelDirect(int jobId, int level) {
            wantedJobLevels[jobId] = level;
            needsCostRecalculation = true;
        }

        public float GetCPUTrainSpeed() {
            if (elNode == null) { return 0f; }
            return cpuToTrainSpeedCurve.Evaluate(elNode.Grid.CPU);
        }

        public float GetCPUBonusMultiplier(out string statName, out string finalValue) {
            statName = T.Speed;
            finalValue = null;
            return GetCPUTrainSpeed();
        }

        public float GetJobEnergyCost(int jobLevel) {
            if (jobLevel == -1) {
                return energyCost * incapableCostMul;
            }
            if (jobLevel == 0) {
                return energyCost;
            }
            return energyCost * (jobLevel + jobLevel);
        }

        public string TitleWithCost(string name, float elCost, float gridOut, float gridUse) {
            return $"{name} [{EnergyCostText(elCost, gridOut, gridUse)}]";
        }

        public string EnergyCostText(float elCost, float gridOut, float gridUse) {
            var gridSpare = Mathf.Clamp(gridOut - gridUse, 0, gridOut);
            string color;
            if (elCost > gridOut) {
                color = Texts.TMPColorRed;
            } else if (elCost > gridSpare) {
                color = Texts.TMPColorYellow;
            } else {
                color = Texts.TMPColorGreen;
            }
            return Texts.WithColor(Units.Electricity(elCost), color);
        }

        private void UpdateCurrentTrainCost() {
            var jobs = Input?.Mind?.Jobs;
            numSkillsTrained = 0;
            needsCostRecalculation = false;
            if (jobs == null) {
                CurrentTrainCost = 0f;
            } else {
                float cost = 0f;
                EnsureWantedJobLevels();
                foreach (var jl in wantedJobLevels) {
                    byte curLevel = 0;
                    int wantLevel = jl.Value;
                    bool incap = true;
                    if (jobs.TryJob(jl.Key, out var jobSlot)) {
                        if (!jobSlot.Incapable) {
                            incap = false;
                        }
                        curLevel = jobSlot.Level;
                    }
                    if (curLevel < wantLevel) {
                        numSkillsTrained++;
                        cost += GetJobEnergyCost(incap ? -1 : curLevel);
                    }
                }
                CurrentTrainCost = cost * Tunables.MLBoothEnergyCost;
            }
        }

        public void EnsureWantedJobLevels() {
            if (wantedJobLevels.Count != 0) { return; }
            var jobs = Input?.Mind?.Jobs;
            if (jobs == null) {
                D.Err("Trying to load initial job levels for null input/mind/jobs: {0} | {1}",
                    Input, Input?.Mind);
                return;
            }
            foreach (var j in JobType.All)  {
                if (jobs.TryJob(j.Key, out var jobSlot)) {
                    wantedJobLevels[j.Key] = jobSlot.Level;
                } else {
                    wantedJobLevels[j.Key] = 0;
                }
            }
        }

        public void AddMindOutputOptions(List<ObjData> items) {
            var objs = S.Query.GetAllObjs.Ask();
            foreach (var obj in ObjData.SortedBy(S, objs.Values, ObjData.SortMode.QualityDesc)) {
                if (!obj.IsAvailable) { continue; }
                if (obj.BodyPart?.HoldsMind != true) { continue; }
                if (obj.BodyPart?.SupportsMLBooth != true) { continue; }
                if (obj.Mind == null) { continue; }
                // We can't allow only pristine minds, because due to
                // corrosion all parts will be of lower quality
                items.Add(obj);
            }
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<MLBoothComp>(out var other)) {
                other.input = input;
                other.inputId = inputId;
                other.IsInputLoaded = IsInputLoaded;
                if (wantedJobLevels.Count > 0) {
                    other.wantedJobLevels = wantedJobLevels;
                    other.UpdateCurrentTrainCost();
                }
            }
        }
    }
}
