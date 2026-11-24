#if !PRODUCTION_BUILD
#define DEBUG_UNSPAWNED
// #define DEBUG_MAIN_MENU_POPUPS
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class WPopupData {
        #if DEBUG_UNSPAWNED
        public static HashSet<WPopupData> UnspawnedPopups = new();
        private string SpawnTrace;
        #endif
        public static ConcurrentQueue<WPopupData> PendingPopups;
        private static readonly Dictionary<string, WPopupData> uniquePopups = new();
        public static void ProcessPending() {
            if (PendingPopups == null) {
                #if DEBUG_UNSPAWNED
                if (UnspawnedPopups.Count > 0) {
                    D.Err("Unspawned popups: {0}", UnspawnedPopups.Count);
                    D.Err("First unspawned popoup trace: {0}",
                        UnspawnedPopups.First().SpawnTrace);
                }
                #endif
                return;
            }
            while (PendingPopups.TryDequeue(out var popup)) {
                popup.SpawnInternal();
            }
        }
        public string Icon;
        public string Title;
        public string SubTitle;
        public string Text;
        public string Button1Text;
        public Action Button1Act;
        public string Button2Text;
        public Action Button2Act;
        public Func<bool> ValidateButton1;
        public Func<bool> ValidateButton2;
        public int TimeoutTicks;
        public Action OnTimeoutAct;
        public Action OnTickAct;
        public bool CenterOfScreen;
        public bool KeepOpenOnButton1;
        public bool CloseOnLinkClick;
        public float DistToEdge;
        public string InputPlaceholder;
        public string InputLabel;
        public string InputValue;
        /// <summary>
        /// Returns null if submit is ok, an error string otherwise
        /// </summary>
        public Func<string, string> InputOnSubmit;
        public bool IsSpawned;
        public Action<WPopup> OnSpawn;
        public RectTransform Container;
        public WPopup Widget;
        public bool IsSystem;
        public string UniqueId;

        public WPopupData(bool isSystem, string icon, string title, string subtitle, string text) {
            D.Warn("Creating popup: {0} (sys: {1})", title, isSystem);
            Icon = icon;
            Title = title;
            SubTitle = subtitle;
            IsSystem = isSystem;
            Text = text;
            Button1Text = T.Ok;
            #if DEBUG_UNSPAWNED
            UnspawnedPopups.Add(this);
            SpawnTrace = Environment.StackTrace;
            #endif
        }

        public WPopupData Spawn(RectTransform container) {
            Spawn(false, container);
            return this;
        }

        public WPopupData Unique(string id) {
            UniqueId = id;
            return this;
        }

        public WPopupData Spawn(bool center = false, RectTransform container = null) {
            if (IsSpawned) {
                D.Err("Popup already spawned! {0}", this);
                return this;
            }
            CenterOfScreen = center;
            return Spawn(null, center, container);
        }
        public WPopupData Spawn(Action<WPopup> onSpawn, bool center = false, RectTransform container = null) {
            if (IsSystem && UIShowing.MainMenu && container != UIPoolRoot.SystemPopupContainer) {
                D.Err("Spawning system popup with main menu visible but not in system container! {0}", Title);
            }
            if (IsSpawned) {
                D.Err("Popup already spawned! {0}", this);
                return this;
            }
            Container = container;
            OnSpawn = onSpawn;
            CenterOfScreen = center;
            IsSpawned = true;
            if (A.IsMainThread) {
                SpawnInternal();
            } else {
                if (PendingPopups == null) {
                    PendingPopups = new ConcurrentQueue<WPopupData>();
                }
                PendingPopups.Enqueue(this);
            }
            return this;
        }
        private void SpawnInternal() {
            #if DEBUG_UNSPAWNED
            UnspawnedPopups.Remove(this);
            #endif
            if (UniqueId != null) {
                if (uniquePopups.TryGetValue(UniqueId, out var spawned)) {
                    spawned.Despawn();
                }
                uniquePopups[UniqueId] = this;
            }
            WPopup.Spawn(this);
        }
        /// <param name="dist">Percentage from corner to mouse</param>
        public WPopupData WithDistToEdge(float dist) {
            if (IsSpawned) {
                D.Err("Popup already spawned! {0}", this);
                return this;
            }
            DistToEdge = dist;
            return this;
        }
        public WPopupData WithAction(string buttonText, Action onClick) {
            if (IsSpawned) {
                D.Err("Popup already spawned! {0}", this);
                return this;
            }
            Button1Text = buttonText;
            Button1Act = onClick;
            return this;
        }

        public WPopupData WithAction2(string buttonText, Action onClick) {
            if (IsSpawned) {
                D.Err("Popup already spawned! {0}", this);
                return this;
            }
            Button2Text = buttonText;
            Button2Act = onClick;
            return this;
        }
        public WPopupData WithKeepOpenOnButton1() {
            if (IsSpawned) {
                D.Err("Popup already spawned! {0}", this);
                return this;
            }
            KeepOpenOnButton1 = true;
            return this;
        }
        // Research tree links open a full-screen view - an informational popup
        // left on top would only cover it
        public WPopupData WithCloseOnLinkClick() {
            if (IsSpawned) {
                D.Err("Popup already spawned! {0}", this);
                return this;
            }
            CloseOnLinkClick = true;
            return this;
        }
        public WPopupData WithValidateAction(Func<bool> func) {
            if (IsSpawned) {
                D.Err("Popup already spawned! {0}", this);
                return this;
            }
            ValidateButton1 = func;
            return this;
        }
        public WPopupData WithValidateAction2(Func<bool> func) {
            if (IsSpawned) {
                D.Err("Popup already spawned! {0}", this);
                return this;
            }
            ValidateButton2 = func;
            return this;
        }
        public WPopupData WithTimeout(int ticks, Action onTimeout, Action onTick) {
            if (IsSpawned) {
                D.Err("Popup already spawned! {0}", this);
                return this;
            }
            TimeoutTicks = ticks;
            OnTimeoutAct = onTimeout;
            OnTickAct = onTick;
            return this;
        }

        public void Despawn() {
            if (Widget != null) {
                if (Widget.Data == this && Widget.gameObject.activeSelf) {
                    D.Warn("Despawning popup: {0}", Title);
                    Widget.CloseInstant();
                }
                Widget = null;
            }
        }

        public WPopupData WithInput(string label, string placeholder, string value, Func<string, string> onSubmit) {
            InputLabel = label;
            InputPlaceholder = placeholder;
            InputValue = value;
            InputOnSubmit = onSubmit;
            return this;
        }

        public void OnDespawn() {
            if (UniqueId != null) {
                if (uniquePopups.TryGetValue(UniqueId, out var data)) {
                    if (data == this) {
                        uniquePopups.Remove(UniqueId);
                    } else {
                        D.Err("Unique popup isn't same on despawn! {0} | {1}",
                            UniqueId, this);
                    }
                }
                uniquePopups.Remove(UniqueId);
            }
        }

        public override string ToString() {
            return $"WPopupData[{Title}]";
        }
    }
}
