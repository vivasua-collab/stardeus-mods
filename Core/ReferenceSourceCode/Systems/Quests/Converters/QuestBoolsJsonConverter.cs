using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Systems.Quests {
    public class QuestBoolsJsonConverter : JsonConverter<QuestBools> {
        public override void WriteJson(JsonWriter writer, QuestBools value, JsonSerializer serializer) {
            if (value == null) {
                writer.WriteNull();
                return;
            }
            serializer.Serialize(writer, value.Names);
        }

        public override QuestBools ReadJson(JsonReader reader, Type objectType,
                QuestBools existingValue, bool hasExistingValue, JsonSerializer serializer) {
            if (reader.TokenType == JsonToken.Null) {
                return null;
            }

            var array = JArray.Load(reader);
            var names = array.ToObject<string[]>();
            return new QuestBools(names);
        }
    }
}
