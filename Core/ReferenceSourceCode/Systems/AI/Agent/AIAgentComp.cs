#if !PRODUCTION_BUILD
// #define DEBUG_COMBAT
// #define DEBUG_AI_AGENT
#endif
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Motion;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Signals;
using KL.Utils;
using UnityEngine;
using Game.Systems.Reg;
using Game.Systems.Turrets;
using Game.Systems.UIUX;
using Game.Systems.Drama;
using Game.Mods;

namespace Game.Systems.AI {

    public sealed class AIAgentComp : BaseComponent<AIAgentComp>,
            ITurretTarget {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new AIAgentComp());
        }
        public BeingData BeingData;
        // internal memory for the current action
        public ISaveable ActionMemory;
        public AIGoal Goal;
        [SkipDumpInstance]
        public AIGoal TopPersonalGoal;
        [SkipDumpInstance]
        public AIPlan Plan;
        public AIAgentBlackboard BB;
        public ComponentData ActionMemoryData;
        public MotionControl Motion;
        public DirectControlComp DirectControl;
        public readonly List<AIGoal> Goals = new();
        [SkipDumpInstance]
        public readonly List<AIAction> Actions = new();
        [SkipDumpInstance]
        public readonly AIActionIndex ActionIndex = new();
        public AIAction CurrentAction { get; private set; }
        public long NextPersonalGoalCheckTick;
        // AIAuction: where this worker's tier walk goes on after it ran out of budget, -1 from
        // the top. Not saved
        [SkipDumpInstance] public int AuctionResumeAfter = -1;
        // AISys checks the running action against the reloaded list before its next step.
        // Not saved
        [SkipDumpInstance] public bool ActionsReloaded;
        public int TicksInCurrentAction;
        public long CurrentActionStartTick;
        [SkipDumpInstance] public long GoalStartTick;
        public AIGoal LastCompletedGoal;
        public long LastCompletedGoalTicks;
        public AIGoal LastFailedGoal;
        public string LastFailedGoalReason;
        public long LastFailedGoalTicks;
        public bool IsIdle => Goal == null || Goal.Type == AIGoalType.Idle;
        public AIGoal IdleGoal;
        public CombatUnit CombatUnit;
        public BotDockableComp Dockable;
        public Jobs Jobs => BeingData.Mind.Jobs;

        // Whether this worker may take a colony task at pos. Every path that hands colony
        // tasks to idle workers asks it (Docs/Architecture/Systems/BotDockSystem.md)
        public bool IsInWorkArea(int pos) {
            var dock = Dockable?.AssignedDock;
            return dock == null || dock.IsInWorkArea(pos);
        }
        [SkipDumpInstance]
        public Signal2<AIAgentComp, AIGoal> OnSetGoal = new(nameof(OnSetGoal));
        [SkipDumpInstance]
        public bool IsFeelingSafeNow => BeingData.Safety.IsActuallySafeAt(Motion.Position.Idx);
        public int PosIdx => Motion.Position.Idx;
        public Vector2 PosExact => Motion.Position.Exact;
        public Vector2 Position => Motion.Position.Exact;
        public int RoamFailCount;
        public int SpareCarryAmount {
            get {
                var carrying = Being.Graphics.Carrying;
                var cap = BeingData.StatsBeing.CarryCapacity.ValueInt;
                if (carrying == null) {
                    return cap;
                }
                if (carrying is Obj obj && obj.Pile != null) {
                    var carryCtrl = Being.Graphics.CarryCtrl;
                    var totalCarried = carryCtrl != null
                        ? carryCtrl.TotalCarriedStackSize
                        : obj.Pile.StackSize;
                    var free = cap - totalCarried;
                    if (free < 0) {
                        // Being is carrying more than able to pick up because
                        // some body part (hands) was damaged and carry capacity got reduced
                        D.Warn("Being carrying more than it can! OK if damaged mid task. {0} => {1}. Capacity: {2}",
                            this, carrying, cap);
                        return 0;
                    }
                    return free;
                }
                // If we are carrying something other than a pile, our hands are full
                return 0;
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.HealthWentToZero) {
                RemoveAgent();
            } else if (message == MsgIdH.DirectControlOn) {
                CombatUnit.DirectControlAttackTargetId = 0;
            } else if (message == MsgIdH.DirectControlOff) {
                CombatUnit.DirectControlAttackTargetId = 0;
            }
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            var particles = BB.Particles;
            if (particles != null) {
                particles.Position += new Vector2(dx, dy);
                particles.EmitParams.position += new Vector3(dx, dy, 0);
            }
        }

        public bool IsDisabled {
            get {
                if (BeingData.Body.IsIncapacitated) { return true; }
                if (!Being.IsAlive) { return true; }
                return false;
            }
        }

        public bool CanWorkOnColonyGoals {
            get {
                // enemies and guests will not build our shit
                if (!BeingData.IsPlayer) { return false; }
                if (BeingData.ConditionsPreventColonyWork) { return false; }
                return true;
            }
        }

        public bool CanWork {
            get {
                if (Motion.IsForcedWalk) { return false; }
                if (Motion.IsImmobilized) { return false; }
                if (BeingData.Body.IsIncapacitated) { return false; }
                if (Goal != null && Goal.IsForceAssigned) {
                    return false;
                }
                // task on being, possibly bring to surgery, don't take work
                if (Being.Goal?.JobIdH == JobTypeIdH.Task) {
                    // after 6 hours, let's keep doing everything else or we can die
                    if (Being.Goal.TimesFailed == 0
                            && G.Ticks - Being.Goal.CreatedAt < Consts.TicksPer6Hours) {
                        if (IsFeelingSafeNow) {
                            return false;
                        }
                    }
                }
                return true;
            }
        }

        private BCallback activityChangedB;
        public BCallback ActivityChangedB => activityChangedB ??= new BCallback();
        private BCallback goalDebugChangedB;
        public BCallback GoalDebugChangedB => goalDebugChangedB ??= new BCallback();
        public void RaiseGoalDebugChanged() => goalDebugChangedB?.Invoke();

        public SocialChatContext ChatContext {
            get {
                if (CurrentAction == null) { return null; }
                return BB.ChatContext ?? CurrentAction.ChatContext;
            }
        }

        public override void OnRemove() {
            RemoveAgent();
            CombatUnit?.SetState(CombatUnitState.Incapacitated);
        }

        public bool IsRegistered;

        public void AddAgent() {
            if (!IsRegistered) {
                IsRegistered = true;
                S.Sig.AIAddAgent.Send(this);
            } else {
                D.Err("Agent already added: {0}", this);
            }
        }

        public void RemoveAgent() {
            if (IsRegistered) {
                IsRegistered = false;
                S.Sig.AIRemoveAgent.Send(this);
                OnSetGoal.RemoveAllListeners();
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            entity.TryGetComponent(out Dockable);
            entity.TryGetComponent(out DirectControl);
        }

        public override void PostLateReady(bool wasLoaded) {
            Motion = Being.Motion;
            CombatUnit = CombatUnit.CreateFor(this);
            Motion.CombatUnit = CombatUnit;
            NextPersonalGoalCheckTick = G.Ticks + Consts.TicksPer5Minutes;
            AddAgent();
        }

        public bool SetGoal(AIGoal goal, AIPlan plan, long ticks, bool force = false) {
            #if DEBUG_AI_AGENT
            D.Warn("Setting goal: {0}->{1}->{2}", this, goal, plan);
            #endif
            if (Motion.IsForcedWalk) {
                if (force) {
                    Motion.Abort(T.NounDirectOrder);
                } else {
                    if ((goal.IsNonInterruptable || goal.State == AIGoalState.PreAssigned)
                            && Goal?.IsNonInterruptable != true) {
                        Motion.Abort(T.AdRejInterrupted);
                    } else {
                        D.Err("Trying to set a goal on agent while in forced walk! {0} | {1} | {2}", goal, CurrentAction, Being);
                        return false;
                    }
                }
            }
            if (goal == null) {
                D.Err("Trying to set null AIGoal on agent! Use AIAgentComp.ClearGoal instead!");
                ClearGoal();
                return false;
            }
            #if DEBUG_COMBAT
            if (goal.Type == AIGoalType.Combat) {
                if (BeingData.EffectiveFaction.IsPlayer) {
                    D.Warn("Combat goal for {0} => {1}", this, goal);
                }
            }
            #endif
            #if !PRODUCTION_BUILD
            if (goal.Type == AIGoalType.Combat
                    && (CombatUnit.State == CombatUnitState.Unaware
                    || CombatUnit.State == CombatUnitState.Incapacitated)) {
                D.Err("Trying to set a combat goal for a unit that is either unaware or incapacitated! {0} | {1} | {2}",
                    this, goal, CombatUnit);
            }
            #endif
            if (Goal != null) {
                if (AIGoal.IsActive(Goal.State)) {
                    if (Goal.IsNonInterruptable) {
                        D.Err("Trying to set a new goal while interruptable goal is in progress: {0}. New goal: {1}",
                            Goal, goal);
                        return false;
                    }
                    // It's ok to reassign idle goals any time, but otherwise the
                    // developer should know what the state of the agent is
                    if (Goal.Type != AIGoalType.Idle) {
                        D.Err("Can't invoke SetGoal when another goal is set. Call ClearGoal first! Cur Goal: {0}. New Goal: {1}",
                            Goal, goal);
                    }
                }
                ClearGoal();
            }
            ShowThoughtBubbleFor(goal);
            #if DEBUG_AI_AGENT
            D.Warn("Setting goal on agent: {0} ({2}) => {1}", goal, this, plan);
            #endif
            Goal = goal;
            GoalStartTick = ticks;
            goal.Validate(this);
            OnSetGoal.Send(this, goal);
            GoalDebugTab.LogStart(this, goal, plan);
            // TODO cleanup if current action != null
            if (CurrentAction != null) {
                SetAction(null);
            }

            ActionMemory = null;
            ActionMemoryData = null;
            goal.CanRetryAfterTick = 0;
            Motion.Abort(T.JobTypeTask);
            // TODO reuse the list
            Plan = plan;
            // TODO return false if some pre-flight checks fail
            goal.SetAgent(this);
            goal.ChangeStateTo(AIGoalState.InProgress, ticks);
            goal.UpdateIcon();
            UpdateUIBlock();
            return true;
        }

        public void ShowThoughtBubbleFor(AIGoal goal) {
            if (goal.IconId != null) {
                ShowThoughtBubble(goal.IconId, null);
            } else {
                HideThoughtBubble();
            }
            BeingData.UpdateAvatarStatus();
        }

        private AIAction lastAct = null;
        public AIAction LastAction => lastAct;

        public void ClearGoal() {
            var ticks = G.Ticks;
            if (CurrentAction != null) {
            #if DEBUG_AI_AGENT
                D.Warn("Deactivating act: {0} -> {1}", CurrentAction, Goal);
                #endif
                // The rest of this cleanup must always run. A throwing OnDeactivate used
                // to leave the goal attached, so the same action failed again on every
                // tick and the retry escaped AISys.OnTick (STA-1335).
                try {
                    CurrentAction.OnDeactivate(this, Goal, ticks);
                } catch (Exception ex) {
                    D.LogEx(ex, "Action failed to deactivate: {0}. Goal: {1}. Agent: {2}",
                        CurrentAction, Goal, this);
                    ModRegistry.WarnAboutBrokenMod(CurrentAction,
                        $"Exception while deactivating AIAction: {CurrentAction}");
                }
            } else {
            #if DEBUG_AI_AGENT
                D.Warn("Goal has no current action: {0}", Goal);
            #endif
            }
            SetAction(null);

            if (Motion.IsBusy && !Motion.IsForcedWalk) {
                Motion.Clear(true);
            }

            if (Being.Graphics.IsCarrying()) {
                D.Err("Carrying something after goal was cleared! last act: {0} -> {1} -> {2} -> {3}",
                    lastAct, Being.Graphics.Carrying, Goal, this);
                Being.Graphics.DropIfCarrying();
            }

            if (Goal != null) {
                Goal.Flags &= ~AIGoalFlags.ForceAssigned;
                if (Goal.Agent != this) {
                    #if DEBUG_AI_AGENT
                    D.Err("Trying to clear goal that is assigned to a different agent: {0}. Agent that is clearing: {1}",
                        Goal, this);
                    #endif
                }
                HideThoughtBubble();
                // Actions that remove their own target (deconstruct -> BreakDown) cancel the
                // goal through the target before CheckDone marks it Completed
                var isDone = AIGoal.IsCompleted(Goal.State)
                    || (AIGoal.IsCancelled(Goal.State)
                        && Goal.DesiredState.IsFulfilledIn(Goal.WorldState));
                if (Goal.Type != AIGoalType.Idle) {
                    if (isDone) {
                        LastCompletedGoal = Goal;
                        LastCompletedGoalTicks = ticks;
                    } else if (Goal.State == AIGoalState.Failed
                            || Goal.State == AIGoalState.Error) {
                        // Replans, soft fails, put-backs, need interrupts and player
                        // cancels are not this worker's failures
                        ShowThoughtBubble(IconId.CCross, Goal.TaskLineFail, 5);
                        LastFailedGoal = Goal;
                        LastFailedGoalReason = Goal.FailReason;
                        LastFailedGoalTicks = ticks;
                    }
                }
                if (Goal.State == AIGoalState.InProgress && Goal.Type != AIGoalType.Idle) {
                    D.Err("Clearing in progress goal: {0} | Agent: {1}",
                        Goal, Being);
                }
                GoalDebugTab.LogEnd(this, Goal, Goal.State);
                Goal.SetAgent(null);
                BB.Validate(S, Goal, lastAct);
                BB.MaybeCleanGoalCooldowns(ticks);
                if (Goal.Type != AIGoalType.Colony && Goal.NonRetryable) {
                    Goals.Remove(Goal);
                }
                if (Goal.Type == AIGoalType.Combat ||
                        (Goal.Flags & AIGoalFlags.IgnoreDeadBodies) == AIGoalFlags.IgnoreDeadBodies) {
                    // ignore dead bodies for a while
                    if (BeingData.Mind.Health != null) {
                        BeingData.Mind.Health.IgnoreDeadBodiesUntil = G.Ticks + Consts.TicksPer3Hours;
                    }
                }
                Goal = null;
            }

            UpdateUIBlock();
            ActionMemory = null;
            ActionMemoryData = null;
            Plan = null;
            TicksInCurrentAction = 0;
        }

        public override string ToString() {
            if (BeingData == null) {
                return $"AIAgent[{Being}|UNINITIALIZED]";
            }
            return $"AIAgent[{Being}|{BeingData.Name}|State:{BeingData.StateFlags}|Body:{BeingData.Body.StateB}]";
        }

        private void UpdateUIBlock() {
            activityChangedB?.Invoke();
        }

        public bool TryFindGoal(uint id, out AIGoal goal) {
            foreach (var g in Goals) {
                if (g.Id == id) {
                    goal = g;
                    return true;
                }
            }
            goal = null;
            return false;
        }

        public void ReloadActions() {
            ActionIndex.MarkDirty();
            ActionsReloaded = true;
            Actions.Clear();
            foreach (var action in Registries.AIActions.All.Values) {
                if (action.IsAvailableFor(this)) {
                    #if DEBUG_AI_AGENT
                    D.Warn("Adding action to agent: {0} -> {1}", action, this);
                    #endif
                    Actions.Add(action);
                }
            }
        }
        public void AddMentalEffect(MentalEffect effect) {
            S.Sig.AddMentalEffect.Send(BeingData, effect);
        }

        public void DropGoal() {
            if (Goal != null) {
                #if DEBUG_AI_AGENT
                D.Warn("Interrupting agent's goal: {0}->{1}", this, Goal);
                #endif
                AIGoal.Interrupt(Goal, G.Ticks);
                ClearGoal();
            }
        }

        public void SetAction(AIAction action) {
            #if DEBUG_AI_AGENT
            D.Warn("Setting current action: {0} on {1}", action, this);
            #endif
            if (CurrentAction != null) {
                lastAct = CurrentAction;
            }
            CurrentAction = action;
            CurrentActionStartTick = G.Ticks;
            activityChangedB?.Invoke();
        }

        public bool IsFeelingSafeAt(int posIdx) {
            return BeingData.Safety.IsActuallySafeAt(posIdx);
        }

        public bool AddRoamFailure() {
            #if !PRODUCTION_BUILD
            RegViz.ValidateRegType(S, PosIdx, true);
            #endif
            if (RoamFailCount++ == 10) {
                ShowInfoIcon(IconId.CWarning, T.AdjStuck);
            }
            // D.Warn("Roam failure: {0} | {1} | {2}", G.Ticks, RoamFailCount, Being);
            return RoamFailCount > 10;
        }

        public void ClearRoamFailure() {
            if (RoamFailCount >= 10) {
                HideInfoIcon();
            }
            RoamFailCount = 0;
        }

        public bool HasAIAction(int actIdH) {
            for (int i = Actions.Count - 1; i >= 0; i--) {
                AIAction a = Actions[i];
                if (a.IdH == actIdH) {
                    return true;
                }
            }
            return false;
        }

        public bool TryGetAIAction(int actIdH, out AIAction action) {
            for (int i = Actions.Count - 1; i >= 0; i--) {
                AIAction a = Actions[i];
                if (a.IdH == actIdH) {
                    action = a;
                    return true;
                }
            }
            action = null;
            return false;
        }

    }
}
