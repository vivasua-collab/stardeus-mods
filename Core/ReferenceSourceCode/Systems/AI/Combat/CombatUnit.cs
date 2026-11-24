#if UNITY_EDITOR
// #define DEBUG_COMBAT
#endif
using System.Collections.Generic;
using System.Diagnostics;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Randomness;
using KL.Utils;
using Priority_Queue;
using Unity.Mathematics;
using UnityEngine;
using Game.Systems.Reg;
using Game.Systems.Combat;
using KL.Grid;
using Game.Systems.Path;
using Game.Systems.Turrets;

namespace Game.Systems.AI.Combat {
    public sealed partial class CombatUnit : FastPriorityQueueNode, IAttackable {
        public AIAgentComp Agent;
        // The army this unit fights in, null outside a fight
        public Army Army;
        // What this unit is after in the fight, picked from its army's objectives
        public CombatObjective Objective;
        public int TicksInObjective;
        // Set when the unit's goals must be rebuilt by its objective (a new config, no goals)
        public bool GoalsChanged;
        public CombatUnitState State;
        public Vector2 Position => Agent.Being.Graphics.Position;
        // Sight is measured between tile centers, like the cover map it reads. Position can be
        // anywhere in the tile: mid-move, or on the tile corner where some spawns put a being.
        // Sight is cached per tile pair (HandleAttackTarget), so it came from wherever that
        // sample happened to be, and a ray from a corner runs along tile edges and clips the
        // walls next to it. Target picking must agree with the cached sight.
        public Vector2 SightPosition => Pos.ToVector(PosIdx);
        public int PosIdx => Agent.PosIdx;
        public bool IsInBadPos;
        private bool shouldFaceTarget;
        public BeingData Data => Agent.BeingData;
        /// <summary>
        /// A humanoid mind fights with tactics: cover, keeping its distance. Anything else
        /// (bot minds, also in a human body) walks straight at its target. Follows the mind,
        /// not the body, and is not cached: minds can be moved.
        /// </summary>
        public bool IsTactical => Data.Mind?.Species?.IsHumanoid == true;
        // How far sight raycasts between units reach, and how far a unit picks targets on its
        // own. Both grew with the 1.5x weapon ranges (the sniper reaches 23). Every check uses
        // these, so sight, targeting and the shot tooltip cannot drift apart.
        public const float SightRange = 30f;
        public const float AutoTargetRange = 22.5f;
        public readonly List<CombatUnit> EnemiesTargetingThisUnit = new();
        public readonly List<IAttackableTurret> TurretsTargetingThisUnit = new();
        public CombatUnitComp Comp;
        public string EntityName => Data.Mind.CombatLogName;
        /// <summary>
        /// Various private goals that this unit can take in this combat
        /// </summary>
        public readonly List<AIGoal> Goals = new();
        /// <summary>
        /// Currently engaged enemy unit
        /// </summary>
        public CombatUnit AttackTarget;
        // If AttackTarget is null, this could be used instead if it's set
        public IAttackable Attackable;
        public ParticlesVFX FX;
        public int DirectControlAttackTargetId;
        // The player ordered this drafted unit to attack its current target and to go after it,
        // so the AI may move it: a melee order, or a shooter's order to close in (Shift, or one
        // of several selected units out of reach). A provoked retaliation also goes through
        // SetAttackTarget(force), but it is not an order: a drafted unit only fights back from
        // where the player put it. Neither is a hold order (IsHoldOrder).
        public bool HasDirectAttackOrder =>
            Agent.DirectControl.IsUnderDirectControl
            && BestAttack != null
            && !IsRetaliating
            && DirectControlAttackTargetId != 0
            && AttackTarget != null
            && AttackTarget.Agent.EntityId == DirectControlAttackTargetId
            && (OrderMayApproach || BestAttack.IsMelee);
        // A shooter's attack order may walk it closer (CombatOrders). Without it the order is a
        // hold order: the unit fires from where it stands.
        public bool OrderMayApproach;
        // A drafted shooter told to fire at a target it can hit from where it stands. It never
        // moves for it, and lets go once the target stays out of reach (UpdateOrderLeash).
        public bool IsHoldOrder =>
            Agent.DirectControl.IsUnderDirectControl
            && IsAttackTargetForced
            && !IsRetaliating
            && !OrderMayApproach
            && AttackTarget != null
            && BestAttack?.IsMelee == false;
        // The player walks a drafted unit somewhere: it does not aim on the way
        private bool IsWalkingOnOrder =>
            Agent.DirectControl.IsUnderDirectControl && Agent.Motion.IsForcedWalk;
        public string[] TakeDamageSounds => Data.Species.TakeDamageSounds;
        private static readonly int attackFxBudgetKey = Hashes.S("AttackFX");
        public Vector2 NozzlePosition => Agent.PosExact + Agent.Being.Graphics.CarryOffset(out _);
        public long LastTick;
        public Rng Rng;
        public bool HasRoutFleeRoll;
        public bool WantsToFleeOnRout;
        // Found no way off the map, so it fights on instead of fleeing again
        public bool GaveUpFleeing;
        // EscapeRoute's cached answer for a grounded unit: can it walk off the map, and when
        // that was last checked (0: never). Not saved: asked again after a load
        internal bool CanWalkToMapEdge;
        internal long EdgeReachCheckTick;
        private const int particleBudget = 100;
        public CombatUnitConfig Config => Data.Mind.CombatConfig;
        public CombatUnitConfig EffectiveConfig {
            get {
                if ((Army?.Flags & CombatFlags.Friendly) == CombatFlags.Friendly) {
                    return CombatUnitConfig.Fight;
                }
                // The reaction is what a unit does on its own. Drafted, the player is in
                // control: a drafted shooter set to Ignore fired at nothing, one set to Flee
                // not even at an ordered target
                if (Agent.DirectControl.IsUnderDirectControl) {
                    return CombatUnitConfig.Fight;
                }
                var cfg = Data.Mind.CombatConfig;
                return cfg == CombatUnitConfig.FreeWill ? FreeWillChoice() : cfg;
            }
        }
        // Free Will picks for the being: fight while it holds a weapon it can use and is not
        // badly hurt, flee otherwise. A fixed reaction goes stale whenever a weapon is picked
        // up or dropped, and an unarmed colonist set to fight only gets hurt.
        private CombatUnitConfig FreeWillChoice() {
            // The player's control comes first: a drafted unit, or one told to attack, fights
            // whatever it holds. Fleeing would drop the order the player just gave.
            if (Agent.DirectControl.IsUnderDirectControl || HasPlayerOrder) {
                return CombatUnitConfig.Fight;
            }
            if (freeWillAttackVersion != Data.Body.AttackMethodsVersion) {
                freeWillAttackVersion = Data.Body.AttackMethodsVersion;
                hasWeaponAttack = HasWeaponAttack();
            }
            return hasWeaponAttack && Data.Body.VitalityPercent >= Tunables.CombatRetreatVitality
                ? CombatUnitConfig.Fight
                : CombatUnitConfig.Flee;
        }
        private int freeWillAttackVersion = -1;
        private bool hasWeaponAttack;
        // A usable attack from an equipped item, not from the body (fists, a drone's ram)
        private bool HasWeaponAttack() {
            var methods = Data.Body.AttackMethods;
            for (int i = 0; i < methods.Count; i++) {
                var meth = methods[i];
                if (!meth.Provider.IsBodyPart && IsUsable(meth, false)) { return true; }
            }
            return false;
        }
        // The player told this unit to attack its target. A provoked retaliation is not an order
        public bool HasPlayerOrder => IsAttackTargetForced && !IsRetaliating && AttackTarget != null;
        // Fights only from where it stands and never goes looking for enemies: the undrafted
        // crew (CombatObjectiveDefend.RespondsInPlace)
        public bool RespondsInPlace => Objective?.RespondsInPlace(this) == true;
        // A drafted unit with Fire at Will off shoots only what the player orders. It still
        // hits an enemy standing next to it in melee (SelectAdjacentMeleeTarget): holding fire is no reason to take a
        // beating.
        public bool HoldsFire => Agent.DirectControl.IsUnderDirectControl
            && !Agent.DirectControl.FireAtWill;
        // how often a unit holding fire looks for an enemy next to it
        private const int HoldFireCheckTicks = 3;

        public void OnFireAtWillChanged() {
            if (HoldsFire) {
                if (AttackTarget != null && (!IsAttackTargetForced || IsRetaliating)) {
                    SetAttackTarget(null);
                }
            }
            // look again right away, not after the usual re-evaluation delay
            NextTargetEvaluateTick = 0;
        }

        // Fire at Will shows only for a unit with a ranged weapon (FireAtWillAction)
        public bool HasUsableRangedAttack() {
            var methods = Data.Body.AttackMethods;
            for (int i = 0; i < methods.Count; i++) {
                if (!methods[i].IsMelee && IsUsable(methods[i], false)) { return true; }
            }
            return false;
        }

