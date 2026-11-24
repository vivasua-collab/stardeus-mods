using System;

namespace Game.Procgen {
    [AttributeUsage(AttributeTargets.Class)]
    public class MustGoBeforeGenStageAttribute : Attribute {
        public readonly Type[] MustGoBefore;
        public MustGoBeforeGenStageAttribute(params Type[] types) {
            MustGoBefore = types;
        }
    }
}
