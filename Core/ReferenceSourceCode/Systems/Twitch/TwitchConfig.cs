using MessagePack;

namespace Game.Systems.Twitch {
    [MessagePackObject]
    public struct TwitchConfig {
        [Key("IsActive")] public bool IsActive;
        // The Twitch Chat OAuth key (see https://twitchapps.com/tmi/)
        [Key("OAuth")] public string OAuth;
        // The username of above's account
        [Key("BotName")] public string BotName;
        // The channel to listen from
        [Key("Channel")] public string ChannelName;

        [IgnoreMember] public bool IsValid {
            get {
                if (string.IsNullOrWhiteSpace(OAuth)) {
                    return false;
                }
                if (string.IsNullOrWhiteSpace(ChannelName)) {
                    return false;
                }
                if (string.IsNullOrWhiteSpace(BotName)) {
                    return false;
                }
                if (!OAuth.StartsWith("oauth:")) {
                    return false;
                }
                return true;
            }
        }
    }
}
