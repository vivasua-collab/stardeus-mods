using Game.Components;
using Game.Constants;

namespace Game.Data {
    public sealed class MatReservation {
        public object Ctx;
        public IMatStorage Storage;
        public Being Being;
        public MatType Type;
        public int Amount;
        public long Since;
        public override string ToString() {
            return $"MatRes[W:{Being.Id}][{Amount}@{Type.NameT}][S:{Storage}|{Storage.Entity}][{Ctx}]";
        }
    }
}
