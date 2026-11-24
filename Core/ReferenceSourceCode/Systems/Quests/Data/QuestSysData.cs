using System.Collections.Generic;
using Game.Components;
using Game.Systems.Story;
using MessagePack;

namespace Game.Systems.Quests {
    [MessagePackObject]
    public sealed class QuestSysData {
        [Key("LastQuestId")] public uint LastQuestId;
        [Key("TrackedQuestId")] public uint TrackedQuestId;
        [Key("AllPlayerQuests")] public QuestDataData[] AllPlayerQuests;
        [Key("Packages")] public QuestPackageData[] Packages;
        [Key("Seen")] public QuestSeenData[] Seen;
        [Key("Bools")] public QuestBools Bools;
        [Key("SharedData")] public ComponentData SharedData;
    }
}
