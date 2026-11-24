using System;
using System.Text;
using Game.Components;
using Game.Data;
using Game.Utils;
using KL.Utils;
using Newtonsoft.Json;

namespace Game.Systems.Quests {
    [Serializable]
    public sealed class QuestVarDef {
        public string Id;
        public int IdH;
        public string Type;
        private QuestVar.Factory factory;
        [JsonConverter(typeof(ComponentConfigJsonConverter))]
        public ComponentConfig Config;
        public bool Deserialize(StringBuilder err, object context) {
            if (string.IsNullOrWhiteSpace(Id)) {
                err.AppendFormat("{0}: {1}: field missing: Id\n", context, this);
                return false;
            }
            if (string.IsNullOrWhiteSpace(Type)) {
                err.AppendFormat("{0}: {1}: field missing: Type\n", context, this);
                return false;
            }
            factory = Registries.QuestVars.Get(Type);
            if (factory == null) {
                err.AppendFormat("Couldn't find QuestVar implementation by id {0} for: {1}",
                    Type, context);
                return false;
            }
            IdH = Hashes.S(Id);
            return true;
        }

        public override string ToString() {
            return $"QuestVarDef[{Id}|{Type}]";
        }

        public QuestVar CreateFor(GameState s, QuestData data, int seed, bool isLoading) {
            try {
                return factory.Invoke(s, this, data, seed, isLoading);
            } catch (Exception ex) {
                D.LogEx(ex, "Failed creating QuestVar: {0}", this);
                return null;
            }
        }

        public string DataKey(string k) {
            return $"v.{Id}.{k}";
        }
    }
}
