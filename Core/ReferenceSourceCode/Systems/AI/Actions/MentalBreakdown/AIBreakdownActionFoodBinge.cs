using Game.AI;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Mental;
using Game.Systems.Path;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using UnityEngine;

namespace Game.Systems.AI {
    // "Food binge" breakdown. The being eats portion after portion, far past the
    // point of being full. When it is over, everything comes back up.
    //
    // A colonist takes a whole stack of food to a table and eats it there, like a
    // meal that does not stop (AtTable). One that cannot carry (no hands left)
    // gorges straight from the storage instead.
    //
    // This does not go through FindFood / ConsumeFood on purpose. Those are built
    // around a hungry being having one proper meal (one portion, stop when full).
    // A binge takes many portions and goes back for more.
    public sealed partial class AIBreakdownActionFoodBinge : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionFoodBinge());
        }
        public const string ActId = "BreakdownFoodBinge";

        public const int MinPortions = 3;
        public const int MaxPortions = 6;
        public const int ThrewUpMoodEffect = -10;
        private const int EatTicks = Consts.TicksPer10Minutes;
        private const float FillPerPortion = 0.4f;

        protected override int AiVar => AIVarsH.MentalBingedOnFood;

        private AIBreakdownActionFoodBinge() : base(ActId, () => T.BreakdownFoodBinge) {
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.Chew, AbilityIdH.Eat);
        }

        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownFoodBingeDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }

        public static bool EatsAtTable(AIAgentComp agent) {
            return agent.BeingData.HasAbility(AbilityIdH.Carry);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            // Byte1: portions eaten so far, Int1: how many this binge is after
            if (ticks >= agent.BB.Long1 || agent.BB.Byte1 >= agent.BB.Int1) {
                return Finish(S, agent, goal, ticks);
            }
            // Bool2NotSaved: the stack is down and being eaten
            if (agent.BB.Bool2NotSaved || IsCarryingStack(agent) || EatsAtTable(agent)) {
                return ExecuteAtTable(S, agent, goal, ticks);
            }
            return ExecuteAtStorage(S, agent, goal, ticks);
        }

        private AIActionResult ExecuteAtStorage(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            // MoveProgress is not saved, so after a load the being lets go of
            // what it had reserved and starts looking again
            if (agent.BB.MoveProgress == null) {
                ReleaseTarget(agent, goal);
                if (!TryHeadForFood(S, agent, goal, 1)) {
                    // The food stock is gone, or out of reach
                    return Finish(S, agent, goal, ticks);
                }
                return InProgress;
            }

            var storage = StorageOf(agent.BB.TargetEntity);
            if (!IsNextTo(agent, storage)) {
                LeaveStorage(agent, goal, storage);
                return InProgress;
            }

            var outPort = EntityUtils.CenterOf(storage.OutPort);
            if (!EatAnimation(agent, outPort, ticks)) {
                return InProgress;
            }

            var type = agent.BB.TargetMatType;
            if (storage.Take(type, agent.Being, 1, goal) > 0) {
                AtePortion(S, agent, type);
            } else {
                agent.BB.AddToBlacklist(storage.EntityId);
            }
            ReleaseTarget(agent, goal);
            return InProgress;
        }

        // Plays the eating progress bar for one portion. True once the portion is eaten.
        private bool EatAnimation(AIAgentComp agent, Vector2 food, long ticks) {
            // Bool1NotSaved: is eating, Long1NotSaved: since when
            if (!agent.BB.Bool1NotSaved) {
                agent.BB.Bool1NotSaved = true;
                agent.BB.Long1NotSaved = ticks;
            }
            var eaten = ticks - agent.BB.Long1NotSaved;
            if (eaten < EatTicks) {
                FacePosition(agent, food);
                agent.ShowInfoBar(Consts.ColorFuelBar, eaten / (float) EatTicks, T.Eating);
                agent.Being.Graphics.Punch(food - agent.Position, 0.05f, 0.3f, 0.3f);
                return false;
            }
            agent.BB.Bool1NotSaved = false;
            agent.HideInfoBar();
            return true;
        }

        private static void AtePortion(GameState S, AIAgentComp agent, MatType type) {
            agent.BB.Byte1++;
            S.Sig.MatConsumed.Send(type, 1);
            // The results are ignored on purpose: a being that is already full
            // keeps eating anyway, that is the whole point
            var stats = agent.BeingData.StatsBeing;
            stats.Hunger.TryIncrease(FillPerPortion);
            stats.StomachContents.TryIncrease(FillPerPortion);
        }

        private AIActionResult Finish(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            int portions = agent.BB.Byte1;
            StandUp(agent);
            if (portions > 0) {
                agent.BeingData.Body.AddExcrementsOnFloor();
                // Bool1: threw up. The mood effect is added in OnDeactivate.
                agent.BB.Bool1 = true;
                var line = Texts.Yellow(T.BreakdownFoodBingeHit_V2.F(
                    Texts.LinkToBeing(agent.BeingData), portions));
                S.Sig.ShowFanfare.Send(line);
                S.Sys.Log.AddLine(agent.Being, line);
                // only once, even if this gets called again
                agent.BB.Byte1 = 0;
            }
            return EndBreakdown(agent, goal);
        }

        // Finds the closest food storage, reserves up to `amount` portions there and
        // starts walking. Storages that turn out to be useless are blacklisted along the way.
        private static bool TryHeadForFood(GameState S, AIAgentComp agent, AIGoal goal,
                int amount) {
            int attempts = 5;
            while (attempts-- > 0) {
                var storage = FindStorage(S, agent);
                if (storage == null) { return false; }
                var food = Caches.MatList;
                food.Clear();
                if (storage.AvailableForGroup(MatGroupIdH.Food, food) && food.Count > 0) {
                    var mat = Rng.UFrom(food);
                    var type = mat.Type;
                    var wanted = amount < mat.StackSize ? amount : mat.StackSize;
                    if (wanted < 1) { wanted = 1; }
                    var reserved = storage.Reserve(type, agent.Being, wanted, goal);
                    if (reserved > 0) {
                        agent.BB.TargetEntity = storage.Entity;
                        agent.BB.TargetMatType = type;
                        // Int1NotSaved: how much was reserved, Take has to match it
                        agent.BB.Int1NotSaved = reserved;
                        if (agent.Motion.TryGoTo(storage.OutPort,
                                PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby
                                | PathReqFlags.TempDestination,
                                out agent.BB.MoveProgress)) {
                            return true;
                        }
                        ReleaseTarget(agent, goal);
                    }
                }
                agent.BB.AddToBlacklist(storage.EntityId);
            }
            return false;
        }

        private static IMatStorage FindStorage(GameState S, AIAgentComp agent) {
            return S.Sys.Inventory.FindClosestReachableStorageByGroup(
                MatGroupIdH.Food, agent.Being, agent.BB.Blacklist, IsBingeable, true, false);
        }

        // A half eaten meal is one item posing as an object: it can be neither split nor
        // eaten portion by portion
        private static bool IsBingeable(IMatStorage storage) {
            return storage is not UnstoredMatComp pile || !pile.TreatAsObj;
        }

        private static IMatStorage StorageOf(Entity e) {
            if (e is Tile t) {
                // fridge or storage device
                return t.MaterialStorage;
            }
            if (e is Obj o) {
                // pile of food items
                return o.Pile;
            }
            return null;
        }

        private static bool IsNextTo(AIAgentComp agent, IMatStorage storage) {
            return storage != null && storage.Entity.IsActive
                && Pos.SqrDistance(agent.PosIdx, storage.OutPort) <= 2.5f;
        }

        // Storage was removed, or we could not get close enough to it
        private static void LeaveStorage(AIAgentComp agent, AIGoal goal, IMatStorage storage) {
            if (storage != null) {
                agent.Motion.StoreNavFailureAt(storage.OutPort);
                agent.BB.AddToBlacklist(storage.EntityId);
            }
            ReleaseTarget(agent, goal);
        }

        // Lets go of the current storage, including the portions reserved there
        private static void ReleaseTarget(AIAgentComp agent, AIGoal goal) {
            var storage = StorageOf(agent.BB.TargetEntity);
            if (storage != null && storage.HasReservation(agent.Being)) {
                storage.CancelReservationFor(agent.Being, goal);
            }
            if (agent.BB.Bool1NotSaved) {
                agent.BB.Bool1NotSaved = false;
                agent.HideInfoBar();
            }
            agent.BB.TargetEntity = null;
            agent.BB.TargetMatType = null;
            agent.BB.Int1NotSaved = 0;
            agent.BB.ClearMoveTarget();
        }

        // Selection-time check: only assign the breakdown when there is a food
        // storage the being can safely get to right now.
        public override bool CanBeAssignedTo(AIAgentComp agent) {
            return FindStorage(agent.S, agent) != null;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.Being.Graphics.DropIfCarrying();
            agent.BB.ClearBlacklist();
            agent.BB.TargetEntity = null;
            agent.BB.TargetMatType = null;
            agent.BB.ClearMoveTarget();
            agent.BB.Bool1 = false;
            agent.BB.Bool1NotSaved = false;
            agent.BB.Bool2NotSaved = false;
            agent.BB.Long1NotSaved = 0;
            agent.BB.Byte1 = 0;
            agent.BB.Int1 = Rng.URange(MinPortions, MaxPortions + 1);
            agent.BB.Long1 = ticks + Consts.TicksPer4Hours;
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            var threwUp = agent.BB.Bool1;
            PutBackLeftovers(agent);
            ReleaseTarget(agent, goal);
            StandUp(agent);
            agent.BB.UnreserveSlotSide(false);
            agent.BB.Bool2NotSaved = false;
            agent.HideInfoBar();
            agent.BB.ClearBlacklist();
            agent.BB.Bool1 = false;
            agent.BB.Long1NotSaved = 0;
            agent.BB.Byte1 = 0;
            agent.BB.Int1 = 0;
            agent.BB.Long1 = 0;
            base.OnDeactivate(agent, goal, ticks);
            // This has to come after the base call: ending a breakdown expires every
            // negative mood effect the being has (MentalHealth.AfterBreakdown), and the
            // regret is supposed to outlast the binge.
            if (threwUp) {
                agent.AddMentalEffect(MentalEffect.Create(agent.PosIdx, ticks,
                    Consts.TicksPer4Hours, T.BreakdownFoodBingeMood, ThrewUpMoodEffect));
            }
        }
    }
}
