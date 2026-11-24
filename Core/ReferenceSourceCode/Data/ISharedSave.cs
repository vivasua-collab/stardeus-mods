using System;

namespace Game.Data {
    public interface ISharedSave {
        public SharedSaveInfo Info { get; set; }
    }
}
