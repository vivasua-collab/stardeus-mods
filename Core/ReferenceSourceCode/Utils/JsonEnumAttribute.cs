using System;

namespace Game.Utils {
    // A config string (or string array) whose values are names of an enum, parsed at load with
    // Enums.FromString / FlagsFromStringArray. The JSON schema generator turns it into a list of
    // allowed values, so a misspelled flag fails JsonSchemaTests instead of only logging at boot.
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class JsonEnumAttribute : Attribute {
        public readonly Type EnumType;

        public JsonEnumAttribute(Type enumType) {
            EnumType = enumType;
        }
    }
}
