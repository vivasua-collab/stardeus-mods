#if !PRODUCTION_BUILD
#define DEBUG_POSITION
#endif
using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.Systems.AI;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using System;
using System.Text;
using Game.Systems.Mental;
using Game.Systems.UIUX;
using Game.Systems.Stats;
using System.Collections.Generic;
using Game.Systems.Creatures;
using KL.Collections;

namespace Game.Components {
    public sealed class OperatableDeviceComp : BaseComponent<OperatableDeviceComp>,
            IAIGoalProvider, IAIGoalPriorityListener,
            IDefDescriptionProvider, IStatProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new OperatableDeviceComp());
        }
        public bool IsAutomated;
        public string StatsCategoryName => T.NounOperator;

        public int StatsOrder => 100;
        private JobType jobType;
        public JobType JobType => jobType;
        public bool CanBeOperated;
        public Being Operator { get; private set; }
        private B<Being> operatorB;
        public B<Being> OperatorB => operatorB ??= new B<Being>(Operator);
        public uint[] allowedMindIds;
        public uint[] AllowedMindIdsActual => allowedMindIds;
        public uint[] allowedMindIdsOverride;
        public uint[] AllowedMindIds => allowedMindIdsOverride ?? allowedMindIds;

        public void SetAllowedMindIds(uint[] ids) {
            if (ids == null && allowedMindIds == null) { return; }
            allowedMindIds = ids;
            MindIdsUpdated();
        }
        public void SetAllowedMindIdsOverride(uint[] ids) {
            if (ids == null && allowedMindIdsOverride == null) { return; }
            allowedMindIdsOverride = ids;
            MindIdsUpdated();
        }

        public bool TryRemoveAllowedMindId(uint mindId) {
            if (!Arrays.ContainsSafe(allowedMindIds, mindId)) {
                return false;
            }
            if (allowedMindIds.Length == 1) {
                SetAllowedMindIds(null);
            } else {
                var ids = new uint[allowedMindIds.Length - 1];
                int idx = 0;
                for (int i = 0; i < allowedMindIds.Length; i++) {
                    var currentId = allowedMindIds[i];
                    if (currentId == mindId) { continue; }
                    ids[idx++] = currentId;
                }
                SetAllowedMindIds(ids);
            }
            stateChangedB?.Invoke();
            return true;
        }

        private Vector2[] offsets;
        private Vector2 offset => offsets[(int) Tile.Transform.Rotation];
        public Vector2 OperatorPosition => Tile.Position + offset;
        public Facing.Type OperatorFacing => Facing.Opposite(
            Tile.Transform.Rotation);

        private FrugalArray<int> requiredAbilities;
        public FrugalArray<int> RequiredAbilities => requiredAbilities;
        private bool canKeepOperator;
        public bool CanKeepOperator => canKeepOperator;
        private bool keepOperator;
        public bool KeepOperator => keepOperator;
        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();
        public void MindIdsUpdated() {
            if (TryGetGoal(out var g)) {
                g.WithAllowedMindIds(AllowedMindIds);
                if (!Arrays.IsEmpty(allowedMindIds) && g.Agent != null) {
                    if (!Arrays.ContainsSafe(AllowedMindIds, g.Agent.BeingData.Mind.Id)) {
                        g.Agent.ClearGoal();
                    }
                }
            }
        }
        public void ToggleKeepOperator() {
            if (!canKeepOperator) {
                D.Err("Trying to toggle operator on device that can't keep one! {0}",
                    Tile.Definition.Id);
                return;
            }
            keepOperator = !keepOperator;
            if (keepOperator) {
                HailOperator(false);
            } else {
                DismissOperator(false);
            }
            stateChangedB?.Invoke();
        }

        public AIGoal HailOperator(bool force) {
            if (IsAutomated) { return null; }
            CanBeOperated = true;
            if (TryGetGoal(out var g)) {
                if (AIGoal.IsActive(g)) {
                    return g;
                } else {
                    AIGoal.TryRetry(g, G.Ticks, force, false);
                    return g;
                }
            }
            if (g != null) {
                return g;
            }
            var goal = CreateGoal(jobType.IdHash, jobType.IconId, jobType.NameT, this)
                .WithWorldToDesired(AIVarsH.IsOperatorPresent, false, true)
                .WithPriority(GoalPriority)
                .WithRequiredAbilities(requiredAbilities)
                .WithPathFlags(PathReqFlags.TryWorkSpot)
                .WithAllowedMindIds(AllowedMindIds)
                .WriteInitialState();
            entity.TrySetGoal(goal);
            return goal;
        }

        public bool IsAbleToOperate(BeingData being) {
            if (!BeingUtils.HasAbilities(being, requiredAbilities)) { return false; }
            if (being.Mind?.Jobs.CanDo(jobType.IdHash, out string _) != true) { return false; }
            return true;
        }

        public override void OnRemove() {
            DismissOperator(false);
        }

        public void DismissOperator(bool isTaskCompleted) {
            if (keepOperator) { return; }
            CanBeOperated = false;
            OperatorEfficiency = 1f;
            if (TryGetGoal(out var g)) {
                if (isTaskCompleted) {
                    g.WithWorld(AIVarsH.IsOperatorPresent, true);
                    g.ChangeStateTo(AIGoalState.Completed, G.Ticks);
                } else {
                    AIGoal.Cancel(g, G.Ticks);
                }
                if (g.Agent != null) {
                    g.Agent.ClearGoal();
                }
            }
        }

        // Otherwise punch
        public string AnimationType;

        protected override void OnConfig() {
            var jobTypeId = Config.GetString(PropertyIdH.JobType, null);
            if (jobTypeId == null) {
                var err = string.Format("JobType not configured for Craftable device: {0}",
                    Entity.DefinitionId);
                D.Err(err);
                ModRegistry.WarnAboutBrokenMod(Entity.Definition, err);
                IsAutomated = true;
                return;
            }
            if (!JobType.TryGet(jobTypeId, out jobType)) {
                var err = string.Format("JobType not valid for Craftable device: {0}: {1}",
                    jobTypeId, Entity.DefinitionId);
                D.Err(err);
                ModRegistry.WarnAboutBrokenMod(Entity.Definition, err);
                IsAutomated = true;
                return;
            }

            offsets = Config.GetVector2Set(PropertyIdH.OperatorOffsets);
            GoalPriority = Config.GetInt(PropertyIdH.DefaultPriority, 3);
            AnimationType = Config.GetString(PropertyIdH.AnimationType, "Punch");
            canKeepOperator = Config.GetBool(PropertyIdH.CanKeepOperator, false);
            JobLevelUpSpeed = Config.GetFloat(PropertyIdH.JobLevelUpSpeed, 1f);
            OperatorEfficiency = 1f;

            var abilityIds = Config.GetStringSet(PropertyIdH.RequiredAbilities, null);
            if (!Arrays.IsEmpty(abilityIds)) {
                requiredAbilities = default;
                foreach (var id in abilityIds) {
                    var ability = Ability.Get(id);
                    if (ability == null) {
                        var err = string.Format("RequiredAbilities entry not valid for OperatableDevice: {0}: {1}",
                            id, Entity.DefinitionId);
                        ModRegistry.WarnAboutBrokenMod(Entity.Definition, err);
                        continue;
                    }
                    requiredAbilities.Add(ability.IdH);
                }
            }
            if (requiredAbilities.Length == 0) {
                // Not configured, or every configured entry was invalid
                requiredAbilities = new FrugalArray<int>(AbilityIdH.Manipulate, AbilityIdH.Intelligence);
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("Priority", GoalPriority);
            data.SetBool("CanBeOperated", CanBeOperated);
            if (canKeepOperator) {
                data.SetBool("KeepOperator", keepOperator);
            }
            data.SetIntSet("AllowedMindIds", Arrays.IsEmpty(allowedMindIds)
                ? null
                : Array.ConvertAll(allowedMindIds, i => (int) i));
        }

        public override void OnLateReady(bool wasLoaded) {
            Tile.Transform.EnsurePortPresence(true, false, false, this);
            foreach (var c in Entity.Components) {
                if (c is IOperatorWorkSource source) {
                    workSource = source;
                    break;
                }
            }
        }

        private IOperatorWorkSource workSource;
        // Whether the operator's skill trains right now. Devices without a source (crafters,
        // research, surgery) dismiss their operator once the work stops, so being there is work.
        public bool IsOperatorWorking => workSource?.IsOperatorWorking ?? true;

        protected override void OnLoad(ComponentData data) {
            GoalPriority = data.GetInt("Priority", GoalPriority);
            CanBeOperated = data.GetBool("CanBeOperated", CanBeOperated);
            if (canKeepOperator) {
                keepOperator = data.GetBool("KeepOperator", keepOperator);
            }
            var ids = data.GetIntSet("AllowedMindIds", null);
            if (!Arrays.IsEmpty(ids)) {
                allowedMindIds = Array.ConvertAll(ids, i => (uint) i);
            }
        }

        public override string ToString() {
            return $" * OperatableDevice [{jobType.NameT}]";
        }

        public int GoalPriority;
        public void SetPriority(int prio) {
            GoalPriority = prio;
            if (TryGetGoal(out var g)) {
                g.WithPriority(prio);
                if (AIGoal.IsFailed(g.State)) {
                    AIGoal.TryRetry(g, G.Ticks, true, false);
                }
            }
        }

        private ElectricNodeComp elNode;
        public float OperatorEfficiency;
        public float JobLevelUpSpeed;

        public ElectricNodeComp ENode => elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                elNode = (ElectricNodeComp) sender;
                elNode.AddAfterTick(this, AfterTickGrid);
            }
        }

        private void AfterTickGrid() {
            if (!elNode.IsPowerable) {
                if (TryGetGoal(out var g)) {
                    entity.ClearGoal(T.Unpowered);
                }
                // A kept operator is hailed again once the device has power: hailing here
                // re-created the goal just cleared, every grid tick (STA-1930)
                return;
            }
            if (keepOperator && !IsAutomated) {
                HailOperator(false);
            }
        }

        public void OnAIGoalPriorityChange(AIGoal goal) {
            if (GoalPriority == goal.GoalPriority) { return; }
            GoalPriority = goal.GoalPriority;
            foreach (var c in Entity.Components) {
                // to tell the crafter that priority changed
                if (c is IAIGoalPriorityListener ap && c != this) {
                    ap.OnAIGoalPriorityChange(goal);
                }
            }
        }

        public Action<AIGoal> OnGoalCancel;

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
            if (AIGoal.IsCancelled(goal)) {
                OnGoalCancel?.Invoke(goal);
            }
        }

        public void OnGoalLoad(AIGoal goal) {
            goal.WithRequiredAbilities(requiredAbilities);
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def,
                bool addedAnything) {
            Def.PrependAdditionalInfo(sb, addedAnything);
            sb.Append(T.OperatorRequired);
            return true;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
        }

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            if (IsAutomated) {
                var w = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
                w.Load(IconId.COperate, Texts.Strike(T.OperatorRequired), T.AdjAutomated);
            } else {
                var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, block.Container, idx++);
                w.Load(IconId.COperate, T.OperatorRequired);
            }
        }

        public void SetOperator(Being op) {
            Operator = op;
            operatorB?.Set(op);
        }

#if !PRODUCTION_BUILD
        public Vector2[] DebugOffsets => offsets;

#else
        public Vector2[] DebugOffsets => null;
#endif
    }
}
