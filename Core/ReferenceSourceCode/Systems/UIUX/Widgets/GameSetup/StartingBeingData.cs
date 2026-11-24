using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Naming;
using Game.Utils;
using KL.Randomness;

namespace Game.Systems.UIUX {
    public class StartingBeingData {
        public BeingData Data;
        public Species Species;
        // Asleep in a stasis array rather than part of the initial crew: shown identity only,
        // and rerolled through StasisManifestSetup
        public bool IsSleeper;
        public Action<StartingBeingData> OnClick;
        public Action RebuildUI;

        public void Randomize(GameSetupData data, bool randomizePets) {
            // bool speciesChanged = false;
            if (randomizePets && Species.IsPet) {
                var newSpecies = Species.RandomPet(Rng.Unseeded);
                if (newSpecies != Species) {
                    var crew = data.StartingBeings;
                    // speciesChanged = true;
                    if (crew.TryGetValue(Species, out var list)) {
                        list.Remove(this);
                    }
                    Species = newSpecies;
                    if (!crew.TryGetValue(Species, out var newList)) {
                        newList = new List<StartingBeingData>();
                        crew[Species] = newList;
                    }
                    newList.Add(this);
                }
            }
            // cleanup old data UI before replacing the data
            // bool needsShowDetails = false;
            // if (Data != null) {
            //     needsShowDetails = Data.UI.DetailsMainUDB?.IsShowing == true;
            //     Data.UI.ListMainUDB.NeedsListRebuild = true;
            //     Data.UI.Cleanup();
            // }
            var vars = new GenBeingVars(Species, BeingType.StartingCrew);
            vars.Body = FactionDef.Player.GenBodyVars;
            Data = BeingUtils.GenBeingWithInventory(vars,
                Rng.Unseeded, GenItemFlags.Casual);
            // rebuildJobs();

            // if (speciesChanged) {
            //     rebuildCrew();
            // }
            // if (needsShowDetails) {
            //     OnClick();
            // }
        }

        public WBase AddListUI(Lifetime life, int idx, WGroup container) {
            var w = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, container.Container, idx);
            w.Load(null, Data.Name, () => OnClick(this));
            w.SetText2(null);

            Data.Avatar.Load(w.Image);
            return w;
        }

        public BeingData DeepCopyFor(GameState s) {
            return BeingDataData.DeepCopy(s, Data);
        }
    }
}
