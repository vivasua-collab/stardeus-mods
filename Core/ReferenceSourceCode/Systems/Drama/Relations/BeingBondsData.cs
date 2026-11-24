using System;
using System.Collections.Generic;
using Game.Data;
using Game.Systems.Mental;
using KL.Utils;
using MessagePack;
using MonoMod.Utils;

namespace Game.Systems.Drama {
    [MessagePackObject]
    public sealed class BeingBondsData {
        [Key("Owner")] public uint OwnerMindId;
        [Key("Ops")] public Dictionary<uint, BeingOpinion> Opinions;
        [Key("LastSocialInteractions")] public long[] LastSocInteractions;
        [Key("LastSocialInteractionsIdx")] public int LastSocInteractionsIdx;

        public static BeingBondsData Serialize(BeingBonds bonds) {
            if (bonds == null) { return null; }
            var d = new BeingBondsData();
            d.OwnerMindId = bonds.Owner.Id;
            d.Opinions = bonds.Opinions;
            d.LastSocInteractions = bonds.LastSocialInteractionTimes.ToArray();
            d.LastSocInteractionsIdx = bonds.LastSocialInteractionTimes.Index;
            return d;
        }

        public static void TryDeserialize(Dictionary<uint, Mind> minds,
                BeingBondsData data) {
            if (!minds.TryGetValue(data.OwnerMindId, out var mind)) {
                D.Err("Failed to find mind for bond with id: {0}", data.OwnerMindId);
                return;
            }

            if (!Arrays.IsEmpty(mind.Bonds.Opinions)) {
                D.Err("Mind has opinions before deserializing! {0}", mind);
                mind.Bonds.Opinions.Clear();
            }

            if (data == null || Arrays.IsEmpty(data.Opinions)) { return; }

            mind.Bonds.Opinions.AddRange(data.Opinions);
            // fix opinions
            foreach (var op in mind.Bonds.Opinions) {
                op.Value.Validate();
            }
            if (data.LastSocInteractions != null) {
                mind.Bonds.LastSocialInteractionTimes = RingBuffer<long>.Load(
                    data.LastSocInteractions, data.LastSocInteractionsIdx, 0L);
            }
        }

        public static List<BeingBondsData> Serialize(Dictionary<uint, Mind> minds) {
            var data = new List<BeingBondsData>();
            foreach (var mind in minds.Values) {
                var bd = Serialize(mind.Bonds);
                if (bd == null) { continue; }
                data.Add(bd);
            }
            return data;
        }
    }
}