        // The melee a unit holding fire still does: an adjacent enemy it can reach. It never
        // walks after one, and lets go once the enemy is no longer next to it.
        private void SelectAdjacentMeleeTarget(GameState s, long ticks) {
            NextTargetEvaluateTick = ticks + HoldFireCheckTicks;
            if (AttackTarget != null) {
                if (!IsAttackTargetForced && AttackTarget.State != CombatUnitState.Incapacitated
                        && TryCloseContact(PosIdx, AttackTarget.PosIdx, out var reaches)
                        && reaches) {
                    return;
                }
                SetAttackTarget(null);
            }
            if (Army == null || BestMeleeAttack(false) == null) { return; }
            var armies = s.Query.GetArmies.Ask();
            for (int i = armies.Count - 1; i >= 0; i--) {
                var a = armies[i];
                if (a == Army || (a.Flags & CombatFlags.Friendly) == CombatFlags.Friendly) {
                    continue;
                }
                foreach (var u in a.Units) {
                    if (u.State == CombatUnitState.Incapacitated) { continue; }
                    if (TryCloseContact(PosIdx, u.PosIdx, out var canReach) && canReach) {
                        SetAttackTarget(u);
                        return;
                    }
                }
            }
        }
        // An enemy in sight within CombatFleeRadius: what a unit set to flee runs from. Without
        // one it keeps working
        public bool IsThreatNear() {
            var enemy = Agent.S.Sys.Combat.ClosestStandingEnemy(this);
            if (enemy == null) { return false; }
            var r = Tunables.CombatFleeRadius;
            if (Vectors.SqrDistance(Position, enemy.Position) > r * r) { return false; }
            return EntityUtils.HasLineOfSightCoverFast(SightPosition, enemy.SightPosition, 0f, r);
        }
        public void LoadState(CombatUnitState state) {
            // The body checks whether it is still down while it loads, before the AI system
            // restores this state. A being the downing rules no longer keep down (a save from
            // before pain shock, the damaged parts rule moved up) is up by then, and a saved
            // Incapacitated would leave a standing being out of every fight.
            var body = Data?.Body;
            if (state == CombatUnitState.Incapacitated && body != null && body.IsAlive
                    && !body.IsIncapacitated) {
                state = CombatUnitState.Unaware;
            } else if (body != null && body.IsIncapacitated) {
                // And the reverse: a downed being saved as up (STA-1852) would be shot at
                state = CombatUnitState.Incapacitated;
            }
            State = state;
        }
        public void SetState(CombatUnitState state) {
            // A downed unit only gets up with its body (Body.RemoveDisability). Woken any other
            // way, it is targeted again while it lies there (STA-1852)
            if (State == CombatUnitState.Incapacitated && state != CombatUnitState.Incapacitated
                    && Data?.Body.IsIncapacitated == true) {
                #if !PRODUCTION_BUILD
                D.Warn("Refusing to set {0} on an incapacitated unit: {1}", state, this);
                #endif
                return;
            }
            if (State != state && (state == CombatUnitState.Incapacitated) && Data != null) {
                if (Data.HasCondition(ConditionIdH.Preoccupied)) {
                    Data.RemoveCondition(Agent.S, ConditionIdH.Preoccupied);
                }
            }
            State = state;
        }

        public void SetConfig(CombatUnitConfig cfg) {
            Data.Mind.CombatConfig = cfg;
            if (cfg == CombatUnitConfig.Ignore && AttackTarget != null) {
                SetAttackTarget(null, force: true);
            }
            ValidateConfig();
        }

        public void RollRoutFleeFor(Army army) {
            if (HasRoutFleeRoll) { return; }
            HasRoutFleeRoll = true;
            WantsToFleeOnRout = false;
            if (army == null) { return; }
            if (Data.IsFriendly) { return; }
            if ((army.Flags & CombatFlags.Friendly) == CombatFlags.Friendly) { return; }
            if (Data.Species.IsWild) { return; }
            // rolled once per fight: a unit without a way out when it joins never breaks
            if (!EscapeRoute.Exists(this)) { return; }
            WantsToFleeOnRout = Rng.Chance(Tunables.CombatEnemyRoutFleeChance);
        }

        public void ResetRoutFleeRoll() {
            HasRoutFleeRoll = false;
            WantsToFleeOnRout = false;
            GaveUpFleeing = false;
            IsLeaving = false;
        }

        // A raider who came to after its raid was over: it heads for its way out
        // (CombatSys.OnHostileRecovered) instead of standing around as a hostile nobody
        // engages. Retreats like a routed unit until it leaves or gives up.
        public bool IsLeaving;

        /// <summary>
        /// Called when a fleeing unit cannot reach an escape route (walled in, behind
        /// locked doors, etc). The unit stops trying to flee for the rest of this combat
        /// and fights to the death instead.
        /// </summary>
        public void GiveUpFleeingAndFight() {
            // All three flags are persisted (CombatUnitData). Setting HasRoutFleeRoll=true
            // prevents RollRoutFleeFor from re-rolling on load; WantsToFleeOnRout=false and
            // GaveUpFleeing stop ValidateConfig from flipping the config back to Flee while
            // routing or badly hurt.
            HasRoutFleeRoll = true;
            WantsToFleeOnRout = false;
            GaveUpFleeing = true;
            IsLeaving = false;
            Data.Mind.CombatConfig = CombatUnitConfig.Fight;
            SetState(CombatUnitState.Aggressive);
            ValidateConfig();
        }

        /// <summary>
        /// Tick when this unit was first observed fleeing (<see cref="State"/> ==
        /// Fleeing) while its active goal is not a combat goal — e.g. it got
        /// sidetracked into a personal need or a mental breakdown. 0 when not stalled.
        /// Not persisted: worst case a unit that was already stalled when the game
        /// was saved just takes up to another <see cref="Consts.TicksPer10Minutes"/>
        /// after load to self-correct.
        /// </summary>
        private long fleeStallStartTick;

        /// <summary>
        /// Safety valve for units whose CombatUnitState is forced back to Fleeing
        /// every tick by ValidateConfig, but whose actually-selected AIGoal keeps
        /// losing to a Personal-type goal (needs, mental breakdown) in the shared
        /// goal priority queue — see AISys.FindTopPersonalGoalFor. Such a unit never
        /// runs the flee/escape action, so it can get stuck indefinitely. If a
        /// fleeing unit spends too long without an active combat goal, give up on
        /// fleeing permanently and fight instead.
        /// </summary>
        private void ValidateFleeProgress(long ticks) {
            if (Data.IsPlayer || State != CombatUnitState.Fleeing || Army == null) {
                fleeStallStartTick = 0;
                return;
            }
            if (Agent.Goal?.Type == AIGoalType.Combat) {
                fleeStallStartTick = 0;
                return;
            }
            if (fleeStallStartTick == 0) {
                fleeStallStartTick = ticks;
                return;
            }
            if (ticks - fleeStallStartTick > Consts.TicksPer10Minutes) {
                D.Warn("Fleeing unit stalled on a non-combat goal for too long, giving up and fighting: {0} | {1}",
                    this, Agent.Goal);
                GiveUpFleeingAndFight();
                fleeStallStartTick = 0;
            }
        }

        /// <summary>
        /// Start of the current continuous stretch during which this unit could not attack
        /// its target from wherever it stands (out of range or no line of sight). 0 otherwise.
        /// Used by <see cref="CombatObjectiveFistFight"/> to break up stalemates where fighters
        /// are separated by an impassable door or wall and would otherwise stand still forever.
        /// Not persisted: worst case a loaded game restarts the timeout window. Sleeping units
        /// skip the update, so elapsed stall time keeps accruing through the whole sleep.
        /// </summary>
        private long noAttackStallStartTick;

        public long NoAttackStallTicks(long ticks) {
            return noAttackStallStartTick == 0 ? 0 : ticks - noAttackStallStartTick;
        }

        public void ResetNoAttackStall() {
            noAttackStallStartTick = 0;
        }

        private void UpdateNoAttackStall(long ticks) {
            if (AttackTarget == null
                    || (Army?.Flags & CombatFlags.Friendly) != CombatFlags.Friendly) {
                noAttackStallStartTick = 0;
                return;
            }
            if (HasLineOfSight && MeleeInAttackRange) {
                noAttackStallStartTick = 0;
            } else if (noAttackStallStartTick == 0) {
                noAttackStallStartTick = ticks;
            }
        }

        /// <summary>
        /// Provoked Ignore units fight back only while they can hit the attacker from where
        /// they stand. A forced target is never re-evaluated, so without this leash one hit
        /// sends the unit after its attacker across the whole ship. A drafted shooter's hold
        /// order is leashed the same way, but shorter: while it waits for a target that left its
        /// reach it would not fire at anyone else.
        /// </summary>
        private void UpdateOrderLeash(long ticks) {
            var isHoldOrder = IsHoldOrder;
            if ((!IsRetaliating && !isHoldOrder) || AttackTarget == null) {
                retaliationOutOfReachStartTick = 0;
                return;
            }
            #if !PRODUCTION_BUILD
            DebugLeashRangeChecks++;
            #endif
            if (CanHitTargetFromHere) {
                retaliationOutOfReachStartTick = 0;
                return;
            }
            if (retaliationOutOfReachStartTick == 0) {
                retaliationOutOfReachStartTick = ticks;
                return;
            }
            var leashTicks = isHoldOrder
                ? (long)(Tunables.CombatHoldOrderLeashMinutes * Consts.TicksPerMinute)
                : retaliationLeashTicks;
            if (ticks - retaliationOutOfReachStartTick > leashTicks) {
                SetAttackTarget(null, force: true);
                ValidateConfig();
            }
        }

