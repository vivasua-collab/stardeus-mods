using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;
using KL.Randomness;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class BreakdownStayInRoomStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownStayInRoom";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownStayInRoomStoryEvent());
        }

        protected override MentalBreakdownBand Band => MentalBreakdownBand.Light;
        protected override int AIVar => AIVarsH.MentalStayedInRoom;
        protected override string Name => T.BreakdownBreakdownstayinroom;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownBreakdownstayinroomDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
