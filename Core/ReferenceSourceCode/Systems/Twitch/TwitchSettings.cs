using System.Collections.Generic;
using Game.Utils;
using MessagePack;

namespace Game.Systems.Twitch {
    [MessagePackObject]
    public class TwitchSettings {
        public enum ViewerQueueMode {
            Random,
            Queue,
        }
        [Key("Roles")] public Dictionary<int, TwitchRoleData> Roles;
        [Key("QueueMode")] public ViewerQueueMode QueueMode;
        /// <summary>
        /// Exclude viewers who aren't in the channel right now
        /// </summary>
        [Key("QueueSkipInactive")] public bool QueueSkipInactive;
        /// <summary>
        /// Will subscribers be prioritized over non-subscribers when processing the queue?
        /// </summary>
        [Key("SubPriority")] public bool SubQueuePriority;
        [Key("AnnounceConnect")] public bool AnnounceConnect;
        [Key("WelcomeMsg")] public string WelcomeMsg;
        [Key("CmdPrefix")] public string CommandPrefix;
        [Key("RepPerHour")] public int RepGainPerHour;
        [Key("DisabledCommands")] public HashSet<int> DisabledCmds;
        public bool IsCommandEnabled(int cmdIdH) {
            if (DisabledCmds == null) {
                return true;
            }
            if (DisabledCmds.Contains(cmdIdH)) {
                return false;
            }
            return true;
        }
        public void SetCommandEnabled(int cmdIdH, bool on) {
            if (on) {
                DisabledCmds.Remove(cmdIdH);
                if (DisabledCmds.Count == 0) {
                    DisabledCmds = null;
                }
            } else {
                DisabledCmds ??= new();
                DisabledCmds.Add(cmdIdH);
            }
        }
        public string AddPrefix(string cmd, bool forUI) {
            if (cmd[0] == '!') {
                return cmd;
            }
            if (forUI) {
                return $"{CommandPrefix}{Texts.Accented(cmd, false)}";
            } else {
                return $"{CommandPrefix}{cmd}";
            }
        }

        public string StripPrefix(string cmd) {
            if (cmd[0] != '!') {
                return cmd;
            }
            var lenB4 = cmd.Length;
            var newCmd = cmd.Replace(CommandPrefix, string.Empty);
            if (newCmd.Length == lenB4) {
                // prefix not found return the wrong command
                return cmd;
            }
            return newCmd;
        }

        public static TwitchSettings Default {
            get {
                return new TwitchSettings() {
                    Roles = TwitchRole.Defaults,
                    QueueMode = ViewerQueueMode.Random,
                    RepGainPerHour = 60,
                    SubQueuePriority = false,
                    QueueSkipInactive = false,
                    AnnounceConnect = true,
                    CommandPrefix = "!",
                };
            }
        }
    }
}