        // distance: how far the effect reaches, the attack target's distance when below 0
        public void ShowFX(GameState S, string prefab, Vector2 target,
                bool moveWithUnit, float lifetime, float distance = -1f) {
            if (prefab == null) { return; }
            if (G.ClockSpeed > ClockSpeed.Fast2) { return; }
            // D.Warn("ShowingFX: {0}->{1}", prefab, this);
            FX = ParticlesVFX.CreateWithBudget(S, prefab, NozzlePosition,
                attackFxBudgetKey, particleBudget, isCosmetic: false);
            if (FX == null) { return; }
            FX.SetClock(S.Clock);
            if (FX.TryGetFX(out var fx)) {
                if (moveWithUnit) {
                    FX.WithDynamicOwnership(Agent.Being, () => NozzlePosition);
                } else {
                    FX.WithoutOwnership();
                }
                fx.SetVector3(ShaderVars.Target, target - Position);
                fx.SetFloat(ShaderVars.Distance, distance < 0f ? DistToAttackTarget : distance);
                FX.Play(true);
                FX.DestroyAfterGameTime(lifetime);
            } else {
                #if UNITY_EDITOR
                D.Warn("could not get fx with budget {0}: {1}", particleBudget, FX);
                #endif
            }
        }

        [Conditional("UNITY_EDITOR")]
        public void Log(string what) {
            D.Log("{2}|{0}|{1}", this, what, Units.TicksToTime(G.Ticks));
        }

        // When the last hit from outside a friendly fight landed (a shot, a blast, a trap), so a
        // downing can be told violent (RaidAdaptation). Not saved: it only matters for minutes.
        public long LastHostileHitTick;

        // A hit landed on this unit, from anything (a shot, a blast, a trap). Harm ends a
        // gathering raid's wait; being aimed at does not.
        public void OnHarmed() {
            OnHarmed(Position);
        }

        // from: where the blow came from, for the hit flash nudge (the unit's own position when
        // nobody knows)
        public void OnHarmed(Vector2 from) {
            var army = Army;
            if (army != null && army.IsStaging) {
                army.EndStaging(Agent.S, G.Ticks);
            }
            // a fistfight among our own is not violence the raids should remember
            if (army == null || (army.Flags & CombatFlags.Friendly) != CombatFlags.Friendly) {
                LastHostileHitTick = G.Ticks;
            }
            Agent.Being?.Looks?.Flash.Hit(GameTimer.time, Position - from);
        }

        public void NoticeAttack(IAttackable attacker) {
            // whatever this unit does about it, our side and an enemy have met
            Agent.S.Sys.Combat.Alerts.OnAttackNoticed(this, attacker);
            // getting shot on the way to a bed is no reason to turn back
            if (IsRetreating) { return; }
            if (HoldsFire) {
                // only an attacker next to it is answered, in melee (SelectAdjacentMeleeTarget)
                if (NextTargetEvaluateTick > G.Ticks + 1) {
                    NextTargetEvaluateTick = G.Ticks + 1;
                }
                return;
            }
            if (State == CombatUnitState.Unaware) {
                if (EffectiveConfig == CombatUnitConfig.Ignore) {
                    // a unit that can't hit back would only leave its task to stand
                    // next to the attacker (STA-1152: carrier with melee turned off)
                    if (AttackTarget == null && attacker is CombatUnit att && HasUsableAttack()) {
                        // if (Data.Mind.Species.IsBiological) {
                            // fight back
                            SetAttackTarget(att, true);
                            // forced so target evaluation leaves it alone, but leashed
                            // (UpdateOrderLeash) so it never turns into a pursuit
                            IsRetaliating = AttackTarget == att;
                        // }
                    }
                    return;
                }
                if (RespondsInPlace) {
                    // Getting hit is no reason to drop work: the crew answers an attacker it
                    // can hit from where it stands, which target selection finds right away
                    if (NextTargetEvaluateTick > G.Ticks + 1) {
                        NextTargetEvaluateTick = G.Ticks + 1;
                    }
                    return;
                }
                if (AIGoal.Interrupt(Agent.Goal, G.Ticks)) {
                    State = CombatUnitState.Aggressive;
                }
            }
            // D.Warn("Noticing attack: {0}. State: {1}", this, State);
        }

        // same filters SelectAttackMethod applies outside a friendly fight
        private bool HasUsableAttack() {
            var methods = Data.Body.AttackMethods;
            for (int i = 0; i < methods.Count; i++) {
                if (IsUsable(methods[i], false)) { return true; }
            }
            return false;
        }

        private bool IsUsable(AttackMethod meth, bool isFriendly) {
            if (!meth.Provider.HasRequiredAbilities(this)) {
                return false;
            }
            if (isFriendly && !meth.Provider.IsBodyPart) {
                return false;
            }
            if (meth.IsMelee && !Agent.Jobs.CanDo(JobTypeIdH.Melee)) {
                return false;
            }
            if (!meth.IsMelee && (isFriendly || !Agent.Jobs.CanDo(JobTypeIdH.Shooting))) {
                return false;
            }
            return true;
        }

        public void SetAttackTarget(CombatUnit target, bool force = false) {
            if (target != null) {
                var cfg = EffectiveConfig;
                if (!force && cfg == CombatUnitConfig.Ignore) {
                    D.Warn("Setting attack target to ignore without force! {0} => {1}",
                        this, target);
                    return;
                }
                // target.NoticeAttack();
                if (Army == null || State == CombatUnitState.Incapacitated) {
                    D.Err("Trying to set attack target on a combat unit that isn't active! {0} -> {1}",
                        this, target);
                    return;
                }
                if (target.State == CombatUnitState.Incapacitated) {
                    if (target.Data.Body.IsAbleToWork) {
                        D.Err("Fixing incapacitated combat unit who is able to work: {0}", target);
                        target.State = CombatUnitState.Unaware;
                    } else {
                        D.Err("Trying to set an inactive attack target on a combat unit! {0} -> {1}", target, this);
                        return;
                    }
                }
                if (target.Army == null) {
                    D.Err("Trying to set an attack target without an army on a combat unit! {0} -> {1}", target, this);
                    return;
                }
            }
            IsAttackTargetForced = target != null && force;
            // an order sets this after the assignment (CombatOrders)
            OrderMayApproach = false;
            // any assignment replaces a provoked retaliation - NoticeAttack re-marks its own
            IsRetaliating = false;
            retaliationOutOfReachStartTick = 0;
            if (force && target != null) {
                // Data.Mind.CombatConfig = CombatUnitConfig.Fight;
                // D.Warn("Forcing target: {0} for unit {1}", target, this);
                if (Agent.DirectControl.IsUnderDirectControl) {
                    DirectControlAttackTargetId = target.Agent.EntityId;
                }
            }
            if (AttackTarget == target) {
                return;
            }
            accuracyBonus = 0f;
            if (CurrentAttack != null) {
                // a charge does not carry over to a new target
                CurrentAttack.ChargeTicks = 0;
            }
            if (AttackTarget != null) {
                Agent.Motion.CombatSlowdown = 0f;
                AttackTarget.EnemiesTargetingThisUnit.Remove(this);
            } else {
                // we had no attack target
                if (State == CombatUnitState.Unaware) {
                    if (Objective != null) {
                        Objective.OnNoticeTarget(Agent.Being.S, this, target);
                    } else {
                        // D.Warn("Setting state to aggressive: {0}", this);
                        State = CombatUnitState.Aggressive;
                    }
                }
            }
            AttackTarget = target;
            if (target == null) {
                DirectControlAttackTargetId = 0;
            } else {
                if (State == CombatUnitState.Unaware) {
                    State = CombatUnitState.Aggressive;
                }
                Agent.S.Sys.Combat.Alerts.OnTargetTaken(this, target);
            }
            // we just changed the target, so no line of sight
            HasLineOfSight = false;
            IsSightFresh = false;
            lastLosPosIdx = -1;
            lastLosTargetPosIdx = -1;
            target?.EnemiesTargetingThisUnit.Add(this);
            BestAttack = SelectAttackMethod(false);
        }
        public bool IsAttackTargetForced;
        // An ordered target from a save, restored on the first tick: the unit's saved data
        // loads with its AI agent, after the armies, and the target may be in any army.
        // Orders used to be lost on load, and target selection then picked whatever it liked.
        public uint PendingForcedTargetId;

