using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public class LogSysData {
        [Key("Entries")] public LogEntry[] Entries;
        [Key("LastLine")] public string LastLine;
        [Key("GameLog")] public string GameLog;
    }
}
