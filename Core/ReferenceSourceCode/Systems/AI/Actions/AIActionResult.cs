#if !PRODUCTION_BUILD
// #define TRACE_RESULT
#endif
using System;
using System.Runtime.CompilerServices;
using KL.Utils;
using MessagePack;

namespace Game.Systems.AI {
    [MessagePackObject]
    public struct AIActionResult {
        [Key("State")] public AIActionState State;
        [Key("Message")] public string Message;
        [Key("Icon")] public string Icon;
        #if !PRODUCTION_BUILD
        [IgnoreMember] public string Trace;
        #endif
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AIActionResult Create(AIActionState state,
                string message = null, string icon = null) {
            #if TRACE_RESULT
            return new AIActionResult() {
                State = state,
                Message = message,
                Icon = icon,
                Trace = Environment.StackTrace,
            };
            #else
            return new AIActionResult() {
                State = state,
                Message = message,
                Icon = icon,
            };
            #endif
        }
        public override string ToString() {
            #if TRACE_RESULT
            D.Warn("Trace for state: {0}{1}: {2}", State, Message, Trace);
            return $"AIActionResult[{State}|{Message}|{Icon}|{Trace}]";
            #else
            return $"AIActionResult[{State}|{Message}|{Icon}]";
            #endif
        }
    }
}
