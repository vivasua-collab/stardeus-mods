using Game.Systems.Stats;

namespace Game.Systems.UIUX {
    public sealed class BInt : B<int> {
        public BInt(int initialValue = 0, int capacity = 1) : base(initialValue, capacity) { }

        public void Increment() => Set(Value + 1);
        public void Decrement() => Set(Value - 1);
        public void Add(int amount) => Set(Value + amount);
        public void Multiply(int factor) => Set(Value * factor);
        public void Divide(int divisor) => Set(Value / divisor);

        // ++ and -- operators (modify the bindable)
        public static BInt operator ++(BInt b) {
            b.Increment();
            return b;
        }

        public static BInt operator --(BInt b) {
            b.Decrement();
            return b;
        }

        // + and - operators (return new value, don't modify bindable)
        public static int operator +(BInt b, int value) => b.Value + value;
        public static int operator +(int value, BInt b) => value + b.Value;
        public static int operator +(BInt a, BInt b) => a.Value + b.Value;
        public static int operator -(BInt b, int value) => b.Value - value;
        public static int operator -(int value, BInt b) => value - b.Value;
        public static int operator -(BInt a, BInt b) => a.Value - b.Value;

        // * and / operators
        public static int operator *(BInt b, int value) => b.Value * value;
        public static int operator *(int value, BInt b) => value * b.Value;
        public static int operator *(BInt a, BInt b) => a.Value * b.Value;
        public static int operator /(BInt b, int value) => b.Value / value;
        public static int operator /(int value, BInt b) => value / b.Value;
        public static int operator /(BInt a, BInt b) => a.Value / b.Value;

        // Comparison operators
        public static bool operator ==(BInt b, int value) => b.Value == value;
        public static bool operator !=(BInt b, int value) => b.Value != value;
        public static bool operator ==(int value, BInt b) => value == b.Value;
        public static bool operator !=(int value, BInt b) => value != b.Value;
        public static bool operator ==(BInt a, BInt b) => a.Value == b.Value;
        public static bool operator !=(BInt a, BInt b) => a.Value != b.Value;
        public static bool operator <(BInt a, BInt b) => a.Value < b.Value;
        public static bool operator >(BInt a, BInt b) => a.Value > b.Value;
        public static bool operator <=(BInt a, BInt b) => a.Value <= b.Value;
        public static bool operator >=(BInt a, BInt b) => a.Value >= b.Value;

        // Implicit conversion to int (read value)
        public static implicit operator int(BInt b) => b.Value;

        // Implicit conversion from int (write value)
        // public static implicit operator BInt(int value) => new BInt(value);

        // Required overrides when overloading ==
        public override bool Equals(object obj) {
            if (obj is BInt other) return Value == other.Value;
            if (obj is int intVal) return Value == intVal;
            return false;
        }

        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() {
            return $"BInt[{Value},L:{numListeners}]";
        }
    }
}
