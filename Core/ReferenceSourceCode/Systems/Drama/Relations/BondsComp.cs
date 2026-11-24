#if !PRODUCTION_BUILD
// #define DEBUG_CHAT
#endif
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using KL.Grid;
using System;
using Game.Systems.Mental;

namespace Game.Systems.Drama {
    public sealed class BondsComp : BaseComponent<BondsComp> {
        private bool showUnfamiliar => S.Situation.Friendlies < 50;
        public uint ChattingWith;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BondsComp());
        }

        public void StartChatWith(BeingData b) {
            #if DEBUG_CHAT
            D.Warn("CHAT: start chat with {0}->{1}", Being, b.Being);
            #endif
            if (ChattingWith != 0) {
                D.Err("CHAT: Already chatting with: {0} -> {1} | trying to talk to {2}",
                    Being, ChattingWith, b.Being);
            }
            ChattingWith = b.Id;
            // ShowThoughtBubble(IconId.CSpeak, T.ChattingWith_V1.F(b.Mind.LinkToShortName) + " " + Units.TicksToTime(G.Ticks));
            if (Being.AIAgent.ChatContext?.FaceBuddy != false) {
                BeingUtils.FacePosition(Being, b.Being.Position);
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.GraphicHidden) {
                // fixes the bug where being shows thumbs up/down forever if
                // thought bubble had a timeout and being was hidden (entered sleeping pod)
                HideThoughtBubble();
            }
        }

        public void EndChatWith(BeingData b, short change, string icon, string text) {
            if (ChattingWith == 0) {
                // wasn't chatting, perhaps chat we're ending didn't start?
                return;
            }
            if (ChattingWith != b.Id) {
                D.Err("CHAT: Chatting with was wrong! {0}->{1} | {2} != {3}",
                    Being, b.Being, b.Id, ChattingWith);
            }
            #if DEBUG_CHAT
            D.Warn("CHAT: Ending chat with {0}->{1}", Being, b.Being);
            #endif
            ChattingWith = 0;
            if (change != 0 && Being.IsAlive && b.HasStateFlag(BeingState.Alive)) {
                text = $"{text} ({T.SomethingHappenedWithName_V1.F(b.Mind.LinkToShortName)})";
                if (change > 0) {
                    ShowThoughtBubble(IconId.CThumbsUp, text, timeoutSeconds: 1);
                } else {
                    ShowThoughtBubble(IconId.CThumbsDown, text, timeoutSeconds: 1);
                }
                Being.Data.Mind.Health?.AddEffect(S, MentalEffect.CreateWithIcon(
                    Being.PosIdx, icon, G.Ticks, Consts.TicksPerHour, text, change));
            } else {
                HideThoughtBubble();
            }
        }
        public override string ToString() {
            return " * Bonds";
        }
    }
}
