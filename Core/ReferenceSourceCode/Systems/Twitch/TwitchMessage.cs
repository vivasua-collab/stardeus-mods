using KL.Utils;

namespace Game.Systems.Twitch {
    public class TwitchMessage {
        private const string PRIVMSG = "PRIVMSG";
        private const string WHISPER = "WHISPER";
        private const string displayNameStr = ";display-name=";
        private const string badgestStr = ";badges=";
        private const string idStr = ";id=";
        /*
        // broadcaster example:
        @badge-info=subscriber/55;badges=broadcaster/1,subscriber/0;client-nonce=afd5d11c1942d205bbf169c8366ad69e;color=#8A2BE2;display-name=dev_spajus;emotes=;first-msg=0;flags=;id=155975d4-d600-461f-92b1-688e3bc8f2b3;mod=0;returning-chatter=0;room-id=65806828;subscriber=1;tmi-sent-ts=1720771906909;turbo=0;user-id=65806828;user-type= :dev_spajus!dev_spajus@dev_spajus.tmi.twitch.tv PRIVMSG #dev_spajus :!sdhelp
        // vip example:
        @badge-info=founder/1;badges=vip/1,founder/0;client-nonce=HEX_NUMBER;color=#DC52FF;display-name=NICKNAME;emotes=;flags=;id=GUID;mod=0;room-id=NUMBER;subscriber=0;tmi-sent-ts=1599660315036;turbo=0;user-id=NUMBER;user-type= :NICKNAME!NICKNAME@NICKNAME.tmi.twitch.tv PRIVMSG #CHANNEL :!join
        // mod example:
        @badge-info=;badges=moderator/1;client-nonce=HEX_NUMBER;color=;display-name=bot_spajus;emotes=;flags=;id=GUID;mod=1;room-id=NUMBER;subscriber=0;tmi-sent-ts=1599660237377;turbo=0;user-id=NUMBER;user-type=mod :NICKNAME!NICKNAME@NICKNAME.tmi.twitch.tv PRIVMSG #CHANNEL :test
        // regular user example:
        @badge-info=;badges=;client-nonce=HEX_NUMBER;color=;display-name=NICKNAME;emotes=;flags=;id=GUID;mod=0;room-id=NUMBER;subscriber=0;tmi-sent-ts=1599633882371;turbo=0;user-id=NUMBER;user-type= :NICKNAME!NICKNAME@NICKNAME.tmi.twitch.tv PRIVMSG #CHANNEL :How are things going?
        // whisper example:
        @badges=;color=;display-name=NICKNAME;emotes=;message-id=2;thread-id=NUMBER_NUMBER;turbo=0;user-id=NUMBER;user-type= :NICKNAME!NICKNAME@NICKNAME.tmi.twitch.tv WHISPER BOTNAME :test
        */
        private string messageRaw;
        public TwitchMessage(string raw) {
            messageRaw = raw;
            message = ParseMessage();
        }

        private string username;
        public string Username {
            get {
                if (username == null) {
                    username = Strings.CutPart(messageRaw, displayNameStr, ";", true);
                }
                return username;
            }
        }

        private string messageId;
        public string MessageId {
            get {
                if (messageId == null) {
                    messageId = Strings.CutPart(messageRaw, idStr, ";", true);
                }
                return messageId;
            }
        }

        private string command;
        public string Command {
            get {
                if (command == null) {
                    command = Message.Split(' ')[0];
                    if (command != null) {
                        command = command.ToLower();
                    }
                    args = command == message ? null :
                        message.Replace($"{command} ", "").Trim();
                }
                return command;
            }
        }
        private string args;
        public string Args => args;

        private string message;
        public string Message => message;
        public bool IsCommand => (IsChannelMessage || IsWhisper) && Message[0] == '!';
        public bool IsChannelMessage;
        public bool IsWhisper;

        public bool ParseViewerInfo(TwitchViewer viewer) {
            if (IsWhisper) { return false; }
            var badgesMsgPart = Strings.CutPart(messageRaw, badgestStr, ";", true);
            var badges = badgesMsgPart.Split(',');
            viewer.Flags = TwitchViewerFlags.Viewer;
            if (badges == null) {
                return true;
            }
            foreach (var badge in badges) {
                var b = badge.Split('/')[0];
                switch (b) {
                    case "broadcaster": {
                        viewer.Flags |= TwitchViewerFlags.Broadcaster;
                        break;
                    }
                    case "vip":
                    case "founder": {
                        viewer.Flags |= TwitchViewerFlags.VIP;
                        break;
                    }
                    case "moderator": {
                        viewer.Flags |= TwitchViewerFlags.Moderator;
                        break;
                    }
                    case "subscriber": {
                        viewer.Flags |= TwitchViewerFlags.Subscriber;
                        break;
                    }
                }
            }
            viewer.FlagsChanged();
            return true;
        }

        private string ParseMessage() {
            var msgStartIdx = messageRaw.IndexOf(PRIVMSG);
            if (msgStartIdx == -1) {
                // It's not a privmsg!
                msgStartIdx = messageRaw.IndexOf(WHISPER);
                if (msgStartIdx == -1) {
                    // We don't care about system messages
                    return null;
                } else {
                    IsWhisper = true;
                }
            } else {
                IsChannelMessage = true;
            }

            var indexOfMsg = msgStartIdx +
                (IsChannelMessage ? PRIVMSG.Length : WHISPER.Length);

            var msg = messageRaw.Substring(indexOfMsg);

            int indexOfColon = msg.IndexOf(':') + 1;
            msg = msg.Substring(indexOfColon).Trim();
            return msg;
        }



        public override string ToString() {
            return $"TwitchMessage[{Username}|{Command}|{Args}]";
        }
    }
}
