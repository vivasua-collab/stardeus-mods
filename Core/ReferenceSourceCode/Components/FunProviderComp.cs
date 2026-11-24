using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.CodeGen;
using KL.Utils;
using System;
using Game.Systems.Drama;
using Game.Systems.AI;
using Game.Systems.Mental;

namespace Game.Components {
    public abstract class FunProviderComp<T> : BaseComponent<T>,
            IFunProvider, IDefPowerProvider where T : IComponent, new() {
        private const float DefaultEnergyCost = 1f;
        public ElectricNodeComp ENode => elNode;
        public Tile FunTile => Tile;
        protected float energyCost;
        protected int userCapacity;
        public int UseCount;
        public Action<FunProviderComp<T>, List<Being>> OnUsersChanged;
        protected int income;
        public int WorkSpot => Tile.Transform.WorkSpot;
        protected readonly List<Being> users = new();
        private Being[] slots;
        public int CurrentUserCount => users.Count;
        // A party gathered at the device keeps it on with nobody using it, so its music plays
        // (PartyStoryEvent renews it every tick, nothing is saved). Internal: not mod API.
        internal long KeepOnUntilTick;
        public abstract void Reward(Being being, long ticks, int ticksUsing);
        public bool CanKeepUsing => Tile.ENode.IsPowerable;
        public bool CanAddUser => Tile.ENode.IsPowerable &&
            users.Count < userCapacity;

        public abstract bool CanUse(Being user);

        public abstract string ActIcon { get; }
        public abstract string ActName { get; }
        protected string chatTopic;
        protected int socialOpDuration;

        protected abstract void PositionUser(Being worker, int num);

        public abstract bool ShouldContinueUsing(AIAgentComp agent, AIGoal goal,
            long ticks, float funValue);

        public virtual int MinSessionTicks => 0;
        public virtual int MaxSessionTicks => Consts.TicksPer3Hours;

        // Mood bonus scaled by how fresh this device still is for the user
        protected void AddRewardMood(Being being, long ticks, int ticksUsing, string icon,
                string reason, int baseEffect) {
            var time = Math.Max(ticksUsing / 2, Consts.TicksPerHour);
            var novelty = FunNovelty.Of(being.Data, FunSubjects.ForDevice(Tile), ticks);
            if (!FunNovelty.TryScaleMood(baseEffect, time, novelty,
                    out var effect, out var duration)) {
                return;
            }
            being.AIAgent.AddMentalEffect(MentalEffect.CreateWithIcon(
                WorkSpot, icon, ticks, duration, reason, effect));
        }

        public bool TryAddUser(Being worker) {
            if (!users.Contains(worker)) {
                int slot = Array.IndexOf(slots, null);
                if (slot < 0) {
                    return false;
                }
                if (users.Count > 0) {
                    CreateSocialInteraction(worker, users);
                }
                slots[slot] = worker;
                users.Add(worker);
                PositionUser(worker, slot);
                UseCount++;
                OnUsersChanged?.Invoke(this, users);
                S.Wallet.Add(income);
                return true;
            }
            D.Err("Trying to add user more than once on a fun provider! {0} / {1}",
                Tile, worker);
            return true;
        }

        protected SocialInteraction curInteraction;
        protected abstract void CreateSocialInteraction(Being worker, List<Being> users);
        public SocialChatContext ChatContext { get; protected set; }

        public void RemoveUser(Being worker) {
            if (curInteraction != null) {
                S.Query.RegisterSocialInteraction.Ask(curInteraction);
                curInteraction = null;
            }
            RemoveUserFromSlot(worker);
            worker.Graphics.SetOffset(Consts.V2Zero);
            worker.Graphics.SetRotation(0, true);
        }

        private void RemoveUserFromSlot(Being worker) {
            int slot = Array.IndexOf(slots, worker);
            if (slot >= 0) {
                slots[slot] = null;
            }
            if (users.Remove(worker)) {
                OnUsersChanged?.Invoke(this, users);
            } else {
                D.Err("Failed to remove a worker from the fun provider: {0} -> {1}",
                    Tile, worker);
            }
        }

        protected abstract bool OnUse(Being worker);

        public bool Use(Being worker) {
            if (!Entity.IsActive || !CanKeepUsing) {
                return false;
            }
            return OnUse(worker);
        }

        protected virtual void ConfigureProvider() {}

        public void CreateSocialOpportunity(AIAgentComp agent) {
            if (chatTopic != null) {
                agent.BB.AddOpportunity(S, SocialOpportunity.CreateFor(
                        agent, this, SocialOpportunityIdH.FunDevice, socialOpDuration));
            }
        }

        protected override void OnConfig() {
            ConfigureProvider();
            chatTopic = Config.GetString(PropertyIdH.SocialChatContext, null);
            if (chatTopic != null) {
                socialOpDuration = (int) (Config.GetFloat(
                    PropertyIdH.SocialOpportunityExpectedDurationHours, 0f)
                        * Consts.TicksPerHour);
                ChatContext = SocialChatContext.Create(chatTopic,
                    faceBuddy: false, socialOpDuration);
            } else {
                ChatContext = SocialChatContext.NoChat;
            }
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            userCapacity = Config.GetInt(PropertyIdH.MaxCapacity, 2);
            income = Config.GetInt(PropertyIdH.Income, 10);
            slots = new Being[userCapacity];
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override string ToString() {
            return " * Fun Provider";
        }

        private ElectricNodeComp elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    this.elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                    AfterTickGrid();
                }
            }
        }

        protected virtual void OnAfterTickGrid() {}

        private void AfterTickGrid() {
            OnAfterTickGrid();
            if (users.Count != 0) {
                elNode.SetConsumerWantedInput(energyCost);
                ValidateUsers();
            } else if (KeepOnUntilTick > G.Ticks) {
                elNode.SetConsumerWantedInput(energyCost);
            } else {
                elNode.SetConsumerWantedInput(0f);
            }
        }

        private void ValidateUsers() {
            for (int i = users.Count - 1; i >= 0; i--) {
                Being u = users[i];
                if (u.IsDead) {
                    D.Err("Dead fun user: {0}. {1}", u, Tile);
                    RemoveUserFromSlot(u);
                    continue;
                }
                if (Vectors.SqrDistance(u.Position, Tile.Position) > 9) {
                    D.Err("Fun user too far away: {0}. {1}", u, Tile);
                    RemoveUserFromSlot(u);
                }
            }
        }
    }
}
