using Game.Components;
using Game.Systems.AI;

namespace Game.Systems.Items {
    public interface ILoadsObj : IComponent {
        int LoadObjSpotIdx { get; }
        uint ReservedObjId { get; }
        bool LoadObj(ObjData objData, AIGoal goal);
    }
}
