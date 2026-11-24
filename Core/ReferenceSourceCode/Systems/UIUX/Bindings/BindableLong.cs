namespace Game.Systems.UIUX {
    public sealed class BLong : B<long> {
        public BLong(long initialValue = 0, int capacity = 1) : base(initialValue, capacity) { }

        public void Increment() => Set(Value + 1L);
        public void Decrement() => Set(Value - 1L);
        public void Add(int amount) => Set(Value + amount);
        public void Add(long amount) => Set(Value + amount);
        public void Multiply(long factor) => Set(Value * factor);
        public void Divide(long divisor) => Set(Value / divisor);

        // ++ and -- operators (modify the bindable)
        public static BLong operator ++(BLong b) {
            b.Increment();
            return b;
        }

        public static BLong operator --(BLong b) {
            b.Decrement();
            return b;
        }

        // + and - operators (return new value, don't modify bindable)
        public static long operator +(BLong b, long value) => b.Value + value;
        public static long operator +(long value, BLong b) => value + b.Value;
        public static long operator +(BLong a, BLong b) => a.Value + b.Value;
        public static long operator -(BLong b, long value) => b.Value - value;
        public static long operator -(long value, BLong b) => value - b.Value;
        public static long operator -(BLong a, BLong b) => a.Value - b.Value;

        // * and / operators
        public static long operator *(BLong b, long value) => b.Value * value;
        public static long operator *(long value, BLong b) => value * b.Value;
        public static long operator *(BLong a, BLong b) => a.Value * b.Value;
        public static long operator /(BLong b, long value) => b.Value / value;
        public static long operator /(long value, BLong b) => value / b.Value;
        public static long operator /(BLong a, BLong b) => a.Value / b.Value;

        // Comparison operators
        public static bool operator ==(BLong b, long value) => b.Value == value;
        public static bool operator !=(BLong b, long value) => b.Value != value;
        public static bool operator ==(long value, BLong b) => value == b.Value;
        public static bool operator !=(long value, BLong b) => value != b.Value;
        public static bool operator ==(BLong a, BLong b) => a.Value == b.Value;
        public static bool operator !=(BLong a, BLong b) => a.Value != b.Value;
        public static bool operator <(BLong a, BLong b) => a.Value < b.Value;
        public static bool operator >(BLong a, BLong b) => a.Value > b.Value;
        public static bool operator <=(BLong a, BLong b) => a.Value <= b.Value;
        public static bool operator >=(BLong a, BLong b) => a.Value >= b.Value;

        // Implicit conversion to long (read value)
        public static implicit operator long(BLong b) => b.Value;

        // Implicit conversion from long (write value)
        // public static implicit operator BLong(long value) => new BLong(value);

        // Required overrides when overloading ==
        public override bool Equals(object obj) {
            if (obj is BLong other) return Value == other.Value;
            if (obj is long longVal) return Value == longVal;
            return false;
        }

        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() {
            return $"BLong[{Value},L:{numListeners}]";
        }
    }
}
