using Game.CodeGen;
using Game.Constants;

namespace Game.Systems.Notify {
    // Per-save alert-sound muting for notification hub blocks. See
    // Docs/Architecture/Systems/NotificationHub.md for why this only affects sound and why
    // NotificationType.Immediate is excluded.
    public static class NotificationMutes {
        public static bool CanMute(NotificationType type) {
            return type != NotificationType.Immediate;
        }

        public static bool IsMuted(NotificationType type) {
            switch (type) {
                case NotificationType.Critical:
                    return A.S.Prefs.GetBool(Pref.MuteNotifyCritical, PrefH.MuteNotifyCritical, false);
                case NotificationType.Alert:
                    return A.S.Prefs.GetBool(Pref.MuteNotifyAlert, PrefH.MuteNotifyAlert, false);
                case NotificationType.Warning:
                    return A.S.Prefs.GetBool(Pref.MuteNotifyWarning, PrefH.MuteNotifyWarning, false);
                case NotificationType.Info:
                    return A.S.Prefs.GetBool(Pref.MuteNotifyInfo, PrefH.MuteNotifyInfo, false);
                default:
                    return false;
            }
        }

        public static void SetMuted(NotificationType type, bool muted) {
            switch (type) {
                case NotificationType.Critical:
                    A.S.Prefs.SetBool(Pref.MuteNotifyCritical, muted);
                    break;
                case NotificationType.Alert:
                    A.S.Prefs.SetBool(Pref.MuteNotifyAlert, muted);
                    break;
                case NotificationType.Warning:
                    A.S.Prefs.SetBool(Pref.MuteNotifyWarning, muted);
                    break;
                case NotificationType.Info:
                    A.S.Prefs.SetBool(Pref.MuteNotifyInfo, muted);
                    break;
            }
        }
    }
}
