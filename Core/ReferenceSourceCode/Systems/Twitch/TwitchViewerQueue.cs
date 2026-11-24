using System;
using System.Collections.Generic;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using MessagePack;
using UnityEngine.Pool;

namespace Game.Systems.Twitch {
    [MessagePackObject]
    public class TwitchViewerQueue {
        private const int maxInactiveMinutes = 5;
        /// <summary>
        /// Viewer ID list
        /// </summary>
        [Key("IdQueue")] public List<int> Queue;

        [IgnoreMember] public int Size => Queue?.Count ?? 0;

        public bool TryAdd(TwitchViewer viewer) {
            Queue ??= new();
            return Queue.AddUniqueInefficient(viewer.Id);
        }

        public bool TryRemove(TwitchViewer viewer) {
            if (viewer == null) {
                return false;
            }
            return Queue?.Remove(viewer.Id) ?? false;
        }

        public int NumberOf(TwitchViewer viewer) {
            if (viewer == null) {
                return -1;
            }
            return Queue?.IndexOf(viewer.Id) ?? -1;
        }

        private DateTime timeoutCutoff;

        private bool Check(TwitchViewer viewer, TwitchSettings settings, bool? sub) {
            if (sub != null && sub != viewer.isSubscriber) {
                return false;
            }
            if (settings.QueueSkipInactive && !viewer.IsActiveWithTimeout(timeoutCutoff)) {
                D.Warn("Skipping viewer due to timeout: {0} | Last Seen: {1}",
                    viewer.Name, viewer.LastSeen);
                return false;
            }
            return true;
        }

        public TwitchViewer Next(Dictionary<int, TwitchViewer> viewers,
                TwitchSettings settings) {

            TwitchViewer selected = null;

            // pool of options if we are getting a random viewer
            List<TwitchViewer> options = null;

            bool isRandom = settings.QueueMode == TwitchSettings.ViewerQueueMode.Random;
            if (isRandom) {
                // get options from pool
                options = ListPool<TwitchViewer>.Get();
                options.Clear();
            }

            // cache the timeoutCutoff
            if (settings.QueueSkipInactive) {
                timeoutCutoff = DateTime.UtcNow - new TimeSpan(0, maxInactiveMinutes, 0);
            }

            bool? sub = null;

            if (settings.SubQueuePriority) {
                // force subscriber == true
                sub = true;
                if (isRandom) {
                    AddMatchingToOptions(viewers, options, (v) => Check(v, settings, sub));
                } else {
                    selected = SelectFirstMatching(viewers, (v) => Check(v, settings, sub));
                    if (selected != null) {
                        // found a subscriber
                        return selected;
                    }
                }
                // force subscriber == false for processing less viewers in the next block
                sub = false;
            }

            // fallback if we didn't find a sub, or if there was no sub priority
            if (isRandom) {
                if (options.Count == 0) {
                    // only if didn't find any subs
                    AddMatchingToOptions(viewers, options, (v) => Check(v, settings, sub));
                }
            } else {
                // didn't find a sub
                if (selected == null) {
                    selected = SelectFirstMatching(viewers, (v) => Check(v, settings, sub));
                }
            }

            if (isRandom) {
                if (options.Count > 0) {
                    selected = Rng.UFrom(options);
                    options.Clear();
                }
                ListPool<TwitchViewer>.Release(options);
            }
            return selected;
        }

        private void AddMatchingToOptions(Dictionary<int, TwitchViewer> viewers,
                List<TwitchViewer> options, Func<TwitchViewer, bool> check) {
            for (int i = 0; i < Queue.Count; i++) {
                int id = Queue[i];
                if (viewers.TryGetValue(id, out var viewer)) {
                    if (check(viewer)) {
                        options.Add(viewer);
                    }
                } else {
                    // someone was in queue but there is no viewer with such id
                    Queue.RemoveAt(i);
                    i--;
                }
            }
        }

        private TwitchViewer SelectFirstMatching(Dictionary<int, TwitchViewer> viewers,
                Func<TwitchViewer, bool> check) {
            for (int i = 0; i < Queue.Count; i++) {
                int id = Queue[i];
                if (viewers.TryGetValue(id, out var viewer)) {
                    if (check(viewer)) {
                        return viewer;
                    }
                } else {
                    // someone was in queue but there is no viewer with such id
                    Queue.RemoveAt(i);
                    i--;
                }
            }
            return null;
        }

        public void Clear() {
            Queue?.Clear();
        }
    }
}
