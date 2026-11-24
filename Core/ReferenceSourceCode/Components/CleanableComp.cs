using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.UI;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.Nanobots;
using Game.Systems.Stats;

namespace Game.Components {
    public sealed class CleanableComp : BaseComponent<CleanableComp>,
            ICopyableComp, IAIGoalProvider, IStatProvider,
            IRelocatable, IAIGoalPriorityListener,
            INanobotCompatible {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new CleanableComp());
        }
        private string goalKey;
        private string textKey;
        private string amountKey;
        private string barKey;
        private string warningKey;
        private string dirtyKey;
        private float dirtyThreshold;
        public float DirtAmount => dirtAmount;
        public string CleanableText => textKey.T();
        public string DirtAmountText => amountKey.T();
        public float DirtPercent => dirtAmount / dirtCapacity;
        private BFloat dirtPercentB;
        public BFloat DirtPercentB => dirtPercentB ??= new BFloat(DirtPercent);
        private float dirtAmount;
        public float DirtAmountBeforeClean;
        private bool hasBar;
        private float dirtToMaterialRate;
        public float DirtToMaterialRate => dirtToMaterialRate;
        public MatType Byproduct;
        public bool SpawnByproductForEverySubPos;
        private int dirtCapacity;
        public int DirtCapacity => dirtCapacity;
        private Color barColor = Color.red;
        public bool IsFull => dirtAmount >= dirtCapacity;
        public string CopyConfigText => T.CopyConfigCleanable;
        public bool IsCopyable => true;
        public float AutocleanAmount;
        public bool AutoHaulByproduct;
        public bool IsAutoClean;
        private ExtraInfoComp extraInfo;
        public bool CanOrderCleaning {
            get {
                if (Tile.ENode != null && !Tile.ENode.IsReachable) {
                    return false;
                }
                return dirtAmount > 0 && !HasGoal;
            }
        }

        public bool HasSubmenuNow => true;
        public string SubmenuTitle => T.Cleanable;
        private bool goalFlyingOnly;

        protected override void OnConfig() {
            hasBar = Config.GetBool(PropertyIdH.HasBar, true);
            if (hasBar) {
                var colorId = Config.GetString(PropertyIdH.BarColor);
                if (Colors.TryFromHex(colorId, out var c)) {
                    barColor = c;
                }
            } else {
                Config.MarkUsed(PropertyIdH.BarColor);
            }
            cleanPriority = Config.GetInt(PropertyIdH.CleanPriority, 3);
            var prodMat = Config.GetString(PropertyIdH.Byproduct, null);

            if (!string.IsNullOrWhiteSpace(prodMat)) {
                Byproduct = MatType.Get(prodMat);
            }
            IsAutoClean = true;
            // TODO make it configurable through UI
            AutoHaulByproduct = Config.GetBool(PropertyIdH.AutoHaulByproduct, true);
            dirtyThreshold = Config.GetFloat(PropertyIdH.DirtyThreshold, 0.5f);
            dirtCapacity = Config.GetInt(PropertyIdH.DirtCapacity, 100);
            dirtToMaterialRate = Config.GetFloat(PropertyIdH.DirtToMaterialRate, 1f);
            AutocleanAmount = Config.GetFloat(PropertyIdH.AutocleanAmount, 0.5f);
            SpawnByproductForEverySubPos = Config.GetBool(PropertyIdH.SpawnByproductForEverySubPos, false);
            textKey = Config.GetString(PropertyIdH.TextKey);
            amountKey = Config.GetString(PropertyIdH.AmountKey);
            dirtyKey = Config.GetString(PropertyIdH.DirtyKey);
            goalKey = Config.GetString(PropertyIdH.GoalKey);
            barKey = Config.GetString(PropertyIdH.BarKey);
            warningKey = Config.GetString(PropertyIdH.WarningKey);
            goalFlyingOnly = Config.GetBool(PropertyIdH.AdFlyingOnly, false);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("AutoClean", IsAutoClean);
            data.SetFloat("Dirt", dirtAmount);
            data.SetFloat("DirtBeforeClean", DirtAmountBeforeClean);
            data.SetFloat("AutocleanAmount", AutocleanAmount);
            data.SetInt("CleanThreshold", cleanPriority);
        }

        protected override void OnLoad(ComponentData data) {
            IsAutoClean = data.GetBool("AutoClean", IsAutoClean);
            dirtAmount = data.GetFloat("Dirt", 0);
            DirtAmountBeforeClean = data.GetFloat("DirtBeforeClean", dirtAmount);
            AutocleanAmount = data.GetFloat("AutocleanAmount", 0.5f);
            cleanPriority = data.GetInt("CleanThreshold", cleanPriority);
        }

        public override string ToString() {
            return $" * Cleanable [{dirtAmount}/{dirtCapacity}]";
        }

        public override void OnLateReady(bool wasLoaded) {
            extraInfo = Entity.GetRequiredComponent<ExtraInfoComp>(this);
            if (hasBar) {
                UpdateBar();
            }
        }

        public float AddDirt(float amt) {
            if (!Entity.IsActive) { return amt; }
            dirtAmount += amt;
            // D.Warn("Dirt: {0}", dirtAmount);
            if (entity.Goal == null || !AIGoal.IsActive(entity.Goal.State)) {
                if (IsAutoClean && dirtAmount >= dirtCapacity * AutocleanAmount) {
                    // Disconnected devices will not get auto cleaning
                    if (Tile.ENode?.IsConnected != false) {
                        if (entity.Goal != null) {
                            AIGoal.Cancel(entity.Goal, G.Ticks, goalKey.T());
                        }
                        CreateCleaningGoal();
                    }
                }
                if (dirtAmount > dirtCapacity * dirtyThreshold) {
                    // because the goal to clean sets info icon on this component
                    extraInfo?.ShowInfoIcon(IconId.CNoSparkles, dirtyKey.T());
                }
            }
            UpdateBar();
            if (dirtAmount > dirtCapacity) {
                var excess = dirtAmount - dirtCapacity;
                dirtAmount = dirtCapacity;
                dirtPercentB?.Set(DirtPercent);
                return excess;
            }
            dirtPercentB?.Set(DirtPercent);
            return 0f;
        }

        private void UpdateUIBlock() {
            if (hasBar) {
                UpdateBar();
            }
        }

        private void UpdateBar() {
            if (!hasBar || !Tile.IsConstructed) { return; }
            ShowInfoBar(barColor, dirtAmount/dirtCapacity, dirtAmount,
                dirtCapacity, barKey);
        }

        private int cleanPriority;
        public int CleanPriority => cleanPriority;

        private void UpdateCleanThreshold() {
            if (entity.Goal?.JobIdH == JobTypeIdH.Cleaning) {
                entity.Goal.SetPriority(G.Ticks, cleanPriority);
            }
        }
        public void ToggleAutoClean(bool val) {
            if (IsAutoClean == val) {
                return;
            }
            if (IsAutoClean) {
                if (entity.Goal?.JobIdH == JobTypeIdH.Cleaning) {
                    AIGoal.Cancel(entity.Goal, G.Ticks);
                }
            }
            IsAutoClean = val;
        }

        private void MovePriority(int direction) {
            cleanPriority = UIUtils.AdjustPriority0To5(cleanPriority, direction);
            UpdateCleanThreshold();
        }

        public void AdjustCleanPriority(int direction) {
            MovePriority(direction);
        }

        public void SetAutocleanAmountByUI(float value) {
            AutocleanAmount = Mathf.Clamp(value, 0.1f, 1f);
            if (AutocleanAmount < DirtPercent) {
                CreateCleaningGoal(true);
            }
        }

        private string goalName;

        public void CreateCleaningGoal(bool byPlayer = false) {
            if (HasAnyGoal) { return; }
            goalName ??= goalKey.T();
            var goal = CreateGoal(JobTypeIdH.Cleaning, IconId.CMop,
                    goalName, this)
                .NanobotCompatible()
                .WithRequiredAbilities(AbilityIdH.CleanDevice, AbilityIdH.DumbLabor)
                .WithWorldToDesired(AIVarsH.IsClean, false, true);
            if (goalFlyingOnly) {
                goal.WithWorldToDesired(AIVarsH.CanFly, false, true);
            }
            goal.ByPlayer(byPlayer);
            goal.WriteInitialState();
            entity.TrySetGoal(goal);
        }

        public bool RemoveDirt(float amount, BeingData cleaner) {
            DirtAmountBeforeClean = Mathf.Max(dirtAmount, DirtAmountBeforeClean);
            dirtAmount -= amount;
            // D.Warn("Dirt: {0}", dirtAmount);
            if (dirtAmount <= 0) {
                dirtAmount = 0;
                UpdateBar();
                HideAllInfo();
                extraInfo?.HideAllInfo();
                S.Sig.CleanableCleaned.Send(this, cleaner);
                SpawnByproduct(cleaner?.Being?.AIAgent);
                DirtAmountBeforeClean = 0;
                return true;
            } else {
                UpdateBar();
                var bar = ShowInfoBar(Consts.GreenRectColor,
                    1f - (dirtAmount / DirtAmountBeforeClean), T.ActClean);
                bar.IsPretranslated = true;
                return false;
            }
        }

        private void SpawnByproduct(AIAgentComp agent) {
            if (Byproduct == null) { return; }
            var stack = Mathf.RoundToInt(DirtAmountBeforeClean
                * DirtToMaterialRate);
            var mat = new Mat {
                Type = Byproduct,
                StackSize = stack
            };
            if (mat.StackSize > 0) {
                if (SpawnByproductForEverySubPos && Tile.Transform.IsMulti) {
                    int count = Tile.Transform.GetSubtilePos(Caches.PosIdx);
                    for (int i = 0; i < count; i++) {
                        SpawnByproductAt(mat, Caches.PosIdx[i], null);
                    }
                } else {
                    var pos = agent == null ?
                        Tile.Transform.AnyPortOutFirst : agent.PosIdx;
                    SpawnByproductAt(mat, pos, agent);
                }
            }
        }
        private void SpawnByproductAt(Mat mat, int pos, AIAgentComp assignTo) {
            var matobj = EntityUtils.SpawnRawMaterial(mat, pos, 0.1f, true);
            if (matobj != null && AutoHaulByproduct) {
                var goal = matobj.CreateStoringGoal(false, false);
                if (goal != null) {
                    if (assignTo != null && !assignTo.Jobs.CanDo(JobTypeIdH.Hauling)) {
                        assignTo = null;
                    }
                    matobj.Entity.TrySetGoal(goal, assignTo);
                }
            }
        }

        public bool CanCopyTo(ICopyableComp target) {
            return target is CleanableComp;
        }

        public bool CopyConfigTo(ICopyableComp target) {
            if (target is CleanableComp t) {
                bool changed = false;
                if (t.IsAutoClean != IsAutoClean) {
                    changed = true;
                    t.IsAutoClean = IsAutoClean;
                }
                if (t.AutocleanAmount != AutocleanAmount) {
                    changed = true;
                    t.AutocleanAmount = AutocleanAmount;
                }
                if (t.cleanPriority != cleanPriority) {
                    changed = true;
                    t.cleanPriority = cleanPriority;
                }
                return changed;
            }
            return false;
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<CleanableComp>(out var cl)) {
                CopyConfigTo(cl);
                cl.dirtAmount = dirtAmount;
            }
        }

        public void OnAIGoalPriorityChange(AIGoal goal) {
            cleanPriority = goal.GoalPriority;
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
        }

        public void AddNanobotsEffort(float amt, out AIGoal goal) {
            if (!TryGetGoal(out goal)) { return; }
            RemoveDirt(DirtCapacity * 0.01f * amt, goal.Agent?.Being.Data);
            if (dirtAmount == 0f) {
                goal.ChangeStateTo(AIGoalState.Completed, G.Ticks);
            }
        }

        public string StatsCategoryName => T.Cleanable;
        public int StatsOrder => 100;
        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var w = UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress, block.Container, idx++);
            w.Load(IconId.CMop, T.Dirt, 0f);
            DirtPercentB.Bind(life, (p) => {
                w.SetPercent(p, false);
                w.Item.SetText2(Units.XOutOfY(dirtAmount, dirtCapacity));
            });
        }
    }
}
