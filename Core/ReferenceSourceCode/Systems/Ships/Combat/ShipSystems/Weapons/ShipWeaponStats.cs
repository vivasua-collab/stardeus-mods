namespace Game.Systems.Ships {
    public struct ShipWeaponStats {
        public int Shots;
        public int Hits;
        public int Misses;

        public ShipWeaponStats(int shotsFired, int hits, int misses) {
            Shots = shotsFired;
            Hits = hits;
            Misses = misses;
        }

        public void RecordShot(bool hit) {
            Shots++;
            if (hit) {
                Hits++;
            } else {
                Misses++;
            }
        }

        public float Accuracy => Shots > 0 ? (float) Hits / Shots : 0f;

        public override string ToString() {
            return $"ShotsFired: {Shots}, Hits: {Hits}, Misses: {Misses}, Accuracy: {Accuracy:P2}";
        }
    }
}
