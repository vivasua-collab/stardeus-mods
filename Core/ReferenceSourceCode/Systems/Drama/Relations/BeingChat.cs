#if !PRODUCTION_BUILD
// #define DEBUG_CHAT
#endif
using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Drama {
    public sealed class BeingChat {
        public readonly int PairHash;
        public readonly BeingData B1;
        public readonly BeingData B2;
        public string Context;
        public SocialChatContext B1Ctx;
        public SocialChatContext B2Ctx;
        public ChatBubble bubble;
        public bool HasEnded;
        private ClassPool<ChatBubble> chatBubblePool;
        private BeingChat(BeingData b1, BeingData b2) {
            B1 = b1;
            B2 = b2;
            B1Ctx = b1.Being?.AIAgent.ChatContext;
            B2Ctx = b2.Being?.AIAgent.ChatContext;
            PairHash = BeingUtils.PairHash(b1, b2);
        }
        public bool HasStarted;
        public long StartTick;
        public long EndTick;
        public static bool WillChat(BeingData b1, BeingData b2) {
            if (b1.EffectiveFaction != b2.EffectiveFaction) { return false; }
            if (b1.Being?.Bonds != null && b2.Being?.Bonds != null) {
                if (b1.Being.Bonds.ChattingWith != 0) { return false; }
                if (b2.Being.Bonds.ChattingWith != 0) { return false; }
            }
            if (!b1.HasAbility(AbilityIdH.Intelligence)) { return false; }
            if (!b2.HasAbility(AbilityIdH.Intelligence)) { return false; }
            if (!b1.HasAbility(AbilityIdH.Vocalize)) { return false; }
            if (!b2.HasAbility(AbilityIdH.Vocalize)) { return false; }
            if (!b1.HasAbility(AbilityIdH.Hear)) { return false; }
            if (!b2.HasAbility(AbilityIdH.Hear)) { return false; }
            // only beings on the same side can chat during combat
            if (b1.Being?.AIAgent.CombatUnit.Army != b2.Being?.AIAgent.CombatUnit.Army) { return false; }
            if (b1.Being?.AIAgent.ChatContext?.Forbidden == true) { return false; }
            if (b2.Being?.AIAgent.ChatContext?.Forbidden == true) { return false; }
            return true;
        }
        public static BeingChat Between(BeingData b1, BeingData b2, long ticks,
                SocialChatContext ctx, ClassPool<ChatBubble> bubblePool) {
            #if DEBUG_CHAT
            D.Warn("CHAT: Creating chat between {0} and {1} | t: {2} | {3}", b1, b2, ticks, Ready.LongStatus);
            #endif
            if (b1 == null || b2 == null) {
                D.Err("Creating a chat with B1 or B2 null: {0} | {1}", b1, b2);
                return null;
            }
            if (b1 == b2) {
                D.Err("Creating a chat with same B1 & B2: {0}", b1);
                return null;
            }
            var chat = new BeingChat(b1, b2);
            chat.chatBubblePool = bubblePool;
            int delay = ctx?.ChatDelay ?? 0;
            if (delay == 0) {
                delay = Rng.URange(Consts.TicksPer1Minutes, Consts.TicksPer5Minutes);
            }
            int duration = ctx?.ExpectedDuration ?? 0;
            if (duration == 0) {
                duration = Rng.URange(Consts.TicksPer15Minutes, Consts.TicksPer30Minutes);
            }
            chat.StartTick = ticks + delay;
            chat.EndTick = ticks + delay + duration;
            chat.Context = ctx?.Context ?? null;
            return chat;
        }

        public bool IsForced { get; private set; }

        // A chat its caller starts and ends (DramaSys.StartForcedChat). Proximity chats
        // never reach beings in a non-interruptable goal, such as a mental breakdown.
        public static BeingChat Forced(BeingData b1, BeingData b2, long ticks,
                string context, ClassPool<ChatBubble> bubblePool) {
            var chat = new BeingChat(b1, b2);
            chat.chatBubblePool = bubblePool;
            chat.IsForced = true;
            chat.StartTick = ticks;
            chat.EndTick = long.MaxValue;
            chat.Context = context;
            return chat;
        }


        /// <returns>false if chat should be removed from ongoing list</returns>
        public bool Tick(GameState s, long ticks, bool inProximity) {
            if (HasEnded) {
                // if we're ticking an ended chat again, it had shared context
                // and we don't want to remove this from the ongoing chats yet
                // because otherwise chat will restart
                return inProximity;
            }
            if (ticks < StartTick) { return true; }
            bool mustEnd = !inProximity || ticks > EndTick
                || B1.Being == null || B2.Being == null;
            if (mustEnd) {
                if (!HasStarted) {
                    return false;
                }
                End(s, ticks, silent: false);
                // let's not remove this chat from the list yet, so we will
                // not restart it
                if (!IsForced && B1Ctx?.Context != null) {
                    return true;
                }
                return false;
            } else if (!HasStarted) {
                if (!WillChat(B1, B2)) {
                    HasEnded = true;
                    return inProximity;
                }
                Start(s);
            }
            return true;
        }

        public void Start(GameState s) {
            #if DEBUG_CHAT
            D.Warn("CHAT: Starting chat {0}", this);
            #endif
            HasStarted = true;
            B1.Being.Bonds?.StartChatWith(B2);
            B2.Being.Bonds?.StartChatWith(B1);
            s.Sig.BeingChatChanged.Send(this, true);
            bubble = chatBubblePool.Spawn();
            bubble.ShowFor(s, B1.Being.Graphics, B2.Being.Graphics);
        }

        // silent: interrupted, so no good or bad outcome
        public void End(GameState s, long ticks, bool silent) {
            #if DEBUG_CHAT
            D.Warn("CHAT: Ending chat {0}", this);
            #endif
            int dur = (int) (ticks - StartTick);
            string title = null;
            string desc = null;
            string icon = null;
            short sanityChange = 0;
            if (!silent && CreateInteraction(Context, dur)) {
                var op1 = s.Query.GetOpinionAbout.Ask(B1.Mind, B2.Mind);
                var op2 = s.Query.GetOpinionAbout.Ask(B2.Mind, B1.Mind);
                var chanceGoodOutcome = 0.5f + (op1.SocialAffinity + op2.SocialAffinity) / 2f;
                chanceGoodOutcome = Mathf.Lerp(0.25f, 0.9f, chanceGoodOutcome);
                float change;
                if (Rng.UChance(chanceGoodOutcome)) {
                    icon = IconId.CChatGood;
                    if (Context != null) {
                        var ctx = Context.ToLower();
                        title = $"social.chat.{ctx}.good".T();
                        desc = $"social.chat.{ctx}.good.desc".T();
                    } else {
                        title = T.SocialChatGood;
                        desc = T.SocialChatGoodDesc_V2;
                    }
                    change = 0.05f;
                    sanityChange = 1;
                    desc = desc.F(B1.Mind.LinkToShortName, B2.Mind.LinkToShortName);
                } else {
                    change = -0.05f;
                    sanityChange = -1;
                    icon = IconId.CChatBad;
                    if (Context != null) {
                        var ctx = Context.ToLower();
                        title = $"social.chat.{ctx}.bad".T();
                        desc = $"social.chat.{ctx}.bad.desc".T();
                    } else {
                        title = T.SocialChatBad;
                        desc = T.SocialChatBadDesc_V2;
                    }
                    desc = desc.F(B1.Mind.LinkToShortName, B2.Mind.LinkToShortName);
                }
                var soc = SocialInteraction.Create(icon, title, desc)
                    .With(B1)
                    .With(B2)
                    .WithAttitude(change);
                s.Query.RegisterSocialInteraction.Ask(soc);
                s.Sys.Log.AddLine(desc);
            }
            B1.Being?.Bonds?.EndChatWith(B2, sanityChange, icon, title);
            B2.Being?.Bonds?.EndChatWith(B1, sanityChange, icon, title);
            bubble?.Hide(s);
            HasEnded = true;
            s.Sig.BeingChatChanged.Send(this, false);
        }

        private bool CreateInteraction(string context, int dur) {
            if (context == null) {
                return dur > Consts.TicksPer15Minutes;
            }
            return !context.StartsWith('_');
        }

        public void Update(GameState s) {
            // An ended chat with a shared context stays listed, its bubble must not come back
            if (HasEnded) { return; }
            bubble?.Update(s);
        }

        public override string ToString() {
            return $"BeingChat[{Context},{B1},{B2},{Units.TicksToTime(StartTick)}-{Units.TicksToTime(EndTick)}]";
        }

        public static bool HasSharedContext(BeingData b1, BeingData b2,
                out SocialChatContext ctx1) {
            ctx1 = b1.Being?.AIAgent.ChatContext;
            if (ctx1 == null) { return false; }
            var ctx2 = b2.Being?.AIAgent.ChatContext;
            if (ctx2 == null) { return false; }
            if (ctx1.Context == ctx2.Context) {
                return true;
            }
            return false;
        }

        public void Cleanup() {
            if (bubble != null) {
                chatBubblePool?.Despawn(bubble);
                bubble = null;
            }
            chatBubblePool = null;
        }
    }
}
