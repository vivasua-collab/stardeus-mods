using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Utils;

namespace Game.Systems.Drama {
    public sealed class SocialOpportunity {
        public BeingData Being;
        public Slot Slot;
        public IFunProvider FunProvider;
        public int TypeH;
        public long StartTick;
        public long ExpireTick;
        private readonly HashSet<uint> takers = new();
        public bool IsExpired => ExpireTick != 0 && ExpireTick < G.Ticks;
        public int TakerCount => takers.Count;

        public bool CanBeTakenBy(BeingData being) {
            if (being == null || being.Id == 0) {
                return false;
            }
            return takers.Contains(being.Id)
                || takers.Count < Tunables.SocialOpportunityMaxTakers;
        }

        public bool TryAddTaker(BeingData being) {
            if (being == null || being.Id == 0) {
                return false;
            }
            if (takers.Contains(being.Id)) {
                return true;
            }
            if (takers.Count >= Tunables.SocialOpportunityMaxTakers) {
                return false;
            }
            takers.Add(being.Id);
            return true;
        }

        public static SocialOpportunity CreateFor(AIAgentComp agent,
                IFunProvider funProvider,
                int type, int expectedDuration) {
            var op = new SocialOpportunity();
            op.Being = agent.BeingData;
            op.FunProvider = funProvider;
            op.TypeH = type;
            op.StartTick = G.Ticks;
            if (expectedDuration > 0) {
                op.ExpireTick = G.Ticks + expectedDuration;
            }
            return op;
        }
        public static SocialOpportunity CreateFor(AIAgentComp agent, Slot slot,
                int type, int expectedDuration) {
            var op = new SocialOpportunity();
            op.Being = agent.BeingData;
            op.Slot = slot;
            op.TypeH = type;
            op.StartTick = G.Ticks;
            if (expectedDuration > 0) {
                op.ExpireTick = G.Ticks + expectedDuration;
            }
            return op;
        }

        public override string ToString() {
            return $"SocialOpportunity[{SocialOpportunityIdH.ResolveName(TypeH)},{Units.TicksToTime(StartTick)}-{Units.TicksToTime(ExpireTick)},{Being}]";
        }
    }
}