        // Puts back an order from a save. Unlike SetAttackTarget it does not touch the objective:
        // the goals were saved as they were.
        public void RestoreForcedTarget(CombatUnit target) {
            PendingForcedTargetId = 0;
            if (target == null || target.Army == null || Army == null
                    || target.State == CombatUnitState.Incapacitated) {
                IsAttackTargetForced = false;
                return;
            }
            AttackTarget = target;
            IsAttackTargetForced = true;
            target.EnemiesTargetingThisUnit.Add(this);
            HasLineOfSight = false;
            IsSightFresh = false;
            lastLosPosIdx = -1;
            lastLosTargetPosIdx = -1;
            if (Agent.DirectControl.IsUnderDirectControl) {
                DirectControlAttackTargetId = target.Agent.EntityId;
            }
            BestAttack = SelectAttackMethod(false);
        }
        /// <summary>
        /// Forced target that an Ignore unit took on because it was attacked (NoticeAttack),
        /// as opposed to a player order. Released by UpdateOrderLeash once the unit
        /// can't hit it from where it stands. Not persisted: AttackTarget isn't either, so a
        /// loaded unit has no target and ValidateConfig returns it to Unaware.
        /// </summary>
        public bool IsRetaliating;
        public bool CanHitTargetFromHere => HasLineOfSight && CurrentAttack != null
            && CurrentAttack.IsWithinAttackRange(DistToAttackTarget, out _, out _);
        // A shooter's gun reaches its target from where it stands. Sight alone is not enough to
        // settle on a spot: sight reaches 20 tiles and most guns far less, so a raider that saw
        // the crew from afar stood behind its cover for the rest of the fight without firing.
        public bool RangedTargetInReach => HasLineOfSight && AttackTarget != null
            && BestAttack != null && !BestAttack.IsMelee
            && BestAttack.IsWithinAttackRange(DistToAttackTarget, out _, out _);
        private long retaliationOutOfReachStartTick;
        private const int retaliationLeashTicks = Consts.TicksPer10Minutes;
        #if !PRODUCTION_BUILD
        public static int DebugTargetEvaluations;
        public static int DebugLeashRangeChecks;
        #endif
        /// <summary>
        /// If this is set, the unit is performing an attack, we will have to
        /// consider this when executing other actions to slow the unit down, pause, etc
        /// </summary>
        public AttackMethod CurrentAttack;
        /// <summary>
        /// Best possible attack method for engaging current target and dealing
        /// most damage to it
        /// </summary>
        public AttackMethod BestAttack;
        public CoverSlot CurrentCover;
        /// <summary>
        /// Ring buffer of the last few cover positions this unit has moved to, so cover
        /// search can deprioritize them and stop a unit from bouncing back and forth between
        /// the same couple of spots. Not persisted - a transient tactical nudge, not gameplay
        /// state; a unit resuming from save just starts with a clean slate.
        /// </summary>
        private const int recentCoverHistorySize = 3;
        private readonly int[] recentCoverPosIdx = new int[recentCoverHistorySize];
        private int recentCoverCursor;
        public void RecordCoverVisit(int posIdx) {
            recentCoverPosIdx[recentCoverCursor] = posIdx;
            recentCoverCursor = (recentCoverCursor + 1) % recentCoverHistorySize;
        }
        public bool WasRecentCover(int posIdx) {
            for (int i = 0; i < recentCoverHistorySize; i++) {
                if (recentCoverPosIdx[i] == posIdx) { return true; }
            }
            return false;
        }
        public bool HasGoodCover;
        // Whether the unit's tile covers it against its target and every enemy aiming at it,
        // counted as the dice count it (CoverUtils.BlockChance, by the angle of each shot).
        // With no enemy to measure against, any cover next to it counts (CurrentCover).
        // Cached: the AI asks every unit tick.
        public bool HasCover {
            get {
                UpdateCoverBlock(G.Ticks);
                return coverBlock < 0f ? CurrentCover.Total > 0 : coverBlock > GoodCoverBlock;
            }
        }
        // the old directional check counted more than 10 cover in the enemy's direction
        private const float GoodCoverBlock = 0.1f;
        private const long CoverRecheckTicks = 10;
        // the least cover against any of them, -1 with nobody to measure against
        private float coverBlock = -1f;
        private int coverBlockPos = -1;
        private long coverBlockTick = -1;

        private void UpdateCoverBlock(long ticks) {
            if (PosIdx == coverBlockPos && ticks - coverBlockTick < CoverRecheckTicks
                    && coverBlockTick <= ticks) {
                return;
            }
            coverBlockPos = PosIdx;
            coverBlockTick = ticks;
            var s = Agent.S;
            var least = float.MaxValue;
            if (AttackTarget != null) {
                least = CoverUtils.BlockChance(s, PosIdx, AttackTarget.PosIdx);
            }
            for (int i = EnemiesTargetingThisUnit.Count - 1; i >= 0; i--) {
                var block = CoverUtils.BlockChance(s, PosIdx, EnemiesTargetingThisUnit[i].PosIdx);
                if (block < least) {
                    least = block;
                }
            }
            coverBlock = least == float.MaxValue ? -1f : least;
        }
        /// <summary>
        /// 0..1 suppression level. Ranged fire from enemies builds this up
        /// quickly, and landed melee hits add a smaller amount; it decays when
        /// not under fire. At or above CombatSuppressionThreshold the unit is
        /// pinned and cannot attack freely.
        /// </summary>
        public float SuppressionLevel;
        // Extra ticks TickSuppression pauses decay for after a successful stun roll - see
        // AddStunSuppression. Not persisted; a self-correcting simplification (see CombatSystem.md).
        private int stunSuppressionPauseTicksLeft;
        public bool IsSuppressed => SuppressionLevel >= Tunables.CombatSuppressionThreshold;
        public void AddSuppression(float amount) {
            bool wasSuppressed = IsSuppressed;
            SuppressionLevel = math.min(1f, SuppressionLevel + amount);
            if (!wasSuppressed && IsSuppressed) {
                Comp?.ShowSuppressionThoughtBubble();
            }
        }
        /// <summary>
        /// Adds suppression from a successful StunChanceBio/StunChanceRobo roll (see
        /// AttackMethod.TryAttack). Stacks on the ordinary hit/near-miss suppression via the same
        /// AddSuppression pool, and additionally pauses this unit's suppression decay for
        /// extraDecayPauseMinutes so the pin lasts a little longer than a normal hit's.
        /// Set CombatStunSuppressionAmount to 0 in Combat.json to disable the whole mechanic.
        /// </summary>
        public void AddStunSuppression(float amount, float extraDecayPauseMinutes) {
            AddSuppression(amount);
            var extraTicks = (int) math.round(extraDecayPauseMinutes * Consts.TicksPerMinute);
            if (extraTicks > stunSuppressionPauseTicksLeft) {
                stunSuppressionPauseTicksLeft = extraTicks;
            }
        }
        /// <summary>
        /// Clears the pending stun decay pause. TickSuppression bails out before draining it once
        /// SuppressionLevel hits 0, so leaving combat has to reset it explicitly - a CombatUnit
        /// lives as long as its being and gets reused by the next fight.
        /// </summary>
        public void ResetStunSuppression() {
            stunSuppressionPauseTicksLeft = 0;
        }
        /// <summary>
        /// True when this is a melee unit with a target it can strike from its current position.
        /// Uses the same range check as TryAttack so units don't lock in place before they can land a hit.
        /// </summary>
        public bool MeleeInAttackRange {
            get {
                if (BestAttack?.IsMelee != true || AttackTarget == null) { return false; }
                // close enough is not in range across a wall corner (TryCloseContact): the unit
                // would hold there forever without landing a blow
                if (!HasLineOfSight) { return false; }
                return BestAttack.IsWithinAttackRange(DistToAttackTarget, out _, out _);
            }
        }

        /// <summary>
        /// True for a ranged unit when a hostile (attack target or anyone targeting this
        /// unit) is closer than <see cref="ClosestRange"/> - i.e. inside this unit's own
        /// weapon's minimum effective range, where it cannot fire back at full effectiveness.
        /// Drives the PrepareForAttack back-off branch. Always false for melee units,
        /// units with no usable attack, unaware units and fleeing units. Updated once per
        /// tick in <see cref="Tick"/>.
        /// </summary>
        public bool IsEnemyTooClose;

        private void UpdateEnemyTooClose() {
            IsEnemyTooClose = false;
            if (State == CombatUnitState.Unaware || IsFleeing) { return; }
            if (BestAttack?.IsMelee != false) { return; } // no usable attack, or melee: not applicable
            // bots never back off, whatever reaches answers (AICombatActionAdvance)
            if (!IsTactical) { return; }
            var range = ClosestRange;
            if (range <= 0f) { return; }
            var rangeSqr = range * range;
            if (AttackTarget != null
                    && Vectors.SqrDistance(Position, AttackTarget.Position) < rangeSqr) {
                IsEnemyTooClose = true;
                return;
            }
            // Indexed loop over the already-maintained list - avoid CombatSys.FirstEnemyMatching,
            // whose closure allocates and which walks every army; this runs every tick for every unit.
            for (int i = EnemiesTargetingThisUnit.Count - 1; i >= 0; i--) {
                if (Vectors.SqrDistance(Position, EnemiesTargetingThisUnit[i].Position) < rangeSqr) {
                    IsEnemyTooClose = true;
                    return;
                }
            }
        }

