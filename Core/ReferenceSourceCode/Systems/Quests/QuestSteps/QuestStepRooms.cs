using Game.Data;

namespace Game.Systems.Quests {
    // Steps that ask for something to be "in the same room" as a device count only an enclosed
    // room, the same test EncloseEntity uses, so a half-built enclosure cannot satisfy them early
    // and a room the player sees listed as enclosed cannot hold them back. Corner gaps are
    // CloseCornerGaps' business, not theirs.
    public static class QuestStepRooms {
        public static int EnclosedAreaOf(GameState s, Entity e) {
            var areaId = s.Sys.Areas.At(e.PosIdx);
            return areaId > 0 ? areaId : 0;
        }
    }
}
