using System;

namespace Game.Procgen.Names {
    [Serializable]
    public sealed class NamePartDef {
        public bool NonUnique;
        public string Type;
        public string[] Paths;
        public float[] Chances;
    }
}