        private static FastPriorityQueue<CombatUnit> unitQueue = new(1024);
        private static FastPriorityQueue<AttackMethod> attackQueue = new(16);
        public static void Cleanup() {
            unitQueue.Clear();
            attackQueue.Clear();
        }
        /// <summary>
        /// For prioritizing attack targets
        /// </summary>
        /// <param name="target"></param>
        /// <param name="attacker"></param>
        /// <returns>lower number = higher priority</returns>
        private static bool UnitPriorityFor(CombatUnit target, CombatUnit attacker, out float priority) {
            // here we will have to evaluate attacker attack methods, their perfect distances, etc
            var distToEnemy = Vectors.Distance(target.Position, attacker.Position);
            // TODO look at stats for this
            // (checked before the sight raycast, which costs far more)
            if (distToEnemy > AutoTargetRange) {
                priority = 0f;
                return false;
            }
            // A drafted unit stays where the player put it, so on its own it only picks what it
            // can hit from there. An out of range pick kept it from shooting one that came close.
            // The undrafted crew does the same, except that melee fighters go at enemies close by
            if (attacker.BestAttack != null && distToEnemy > ReachOnItsOwn(attacker)) {
                priority = 0f;
                return false;
            }
            if (!EntityUtils.HasLineOfSightCover(attacker.SightPosition,
                    target.SightPosition, 0.5f, SightRange, out var maxCover)) {
                priority = 0f;
                return false;
            }

            var isG = attacker.Agent.Motion.IsGrounded;
            if (isG && attacker.BestAttack?.IsMelee != false) {
                // check path
                int regDist = attacker.Agent.S.Query.RegionDist.Ask(
                    RegPathReq.Create(attacker.PosIdx, target.PosIdx, isG, false));
                if (regDist == -1) {
                    // no way to reach the target
                    priority = 0f;
                    return false;
                }
            }

            float currentTargetBonus = 1f;
            switch (target.EnemiesTargetingThisUnit.Count) {
                case 0: currentTargetBonus *= 1.1f; break;
                case 1: currentTargetBonus *= 0.9f; break;
                case 2: currentTargetBonus *= 1.1f; break;
                case 3: currentTargetBonus *= 5.0f; break;
                default: currentTargetBonus *= 20.0f; break;
            }

            // we don't want to change the target too frequently, so we will increase the priority of
            // current target if there is one
            if (target == attacker.AttackTarget) {
                currentTargetBonus *= 0.75f;
            }
            // Less cover = higher priority
            float coverInf = math.lerp(0.5f, 2.0f, maxCover / 100f);
            priority = distToEnemy * currentTargetBonus * coverInf;
            return true;
        }

        // How far a unit picks targets on its own: no limit but sight for units that go after
        // them, the reach of its attack for a drafted unit, and for the undrafted crew its gun's
        // reach or, for a melee fighter, CombatMeleeResponseRadius
        private static float ReachOnItsOwn(CombatUnit attacker) {
            if (attacker.Agent.DirectControl.IsUnderDirectControl) {
                return attacker.BestAttack.Stats.RangeMax.Value;
            }
            if (attacker.RespondsInPlace) {
                return attacker.BestAttack.IsMelee
                    ? Tunables.CombatMeleeResponseRadius
                    : attacker.BestAttack.Stats.RangeMax.Value;
            }
            return float.MaxValue;
        }

        /// <summary>
        /// Selecting best target will be expensive, so we want to do this only
        /// once in a while
        /// </summary>
        public long NextTargetEvaluateTick;
        public float DistToAttackTarget;
        public bool HasLineOfSight;
        // HasLineOfSight and DistToAttackTarget were measured for the current target. A target set
        // between ticks has neither yet, and the AI may run before this unit ticks.
        public bool IsSightFresh;
        /// <summary>
        /// Amount of cover the target has in direct line of sight,
        /// does not include the cover from target position slot
        /// </summary>
        public byte TargetDirectCoverAmt;
        // We'll track how many hits we performed at the target while in
        // prepare for attack ai action
        public int AttackTargetHitsMade;
        private int lastLosPosIdx = -1;
        private int lastLosTargetPosIdx = -1;
        private int lastLosCoverVersion;

        public void ValidateConfig() {
            if (State == CombatUnitState.Incapacitated) { return; }
            var cfg = EffectiveConfig;
            if (!Data.IsPlayer) {
                var army = Army;
                if (army != null) {
                    RollRoutFleeFor(army);
                }
                // Only a unit that can get off the map retreats (EscapeRoute). One without a way
                // out fights on until it is down or dead: hiding somewhere would keep hostile
                // combat open with nobody coming for it, since colonists only engage when
                // attacked or ordered to. The way out is checked last, it is the costly part.
                var routing = IsLeaving || WantsToFleeOnRout && army != null && army.IsRouting();
                var badlyHurt = !Data.Species.IsWild
                    && Data.Body.VitalityPercent < Tunables.CombatEnemyFleeHpThreshold;
                if ((routing || badlyHurt) && army != null
                        && (army.Flags & CombatFlags.Friendly) != CombatFlags.Friendly
                        && !GaveUpFleeing
                        && EscapeRoute.Exists(this)) {
                    // Army has collapsed past the rout threshold and this unit broke, or it is
                    // too hurt to keep fighting.
                    if (Data.Mind.CombatConfig != CombatUnitConfig.Flee) {
                        Data.Mind.CombatConfig = CombatUnitConfig.Flee;
                    }
                    cfg = CombatUnitConfig.Flee;
                } else if (cfg != CombatUnitConfig.Fight) {
                    D.Warn("Non-player combat config was not set to fight, fixing: {0}", this);
                    Data.Mind.CombatConfig = CombatUnitConfig.Fight;
                    cfg = CombatUnitConfig.Fight;
                }
            }
            switch (cfg) {
                case CombatUnitConfig.FreeWill: return;
                case CombatUnitConfig.Ignore: {
                    // a target picked while drafted (fire at will) is dropped once undrafted;
                    // a retaliation is forced and stays
                    if (AttackTarget != null && !IsAttackTargetForced) {
                        SetAttackTarget(null, force: true);
                    }
                    if (State != CombatUnitState.Unaware && AttackTarget == null) {
                        // only the fight is ignored - a need or a task the unit went back
                        // to while still aggressive keeps running
                        if (Agent.Goal?.Type == AIGoalType.Combat) {
                            AIGoal.Cancel(Agent.Goal, G.Ticks, T.IgnoreCombat);
                        }
                        State = CombatUnitState.Unaware;
                    }
                    return;
                }
                case CombatUnitConfig.Fight: {
                    if (State == CombatUnitState.Fleeing
                            && (Data.Mind.CombatConfig == CombatUnitConfig.FreeWill
                                || Agent.DirectControl.IsUnderDirectControl)
                            && !Data.HasCondition(ConditionIdH.MentalBreakdown)) {
                        // Free Will turned back to fighting (armed again, or healed), or
                        // a fleeing unit was drafted
                        State = CombatUnitState.Unaware;
                    }
                    if (State == CombatUnitState.Unaware) {
                        // prevent all friendlies and crawlers from suddenly
                        // starting to seek enemy when someone attacks their faction.
                        // A guest sheltering with the crew only fights back once hit.
                        if (Data.IsPlayer || Data.IsProtectedGuest || Data.Species.IsAnimal) {
                            return;
                        }
                        // D.Warn("Setting state to fight / aggressive: {0}", this);
                        State = CombatUnitState.Aggressive;
                    } else if (State == CombatUnitState.Aggressive
                            && (Data.IsPlayer || Data.IsProtectedGuest)
                            && AttackTarget == null
                            && !HasLineOfSight
                            && EnemiesTargetingThisUnit.Count == 0
                            && TurretsTargetingThisUnit.Count == 0
                            && !IsSuppressed) {
                        if (Agent.Goal?.Type != AIGoalType.Combat) {
                            State = CombatUnitState.Unaware;
                        }
                    }
                    return;
                }
                case CombatUnitConfig.Flee: {
                    if (AttackTarget != null) {
                        SetAttackTarget(null, force: true);
                    }
                    CurrentAttack = null;
                    if (State != CombatUnitState.Fleeing) {
                        State = CombatUnitState.Fleeing;
                    }
                    return;
                }
            }
        }

        public static bool IsBadPos(GameState s, int pos) {
            return s.Sys.Path.IsDoorOrTeleporter(pos) || s.Beings.CountAt(pos) > 1;
        }

        private void TickSuppression() {
            if (SuppressionLevel <= 0f) { return; }
            if (stunSuppressionPauseTicksLeft > 0) {
                stunSuppressionPauseTicksLeft--;
                return;
            }
            bool wasSuppressed = IsSuppressed;
            var decayPerTick = 1f / (Tunables.CombatSuppressionDecayMinutes * Consts.TicksPerMinute);
            SuppressionLevel = math.max(0f, SuppressionLevel - decayPerTick);
            if (wasSuppressed && !IsSuppressed) {
                Comp?.HideSuppressionThoughtBubble();
            }
        }

