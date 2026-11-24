using System;
using System.Collections.Generic;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Quests {
    [MessagePackObject]
    public sealed class QuestVarData {
        [Key("DefId")] public string DefId;
        [Key("Seed")] public int Seed;

        public static QuestVarData[] Serialize(Dictionary<int, QuestVar> variables) {
            if (variables == null || variables.Count == 0) {
                return null;
            }
            var res = new QuestVarData[variables.Count];
            int idx = 0;
            foreach (var kvp in variables) {
                var v = kvp.Value;
                var d = new QuestVarData();
                d.DefId = v.Def.Id;
                d.Seed = v.Seed;
                res[idx++] = d;
            }
            return res;
        }

        public static bool TryDeserialize(GameState s, QuestVarData[] variables, QuestData q) {
            if (Arrays.IsEmpty(variables)) {
                return true;
            }
            q.Variables = new Dictionary<int, QuestVar>(variables.Length);
            for (int i = variables.Length - 1; i >= 0; i--) {
                QuestVarData d = variables[i];
                var varDef = FindVarDef(q.Def, d.DefId);
                if (varDef == null) {
                    D.Err("QuestVarDef not found by id {0} | {1}",
                        d.DefId, q);
                    return false;
                }
                var v = varDef.CreateFor(s, q, d.Seed, isLoading: true);
                if (v == null) {
                    return false;
                }
                if (!q.Variables.TryAdd(varDef.IdH, v)) {
                    D.Err("QuestVar with same ID {0} already exists for quest: {1}",
                        varDef.Id, q);
                    return false;
                }
            }
            return true;
        }

        private static QuestVarDef FindVarDef(QuestDef questDef, string varId) {
            if (Arrays.IsEmpty(questDef.Variables)) {
                return null;
            }
            var varIdH = Hashes.S(varId);
            for (int i = questDef.Variables.Length - 1; i >= 0; i--) {
                QuestVarDef vd = questDef.Variables[i];
                if (vd.IdH == varIdH) {
                    return vd;
                }
            }
            return null;
        }
    }
}
