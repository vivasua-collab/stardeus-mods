using System;
using System.Collections.Generic;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using KL.Collections;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Drama {
    [MessagePackObject]
    public struct SocialInteractionData {
        [Key("InvolvedMindIds")] public uint[] InvolvedMindIds;
        [Key("AttitudeChange")] public float AttitudeChange;
        [Key("FriendshipChange")] public float FriendshipChange;
        [Key("LoveChange")] public float LoveChange;
        [Key("OwnershipChange")] public float OwnershipChange;
        [Key("Icon")] public string Icon;
        [Key("Title")] public string Title;
        [Key("Desc")] public string Desc;
        [Key("CreatedAt")] public long CreatedAt;

        private static SocialInteractionData Serialize(SocialInteraction item) {
            var data = new SocialInteractionData();
            data.InvolvedMindIds = new uint[item.InvolvedMinds.Length];
            for (int i = item.InvolvedMinds.Length - 1; i >= 0; i--) {
                data.InvolvedMindIds[i] = item.InvolvedMinds[i].Id;
            }
            data.AttitudeChange = item.AttitudeChange;
            data.FriendshipChange = item.FriendshipChange;
            data.LoveChange = item.LoveChange;
            data.OwnershipChange = item.OwnershipChange;
            data.Icon = item.Icon;
            data.Title = item.Title;
            data.Desc = item.Desc;
            data.CreatedAt = item.CreatedAt;
            return data;
        }

        private static bool TryDeserialize(GameState s,
                SocialInteractionData data, out SocialInteraction item) {
            var minds = new FrugalArray<Mind>();
            foreach (var mId in data.InvolvedMindIds) {
                var mind = s.Query.FindMind.Ask(mId);
                if (mind == null) {
                    D.Err("Cannot find mind by id {0} when deserializing social interaction: {1}",
                        mId, D.DumpInstance(data, forLog: true));
                } else {
                    minds.Add(mind);
                }
            }
            if (minds.Length < 2) {
                D.Err("Not enough minds when deserializing social interaction: {0}: {1}",
                    minds.Length, D.DumpInstance(data, forLog: true));
                item = null;
                return false;
            }
            item = new SocialInteraction();
            item.InvolvedMinds = minds;
            item.AttitudeChange = data.AttitudeChange;
            item.FriendshipChange = data.FriendshipChange;
            item.LoveChange = data.LoveChange;
            item.OwnershipChange = data.OwnershipChange;
            item.Icon = data.Icon;
            item.Title = data.Title;
            item.Desc = data.Desc;
            item.CreatedAt = data.CreatedAt;
            return true;
        }

        public static void Deserialize(GameState s,
                SocialInteractionData[] data, List<SocialInteraction> res) {
            if (res.Count > 0) {
                D.Err("Already have social interactions (x{0}) when deserializing!",
                    res.Count);
            }
            if (data == null) { return; }
            foreach (var d in data) {
                if (TryDeserialize(s, d, out var item)) {
                    res.Add(item);
                }
            }
        }

        public static SocialInteractionData[] Serialize(
                List<SocialInteraction> interactions) {
            if (interactions.Count == 0) { return null; }
            var res = new SocialInteractionData[interactions.Count];
            for (int i = interactions.Count - 1; i >= 0; i--) {
                var item = interactions[i];
                res[i] = Serialize(item);
            }
            return res;
        }
    }
}
