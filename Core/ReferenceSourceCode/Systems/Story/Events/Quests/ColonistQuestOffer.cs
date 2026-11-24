using Game.Constants;
using Game.Systems.Creatures;
using Game.Systems.Quests;
using Game.Utils;

namespace Game.Systems.Story {
    // The "a colonist is asking for a favour" dialog of every personal colonist quest
    public static class ColonistQuestOffer {
        public static void Show(BeingData colonist, Quest quest) {
            var dd = The.Dialog.ShowChoice(
                null,
                T.ColonistAskingFavor_V1.F(colonist.Mind.LinkToShortName),
                left: () => { A.S.UIQuery.ShowNewQuest.Ask(quest); },
                // Ends the offer: a waiting one keeps the colonist from asking again
                right: () => { quest.Data.SetState(QuestState.Rejected); },
                rightButtonText: T.Ignore,
                leftButtonText: T.VerbRespond,
                canCloseWithEsc: false);
            dd.Being = colonist;
        }
    }
}