        public void Tick(GameState s, long ticks) {
            LastTick = ticks;
            if (PendingForcedTargetId != 0) {
                RestoreForcedTarget(s.Query.FindBeingData.Ask(PendingForcedTargetId)?.Being
                    ?.AIAgent?.CombatUnit);
            }
            var pos = PosIdx;
            CurrentCover = s.Query.CoverSlotAt.Ask(pos);
            TickSuppression();
            ValidateConfig();
            UpdateRetreat(ticks);
            UpdateCommand(ticks);
            ValidateFleeProgress(ticks);
            // break up friendly fights in autodoc
            if (Data.HasStateFlag(BeingState.InDevice) && Army != null) {
                if ((Army.Flags & CombatFlags.Friendly) == CombatFlags.Friendly) {
                    Army.RemoveUnit(this, CombatUnitState.Unaware);
                    return;
                }
            }
            if (Data.HasCondition(ConditionIdH.Sleeping)) {
                // ignore sleeping units - we return before AttackIfPossible, so clear the
                // suppression slowdown here or it stays applied for the whole sleep
                Agent.Motion.CombatSlowdown = 0f;
                return;
            }
            IsInBadPos = IsBadPos(s, pos);
            if (canAttack == null || attackMethodsVersion != Data.Body.AttackMethodsVersion) {
                attackMethodsVersion = Data.Body.AttackMethodsVersion;
                BestAttack = SelectAttackMethod(false);
            }
            ConsiderShuffle(s);
            if (State != CombatUnitState.Fleeing && !IsRetreating) {
                SelectBestAttackTarget(s, ticks);
            }
            shouldFaceTarget = State == CombatUnitState.Aggressive;
            if (AttackTarget != null) {
                HandleAttackTarget();
            } else {
                BestAttack ??= SelectAttackMethod(false);
                DistToAttackTarget = 0f;
            }
            if (AttackTarget != null) {
                if (!IsInBadPos && Pos.AreAdjacent(pos, AttackTarget.PosIdx) && CurrentAttack?.IsMelee == true) {
                    // don't melee-attack a target that stands on top of a dead or incapacitated body
                    IsInBadPos = IsBadPos(s, AttackTarget.PosIdx);
                }
                // a drafted unit does not walk to its own picks: look for one it can hit soon,
                // not after the usual re-evaluation delay
                if (!IsAttackTargetForced && !CanHitTargetFromHere
                        && (Agent.DirectControl.IsUnderDirectControl
                            || (RespondsInPlace && BestAttack?.IsMelee == false))
                        && NextTargetEvaluateTick > ticks + 3) {
                    NextTargetEvaluateTick = ticks + Rng.Range(1, 4);
                }
            }
            UpdateEnemyTooClose();
            UpdateNoAttackStall(ticks);
            UpdateOrderLeash(ticks);
            AttackIfPossible(s, ticks);
            HasGoodCover = IsInGoodCover(this);
        }

        private void ConsiderShuffle(GameState s) {
            if (State == CombatUnitState.Unaware ||
                    Agent.Motion.IsBusy || Agent.Motion.IsImmobilized) {
                return;
            }
            // Only a unit on a combat goal can be stuck in combat. An Aware or Fleeing
            // colonist on a workout or a job would be walked away from it (STA-1587)
            if (Agent.Goal?.Type != AIGoalType.Combat) { return; }
            // Melee units never shuffle — they hold position and keep attacking
            if (BestAttack?.IsMelee == true) { return; }
            // Ranged units only shuffle as last resort when genuinely stuck: no cover and no LOS
            if (HasCover || HasLineOfSight) { return; }
            if (Agent.Motion.TicksInCurrentPosition > Consts.TicksPer30Minutes
                    && !Agent.IsIdle && Rng.Chance(0.01f)) {
                if (!TryShuffle(s, AttackTarget)) {
                    D.Warn("Can't shuffle stuck ranged combat unit for {0}: {1} | {2} | {3} | {4}",
                        Units.TicksToTime(Agent.Motion.TicksInCurrentPosition), this, Agent.CurrentAction, Agent.Goal, Agent);
                }
            }
        }

        private void HandleAttackTarget() {
            if (AttackTarget.LastTick < G.Ticks - 3) {
                if (AttackTarget.LastTick == 0) {
                    AttackTarget.LastTick = G.Ticks;
                } else {
                    D.Err("Attack Target hasn't been ticked for: {0} ticks. {1} -> {2}",
                        G.Ticks - AttackTarget.LastTick, this, AttackTarget);
                }
            }
            if (AttackTarget.Data.HasStateFlag(BeingState.Hidden) || AttackTarget.Army == null
                    || AttackTarget.State == CombatUnitState.Incapacitated
                    // the target changed sides and joined our army; forced targets are
                    // never re-evaluated, so this is the only place that lets go of them
                    || (Army != null && AttackTarget.Army == Army)
                    // Friendly units stop pursuing a target that starts fleeing,
                    // unless the player explicitly forced this attack target.
                    || (Data.IsPlayer && !IsAttackTargetForced && AttackTarget.IsFleeing)) {
                SetAttackTarget(null);
                BestAttack ??= SelectAttackMethod(false);
                DistToAttackTarget = 0f;
            } else {
                DistToAttackTarget = Vectors.Distance(Position, AttackTarget.Position);
                var hadLineOfSight = HasLineOfSight;
                int myPos = PosIdx;
                int targetPos = AttackTarget.PosIdx;
                if (DistToAttackTarget < 2f && TryCloseContact(myPos, targetPos, out var canReach)) {
                    HasLineOfSight = canReach;
                    if (canReach) {
                        shouldFaceTarget = true;
                    }
                    // the sight check below runs afresh once they are apart again
                    lastLosPosIdx = -1;
                } else {
                    // a door that shuts between them moves neither (STA-1747)
                    var coverVersion = Agent.S.Query.GetCoverVersion.Ask();
                    if (myPos != lastLosPosIdx || targetPos != lastLosTargetPosIdx
                            || coverVersion != lastLosCoverVersion) {
                        lastLosPosIdx = myPos;
                        lastLosTargetPosIdx = targetPos;
                        lastLosCoverVersion = coverVersion;
                        HasLineOfSight = EntityUtils.HasLineOfSightCover(SightPosition,
                            AttackTarget.SightPosition, 0.5f, SightRange, out TargetDirectCoverAmt);
                    }
                }
                IsSightFresh = true;
                if (hadLineOfSight && !HasLineOfSight && CurrentAttack != null) {
                    // an interrupted charge starts over, or the next sighting fires at once
                    CurrentAttack.ChargeTicks = 0;
                }
                var m = SelectAttackMethod(true);
                if (m != CurrentAttack) {
                    #if DEBUG_COMBAT
                    D.Warn("Selected attack against target: {0} -> {1} | {2} -> {3}",
                        CurrentAttack, m, this, AttackTarget);
                    #endif
                    if (CurrentAttack != null) {
                        CurrentAttack.ChargeTicks = 0;
                    }
                    CurrentAttack = m;
                }
            }
        }

        // the cover above which a corner tile keeps melee from reaching past it diagonally
        private const byte MaxCornerCover = 50;

        // Units on the same or neighboring tiles: true, and whether they can reach each other.
        // Being close in world units is not enough. A diagonal neighbor across a wall corner, or
        // a unit passing a wall mid-move, counted as in sight and was hit through the wall, and
        // the sight raycast cannot tell: on an exact diagonal it passes the corner point between
        // the two wall tiles.
        private bool TryCloseContact(int myPos, int targetPos, out bool canReach) {
            if (myPos == targetPos) {
                canReach = true;
                return true;
            }
            Pos.ToXY(myPos, out var x, out var y);
            Pos.ToXY(targetPos, out var tx, out var ty);
            var dx = tx - x;
            var dy = ty - y;
            if (dx < -1 || dx > 1 || dy < -1 || dy > 1) {
                canReach = false;
                return false;
            }
            if (dx == 0 || dy == 0) {
                canReach = true;
                return true;
            }
            var cover = Agent.S.Query.GetCoverMap.Ask();
            canReach = cover[Pos.FromXY(tx, y)] <= MaxCornerCover
                && cover[Pos.FromXY(x, ty)] <= MaxCornerCover;
            return true;
        }

        private void AttackIfPossible(GameState s, long ticks) {
            // Fleeing units are never slowed by suppression - a pinned unit that decides to run
            // must be able to, or CombatEnemyRoutFleeChance and CombatMeleeEscapeHpThreshold
            // can never be acted on (nothing else stops a fleeing unit: HoldPositionPriorityResolver
            // needs IsAtClaimedPos, and ValidateFleeProgress ignores units on a Combat goal).
            Agent.Motion.CombatSlowdown = IsSuppressed && !IsFleeing
                ? Tunables.CombatSuppressedMoveSpeedMul
                : 0f;
            if (IsWalkingOnOrder || IsRetreating) {
                // A move order cancels the aim, or every charge slowed the walk and a retreat
                // crawled under fire. The weapon still cools down, so it fires once it stops.
                // A wounded colonist on its way to a bed does not stop to aim either.
                if (CurrentAttack != null) {
                    CurrentAttack.ChargeTicks = 0;
                    CurrentAttack.TickCooldown(ticks);
                }
                shouldFaceTarget = false;
                return;
            }
            bool mustFaceTarget = false;

            if (HasLineOfSight) {
                if (TryAttack(s, ticks, out mustFaceTarget)) {
                    // do we need a bool return value?
                    AttackTargetHitsMade++;
                }
            }
            // } else {
            //     Agent.Being.Name.Debug($"No LOS M:{CurrentAttack?.IsMelee}");
            if (mustFaceTarget) {
                shouldFaceTarget = true;
            }
        }

