using System;
using Game.Components;
using Game.Constants;
using KL.Utils;
using MessagePack;

namespace Game.Data {
    [Serializable]
    [MessagePackObject]
    public class MatRequest {
        [Key("MatId")] public string TypeId;
        private MatType type;
        [IgnoreMember] public MatType Type {
            get => type ??= MatType.Get(TypeId);
            set {
                if (value == null) {
                    #if !PRODUCTION_BUILD
                    D.Err("Setting MatRequest value to null!");
                    #endif
                    TypeId = null;
                } else {
                    TypeId = value.Id;
                }
                type = value;
            }
        }

        [IgnoreMember] public IMatRequester Requester;

        [Key("Amount")] public int Amount;
        // Even if we can't find enough, we will still try to bring as much
        // as we can
        [Key("IsOptional")] public bool IsAmountOptional;
        [Key("IsCancelled")] public bool IsCancelled;

        public override string ToString() {
            return $"RawMatReq: {Amount} of {Type}, AmtOptional: {IsAmountOptional}, Cncld: {IsCancelled}";
        }
    }
}
