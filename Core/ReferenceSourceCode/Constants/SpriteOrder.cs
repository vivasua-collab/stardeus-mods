using KL.Utils;

namespace Game.Constants {
    public static class SpriteOrder {
        public const int Shadow = -22 -5;
        public const int ObjFlat = -23 -5;
        public const int ObjBelowFlat = -24 -5;
        public const int PlantSmall = -23 -5;
        public const int PlantBig = -8;
        public const int Being = -20;
        public const int BeingBody = -20;
        // Just behind/in front of BeingBody, for parts whose draw order flips with facing
        // (e.g. a Tail, or Weapon below). BeingBodyBack doubles as Obj; BeingBodyFront
        // doubles as the old Weapon layer (kept below as a legacy alias).
        public const int BeingBodyBack = -21;
        public const int BeingBodyFront = -18;
        public const int BeingClothing = -19;
        // Legacy alias for old mods; new content should reference BeingBodyFront by name.
        public const int Weapon = BeingBodyFront;
        public const int BeingHair = -16;
        public const int BeingHead = -17;
        public const int BeingFace = -15;
        public const int BeingHat = -14;
        public const int Obj = -21;
        public const int CarriedObj_D = -18;
        public const int CarriedObj_U = -20;
        public const int CarriedObj_L = -20;
        public const int CarriedObj_R = -20;
        public const int HauledObj = -4;
        public const int StatusBar = -3;
        public const int StatusIcon = -2;
        public const int StatusBubble = -1;
        // Things in flight over the ship (copters, shuttles, capsules, meteors). The render queue
        // is 3000 + order, so these sit above the hull view's roofs (+15, +16) and the being
        // markers (+17), which hid them when zoomed out (STA-1761)
        public const int ObjAbove = 20;
        public const int ObjAbove2 = 21;
        public const int AboveBeingHat = -13;
        public const int TurretHead = -11;
        public const int TurretTop = -10;
        public const int InBetweenTile = -29;
        public const int AboveAnyTile = -9;
        public const int BelowObjTile = -31;
        // Geometry-5
        public const int ObjBelow = -45;
        public const int Dirt = -32;
        public const int AboveSolidFloor = -39;

        public static int ForName(string name, object ctx) {
            switch (name) {
                case "Shadow" : return Shadow;
                case "Being" : return Being;
                case "BeingBody" : return BeingBody;
                case "BeingBodyBack" : return BeingBodyBack;
                case "BeingBodyFront" : return BeingBodyFront;
                case "BeingClothing" : return BeingClothing;
                case "BeingHead" : return BeingHead;
                case "BeingHair" : return BeingHair;
                case "BeingHat" : return BeingHat;
                case "Obj" : return Obj;
                case "Weapon" : return Weapon;
                case "InBetweenTile" : return InBetweenTile;
                case "AboveAnyTile" : return AboveAnyTile;
                case "BelowObjTile" : return BelowObjTile;
                case "ObjAbove" : return ObjAbove;
                case "ObjAbove2" : return ObjAbove2;
                case "ObjFlat" : return ObjFlat;
                case "ObjBelow" : return ObjBelow;
                case "ObjBelowFlat" : return ObjBelowFlat;
                case "Dirt" : return Dirt;
                case "AboveBeingHat" : return AboveBeingHat;
                case "PlantSmall": return PlantSmall;
                case "PlantBig": return PlantBig;
                case "BeingFace": return BeingFace;
                case "CarriedObj_D": return CarriedObj_D;
                case "CarriedObj_U": return CarriedObj_U;
                case "CarriedObj_L": return CarriedObj_L;
                case "CarriedObj_R": return CarriedObj_R;
                case "StatusBar": return StatusBar;
                case "StatusIcon": return StatusIcon;
                case "StatusBubble": return StatusBubble;
                case "TurretHead": return TurretHead;
                case "TurretTop": return TurretTop;
                case "AboveSolidFloor": return AboveSolidFloor;
            }
            D.Err("Unknown SpriteOrder by name: {0} ({1})", name, ctx);
            return 0;
        }
    }
}
