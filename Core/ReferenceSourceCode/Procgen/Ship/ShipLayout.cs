using Game.Systems.Ships;
using KL.Randomness;

namespace Game.Procgen {
    public enum HullVariant {
        Bollinger,
        Modular,
        Organic,
    }

    public enum RoomVariant {
        Spine,
        Growth,
    }

    // What a random ship is built from: hull generator, how far its two halves may differ,
    // room layout and how mirrored the rooms are. Rolled once per ship from the factory's rng
    // before any stage is added, so the new-game preview and the real generation get the
    // same layout for a seed.
    public sealed class ShipLayout {
        private const float BollingerShare = 0.5f;
        private const float ModularShare = 0.25f;
        private const float AsymmetricChance = 0.25f;
        private const float MinHullSymmetry = 0.65f;
        private const float MaxHullSymmetry = 0.95f;
        private const float SpineChance = 0.66f;
        private const float MinRoomSymmetry = 0.25f;
        private const float MaxRoomSymmetry = 0.85f;
        // the metaball hull only reads as organic with finer cells than the scenario's
        private const float OrganicCellScale = 0.6f;

        public HullVariant Hull;
        // Share of hull cells with a mirror twin to aim for; 1 = mirrored hull
        public float HullSymmetry = 1f;
        public RoomVariant Rooms;
        // Share of room walls with a mirror twin to aim for
        public float RoomSymmetry;
        public float CellScale = 1f;
        // Leaving Earth hands the player an empty hull
        public bool HasRooms = true;
        // Set by GenShip.AddRoomStages, so the new-game preview grows rooms like the real stage
        public int CenterAreaSize;
        public float RoomSizeMul = 1f;
        // Rooms RoomPartitioner reserves for the stasis arrays and the shuttle
        public int StasisRooms;
        public int ShuttleRooms;

        public bool IsMirrored => HullSymmetry >= 1f;

        public static ShipLayout Roll(Rng rng, bool isStation, bool hasRooms = true) {
            var layout = new ShipLayout { HasRooms = hasRooms };
            // every roll is drawn even when unused, so later draws do not shift around
            var hull = rng.Value;
            var asymmetric = rng.Chance(AsymmetricChance);
            var hullSymmetry = rng.Range(MinHullSymmetry, MaxHullSymmetry);
            var spine = rng.Chance(SpineChance);
            layout.RoomSymmetry = rng.Range(MinRoomSymmetry, MaxRoomSymmetry);
            if (isStation || hull < BollingerShare) {
                layout.Hull = HullVariant.Bollinger;
            } else if (hull < BollingerShare + ModularShare) {
                layout.Hull = HullVariant.Modular;
            } else {
                layout.Hull = HullVariant.Organic;
            }
            if (asymmetric && !isStation) {
                layout.HullSymmetry = hullSymmetry;
            }
            layout.Rooms = spine ? RoomVariant.Spine : RoomVariant.Growth;
            layout.CellScale = layout.Hull == HullVariant.Organic ? OrganicCellScale : 1f;
            ShipGenExperiments.ApplyTo(layout);
            return layout;
        }

        public IShipGenStage CreateHullStage(ShipShapeConfig shape, NPCShip ship) {
            switch (Hull) {
                case HullVariant.Modular:
                    return new ModularShapeGenStage(HullSymmetry, CellScale);
                case HullVariant.Organic:
                    return new OrganicShapeGenStage(HullSymmetry, CellScale);
                default:
                    return new BollingerShapeGenStage(shape, ship, HullSymmetry);
            }
        }

        public override string ToString() {
            return $"ShipLayout[{Hull} hullSym {HullSymmetry:0.00} cells x{CellScale:0.##} | {Rooms} roomSym {RoomSymmetry:0.00}]";
        }
    }
}
