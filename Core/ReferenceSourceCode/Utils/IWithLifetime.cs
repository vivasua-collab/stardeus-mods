using System;

namespace Game.Utils {
    public interface IWithLifetime : IDisposable {
        bool IsDisposed { get; }
    }

}
