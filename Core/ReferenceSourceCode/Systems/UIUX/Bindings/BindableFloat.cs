namespace Game.Systems.UIUX {
    public sealed class BFloat : B<float> {
        public BFloat(float initialValue = 0f, int capacity = 1) : base(initialValue, capacity) { }

        public void Increment() => Set(Value + 1f);
        public void Decrement() => Set(Value - 1f);
        public void Add(float amount) => Set(Value + amount);
        public void Multiply(float factor) => Set(Value * factor);
        public void Divide(float divisor) => Set(Value / divisor);

        // ++ and -- operators (modify the bindable)
        public static BFloat operator ++(BFloat b) {
            b.Increment();
            return b;
        }

        public static BFloat operator --(BFloat b) {
            b.Decrement();
            return b;
        }

        // + and - operators (return new value, don't modify bindable)
        public static float operator +(BFloat b, float value) => b.Value + value;
        public static float operator +(float value, BFloat b) => value + b.Value;
        public static float operator +(BFloat a, BFloat b) => a.Value + b.Value;
        public static float operator -(BFloat b, float value) => b.Value - value;
        public static float operator -(float value, BFloat b) => value - b.Value;
        public static float operator -(BFloat a, BFloat b) => a.Value - b.Value;

        // * and / operators
        public static float operator *(BFloat b, float value) => b.Value * value;
        public static float operator *(float value, BFloat b) => value * b.Value;
        public static float operator *(BFloat a, BFloat b) => a.Value * b.Value;
        public static float operator /(BFloat b, float value) => b.Value / value;
        public static float operator /(float value, BFloat b) => value / b.Value;
        public static float operator /(BFloat a, BFloat b) => a.Value / b.Value;

        // Comparison operators
        public static bool operator ==(BFloat b, float value) => b.Value == value;
        public static bool operator !=(BFloat b, float value) => b.Value != value;
        public static bool operator ==(float value, BFloat b) => value == b.Value;
        public static bool operator !=(float value, BFloat b) => value != b.Value;
        public static bool operator ==(BFloat a, BFloat b) => a.Value == b.Value;
        public static bool operator !=(BFloat a, BFloat b) => a.Value != b.Value;
        public static bool operator <(BFloat a, BFloat b) => a.Value < b.Value;
        public static bool operator >(BFloat a, BFloat b) => a.Value > b.Value;
        public static bool operator <=(BFloat a, BFloat b) => a.Value <= b.Value;
        public static bool operator >=(BFloat a, BFloat b) => a.Value >= b.Value;

        // Implicit conversion to float (read value)
        public static implicit operator float(BFloat b) => b.Value;

        // Implicit conversion from float (write value)
        // public static implicit operator BFloat(float value) => new BFloat(value);

        // Required overrides when overloading ==
        public override bool Equals(object obj) {
            if (obj is BFloat other) return Value == other.Value;
            if (obj is float floatVal) return Value == floatVal;
            return false;
        }

        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() {
            return $"BFloat[{Value},L:{numListeners}]";
        }
    }
}