        private void SelectBestAttackTarget(GameState s, long ticks) {
            if (IsFleeing) {
                if (AttackTarget != null) {
                    SetAttackTarget(null, force: true);
                }
                return;
            }
            // Ignore units only fight back when provoked (NoticeAttack) or ordered - any
            // target picked here would be rejected by SetAttackTarget anyway
            if (EffectiveConfig == CombatUnitConfig.Ignore) { return; }
            if (ticks < NextTargetEvaluateTick) { return; }
            if (IsAttackTargetForced && AttackTarget != null &&
                    AttackTarget.State != CombatUnitState.Incapacitated) {
                return;
            }
            if (HoldsFire) {
                SelectAdjacentMeleeTarget(s, ticks);
                return;
            }
            if (Objective == null) {
                // let's not set the target yet
                return;
            }
            #if !PRODUCTION_BUILD
            DebugTargetEvaluations++;
            #endif
            var prevTarget = AttackTarget;
            if (AttackTarget?.State == CombatUnitState.Incapacitated) {
                SetAttackTarget(null);
            }

            var armies = s.Query.GetArmies.Ask();

            var myArmy = Army;
            unitQueue.Clear();
            for (int i = armies.Count - 1; i >= 0; i--) {
                Army a = armies[i];
                if (a == myArmy) { continue; }
                foreach (var u in a.Units) {
                    if (u.State == CombatUnitState.Incapacitated) { continue; }
                    // Friendly units don't chase fleeing enemies (e.g. raiders running
                    // for the map edge). Player-forced targets bypass this method entirely.
                    if (Data.IsPlayer && u.IsFleeing) { continue; }
                    if (UnitPriorityFor(u, this, out var priority)) {
                        unitQueue.Enqueue(u, priority);
                    }
                }
            }
            if (unitQueue.Count == 0) {
                #if DEBUG_COMBAT
                // D.Warn("Could not find any attack targets for {0}", this);
                #endif
                unitQueue.Clear();
                // a drafted unit lets go of a pick it can no longer hit from where it stands,
                // and so does the undrafted crew
                if (AttackTarget != null && !IsAttackTargetForced
                        && (Agent.DirectControl.IsUnderDirectControl || RespondsInPlace)) {
                    SetAttackTarget(null);
                }
                // we don't have a target, we will be looking for one soon
                NextTargetEvaluateTick = ticks + Rng.Range(Consts.TicksPerMinute,
                    Consts.TicksPer3Minutes);
                return;
            }

            NextTargetEvaluateTick = ticks + Rng.Range(
                (int)(Tunables.CombatTargetReevalMinMinutes * Consts.TicksPerMinute),
                (int)(Tunables.CombatTargetReevalMaxMinutes * Consts.TicksPerMinute));
            // what the unit can use changes without the list changing (a lost hand), so the
            // role is re-picked here too, not only with a new target
            BestAttack = SelectAttackMethod(false);

            var selectedTarget = unitQueue.Dequeue();
            if (AttackTarget != selectedTarget) {
                #if DEBUG_COMBAT
                D.Warn("Assigning target for {0} => {1}", this, selectedTarget);
                #endif
                SetAttackTarget(selectedTarget);
                if (Agent.CurrentAction is IAICombatAction comAct) {
                    comAct.OnAttackTargetChanged(Agent, Agent.Goal,
                        prevTarget, selectedTarget);
                }
                #if DEBUG_COMBAT
            } else {
                D.Warn("Still having same target; {0} => {1}", this, selectedTarget);
                #endif
            }
            unitQueue.Clear();
        }

        /// <summary>
        /// With each miss, we'll gain some accuracy bonus, which will be reset
        /// after a successful hit. This way even bad accuracy won't result in
        /// endless shooting without hitting the target
        /// </summary>
        private float accuracyBonus;

        private bool TryAttack(GameState s, long ticks, out bool mustFaceTarget) {
            var cfg = EffectiveConfig;
            if (CurrentAttack == null || cfg == CombatUnitConfig.Flee) {
                mustFaceTarget = false;
                return false;
            }
            // Suppressed units are pinned — they skip attacks unless they randomly return fire
            if (IsSuppressed && !Rng.Chance(Tunables.CombatSuppressedAttackChance)) {
                mustFaceTarget = false;
                return false;
            }
            if (!CurrentAttack.Provider.HasRequiredAbilities(this)) {
                // we lost the ability to use this attack!
                CurrentAttack = null;
                mustFaceTarget = false;
                // Agent.Being.Name.Debug($"Try Attack nop 2 M:{CurrentAttack?.IsMelee}");
                return false;
            }
            // melee attacks have lower chance to be performed while in motion
            if (!Agent.Motion.IsAtClaimedPos && CurrentAttack.IsMelee
                    && Rng.Chance(Tunables.CombatSkipMeleeAttackWhileMovingChance)) {
                mustFaceTarget = false;
                // Agent.Being.Name.Debug($"Try Attack nop 3 M:{CurrentAttack.IsMelee}");
                return false;
            }
            if (CurrentAttack.TryAttack(s, this, AttackTarget,
                    DistToAttackTarget, ticks, accuracyBonus, out mustFaceTarget,
                    out bool attackPerformed)) {
                accuracyBonus = 0f;
                // Agent.Being.Name.Debug($"Try Attack OK M:{CurrentAttack.IsMelee}");
                return true;
            } else {
                if (attackPerformed) {
                    accuracyBonus += Tunables.AccuracyBonusAfterEachMiss;
                }
                // Agent.Being.Name.Debug($"Try Attack Fail M:{CurrentAttack.IsMelee}");
                return false;
            }
        }

        public static CombatUnit CreateFor(AIAgentComp agent) {
            var unit = new CombatUnit() {
                Agent = agent,
                Rng = Rng.Unseeded
            };
            return unit;
        }

        public override string ToString() {
            return $"CombatUnit[{Agent.BeingData.Id}|{Agent.BeingData.Name}|Cfg:{Config}|A:{Agent.Being.IsActive}|State:{Agent.BeingData.StateFlags}|Body:{Agent.BeingData.Body.StateB}|{State}]";
        }

        public static bool IsInGoodCover(CombatUnit cu) {
            return cu.HasCover;
        }

        public AttackMethod BestMeleeAttack(bool fallbackToRanged) {
            AttackMethod best = null;
            AttackMethod rangedFallback = null;
            float maxDPSRanged = 0f;
            float maxDPS = 0f;
            foreach (var meth in Data.Body.AttackMethods) {
                if (!meth.Provider.HasRequiredAbilities(this)) {
                    continue;
                }
                if (!meth.IsMelee && !fallbackToRanged) {
                    continue;
                }
                if (meth.IsMelee && !Agent.Jobs.CanDo(JobTypeIdH.Melee)) {
                    continue;
                }
                var dps = meth.Stats.DPS.Value;
                if (meth.IsMelee) {
                    if (dps > maxDPS) {
                        maxDPS = dps;
                        best = meth;
                    }
                } else {
                    if (dps > maxDPSRanged) {
                        rangedFallback = meth;
                        maxDPSRanged = dps;
                    }
                }
            }
            if (best == null && fallbackToRanged) {
                return rangedFallback;
            }
            return best;
        }


        public AttackMethod SelectAttackMethod(bool forCurSituation) {
            // holding fire, the unit only hits back in melee at what stands next to it
            var meleeOnly = HoldsFire && !IsAttackTargetForced && AttackTarget != null;
            if (forCurSituation) {
                // Cannot change the attack in mid run, unless it's melee
                if (CurrentAttack != null && !CurrentAttack.IsMelee
                        && CurrentAttack.CooldownTicksLeft > 0 && !meleeOnly) {
                    return CurrentAttack;
                }
            }
            if (AttackTarget == null) {
                CurrentAttack = null;
            }
            attackQueue.Clear();
            var isFriendly = Army != null && ((Army.Flags & CombatFlags.Friendly) == CombatFlags.Friendly);
            foreach (var meth in Data.Body.AttackMethods) {
                if (!IsUsable(meth, isFriendly)) {
                    continue;
                }
                if (meleeOnly && !meth.IsMelee) {
                    continue;
                }
                if (forCurSituation) {
                    if (meth.TryPriorityFor(AttackTarget, DistToAttackTarget, out var prio)) {
                        // An attack without an accuracy curve (fists) scores at any distance.
                        // Out of its reach it outscored a gun at the far end of the gun's range,
                        // and the unit stood in range of its target without ever firing.
                        if (!meth.IsWithinAttackRange(DistToAttackTarget, out _, out _)) {
                            prio -= OutOfReachPenalty;
                        }
                        attackQueue.Enqueue(meth, -prio);
                    }
                } else {
                    if (meth.TryPriorityFor(AttackTarget, out var prio)) {
                        attackQueue.Enqueue(meth, -RolePriority(meth, prio));
                    }
                }
            }
            if (attackQueue.Count == 0) {
                canAttack = false;
                #if DEBUG_COMBAT
                D.Warn("No attack method found for current attack target: {0} -> {1}",
                    this, AttackTarget);
                #endif
                return null;
            }
            var selected = attackQueue.Dequeue();
            canAttack = true;
            return selected;
        }

