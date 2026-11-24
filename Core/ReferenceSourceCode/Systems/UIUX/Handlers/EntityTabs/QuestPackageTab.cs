using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Systems.Quests;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class QuestPackageTab : BaseEntityTab {
        public const string TabId = "QuestPackage";

        [SelfInit]
        private static void Register() {
            RegisterTab(new QuestPackageTab());
        }

        private QuestPackageComp package;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 20;
        public override string TabName => T.NounQuestPackage;

        public override bool ShowFor(GameState s, ObjData obj) {
            if (obj.Obj == null) { return false; }
            if (!obj.Obj.TryGetComponent(out package)) {
                return false;
            }

            if (package.IsOpen) {
                return false;
            }

            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContent2Header,
                block.Container, idx++);
            header.Load(T.NounQuest);

            var pack = FindQuestPackage();
            var quest = pack?.Step?.Data?.Quest;
            if (quest == null) {
                var questRow = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                questRow.Load(IconId.CQuest, T.DetailsUnknown, null,
                    () => ShowQuest(null));

            } else {
                var questTitle = QuestUtils.DecoratedTitle(quest.Data);
                var questRow = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                var isTracked = S.Query.IsTrackedQuest.Ask(quest);
                questRow.Load(QuestUtils.IconFor(quest, isTracked), questTitle, null,
                    () => ShowQuest(quest));
            }

            return true;
        }

        private QuestPackage FindQuestPackage() {
            return S.Query.FindQuestPackage.Ask(package.QuestPackageId);
        }

        private void ShowQuest(Quest quest) {
            if (quest == null) {
                S.UIQuery.ShowQuests.Ask(true);
                return;
            }

            S.UIQuery.ShowQuest.Ask(quest);
        }

        protected override void Cleanup() {
            package = null;
            S = null;
        }
    }
}
