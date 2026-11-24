using System;
using Game.Components;
using Game.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Utils {
    public sealed class ComponentConfigJsonConverter : JsonConverter<ComponentConfig> {
        public override ComponentConfig ReadJson(JsonReader reader, Type objectType, ComponentConfig existingValue, bool hasExistingValue, JsonSerializer serializer) {
            if (reader.TokenType == JsonToken.Null) {
                return null;
            }

            var array = JArray.Load(reader);
            var properties = array.ToObject<SerializableProperty[]>(serializer);
            var config = new ComponentConfig();
            config.Properties = properties;
            return config;
        }

        public override void WriteJson(JsonWriter writer, ComponentConfig value, JsonSerializer serializer) {
            if (value == null) {
                writer.WriteNull();
                return;
            }
            serializer.Serialize(writer, value.Properties);
        }
    }
}
