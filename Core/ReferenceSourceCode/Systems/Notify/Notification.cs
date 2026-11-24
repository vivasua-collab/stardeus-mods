using System;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;

namespace Game.Systems.Notify {
    public static class Notification {
        public static NotificationData Build(string icon, string text, int durationTicks,
                NotificationType type = NotificationType.Warning) {
            var exp = durationTicks > 0L ? G.Ticks + durationTicks : 0L;
            var data = new NotificationData {
                Icon = icon,
                Title = text,
                ExpireTick = exp,
                Type = type,
            };
            return data;
        }
        public static NotificationData BuildFor(Entity entity, string icon, string text, int durationTicks,
                NotificationType type = NotificationType.Warning) {
            var data = Build(icon, text, durationTicks, type);
            if (entity is Being b) {
                data.Being = b.Data;
                if (icon == null) {
                    b.Data.Avatar.Load((s) => data.Sprite = s);
                }
            } else if (entity is Obj o) {
                data.Obj = o.Data;
                if (icon == null) {
                    data.Sprite = o.Definition.PreviewSprite;
                }
            } else if (entity is Tile t) {
                data.Tile = t;
                if (icon == null) {
                    data.Sprite = t.Definition.PreviewSprite;
                }
            }
            return data;
        }
        public static NotificationData BuildFor(BeingData being, string icon, string text, int durationTicks,
                NotificationType type = NotificationType.Warning) {
            var data = Build(icon, text, durationTicks, type);
            data.Being = being;
            data.OnHover = (h) => {
                if (h && being.Being != null) {
                    EntityUtils.ShowEntityLink(null, being.Being, h);
                } else {
                    EntityUtils.HideEntityLink();
                }
            };
            if (icon == null) {
                being.Avatar.Load((s) => data.Sprite = s);
            }
            return data;
        }
        public static NotificationData BuildFor(Obj obj, string icon, string text, int durationTicks,
                NotificationType type = NotificationType.Warning) {
            var data = Build(icon, text, durationTicks, type);
            data.Obj = obj.Data;
            data.OnHover = (h) => EntityUtils.ShowEntityLink(null, obj, h);
            if (icon == null) {
                data.Sprite = obj.Definition.PreviewSprite;
            }
            return data;
        }
        public static NotificationData BuildFor(Tile tile, string icon, string text, int durationTicks,
                NotificationType type = NotificationType.Warning) {
            var data = Build(icon, text, durationTicks, type);
            data.Tile = tile;
            data.OnHover = (h) => EntityUtils.ShowEntityLink(null, tile, h);
            if (icon == null) {
                data.Sprite = tile.Definition.PreviewSprite;
            }
            return data;
        }
        public static NotificationHandle Show(string icon, string text, int durationTicks,
                NotificationType type = NotificationType.Warning) {
            var data = Build(icon, text, durationTicks, type);
            return A.S.Query.AddNotification.Ask(data);
        }
        public static NotificationHandle Show(string icon, string text, string tip,
                int durationTicks,
                NotificationType type = NotificationType.Warning) {
            var data = Build(icon, text, durationTicks, type);
            data.Tooltip = tip;
            return A.S.Query.AddNotification.Ask(data);
        }

        public static void Remove(ref NotificationHandle h) {
            if (h.IsValid) {
                A.S.Query.RemoveNotification.Ask(h);
                h = default;
            }
        }
    }
}
