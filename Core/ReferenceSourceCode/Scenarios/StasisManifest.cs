namespace Game.Scenarios {
    // Who a scenario puts to sleep in one of its stasis arrays at the start. A class, not a
    // struct, because it is also the key that ties the sleepers rolled on the Custom Start
    // screen to the gen stage that builds that array: a scenario with two arrays hands each
    // stage its own instance, and reference identity picks the right people.
    public sealed class StasisManifest {
        public readonly int MinHumans;
        public readonly int MaxHumans;
        public readonly int MinPets;
        public readonly int MaxPets;

        public StasisManifest(int minHumans, int maxHumans, int minPets, int maxPets) {
            MinHumans = minHumans;
            MaxHumans = maxHumans;
            MinPets = minPets;
            MaxPets = maxPets;
        }

        public override string ToString() {
            return $"StasisManifest[humans {MinHumans}-{MaxHumans}, pets {MinPets}-{MaxPets}]";
        }
    }
}
