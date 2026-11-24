using System;
using Game.CodeGen;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class StringQuestVar : QuestVar, IQuestVar<string> {
        [SelfInit]
        private static void Register() {
            Registries.QuestVars.Add(VarId,
                (s, def, data, seed, l) => new StringQuestVar(s, def, data, seed, l));
        }

        public const string VarId = "String";
        private string val;

        public string Value => val;

        public StringQuestVar(GameState s, QuestVarDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            throw new NotImplementedException("must be random from a list or something");
        }
    }
}
