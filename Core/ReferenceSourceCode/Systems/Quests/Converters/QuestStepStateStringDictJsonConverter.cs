using System;
using System.Collections.Generic;
using KL.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Systems.Quests {
    public class QuestStepStateStringDictJsonConverter : JsonConverter<Dictionary<QuestStepState, string>> {
        public override void WriteJson(JsonWriter writer, Dictionary<QuestStepState, string> value, JsonSerializer serializer) {
            if (value == null) {
                writer.WriteNull();
                return;
            }
            serializer.Serialize(writer, value);
        }

        public override Dictionary<QuestStepState, string> ReadJson(JsonReader reader, Type objectType,
                Dictionary<QuestStepState, string> existingValue, bool hasExistingValue, JsonSerializer serializer) {
            if (reader.TokenType == JsonToken.Null) {
                return null;
            }

            var jObject = JObject.Load(reader);
            var result = new Dictionary<QuestStepState, string>();

            foreach (var property in jObject.Properties()) {
                if (Enums.TryFromString<QuestStepState>(property.Name, out var state)) {
                    result[state] = property.Value.ToObject<string>();
                } else {
                    throw new Exception($"Invalid quest step state: {property.Name}");
                }
            }

            return result;
        }
    }
}
