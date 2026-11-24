using Game.Utils;
using System;
using Game.CodeGen;
using Game.Constants;
using Game.Systems.BodyParts;
using KL.Utils;

namespace Game.Systems.Creatures {
    [Serializable]
    public class SpeciesLooksEyeOverrideDef {
        public string Id;
        public BeingState BeingState;
        public BodyState BodyState;
        public BodyPartStateFlags BodyPartState;
        [JsonEnum(typeof(Game.Systems.Creatures.BeingState))]
        public string[] BeingStateFlags;
        [JsonEnum(typeof(Game.Systems.BodyParts.BodyState))]
        public string[] BodyStateFlags;
        public string[] BodyPartFlags;
        [JsonEnum(typeof(Game.Systems.BodyParts.BodyPartStateFlags))]
        public string[] BodyPartStateFlags;
        public string PartSpecies;
        public string[] ForConditions;
        public int[] ForConditionIds;
        public bool ForRobotic;
        public bool Removed;
        public bool HasLR;
        public bool CanBeClosed;
        public string SpriteState;
        public string SpriteId;
        public bool IgnoreColor;

        public void Configure(object ctx) {
            var context = $"{ctx}.EyeOverride: {Id}";
            BeingState = Enums.FlagsFromStringArray_Byte<BeingState>(BeingStateFlags, context);
            BodyState = Enums.FlagsFromStringArray_Byte<BodyState>(BodyStateFlags, context);
            BodyPartState = Enums.FlagsFromStringArray_Byte<BodyPartStateFlags>(BodyPartStateFlags, context);
            if (ForConditions != null) {
                ForConditionIds = new int[ForConditions.Length];
                for (int i = 0; i < ForConditions.Length; i++) {
                    string c = ForConditions[i];
                    ForConditionIds[i] = Hashes.S(c);
                }
            }
        }

        public bool Matches(BeingData data, LookPartTypeDef type) {
            var eyeSlots = data.Body?.EyeSlots;
            if (eyeSlots == null) {
                D.Err("No eye slots when trying to score overrides for {0} | LookPartType: {1}",
                    data, type);
                return false;
            }
            var wantedSlotIdH = 0;
            // swapped because we're looking at things not through a mirror
            // right eye becomes the left one
            if (type == LookPartTypeDef.Eye_L) {
                wantedSlotIdH = BodyPartSlotIdH.Eye_R;
            } else if (type == LookPartTypeDef.Eye_R) {
                wantedSlotIdH = BodyPartSlotIdH.Eye_L;
            } else {
                D.Err("Unhandled LookPartType for eye overrides: {0}! Only Eye_L / Eye_R look part types are supported without modding!", type);
                return false;
            }
            BodyPartSlot eyeSlot = null;
            for (int i = eyeSlots.Length - 1; i >= 0; i--) {
                var slot = eyeSlots[i];
                if (slot.IdH == wantedSlotIdH) {
                    eyeSlot = slot;
                    break;
                }
            }
            if (eyeSlot == null) {
                D.Err("Eye slot not found for type: {0}| {1}",
                    type, BodyPartSlotIdH.ResolveName(wantedSlotIdH));
                return false;
            }
            if (Removed && eyeSlot.IsPartRemoved) {
                return true;
            }
            if (eyeSlot.InstalledDef == null) {
                D.Err("Eye slot installed def empty found for type: {0}| {1}",
                    type, BodyPartSlotIdH.ResolveName(wantedSlotIdH));
                return false;
            }
            if (BeingState != BeingState.Uninitialized
                    && (BeingState & data.StateFlags) == BeingState) {
                return true;
            }
            if (BodyState != BodyState.None
                    && (BodyState & data.Body.StateB.Value) == BodyState) {
                return true;
            }
            if (ForRobotic && !eyeSlot.InstalledDef.IsBiological) {
                return true;
            }
            if (BodyPartState != BodyParts.BodyPartStateFlags.None
                    && (BodyPartState & eyeSlot.State.Flags) == BodyPartState) {
                return true;
            }
            if (ForConditionIds != null) {
                for (int i = ForConditionIds.Length - 1; i >= 0; i--) {
                    var cId = ForConditionIds[i];
                    if (data.HasCondition(cId)) {
                        return true;
                    }
                }
            }
            if (PartSpecies != null &&
                    Arrays.ContainsSafe(eyeSlot.InstalledDef.TargetSpecies, PartSpecies)) {
                return true;
            }
            return false;
        }
    }
}
