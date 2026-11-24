using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Utils;
using Game.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AINeedEquipment : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedEquipment());
        }

        public override int[] RelatedStats => null;

        public override bool RequiresAttention(BeingData data, out string iconId, out float priority) {
            iconId = IconId.CNoClothing;
            priority = 2f;
            if (data.Inventory == null) { iconId = null; return false; }
            return data.Species.IsHumanoid && data.Inventory.IsNaked;
        }

        public override void Tick(GameState s, BeingData data, long ticks) {

            if (data.Inventory == null) {
                // cat? pig? dog?
                return;
            }
            const int minute = 8;
            // Before the sleeping / incapacitated returns below: these permanent effects
            // must keep being confirmed while the being lies still, and removed the moment
            // their cause is gone.
            if (ticks % Consts.TicksPer15Minutes == minute) {
                TickGearMood(s, data);
            }
            if (data.HasCondition(ConditionIdH.Sleeping)) { return; }
            if (data.Body.IsIncapacitated) { return; }
            // Don't steal our shit!
            if (data.IsGuest) { return; }
            var invCnt = data.Inventory?.Equipment.Count ?? 0;
            if (invCnt == 0) {
                // throttle to avoid log spam now that Tick runs every game tick
                if (ticks % Consts.TicksPerHour == minute) {
                    D.Err("Equipment need exists for someone who doesn't have any equipment slots! {0}",
                        data);
                }
                return;
            }
            if (invCnt == 1) {
                // we likely can only equip just a weapon, let's make sure that we can use it
                // before creating the goal
                if (data.Inventory.Equipment.TryGetValue(InventorySlotIdH.Weapon, out var weapon)) {
                    if (!data.Mind.Jobs.CanHaveWeapon()) {
                        return;
                    }
                }
            }
            var agent = data.Being.AIAgent;

            if (!data.Inventory.AnyAutoEquip(s)) {
                // Every slot resolves to Manual, so this being must not go shopping.
                // Drop a goal left over from a policy the player has since changed.
                if (agent.BB.TryNeedGoal(IdH, out var stale)
                        && (stale.State == AIGoalState.Available
                            || stale.State == AIGoalState.InProgress)) {
                    AIGoal.Cancel(stale, ticks, T.Equipment);
                }
                return;
            }

            if (!agent.BB.TryNeedGoal(IdH, out var g)) {
                g = CreateGoal(agent, ticks);
            }

            if (g.State == AIGoalState.InProgress) {
                // never touch goal that is in progress
                return;
            }

            // TODO check if there still is a weapon
            if (g.State != AIGoalState.Available) {
                if (agent.BB.IsCoolingDown(IdH, ticks)) {
                    return;
                }
                if (agent.BeingData.Inventory.HasMissingEquipment) {
                    if (AIGoal.IsFailed(g.State)) {
                        agent.BB.SetCooldown(IdH, ticks,
                            Consts.TicksPer6Hours, Consts.TicksPer12Hours);
                    }
                } else {
                    // we can only find better equipment
                    BackOffUpgradeSearch(agent, ticks);
                }
                g.GoalPriority = 2;
                if (data.Species.IsHumanoid) {
                    if (data.Inventory.IsNaked) {
                        g.GoalPriority = 5;
                    }
                }
                g.ChangeStateTo(AIGoalState.Available, ticks);
                g.CreatedAt = ticks;
                g.ResetWorldState();
            }
        }

        private static void TickGearMood(GameState s, BeingData data) {
            var health = data.Mind.Health;
            if (health == null) { return; }
            // Only humanoids care what they wear, and guests keep to their own business
            bool cares = data.Species.IsHumanoid && !data.IsGuest;
            health.SetPermanentEffect(s, MoodSourceIdH.Naked, IconId.CPreviewHuman01, T.Naked,
                cares && data.Inventory.IsNaked ? -8 : 0);
            WornItemMood.Apply(s, data, cares);
        }

        private AIGoal CreateGoal(AIAgentComp agent, long ticks) {
            var g = AIGoal.CreatePersonal(JobTypeIdH.Need, IconId.CClothing,
                T.VerbSearchingFor_V1.F(T.EquipableItem), agent, ticks)
                .WithWorldToDesired(AIVarsH.HasFoundEquipment, false, true)
                .WithWorldToDesired(AIVarsH.IsItemEquipped, false, true)
                .WriteInitialState();
            // equipment is idle-only — InterruptPriority 1 equals colony goal priority,
            // so this goal will never preempt colony work
            g.WithInterrupt(1, Consts.TicksPer4Hours);
            agent.BB.SetNeedGoal(IdH, g);
            return g;
        }

        /// <summary>
        /// A raid is coming: go get a weapon now instead of after the current task (see
        /// ArmUpNudge for who). Interrupts colony work like a forced healing does; the goal
        /// keeps interrupt priority 1, so a combat goal still preempts it.
        /// </summary>
        public bool ForceArmUp(AIAgentComp agent, long ticks) {
            agent.BB.Cooldowns.Remove(IdH);
            // AIActionFindBestEquipment fills the weapon slot first while this lasts
            agent.BB.SetCooldown(ArmUpIdH, ticks, Consts.TicksPerHour, Consts.TicksPerHour);
            if (!agent.BB.TryNeedGoal(IdH, out var g)) {
                g = CreateGoal(agent, ticks);
            }
            if (g.State == AIGoalState.InProgress) { return true; }
            if (agent.Goal != null) {
                if (agent.Goal.Type == AIGoalType.Combat) { return false; }
                if (!AIGoal.Interrupt(agent.Goal, ticks)) { return false; }
            }
            var plan = new AIPlan();
            plan.Steps = new AIAction[] {
                Registries.AIActions.FastGet(AIActionFindBestEquipment.ActIdH),
                Registries.AIActions.FastGet(AIActionEquipItem.ActIdH),
            };
            g.GoalPriority = 5;
            g.ChangeStateTo(AIGoalState.Available, ticks);
            g.ResetWorldState();
            return agent.SetGoal(g, plan, ticks);
        }

        // The find actions cancel a search that had nothing to fill, so it stays out of the
        // log. Tick only backs off after a failure when a slot is empty, and an unwanted
        // empty slot (Manual, or a weapon the being can't use) would then have the goal
        // retried at once, forever. So the actions back off through here themselves.
        public static void BackOffUpgradeSearch(AIAgentComp agent, long ticks) {
            agent.BB.SetCooldown(NeedIdH.Equipment, ticks,
                Consts.TicksPer18Hours, Consts.TicksPer24Hours);
        }

        public override void InitializeFor(BeingData data) {
        }
        public override void InitializeForNew(BeingData data) {
        }

        // a timed blackboard mark: this being was told to arm up for a raid
        public static readonly int ArmUpIdH = Hashes.S("ArmUp");

        public AINeedEquipment() : base(NeedId.Equipment) {}
    }
}
