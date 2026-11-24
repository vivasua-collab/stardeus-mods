using Game.Data;
using Game.Systems.AI;
using Game.Systems.Drama;

namespace Game.Components {
    public interface IFunProvider : IComponent {
        int WorkSpot { get; }
        bool CanKeepUsing { get; }
        bool CanAddUser { get; }
        Tile FunTile { get; }
        string ActIcon { get; }
        string ActName { get; }
        SocialChatContext ChatContext { get; }
        void CreateSocialOpportunity(AIAgentComp agent);
        bool TryAddUser(Being user);
        void RemoveUser(Being user);
        bool CanUse(Being user);
        /// <returns>false if user failed to use the device</returns>
        bool Use(Being user);
        bool ShouldContinueUsing(AIAgentComp agent, AIGoal goal, long ticks,
            float funValue);
        void Reward(Being user, long ticks, int ticksUsing);
        // Session length limits at full novelty
        int MinSessionTicks { get; }
        int MaxSessionTicks { get; }
    }
}
