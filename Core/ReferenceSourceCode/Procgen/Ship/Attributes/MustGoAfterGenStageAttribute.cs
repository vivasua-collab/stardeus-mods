using System;

namespace Game.Procgen {
    [AttributeUsage(AttributeTargets.Class)]
    public class MustGoAfterGenStageAttribute : Attribute {
        public readonly Type[] MustGoAfter;
        public MustGoAfterGenStageAttribute(params Type[] types) {
            MustGoAfter = types;
        }
    }
}
