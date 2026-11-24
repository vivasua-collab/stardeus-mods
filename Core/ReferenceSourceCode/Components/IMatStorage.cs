using System.Collections.Generic;
using Game.Constants;
using Game.Data;

namespace Game.Components {
    public interface IMatStorage : IComponent {
        /// <param name="type">material type</param>
        /// <param name="amount">how much we want to store</param>
        /// <param name="wouldAcceptSome">return true if storage could accept some of the amount</param>
        /// <returns>true if full amount would be accepted</returns>
        bool WillAccept(MatType type, int amount, out bool wouldAcceptSome);
        StorageType StorageType { get; }
        bool IsAddedToInventory { get; set; }
        int Priority { get;}
        ///<returns>Public storage can be used to take/put anything by anyone</returns>
        bool IsPublic { get; }
        bool IsFull { get; }
        float FillPercent { get; }
        bool IsEmpty { get; }
        //string GroupId { get; }
        int[] Groups { get; }
        MatHaulTargetComp HaulTarget { get; }
        int ReservedAmt(MatType type);
        void AppendMatCounts(Dictionary<MatType, int> counts);
        /// <summary>Cancels all reservations for this being</summary>
        bool CancelReservationFor(Being who, object ctx);
        /// <summary>Cancels reservation for specific being and material type</summary>
        bool CancelReservationFor(Being who, MatType type, object ctx);
        /// <summary>Returns true if being has any reservation</summary>
        bool HasReservation(Being who);
        /// <summary>Returns true if being has reservation for specific material type</summary>
        bool HasReservation(Being who, MatType type);
        ///<returns>Amount that was actually reserved</returns>
        int Reserve(MatType type, Being who, int wantedAmount, object ctx);
        ///<returns>Amount that is available after current reservations</returns>
        int Available(MatType type);
        int AvailableWithoutReservation(MatType type);
        bool AcceptsGroup(int GroupId);
        ///<returns>Contents with that group id and StackSize > 0 will be filled into mats</returns>
        bool AvailableForGroup(int groupId, List<Mat> mats);
        bool AvailableForGroup(int groupId);
        ///<returns>Amount that was actually given. Requires reservation</returns>
        int Take(MatType type, Being who, int wantedAmount, object ctx);
        ///<returns>Remainder that didn't fit</returns>
        int Store(MatType type, Being who, int givenAmount, object ctx);
        int InPort { get; }
        int OutPort { get; }
        Dictionary<MatType, int> Contents { get; }
        List<Mat> TakeAll();
        ///<summary>Have to call this when material storage is initialized</summary>
        void AddToInventory();
        ///<summary>Have to call this when material storage is removed</summary>
        void RemoveFromInventory();
        // int WorkSpot { get; }
        bool IsValidTake(int amt) { return amt > 0; }
        ///<summary>A flying worker could not get to this storage. Two distinct
        ///flyers failing flags it to the player; the flag clears on the next Take</summary>
        void MarkAsUnreachableByFlying(Being worker);
        bool IsMarkedUnreachable { get; }

    }
}
