using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;

namespace Game.Systems.Quests {
    public sealed class QuestPackage {
        public QuestPackageState State;
        public QuestPackageFlags Flags;
        public static uint LastId;
        public uint Id;
        public QuestStep Step;
        public string PackageDefId;
        public List<Mat> Mats;
        public double Money;
        public string ResearchDefId;
        public bool IsRegistered;
        public string StoryEventId;
        public string AddQuestId;
        public int Seed;
        public string Tooltip =>
            $"{T.NounQuestPackage}: {QuestUtils.DecoratedTitle(Step.Data)}";

        public static QuestPackage CreateFor(QuestStep step, QuestPackageFlags flags) {
            var pack = new QuestPackage();
            pack.Step = step;
            pack.Flags = flags;
            return pack;
        }

        public void Add(MatType matType, int amt = 1) {
            Mats ??= new List<Mat>();
            Mats.Add(new Mat() {
                Type = matType,
                StackSize = amt,
                MaxStackSize = amt,
            });
        }
    }
}