        // The best attack sets the unit's role, and a unit with a usable gun is a shooter: ranked
        // by accuracy and damage alone, a poor gun lost to fists, and the colonist holding it
        // charged in and never took cover
        private static float RolePriority(AttackMethod meth, float prio) {
            return meth.IsMelee ? prio : prio + RangedRoleBonus;
        }
        private const float RangedRoleBonus = 1000000f;
        private const float OutOfReachPenalty = 1000000f;

        // What SelectAttackMethod(false) would pick, for UI. SelectAttackMethod itself must not
        // be called from UI: it writes canAttack (Tick only fills BestAttack while that is null),
        // can clear CurrentAttack and reuses the shared attackQueue.
        public AttackMethod PreviewBestAttack() {
            var isFriendly = Army != null && ((Army.Flags & CombatFlags.Friendly) == CombatFlags.Friendly);
            AttackMethod best = null;
            float bestPrio = float.MinValue;
            var methods = Data.Body.AttackMethods;
            for (int i = 0; i < methods.Count; i++) {
                var meth = methods[i];
                if (!IsUsable(meth, isFriendly)) {
                    continue;
                }
                if (meth.TryPriorityFor(AttackTarget, out var prio)
                        && RolePriority(meth, prio) > bestPrio) {
                    bestPrio = RolePriority(meth, prio);
                    best = meth;
                }
            }
            return best;
        }

        public bool TryShuffle(GameState S, IAttackable target) {
            var isUnderDC = Agent.DirectControl.IsUnderDirectControl;
            if (isUnderDC) {
                return false;
            }
            // Melee units never shuffle — they commit to attack position
            // if (BestAttack?.IsMelee == true) {
            //     return false;
            // }
            if (PosIdx < 1) {
                D.Err("Trying to shuffle a combat unit with PosIdx < 1: {0} | {1}", PosIdx, this);
                return false;
            }
            int p;
            var nav = Agent.Motion;
            if (nav.IsBusy || nav.IsImmobilized) {
                return false;
            }
            // a being in a slot (treadmill, chair, bed) would keep using it from afar
            if (Data.HasStateFlag(BeingState.InDevice)) {
                return false;
            }
            #if UNITY_EDITOR
            D.ErrSoft("Combat unit trying to shuffle! {0}", this);
            #endif
            if (target == null) {
                p = PosIdx;
            } else {
                p = target.AttackablePosIdx;
            }
            if (target != null && DistToAttackTarget < 2f) {
                // we are clashed in melee, need to dance around the target
                Caches.PosIdx9[0] = Pos.N(p);
                Caches.PosIdx9[1] = Pos.S(p);
                Caches.PosIdx9[2] = Pos.E(p);
                Caches.PosIdx9[3] = Pos.W(p);
                Arrays.Shuffle(Rng, Caches.PosIdx9, 4);

                for (int i = 0; i < 4; i++) {
                    var pos = Caches.PosIdx9[i];
                    if (S.Sys.Path.IsNavigable(pos, nav.IsGrounded, 0.5f, out int claimedById)) {
                        if (claimedById == 0) {
                            if (S.Beings.CountAt(pos) == 0) {
                                if (nav.TryGoTo(pos, PathReqFlags.AdjOnTop)) {
                                    // D.Warn("Shuffling to: {0}. 1", Pos.StringFromTo(PosIdx, pos));
                                    return true;
                                }
                            }
                        }
                    }
                }
                // there was no empty space around! too crowded. Let's get away from here
                var near = S.Query.RandomSafePosNearby.Ask(p, Agent, 1, 0.3f);
                // var near = S.Sys.Regions.RandomSafePosNearbyBROKEN(target.PosIdx, unit.Agent.Being, 1);
                if (near != Pos.Invalid) {
                    // D.Warn("Shuffling to: {0}. 2", Pos.StringFromTo(PosIdx, near));
                    return nav.TryGoTo(near, PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby);
                } else {
                    return false;
                }
            }

            // let's not stand in the same tile
            var near2 = S.Query.RandomSafePosNearby.Ask(p, Agent, 1, 0.3f);
            // var near = S.Sys.Regions.RandomSafePosNearbyBROKEN(target.PosIdx, unit.Agent.Being, 1);
            if (near2 != Pos.Invalid) {
                // D.Warn("Trying to shuffle towards: {0} by 15%", Pos.StringFromTo(p, near2));
                return nav.TryGoTowards(near2, 0.15f);
            }
            return false;
        }

        public Vector2 AttackablePosition => Position;
        public int AttackablePosIdx => PosIdx;

        public string StateName {
            get {
                switch (State) {
                    // case CombatUnitState.Aware: return T.CombatUnitStateAware;
                    case CombatUnitState.Unaware: return T.CombatUnitStateUnaware;
                    case CombatUnitState.Aggressive: return T.CombatUnitStateAggressive;
                    case CombatUnitState.Fleeing: return T.CombatUnitStateFleeing;
                    case CombatUnitState.Incapacitated: return T.BeingStateIncapacitated;
                }
                D.Err("Untranslated combat state: {0}", State);
                return State.ToString();
            }
        }

        public string Name => Texts.LinkToBeing(Agent.BeingData);

        public bool CanAttack => canAttack != false;
        /// <summary>
        /// True if this unit is actively fleeing combat, either because its
        /// behavioral state is Fleeing or its configured intent is Flee.
        /// </summary>
        public bool IsFleeing =>
            State == CombatUnitState.Fleeing
            || EffectiveConfig == CombatUnitConfig.Flee;
        public bool CanIdle {
            get {
                if (State == CombatUnitState.Fleeing
                        && Army != null
                        && !Agent.Motion.IsImmobilized
                        && Data.HasAbility(AbilityIdH.Move)) {
                    return false;
                }
                return (State != CombatUnitState.Aggressive && AttackTarget == null) || !CanAttack;
            }
        }
        private bool? canAttack;
        // Body.AttackMethodsVersion when BestAttack was last picked
        private int attackMethodsVersion = -1;
        public int EnemiesTargetingThis => EnemiesTargetingThisUnit.Count;

        public bool ShouldFaceTarget(Vector2 pos, Facing.Type lastFacing, out Facing.Type f) {
            if (!shouldFaceTarget) {
                f = Facing.Type.Down;
                return false;
            }
            if (AttackTarget != null) {
                f = Facing.FromDirection(AttackTarget.Position - pos, lastFacing);
                return true;
            }
            f = Facing.Type.Down;
            return false;
        }

        public void RefreshCover() {
            if (CurrentCover.PosIdx != PosIdx) {
                CurrentCover = The.R.Query.CoverSlotAt.Ask(PosIdx);
            }
        }
        public void ClearGoals() {
            if (Goals.Count == 0) { return; }
            if (Goals.Contains(Agent.Goal)) {
                // D.Warn("Cancelling CombatUnit's goal when clearing goals: {0} | {1}",
                //     Agent.Goal, this);
                AIGoal.Cancel(Agent.Goal, G.Ticks);
            }
            if (Goals.Contains(Agent.TopPersonalGoal)) {
                Agent.TopPersonalGoal = null;
            }
            Goals.Clear();
        }

        public void OnAddToArmy(Army army) {
            // refresh the cover when adding a unit
            if (Army != null && Army != army) {
                D.Err("Trying to add a unit to an army when it already belongs to a different army! {0}, new Army: {1}",
                    this, army);
            }
            Army = army;
            CurrentCover = Agent.S.Query.CoverSlotAt.Ask(PosIdx);
            RollRoutFleeFor(army);
        }

        public float ClosestRange {
            get {
                if (BestAttack == null) { return 5f; }
                if (BestAttack.IsMelee) { return 0f; }
                // ranged: the "keep away" distance, used both for the HoldPosition GTFO
                // check and for IsEnemyTooClose / cover selection below. Prefer RangeMin+1
                // (or the tunable floor, whichever is larger, so short/no-RangeMin weapons
                // like the SMG still treat an adjacent enemy as "too close"), but never ask
                // the unit to retreat past its own weapon's effective range.
                var bas = BestAttack.Stats;
                var wanted = math.max(bas.RangeMin.Value + 1f, Tunables.CombatRangedTooCloseDist);
                if (bas.RangeMax.Value > wanted) {
                    return wanted;
                }
                return bas.RangeMin.Value;
            }
        }
    }
}
