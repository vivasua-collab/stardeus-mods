using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using KL.Algos;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Input;
using Game.Systems.UIUX;

namespace Game.Systems.Hints {
    public abstract class Hint : ISaveableChild {
        protected bool HasBinding(ActionType act) {
            return The.Bindings.HasBinding(act);
        }
        private static readonly Dictionary<string, Hint> all = new();
        public static Dictionary<string, Hint> All => all;
        [ExecuteOnReload]
        public static void CleanupStatics() {
            all.Clear();
        }

        protected virtual float cooldownTimeSeconds => 15;
        protected virtual int hintAfterActions => 3;
        protected virtual bool HasCooldown => true;
        protected virtual bool IsRecurring => false;
        protected virtual void Validate() {}
        int actionCount;
        float lastActionTime;

        private bool isIrrelevant;
        public bool IsRelevant => !isIrrelevant;

        public void Reset() {
            isIrrelevant = false;
            actionCount = 0;
            lastActionTime = 0;
        }

        protected virtual void ShowHint(bool optional) {
            if (IsRecurring) {
                ShowWithCooldown(optional);
            } else {
                WPopup.Create(false, IconId.CInfo, T.Hint, null, Text).Spawn();
                isIrrelevant = true;
            }
        }

        public void ReduceActionCount() {
            if (isIrrelevant) { return; }
            if (actionCount > 0) {
                actionCount--;
            }
        }

        public void TrackAction() {
            if (isIrrelevant) { return; }
            if (HasCooldown) {
                TrackWithCooldown();
            } else {
                IncreaseCount();
            }
        }

        public void Deactivate() {
            isIrrelevant = true;
        }

        public void Show(bool optional) {
            Validate();
            if (isIrrelevant) { return; }
            ShowHint(optional);
        }
        private void ShowWithCooldown(bool optional) {
            var t = GameTimer.unscaledTime;
            if (t - lastActionTime > cooldownTimeSeconds) {
                // FIXME this was 'optional' in old popup system
                WPopup.Create(false, IconId.CInfo, T.Hint, Text).Spawn();
                lastActionTime = t;
            }
        }

        private void TrackWithCooldown() {
            if (isIrrelevant) { return; }
            var t = GameTimer.unscaledTime;
            if (Maths.Approximately(lastActionTime, 0f)) {
                lastActionTime = t;
            }
            if (t - lastActionTime > cooldownTimeSeconds) {
                actionCount = 0;
            } else {
                IncreaseCount();
            }
            lastActionTime = t;
        }

        private void IncreaseCount() {
            if (++actionCount >= hintAfterActions) {
                Show(true);
            }
        }

        protected static void Register(Hint hint) {
            var old = all.PutGet(hint.id, hint);
            if (old != null) {
                D.Err("Overwriting a hint with same id: {0}", hint);
            }
        }
        public bool HasUIAction => UIAction != null;
        public virtual Action UIAction => null;
        public virtual string UIActionKey =>
            throw new NotImplementedException("Override this!");

        protected abstract string id { get; }
        protected string textT;
        public virtual string Text => textT ??= $"hint.{id}".T();

        public override string ToString() {
            return $"Hint [{id}]";
        }

        public static Hint Find(string id) {
            var hint = all.Get(id, null);
            if (hint == null) {
                D.Err("Cannot find hint with id: {0}. Available Ids: {1}",
                    id, D.Dump(all.Keys));
            }
            return hint;
        }

        protected string DataKey(string key) {
            return $"hint.{id}.{key}";
        }

        public void OnSave(ComponentData data) {
            data.SetBool(DataKey("irrelevant"), isIrrelevant);
            if (!HasCooldown) {
                data.SetInt(DataKey("count"), actionCount);
            }
        }

        public void OnLoad(ComponentData data) {
            isIrrelevant = data.GetBool(DataKey("irrelevant"), false);
            if (!HasCooldown) {
                actionCount = data.GetInt(DataKey("count"), 0);
            }
        }
    }
}
