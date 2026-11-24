using System;
using System.Collections.Generic;
using Game.Systems.Mental;
using KL.Collections;
using KL.Utils;

namespace Game.Systems.Drama {
    public sealed class DramaMemory {
        /// <summary>
        /// Mind ID of the being who wants a birthday or wedding party
        /// </summary>
        public FrugalArray<Mind> WantsParty;
        public List<string> PartyTitles;

        public void RequestParty(string title, Mind being) {
            D.Warn("Wants party: {0} -> {1}", being, title);
            PartyTitles ??= new();
            PartyTitles.AddUniqueInefficient(title);
            WantsParty.Add(being);
        }

        public void ResetParty() {
            WantsParty = default;
            PartyTitles.Clear();
        }
    }
}
