using System;

namespace Game.Data {
    // Basically MonoBehaviour.Update();
    // FIXME: [Obsolete("Remove this shit")]
    public interface ITickProgressListener {
        void TickProgress(float tickProgress);
    }
}
