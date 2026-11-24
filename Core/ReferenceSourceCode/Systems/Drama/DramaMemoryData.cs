using System.Collections.Generic;
using Game.Data;
using KL.Collections;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Drama {
    [MessagePackObject]
    public sealed class DramaMemoryData {
        /// <summary>
        /// Mind ID of the being who wants a birthday or wedding party
        /// </summary>
        [Key("WantsParty")] public uint[] WantsParty;
        [Key("PartyTitles")] public List<string> PartyTitles;

        public static DramaMemory Deserialize(GameState s, DramaMemoryData d) {
            var mem = new DramaMemory();
            if (d != null) {
                mem.PartyTitles = d.PartyTitles;
                if (d.WantsParty != null) {
                    foreach (var mindId in d.WantsParty) {
                        var mind = s.Query.FindMind.Ask(mindId);
                        if (mind != null) {
                            mem.WantsParty.Add(mind);
                        } else {
                            D.Err("Mind not found by id: {0}", mindId);
                        }
                    }
                }
            }
            return mem;
        }

        public static DramaMemoryData Serialize(DramaMemory d) {
            if (d == null) {
                return null;
            }
            var data = new DramaMemoryData();
            data.PartyTitles = d.PartyTitles;
            if (d.WantsParty.Length > 0)  {
                data.WantsParty = new uint[d.WantsParty.Length];
                for (int i = 0; i < d.WantsParty.Length; i++) {
                    data.WantsParty[i] = d.WantsParty[i].Id;
                }
            }
            return data;
        }

    }
}
