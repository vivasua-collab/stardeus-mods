namespace Game.Systems.UIUX {
    public sealed class BBool : B<bool> {
        public BBool(bool initialValue = false, int capacity = 1) : base(initialValue, capacity) { }

        // Comparison operators
        public static bool operator ==(BBool b, bool value) => b.Value == value;
        public static bool operator !=(BBool b, bool value) => b.Value != value;
        public static bool operator ==(bool value, BBool b) => value == b.Value;
        public static bool operator !=(bool value, BBool b) => value != b.Value;

        // Implicit conversion to int (read value)
        public static implicit operator bool(BBool b) => b.Value;

        // Implicit conversion from int (write value)
        // public static implicit operator BBool(bool value) => new BBool(value);

        // Required overrides when overloading ==
        public override bool Equals(object obj) {
            if (obj is BBool other) return Value == other.Value;
            if (obj is bool boolVal) return Value == boolVal;
            return false;
        }

        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() {
            return $"BBool[{Value},L:{numListeners}]";
        }
    }
}
