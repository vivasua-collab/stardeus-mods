using System;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Notify {
    public sealed class NotificationData  {
        public NotificationHandle Handle;
        public INotificationUIProvider UIProvider;
        public NotificationType Type;
        private WBase widget;
        private Lifetime widgetLife;
        public string GroupId;
        public string GroupIcon;
        public string GroupTitle;
        public NotificationFlags Flags;
        public BeingData Being;
        public ObjData Obj;
        public Tile Tile;
        public long CreatedTick;
        public long ExpireTick;
        public string Icon;
        public Sprite Sprite;
        public Color IconTint;
        public string Title;
        public string VoiceLine;
        public string Text2;
        public B<string> TitleB;
        public B<string> Text2B;
        public string Tooltip;
        public Func<string> TooltipFunc;
        /// <summary>
        /// We don't want to show a notification for 2 seconds and immediately
        /// remove it if it's not relevant anymore. There will be minimum
        /// display time, that's why we track first display time so we
        /// can check for how long it was displayed;
        /// </summary>
        public double FirstDisplayTime;
        public Action OnClick;
        public Action<bool> OnHover;

        public void SetWidget(Lifetime life, WBase w) {
            widget = w;
            widgetLife = life;
        }

        public void DisposeWidget() {
            if (widget != null && !widgetLife.IsDisposed) {
                widget.Dispose();
            }
            widget = null;
            widgetLife = null;
        }

        public NotificationHandle Show() {
            if (!Handle.IsValid) {
                A.S.Query.AddNotification.Ask(this);
            }
            return Handle;
        }

        public NotificationData WithOnClick(Action onClick) {
            OnClick = onClick;
            return this;
        }
        public NotificationData WithOnHover(Action<bool> onHover) {
            if (OnHover != null) {
                D.Err("OnHover is already set for this notification, was WithArea called?");
                return this;
            }
            OnHover = onHover;
            return this;
        }

        public NotificationData Sticky() {
            Flags |= NotificationFlags.Sticky;
            return this;
        }

        // For a notification whose own UI is already in front of the player when it is added
        // (an open trade panel, a hail popup): the hub only pulses, never rings from across
        // the screen
        public NotificationData PulseOnly() {
            Flags |= NotificationFlags.PulseOnly;
            return this;
        }

        public NotificationData Emergency(bool triggerSiren = false) {
            Flags |= NotificationFlags.Emergency;
            if (triggerSiren) {
                Flags |= NotificationFlags.TriggersSiren;
            }
            return this;
        }

        public NotificationData WithVoice(string voiceLine) {
            VoiceLine = voiceLine;
            return this;
        }

        public NotificationData Group(string groupId, string title = null, string icon = null) {
            GroupId = groupId;
            GroupIcon = icon;
            GroupTitle = title;
            return this;
        }

        public NotificationData WithTooltip(string tip) {
            Tooltip = tip;
            return this;
        }
        public NotificationData WithTooltipFunc(Func<string> tipFunc) {
            TooltipFunc = tipFunc;
            return this;
        }

        public NotificationData WithTitleB(B<string> titleB) {
            TitleB = titleB;
            return this;
        }

        public NotificationData WithText2B(B<string> text2B) {
            Text2B = text2B;
            return this;
        }
        public NotificationData WithUIProvider(INotificationUIProvider provider) {
            UIProvider = provider;
            return this;
        }
        public NotificationData WithPosIdx(int posIdx) {
            return WithArea(EntityUtils.CenterOf(posIdx), 1f);

        }
        public NotificationData WithArea(Vector2 position, float radius = 1f) {
            return WithArea(position, new Vector2(radius, radius), false);
        }
        public NotificationData WithArea(Vector2 position, Vector2 size, bool overwriteOnHover = false) {
            if (!overwriteOnHover && OnHover != null) {
                D.Err("OnHover is already set for this notification, can't set area! {0}", OnHover);
                return this;
            }
            OnHover = (h) => {
                if (h) {
                   A.S.Sig.CameraShowPosLink.Send(
                    null, position, size, CameraFocus.FlashColor());
                } else {
                    EntityUtils.HideEntityLink();
                }
            };
            return this;
        }

        public void Remove(bool instant) {
            Flags |= NotificationFlags.Removed;
            if (instant) {
                Notification.Remove(ref Handle);
            }
        }

        public override string ToString() {
            return $"Notification [{Handle}|{Title}]";
        }

    }
}
