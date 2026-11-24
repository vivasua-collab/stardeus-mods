using System.Runtime.CompilerServices;
using Game.Constants;
using Game.Data;
using KL.Objects;
using KL.Utils;

namespace Game.Visuals {
    public struct TileChange {
        public static TileChange None = new() {
            Layer = -1, PosIdx = -1
        };
        public int Layer;
        public int PosIdx;
        public Tile Current;
        public Tile Prev;

        public override bool Equals(object obj) {
            if (!(obj is TileChange)) { return false; }
            var other = (TileChange) obj;
            return other.Layer == Layer && other.PosIdx == PosIdx &&
                SafeEquals(Current, other.Current) &&
                SafeEquals(Prev, other.Prev);
        }

        public override int GetHashCode() {
            unchecked {
                var hc = Hashes.Quick(Primes.Prime018, Layer);
                hc = Hashes.Quick(hc, PosIdx);
                hc = Hashes.Quick(hc, NullSafeHashCode(Current));
                hc = Hashes.Quick(hc, NullSafeHashCode(Prev));
                return hc;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool SafeEquals(Tile a, Tile b) {
            return NullSafeHashCode(a) == NullSafeHashCode(b);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int NullSafeHashCode(Tile tile) {
            if (tile == null) { return 0; }
            return tile.GetVisualHashCode();
        }
    }
}
