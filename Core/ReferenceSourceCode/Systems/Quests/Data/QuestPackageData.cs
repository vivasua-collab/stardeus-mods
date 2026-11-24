using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Quests {
    [Serializable]
    [MessagePackObject]
    public sealed class QuestPackageData {
        [Key("Id")] public uint Id;
        [Key("State")] public QuestPackageState State;
        [Key("Flags")] public QuestPackageFlags Flags;
        [Key("QuestId")] public uint QuestId;
        [Key("StepId")] public string StepId;
        [Key("StoryEventId")] public string StoryEventId;
        [Key("AddQuestId")] public string AddQuestId;
        [Key("Mats")] public List<Mat> Mats;
        [Key("Money")] public double Money;
        [Key("ResearchDefId")] public string ResearchDefId;
        [Key("Seed")] public int Seed;

        public static QuestPackageData[] Serialize(
                Dictionary<uint, QuestPackage> questPackages) {
            if (questPackages == null || questPackages.Count == 0) {
                return null;
            }
            var res = new QuestPackageData[questPackages.Count];
            int idx = 0;
            foreach (var kvp in questPackages) {
                var pack = kvp.Value;
                var d = new QuestPackageData();
                d.Id = pack.Id;
                d.QuestId = pack.Step.Data.Id;
                d.StepId = pack.Step.Def.Id;
                d.State = pack.State;
                d.Seed = pack.Seed;
                d.ResearchDefId = pack.ResearchDefId;
                d.Money = pack.Money;
                d.AddQuestId = pack.AddQuestId;
                d.StoryEventId = pack.StoryEventId;
                d.Flags = pack.Flags;
                d.Mats = Mat.SerializeList(pack.Mats);
                res[idx++] = d;
            }
            return res;
        }

        public static void Deserialize(GameState s, QuestPackageData[] packages,
                Dictionary<uint, QuestPackage> questPackages) {
            if (Arrays.IsEmpty(packages)) {
                return;
            }
            for (int i = packages.Length - 1; i >= 0; i--) {
                QuestPackageData d = packages[i];
                var pack = Deserialize(s, d);
                if (pack == null) {
                    continue;
                }
                if (pack.Id > QuestPackage.LastId) {
                    QuestPackage.LastId = pack.Id;
                }
                if (!questPackages.TryAdd(pack.Id, pack)) {
                    D.Err("QuestPackage with same ID {0} already exists during deserialization", pack.Id);
                }
            }
        }

        private static QuestPackage Deserialize(GameState s, QuestPackageData data) {
            var questSys = s.Sys.Get<QuestSys>();
            QuestData questData = null;
            for (int i = 0; i < questSys.AllPlayerQuests.Count; i++) {
                if (questSys.AllPlayerQuests[i].Data.Id == data.QuestId) {
                    questData = questSys.AllPlayerQuests[i].Data;
                    break;
                }
            }
            if (questData == null) {
                D.Err("Could not find quest by id {0} for quest package {1}",
                    data.QuestId, data.Id);
                return null;
            }
            QuestStep step = null;
            for (int i = questData.Steps.Length - 1; i >= 0; i--) {
                if (questData.Steps[i].Def.Id == data.StepId) {
                    step = questData.Steps[i];
                    break;
                }
            }
            if (step == null) {
                D.Err("Could not find quest step by DefId {0} in quest {1} for package {2}",
                    data.StepId, questData, data.Id);
                return null;
            }
            var pack = new QuestPackage();
            pack.Id = data.Id;
            pack.State = data.State;
            pack.Step = step;
            pack.Seed = data.Seed;
            pack.Money = data.Money;
            pack.ResearchDefId = data.ResearchDefId;
            pack.StoryEventId = data.StoryEventId;
            pack.AddQuestId = data.AddQuestId;
            pack.Flags = data.Flags;
            pack.Mats = Mat.DeserializeList(data.Mats);
            return pack;
        }
    }
}
