#if !PRODUCTION_BUILD
// #define DEBUG_NOTIFICATIONS
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.UIUX;
using Game.Systems.Voice;
using Game.UI;
using Game.Utils;
using KL.Atlas;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Notify {
    public sealed class NotificationSys : GameSystem {
        public const string SysId = "NotificationSys";
        public override string Id => SysId;
        private int nextId = 1;
        private string[] emptyNotifications;
        private string lastEmptyNotification = null;
        private int lastCnt = -1;
        readonly Dictionary<NotificationHandle, NotificationData> notifications = new();

        private readonly List<NotificationView>[] items = new List<NotificationView>[5];
        private readonly Dictionary<(string GroupId, NotificationType Type), NotificationView> groupedViews = new();
        private readonly List<NotificationHandle> toRemove = new();
        private NotificationHubWidget widget;
        private double lastAlertSound;
        private double lastNotifySound;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new NotificationSys());
        }


        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadState);
            items[(int) NotificationType.Immediate] = new();
            items[(int) NotificationType.Critical] = new();
            items[(int) NotificationType.Alert] = new();
            items[(int) NotificationType.Warning] = new();
            items[(int) NotificationType.Info] = new();
        }

        private void OnLoadState(GameState s) {
            var list = Caches.StringList;
            list.Clear();
            if (!Translations.TryLoadSequence("no.notifications", list)) {
                list.Add(T.NoNotifications0);
            }
            emptyNotifications = list.ToArray();
            s.Query.AddNotification.SetResponder(OnAdd);
            s.Query.RemoveNotification.SetResponder(OnRemove);
            s.Query.GetNotification.SetResponder(OnGet);
            s.Clock.OnTick.AddListener(OnTick);
            The.SysSig.ResolutionChanged.AddListener(OnResolutionChanged);
            widget = s.UIQuery.GetWidgetRegistry.Ask().NotificationHub;
            widget.Initialize(OnShow, SummaryTip);
            widget.SetCount(0, GetEmptyNotification());
        }

        private void OnResolutionChanged() {
            widget.UpdateSize();
        }

        private Lifetime OnShow(WGroup group) {
            var life = Lifetime.Create("NotificationHub");

            // Group first: notifications flagged Removed (Clear all) stay until the next tick,
            // which never comes while the clock is stopped, and they show nothing
            GroupNotifications();

            if (!HasVisibleItems()) {
                var block = UISpawn.Block(life, group, 0);
                var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, block.Container, 0);
                lastEmptyNotification = GetEmptyNotification();
                w.Load(IconId.CCheck, lastEmptyNotification);
                return life;
            }

            for (int i = 0; i < 5; i++) {
                AddGroupBlock(life, group, (NotificationType) i, items[i]);
            }

            return life;
        }

        private void GroupNotifications() {
            groupedViews.Clear();
            ClearItems();

            // Collect and group items
            foreach (var data in notifications.Values
                    .Where(d => (d.Flags & NotificationFlags.Removed) == NotificationFlags.None)
                    .OrderByDescending(n => n.CreatedTick)) {
                if (data.GroupId != null) {
                    var key = (data.GroupId, data.Type);
                    if (!groupedViews.TryGetValue(key, out var gView)) {
                        gView = new NotificationView();
                        groupedViews[key] = gView;
                        items[(int)data.Type].Add(gView);
                    }
                    gView.AddData(data);
                } else {
                    var view = new NotificationView();
                    view.AddData(data);
                    items[(int)data.Type].Add(view);
                }
            }
        }

        private bool HasVisibleItems() {
            for (int i = 0; i < items.Length; i++) {
                if (items[i].Count > 0) { return true; }
            }
            return false;
        }

        private void AddGroupBlock(Lifetime life, WGroup parent,
                NotificationType type, List<NotificationView> views) {
            if (views.Count == 0) {
                return;
            }

            var block = UISpawn.Block(life, parent, (int) type);
            int idx = 0;
            if (NotificationMutes.CanMute(type)) {
                SpawnMutableHeader(life, block.Container, type, idx++);
            } else {
                var header = UISpawn.Widget<WText>(life,
                    UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(TitleFor(type));
            }

            foreach (var view in views) {
                view.SpawnNotificationUI(life, block, idx++);
            }
        }

        private static void SpawnMutableHeader(Lifetime life, RectTransform container,
                NotificationType type, int idx) {
            var header = UISpawn.Widget<WButtonIconText>(life,
                UIPrefabId.WContent2HeaderButtonIconAlt, container, idx);
            LoadMutableHeader(header, type);
            header.SetOnClick(() => {
                NotificationMutes.SetMuted(type, !NotificationMutes.IsMuted(type));
                LoadMutableHeader(header, type);
            });
            UIUtils.AddTipFunc(life, header,
                () => NotificationMutes.IsMuted(type) ? T.NotifyMuted : null);
        }

        private static void LoadMutableHeader(WButtonIconText header, NotificationType type) {
            var icon = NotificationMutes.IsMuted(type) ? IconId.CSoundOff : IconId.CSoundOn;
            header.SetImage(icon);
            header.SetText(TitleFor(type));
        }

        private static string TitleFor(NotificationType type) {
            switch (type) {
                case NotificationType.Immediate: return T.ActionRequired;
                case NotificationType.Critical: return T.AlertsCritical;
                case NotificationType.Alert: return T.AlertsRegular;
                case NotificationType.Warning: return T.AlertsWarning;
                case NotificationType.Info: return T.AlertsInfo;
                default:
                    D.Err("Unhandled notification type: {0}", type);
                    return T.Unknown;
            }
        }

        private void ClearItems() {
            items[0].Clear();
            items[1].Clear();
            items[2].Clear();
            items[3].Clear();
            items[4].Clear();
        }

        private string GetEmptyNotification() {
            lastEmptyNotification = Rng.UFromExcept(emptyNotifications, lastEmptyNotification);
            return lastEmptyNotification;
        }

        private void OnTick(long ticks) {
            toRemove.Clear();
            foreach (var data in notifications.Values) {
                if (data.ExpireTick > 0 && ticks >= data.ExpireTick) {
                    data.Flags |= NotificationFlags.Removed;
                    toRemove.Add(data.Handle);
                } else if (data.Tile != null && !data.Tile.IsActive) {
                    data.Flags |= NotificationFlags.Removed;
                    toRemove.Add(data.Handle);
                } else if (data.Obj != null && data.Obj.Obj?.IsActive != true
                        && !data.Obj.IsStorable(out _)) {
                    data.Flags |= NotificationFlags.Removed;
                    toRemove.Add(data.Handle);
                } else if (data.Flags.HasFlag(NotificationFlags.Removed)) {
                    toRemove.Add(data.Handle);
                }
            }
            if (toRemove.Count > 0) {
                for (int i = toRemove.Count - 1; i >= 0; i--) {
                    NotificationHandle handle = toRemove[i];
                    if (notifications.TryGetValue(handle, out var data)) {
                        data.DisposeWidget();
                        notifications.Remove(handle);
                    }
                }
                UpdateCount(false);
                ClearAttentionIfNoAlerts();
                widget.Rebuild();
            }
        }

        // The red overlay points at an alert the player has not looked at yet. Once every alert
        // is gone it points at nothing, and over "Looks clear." it reads as a contradiction
        private void ClearAttentionIfNoAlerts() {
            foreach (var data in notifications.Values) {
                if (data.Type <= NotificationType.Alert) { return; }
            }
            widget.SetNeedAttention(false);
        }

        private bool OnRemove(NotificationHandle handle) {
            if (!notifications.TryGetValue(handle, out var data)) {
                return false;
            }
            if (data == null) { return false; }
            #if DEBUG_NOTIFICATIONS
            D.Warn("Removing notification handle: {0}", data.Title);
            #endif
            data.DisposeWidget();
            var removed = notifications.Remove(handle);
            UpdateCount(false);
            ClearAttentionIfNoAlerts();
            widget.Rebuild();
            return removed;
        }

        private NotificationData OnGet(NotificationHandle handle) {
            if (!notifications.TryGetValue(handle, out var data)) {
                return null;
            }
            return data;
        }

        private NotificationHandle OnAdd(NotificationData data) {
            #if DEBUG_NOTIFICATIONS
            D.Warn("Adding notification: {0}", data);
            #endif
            if (data.Handle.IsValid) {
                var not = notifications[data.Handle];
                if (not != null) {
                    notifications.Remove(data.Handle);
                }
                D.Err("Trying to add notification with already valid handle!");
            }
            data.CreatedTick = G.Ticks;
            var handle = new NotificationHandle(nextId++);
            data.Handle = handle;
            notifications[handle] = data;
            UpdateCount(true);
            PlayEventSound(data);
            widget.Rebuild();
            if (data.Type <= NotificationType.Alert) {
                widget.SetNeedAttention(true);
            }
            // The ring interrupts like the beep does, so muting a type silences both
            if (!NotificationMutes.IsMuted(data.Type)) {
                widget.RingAlert(data.Type,
                    (data.Flags & NotificationFlags.PulseOnly) == NotificationFlags.PulseOnly);
            }
            return handle;
        }

        private void PlayEventSound(NotificationData data) {
            bool isEmergency = (data.Flags & NotificationFlags.Emergency) == NotificationFlags.Emergency;
            if (isEmergency && (data.Flags & NotificationFlags.TriggersSiren) == NotificationFlags.TriggersSiren) {
                // Player-built Emergency Siren devices react to the hazard itself, not to the
                // UI alert sound below - they keep firing even if this notification type is muted.
                S.Sig.TriggerSirens.Send();
            }
            if (NotificationMutes.IsMuted(data.Type)) { return; }
            if (isEmergency) {
                if (GameTimer.realtimeSinceStartupAsDouble - lastAlertSound > 10f) {
                    UISounds.PlayEventEmergency();
                    lastAlertSound = GameTimer.realtimeSinceStartupAsDouble;
                }
            } else {
                if (GameTimer.realtimeSinceStartupAsDouble - lastNotifySound > 1f) {
                    UISounds.PlayEventWarning();
                    lastNotifySound = GameTimer.realtimeSinceStartupAsDouble;
                }
            }
            VoiceAlerts.Say(data.VoiceLine);
        }

        private void UpdateCount(bool punch) {
            var cnt = notifications.Count;
            if (cnt == lastCnt) { return; }
            lastCnt = cnt;
            widget.SetCount(cnt, CountTextFor(cnt));
            if (punch) {
                widget.PunchCount();
            }
            if (cnt == 0) {
                widget.SetAlertIcon(IconId.CCheck, punch);
            } else {
                UpdateAlertIcon(punch);
            }
        }

        /// <summary>
        /// Sets the alert icon based on the highest priority notification present.
        /// </summary>
        private void UpdateAlertIcon(bool punch) {
            string icon = null;
            int mostImportant = int.MaxValue;
            long newestTime = 0L;
            foreach (var not in notifications.Values) {
                var type = (int) not.Type;
                if (type < mostImportant) {
                    mostImportant = type;
                    icon = not.Icon;
                    newestTime = not.CreatedTick;
                    // found first critical one
                } else if (type == mostImportant) {
                    var time = not.CreatedTick;
                    if (time > newestTime) {
                        newestTime = time;
                        icon = not.Icon;
                    }
                }
            }
            if (icon == null) {
                switch ((NotificationType) mostImportant) {
                    case NotificationType.Immediate:
                        icon = IconId.CBell;
                        break;
                    case NotificationType.Critical:
                        icon = IconId.CSiren;
                        break;
                    case NotificationType.Alert:
                        icon = IconId.CWarning;
                        break;
                    case NotificationType.Warning:
                        icon = IconId.CWarning;
                        break;
                    case NotificationType.Info:
                        icon = IconId.CInfo;
                        break;
                    default:
                        icon = IconId.CQuestion;
                        break;
                }
            }
            widget.SetAlertIcon(icon, punch);
        }
        private string SummaryTip() {
            if (notifications.Count == 0) { return null; }
            var sb = Caches.StringBuilder3;
            sb.Clear();

            GroupNotifications();
            for (int i = 0; i < 5; i++) {
                AddGroupTip(sb, (NotificationType) i, items[i]);
            }

            return sb.ToString();
        }

        private void AddGroupTip(StringBuilder sb, NotificationType type, List<NotificationView> items) {
            if (items.Count == 0) { return; }
            if (sb.Length > 0) {
                sb.AppendLine();
            }
            if (type == NotificationType.Immediate) {
                sb.Append(Texts.SubTitle(Texts.Accented(T.ActionRequired, false)));
            } else {
                sb.Append(Texts.SubTitle(TitleFor(type)));
                if (NotificationMutes.IsMuted(type)) {
                    sb.Append(' ').Append(Texts.Italic(T.NotifyMuted));
                }
            }

            foreach (var item in items) {
                if (item.List != null) {
                    sb.AppendFormat("{0} {1} x{2}\n", GlyphMap.ArrowRight, item.Title, item.List.Count);
                } else {
                    sb.AppendFormat("{0} {1}\n", GlyphMap.ArrowRight, item.Title);
                }
            }
        }

        private string CountTextFor(int count) {
            switch (count) {
                case 0: return GetEmptyNotification();
                case 1: return T.IssuesNeedAttentionOne;
                case 2: return T.IssuesNeedAttentionTwo;
                case 3:
                case 4: return T.IssuesNeedAttentionFew;
                default: return T.IssuesNeedAttentionMany;
            }
        }

        public override void Unload() {
            The.SysSig?.ResolutionChanged.RemoveListener(OnResolutionChanged);
            notifications.Clear();
            for (int i = 0; i < 5; i++) {
                items[i]?.Clear();
            }
            if (widget != null) {
                widget.Cleanup();
            }
        }

        public string GetName() {
            return "NotifySys";
        }
    }
}
