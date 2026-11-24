using System;
using System.Collections.Generic;
using Game.Data;
using KL.Algos;
using KL.Collections;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using Game.Systems.Notify;
using Game.Constants;

namespace Game.Systems.Suggestions {
    public abstract class Suggestion : IPrioritizable {
        public int QueueIndex { get; set; }
        public float QueuePriority { get; set; }
        private static readonly Dictionary<string, Suggestion> all = new();
        public static Dictionary<string, Suggestion> All => all;
        [ExecuteOnReload]
        public static void CleanupStatics() {
            all.Clear();
        }

        protected static void Register(Suggestion sugg) {
            var old = all.PutGet(sugg.Id, sugg);
            if (old != null) {
                D.Err("Overwriting a goal with same id: {0}", sugg);
            }
        }
        public bool HasUIAction => UIAction != null;
        public virtual Action<GameState> UIAction => null;
        public virtual Func<bool> UIActionShouldBeRemoved => null;
        public virtual string UIActionText =>
            throw new NotImplementedException("Override this!");

        public bool IsInternal;
        public bool IsCompleted;
        public bool IsIgnored;
        public bool IsCurrent;
        ///<summary>Recurring suggestions can be scored and repeatedly made
        /// current regardless if they were completed or not
        /// </summary>
        public virtual bool IsRecurring => false;
        public abstract string Id { get; }
        public abstract string IconId { get; }
        public static int Count => all.Count;
        public virtual string Title => $"goal.{Id}".T();
        public virtual string Description => $"goal.{Id}.desc".T();

        public virtual bool CheckDependencies(HashSet<string> completedGoals) {
            return true;
        }
        // Shows Options (UIAction + Ignore) instead of a single UIAction button.
        // The suggestion decides what ignoring means in OnIgnore.
        public virtual bool CanIgnore => false;
        // A tutorial quest teaches this suggestion's subject. While the tutorial chain runs,
        // the suggestion waits until this quest bool is set, so the two never speak at once.
        public virtual string WaitsForBool => null;
        public virtual void OnIgnore(GameState state) { }
        // No longer relevant - removed silently, without counting as completed
        public virtual bool ShouldBeDismissed(GameState state) => false;
        public virtual void OnAdded(GameState state) { }
        public virtual void OnCompleted(GameState state) { }
        protected virtual void OnReset() { }
        /// <summary>
        /// NEVER USE if ticks % something == something, it will not run every tick!!!
        /// </summary>
        public abstract bool IsAvailable(GameState state);
        public abstract bool IsComplete(GameState state);
        public abstract float Score(GameState state);

        public override string ToString() {
            return $"Goal [{Id}]";
        }

        public static Suggestion Find(string id) {
            var goal = all.Get(id, null);
            return goal;
        }

        protected string DataKey(string key) {
            return $"goal.{Id}.{key}";
        }

        protected NotificationHandle notification;

        public void Notify(GameState s, NotificationData data, bool skipAutoAction) {
            if (notification.IsValid) { return; }
            notification = data.Show();
            if (!skipAutoAction && !IsCompleted) {
                data.OnClick?.Invoke();
            }
        }

        public void Reset() {
            IsCompleted = false;
            IsIgnored = false;
            IsCurrent = false;
            notification = default;
            OnReset();
        }

        public void Dismiss() {
            IsCurrent = false;
            if (notification.IsValid) {
                Notification.Remove(ref notification);
            }
        }

        public void NotifyComplete(bool silent) {
            if (notification.IsValid) {
                Notification.Remove(ref notification);
            }
            if (silent) { return; }
            var msg = T.GoalCompleted_V1.F(Title);
            A.S.Sig.ShowFanfare.Send(msg);
        }
    }
}
