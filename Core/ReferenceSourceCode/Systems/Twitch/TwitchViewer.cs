//#define DEBUG_TWITCH
using System;
using System.Collections.Generic;
using System.Text;
using Game.Commands;
using Game.Components;
using Game.Constants;
using KL.Collections;
using KL.Utils;
using Game.Utils;
using Game.Data;
using MessagePack;
using Game.CodeGen;
using Game.Systems.Twitch.Commands;
using Game.Input;
using Game.Systems.UIUX;
using UnityEngine;
using System.Runtime.Serialization;

namespace Game.Systems.Twitch {
    [MessagePackObject]
    public class TwitchViewer {
        private const int MaxCommandsPerTimeLimit = 20;
        private static TimeSpan TimeLimit = new TimeSpan(0, 5, 0);
        [Key("Reputation")] public float Reputation;
        [IgnoreMember] public string ReputationText => Units.Num(Reputation);
        [Key("TotalReputation")] public float TotalReputation;
        [Key("Id")] public int Id;
        [Key("Name")] public string Name;
        [Key("Flags")] public TwitchViewerFlags Flags;
        [Key("LastSeen")] public DateTime LastSeen;
        [Key("RoleIds")] public List<int> Roles;
        [Key("CommandCounts")] public Dictionary<int, int> CommandCounts;
        [IgnoreMember] public string Icon;
        [IgnoreMember] public int CurrentGameIdH;
        [IgnoreMember] public bool IsViewing;
        [IgnoreMember] public readonly List<TwitchCommand> LastCommands = new();
        private BFloat bReputation;
        [IgnoreMember] public BFloat BReputation => bReputation ??= new BFloat();

        private BCallback bViewerChanged;
        [IgnoreMember] public BCallback BViewerChanged => bViewerChanged ??= new BCallback();

        public void NotifyViewerChanged() => bViewerChanged?.Invoke();

        [IgnoreMember] public bool IsBroadcaster => (Flags & TwitchViewerFlags.Broadcaster) == TwitchViewerFlags.Broadcaster;
        [IgnoreMember] public bool IsVIP => (Flags & TwitchViewerFlags.VIP) == TwitchViewerFlags.VIP;
        [IgnoreMember] public bool IsMod => (Flags & TwitchViewerFlags.Moderator) == TwitchViewerFlags.Moderator;
        [IgnoreMember] public bool isSubscriber => (Flags & TwitchViewerFlags.Subscriber) == TwitchViewerFlags.Subscriber;
        private string searchName;
        [IgnoreMember] public string SearchName {
            get {
                if (searchName == null) {
                    searchName = Strings.ToSearchString(Name);
                }
                return searchName;
            }
        }

        public void EnqueueCommand(GameState S, long tick, TwitchCommand command) {
            #if DEBUG_TWITCH
            D.Log("Adding cmd to {0}: {1}", Name, command);
            #endif
            if (command == null) { return; }
            #if DEBUG_TWITCH
            if (lastCommandTick == tick) {
                D.Warn("More than one command per tick for twitch viewer {0}!",
                    Name);
            }
            #endif
            CleanupOldCommands();
            if (LastCommands.Count > MaxCommandsPerTimeLimit) {
                S.Sys.Twitch.Client.SendChannelMessage(
                    "{0}: you are sending too many commands. Please chill. Limit: {1} commands per {2} minutes",
                        Name, MaxCommandsPerTimeLimit, TimeLimit.TotalMinutes);
                D.Warn("Command throttled for {0} -> {1} commands in last {2} ticks!",
                    Name, LastCommands.Count, TimeLimit);
                return;
            }
            CommandCounts ??= new();
            if (!CommandCounts.TryGetValue(command.CmdH, out var cnt)) {
                cnt = 0;
            }
            CommandCounts[command.IdH] = cnt + 1;
            LastCommands.Add(command);
            S.CmdQ.Enqueue(command);
        }

        public void SetReputation(float amount) {
            Reputation = amount;
            bReputation?.Set(Reputation);
        }

        public void AddReputation(float amount) {
            if (amount > 0) {
                TotalReputation += amount;
            }
            Reputation += amount;
            bReputation?.Set(Reputation);
        }

        public void AddRole(int role) {
            Roles ??= new();
            Roles.AddUniqueInefficient(role);
        }
        public void RemoveRole(int role) {
            Roles?.Remove(role);
        }
        public bool HasRole(int roleIdH) {
            return Roles?.Contains(roleIdH) == true;
        }

        private void CleanupOldCommands() {
            if (LastCommands.Count < MaxCommandsPerTimeLimit) {
                return;
            }
            var threshold = DateTime.UtcNow - TimeLimit;
            for (int i = LastCommands.Count - 1; i > 0; i--) {
                if (LastCommands.Count < MaxCommandsPerTimeLimit) {
                    break;
                }
                var cmd = LastCommands[i];
                if (cmd.Time < threshold) {
                    LastCommands.RemoveAt(i);
                }
            }
        }

        public void Cleanup() {
            LastCommands.Clear();
        }

        public void AppendInfo(StringBuilder sb) {
            // sb.AppendFormat("Flags: {0}, ", Flags);
            if (Roles != null) {
                sb.Append("Roles: ");
                for (int i = 0; i < Roles.Count; i++) {
                    int r = Roles[i];
                    sb.Append(TwitchRoleIdH.ResolveName(r));
                    sb.Append(", ");
                }
            }
            sb.AppendFormat("Reputation: {0}", ReputationText);
        }

        public bool IsActiveWithTimeout(DateTime minActiveDate) {
            D.Warn("last interaction is at {0}. Cutoff (-5 min is) {1}",
                LastSeen, minActiveDate);
            return IsViewing || LastSeen > minActiveDate;
        }

        public void FlagsChanged() {
            if ((Flags & TwitchViewerFlags.Broadcaster) == TwitchViewerFlags.Broadcaster) {
                Icon = IconId.CShipComputerBets;
            } else if ((Flags & TwitchViewerFlags.Moderator) == TwitchViewerFlags.Moderator) {
                Icon = IconId.CShield;
            } else if ((Flags & TwitchViewerFlags.Subscriber) == TwitchViewerFlags.Subscriber) {
                Icon = IconId.CUpgrade;
            } else if ((Flags & TwitchViewerFlags.VIP) == TwitchViewerFlags.VIP) {
                Icon = IconId.CBookmark;
            } else {
                Icon = IconId.CBinoculars;
            }
        }

        public void MarkSeen(TwitchSys sys) {
            LastSeen = DateTime.UtcNow;
            if (!IsViewing) {
                IsViewing = true;
                sys.ApplyRoleChanges(this);
            }
            bViewerChanged?.Invoke();
        }

        public void AfterLoad() {
            FlagsChanged();
        }

        public string FullTooltip(TwitchSys sys) {
            var b = sys.BeingOf(this);
            string beingTip = null;
            if (b != null) {
                beingTip = b.UI.GetFullBeingTooltip();
            }
            var sb = Caches.StringBuilder2;
            sb.Clear();
            sb.Append(Texts.Accented(Name));
            sb.AppendFormat("\n * Reputation: {0}", ReputationText);
            sb.AppendFormat("\n * Flags: {0}", Flags);
            if (b != null) {
                sb.AppendFormat("\n * Controlled Being: {0}", b.Name);
                sb.Append(beingTip);
            }
            return sb.ToString();
        }
    }
}
