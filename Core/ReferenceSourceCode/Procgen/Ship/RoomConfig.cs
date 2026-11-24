using System;

namespace Game.Procgen {
    [Serializable]
    public class RoomConfig {
        public string Type;
        // Area size 
        public int SizeMin;
        public int SizeMax;
        // Number of rooms of this type that we want to generate
        public int CountMin;
        public int CountMax;
        // How much of the room should we fill with stuff
        public float FillMin;
        public float FillMax;
        // Chance 0 to 1 of adding any rooms of this type at all
        public float Chance;
        public string[] RequiredObjects;
        // Definition ids of compatible objects
        public string[] CompatibleObjects;
        // Definition ids of compatible templates
        public string[] CompatibleTemplates;
    }
}
