#if !PRODUCTION_BUILD
// #define DEBUG_ACTION_Q
// #define IMMEDIATE_Q
#endif
using System;
using System.Collections.Generic;
using KL.Utils;
using UnityEngine;

namespace Game.Commands {
    public class ActionQueue {
        private readonly Queue<Action> queue = new();
        private const int warnAt = 50000;
        public void Enqueue(Action action) {
            #if IMMEDIATE_Q
            action.Invoke();
            return;
            #endif
            queue.Enqueue(action);
        }
        public void Clear() {
            queue.Clear();
        }
        public void EnsureEmptyAfterLoad() {
            if (queue.Count == 0) {
                return;
            }

            D.Err("Having pending actions in the action queue in after load! {0}",
                D.Dump(queue));
            Clear();
        }

        private bool forceBurnQueue;

        public void Execute(float budget) {
            var qCount = queue.Count;
            if (qCount == 0) { return; }
            if (qCount > warnAt) {
                D.Err("ActionQueue count exceeded 50000! FORCE BURN");
                forceBurnQueue = true;
            }
            var startTime = Time.realtimeSinceStartupAsDouble;
            var executed = 0;
            while (queue.TryDequeue(out var cb)) {
                // a throwing action used to escape into Bootstrap's Update / FixedUpdate and
                // skip that frame's clock update or fixed update along with the queue
                try {
                    cb.Invoke();
                } catch (Exception ex) {
                    D.LogEx(ex, "Failed executing queued action: {0}", cb);
                }
                if (!forceBurnQueue && executed++ % 25 == 0) {
                    if (Time.realtimeSinceStartupAsDouble - startTime > budget) {
                        #if DEBUG_ACTION_Q
                        D.Warn("Executed {0} - {1} more items left for next frame",
                            executed, qCount);
                        #endif
                        return;
                    }
                }
            }
            // the overflow burn lasts until the queue drains, not for the rest of the session
            forceBurnQueue = false;
            #if DEBUG_ACTION_Q
            D.Warn("Executed all {0} items in action q!", qCount);
            #endif
        }
    }
}
